using System;
using System.Collections.Generic;
using Latios.Authoring;
using Latios.Authoring.Systems;
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
    public static class ConvexColliderSmartBlobberAPIExtensions
    {
        /// <summary>
        /// Requests the creation of a BlobAssetReference<ConvexColliderBlob> that is a convex hull of the passed in mesh
        /// </summary>
        public static SmartBlobberHandle<ConvexColliderBlob> RequestCreateConvexBlobAsset(this IBaker baker, Mesh mesh)
        {
            return baker.RequestCreateBlobAsset<ConvexColliderBlob, ConvexColliderBakeData>(new ConvexColliderBakeData { sharedMesh = mesh });
        }
    }

    public struct ConvexColliderBakeData : ISmartBlobberRequestFilter<ConvexColliderBlob>
    {
        public Mesh sharedMesh;

        public bool Filter(IBaker baker, Entity blobBakingEntity)
        {
            if (sharedMesh == null)
                return false;

            baker.DependsOn(sharedMesh);
            // Todo: Is this necessary since baking is Editor-only?
            //if (!sharedMesh.isReadable)
            //    Debug.LogError($"Psyshock failed to convert convex mesh {sharedMesh.name}. The mesh was not marked as readable. Please correct this in the mesh asset's import settings.");

            baker.AddComponent(blobBakingEntity, new ConvexColliderBlobBakeData { mesh = sharedMesh });
            return true;
        }
    }

    [TemporaryBakingType]
    internal struct ConvexColliderBlobBakeData : IComponentData
    {
        public UnityObjectRef<Mesh> mesh;
    }
}

namespace Latios.Psyshock.Authoring.Systems
{
    [RequireMatchingQueriesForUpdate]
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    [UpdateInGroup(typeof(SmartBlobberBakingGroup))]
    public unsafe sealed partial class ConvexColliderSmartBlobberSystem : SystemBase
    {
        EntityQuery m_query;
        List<Mesh>  m_meshCache;

        struct UniqueItem
        {
            public ConvexColliderBlobBakeData             bakeData;
            public BlobAssetReference<ConvexColliderBlob> blob;
        }

        protected override void OnCreate()
        {
            new SmartBlobberTools<ConvexColliderBlob>().Register(World);
        }

#pragma warning disable CS0618
        protected override void OnUpdate()
        {
            int count = m_query.CalculateEntityCountWithoutFiltering();

            var hashmap   = new NativeParallelHashMap<int, UniqueItem>(count * 2, WorldUpdateAllocator);
            var mapWriter = hashmap.AsParallelWriter();

            Entities.WithEntityQueryOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities).ForEach((in ConvexColliderBlobBakeData data) =>
            {
                mapWriter.TryAdd(data.mesh.GetHashCode(), new UniqueItem { bakeData = data });
            }).WithStoreEntityQueryInField(ref m_query).ScheduleParallel();

            var meshes   = new NativeList<UnityObjectRef<Mesh> >(WorldUpdateAllocator);
            var builders = new NativeList<ConvexBuilder>(WorldUpdateAllocator);

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

            Entities.WithReadOnly(hashmap).ForEach((ref SmartBlobberResult result, in ConvexColliderBlobBakeData data) =>
            {
                result.blob = UnsafeUntypedBlobAssetReference.Create(hashmap[data.mesh.GetHashCode()].blob);
            }).WithEntityQueryOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities).ScheduleParallel();
        }
#pragma warning restore CS0618

        struct ConvexBuilder
        {
            public FixedString128Bytes                    meshName;
            public BlobAssetReference<ConvexColliderBlob> result;

            public unsafe void BuildBlob(Mesh.MeshData mesh)
            {
                using ThreadStackAllocator tsa = ThreadStackAllocator.GetAllocator();

                var vector3CachePtr = tsa.Allocate<UnityEngine.Vector3>(mesh.vertexCount);
                var vector3Cache    = CollectionHelper.ConvertExistingDataToNativeArray<UnityEngine.Vector3>(vector3CachePtr, mesh.vertexCount, Allocator.None, true);
                mesh.GetVertices(vector3Cache);

                var builder = new BlobBuilder(Allocator.Temp);
                result      = ConvexColliderBlob.BuildBlob(ref builder, vector3Cache.Reinterpret<float3>().AsReadOnlySpan(), in meshName, Allocator.Persistent);
            }
        }

        [BurstCompile]
        struct BuildBlobsJob : IJobFor
        {
            public NativeArray<ConvexBuilder>    builders;
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

