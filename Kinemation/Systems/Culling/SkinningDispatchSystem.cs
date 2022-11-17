using Latios.Psyshock;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine.Profiling;

namespace Latios.Kinemation.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    public partial class SkinningDispatchSystem : SubSystem
    {
        EntityQuery m_skeletonQuery;

        UnityEngine.ComputeShader m_batchSkinningShader;

        int _dstMats;
        int _dstVertices;
        int _srcVertices;
        int _boneWeights;
        int _bindPoses;
        int _boneOffsets;
        int _metaBuffer;
        int _skeletonMats;
        int _startOffset;
        int _DeformedMeshData;
        int _SkinMatrices;

        CollectMeshMetadataJob m_collectChunkJob;
        WriteBuffersJob        m_writeChunkJob;

        protected override void OnCreate()
        {
            m_skeletonQuery = Fluent.WithAll<DependentSkinnedMesh>(true).WithAll<PerFrameSkeletonBufferMetadata>(false).Build();

            if (UnityEngine.SystemInfo.maxComputeWorkGroupSizeX < 1024)
                m_batchSkinningShader = UnityEngine.Resources.Load<UnityEngine.ComputeShader>("BatchSkinning512");
            else
                m_batchSkinningShader = UnityEngine.Resources.Load<UnityEngine.ComputeShader>("BatchSkinning");

            _dstMats          = UnityEngine.Shader.PropertyToID("_dstMats");
            _dstVertices      = UnityEngine.Shader.PropertyToID("_dstVertices");
            _srcVertices      = UnityEngine.Shader.PropertyToID("_srcVertices");
            _boneWeights      = UnityEngine.Shader.PropertyToID("_boneWeights");
            _bindPoses        = UnityEngine.Shader.PropertyToID("_bindPoses");
            _boneOffsets      = UnityEngine.Shader.PropertyToID("_boneOffsets");
            _metaBuffer       = UnityEngine.Shader.PropertyToID("_metaBuffer");
            _skeletonMats     = UnityEngine.Shader.PropertyToID("_skeletonMats");
            _startOffset      = UnityEngine.Shader.PropertyToID("_startOffset");
            _DeformedMeshData = UnityEngine.Shader.PropertyToID("_DeformedMeshData");
            _SkinMatrices     = UnityEngine.Shader.PropertyToID("_SkinMatrices");

            m_collectChunkJob = new CollectMeshMetadataJob
            {
                entityHandle                         = GetEntityTypeHandle(),
                skinnedMeshesBufferHandle            = GetBufferTypeHandle<DependentSkinnedMesh>(true),
                perFrameMetadataHandle               = GetComponentTypeHandle<PerFrameSkeletonBufferMetadata>(true),
                skeletonCullingMaskHandle            = GetComponentTypeHandle<ChunkPerCameraSkeletonCullingMask>(true),
                boneReferenceBufferHandle            = GetBufferTypeHandle<BoneReference>(true),
                optimizedBoneBufferHandle            = GetBufferTypeHandle<OptimizedBoneToRoot>(true),
                meshPerCameraCullingMaskHandle       = GetComponentTypeHandle<ChunkPerCameraCullingMask>(true),
                meshPerFrameCullingMaskHandle        = GetComponentTypeHandle<ChunkPerFrameCullingMask>(true),
                computeDeformShaderIndexLookup       = GetComponentLookup<ComputeDeformShaderIndex>(true),
                linearBlendSkinningShaderIndexLookup = GetComponentLookup<LinearBlendSkinningShaderIndex>(true),
                esiLookup                            = GetEntityStorageInfoLookup(),
            };

            m_writeChunkJob = new WriteBuffersJob
            {
                skinnedMeshesBufferHandle = m_collectChunkJob.skinnedMeshesBufferHandle,
                boneReferenceBufferHandle = m_collectChunkJob.boneReferenceBufferHandle,
                optimizedBoneBufferHandle = m_collectChunkJob.optimizedBoneBufferHandle,
                ltwLookup                 = GetComponentLookup<LocalToWorld>(true),
                ltwHandle                 = GetComponentTypeHandle<LocalToWorld>(true),
                perFrameMetadataHandle    = GetComponentTypeHandle<PerFrameSkeletonBufferMetadata>(false),
            };
        }

        protected override void OnUpdate()
        {
            Profiler.BeginSample("Setup gather jobs");
            var skeletonChunkCount = m_skeletonQuery.CalculateChunkCountWithoutFiltering();

            var meshDataStream = new NativeStream(skeletonChunkCount, WorldUpdateAllocator);
            var countsArray    = CollectionHelper.CreateNativeArray<CountsElement>(skeletonChunkCount, WorldUpdateAllocator, NativeArrayOptions.UninitializedMemory);

            var boneMatsBufferList = worldBlackboardEntity.GetManagedStructComponent<BoneMatricesPerFrameBuffersManager>();

            var skeletonCountsByBufferByBatch = CollectionHelper.CreateNativeArray<int>(skeletonChunkCount * (boneMatsBufferList.boneMatricesBuffers.Count + 1),
                                                                                        WorldUpdateAllocator,
                                                                                        NativeArrayOptions.ClearMemory);

            m_collectChunkJob.entityHandle.Update(this);
            m_collectChunkJob.skinnedMeshesBufferHandle.Update(this);
            m_collectChunkJob.perFrameMetadataHandle.Update(this);
            m_collectChunkJob.skeletonCullingMaskHandle.Update(this);
            m_collectChunkJob.boneReferenceBufferHandle.Update(this);
            m_collectChunkJob.optimizedBoneBufferHandle.Update(this);
            m_collectChunkJob.meshPerCameraCullingMaskHandle.Update(this);
            m_collectChunkJob.meshPerFrameCullingMaskHandle.Update(this);
            m_collectChunkJob.computeDeformShaderIndexLookup.Update(this);
            m_collectChunkJob.linearBlendSkinningShaderIndexLookup.Update(this);
            m_collectChunkJob.esiLookup.Update(this);
            m_collectChunkJob.meshDataStream                = meshDataStream.AsWriter();
            m_collectChunkJob.countsArray                   = countsArray;
            m_collectChunkJob.skeletonCountsByBufferByBatch = skeletonCountsByBufferByBatch;
            m_collectChunkJob.bufferId                      = boneMatsBufferList.boneMatricesBuffers.Count;

            Dependency = m_collectChunkJob.ScheduleParallelByRef(m_skeletonQuery, Dependency);

            var totalCounts            = new NativeReference<CountsElement>(WorldUpdateAllocator, NativeArrayOptions.UninitializedMemory);
            var countsArrayPrefixSumJH = new PrefixSumCountsJob
            {
                array       = countsArray,
                finalValues = totalCounts
            }.Schedule(Dependency);

            var totalSkeletonCountsByBuffer = CollectionHelper.CreateNativeArray<int>(boneMatsBufferList.boneMatricesBuffers.Count + 1,
                                                                                      WorldUpdateAllocator,
                                                                                      NativeArrayOptions.ClearMemory);
            var skeletonOffsetsByBuffer = CollectionHelper.CreateNativeArray<int>(boneMatsBufferList.boneMatricesBuffers.Count + 1,
                                                                                  WorldUpdateAllocator,
                                                                                  NativeArrayOptions.UninitializedMemory);
            var skeletonCountsByBufferByBatchPrefixSumJH = new PrefixSumPerBufferIdSkeletonCountsJob
            {
                counts          = skeletonCountsByBufferByBatch,
                finalValues     = totalSkeletonCountsByBuffer,
                offsetsByBuffer = skeletonOffsetsByBuffer,
                numberOfBatches = skeletonChunkCount
            }.Schedule(Dependency);

            JobHandle.ScheduleBatchedJobs();

            var pool = worldBlackboardEntity.GetManagedStructComponent<ComputeBufferManager>().pool;
            Profiler.EndSample();

            countsArrayPrefixSumJH.Complete();
            if (totalCounts.Value.skeletonCount == 0)
            {
                // Early exit.
                return;
            }

            Profiler.BeginSample("Setup write jobs");
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
                boneMatsArray = CollectionHelper.CreateNativeArray<float3x4>(0, WorldUpdateAllocator, NativeArrayOptions.UninitializedMemory);
            }

            m_writeChunkJob.skinnedMeshesBufferHandle.Update(this);
            m_writeChunkJob.boneReferenceBufferHandle.Update(this);
            m_writeChunkJob.optimizedBoneBufferHandle.Update(this);
            m_writeChunkJob.ltwLookup.Update(this);
            m_writeChunkJob.ltwHandle.Update(this);
            m_writeChunkJob.perFrameMetadataHandle.Update(this);
            m_writeChunkJob.meshDataStream                = meshDataStream.AsReader();
            m_writeChunkJob.countsArray                   = countsArray;
            m_writeChunkJob.skeletonOffsetsByBuffer       = skeletonOffsetsByBuffer;
            m_writeChunkJob.skeletonCountsByBufferByBatch = skeletonCountsByBufferByBatch;
            m_writeChunkJob.boneMatsBuffer                = boneMatsArray;
            m_writeChunkJob.metaBuffer                    = skinningMetaArray;
            m_writeChunkJob.skeletonCount                 = totalCounts.Value.skeletonCount;
            m_writeChunkJob.bufferId                      = boneMatsBufferList.boneMatricesBuffers.Count;

            Dependency = m_writeChunkJob.ScheduleParallelByRef(m_skeletonQuery, skeletonCountsByBufferByBatchPrefixSumJH);

            JobHandle.ScheduleBatchedJobs();

            // While that heavy job is running, try and do whatever else we need to do in this system so that after we complete the job, we can exit out as fast as possible.
            int verticesRequired          = worldBlackboardEntity.GetComponentData<MaxRequiredDeformVertices>().verticesCount;
            var deformBuffer              = pool.GetDeformBuffer(verticesRequired);
            int matricesRequired          = worldBlackboardEntity.GetComponentData<MaxRequiredLinearBlendMatrices>().matricesCount;
            var linearBlendSkinningBuffer = pool.GetLbsMatsBuffer(matricesRequired);
            var gpuUploadbuffers          = worldBlackboardEntity.GetManagedStructComponent<GpuUploadBuffers>();

            if (boneMatsBuffer != null)
            {
                boneMatsBufferList.boneMatricesBuffers.Add(boneMatsBuffer);
            }

            m_batchSkinningShader.SetBuffer(0, _dstMats,     linearBlendSkinningBuffer);
            m_batchSkinningShader.SetBuffer(0, _dstVertices, deformBuffer);
            m_batchSkinningShader.SetBuffer(0, _srcVertices, gpuUploadbuffers.verticesBuffer);
            m_batchSkinningShader.SetBuffer(0, _boneWeights, gpuUploadbuffers.weightsBuffer);
            m_batchSkinningShader.SetBuffer(0, _bindPoses,   gpuUploadbuffers.bindPosesBuffer);
            m_batchSkinningShader.SetBuffer(0, _boneOffsets, gpuUploadbuffers.boneOffsetsBuffer);
            m_batchSkinningShader.SetBuffer(0, _metaBuffer,  skinningMetaBuffer);

            int boneMatsWriteCount     = totalCounts.Value.boneCount;
            int skinningMetaWriteCount = totalCounts.Value.meshCount * 2 + totalCounts.Value.skeletonCount;
            Profiler.EndSample();

            // Alright. It is go time!
            CompleteDependency();

            //foreach (var metaVal in skinningMetaArray)
            //    UnityEngine.Debug.LogError(metaVal);

            Profiler.BeginSample("Dispatch Compute Shaders");
            if (boneMatsBuffer != null)
                boneMatsBuffer.EndWrite<float3x4>(boneMatsWriteCount);
            skinningMetaBuffer.EndWrite<uint4>(skinningMetaWriteCount);
            for (int bufferId = 0; bufferId < skeletonOffsetsByBuffer.Length; bufferId++)
            {
                int skeletonCount = totalSkeletonCountsByBuffer[bufferId];
                if (skeletonCount <= 0)
                    continue;

                m_batchSkinningShader.SetBuffer(0, _skeletonMats, boneMatsBufferList.boneMatricesBuffers[bufferId]);
                for (int dispatchesRemaining = skeletonCount, offset = skeletonOffsetsByBuffer[bufferId]; dispatchesRemaining > 0;)
                {
                    int dispatchCount = math.min(dispatchesRemaining, 65535);
                    m_batchSkinningShader.SetInt(_startOffset, offset);
                    m_batchSkinningShader.Dispatch(0, dispatchCount, 1, 1);
                    offset              += dispatchCount;
                    dispatchesRemaining -= dispatchCount;
                }
            }
            UnityEngine.Shader.SetGlobalBuffer(_DeformedMeshData, deformBuffer);
            UnityEngine.Shader.SetGlobalBuffer(_SkinMatrices,     linearBlendSkinningBuffer);
            Profiler.EndSample();
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
        struct CollectMeshMetadataJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle                                       entityHandle;
            [ReadOnly] public BufferTypeHandle<DependentSkinnedMesh>                 skinnedMeshesBufferHandle;
            [ReadOnly] public ComponentTypeHandle<PerFrameSkeletonBufferMetadata>    perFrameMetadataHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkPerCameraSkeletonCullingMask> skeletonCullingMaskHandle;

            [ReadOnly] public BufferTypeHandle<BoneReference>       boneReferenceBufferHandle;
            [ReadOnly] public BufferTypeHandle<OptimizedBoneToRoot> optimizedBoneBufferHandle;

            [ReadOnly] public ComponentTypeHandle<ChunkPerCameraCullingMask>  meshPerCameraCullingMaskHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkPerFrameCullingMask>   meshPerFrameCullingMaskHandle;
            [ReadOnly] public ComponentLookup<ComputeDeformShaderIndex>       computeDeformShaderIndexLookup;
            [ReadOnly] public ComponentLookup<LinearBlendSkinningShaderIndex> linearBlendSkinningShaderIndexLookup;
            [ReadOnly] public EntityStorageInfoLookup                         esiLookup;

            [NativeDisableParallelForRestriction] public NativeStream.Writer        meshDataStream;
            [NativeDisableParallelForRestriction] public NativeArray<CountsElement> countsArray;
            [NativeDisableParallelForRestriction] public NativeArray<int>           skeletonCountsByBufferByBatch;

            public int bufferId;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                meshDataStream.BeginForEachIndex(unfilteredChunkIndex);
                int boneCount     = 0;
                int skeletonCount = 0;
                int meshCount     = 0;

                int stride                 = bufferId + 1;
                var skeletonCountsByBuffer = skeletonCountsByBufferByBatch.GetSubArray(stride * unfilteredChunkIndex, stride);
                if (chunk.Has(boneReferenceBufferHandle))
                {
                    ProcessExposed(chunk, ref boneCount, ref skeletonCount, ref meshCount, skeletonCountsByBuffer);
                }
                else if (chunk.Has(optimizedBoneBufferHandle))
                {
                    ProcessOptimized(chunk, ref boneCount, ref skeletonCount, ref meshCount, skeletonCountsByBuffer);
                }

                countsArray[unfilteredChunkIndex] = new CountsElement
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

                    var  storageInfo = esiLookup[meshEntity];
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

                        bool hasComputeDeform = computeDeformShaderIndexLookup.HasComponent(meshEntity);
                        bool hasLinearBlend   = linearBlendSkinningShaderIndexLookup.HasComponent(meshEntity);

                        meshDataStream.Write(new MeshDataStreamElement
                        {
                            computeDeformShaderIndex = hasComputeDeform ? computeDeformShaderIndexLookup[meshEntity].firstVertexIndex : 0,
                            linearBlendShaderIndex   = hasLinearBlend ? linearBlendSkinningShaderIndexLookup[meshEntity].firstMatrixIndex : 0,
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
        struct WriteBuffersJob : IJobChunk
        {
            [ReadOnly] public BufferTypeHandle<DependentSkinnedMesh> skinnedMeshesBufferHandle;

            [ReadOnly] public BufferTypeHandle<BoneReference>       boneReferenceBufferHandle;
            [ReadOnly] public BufferTypeHandle<OptimizedBoneToRoot> optimizedBoneBufferHandle;

            [ReadOnly] public ComponentLookup<LocalToWorld>     ltwLookup;
            [ReadOnly] public ComponentTypeHandle<LocalToWorld> ltwHandle;

            [ReadOnly] public NativeStream.Reader        meshDataStream;
            [ReadOnly] public NativeArray<CountsElement> countsArray;

            [ReadOnly] public NativeArray<int> skeletonOffsetsByBuffer;

            public ComponentTypeHandle<PerFrameSkeletonBufferMetadata> perFrameMetadataHandle;

            [NativeDisableParallelForRestriction] public NativeArray<int>      skeletonCountsByBufferByBatch;
            [NativeDisableParallelForRestriction] public NativeArray<float3x4> boneMatsBuffer;
            [NativeDisableParallelForRestriction] public NativeArray<uint4>    metaBuffer;

            public int skeletonCount;
            public int bufferId;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                int count = meshDataStream.BeginForEachIndex(unfilteredChunkIndex);
                if (count == 0)
                {
                    meshDataStream.EndForEachIndex();
                    return;
                }

                var countsElement          = countsArray[unfilteredChunkIndex];
                int stride                 = bufferId + 1;
                var skeletonCountsByBuffer = skeletonCountsByBufferByBatch.GetSubArray(stride * unfilteredChunkIndex, stride);

                if (chunk.Has(boneReferenceBufferHandle))
                {
                    ProcessExposed(chunk, countsElement, count, skeletonCountsByBuffer);
                }
                else if (chunk.Has(optimizedBoneBufferHandle))
                {
                    ProcessOptimized(chunk, countsElement, count, skeletonCountsByBuffer);
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
                            var boneToWorld                = ltwLookup[entity].Value;
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

