#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
using System.Collections.Generic;
using Latios;
using Latios.Kinemation.SparseUpload;
using Latios.Transforms;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine.Profiling;

namespace Latios.Kinemation.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    public partial class UploadMaterialPropertiesSystem : CullingComputeDispatchSubSystemBase
    {
        EntityQuery m_metaQuery;

        private UnityEngine.GraphicsBuffer        m_GPUPersistentInstanceData;
        internal UnityEngine.GraphicsBufferHandle m_GPUPersistentInstanceBufferHandle;
        private LatiosSparseUploader              m_GPUUploader;
        private LatiosThreadedSparseUploader      m_ThreadedGPUUploader;

        private long m_persistentInstanceDataSize;

        const int  kGPUUploaderChunkSize = 4 * 1024 * 1024;
        const long kGPUBufferSizeInitial = 32 * 1024 * 1024;

        internal ComponentTypeCache.BurstCompatibleTypeArray m_burstCompatibleTypeArray;

#if DEBUG_LOG_MEMORY_USAGE
        private static ulong PrevUsedSpace = 0;
#endif

        protected override void OnCreate()
        {
            m_metaQuery = Fluent.WithAll<EntitiesGraphicsChunkInfo>(false).WithAll<ChunkHeader>(true).WithAll<ChunkPerCameraCullingMask>(true).Build();

            m_persistentInstanceDataSize = kGPUBufferSizeInitial;

            m_GPUPersistentInstanceData = new UnityEngine.GraphicsBuffer(
                UnityEngine.GraphicsBuffer.Target.Raw,
                UnityEngine.GraphicsBuffer.UsageFlags.None,
                (int)m_persistentInstanceDataSize / 4,
                4);
            m_GPUPersistentInstanceBufferHandle = m_GPUPersistentInstanceData.bufferHandle;
            m_GPUUploader                       = new LatiosSparseUploader(m_GPUPersistentInstanceData, kGPUUploaderChunkSize);
        }

        // Todo: Get rid of the hard system dependencies.
        internal bool SetBufferSize(long requiredPersistentBufferSize, out UnityEngine.GraphicsBufferHandle newHandle)
        {
            if (requiredPersistentBufferSize != m_persistentInstanceDataSize)
            {
                m_persistentInstanceDataSize = requiredPersistentBufferSize;

                var newBuffer = new UnityEngine.GraphicsBuffer(
                    UnityEngine.GraphicsBuffer.Target.Raw,
                    UnityEngine.GraphicsBuffer.UsageFlags.None,
                    (int)(m_persistentInstanceDataSize / 4),
                    4);
                m_GPUUploader.ReplaceBuffer(newBuffer, true);
                m_GPUPersistentInstanceBufferHandle = newBuffer.bufferHandle;
                newHandle                           = m_GPUPersistentInstanceBufferHandle;

                if (m_GPUPersistentInstanceData != null)
                    m_GPUPersistentInstanceData.Dispose();
                m_GPUPersistentInstanceData = newBuffer;
                return true;
            }
            newHandle = default;
            return false;
        }

        protected override IEnumerable<bool> UpdatePhase()
        {
            while (true)
            {
                if (!GetPhaseActions(CullingComputeDispatchState.Collect, out var terminate))
                {
                    yield return false;
                    continue;
                }
                if (terminate)
                    break;

                var context               = worldBlackboardEntity.GetCollectionComponent<MaterialPropertiesUploadContext>(true);
                var materialPropertyTypes = worldBlackboardEntity.GetBuffer<MaterialPropertyComponentType>(true);
                var culling               = worldBlackboardEntity.GetComponentData<CullingContext>();

                // Conservative estimate is that every known type is in every chunk. There will be
                // at most one operation per type per chunk, which will be either an actual
                // chunk data upload, or a default value blit (a single type should not have both).
                int conservativeMaximumGpuUploads = context.hybridRenderedChunkCount * materialPropertyTypes.Length;
                var gpuUploadOperations           = CollectionHelper.CreateNativeArray<GpuUploadOperation>(
                    conservativeMaximumGpuUploads,
                    WorldUpdateAllocator,
                    NativeArrayOptions.UninitializedMemory);
                var numGpuUploadOperations = new NativeReference<int>(
                    WorldUpdateAllocator,
                    NativeArrayOptions.ClearMemory);

                m_burstCompatibleTypeArray.Update(ref CheckedStateRef);
                var collectJh = new ComputeOperationsJob
                {
                    ChunkHeader                     = SystemAPI.GetComponentTypeHandle<ChunkHeader>(true),
                    ChunkProperties                 = context.chunkProperties,
                    chunkPropertyDirtyMaskHandle    = SystemAPI.GetComponentTypeHandle<ChunkMaterialPropertyDirtyMask>(false),
                    chunkPerCameraCullingMaskHandle = SystemAPI.GetComponentTypeHandle<ChunkPerCameraCullingMask>(true),
                    ComponentTypes                  = m_burstCompatibleTypeArray,
                    GpuUploadOperations             = gpuUploadOperations,
                    EntitiesGraphicsChunkInfo       = SystemAPI.GetComponentTypeHandle<EntitiesGraphicsChunkInfo>(true),
                    WorldTransformType              = TypeManager.GetTypeIndex<WorldTransform>(),
                    NumGpuUploadOperations          = numGpuUploadOperations,
                    PreviousTransformType           = TypeManager.GetTypeIndex<PreviousTransform>(),
                    PreviousTransformPreviousType   = TypeManager.GetTypeIndex<BuiltinMaterialPropertyUnity_MatrixPreviousMI_Tag>(),
                    WorldTransformInverseType       = TypeManager.GetTypeIndex<WorldToLocal_Tag>(),
                    postProcessMatrixHandle         = SystemAPI.GetComponentTypeHandle<PostProcessMatrix>(true),
                    previousPostProcessMatrixHandle = SystemAPI.GetComponentTypeHandle<PreviousPostProcessMatrix>(true)
                }.ScheduleParallel(m_metaQuery, Dependency);

                var uploadSizeRequirements = new NativeReference<UploadSizeRequirements>(WorldUpdateAllocator, NativeArrayOptions.UninitializedMemory);
                Dependency                 = new ComputeUploadSizeRequirementsJob
                {
                    numGpuUploadOperations = numGpuUploadOperations,
                    gpuUploadOperations    = gpuUploadOperations,
                    valueBlits             = context.valueBlits,
                    requirements           = uploadSizeRequirements
                }.Schedule(collectJh);

                yield return true;

                if (!GetPhaseActions(CullingComputeDispatchState.Write, out terminate))
                    continue;
                if (terminate)
                    break;

                UnityEngine.Debug.Assert(numGpuUploadOperations.Value <= gpuUploadOperations.Length, "Maximum GPU upload operation count exceeded");

#if DEBUG_LOG_UPLOADS
                if (numOperations > 0)
                {
                    Debug.Log($"GPU upload operations: {numOperations}, GPU upload bytes: {totalUploadBytes}");
                }
#endif

                // Todo: Once blits are removed in newer Entities versions, we can add early-out checks here.
                var sizeRequirements  = uploadSizeRequirements.Value;
                m_ThreadedGPUUploader = m_GPUUploader.Begin(sizeRequirements.totalUploadBytes,
                                                            sizeRequirements.biggestUploadBytes,
                                                            sizeRequirements.numOperations,
                                                            culling.globalSystemVersionOfLatiosEntitiesGraphics);

                // This is a different update, so we need to resecure this collection component.
                // Also, this time we write to it.
                context = worldBlackboardEntity.GetCollectionComponent<MaterialPropertiesUploadContext>(false);

                var writeJh = new ExecuteGpuUploads
                {
                    GpuUploadOperations    = gpuUploadOperations,
                    ThreadedSparseUploader = m_ThreadedGPUUploader,
                }.Schedule(numGpuUploadOperations.Value, 1, Dependency);

                // Todo: Do only on first culling pass?
                UploadBlitJob uploadJob = new UploadBlitJob()
                {
                    BlitList               = context.valueBlits,
                    ThreadedSparseUploader = m_ThreadedGPUUploader
                };
                writeJh    = uploadJob.ScheduleByRef(context.valueBlits.Length, 1, writeJh);
                Dependency = new ClearBlitsJob { blits = context.valueBlits }.Schedule(writeJh);

                yield return true;

                if (!GetPhaseActions(CullingComputeDispatchState.Dispatch, out terminate))
                    continue;

                try
                {
                    m_GPUUploader.EndAndCommit(m_ThreadedGPUUploader);
                }
                finally
                {
                    m_GPUUploader.FrameCleanup();
                }

                if (terminate)
                    break;
            }
        }

        protected override void OnDestroyDispatchSystem()
        {
            m_GPUUploader.Dispose();
            m_GPUPersistentInstanceData.Dispose();
            m_burstCompatibleTypeArray.Dispose(default);
        }

        [BurstCompile]
        struct ComputeOperationsJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<EntitiesGraphicsChunkInfo> EntitiesGraphicsChunkInfo;
            public ComponentTypeHandle<ChunkMaterialPropertyDirtyMask>       chunkPropertyDirtyMaskHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkPerCameraCullingMask> chunkPerCameraCullingMaskHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkHeader>               ChunkHeader;
            [ReadOnly] public ComponentTypeHandle<PostProcessMatrix>         postProcessMatrixHandle;
            [ReadOnly] public ComponentTypeHandle<PreviousPostProcessMatrix> previousPostProcessMatrixHandle;

            [ReadOnly] public NativeArray<ChunkProperty> ChunkProperties;
            public int                                   WorldTransformType;
            public int                                   WorldTransformInverseType;
            public int                                   PreviousTransformType;
            public int                                   PreviousTransformPreviousType;

            [NativeDisableParallelForRestriction] public NativeArray<GpuUploadOperation> GpuUploadOperations;
            [NativeDisableParallelForRestriction] public NativeReference<int>            NumGpuUploadOperations;

            public ComponentTypeCache.BurstCompatibleTypeArray ComponentTypes;

            public void Execute(in ArchetypeChunk metaChunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                // metaChunk is the chunk which contains the meta entities (= entities holding the chunk components) for the actual chunks

                var hybridChunkInfos   = metaChunk.GetNativeArray(ref EntitiesGraphicsChunkInfo);
                var chunkHeaders       = metaChunk.GetNativeArray(ref ChunkHeader);
                var chunkDirtyMasks    = metaChunk.GetNativeArray(ref chunkPropertyDirtyMaskHandle);
                var chunkPerCameraMask = metaChunk.GetNativeArray(ref chunkPerCameraCullingMaskHandle);

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

            unsafe void ProcessChunk(in EntitiesGraphicsChunkInfo chunkInfo, ref ChunkMaterialPropertyDirtyMask dirtyMask, ArchetypeChunk chunk)
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
                        if (type == WorldTransformInverseType)
                            dstOffsetWorldToLocal = chunkProperty.GPUDataBegin;
                        else if (type == PreviousTransformPreviousType)
                            dstOffsetPrevWorldToLocal = chunkProperty.GPUDataBegin;
                    }

                    for (int i = chunkInfo.ChunkTypesBegin; i < chunkInfo.ChunkTypesEnd; ++i)
                    {
                        var chunkProperty = ChunkProperties[i];
                        var type          = ComponentTypes.Type(fixedT0, chunkProperty.ComponentTypeIndex);
                        var typeIndex     = ComponentTypes.TypeIndexToArrayIndex[ComponentTypeCache.GetArrayIndex(chunkProperty.ComponentTypeIndex)];

                        var chunkType          = chunkProperty.ComponentTypeIndex;
                        var isLocalToWorld     = chunkType == WorldTransformType;
                        var isPrevLocalToWorld = chunkType == PreviousTransformType;

                        bool copyComponentData = typeIndex >= 64 ? dirtyMask.upper.IsSet(typeIndex - 64) : dirtyMask.lower.IsSet(typeIndex);

                        if (copyComponentData)
                        {
#if DEBUG_LOG_PROPERTIES
                            Debug.Log($"UpdateChunkProperty(internalBatchIndex: {chunkInfo.InternalIndex}, property: {i}, elementSize: {chunkProperty.ValueSizeBytesCPU})");
#endif

                            var src = chunk.GetDynamicComponentDataArrayReinterpret<int>(ref type,
                                                                                         chunkProperty.ValueSizeBytesCPU);

#if PROFILE_BURST_JOB_INTERNALS
                            ProfileAddUpload.Begin();
#endif

                            int sizeBytes = (int)((uint)chunk.Count * (uint)chunkProperty.ValueSizeBytesCPU);
                            var srcPtr    = src.GetUnsafeReadOnlyPtr();
                            var dstOffset = chunkProperty.GPUDataBegin;
                            if (isLocalToWorld || isPrevLocalToWorld)
                            {
                                var numQvvs = sizeBytes / sizeof(TransformQvvs);

                                void* extraPtr = null;
                                if (isLocalToWorld)
                                    extraPtr = chunk.GetComponentDataPtrRO(ref postProcessMatrixHandle);
                                else
                                    extraPtr = chunk.GetComponentDataPtrRO(ref previousPostProcessMatrixHandle);

                                AddQvvsUpload(
                                    srcPtr,
                                    numQvvs,
                                    dstOffset,
                                    isLocalToWorld ? dstOffsetWorldToLocal : dstOffsetPrevWorldToLocal,
                                    extraPtr
                                    );
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
                    };
                }
                else
                {
                    // Debug.Assert(false, "Maximum amount of GPU upload operations exceeded");
                }
            }

            private unsafe void AddQvvsUpload(
                void* srcPtr,
                int numQvvs,
                int dstOffset,
                int dstOffsetInverse,
                void* srcExtraPtr)
            {
                int* numGpuUploadOperations = (int*)NumGpuUploadOperations.GetUnsafePtr();
                int  index                  = System.Threading.Interlocked.Add(ref numGpuUploadOperations[0], 1) - 1;

                if (index < GpuUploadOperations.Length)
                {
                    GpuUploadOperations[index] = new GpuUploadOperation
                    {
                        Kind = (srcExtraPtr == null) ?
                               GpuUploadOperation.UploadOperationKind.SOAQvvsUpload3x4 :
                               GpuUploadOperation.UploadOperationKind.SOACombineQvvsMatrixUpload3x4,
                        Src              = srcPtr,
                        SrcExtra         = srcExtraPtr,
                        DstOffset        = dstOffset,
                        DstOffsetInverse = dstOffsetInverse,
                        Size             = numQvvs,
                    };
                }
                else
                {
                    // Debug.Assert(false, "Maximum amount of GPU upload operations exceeded");
                }
            }
        }

        struct UploadSizeRequirements
        {
            public int numOperations;
            public int totalUploadBytes;
            public int biggestUploadBytes;
        }

        [BurstCompile]
        struct ComputeUploadSizeRequirementsJob : IJob
        {
            [ReadOnly] public NativeReference<int>            numGpuUploadOperations;
            [ReadOnly] public NativeArray<GpuUploadOperation> gpuUploadOperations;
            [ReadOnly] public NativeList<ValueBlitDescriptor> valueBlits;
            public NativeReference<UploadSizeRequirements>    requirements;

            public void Execute()
            {
                var numOperations      = numGpuUploadOperations.Value + valueBlits.Length;
                var totalUploadBytes   = 0;
                var biggestUploadBytes = 0;
                for (int i = 0; i < numGpuUploadOperations.Value; ++i)
                {
                    var numBytes        = gpuUploadOperations[i].BytesRequiredInUploadBuffer;
                    totalUploadBytes   += numBytes;
                    biggestUploadBytes  = math.max(biggestUploadBytes, numBytes);
                }

                for (int i = 0; i < valueBlits.Length; ++i)
                {
                    var numBytes        = valueBlits[i].BytesRequiredInUploadBuffer;
                    totalUploadBytes   += numBytes;
                    biggestUploadBytes  = math.max(biggestUploadBytes, numBytes);
                }

                requirements.Value = new UploadSizeRequirements
                {
                    numOperations      = numOperations,
                    totalUploadBytes   = totalUploadBytes,
                    biggestUploadBytes = biggestUploadBytes
                };
            }
        }

        // Describes a single set of data to be uploaded from the CPU to the GPU during this frame.
        // The operations are collected up front so their total size can be known for buffer allocation
        // purposes, and for effectively load balancing the upload memcpy work.
        internal unsafe struct GpuUploadOperation
        {
            public enum UploadOperationKind
            {
                Memcpy,  // raw upload of a byte block to the GPU
                SOAMatrixUpload3x4,  // upload matrices from CPU, invert on GPU, write in SoA arrays, 3x4 destination
                SOAMatrixUpload4x4,  // upload matrices from CPU, invert on GPU, write in SoA arrays, 4x4 destination
                                     // TwoMatrixUpload, // upload matrices from CPU, write them and their inverses to GPU (for transform sharing branch)
                SOAQvvsUpload3x4,  // upload qvvs transforms from CPU, convert and invert on GPU, write in SoA arrays, 3x4 destination
                SOACombineQvvsMatrixUpload3x4  // combine qvvs transforms with matrices on CPU, upload, invert on GPU, write in SoA arrays, 3x4 destination
            }

            // Which kind of upload operation this is
            public UploadOperationKind Kind;
            // If a matrix upload, what matrix type is this?
            public LatiosThreadedSparseUploader.MatrixType SrcMatrixType;
            // Pointer to source data, whether raw byte data, matrices, or qvvs
            public void* Src;
            // Pointer to extra source data that should be combined, typically for qvvs * matrices
            public void* SrcExtra;
            // GPU offset to start writing destination data in
            public int DstOffset;
            // GPU offset to start writing any inverse matrices in, if applicable
            public int DstOffsetInverse;
            // Size in bytes for raw operations, size in whole matrices for matrix operations
            public int Size;

            // Raw uploads require their size in bytes from the upload buffer.
            // Matrix operations require a single 48-byte matrix per matrix.
            public int BytesRequiredInUploadBuffer => (Kind == UploadOperationKind.Memcpy) ?
            Size :
            (Size * UnsafeUtility.SizeOf<float3x4>());
        }

        [BurstCompile]
        internal unsafe struct ExecuteGpuUploads : IJobParallelFor
        {
            [ReadOnly] public NativeArray<GpuUploadOperation> GpuUploadOperations;
            public LatiosThreadedSparseUploader               ThreadedSparseUploader;

            public void Execute(int index)
            {
                var uploadOperation = GpuUploadOperations[index];

                switch (uploadOperation.Kind)
                {
                    case GpuUploadOperation.UploadOperationKind.Memcpy:
                        ThreadedSparseUploader.AddUpload(
                            uploadOperation.Src,
                            uploadOperation.Size,
                            uploadOperation.DstOffset);
                        break;
                    case GpuUploadOperation.UploadOperationKind.SOAMatrixUpload3x4:
                    case GpuUploadOperation.UploadOperationKind.SOAMatrixUpload4x4:
                        var dstType = (uploadOperation.Kind == GpuUploadOperation.UploadOperationKind.SOAMatrixUpload3x4) ?
                                      LatiosThreadedSparseUploader.MatrixType.MatrixType3x4 :
                                      LatiosThreadedSparseUploader.MatrixType.MatrixType4x4;
                        if (uploadOperation.DstOffsetInverse < 0)
                        {
                            ThreadedSparseUploader.AddMatrixUpload(
                                uploadOperation.Src,
                                uploadOperation.Size,
                                uploadOperation.DstOffset,
                                uploadOperation.SrcMatrixType,
                                dstType);
                        }
                        else
                        {
                            ThreadedSparseUploader.AddMatrixUploadAndInverse(
                                uploadOperation.Src,
                                uploadOperation.Size,
                                uploadOperation.DstOffset,
                                uploadOperation.DstOffsetInverse,
                                uploadOperation.SrcMatrixType,
                                dstType);
                        }
                        break;
                    case GpuUploadOperation.UploadOperationKind.SOAQvvsUpload3x4:
                        if (uploadOperation.DstOffsetInverse < 0)
                        {
                            ThreadedSparseUploader.AddQvvsUpload(
                                uploadOperation.Src,
                                uploadOperation.Size,
                                uploadOperation.DstOffset);
                        }
                        else
                        {
                            ThreadedSparseUploader.AddQvvsUploadAndInverse(
                                uploadOperation.Src,
                                uploadOperation.Size,
                                uploadOperation.DstOffset,
                                uploadOperation.DstOffsetInverse);
                        }
                        break;
                    case GpuUploadOperation.UploadOperationKind.SOACombineQvvsMatrixUpload3x4:
                        if (uploadOperation.DstOffsetInverse < 0)
                        {
                            ThreadedSparseUploader.AddQvvsUpload(
                                uploadOperation.Src,
                                uploadOperation.Size,
                                uploadOperation.DstOffset,
                                uploadOperation.SrcExtra);
                        }
                        else
                        {
                            ThreadedSparseUploader.AddQvvsUploadAndInverse(
                                uploadOperation.Src,
                                uploadOperation.Size,
                                uploadOperation.DstOffset,
                                uploadOperation.DstOffsetInverse,
                                uploadOperation.SrcExtra);
                        }
                        break;
                    default:
                        break;
                }
            }
        }

        [BurstCompile]
        internal unsafe struct UploadBlitJob : IJobParallelFor
        {
            [ReadOnly] public NativeList<ValueBlitDescriptor> BlitList;
            public LatiosThreadedSparseUploader               ThreadedSparseUploader;

            public void Execute(int index)
            {
                ValueBlitDescriptor blit = BlitList[index];
                ThreadedSparseUploader.AddUpload(
                    &blit.Value,
                    (int)blit.ValueSizeBytes,
                    (int)blit.DestinationOffset,
                    (int)blit.Count);
            }
        }

        [BurstCompile]
        struct ClearBlitsJob : IJob
        {
            public NativeList<ValueBlitDescriptor> blits;

            public void Execute() => blits.Clear();
        }
    }
}

