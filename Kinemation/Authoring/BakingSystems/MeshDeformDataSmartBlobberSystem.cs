using System;
using System.Collections.Generic;
using Latios.Authoring;
using Latios.Authoring.Systems;
using Latios.Psyshock;
using Latios.Transforms;
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
    public static class MeshDeformDataBlobberAPIExtensions
    {
        /// <summary>
        /// Requests the creation of a MeshDeformDataBlob Blob Asset
        /// </summary>
        /// <param name="mesh">The mesh containing the skin weights to be baked into a blob</param>
        public static SmartBlobberHandle<MeshDeformDataBlob> RequestCreateBlobAsset(this IBaker baker, Mesh mesh)
        {
            return baker.RequestCreateBlobAsset<MeshDeformDataBlob, MeshDeformDataBakeData>(new MeshDeformDataBakeData
            {
                mesh = mesh
            });
        }
    }

    public struct MeshDeformDataBakeData : ISmartBlobberRequestFilter<MeshDeformDataBlob>
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
            return mesh.Equals(other.mesh);
        }

        public override int GetHashCode()
        {
            return mesh.GetHashCode();
        }
    }
}

namespace Latios.Kinemation.Authoring.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    public partial class MeshDeformDataSmartBlobberSystem : SystemBase
    {
        List<GraphicsBuffer> m_graphicsBufferCache;
        List<Mesh>           m_meshCache;
        EntityQuery          m_query;

        protected override void OnCreate()
        {
            new SmartBlobberTools<MeshDeformDataBlob>().Register(World);
        }

        protected unsafe override void OnUpdate()
        {
            if (m_meshCache == null)
                m_meshCache = new List<Mesh>();
            if (m_graphicsBufferCache == null)
                m_graphicsBufferCache = new List<GraphicsBuffer>();

            m_meshCache.Clear();
            int count   = m_query.CalculateEntityCountWithoutFiltering();
            var hashmap = new NativeParallelHashMap<MeshReference, BlobAssetReference<MeshDeformDataBlob> >(count, Allocator.TempJob);
            Entities.WithStoreEntityQueryInField(ref m_query).ForEach((in MeshReference meshRef) =>
            {
                //Debug.Log($"Adding MeshRef key: {meshRef.GetHashCode()}");
                hashmap.TryAdd(meshRef, default);
            }).WithEntityQueryOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities).Run();

            var builders                 = new NativeArray<MeshDeformDataBuilder>(hashmap.Count(), Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var blendShapeRequestBuffers = new NativeArray<NativeArray<BlendShapeVertexDisplacement> >(builders.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var blendShapeRequests       =
                new NativeArray<UnityEngine.Rendering.AsyncGPUReadbackRequest>(builders.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            int index = 0;
            foreach (var pair in hashmap)
            {
                var mesh = pair.Key.mesh.Value;
                m_meshCache.Add(mesh);

                MeshDeformDataBuilder builder = default;

                builder.name       = mesh.name;
                builder.resultBlob = default;
                builder.reference  = pair.Key;
                builder.meshBounds = mesh.bounds;

                if (mesh.bindposeCount > 0)
                {
                    var bindposes     = mesh.GetBindposes();
                    var bindposesList = new UnsafeList<Matrix4x4>(bindposes.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                    bindposesList.AddRange(bindposes.GetUnsafeReadOnlyPtr(), bindposes.Length);

                    var weightsArray = mesh.GetAllBoneWeights();
                    var boneWeights  = new UnsafeList<BoneWeight1>(weightsArray.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                    boneWeights.AddRange(weightsArray.GetUnsafeReadOnlyPtr(), weightsArray.Length);

                    var weightCountsArray = mesh.GetBonesPerVertex();
                    var weightCounts      = new UnsafeList<byte>(weightCountsArray.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                    weightCounts.AddRange(weightCountsArray.GetUnsafeReadOnlyPtr(), weightCountsArray.Length);

                    builder.bindPoses                 = bindposesList;
                    builder.boneWeightCountsPerVertex = weightCounts;
                    builder.boneWeights               = boneWeights;
                }
                var blendShapeCount = mesh.blendShapeCount;
                if (blendShapeCount > 0)
                {
                    var gpuBuffer            = mesh.GetBlendShapeBuffer(UnityEngine.Rendering.BlendShapeBufferLayout.PerShape);
                    builder.blendShapeNames  = new UnsafeList<FixedString128Bytes>(blendShapeCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                    builder.blendShapeRanges = new UnsafeList<BlendShapeBufferRange>(blendShapeCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

                    uint verticesCount = 0;
                    for (int shapeIndex = 0; shapeIndex < blendShapeCount; shapeIndex++)
                    {
                        builder.blendShapeNames.Add(mesh.GetBlendShapeName(shapeIndex));
                        var range = mesh.GetBlendShapeBufferRange(shapeIndex);
                        builder.blendShapeRanges.Add(in range);
                        verticesCount = math.max(verticesCount, range.endIndex + 1);
                    }
                    var cpuBuffer =
                        new NativeArray<BlendShapeVertexDisplacement>((int)verticesCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                    blendShapeRequests[index]       = UnityEngine.Rendering.AsyncGPUReadback.RequestIntoNativeArray(ref cpuBuffer, gpuBuffer);
                    blendShapeRequestBuffers[index] = cpuBuffer;
                    m_graphicsBufferCache.Add(gpuBuffer);
                }
                else
                {
                    blendShapeRequestBuffers[index] = default;
                    blendShapeRequests[index]       = default;
                }

                builders[index] = builder;
                index++;
            }

            UnityEngine.Rendering.AsyncGPUReadback.WaitAllRequests();
            for (int i = 0; i < builders.Length; i++)
            {
                if (blendShapeRequestBuffers[i].IsCreated)
                {
                    var request = blendShapeRequests[i];
                    if (request.hasError)
                    {
                        UnityEngine.Debug.LogError($"An error occurred while obtaining the blend shapes for mesh {builders[i].name}");
                        var builder = builders[i];
                        blendShapeRequestBuffers[i].Dispose();
                        blendShapeRequestBuffers[i] = default;
                        builder.blendShapeNames.Dispose();
                        builder.blendShapeNames = default;
                        builder.blendShapeRanges.Dispose();
                        builder.blendShapeRanges = default;
                        builders[i]              = builder;
                    }
                    else
                    {
                        var builder                = builders[i];
                        var buffer                 = blendShapeRequestBuffers[i];
                        builder.blendShapeVertices = new UnsafeList<BlendShapeVertexDisplacement>(buffer.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                        builder.blendShapeVertices.AddRange(buffer.GetUnsafeReadOnlyPtr(), buffer.Length);
                        buffer.Dispose();
                        builders[i] = builder;
                    }
                }
            }

            foreach (var disposable in m_graphicsBufferCache)
                disposable.Dispose();
            m_graphicsBufferCache.Clear();
            blendShapeRequests.Dispose();
            blendShapeRequestBuffers.Dispose();

            var context = new MeshDeformDataContext
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

        struct MeshDeformDataBuilder
        {
            public FixedString128Bytes                      name;
            public UnsafeList<byte>                         boneWeightCountsPerVertex;
            public UnsafeList<BoneWeight1>                  boneWeights;
            public UnsafeList<Matrix4x4>                    bindPoses;
            public UnsafeList<BlendShapeVertexDisplacement> blendShapeVertices;
            public UnsafeList<BlendShapeBufferRange>        blendShapeRanges;
            public UnsafeList<FixedString128Bytes>          blendShapeNames;
            public BlobAssetReference<MeshDeformDataBlob>   resultBlob;
            public MeshReference                            reference;
            public Bounds                                   meshBounds;

            public unsafe void BuildBlob(int meshIndex, ref MeshDeformDataContext context)
            {
                if (!context.vector3Cache.IsCreated)
                {
                    context.vector3Cache = new NativeList<Vector3>(Allocator.Temp);
                    context.vector4Cache = new NativeList<Vector4>(Allocator.Temp);
                }

                var builder = new BlobBuilder(Allocator.Temp);

                ref var blobRoot = ref builder.ConstructRoot<MeshDeformDataBlob>();
                var     mesh     = context.meshes[meshIndex];

                blobRoot.name = name;

                // Vertices
                var verticesToSkin = (UndeformedVertex*)builder.Allocate(ref blobRoot.undeformedVertices, mesh.vertexCount).GetUnsafePtr();
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
                var bindposesMats = builder.Allocate(ref blobRoot.skinningData.bindPoses, bindPoses.Length);
                var bindposesDq   = builder.Allocate(ref blobRoot.skinningData.bindPosesDQ, bindPoses.Length);
                for (int i = 0; i < bindPoses.Length; i++)
                {
                    float4x4 mat4x4  = bindPoses[i];
                    bindposesMats[i] = new float3x4(mat4x4.c0.xyz, mat4x4.c1.xyz, mat4x4.c2.xyz, mat4x4.c3.xyz);
                    var inverse      = math.inverse(mat4x4);
                    var scale        = 1f / Unity.Transforms.TransformHelpers.Scale(in inverse);
                    var rotation     = math.inverse(Unity.Transforms.TransformHelpers.Rotation(in inverse));
                    var position     = mat4x4.c3.xyz;
                    bindposesDq[i]   = new BindPoseDqs
                    {
                        real  = rotation,
                        dual  = new quaternion(0.5f * math.mul(new quaternion(new float4(position, 0f)), rotation).value),
                        scale = new float4(scale, 0f)
                    };
                }

                // Weights
                var maxRadialOffsets = builder.Allocate(ref blobRoot.skinningData.maxRadialOffsetsInBoneSpaceByBone, bindPoses.Length);
                for (int i = 0; i < maxRadialOffsets.Length; i++)
                    maxRadialOffsets[i] = 0;

                // The compute shader processes batches of 1024 vertices at a time. Before each batch, there's a special
                // "bone weight" which serves as a header and holds an offset to the next header.
                int boneWeightBatches = mesh.vertexCount / 1024;
                if (mesh.vertexCount % 1024 != 0)
                    boneWeightBatches++;
                boneWeightBatches    = math.select(boneWeightBatches, 0, boneWeights.IsEmpty);
                var boneWeightStarts = builder.Allocate(ref blobRoot.skinningData.boneWeightBatchStarts, boneWeightBatches);

                var boneWeightsDst = builder.Allocate(ref blobRoot.skinningData.boneWeights, boneWeights.Length + boneWeightBatches);

                var weightStartsPerCache    = stackalloc int[1024];
                int weightStartsBatchOffset = 0;

                Aabb aabb = new Aabb(float.MaxValue, float.MinValue);
                int  dst  = 0;
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
                            aabb                                  = Psyshock.Physics.CombineAabb(verticesToSkin[i].position, aabb);

                            if (retireThisRound)
                                threadsAlive--;
                        }
                    }

                    // When we were first finding bone weight offsets per vertex, we used this counter.
                    // It now holds the number of weights in the batch, so we add it here for the next batch.
                    weightStartsBatchOffset += weightStartCounter;

                    // And now we write the header. We write the vertex count into the weight.
                    // The counter is how many weights we need to jump over to reach the next header.
                    // We add one to account for the fact that the first vertex weight is after this
                    // header, effectively making it a base-1 array and the count indexing the last
                    // weight rather than the next header.
                    boneWeightsDst[batchHeaderIndex] = new BoneWeightLinkedList
                    {
                        weight           = math.asfloat((uint)mesh.vertexCount),
                        next10Lds7Bone15 = (uint)weightStartCounter + 1
                    };
                    boneWeightStarts[batchIndex] = (uint)batchHeaderIndex;
                }
                blobRoot.undeformedAabb = aabb;
                if (boneWeights.IsEmpty)
                    blobRoot.undeformedAabb = new Aabb(meshBounds.min, meshBounds.max);

                // Blend Shapes
                var shapeMetas  = builder.Allocate(ref blobRoot.blendShapesData.shapes, blendShapeRanges.Length);
                var shapeNames  = builder.Allocate(ref blobRoot.blendShapesData.shapeNames, blendShapeRanges.Length);
                var shapeBounds = builder.Allocate(ref blobRoot.blendShapesData.maxRadialOffsets, blendShapeRanges.Length);

                var allShapeVerticesAsArray = CollectionHelper.ConvertExistingDataToNativeArray<BlendShapeVertexDisplacement>(blendShapeVertices.Ptr,
                                                                                                                              blendShapeVertices.Length,
                                                                                                                              Allocator.None,
                                                                                                                              true);
                for (int i = 0; i < blendShapeRanges.Length; i++)
                {
                    shapeNames[i]      = blendShapeNames[i];
                    uint shapeStart    = blendShapeRanges[i].startIndex;
                    uint shapeCount    = blendShapeRanges[i].endIndex + 1 - shapeStart;
                    var  shapeVertices = allShapeVerticesAsArray.GetSubArray((int)shapeStart, (int)shapeCount);
                    shapeVertices.Sort(new BlendShapeVertexComparer());
                    float bound = 0f;
                    foreach (var vertex in shapeVertices)
                        bound      = math.max(bound, math.length(vertex.positionDisplacement));
                    shapeBounds[i] = bound;

                    // Search for matching permutation. PermutationIds are just set to the first shape index with the permutation.
                    bool matchedGroup = false;
                    for (int j = 0; j < i; j++)
                    {
                        if (shapeMetas[j].count != shapeCount)
                            continue;
                        var  compareVertices = allShapeVerticesAsArray.GetSubArray((int)shapeMetas[j].start, (int)shapeMetas[j].count);
                        bool different       = false;
                        for (int k = 0; k < shapeCount; k++)
                        {
                            if (shapeVertices[k].targetVertexIndex != compareVertices[k].targetVertexIndex)
                            {
                                different = true;
                                break;
                            }
                        }
                        if (different)
                            continue;

                        matchedGroup  = true;
                        shapeMetas[i] = new BlendShapeVertexDisplacementShape
                        {
                            start         = shapeStart,
                            count         = shapeCount,
                            permutationID = (uint)j
                        };
                        break;
                    }
                    if (matchedGroup)
                        continue;

                    shapeMetas[i] = new BlendShapeVertexDisplacementShape
                    {
                        start         = shapeStart,
                        count         = shapeCount,
                        permutationID = (uint)i
                    };
                }
                builder.ConstructFromNativeArray(ref blobRoot.blendShapesData.gpuData, allShapeVerticesAsArray);

                // Finish
                resultBlob = builder.CreateBlobAssetReference<MeshDeformDataBlob>(Allocator.Persistent);

                bindPoses.Dispose();
                boneWeights.Dispose();
                boneWeightCountsPerVertex.Dispose();
                blendShapeNames.Dispose();
                blendShapeRanges.Dispose();
                blendShapeVertices.Dispose();
            }
        }

        struct BlendShapeVertexComparer : IComparer<BlendShapeVertexDisplacement>
        {
            public int Compare(BlendShapeVertexDisplacement x, BlendShapeVertexDisplacement y)
            {
                return x.targetVertexIndex.CompareTo(y.targetVertexIndex);
            }
        }

        struct MeshDeformDataContext : IDisposable
        {
            [ReadOnly] public Mesh.MeshDataArray meshes;

            [NativeDisableContainerSafetyRestriction] public NativeList<Vector3> vector3Cache;
            [NativeDisableContainerSafetyRestriction] public NativeList<Vector4> vector4Cache;

            public void Dispose() => meshes.Dispose();
        }

        [BurstCompile]
        struct BuildJob : IJobFor
        {
            public MeshDeformDataContext              context;
            public NativeArray<MeshDeformDataBuilder> builders;

            public void Execute(int i)
            {
                var builder = builders[i];
                builder.BuildBlob(i, ref context);
                builders[i] = builder;
            }
        }
    }
}

