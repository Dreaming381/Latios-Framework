using System.Collections.Generic;
using Latios.Authoring;
using Latios.Authoring.Systems;
using Latios.Transforms;
using Latios.Unsafe;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Psyshock.Authoring
{
    public static class TriMeshColliderSmartBlobberAPIExtensions
    {
        /// <summary>
        /// Requests the creation of a BlobAssetReference<TriMeshColliderBlob> that is a triMesh hull of the passed in mesh
        /// </summary>
        public static SmartBlobberHandle<TriMeshColliderBlob> RequestCreateTriMeshBlobAsset(this IBaker baker, Mesh mesh)
        {
            return baker.RequestCreateBlobAsset<TriMeshColliderBlob, TriMeshColliderBakeData>(new TriMeshColliderBakeData { sharedMesh = mesh });
        }
    }

    public struct TriMeshColliderBakeData : ISmartBlobberRequestFilter<TriMeshColliderBlob>
    {
        public Mesh sharedMesh;

        public bool Filter(IBaker baker, Entity blobBakingEntity)
        {
            if (sharedMesh == null)
                return false;

            baker.DependsOn(sharedMesh);
            // Todo: Is this necessary since baking is Editor-only?
            //if (!sharedMesh.isReadable)
            //    Debug.LogError($"Psyshock failed to convert triMesh mesh {sharedMesh.name}. The mesh was not marked as readable. Please correct this in the mesh asset's import settings.");

            baker.AddComponent(blobBakingEntity, new TriMeshColliderBlobBakeData { mesh = sharedMesh });
            return true;
        }
    }

    [TemporaryBakingType]
    internal struct TriMeshColliderBlobBakeData : IComponentData
    {
        public UnityObjectRef<Mesh> mesh;
    }
}

namespace Latios.Psyshock.Authoring.Systems
{
    [RequireMatchingQueriesForUpdate]
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    [UpdateInGroup(typeof(SmartBlobberBakingGroup))]
    public unsafe sealed partial class TriMeshColliderSmartBlobberSystem : SystemBase
    {
        EntityQuery m_query;
        List<Mesh>  m_meshCache;

        struct UniqueItem
        {
            public TriMeshColliderBlobBakeData             bakeData;
            public BlobAssetReference<TriMeshColliderBlob> blob;
        }

        protected override void OnCreate()
        {
            new SmartBlobberTools<TriMeshColliderBlob>().Register(World);
        }

        protected override void OnUpdate()
        {
            int count = m_query.CalculateEntityCountWithoutFiltering();

            var hashmap   = new NativeParallelHashMap<int, UniqueItem>(count * 2, Allocator.TempJob);
            var mapWriter = hashmap.AsParallelWriter();

            Entities.WithEntityQueryOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities).ForEach((in TriMeshColliderBlobBakeData data) =>
            {
                mapWriter.TryAdd(data.mesh.GetHashCode(), new UniqueItem { bakeData = data });
            }).WithStoreEntityQueryInField(ref m_query).ScheduleParallel();

            var meshes   = new NativeList<UnityObjectRef<Mesh> >(Allocator.TempJob);
            var builders = new NativeList<TriMeshBuilder>(Allocator.TempJob);

            Job.WithCode(() =>
            {
                int count = hashmap.Count();
                if (count == 0)
                    return;

                meshes.ResizeUninitialized(count);
                builders.ResizeUninitialized(count);

                int i = 0;
                foreach (var pair in hashmap)
                {
                    meshes[i]   = pair.Value.bakeData.mesh;
                    builders[i] = default;
                    i++;
                }
            }).Schedule();

            if (m_meshCache == null)
                m_meshCache = new List<Mesh>();
            m_meshCache.Clear();
            CompleteDependency();

            for (int i = 0; i < meshes.Length; i++)
            {
                var mesh         = meshes[i].Value;
                var builder      = builders[i];
                builder.meshName = mesh.name ?? default;
                builders[i]      = builder;
                m_meshCache.Add(mesh);
            }

            #if UNITY_EDITOR
            var meshDataArray = UnityEditor.MeshUtility.AcquireReadOnlyMeshData(m_meshCache);
            #else
            var meshDataArray = Mesh.AcquireReadOnlyMeshData(m_meshCache);
            #endif

            // var meshDataArray = Mesh.AcquireReadOnlyMeshData(m_meshCache);