namespace Latios.Kinemation
{
    internal static class BurstCompatibleMaterialComponentTypeCacheExtensions
    {
        public static void Update(ref this ComponentTypeCache.BurstCompatibleTypeArray array, ref SystemState state)
        {
            array.t0.Update(ref state);
            array.t1.Update(ref state);
            array.t2.Update(ref state);
            array.t3.Update(ref state);
            array.t4.Update(ref state);
            array.t5.Update(ref state);
            array.t6.Update(ref state);
            array.t7.Update(ref state);
            array.t8.Update(ref state);
            array.t9.Update(ref state);
            array.t10.Update(ref state);
            array.t11.Update(ref state);
            array.t12.Update(ref state);
            array.t13.Update(ref state);
            array.t14.Update(ref state);
            array.t15.Update(ref state);
            array.t16.Update(ref state);
            array.t17.Update(ref state);
            array.t18.Update(ref state);
            array.t19.Update(ref state);
            array.t20.Update(ref state);
            array.t21.Update(ref state);
            array.t22.Update(ref state);
            array.t23.Update(ref state);
            array.t24.Update(ref state);
            array.t25.Update(ref state);
            array.t26.Update(ref state);
            array.t27.Update(ref state);
            array.t28.Update(ref state);
            array.t29.Update(ref state);
            array.t30.Update(ref state);
            array.t31.Update(ref state);
            array.t32.Update(ref state);
            array.t33.Update(ref state);
            array.t34.Update(ref state);
            array.t35.Update(ref state);
            array.t36.Update(ref state);
            array.t37.Update(ref state);
            array.t38.Update(ref state);
            array.t39.Update(ref state);
            array.t40.Update(ref state);
            array.t41.Update(ref state);
            array.t42.Update(ref state);
            array.t43.Update(ref state);
            array.t44.Update(ref state);
            array.t45.Update(ref state);
            array.t46.Update(ref state);
            array.t47.Update(ref state);
            array.t48.Update(ref state);
            array.t49.Update(ref state);
            array.t50.Update(ref state);
            array.t51.Update(ref state);
            array.t52.Update(ref state);
            array.t53.Update(ref state);
            array.t54.Update(ref state);
            array.t55.Update(ref state);
            array.t56.Update(ref state);
            array.t57.Update(ref state);
            array.t58.Update(ref state);
            array.t59.Update(ref state);
            array.t60.Update(ref state);
            array.t61.Update(ref state);
            array.t62.Update(ref state);
            array.t63.Update(ref state);
            array.t64.Update(ref state);
            array.t65.Update(ref state);
            array.t66.Update(ref state);
            array.t67.Update(ref state);
            array.t68.Update(ref state);
            array.t69.Update(ref state);
            array.t70.Update(ref state);
            array.t71.Update(ref state);
            array.t72.Update(ref state);
            array.t73.Update(ref state);
            array.t74.Update(ref state);
            array.t75.Update(ref state);
            array.t76.Update(ref state);
            array.t77.Update(ref state);
            array.t78.Update(ref state);
            array.t79.Update(ref state);
            array.t80.Update(ref state);
            array.t81.Update(ref state);
            array.t82.Update(ref state);
            array.t83.Update(ref state);
            array.t84.Update(ref state);
            array.t85.Update(ref state);
            array.t86.Update(ref state);
            array.t87.Update(ref state);
            array.t88.Update(ref state);
            array.t89.Update(ref state);
            array.t90.Update(ref state);
            array.t91.Update(ref state);
            array.t92.Update(ref state);
            array.t93.Update(ref state);
            array.t94.Update(ref state);
            array.t95.Update(ref state);
            array.t96.Update(ref state);
            array.t97.Update(ref state);
            array.t98.Update(ref state);
            array.t99.Update(ref state);
            array.t100.Update(ref state);
            array.t101.Update(ref state);
            array.t102.Update(ref state);
            array.t103.Update(ref state);
            array.t104.Update(ref state);
            array.t105.Update(ref state);
            array.t106.Update(ref state);
            array.t107.Update(ref state);
            array.t108.Update(ref state);
            array.t109.Update(ref state);
            array.t110.Update(ref state);
            array.t111.Update(ref state);
            array.t112.Update(ref state);
            array.t113.Update(ref state);
            array.t114.Update(ref state);
            array.t115.Update(ref state);
            array.t116.Update(ref state);
            array.t117.Update(ref state);
            array.t118.Update(ref state);
            array.t119.Update(ref state);
            array.t120.Update(ref state);
            array.t121.Update(ref state);
            array.t122.Update(ref state);
            array.t123.Update(ref state);
            array.t124.Update(ref state);
            array.t125.Update(ref state);
            array.t126.Update(ref state);
            array.t127.Update(ref state);
        }

        public static void FetchTypeHandles(ref this ComponentTypeCache cache, ref SystemState componentSystem)
        {
            var types = cache.UsedTypes.GetKeyValueArrays(Allocator.Temp);

            if (cache.TypeDynamics == null || cache.TypeDynamics.Length < cache.MaxIndex + 1)
                // Allocate according to Capacity so we grow with the same geometric formula as NativeList
                cache.TypeDynamics = new DynamicComponentTypeHandle[cache.MaxIndex + 1];

            ref var keys     = ref types.Keys;
            ref var values   = ref types.Values;
            int     numTypes = keys.Length;
            for (int i = 0; i < numTypes; ++i)
            {
                int arrayIndex                 = keys[i];
                int typeIndex                  = values[i];
                cache.TypeDynamics[arrayIndex] = componentSystem.GetDynamicComponentTypeHandle(
                    ComponentType.ReadOnly(typeIndex));
            }

            types.Dispose();
        }
    }
}
#endif

