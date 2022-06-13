using Latios;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine.Profiling;

namespace Latios.Kinemation.Systems
{
    [DisableAutoCreation]
    public partial class UploadMaterialPropertiesSystem : SubSystem
    {
        EntityQuery m_metaQuery;

        UnityEngine.ComputeBuffer m_GPUPersistentInstanceData;
        int                       m_persistentInstanceDataSize = 32 * 1024 * 1024;

        const int              kGPUUploaderChunkSize = 4 * 1024 * 1024;
        SparseUploader         m_GPUUploader;
        ThreadedSparseUploader m_ThreadedGPUUploader;

#if DEBUG_LOG_MEMORY_USAGE
        private static ulong PrevUsedSpace = 0;
#endif

        protected override void OnCreate()
        {
            m_metaQuery = Fluent.WithAll<HybridChunkInfo>(false).WithAll<ChunkHeader>(true).Build();

            m_GPUPersistentInstanceData = new UnityEngine.ComputeBuffer(
                m_persistentInstanceDataSize / 4,
                4,
                UnityEngine.ComputeBufferType.Raw);
            m_GPUUploader = new SparseUploader(m_GPUPersistentInstanceData, kGPUUploaderChunkSize);
        }

        protected override unsafe void OnUpdate()
        {
            Profiler.BeginSample("GetBlackboardData");
            var context               = worldBlackboardEntity.GetCollectionComponent<MaterialPropertiesUploadContext>(false);
            var materialPropertyTypes = worldBlackboardEntity.GetBuffer<MaterialPropertyComponentType>(true);
            Profiler.EndSample();

            // Conservative estimate is that every known type is in every chunk. There will be
            // at most one operation per type per chunk, which will be either an actual
            // chunk data upload, or a default value blit (a single type should not have both).
            int conservativeMaximumGpuUploads = context.hybridRenderedChunkCount * materialPropertyTypes.Length;
            var gpuUploadOperations           = new NativeArray<GpuUploadOperation>(
                conservativeMaximumGpuUploads,
                Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);
            var numGpuUploadOperations = new NativeReference<int>(
                Allocator.TempJob,
                NativeArrayOptions.ClearMemory);

            Profiler.BeginSample("GetTypeHandles");
            context.componentTypeCache.FetchTypeHandles(this);
            var componentTypes = context.componentTypeCache.ToBurstCompatible(Allocator.TempJob);
            Profiler.EndSample();

            Dependency = new ComputeOperationsJob
            {
                ChunkHeader                     = GetComponentTypeHandle<ChunkHeader>(true),
                ChunkProperties                 = context.chunkProperties,
                chunkPropertyDirtyMaskHandle    = GetComponentTypeHandle<ChunkMaterialPropertyDirtyMask>(false),
                chunkPerCameraCullingMaskHandle = GetComponentTypeHandle<ChunkPerCameraCullingMask>(true),
                ComponentTypes                  = componentTypes,
                GpuUploadOperations             = gpuUploadOperations,
                HybridChunkInfo                 = GetComponentTypeHandle<HybridChunkInfo>(true),
                LocalToWorldType                = TypeManager.GetTypeIndex<LocalToWorld>(),
                NumGpuUploadOperations          = numGpuUploadOperations,
                PrevLocalToWorldType            = TypeManager.GetTypeIndex<BuiltinMaterialPropertyUnity_MatrixPreviousM>(),
                PrevWorldToLocalType            = TypeManager.GetTypeIndex<BuiltinMaterialPropertyUnity_MatrixPreviousMI_Tag>(),
                WorldToLocalType                = TypeManager.GetTypeIndex<WorldToLocal_Tag>(),
            }.ScheduleParallel(m_metaQuery, Dependency);
            CompleteDependency();
            componentTypes.TypeIndexToArrayIndex.Dispose();
            Dependency = default;

            UnityEngine.Debug.Assert(numGpuUploadOperations.Value <= gpuUploadOperations.Length, "Maximum GPU upload operation count exceeded");

            ComputeUploadSizeRequirements(
                numGpuUploadOperations.Value, gpuUploadOperations, context.defaultValueBlits,
                out int numOperations, out int totalUploadBytes, out int biggestUploadBytes);

#if DEBUG_LOG_UPLOADS
            if (numOperations > 0)
            {
                Debug.Log($"GPU upload operations: {numOperations}, GPU upload bytes: {totalUploadBytes}");
            }
#endif

            // BeginUpdate()
            Profiler.BeginSample("StartUpdate");
            if (context.requiredPersistentBufferSize != m_persistentInstanceDataSize)
            {
                m_persistentInstanceDataSize = context.requiredPersistentBufferSize;

                var newBuffer = new UnityEngine.ComputeBuffer(
                    m_persistentInstanceDataSize / 4,
                    4,
                    UnityEngine.ComputeBufferType.Raw);
                m_GPUUploader.ReplaceBuffer(newBuffer, true);

                if (m_GPUPersistentInstanceData != null)
                    m_GPUPersistentInstanceData.Dispose();
                m_GPUPersistentInstanceData = newBuffer;
            }

            m_ThreadedGPUUploader = m_GPUUploader.Begin(totalUploadBytes, biggestUploadBytes, numOperations);
            Profiler.EndSample();

            new ExecuteGpuUploads
            {
                GpuUploadOperations    = gpuUploadOperations,
                ThreadedSparseUploader = m_ThreadedGPUUploader,
            }.Schedule(numGpuUploadOperations.Value, 1).Complete();
            numGpuUploadOperations.Dispose();
            gpuUploadOperations.Dispose();

            // UploadAllBlits()
            Profiler.BeginSample("UploadAllBlits");
            UploadBlitJob uploadJob = new UploadBlitJob()
            {
                BlitList               = context.defaultValueBlits,
                ThreadedSparseUploader = m_ThreadedGPUUploader
            };
            Profiler.EndSample();

            uploadJob.Schedule(context.defaultValueBlits.Length, 1).Complete();
            context.defaultValueBlits.Clear();

            Profiler.BeginSample("EndUpdate");
            try
            {
                // EndUpdate()
                m_GPUUploader.EndAndCommit(m_ThreadedGPUUploader);
                // Bind compute buffer here globally
                // TODO: Bind it once to the shader of the batch!
                UnityEngine.Shader.SetGlobalBuffer("unity_DOTSInstanceData", m_GPUPersistentInstanceData);

#if DEBUG_LOG_MEMORY_USAGE
                if (m_GPUPersistentAllocator.UsedSpace != PrevUsedSpace)
                {
                    Debug.Log($"GPU memory: {m_GPUPersistentAllocator.UsedSpace / 1024.0 / 1024.0:F4} / {m_GPUPersistentAllocator.Size / 1024.0 / 1024.0:F4}");
                    PrevUsedSpace = m_GPUPersistentAllocator.UsedSpace;
                }
#endif
            }
            finally
            {
                m_GPUUploader.FrameCleanup();
            }
            Profiler.EndSample();
        }

