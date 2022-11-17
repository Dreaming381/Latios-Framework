using System;
using System.Collections.Generic;
using Latios.Authoring;
using Latios.Authoring.Systems;
using Latios.Unsafe;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Entities.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Kinemation.Authoring
{
    public static class MeshSkinningBlobberAPIExtensions
    {
        /// <summary>
        /// Requests the creation of a MeshSkinningBlob Blob Asset
        /// </summary>
        /// <param name="mesh">The mesh containing the skin weights to be baked into a blob</param>
        public static SmartBlobberHandle<MeshSkinningBlob> RequestCreateBlobAsset(this IBaker baker, Mesh mesh)
        {
            return baker.RequestCreateBlobAsset<MeshSkinningBlob, MeshSkinningBakeData>(new MeshSkinningBakeData
            {
                mesh = mesh
            });
        }
    }

    public struct MeshSkinningBakeData : ISmartBlobberRequestFilter<MeshSkinningBlob>
    {
        public Mesh mesh;

        public bool Filter(IBaker baker, Entity blobBakingEntity)
        {
            if (mesh == null)
            {
                Debug.LogError( $"Kinemation failed to bake a mesh skinning blob for {baker.GetName()}. The mesh was null.");
                return false;
            }
            baker.DependsOn(mesh);
            if (mesh.bindposeCount <= 0)
            {
                Debug.LogError(
                    $"Kinemation failed to bake a mesh skinning blob for {baker.GetName()}. The mesh does not have skinning info. If you are trying to bake a mesh with only blend shapes, this is not currently supported.");
                return false;
            }

            baker.AddComponent(blobBakingEntity, new MeshReference
            {
                mesh = mesh
            });
            return true;
        }
    }

    [TemporaryBakingType]
    internal struct MeshReference : IComponentData, IEquatable<MeshReference>
    {
        public UnityObjectRef<Mesh> mesh;

        public bool Equals(MeshReference other)
        {
            return mesh.GetInstanceID().Equals(other.mesh.GetInstanceID());
        }

        public override int GetHashCode()
        {
            return mesh.GetInstanceID();
        }
    }
}