            Dependency = new BuildBlobsJob
            {
                builders = builders.AsArray(),
                meshes   = meshDataArray
            }.ScheduleParallel(builders.Length, 1, Dependency);

            // Todo: Defer this to a later system?
            CompleteDependency();
            meshDataArray.Dispose();

            Job.WithCode(() =>
            {
                for (int i = 0; i < meshes.Length; i++)
                {
                    var element                      = hashmap[meshes[i].GetHashCode()];
                    element.blob                     = builders[i].result;
                    hashmap[meshes[i].GetHashCode()] = element;
                }
            }).Schedule();

            Entities.WithReadOnly(hashmap).ForEach((ref SmartBlobberResult result, in TriMeshColliderBlobBakeData data) =>
            {
                result.blob = UnsafeUntypedBlobAssetReference.Create(hashmap[data.mesh.GetHashCode()].blob);
            }).ScheduleParallel();

            Dependency = hashmap.Dispose(Dependency);
            Dependency = meshes.Dispose(Dependency);
            Dependency = builders.Dispose(Dependency);
        }

        struct TriMeshBuilder
        {
            public FixedString128Bytes                     meshName;
            public BlobAssetReference<TriMeshColliderBlob> result;

            public unsafe void BuildBlob(Mesh.MeshData mesh)
            {
                var vector3Cache = new NativeList<Vector3>(Allocator.Temp);
                var indicesCache = new NativeList<int>(Allocator.Temp);
                var bodies       = new NativeList<ColliderBody>(Allocator.Temp);

                var builder = new BlobBuilder(Allocator.Temp);

                ref var blobRoot = ref builder.ConstructRoot<TriMeshColliderBlob>();

                blobRoot.meshName = meshName;

                vector3Cache.ResizeUninitialized(mesh.vertexCount);
                mesh.GetVertices(vector3Cache.AsArray());

                for (int i = 0; i < mesh.subMeshCount; i++)
                {
                    var descriptor = mesh.GetSubMesh(i);
                    if (descriptor.topology != MeshTopology.Triangles)
                        continue;
                    indicesCache.ResizeUninitialized(descriptor.indexCount);
                    mesh.GetIndices(indicesCache.AsArray(), i);

                    for (int j = 0; j < indicesCache.Length; j += 3)
                    {
                        float3 a = vector3Cache[indicesCache[j]];
                        float3 b = vector3Cache[indicesCache[j + 1]];
                        float3 c = vector3Cache[indicesCache[j + 2]];

                        bodies.Add(new ColliderBody { collider = new TriangleCollider(a, b, c), entity = default, transform = TransformQvvs.identity });
                    }
                }

                Physics.BuildCollisionLayer(bodies.AsArray()).WithSubdivisions(1).RunImmediate(out var layer, Allocator.Temp);

                builder.ConstructFromNativeArray(ref blobRoot.xmins,         layer.xmins.AsArray());
                builder.ConstructFromNativeArray(ref blobRoot.xmaxs,         layer.xmaxs.AsArray());
                builder.ConstructFromNativeArray(ref blobRoot.yzminmaxs,     layer.yzminmaxs.AsArray());
                builder.ConstructFromNativeArray(ref blobRoot.intervalTree,  layer.intervalTrees.AsArray());
                builder.ConstructFromNativeArray(ref blobRoot.sourceIndices, layer.srcIndices.AsArray());

                var triangles = builder.Allocate(ref blobRoot.triangles, layer.count);
                var aabb      = new Aabb(float.MaxValue, float.MinValue);
                for (int i = 0; i < layer.count; i++)
                {
                    TriangleCollider triangle = layer.colliderBodies[i].collider;
                    aabb.min                  = math.min(math.min(aabb.min, triangle.pointA), math.min(triangle.pointB, triangle.pointC));
                    aabb.max                  = math.max(math.max(aabb.max, triangle.pointA), math.max(triangle.pointB, triangle.pointC));
                    triangles[i]              = triangle;
                }

                blobRoot.localAabb = aabb;

                result = builder.CreateBlobAssetReference<TriMeshColliderBlob>(Allocator.Persistent);
            }
        }

        [BurstCompile]
        struct BuildBlobsJob : IJobFor
        {
            public NativeArray<TriMeshBuilder>   builders;
            [ReadOnly] public Mesh.MeshDataArray meshes;

            public void Execute(int i)
            {
                var builder = builders[i];
                builder.BuildBlob(meshes[i]);
                builders[i] = builder;
            }
        }
    }
}