        protected override void OnDestroy()
        {
            m_GPUUploader.Dispose();
            m_GPUPersistentInstanceData.Dispose();
        }

        private void ComputeUploadSizeRequirements(
            int numGpuUploadOperations,
            NativeArray<GpuUploadOperation>         gpuUploadOperations,
            NativeArray<DefaultValueBlitDescriptor> defaultValueBlits,
            out int _numOperations,
            out int _totalUploadBytes,
            out int _biggestUploadBytes)
        {
            var numOperations      = numGpuUploadOperations + defaultValueBlits.Length;
            var totalUploadBytes   = 0;
            var biggestUploadBytes = 0;
            Job.WithCode(() =>
            {
                for (int i = 0; i < numGpuUploadOperations; ++i)
                {
                    var numBytes        = gpuUploadOperations[i].BytesRequiredInUploadBuffer;
                    totalUploadBytes   += numBytes;
                    biggestUploadBytes  = math.max(biggestUploadBytes, numBytes);
                }

                for (int i = 0; i < defaultValueBlits.Length; ++i)
                {
                    var numBytes        = defaultValueBlits[i].BytesRequiredInUploadBuffer;
                    totalUploadBytes   += numBytes;
                    biggestUploadBytes  = math.max(biggestUploadBytes, numBytes);
                }
            }).Run();

            _numOperations      = numOperations;
            _totalUploadBytes   = totalUploadBytes;
            _biggestUploadBytes = biggestUploadBytes;
        }

        [BurstCompile]
        struct ComputeOperationsJob : IJobEntityBatch
        {
            [ReadOnly] public ComponentTypeHandle<HybridChunkInfo>           HybridChunkInfo;
            public ComponentTypeHandle<ChunkMaterialPropertyDirtyMask>       chunkPropertyDirtyMaskHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkPerCameraCullingMask> chunkPerCameraCullingMaskHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkHeader>               ChunkHeader;

