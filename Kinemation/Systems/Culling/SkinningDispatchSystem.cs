using Latios.Psyshock;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

namespace Latios.Kinemation.Systems
{
    [DisableAutoCreation]
    public partial class SkinningDispatchSystem : SubSystem
    {
        EntityQuery m_skeletonQuery;

        UnityEngine.ComputeShader m_batchSkinningShader;

        protected override void OnCreate()
        {
            m_skeletonQuery = Fluent.WithAll<DependentSkinnedMesh>(true).WithAll<PerFrameSkeletonBufferMetadata>(false).Build();

            m_batchSkinningShader = UnityEngine.Resources.Load<UnityEngine.ComputeShader>("BatchSkinning");
        }

        protected override void OnUpdate()
        {
            var skeletonChunkCount = m_skeletonQuery.CalculateChunkCountWithoutFiltering();

            var skinnedMeshesBufferHandle = GetBufferTypeHandle<DependentSkinnedMesh>(true);
            var boneReferenceBufferHandle = GetBufferTypeHandle<BoneReference>(true);
            var optimizedBoneBufferHandle = GetBufferTypeHandle<OptimizedBoneToRoot>(true);

            var meshDataStream = new NativeStream(skeletonChunkCount, Allocator.TempJob);
            var countsArray    = new NativeArray<CountsElement>(skeletonChunkCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            var boneMatsBufferList = worldBlackboardEntity.GetCollectionComponent<BoneMatricesPerFrameBuffersManager>(false, out var boneMatsBufferJH);
            boneMatsBufferJH.Complete();

            var skeletonCountsByBufferByBatch = new NativeArray<int>(skeletonChunkCount * (boneMatsBufferList.boneMatricesBuffers.Count + 1),
                                                                     Allocator.TempJob,
                                                                     NativeArrayOptions.ClearMemory);

            Dependency = new CollectMeshMetadataJob
            {
                entityHandle                       = GetEntityTypeHandle(),
                skinnedMeshesBufferHandle          = skinnedMeshesBufferHandle,
                perFrameMetadataHandle             = GetComponentTypeHandle<PerFrameSkeletonBufferMetadata>(true),
                skeletonCullingMaskHandle          = GetComponentTypeHandle<ChunkPerCameraSkeletonCullingMask>(true),
                boneReferenceBufferHandle          = boneReferenceBufferHandle,
                optimizedBoneBufferHandle          = optimizedBoneBufferHandle,
                meshPerCameraCullingMaskHandle     = GetComponentTypeHandle<ChunkPerCameraCullingMask>(true),
                meshPerFrameCullingMaskHandle      = GetComponentTypeHandle<ChunkPerFrameCullingMask>(true),
                computeDeformShaderIndexCdfe       = GetComponentDataFromEntity<ComputeDeformShaderIndex>(true),
                linearBlendSkinningShaderIndexCdfe = GetComponentDataFromEntity<LinearBlendSkinningShaderIndex>(true),
                sife                               = GetStorageInfoFromEntity(),
                meshDataStream                     = meshDataStream.AsWriter(),
                countsArray                        = countsArray,
                skeletonCountsByBufferByBatch      = skeletonCountsByBufferByBatch,
                bufferId                           = boneMatsBufferList.boneMatricesBuffers.Count
            }.ScheduleParallel(m_skeletonQuery, Dependency);

            var totalCounts            = new NativeReference<CountsElement>(Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var countsArrayPrefixSumJH = new PrefixSumCountsJob
            {
                array       = countsArray,
                finalValues = totalCounts
            }.Schedule(Dependency);

            var totalSkeletonCountsByBuffer =
                new NativeArray<int>(boneMatsBufferList.boneMatricesBuffers.Count + 1, Allocator.TempJob, NativeArrayOptions.ClearMemory);
            var skeletonOffsetsByBuffer = new NativeArray<int>(boneMatsBufferList.boneMatricesBuffers.Count + 1,
                                                               Allocator.TempJob,
                                                               NativeArrayOptions.UninitializedMemory);
            var skeletonCountsByBufferByBatchPrefixSumJH = new PrefixSumPerBufferIdSkeletonCountsJob
            {
                counts          = skeletonCountsByBufferByBatch,
                finalValues     = totalSkeletonCountsByBuffer,
                offsetsByBuffer = skeletonOffsetsByBuffer,
                numberOfBatches = skeletonChunkCount
            }.Schedule(Dependency);

            JobHandle.ScheduleBatchedJobs();

            var pool = worldBlackboardEntity.GetCollectionComponent<ComputeBufferManager>(false, out var poolJH).pool;
            poolJH.Complete();

            countsArrayPrefixSumJH.Complete();
            if (totalCounts.Value.skeletonCount == 0)
            {
                // Cleanup and early exit.
                var dependencyList = new NativeList<JobHandle>(6, Allocator.Temp);
                dependencyList.Add(meshDataStream.Dispose(default));
                dependencyList.Add(countsArray.Dispose(default));
                dependencyList.Add(skeletonCountsByBufferByBatch.Dispose(skeletonCountsByBufferByBatchPrefixSumJH));
                dependencyList.Add(totalCounts.Dispose(default));
                dependencyList.Add(totalSkeletonCountsByBuffer.Dispose(skeletonCountsByBufferByBatchPrefixSumJH));
                dependencyList.Add(skeletonOffsetsByBuffer.Dispose(skeletonCountsByBufferByBatchPrefixSumJH));
                Dependency = JobHandle.CombineDependencies(dependencyList);
                return;
            }

            var                       skinningMetaBuffer = pool.GetSkinningMetaBuffer(totalCounts.Value.meshCount * 2 + totalCounts.Value.skeletonCount);
            var                       skinningMetaArray  = skinningMetaBuffer.BeginWrite<uint4>(0, totalCounts.Value.meshCount * 2 + totalCounts.Value.skeletonCount);
            NativeArray<float3x4>     boneMatsArray;
            UnityEngine.ComputeBuffer boneMatsBuffer = null;

            if (totalCounts.Value.boneCount > 0)
            {
                boneMatsBuffer = pool.GetBonesBuffer(totalCounts.Value.boneCount);
                boneMatsArray  = boneMatsBuffer.BeginWrite<float3x4>(0, totalCounts.Value.boneCount);
            }
            else
            {
                boneMatsArray = new NativeArray<float3x4>(0, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            }

            Dependency = new WriteBuffersJob
            {
                skinnedMeshesBufferHandle     = skinnedMeshesBufferHandle,
                boneReferenceBufferHandle     = boneReferenceBufferHandle,
                optimizedBoneBufferHandle     = optimizedBoneBufferHandle,
                ltwCdfe                       = GetComponentDataFromEntity<LocalToWorld>(true),
                ltwHandle                     = GetComponentTypeHandle<LocalToWorld>(true),
                meshDataStream                = meshDataStream.AsReader(),
                countsArray                   = countsArray,
                skeletonOffsetsByBuffer       = skeletonOffsetsByBuffer,
                perFrameMetadataHandle        = GetComponentTypeHandle<PerFrameSkeletonBufferMetadata>(false),
                skeletonCountsByBufferByBatch = skeletonCountsByBufferByBatch,
                boneMatsBuffer                = boneMatsArray,
                metaBuffer                    = skinningMetaArray,
                skeletonCount                 = totalCounts.Value.skeletonCount,
                bufferId                      = boneMatsBufferList.boneMatricesBuffers.Count,
            }.ScheduleParallel(m_skeletonQuery, skeletonCountsByBufferByBatchPrefixSumJH);

            JobHandle.ScheduleBatchedJobs();

            // While that heavy job is running, try and do whatever else we need to do in this system so that after we complete the job, we can exit out as fast as possible.
            int verticesRequired          = worldBlackboardEntity.GetComponentData<MaxRequiredDeformVertices>().verticesCount;
            var deformBuffer              = pool.GetDeformBuffer(verticesRequired);
            int matricesRequired          = worldBlackboardEntity.GetComponentData<MaxRequiredLinearBlendMatrices>().matricesCount;
            var linearBlendSkinningBuffer = pool.GetLbsMatsBuffer(matricesRequired);
            var gpuUploadbuffers          = worldBlackboardEntity.GetCollectionComponent<GpuUploadBuffers>(false, out var gpuUploadBuffersJH);

            var disposeDependencies = new NativeList<JobHandle>(Allocator.Temp);
            disposeDependencies.Add(meshDataStream.Dispose(Dependency));
            disposeDependencies.Add(countsArray.Dispose(Dependency));
            disposeDependencies.Add(skeletonCountsByBufferByBatch.Dispose(Dependency));
            if (boneMatsBuffer == null)
            {
                disposeDependencies.Add(boneMatsArray.Dispose(Dependency));
            }
            else
            {
                boneMatsBufferList.boneMatricesBuffers.Add(boneMatsBuffer);
            }

            gpuUploadBuffersJH.Complete();
            m_batchSkinningShader.SetBuffer(0, "_dstMats",     linearBlendSkinningBuffer);
            m_batchSkinningShader.SetBuffer(0, "_dstVertices", deformBuffer);
            m_batchSkinningShader.SetBuffer(0, "_srcVertices", gpuUploadbuffers.verticesBuffer);
            m_batchSkinningShader.SetBuffer(0, "_boneWeights", gpuUploadbuffers.weightsBuffer);
            m_batchSkinningShader.SetBuffer(0, "_bindPoses",   gpuUploadbuffers.bindPosesBuffer);
            m_batchSkinningShader.SetBuffer(0, "_boneOffsets", gpuUploadbuffers.boneOffsetsBuffer);
            m_batchSkinningShader.SetBuffer(0, "_metaBuffer",  skinningMetaBuffer);

            int boneMatsWriteCount     = totalCounts.Value.boneCount;
            int skinningMetaWriteCount = totalCounts.Value.meshCount * 2 + totalCounts.Value.skeletonCount;
            totalCounts.Dispose();

            // Alright. It is go time!
            gpuUploadBuffersJH.Complete();
            CompleteDependency();

            //foreach (var metaVal in skinningMetaArray)
            //    UnityEngine.Debug.LogError(metaVal);

            if (boneMatsBuffer != null)
                boneMatsBuffer.EndWrite<float3x4>(boneMatsWriteCount);
            skinningMetaBuffer.EndWrite<uint4>(skinningMetaWriteCount);
            for (int bufferId = 0; bufferId < skeletonOffsetsByBuffer.Length; bufferId++)
            {
                int skeletonCount = totalSkeletonCountsByBuffer[bufferId];
                if (skeletonCount <= 0)
                    continue;

                m_batchSkinningShader.SetBuffer(0, "_skeletonMats", boneMatsBufferList.boneMatricesBuffers[bufferId]);
                for (int dispatchesRemaining = skeletonCount, offset = skeletonOffsetsByBuffer[bufferId]; dispatchesRemaining > 0;)
                {
                    int dispatchCount = math.min(dispatchesRemaining, 65535);
                    m_batchSkinningShader.SetInt("_startOffset", offset);
                    m_batchSkinningShader.Dispatch(0, dispatchCount, 1, 1);
                    offset              += dispatchCount;
                    dispatchesRemaining -= dispatchCount;
                    //UnityEngine.Debug.Log($"Dispatching skinning dispatchCount: {dispatchCount}");
                }
            }
            UnityEngine.Shader.SetGlobalBuffer("_DeformedMeshData", deformBuffer);
            UnityEngine.Shader.SetGlobalBuffer("_SkinMatrices",     linearBlendSkinningBuffer);

            disposeDependencies.Add(totalSkeletonCountsByBuffer.Dispose(default));
            disposeDependencies.Add(skeletonOffsetsByBuffer.Dispose(default));
            Dependency = JobHandle.CombineDependencies(disposeDependencies);
        }

        struct MeshDataStreamHeader
        {
            public int indexInSkeletonChunk;
            public int meshCount;
        }

        struct MeshDataStreamElement
        {
            public int  indexInDependentBuffer;
            public uint computeDeformShaderIndex;
            public int  linearBlendShaderIndex;
            public uint operationsCode;

            public const uint linearBlendOpCode            = 1;
            public const uint computeSkinningFromSrcOpCode = 2;
            public const uint computeSkinningFromDstOpCode = 4;
        }

        struct CountsElement
        {
            public int boneCount;  // For new bufferId
            public int skeletonCount;  // For all bufferIds
            public int meshCount;  // For all bufferIds
        }

        [BurstCompile]
        struct CollectMeshMetadataJob : IJobEntityBatch
        {
            [ReadOnly] public EntityTypeHandle                                       entityHandle;
            [ReadOnly] public BufferTypeHandle<DependentSkinnedMesh>                 skinnedMeshesBufferHandle;
            [ReadOnly] public ComponentTypeHandle<PerFrameSkeletonBufferMetadata>    perFrameMetadataHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkPerCameraSkeletonCullingMask> skeletonCullingMaskHandle;

            [ReadOnly] public BufferTypeHandle<BoneReference>       boneReferenceBufferHandle;
            [ReadOnly] public BufferTypeHandle<OptimizedBoneToRoot> optimizedBoneBufferHandle;

            [ReadOnly] public ComponentTypeHandle<ChunkPerCameraCullingMask>          meshPerCameraCullingMaskHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkPerFrameCullingMask>           meshPerFrameCullingMaskHandle;
            [ReadOnly] public ComponentDataFromEntity<ComputeDeformShaderIndex>       computeDeformShaderIndexCdfe;
            [ReadOnly] public ComponentDataFromEntity<LinearBlendSkinningShaderIndex> linearBlendSkinningShaderIndexCdfe;
            [ReadOnly] public StorageInfoFromEntity                                   sife;

            [NativeDisableParallelForRestriction] public NativeStream.Writer        meshDataStream;
            [NativeDisableParallelForRestriction] public NativeArray<CountsElement> countsArray;
            [NativeDisableParallelForRestriction] public NativeArray<int>           skeletonCountsByBufferByBatch;

            public int bufferId;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                meshDataStream.BeginForEachIndex(batchIndex);
                int boneCount     = 0;
                int skeletonCount = 0;
                int meshCount     = 0;

                int stride                 = bufferId + 1;
                var skeletonCountsByBuffer = skeletonCountsByBufferByBatch.GetSubArray(stride * batchIndex, stride);
                if (batchInChunk.Has(boneReferenceBufferHandle))
                {
                    ProcessExposed(batchInChunk, ref boneCount, ref skeletonCount, ref meshCount, skeletonCountsByBuffer);
                }
                else if (batchInChunk.Has(optimizedBoneBufferHandle))
                {
                    ProcessOptimized(batchInChunk, ref boneCount, ref skeletonCount, ref meshCount, skeletonCountsByBuffer);
                }

                countsArray[batchIndex] = new CountsElement
                {
                    boneCount     = boneCount,
                    skeletonCount = skeletonCount,
                    meshCount     = meshCount
                };
                meshDataStream.EndForEachIndex();
            }

            void ProcessExposed(ArchetypeChunk batchInChunk, ref int batchBoneCount, ref int skeletonCount, ref int meshCount, NativeArray<int> skeletonCountsByBuffer)
            {
                var entityArray           = batchInChunk.GetNativeArray(entityHandle);
                var skinnedMeshesAccessor = batchInChunk.GetBufferAccessor(skinnedMeshesBufferHandle);
                var boneBufferAccessor    = batchInChunk.GetBufferAccessor(boneReferenceBufferHandle);
                var perFrameMetadataArray = batchInChunk.GetNativeArray(perFrameMetadataHandle);
                var skeletonCullingMask   = batchInChunk.GetChunkComponentData(skeletonCullingMaskHandle);

                for (int i = 0; i < batchInChunk.Count; i++)
                {
                    var mask = i >= 64 ? skeletonCullingMask.upper : skeletonCullingMask.lower;
                    if (mask.IsSet(i % 64))
                    {
                        if (CollectMeshData(entityArray[i], skinnedMeshesAccessor[i].AsNativeArray(), ref meshCount, i, boneBufferAccessor[i].Length))
                        {
                            skeletonCount++;
                            if ( perFrameMetadataArray[i].bufferId < 0)
                            {
                                batchBoneCount += boneBufferAccessor[i].Length;
                                skeletonCountsByBuffer[bufferId]++;
                            }
                            else
                            {
                                skeletonCountsByBuffer[perFrameMetadataArray[i].bufferId]++;
                            }
                        }
                    }
                }
            }

            void ProcessOptimized(ArchetypeChunk batchInChunk, ref int batchBoneCount, ref int skeletonCount, ref int meshCount, NativeArray<int> skeletonCountsByBuffer)
            {
                var entityArray           = batchInChunk.GetNativeArray(entityHandle);
                var skinnedMeshesAccessor = batchInChunk.GetBufferAccessor(skinnedMeshesBufferHandle);
                var boneBufferAccessor    = batchInChunk.GetBufferAccessor(optimizedBoneBufferHandle);
                var perFrameMetadataArray = batchInChunk.GetNativeArray(perFrameMetadataHandle);
                var skeletonCullingMask   = batchInChunk.GetChunkComponentData(skeletonCullingMaskHandle);

                for (int i = 0; i < batchInChunk.Count; i++)
                {
                    var mask = i >= 64 ? skeletonCullingMask.upper : skeletonCullingMask.lower;
                    if (mask.IsSet(i % 64))
                    {
                        if (CollectMeshData(entityArray[i], skinnedMeshesAccessor[i].AsNativeArray(), ref meshCount, i, boneBufferAccessor.Length))
                        {
                            skeletonCount++;
                            if (perFrameMetadataArray[i].bufferId < 0)
                            {
                                batchBoneCount += boneBufferAccessor[i].Length;
                                skeletonCountsByBuffer[bufferId]++;
                            }
                            else
                            {
                                skeletonCountsByBuffer[perFrameMetadataArray[i].bufferId]++;
                            }
                        }
                    }
                }
            }

            // Returns true if new meshes need skinning.
            // Already skinned meshes will update the component but not add to the totals nor require any further processing.
            unsafe bool CollectMeshData(Entity skeletonEntity, NativeArray<DependentSkinnedMesh> meshes, ref int meshCount, int indexInBatch, int skeletonBonesCount)
            {
                MeshDataStreamHeader* header = null;

                for (int i = 0; i < meshes.Length; i++)
                {
                    var meshEntity = meshes[i].skinnedMesh;

                    if (skeletonBonesCount + meshes[i].meshBindPosesCount > 682)
                    {
                        UnityEngine.Debug.LogError(
                            $"Skeleton entity {skeletonEntity} has {skeletonBonesCount} bones. Skinned mesh entity {meshEntity} has {meshes[i].meshBindPosesCount} bone references. The sum of these exceed the max shader capacity of 682.");
                        continue;
                    }

                    var  storageInfo = sife[meshEntity];
                    var  cameraMask  = storageInfo.Chunk.GetChunkComponentData(meshPerCameraCullingMaskHandle);
                    var  frameMask   = storageInfo.Chunk.GetChunkComponentData(meshPerFrameCullingMaskHandle);
                    bool isNewMesh   = false;
                    if (storageInfo.IndexInChunk >= 64)
                    {
                        cameraMask.upper.Value &= ~frameMask.upper.Value;
                        isNewMesh               = cameraMask.upper.IsSet(storageInfo.IndexInChunk - 64);
                    }
                    else
                    {
                        cameraMask.lower.Value &= ~frameMask.lower.Value;
                        isNewMesh               = cameraMask.lower.IsSet(storageInfo.IndexInChunk);
                    }

                    if (isNewMesh)
                    {
                        if (header == null)
                        {
                            header                       = (MeshDataStreamHeader*)UnsafeUtility.AddressOf(ref meshDataStream.Allocate<MeshDataStreamHeader>());
                            header->indexInSkeletonChunk = indexInBatch;
                            header->meshCount            = 0;
                        }

                        bool hasComputeDeform = computeDeformShaderIndexCdfe.HasComponent(meshEntity);
                        bool hasLinearBlend   = linearBlendSkinningShaderIndexCdfe.HasComponent(meshEntity);

                        meshDataStream.Write(new MeshDataStreamElement
                        {
                            computeDeformShaderIndex = hasComputeDeform ? computeDeformShaderIndexCdfe[meshEntity].firstVertexIndex : 0,
                            linearBlendShaderIndex   = hasLinearBlend ? linearBlendSkinningShaderIndexCdfe[meshEntity].firstMatrixIndex : 0,
                            indexInDependentBuffer   = i,
                            operationsCode           = math.select(0, MeshDataStreamElement.linearBlendOpCode, hasLinearBlend) +
                                                       math.select(0, MeshDataStreamElement.computeSkinningFromSrcOpCode, hasComputeDeform)
                        });
                        header->meshCount++;
                        meshCount++;
                    }
                }

                return header != null;
            }
        }

        [BurstCompile]
        struct PrefixSumCountsJob : IJob
        {
            public NativeArray<CountsElement>     array;
            public NativeReference<CountsElement> finalValues;

            public void Execute()
            {
                CountsElement running = default;
                for (int i = 0; i < array.Length; i++)
                {
                    var temp               = array[i];
                    array[i]               = running;
                    running.boneCount     += temp.boneCount;
                    running.skeletonCount += temp.skeletonCount;
                    running.meshCount     += temp.meshCount;
                }

                finalValues.Value = running;
            }
        }

        [BurstCompile]
        struct PrefixSumPerBufferIdSkeletonCountsJob : IJob
        {
            public NativeArray<int> counts;
            public NativeArray<int> finalValues;
            public NativeArray<int> offsetsByBuffer;
            public int              numberOfBatches;

            public void Execute()
            {
                int stride = finalValues.Length;
                var temp   = new NativeArray<int>(stride, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                for (int i = 0; i < numberOfBatches; i++)
                {
                    NativeArray<int>.Copy(counts, i * stride, temp, 0, stride);
                    NativeArray<int>.Copy(finalValues, 0, counts, i * stride, stride);
                    for (int j = 0; j < stride; j++)
                    {
                        finalValues[j] += temp[j];
                    }
                }

                int offset = 0;
                for (int i = 0; i < stride; i++)
                {
                    offsetsByBuffer[i]  = offset;
                    offset             += finalValues[i];
                }
            }
        }

        [BurstCompile]
        struct WriteBuffersJob : IJobEntityBatch
        {
            [ReadOnly] public BufferTypeHandle<DependentSkinnedMesh> skinnedMeshesBufferHandle;

            [ReadOnly] public BufferTypeHandle<BoneReference>       boneReferenceBufferHandle;
            [ReadOnly] public BufferTypeHandle<OptimizedBoneToRoot> optimizedBoneBufferHandle;

            [ReadOnly] public ComponentDataFromEntity<LocalToWorld> ltwCdfe;
            [ReadOnly] public ComponentTypeHandle<LocalToWorld>     ltwHandle;

            [ReadOnly] public NativeStream.Reader        meshDataStream;
            [ReadOnly] public NativeArray<CountsElement> countsArray;

            [ReadOnly] public NativeArray<int> skeletonOffsetsByBuffer;

            public ComponentTypeHandle<PerFrameSkeletonBufferMetadata> perFrameMetadataHandle;

            [NativeDisableParallelForRestriction] public NativeArray<int>      skeletonCountsByBufferByBatch;
            [NativeDisableParallelForRestriction] public NativeArray<float3x4> boneMatsBuffer;
            [NativeDisableParallelForRestriction] public NativeArray<uint4>    metaBuffer;

            public int skeletonCount;
            public int bufferId;

            public void Execute(ArchetypeChunk batchInChunk, int batchIndex)
            {
                int count = meshDataStream.BeginForEachIndex(batchIndex);
                if (count == 0)
                {
                    meshDataStream.EndForEachIndex();
                    return;
                }

                var countsElement          = countsArray[batchIndex];
                int stride                 = bufferId + 1;
                var skeletonCountsByBuffer = skeletonCountsByBufferByBatch.GetSubArray(stride * batchIndex, stride);

                if (batchInChunk.Has(boneReferenceBufferHandle))
                {
                    ProcessExposed(batchInChunk, countsElement, count, skeletonCountsByBuffer);
                }
                else if (batchInChunk.Has(optimizedBoneBufferHandle))
                {
                    ProcessOptimized(batchInChunk, countsElement, count, skeletonCountsByBuffer);
                }

                meshDataStream.EndForEachIndex();
            }

            void ProcessExposed(ArchetypeChunk batchInChunk, CountsElement countsElement, int streamWriteCount, NativeArray<int> skeletonCountsByBuffer)
            {
                int boneOffset = countsElement.boneCount;
                int meshOffset = countsElement.meshCount * 2 + skeletonCount;

                var perFrameMetaArray = batchInChunk.GetNativeArray(perFrameMetadataHandle);
                var meshesAccessor    = batchInChunk.GetBufferAccessor(skinnedMeshesBufferHandle);

                var bonesAccessor = batchInChunk.GetBufferAccessor(boneReferenceBufferHandle);
                var skeletonLtws  = batchInChunk.GetNativeArray(ltwHandle);

                for (int streamWrites = 0; streamWrites < streamWriteCount;)
                {
                    var header = meshDataStream.Read<MeshDataStreamHeader>();
                    streamWrites++;

                    var bones = bonesAccessor[header.indexInSkeletonChunk].AsNativeArray();

                    bool alreadyUploaded = perFrameMetaArray[header.indexInSkeletonChunk].bufferId >= 0;
                    int  targetBuffer    = math.select(bufferId, perFrameMetaArray[header.indexInSkeletonChunk].bufferId, alreadyUploaded);
                    int  skeletonIndex   = skeletonCountsByBuffer[targetBuffer] + skeletonOffsetsByBuffer[targetBuffer];
                    skeletonCountsByBuffer[targetBuffer]++;
                    metaBuffer[skeletonIndex] = new uint4
                    {
                        x = (uint)boneOffset,
                        y = (uint)bones.Length,
                        z = (uint)meshOffset,
                        w = (uint)header.meshCount
                    };

                    if (!alreadyUploaded)
                    {
                        float4x4 worldToRoot = math.inverse(skeletonLtws[header.indexInSkeletonChunk].Value);
                        for (int i = 0; i < bones.Length; i++)
                        {
                            var entity                     = bones[i].bone;
                            var boneToWorld                = ltwCdfe[entity].Value;
                            var boneToRoot                 = math.mul(worldToRoot, boneToWorld);
                            boneMatsBuffer[boneOffset + i] = Shrink(boneToRoot);
                        }

                        boneOffset += bones.Length;
                    }

                    var meshes = meshesAccessor[header.indexInSkeletonChunk].AsNativeArray();
                    ProcessMeshes(meshes, header.meshCount, ref meshOffset, ref streamWrites);
                }
            }

            void ProcessOptimized(ArchetypeChunk batchInChunk, CountsElement countsElement, int streamWriteCount, NativeArray<int> skeletonCountsByBuffer)
            {
                int boneOffset = countsElement.boneCount;
                int meshOffset = countsElement.meshCount * 2 + skeletonCount;

                var perFrameMetaArray = batchInChunk.GetNativeArray(perFrameMetadataHandle);
                var meshesAccessor    = batchInChunk.GetBufferAccessor(skinnedMeshesBufferHandle);

                var bonesAccessor = batchInChunk.GetBufferAccessor(optimizedBoneBufferHandle);

                for (int streamWrites = 0; streamWrites < streamWriteCount;)
                {
                    var header = meshDataStream.Read<MeshDataStreamHeader>();
                    streamWrites++;

                    var bones = bonesAccessor[header.indexInSkeletonChunk].AsNativeArray();

                    bool alreadyUploaded = perFrameMetaArray[header.indexInSkeletonChunk].bufferId >= 0;
                    int  targetBuffer    = math.select(bufferId, perFrameMetaArray[header.indexInSkeletonChunk].bufferId, alreadyUploaded);
                    int  skeletonIndex   = skeletonCountsByBuffer[targetBuffer] + skeletonOffsetsByBuffer[targetBuffer];
                    skeletonCountsByBuffer[targetBuffer]++;
                    metaBuffer[skeletonIndex] = new uint4
                    {
                        x = (uint)boneOffset,
                        y = (uint)bones.Length,
                        z = (uint)meshOffset,
                        w = (uint)header.meshCount
                    };

                    if (!alreadyUploaded)
                    {
                        for (int i = 0; i < bones.Length; i++)
                        {
                            boneMatsBuffer[boneOffset + i] = Shrink(bones[i].boneToRoot);
                        }
                        boneOffset += bones.Length;
                    }

                    var meshes = meshesAccessor[header.indexInSkeletonChunk].AsNativeArray();
                    ProcessMeshes(meshes, header.meshCount, ref meshOffset, ref streamWrites);
                }
            }

            void ProcessMeshes(NativeArray<DependentSkinnedMesh> meshes, int meshCount, ref int meshOffset, ref int streamWrites)
            {
                for (int i = 0; i < meshCount; i++)
                {
                    var element = meshDataStream.Read<MeshDataStreamElement>();
                    streamWrites++;

                    var mesh               = meshes[element.indexInDependentBuffer];
                    metaBuffer[meshOffset] = new uint4
                    {
                        x = element.operationsCode | ((uint)mesh.meshBindPosesCount << 16),
                        y = (uint)mesh.meshBindPosesStart,
                        z = (uint)mesh.boneOffsetsStart,
                        w = (uint)element.linearBlendShaderIndex
                    };
                    meshOffset++;
                    metaBuffer[meshOffset] = new uint4
                    {
                        x = (uint)mesh.meshVerticesStart,
                        y = (uint)mesh.meshVerticesCount,
                        z = (uint)mesh.meshWeightsStart,
                        w = element.computeDeformShaderIndex
                    };
                    meshOffset++;
                }
            }

            float3x4 Shrink(float4x4 a)
            {
                return new float3x4(a.c0.xyz, a.c1.xyz, a.c2.xyz, a.c3.xyz);
            }
        }
    }
}