namespace Latios.Kinemation.Authoring.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    public partial class MeshSkinningSmartBlobberSystem : SystemBase
    {
        List<Mesh>  m_meshCache;
        EntityQuery m_query;

        protected override void OnCreate()
        {
            new SmartBlobberTools<MeshSkinningBlob>().Register(World);
        }

        protected unsafe override void OnUpdate()
        {
            if (m_meshCache == null)
                m_meshCache = new List<Mesh>();

            m_meshCache.Clear();
            int count   = m_query.CalculateEntityCountWithoutFiltering();
            var hashmap = new NativeParallelHashMap<MeshReference, BlobAssetReference<MeshSkinningBlob> >(count, Allocator.TempJob);
            Entities.WithStoreEntityQueryInField(ref m_query).ForEach((in MeshReference meshRef) =>
            {
                //Debug.Log($"Adding MeshRef key: {meshRef.GetHashCode()}");
                hashmap.TryAdd(meshRef, default);
            }).WithEntityQueryOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities).Run();

            var builders = new NativeArray<MeshSkinningBuilder>(hashmap.Count(), Allocator.TempJob);
            int index    = 0;
            foreach (var pair in hashmap)
            {
                var mesh = pair.Key.mesh.Value;
                m_meshCache.Add(mesh);

                var bindposes     = mesh.GetBindposes();
                var bindposesList = new UnsafeList<Matrix4x4>(bindposes.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                bindposesList.AddRange(bindposes.GetUnsafeReadOnlyPtr(), bindposes.Length);

                var weightsArray = mesh.GetAllBoneWeights();
                var boneWeights  = new UnsafeList<BoneWeight1>(weightsArray.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                boneWeights.AddRange(weightsArray.GetUnsafeReadOnlyPtr(), weightsArray.Length);

                var weightCountsArray = mesh.GetBonesPerVertex();
                var weightCounts      = new UnsafeList<byte>(weightCountsArray.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                weightCounts.AddRange(weightCountsArray.GetUnsafeReadOnlyPtr(), weightCountsArray.Length);

                builders[index] = new MeshSkinningBuilder
                {
                    name                      = mesh.name,
                    bindPoses                 = bindposesList,
                    boneWeightCountsPerVertex = weightCounts,
                    boneWeights               = boneWeights,
                    resultBlob                = default,
                    reference                 = pair.Key
                };
                index++;
            }

            var context = new MeshSkinningContext
            {
#if UNITY_EDITOR
                meshes = UnityEditor.MeshUtility.AcquireReadOnlyMeshData(m_meshCache)
#else
                meshes = Mesh.AcquireReadOnlyMeshData(m_meshCache)
#endif
            };

            new BuildJob { builders = builders, context = context }.ScheduleParallel(builders.Length, 1, default).Complete();

            context.meshes.Dispose();

            Job.WithCode(() =>
            {
                foreach (var builder in builders)
                {
                    //Debug.Log($"Setting MeshRef blob: {builder.reference.GetHashCode()}");
                    hashmap[builder.reference] = builder.resultBlob;
                }
            }).Schedule();

            Entities.ForEach((ref SmartBlobberResult result, in MeshReference reference) =>
            {
                result.blob = UnsafeUntypedBlobAssetReference.Create(hashmap[reference]);
            }).WithReadOnly(hashmap).WithEntityQueryOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities).ScheduleParallel();

            Dependency = builders.Dispose(Dependency);
            Dependency = hashmap.Dispose(Dependency);
        }

        struct MeshSkinningBuilder
        {
            public FixedString128Bytes                  name;
            public UnsafeList<byte>                     boneWeightCountsPerVertex;
            public UnsafeList<BoneWeight1>              boneWeights;
            public UnsafeList<Matrix4x4>                bindPoses;
            public BlobAssetReference<MeshSkinningBlob> resultBlob;
            public MeshReference                        reference;

            public unsafe void BuildBlob(int meshIndex, ref MeshSkinningContext context)
            {
                if (!context.vector3Cache.IsCreated)
                {
                    context.vector3Cache = new NativeList<Vector3>(Allocator.Temp);
                    context.vector4Cache = new NativeList<Vector4>(Allocator.Temp);
                }

                var builder = new BlobBuilder(Allocator.Temp);

                ref var blobRoot = ref builder.ConstructRoot<MeshSkinningBlob>();
                var     mesh     = context.meshes[meshIndex];

                //builder.AllocateFixedString(ref blobRoot.name, meshNames[data.meshIndex]);
                blobRoot.name = name;

                // Vertices
                var verticesToSkin = (VertexToSkin*)builder.Allocate(ref blobRoot.verticesToSkin, mesh.vertexCount).GetUnsafePtr();
                context.vector3Cache.ResizeUninitialized(mesh.vertexCount);
                mesh.GetVertices(context.vector3Cache.AsArray());
                var t = context.vector3Cache.AsArray().Reinterpret<float3>();
                for (int i = 0; i < mesh.vertexCount; i++)
                {
                    verticesToSkin[i].position = t[i];
                }
                mesh.GetNormals(context.vector3Cache.AsArray());
                for (int i = 0; i < mesh.vertexCount; i++)
                {
                    verticesToSkin[i].normal = t[i];
                }
                context.vector4Cache.ResizeUninitialized(mesh.vertexCount);
                mesh.GetTangents(context.vector4Cache.AsArray());
                var tt = context.vector4Cache.AsArray().Reinterpret<float4>();
                for (int i = 0; i < mesh.vertexCount; i++)
                {
                    verticesToSkin[i].tangent = tt[i].xyz;
                }

                // BindPoses
                builder.ConstructFromNativeArray(ref blobRoot.bindPoses, (float4x4*)bindPoses.Ptr, bindPoses.Length);

                // Weights
                var maxRadialOffsets = builder.Allocate(ref blobRoot.maxRadialOffsetsInBoneSpaceByBone, bindPoses.Length);
                for (int i = 0; i < maxRadialOffsets.Length; i++)
                    maxRadialOffsets[i] = 0;

                // The compute shader processes batches of 1024 vertices at a time. Before each batch, there's a special
                // "bone weight" which serves as a header and holds an offset to the next header.
                int boneWeightBatches = mesh.vertexCount / 1024;
                if (mesh.vertexCount % 1024 != 0)
                    boneWeightBatches++;
                var boneWeightStarts = builder.Allocate(ref blobRoot.boneWeightBatchStarts, boneWeightBatches);

                var boneWeightsDst = builder.Allocate(ref blobRoot.boneWeights, boneWeights.Length + boneWeightBatches);

                var weightStartsPerCache    = stackalloc int[1024];
                int weightStartsBatchOffset = 0;

                int dst = 0;
                for (int batchIndex = 0; batchIndex < boneWeightBatches; batchIndex++)
                {
                    // Keep this, because we need to write to it after we know how many weights to jump over.
                    int batchHeaderIndex = dst;
                    dst++;
                    int verticesInBatch = math.min(1024,
                                                   mesh.vertexCount - batchIndex * 1024);
                    int batchOffset  = batchIndex * 1024;
                    int threadsAlive = verticesInBatch;

                    int weightStartCounter = 0;
                    for (int i = 0; i < verticesInBatch; i++)
                    {
                        // Find the first bone weight for each vertex in the batch in the source bone weights.
                        weightStartsPerCache[i]  = weightStartCounter;
                        weightStartCounter      += boneWeightCountsPerVertex[batchOffset + i];
                    }

                    // We have as many rounds as weights in a batch of vertices.
                    // The number of rounds translates directly to inner loop iterations per batch on the GPU.
                    for (int weightRound = 1; threadsAlive > 0; weightRound++)
                    {
                        for (int i = 0; i < verticesInBatch; i++)
                        {
                            // If the number of weights for this vertex is less than the weightRound,
                            // this vertex has already finished.
                            int weightsForThisVertex = boneWeightCountsPerVertex[batchOffset + i];
                            if (weightsForThisVertex < weightRound)
                                continue;
                            // If this is the last weight for this vertex, we'll set the weight negative
                            // to signal to the GPU it is the last weight. Packing signals into sign bits
                            // is free on most GPUs.
                            bool retireThisRound = weightsForThisVertex == weightRound;
                            // First, find the offset in the source weights related to this batch.
                            // Then, find the offset for this vertex.
                            // Then, add the weight round and convert from base-1 to base-0.
                            var srcWeight = boneWeights[weightStartsBatchOffset + weightStartsPerCache[i] + weightRound - 1];
                            var dstWeight = new BoneWeightLinkedList
                            {
                                weight = math.select(srcWeight.weight, -srcWeight.weight, retireThisRound),
                                // There are up to 1024 vertices in a batch, but we only need the next offset when
                                // at least one vertex is active. So we map the range of [1, 1024] to [0, 1023].
                                next10Lds7Bone15 = (((uint)threadsAlive - 1) << 22) | (uint)(srcWeight.boneIndex & 0x7fff)
                            };

                            boneWeightsDst[dst] = dstWeight;
                            dst++;

                            // Compute how much the vertex deviates from the bone it is targeting.
                            // That deviation is applied to the maxRadialOffsets for that bone for culling.
                            float3 boneSpacePosition              = math.transform(bindPoses[srcWeight.boneIndex], verticesToSkin[i].position);
                            maxRadialOffsets[srcWeight.boneIndex] = math.max(maxRadialOffsets[srcWeight.boneIndex], math.length(boneSpacePosition));

                            if (retireThisRound)
                                threadsAlive--;
                        }
                    }

                    // When we were first finding bone weight offsets per vertex, we used this counter.
                    // It now holds the number of weights in the batch, so we add it here for the next batch.
                    weightStartsBatchOffset += weightStartCounter;

                    // And now we write the header. We write mostly debug metadata into the weight.
                    // The counter is how many weights we need to jump over to reach the next header.
                    // We add one to account for the fact that the first vertex weight is after this
                    // header, effectively making it a base-1 array and the count indexing the last
                    // weight rather than the next header.
                    boneWeightsDst[batchHeaderIndex] = new BoneWeightLinkedList
                    {
                        weight           = math.asfloat(0xbb000000 | (uint)batchIndex),
                        next10Lds7Bone15 = (uint)weightStartCounter + 1
                    };
                    boneWeightStarts[batchIndex] = (uint)batchHeaderIndex;
                }

                resultBlob = builder.CreateBlobAssetReference<MeshSkinningBlob>(Allocator.Persistent);

                bindPoses.Dispose();
                boneWeights.Dispose();
                boneWeightCountsPerVertex.Dispose();
            }
        }

        struct MeshSkinningContext : IDisposable
        {
            [ReadOnly] public Mesh.MeshDataArray meshes;

            [NativeDisableContainerSafetyRestriction] public NativeList<Vector3> vector3Cache;
            [NativeDisableContainerSafetyRestriction] public NativeList<Vector4> vector4Cache;

            public void Dispose() => meshes.Dispose();
        }

        [BurstCompile]
        struct BuildJob : IJobFor
        {
            public MeshSkinningContext              context;
            public NativeArray<MeshSkinningBuilder> builders;

            public void Execute(int i)
            {
                var builder = builders[i];
                builder.BuildBlob(i, ref context);
                builders[i] = builder;
            }
        }
    }
}