            [ReadOnly] public NativeArray<ChunkProperty> ChunkProperties;
            public int                                   LocalToWorldType;
            public int                                   WorldToLocalType;
            public int                                   PrevLocalToWorldType;
            public int                                   PrevWorldToLocalType;

            [NativeDisableParallelForRestriction] public NativeArray<GpuUploadOperation> GpuUploadOperations;
            [NativeDisableParallelForRestriction] public NativeReference<int>            NumGpuUploadOperations;

            public ComponentTypeCache.BurstCompatibleTypeArray ComponentTypes;

            public void Execute(ArchetypeChunk metaChunk, int chunkIndex)
            {
                // metaChunk is the chunk which contains the meta entities (= entities holding the chunk components) for the actual chunks

                var hybridChunkInfos   = metaChunk.GetNativeArray(HybridChunkInfo);
                var chunkHeaders       = metaChunk.GetNativeArray(ChunkHeader);
                var chunkDirtyMasks    = metaChunk.GetNativeArray(chunkPropertyDirtyMaskHandle);
                var chunkPerCameraMask = metaChunk.GetNativeArray(chunkPerCameraCullingMaskHandle);

                for (int i = 0; i < metaChunk.Count; ++i)
                {
                    var visibleMask = chunkPerCameraMask[i];
                    if ((visibleMask.lower.Value | visibleMask.upper.Value) == 0)
                        continue;

                    var chunkInfo   = hybridChunkInfos[i];
                    var dirtyMask   = chunkDirtyMasks[i];
                    var chunkHeader = chunkHeaders[i];
                    var chunk       = chunkHeader.ArchetypeChunk;

                    ProcessChunk(in chunkInfo, ref dirtyMask, chunk);
                    chunkDirtyMasks[i] = dirtyMask;
                }
            }

            unsafe void ProcessChunk(in HybridChunkInfo chunkInfo, ref ChunkMaterialPropertyDirtyMask dirtyMask, ArchetypeChunk chunk)
            {
                if (!chunkInfo.Valid)
                    return;

                var dstOffsetWorldToLocal     = -1;
                var dstOffsetPrevWorldToLocal = -1;

                fixed (DynamicComponentTypeHandle* fixedT0 = &ComponentTypes.t0)
                {
                    for (int i = chunkInfo.ChunkTypesBegin; i < chunkInfo.ChunkTypesEnd; ++i)
                    {
                        var chunkProperty = ChunkProperties[i];
                        var type          = chunkProperty.ComponentTypeIndex;
                        if (type == WorldToLocalType)
                            dstOffsetWorldToLocal = chunkProperty.GPUDataBegin;
                        else if (type == PrevWorldToLocalType)
                            dstOffsetPrevWorldToLocal = chunkProperty.GPUDataBegin;
                    }

                    for (int i = chunkInfo.ChunkTypesBegin; i < chunkInfo.ChunkTypesEnd; ++i)
                    {
                        var chunkProperty = ChunkProperties[i];
                        var type          = ComponentTypes.Type(fixedT0, chunkProperty.ComponentTypeIndex);
                        var typeIndex     = ComponentTypes.TypeIndexToArrayIndex[ComponentTypeCache.GetArrayIndex(chunkProperty.ComponentTypeIndex)];

                        var chunkType          = chunkProperty.ComponentTypeIndex;
                        var isLocalToWorld     = chunkType == LocalToWorldType;
                        var isPrevLocalToWorld = chunkType == PrevLocalToWorldType;

                        bool copyComponentData = typeIndex >= 64 ? dirtyMask.upper.IsSet(typeIndex - 64) : dirtyMask.lower.IsSet(typeIndex);

                        if (copyComponentData)
                        {
#if DEBUG_LOG_PROPERTIES
                            Debug.Log($"UpdateChunkProperty(internalBatchIndex: {chunkInfo.InternalIndex}, property: {i}, elementSize: {chunkProperty.ValueSizeBytesCPU})");
#endif

                            var src = chunk.GetDynamicComponentDataArrayReinterpret<int>(type,
                                                                                         chunkProperty.ValueSizeBytesCPU);

#if PROFILE_BURST_JOB_INTERNALS
                            ProfileAddUpload.Begin();
#endif

                            int sizeBytes = (int)((uint)chunk.Count * (uint)chunkProperty.ValueSizeBytesCPU);
                            var srcPtr    = src.GetUnsafeReadOnlyPtr();
                            var dstOffset = chunkProperty.GPUDataBegin;
                            if (isLocalToWorld || isPrevLocalToWorld)
                            {
                                var numMatrices = sizeBytes / sizeof(float4x4);
                                AddMatrixUpload(
                                    srcPtr,
                                    numMatrices,
                                    dstOffset,
                                    isLocalToWorld ? dstOffsetWorldToLocal : dstOffsetPrevWorldToLocal,
                                    (chunkProperty.ValueSizeBytesCPU == 4 * 4 * 3) ?
                                    ThreadedSparseUploader.MatrixType.MatrixType3x4 :
                                    ThreadedSparseUploader.MatrixType.MatrixType4x4,
                                    (chunkProperty.ValueSizeBytesGPU == 4 * 4 * 3) ?
                                    ThreadedSparseUploader.MatrixType.MatrixType3x4 :
                                    ThreadedSparseUploader.MatrixType.MatrixType4x4);

#if USE_PICKING_MATRICES
                                // If picking support is enabled, also copy the LocalToWorld matrices
                                // to the traditional instancing matrix array. This should be thread safe
                                // because the related Burst jobs run during DOTS system execution, and
                                // are guaranteed to have finished before rendering starts.
                                if (isLocalToWorld)
                                {
#if PROFILE_BURST_JOB_INTERNALS
                                    ProfilePickingMatrices.Begin();
#endif
                                    float4x4* batchPickingMatrices = (float4x4*)BatchPickingMatrices[internalIndex];
                                    int chunkOffsetInBatch   = chunkInfo.CullingData.BatchOffset;
                                    UnsafeUtility.MemCpy(
                                        batchPickingMatrices + chunkOffsetInBatch,
                                        srcPtr,
                                        sizeBytes);
#if PROFILE_BURST_JOB_INTERNALS
                                    ProfilePickingMatrices.End();
#endif
                                }
#endif
                            }
                            else
                            {
                                AddUpload(
                                    srcPtr,
                                    sizeBytes,
                                    dstOffset);
                            }
#if PROFILE_BURST_JOB_INTERNALS
                            ProfileAddUpload.End();
#endif
                        }
                    }
                }

                dirtyMask = default;
            }

