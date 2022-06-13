using System.Collections.Generic;
using Latios.Authoring;
using Latios.Authoring.Systems;
using Latios.Unsafe;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Kinemation.Authoring.Systems
{
    [ConverterVersion("Latios", 1)]
    [DisableAutoCreation]
    public class MeshSkinningSmartBlobberSystem : SmartBlobberConversionSystem<MeshSkinningBlob, MeshSkinningBakeData, MeshSkinningConverter, MeshSkinningContext>
    {
        struct AuthoringHandlePair
        {
            public SkinnedMeshConversionContext         authoring;
            public SmartBlobberHandle<MeshSkinningBlob> handle;
        }

        List<AuthoringHandlePair> m_contextList = new List<AuthoringHandlePair>();

        protected override void GatherInputs()
        {
            m_contextList.Clear();

            bool isEditorAndSubscene = false;
#if UNITY_EDITOR
            isEditorAndSubscene = this.GetSettings().sceneGUID != default;
#endif

            Entities.ForEach((SkinnedMeshConversionContext context) =>
            {
                var sharedMesh = context.renderer.sharedMesh;

                // Todo: Should we throw an error here?
                if (sharedMesh == null)
                    return;

                if (!isEditorAndSubscene && !sharedMesh.isReadable)
                {
                    Debug.LogError(
                        $"Attempted to runtime-convert skinned mesh {context.renderer.gameObject.name} with mesh {sharedMesh.name} but the mesh is not been imported with read/write access.");
                    return;
                }
                var handle = AddToConvert(context.renderer.gameObject, new MeshSkinningBakeData { sharedMesh = sharedMesh });
                m_contextList.Add(new AuthoringHandlePair { authoring                                        = context, handle = handle });
            });
        }

        protected override void FinalizeOutputs()
        {
            foreach (var pair in m_contextList)
            {
                var context                                                                    = pair.authoring;
                var go                                                                         = context.renderer.gameObject;
                var entity                                                                     = GetPrimaryEntity(go);
                DstEntityManager.AddComponentData(entity, new MeshSkinningBlobReference { blob = pair.handle.Resolve() });
            }
        }

        protected override void Filter(FilterBlobberData blobberData, ref MeshSkinningContext context, NativeArray<int> inputToFilteredMapping)
        {
            var hashes = new NativeArray<int>(blobberData.Count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < blobberData.Count; i++)
            {
                var input = blobberData.input[i];

                DeclareAssetDependency(blobberData.associatedObject[i], input.sharedMesh);
                hashes[i] = input.sharedMesh.GetInstanceID();
            }

            new DeduplicateJob { hashes = hashes, inputToFilteredMapping = inputToFilteredMapping }.Run();
            hashes.Dispose();
        }

        protected override unsafe void PostFilter(PostFilterBlobberData blobberData, ref MeshSkinningContext context)
        {
            var meshList       = new List<Mesh>();
            var bindPosesCache = new List<Matrix4x4>();

            var converters = blobberData.converters;

            var allocator = World.UpdateAllocator.ToAllocator;

            for (int i = 0; i < blobberData.Count; i++)
            {
                var mesh = blobberData.input[i].sharedMesh;
                meshList.Add(mesh);

                bindPosesCache.Clear();
                mesh.GetBindposes(bindPosesCache);
                var bindposes = new UnsafeList<Matrix4x4>(bindPosesCache.Count, allocator);
                foreach (var bp in bindPosesCache)
                    bindposes.Add(bp);

                var weightsArray = mesh.GetAllBoneWeights();
                var boneWeights  = new UnsafeList<BoneWeight1>(weightsArray.Length, allocator);
                boneWeights.AddRange(weightsArray.GetUnsafeReadOnlyPtr(), weightsArray.Length);

                var weightCountsArray = mesh.GetBonesPerVertex();
                var weightCounts      = new UnsafeList<byte>(weightCountsArray.Length, allocator);
                weightCounts.AddRange(weightCountsArray.GetUnsafeReadOnlyPtr(), weightCountsArray.Length);

                converters[i] = new MeshSkinningConverter
                {
                    name                      = mesh.name,
                    bindPoses                 = bindposes,
                    boneWeights               = boneWeights,
                    boneWeightCountsPerVertex = weightCounts
                };
            }

            context.meshes = Mesh.AcquireReadOnlyMeshData(meshList);
        }

        [BurstCompile]
        struct DeduplicateJob : IJob
        {
            [ReadOnly] public NativeArray<int> hashes;
            public NativeArray<int>            inputToFilteredMapping;

            public void Execute()
            {
                var map = new NativeHashMap<int, int>(hashes.Length, Allocator.Temp);
                for (int i = 0; i < hashes.Length; i++)
                {
                    if (inputToFilteredMapping[i] < 0)
                        continue;

                    if (map.TryGetValue(hashes[i], out int index))
                        inputToFilteredMapping[i] = index;
                    else
                        map.Add(hashes[i], i);
                }
            }
        }
    }

    public struct MeshSkinningConverter : ISmartBlobberContextBuilder<MeshSkinningBlob, MeshSkinningContext>
    {
        public FixedString128Bytes     name;
        public UnsafeList<byte>        boneWeightCountsPerVertex;
        public UnsafeList<BoneWeight1> boneWeights;
        public UnsafeList<Matrix4x4>   bindPoses;

        public unsafe BlobAssetReference<MeshSkinningBlob> BuildBlob(int prefilterIndex, int postfilterIndex, ref MeshSkinningContext context)
        {
            if (!context.vector3Cache.IsCreated)
            {
                context.vector3Cache = new NativeList<Vector3>(Allocator.Temp);
                context.vector4Cache = new NativeList<Vector4>(Allocator.Temp);
            }

            var builder = new BlobBuilder(Allocator.Temp);

            ref var blobRoot = ref builder.ConstructRoot<MeshSkinningBlob>();
            var     mesh     = context.meshes[postfilterIndex];

            //builder.AllocateFixedString(ref blobRoot.name, meshNames[data.meshIndex]);
            blobRoot.name = name;

            // Vertices
            var verticesToSkin = (VertexToSkin*)builder.Allocate(ref blobRoot.verticesToSkin, mesh.vertexCount).GetUnsafePtr();
            context.vector3Cache.ResizeUninitialized(mesh.vertexCount);
            mesh.GetVertices(context.vector3Cache);
            var t = context.vector3Cache.AsArray().Reinterpret<float3>();
            for (int i = 0; i < mesh.vertexCount; i++)
            {
                verticesToSkin[i].position = t[i];
            }
            mesh.GetNormals(context.vector3Cache);
            for (int i = 0; i < mesh.vertexCount; i++)
            {
                verticesToSkin[i].normal = t[i];
            }
            context.vector4Cache.ResizeUninitialized(mesh.vertexCount);
            mesh.GetTangents(context.vector4Cache);
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
                int verticesInBatch = math.min(1024, mesh.vertexCount - batchIndex * 1024);
                int batchOffset     = batchIndex * 1024;
                int threadsAlive    = verticesInBatch;

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

            return builder.CreateBlobAssetReference<MeshSkinningBlob>(Allocator.Persistent);
        }
    }

    public struct MeshSkinningContext : System.IDisposable
    {
        [ReadOnly] public Mesh.MeshDataArray meshes;

        [NativeDisableContainerSafetyRestriction] public NativeList<Vector3> vector3Cache;
        [NativeDisableContainerSafetyRestriction] public NativeList<Vector4> vector4Cache;

        public void Dispose() => meshes.Dispose();
    }

    public struct MeshSkinningBakeData
    {
        public Mesh sharedMesh;
    }
}

