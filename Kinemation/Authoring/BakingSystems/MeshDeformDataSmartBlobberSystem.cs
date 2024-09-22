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
    /// <summary>
    /// Feature flags that specify use cases a MeshDeformDataBlob should have the data to support
    /// </summary>
    public enum MeshDeformDataFeatures
    {
        VertexSkinning = 0x1,
        Deform = 0x2,
        BlendShapes = 0x4,
        DynamicMesh = 0x8,
        None = 0x0,
        All = 0xf
    }

    public static class MeshDeformDataBlobberAPIExtensions
    {
        /// <summary>
        /// Requests the creation of a MeshDeformDataBlob Blob Asset
        /// </summary>
        /// <param name="mesh">The mesh containing the skin weights to be baked into a blob</param>
        /// <param name="features">Feature flags that specify which use cases the blob should support</param>
        public static SmartBlobberHandle<MeshDeformDataBlob> RequestCreateBlobAsset(this IBaker baker, Mesh mesh, MeshDeformDataFeatures features = MeshDeformDataFeatures.All)
        {
            return baker.RequestCreateBlobAsset<MeshDeformDataBlob, MeshDeformDataBakeData>(new MeshDeformDataBakeData
            {
                mesh     = mesh,
                features = features
            });
        }
    }

    public struct MeshDeformDataBakeData : ISmartBlobberRequestFilter<MeshDeformDataBlob>
    {
        public Mesh                   mesh;
        public MeshDeformDataFeatures features;

        public bool Filter(IBaker baker, Entity blobBakingEntity)
        {
            if (mesh == null)
            {
                Debug.LogError( $"Kinemation failed to bake a mesh deform data blob for {baker.GetAuthoringObjectForDebugDiagnostics().name}. The mesh was null.");
                return false;
            }
            if (features == MeshDeformDataFeatures.None)
            {
                Debug.LogError($"Kinemation failed to bake a mesh deform data blob for {baker.GetAuthoringObjectForDebugDiagnostics().name}. No deform features were enabled.");
                return false;
            }
            baker.DependsOn(mesh);

            baker.AddComponent(blobBakingEntity, new MeshReference
            {
                mesh = mesh
            });
            baker.AddComponent(blobBakingEntity, new MeshFeatures { features = features });
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

    [TemporaryBakingType]
    internal struct MeshFeatures : IComponentData
    {
        public MeshDeformDataFeatures features;
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

        struct BlobAndFeatures
        {
            public BlobAssetReference<MeshDeformDataBlob> blob;
            public MeshDeformDataFeatures                 features;
        }

        protected unsafe override void OnUpdate()
        {
            if (m_meshCache == null)
                m_meshCache = new List<Mesh>();
            if (m_graphicsBufferCache == null)
                m_graphicsBufferCache = new List<GraphicsBuffer>();

            m_meshCache.Clear();
            int count   = m_query.CalculateEntityCountWithoutFiltering();
            var hashmap = new NativeParallelHashMap<MeshReference, BlobAndFeatures>(count, Allocator.TempJob);
            Entities.WithStoreEntityQueryInField(ref m_query).ForEach((in MeshReference meshRef, in MeshFeatures features) =>
            {
                //Debug.Log($"Adding MeshRef key: {meshRef.GetHashCode()}");
                if (!hashmap.TryAdd(meshRef, new BlobAndFeatures { features = features.features }))
                {
                    var v             = hashmap[meshRef];
                    v.features       |= features.features;
                    hashmap[meshRef]  = v;
                }
            }).WithEntityQueryOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities).Run();

            var builders                 = new NativeArray<MeshDeformDataBuilder>(hashmap.Count(), Allocator.TempJob, NativeArrayOptions.ClearMemory);
            var blendShapeRequestBuffers = new NativeArray<NativeArray<uint> >(builders.Length, Allocator.TempJob, NativeArrayOptions.ClearMemory);
            var blendShapeRequests       =
                new NativeArray<UnityEngine.Rendering.AsyncGPUReadbackRequest>(builders.Length, Allocator.TempJob, NativeArrayOptions.ClearMemory);
            int index = 0;
            foreach (var pair in hashmap)
            {
                var mesh     = pair.Key.mesh.Value;
                var features = pair.Value.features;
                m_meshCache.Add(mesh);

                MeshDeformDataBuilder builder = default;

                builder.name       = mesh.name;
                builder.resultBlob = default;
                builder.reference  = pair.Key;
                builder.meshBounds = mesh.bounds;
                builder.features   = features;

                bool requiresBindposes = (features & (MeshDeformDataFeatures.VertexSkinning | MeshDeformDataFeatures.Deform)) != MeshDeformDataFeatures.None;
                if (requiresBindposes && mesh.bindposeCount > 0)
                {
                    var bindposes     = mesh.GetBindposes();
                    var bindposesList = new UnsafeList<Matrix4x4>(bindposes.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                    bindposesList.AddRange(bindposes.GetUnsafeReadOnlyPtr(), bindposes.Length);
                    builder.bindPoses = bindposesList;
                }
                bool requiresDeformWeights = (features & MeshDeformDataFeatures.Deform) != MeshDeformDataFeatures.None;
                if (requiresDeformWeights && mesh.bindposeCount > 0)
                {
                    var weightsArray = mesh.GetAllBoneWeights();
                    var boneWeights  = new UnsafeList<BoneWeight1>(weightsArray.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                    boneWeights.AddRange(weightsArray.GetUnsafeReadOnlyPtr(), weightsArray.Length);

                    var weightCountsArray = mesh.GetBonesPerVertex();
                    var weightCounts      = new UnsafeList<byte>(weightCountsArray.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                    weightCounts.AddRange(weightCountsArray.GetUnsafeReadOnlyPtr(), weightCountsArray.Length);

                    builder.boneWeightCountsPerVertex = weightCounts;
                    builder.boneWeights               = boneWeights;
                }
                bool requiresBlendShapes = (features & MeshDeformDataFeatures.BlendShapes) != MeshDeformDataFeatures.None;
                var  blendShapeCount     = mesh.blendShapeCount;
                if (requiresBlendShapes && blendShapeCount > 0)
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
                    builder.blendShapeBufferSize = verticesCount;
                    var cpuBuffer                = new NativeArray<uint>(math.max(gpuBuffer.count, (int)verticesCount * 10),
                                                                         Allocator.Persistent,
                                                                         NativeArrayOptions.ClearMemory);
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
                        var builder = builders[i];
                        var buffer  = blendShapeRequestBuffers[i];
                        //for (int n = 0; n < 10; n++)
                        //    if (buffer[n].targetVertexIndex > builder.reference.mesh.Value.vertexCount)
                        //        UnityEngine.Debug.LogWarning("Async readback is returning a corrupted blend shape buffer.");
                        builder.blendShapeVertices = new UnsafeList<BlendShapeVertexDisplacement>((int)builder.blendShapeBufferSize,
                                                                                                  Allocator.TempJob,
                                                                                                  NativeArrayOptions.UninitializedMemory);
                        builder.blendShapeVertices.AddRange(buffer.GetUnsafeReadOnlyPtr(), (int)builder.blendShapeBufferSize);
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
                    hashmap[builder.reference] = new BlobAndFeatures { blob = builder.resultBlob, features = builder.features };
                }
            }).Schedule();

            Entities.ForEach((ref SmartBlobberResult result, in MeshReference reference) =>
            {
                result.blob = UnsafeUntypedBlobAssetReference.Create(hashmap[reference].blob);
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
            public uint                                     blendShapeBufferSize;  // Size ignoring excess that Unity sometimes provides
            public MeshDeformDataFeatures                   features;

            public unsafe void BuildBlob(int meshIndex, ref MeshDeformDataContext context)
            {
                if (!context.vector3Cache.IsCreated)
                {
                    context.vector2Cache         = new NativeList<Vector2>(Allocator.Temp);
                    context.vector3Cache         = new NativeList<Vector3>(Allocator.Temp);
                    context.vector4Cache         = new NativeList<Vector4>(Allocator.Temp);
                    context.intCache             = new NativeList<int>(Allocator.Temp);
                    context.positionHashMapCache = new NativeHashMap<float3, int>(1, Allocator.Temp);
                    context.normalHashMapCache   = new NativeHashMap<float3x2, int>(1, Allocator.Temp);
                    context.tangentHashMapCache  = new NativeHashMap<float3x3, int>(1, Allocator.Temp);
                }

                var builder = new BlobBuilder(Allocator.Temp);

                ref var blobRoot = ref builder.ConstructRoot<MeshDeformDataBlob>();
                var     mesh     = context.meshes[meshIndex];

                blobRoot.name = name;

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

                // Vertices
                bool hasVertices    = features != MeshDeformDataFeatures.VertexSkinning;
                var  verticesToSkin = (UndeformedVertex*)builder.Allocate(ref blobRoot.undeformedVertices, hasVertices ? mesh.vertexCount : 0).GetUnsafePtr();
                if (hasVertices)
                {
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
                }

                // Weights
                var maxRadialOffsets = builder.Allocate(ref blobRoot.skinningData.maxRadialOffsetsInBoneSpaceByBone, bindPoses.Length);
                for (int i = 0; i < maxRadialOffsets.Length; i++)
                    maxRadialOffsets[i] = 0;

                Aabb aabb = new Aabb(float.MaxValue, float.MinValue);

                if (hasVertices)
                {
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
                }
                else if (bindPoses.Length > 0)
                {
                    // We still need to compute the bone radial offsets, which means we still need the vertices.
                    context.vector3Cache.ResizeUninitialized(mesh.vertexCount);
                    mesh.GetVertices(context.vector3Cache.AsArray());
                    var positionsArray   = context.vector3Cache.AsArray().Reinterpret<float3>();
                    int weightsProcessed = 0;
                    for (int i = 0; i < boneWeightCountsPerVertex.Length; i++)
                    {
                        var weightsCount = boneWeightCountsPerVertex[i];
                        for (int j = 0; j < weightsCount; j++)
                        {
                            var srcWeight = boneWeights[weightsProcessed + j];
                            // Compute how much the vertex deviates from the bone it is targeting.
                            // That deviation is applied to the maxRadialOffsets for that bone for culling.
                            float3 boneSpacePosition              = math.transform(bindPoses[srcWeight.boneIndex], positionsArray[i]);
                            maxRadialOffsets[srcWeight.boneIndex] = math.max(maxRadialOffsets[srcWeight.boneIndex], math.length(boneSpacePosition));
                            aabb                                  = Psyshock.Physics.CombineAabb(verticesToSkin[i].position, aabb);
                        }
                        weightsProcessed += weightsCount;
                    }
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
                    {
                        bound = math.max(bound, math.length(vertex.positionDisplacement));
                    }
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

                // Normalization
                if ((features & MeshDeformDataFeatures.DynamicMesh) != MeshDeformDataFeatures.None)
                {
                    context.positionHashMapCache.Clear();
                    context.normalHashMapCache.Clear();
                    context.tangentHashMapCache.Clear();
                    context.positionHashMapCache.Capacity = math.max(context.positionHashMapCache.Capacity, mesh.vertexCount);
                    context.normalHashMapCache.Capacity   = math.max(context.normalHashMapCache.Capacity, mesh.vertexCount);
                    context.tangentHashMapCache.Capacity  = math.max(context.tangentHashMapCache.Capacity, mesh.vertexCount);
                    var duplicatesBitsLength              = mesh.vertexCount / 32 + math.select(0, 1, (mesh.vertexCount % 32) != 0);
                    var positionDuplicatesBits            =
                        (MeshNormalizationBlob.BitFieldPrefixSumPair*)builder.Allocate(ref blobRoot.normalizationData.positionDuplicates, duplicatesBitsLength).GetUnsafePtr();
                    var normalDuplicatesBits =
                        (MeshNormalizationBlob.BitFieldPrefixSumPair*)builder.Allocate(ref blobRoot.normalizationData.normalDuplicates, duplicatesBitsLength).GetUnsafePtr();
                    var tangentDuplicatesBits =
                        (MeshNormalizationBlob.BitFieldPrefixSumPair*)builder.Allocate(ref blobRoot.normalizationData.tangentDuplicates, duplicatesBitsLength).GetUnsafePtr();
                    for (int i = 0; i < duplicatesBitsLength; i++)
                    {
                        positionDuplicatesBits[i] = default;
                        normalDuplicatesBits[i]   = default;
                        tangentDuplicatesBits[i]  = default;
                    }
                    int positionDuplicateCount = 0;
                    int normalDuplicateCount   = 0;
                    int tangentDuplicateCount  = 0;
                    for (int i = 0; i < mesh.vertexCount; i++)
                    {
                        var pos = verticesToSkin[i].position;
                        var nrm = new float3x2(pos, verticesToSkin[i].normal);
                        var tan = new float3x3(pos, nrm.c1, verticesToSkin[i].tangent);
                        if (context.positionHashMapCache.ContainsKey(pos))
                        {
                            positionDuplicatesBits[i / 32].bitfield.SetBits(i % 32, true);
                            positionDuplicateCount++;
                            if (context.normalHashMapCache.ContainsKey(nrm))
                            {
                                normalDuplicatesBits[i / 32].bitfield.SetBits(i % 32, true);
                                normalDuplicateCount++;
                                if (context.tangentHashMapCache.ContainsKey(tan))
                                {
                                    tangentDuplicatesBits[i / 32].bitfield.SetBits(i % 32, true);
                                    tangentDuplicateCount++;
                                }
                                else
                                    context.tangentHashMapCache.Add(tan, i);
                            }
                            else
                            {
                                context.normalHashMapCache.Add(nrm, i);
                                context.tangentHashMapCache.Add(tan, i);
                            }
                        }
                        else
                        {
                            context.positionHashMapCache.Add(pos, i);
                            context.normalHashMapCache.Add(nrm, i);
                            context.tangentHashMapCache.Add(tan, i);
                        }
                    }
                    blobRoot.normalizationData.duplicatePositionCount = positionDuplicateCount;
                    blobRoot.normalizationData.duplicateNormalCount   = normalDuplicateCount;
                    blobRoot.normalizationData.duplicateTangentCount  = tangentDuplicateCount;
                    int duplicatePositionPairUintsToAllocate          = 0;
                    int duplicateNormalPairUintsToAllocate            = 0;
                    int duplicateTangentPairUintsToAllocate           = 0;
                    if (mesh.vertexCount <= 1024)
                    {
                        duplicatePositionPairUintsToAllocate = (positionDuplicateCount * 2 / 3) + math.select(0, 1, (positionDuplicateCount * 2 % 3) != 0);
                        duplicateNormalPairUintsToAllocate   = (normalDuplicateCount * 2 / 3) + math.select(0, 1, (normalDuplicateCount * 2 % 3) != 0);
                        duplicateTangentPairUintsToAllocate  = (tangentDuplicateCount * 2 / 3) + math.select(0, 1, (tangentDuplicateCount * 2 % 3) != 0);
                        blobRoot.normalizationData.packMode  = MeshNormalizationBlob.IndicesPackMode.Bits10;
                    }
                    else if (mesh.vertexCount <= ushort.MaxValue)
                    {
                        duplicatePositionPairUintsToAllocate = positionDuplicateCount;
                        duplicateNormalPairUintsToAllocate   = normalDuplicateCount;
                        duplicateTangentPairUintsToAllocate  = tangentDuplicateCount;
                        blobRoot.normalizationData.packMode  = MeshNormalizationBlob.IndicesPackMode.Bits16;
                    }
                    else if (mesh.vertexCount < (1 << 21))
                    {
                        duplicatePositionPairUintsToAllocate = ((positionDuplicateCount * 2 / 3) + math.select(0, 1, (positionDuplicateCount * 2 % 3) != 0)) * 2;  // We cast to ulong so need even count.
                        duplicateNormalPairUintsToAllocate   = ((normalDuplicateCount * 2 / 3) + math.select(0, 1, (normalDuplicateCount * 2 % 3) != 0)) * 2;  // We cast to ulong so need even count.
                        duplicateTangentPairUintsToAllocate  = ((tangentDuplicateCount * 2 / 3) + math.select(0, 1, (tangentDuplicateCount * 2 % 3) != 0)) * 2;  // We cast to ulong so need even count.
                        blobRoot.normalizationData.packMode  = MeshNormalizationBlob.IndicesPackMode.Bits21;
                    }
                    else
                    {
                        duplicatePositionPairUintsToAllocate = positionDuplicateCount * 2;
                        duplicateNormalPairUintsToAllocate   = normalDuplicateCount * 2;
                        duplicateTangentPairUintsToAllocate  = tangentDuplicateCount * 2;
                        blobRoot.normalizationData.packMode  = MeshNormalizationBlob.IndicesPackMode.Bits32;
                    }
                    var duplicatePositionPairs = builder.Allocate(ref blobRoot.normalizationData.packedPositionDuplicateReferencePairs, duplicatePositionPairUintsToAllocate);
                    var duplicateNormalPairs   = builder.Allocate(ref blobRoot.normalizationData.packedNormalDuplicateReferencePairs, duplicateNormalPairUintsToAllocate);
                    var duplicateTangentPairs  = builder.Allocate(ref blobRoot.normalizationData.packedTangentDuplicateReferencePairs, duplicateTangentPairUintsToAllocate);
                    int positionPairsAdded     = 0;
                    int normalPairsAdded       = 0;
                    int tangentPairsAdded      = 0;
                    for (int element = 0; element < duplicatesBitsLength; element++)
                    {
                        positionDuplicatesBits[element].prefixSum = positionPairsAdded;
                        if (positionDuplicatesBits[element].bitfield.Value != 0)
                        {
                            var bits = positionDuplicatesBits[element].bitfield;
                            for (int b = bits.CountTrailingZeros(); b < 32; bits.SetBits(b, false), b = bits.CountTrailingZeros())
                            {
                                int  duplicateIndex = element * 32 + b;
                                int  referenceIndex = context.positionHashMapCache[verticesToSkin[duplicateIndex].position];
                                int2 i              = 2 * positionPairsAdded;
                                i.y                 = i.x + 1;
                                switch (blobRoot.normalizationData.packMode)
                                {
                                    case MeshNormalizationBlob.IndicesPackMode.Bits10:
                                    {
                                        var loadIndex                        = i / 3;
                                        var shift                            = 10 * (i % 3);
                                        duplicatePositionPairs[loadIndex.x] |= (uint)duplicateIndex << shift.x;
                                        duplicatePositionPairs[loadIndex.y] |= (uint)referenceIndex << shift.y;
                                        break;
                                    }
                                    case MeshNormalizationBlob.IndicesPackMode.Bits16:
                                    {
                                        duplicatePositionPairs[positionPairsAdded] = (((uint)referenceIndex) << 16) | ((uint)duplicateIndex);
                                        break;
                                    }
                                    case MeshNormalizationBlob.IndicesPackMode.Bits21:
                                    {
                                        var loadIndex     = i / 3;
                                        var shift         = 21 * (i % 3);
                                        var ptr           = (ulong*)duplicatePositionPairs.GetUnsafePtr();
                                        ptr[loadIndex.x] |= (ulong)duplicateIndex << shift.x;
                                        ptr[loadIndex.y] |= (ulong)referenceIndex << shift.y;
                                        break;
                                    }
                                    case MeshNormalizationBlob.IndicesPackMode.Bits32:
                                    {
                                        duplicatePositionPairs[i.x] = (uint)duplicateIndex;
                                        duplicatePositionPairs[i.y] = (uint)referenceIndex;
                                        break;
                                    }
                                }
                                positionPairsAdded++;
                            }
                        }
                        normalDuplicatesBits[element].prefixSum = normalPairsAdded;
                        if (normalDuplicatesBits[element].bitfield.Value != 0)
                        {
                            var bits = normalDuplicatesBits[element].bitfield;
                            for (int b = bits.CountTrailingZeros(); b < 32; bits.SetBits(b, false), b = bits.CountTrailingZeros())
                            {
                                int  duplicateIndex = element * 32 + b;
                                int  referenceIndex = context.normalHashMapCache[new float3x2(verticesToSkin[duplicateIndex].position, verticesToSkin[duplicateIndex].normal)];
                                int2 i              = 2 * normalPairsAdded;
                                i.y                 = i.x + 1;
                                switch (blobRoot.normalizationData.packMode)
                                {
                                    case MeshNormalizationBlob.IndicesPackMode.Bits10:
                                    {
                                        var loadIndex                      = i / 3;
                                        var shift                          = 10 * (i % 3);
                                        duplicateNormalPairs[loadIndex.x] |= (uint)duplicateIndex << shift.x;
                                        duplicateNormalPairs[loadIndex.y] |= (uint)referenceIndex << shift.y;
                                        break;
                                    }
                                    case MeshNormalizationBlob.IndicesPackMode.Bits16:
                                    {
                                        duplicateNormalPairs[normalPairsAdded] = (((uint)referenceIndex) << 16) | ((uint)duplicateIndex);
                                        break;
                                    }
                                    case MeshNormalizationBlob.IndicesPackMode.Bits21:
                                    {
                                        var loadIndex     = i / 3;
                                        var shift         = 21 * (i % 3);
                                        var ptr           = (ulong*)duplicateNormalPairs.GetUnsafePtr();
                                        ptr[loadIndex.x] |= (ulong)duplicateIndex << shift.x;
                                        ptr[loadIndex.y] |= (ulong)referenceIndex << shift.y;
                                        break;
                                    }
                                    case MeshNormalizationBlob.IndicesPackMode.Bits32:
                                    {
                                        duplicateNormalPairs[i.x] = (uint)duplicateIndex;
                                        duplicateNormalPairs[i.y] = (uint)referenceIndex;
                                        break;
                                    }
                                }
                                normalPairsAdded++;
                            }
                        }
                        tangentDuplicatesBits[element].prefixSum = tangentPairsAdded;
                        if (tangentDuplicatesBits[element].bitfield.Value != 0)
                        {
                            var bits = tangentDuplicatesBits[element].bitfield;
                            for (int b = bits.CountTrailingZeros(); b < 32; bits.SetBits(b, false), b = bits.CountTrailingZeros())
                            {
                                int duplicateIndex = element * 32 + b;
                                int referenceIndex =
                                    context.tangentHashMapCache[new float3x3(verticesToSkin[duplicateIndex].position, verticesToSkin[duplicateIndex].normal,
                                                                             verticesToSkin[duplicateIndex].tangent)];
                                int2 i = 2 * tangentPairsAdded;
                                i.y    = i.x + 1;
                                switch (blobRoot.normalizationData.packMode)
                                {
                                    case MeshNormalizationBlob.IndicesPackMode.Bits10:
                                    {
                                        var loadIndex                       = i / 3;
                                        var shift                           = 10 * (i % 3);
                                        duplicateTangentPairs[loadIndex.x] |= (uint)duplicateIndex << shift.x;
                                        duplicateTangentPairs[loadIndex.y] |= (uint)referenceIndex << shift.y;
                                        break;
                                    }
                                    case MeshNormalizationBlob.IndicesPackMode.Bits16:
                                    {
                                        duplicateTangentPairs[tangentPairsAdded] = (((uint)referenceIndex) << 16) | ((uint)duplicateIndex);
                                        break;
                                    }
                                    case MeshNormalizationBlob.IndicesPackMode.Bits21:
                                    {
                                        var loadIndex     = i / 3;
                                        var shift         = 21 * (i % 3);
                                        var ptr           = (ulong*)duplicateTangentPairs.GetUnsafePtr();
                                        ptr[loadIndex.x] |= (ulong)duplicateIndex << shift.x;
                                        ptr[loadIndex.y] |= (ulong)referenceIndex << shift.y;
                                        break;
                                    }
                                    case MeshNormalizationBlob.IndicesPackMode.Bits32:
                                    {
                                        duplicateTangentPairs[i.x] = (uint)duplicateIndex;
                                        duplicateTangentPairs[i.y] = (uint)referenceIndex;
                                        break;
                                    }
                                }
                                tangentPairsAdded++;
                            }
                        }
                    }

                    if (mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0))
                    {
                        context.vector2Cache.ResizeUninitialized(mesh.vertexCount);
                        mesh.GetUVs(0, context.vector2Cache.AsArray());
                        builder.ConstructFromNativeArray(ref blobRoot.normalizationData.uvs, context.vector2Cache.AsArray().Reinterpret<float2>());
                    }

                    // We need the meshes with absolute indices. But we also need the number of indices to size our buffer accordingly.
                    // Hence these weird gymnastics.
                    int indicesCount = 0;
                    if (mesh.indexFormat == UnityEngine.Rendering.IndexFormat.UInt16)
                        indicesCount = mesh.GetIndexData<ushort>().Length;
                    else
                        indicesCount = mesh.GetIndexData<int>().Length;
                    context.intCache.ResizeUninitialized(indicesCount);
                    bool normalizationIsValid = true;
                    for (int i = 0; i < mesh.subMeshCount; i++)
                    {
                        var submesh = mesh.GetSubMesh(i);
                        if (submesh.topology != MeshTopology.Triangles)
                        {
                            normalizationIsValid = false;
                            break;
                        }

                        mesh.GetIndices(context.intCache.AsArray().GetSubArray(submesh.indexStart, submesh.indexCount), i, true);
                    }

                    if (normalizationIsValid)
                    {
                        blobRoot.normalizationData.triangleCount = indicesCount / 3;
                        int indicesUintToAllocate                = 0;
                        switch (blobRoot.normalizationData.packMode)
                        {
                            case MeshNormalizationBlob.IndicesPackMode.Bits10:
                                indicesUintToAllocate = indicesCount;
                                break;
                            case MeshNormalizationBlob.IndicesPackMode.Bits16:
                                indicesUintToAllocate = indicesCount * 3 / 2 + (indicesCount * 3 / 2) % 2;
                                break;
                            case MeshNormalizationBlob.IndicesPackMode.Bits21:
                                indicesUintToAllocate = indicesCount * 2;
                                break;
                            case MeshNormalizationBlob.IndicesPackMode.Bits32:
                                indicesUintToAllocate = indicesCount * 3;
                                break;
                        }
                        var packedIndices = builder.Allocate(ref blobRoot.normalizationData.packedIndicesByTriangle, indicesUintToAllocate);
                        for (int i = 0; i < indicesCount / 3; i++)
                        {
                            int indexA = context.intCache[3 * i];
                            int indexB = context.intCache[3 * i + 1];
                            int indexC = context.intCache[3 * i + 2];

                            switch (blobRoot.normalizationData.packMode)
                            {
                                case MeshNormalizationBlob.IndicesPackMode.Bits10:
                                    packedIndices[i] = (uint)((indexC << 20) | (indexB << 10) | indexA);
                                    break;
                                case MeshNormalizationBlob.IndicesPackMode.Bits16:
                                {
                                    var ptr        = (ushort*)packedIndices.GetUnsafePtr();
                                    ptr[i * 3]     = (ushort)indexA;
                                    ptr[i * 3 + 1] = (ushort)indexB;
                                    ptr[i * 3 + 2] = (ushort)indexC;
                                    break;
                                }
                                case MeshNormalizationBlob.IndicesPackMode.Bits21:
                                {
                                    var ptr = (ulong*)packedIndices.GetUnsafePtr();
                                    ptr[i]  = ((ulong)indexC << 42) | ((ulong)indexB << 21) | (uint)indexA;
                                    break;
                                }
                                case MeshNormalizationBlob.IndicesPackMode.Bits32:
                                {
                                    packedIndices[i * 3]     = (uint)indexA;
                                    packedIndices[i * 3 + 1] = (uint)indexB;
                                    packedIndices[i * 3 + 2] = (uint)indexC;
                                    break;
                                }
                            }
                        }
                    }
                    else
                        blobRoot.normalizationData.triangleCount = 0;
                }

                // Finish
                resultBlob = builder.CreateBlobAssetReference<MeshDeformDataBlob>(Allocator.Persistent);

                if (bindPoses.IsCreated)
                    bindPoses.Dispose();
                if (boneWeights.IsCreated)
                    boneWeights.Dispose();
                if (boneWeightCountsPerVertex.IsCreated)
                    boneWeightCountsPerVertex.Dispose();
                if (blendShapeNames.IsCreated)
                    blendShapeNames.Dispose();
                if (blendShapeRanges.IsCreated)
                    blendShapeRanges.Dispose();
                if (blendShapeVertices.IsCreated)
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

            [NativeDisableContainerSafetyRestriction] public NativeList<Vector2>          vector2Cache;
            [NativeDisableContainerSafetyRestriction] public NativeList<Vector3>          vector3Cache;
            [NativeDisableContainerSafetyRestriction] public NativeList<Vector4>          vector4Cache;
            [NativeDisableContainerSafetyRestriction] public NativeList<int>              intCache;
            [NativeDisableContainerSafetyRestriction] public NativeHashMap<float3, int>   positionHashMapCache;
            [NativeDisableContainerSafetyRestriction] public NativeHashMap<float3x2, int> normalHashMapCache;
            [NativeDisableContainerSafetyRestriction] public NativeHashMap<float3x3, int> tangentHashMapCache;

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