            private unsafe void AddUpload(void* srcPtr, int sizeBytes, int dstOffset)
            {
                int* numGpuUploadOperations = (int*)NumGpuUploadOperations.GetUnsafePtr();
                int  index                  = System.Threading.Interlocked.Add(ref numGpuUploadOperations[0], 1) - 1;

                if (index < GpuUploadOperations.Length)
                {
                    GpuUploadOperations[index] = new GpuUploadOperation
                    {
                        Kind             = GpuUploadOperation.UploadOperationKind.Memcpy,
                        Src              = srcPtr,
                        DstOffset        = dstOffset,
                        DstOffsetInverse = -1,
                        Size             = sizeBytes,
                        Stride           = 0,
                    };
                }
                else
                {
                    // Debug.Assert(false, "Maximum amount of GPU upload operations exceeded");
                }
            }

            private unsafe void AddMatrixUpload(
                void*                             srcPtr,
                int numMatrices,
                int dstOffset,
                int dstOffsetInverse,
                ThreadedSparseUploader.MatrixType matrixTypeCpu,
                ThreadedSparseUploader.MatrixType matrixTypeGpu)
            {
                int* numGpuUploadOperations = (int*)NumGpuUploadOperations.GetUnsafePtr();
                int  index                  = System.Threading.Interlocked.Add(ref numGpuUploadOperations[0], 1) - 1;

                if (index < GpuUploadOperations.Length)
                {
                    GpuUploadOperations[index] = new GpuUploadOperation
                    {
                        Kind = (matrixTypeGpu == ThreadedSparseUploader.MatrixType.MatrixType3x4) ?
                               GpuUploadOperation.UploadOperationKind.SOAMatrixUpload3x4 :
                               GpuUploadOperation.UploadOperationKind.SOAMatrixUpload4x4,
                        Src              = srcPtr,
                        DstOffset        = dstOffset,
                        DstOffsetInverse = dstOffsetInverse,
                        Size             = numMatrices,
                        Stride           = 0,
                    };
                }
                else
                {
                    // Debug.Assert(false, "Maximum amount of GPU upload operations exceeded");
                }
            }
        }
    }
}

