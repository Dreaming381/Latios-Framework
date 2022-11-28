using Latios;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace Latios.Kinemation.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    public partial class UploadMaterialPropertiesSystem : SubSystem
    {
        EntityQuery m_metaQuery;

        private UnityEngine.GraphicsBuffer        m_GPUPersistentInstanceData;
        internal UnityEngine.GraphicsBufferHandle m_GPUPersistentInstanceBufferHandle;
        private SparseUploader                    m_GPUUploader;
        private ThreadedSparseUploader            m_ThreadedGPUUploader;

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
            m_GPUUploader                       = new SparseUploader(m_GPUPersistentInstanceData, kGPUUploaderChunkSize);
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

            m_burstCompatibleTypeArray.Update(ref CheckedStateRef);
            Dependency = new ComputeOperationsJob
            {
                ChunkHeader                     = GetComponentTypeHandle<ChunkHeader>(true),
                ChunkProperties                 = context.chunkProperties,
                chunkPropertyDirtyMaskHandle    = GetComponentTypeHandle<ChunkMaterialPropertyDirtyMask>(false),
                chunkPerCameraCullingMaskHandle = GetComponentTypeHandle<ChunkPerCameraCullingMask>(true),
                ComponentTypes                  = m_burstCompatibleTypeArray,
                GpuUploadOperations             = gpuUploadOperations,
                EntitiesGraphicsChunkInfo       = GetComponentTypeHandle<EntitiesGraphicsChunkInfo>(true),
                LocalToWorldType                = TypeManager.GetTypeIndex<LocalToWorld>(),
                NumGpuUploadOperations          = numGpuUploadOperations,
                PrevLocalToWorldType            = TypeManager.GetTypeIndex<BuiltinMaterialPropertyUnity_MatrixPreviousM>(),
                PrevWorldToLocalType            = TypeManager.GetTypeIndex<BuiltinMaterialPropertyUnity_MatrixPreviousMI_Tag>(),
                WorldToLocalType                = TypeManager.GetTypeIndex<WorldToLocal_Tag>(),
            }.ScheduleParallel(m_metaQuery, Dependency);
            CompleteDependency();
            Dependency = default;

            UnityEngine.Debug.Assert(numGpuUploadOperations.Value <= gpuUploadOperations.Length, "Maximum GPU upload operation count exceeded");

            ComputeUploadSizeRequirements(
                numGpuUploadOperations.Value, gpuUploadOperations, context.valueBlits,
                out int numOperations, out int totalUploadBytes, out int biggestUploadBytes);

#if DEBUG_LOG_UPLOADS
            if (numOperations > 0)
            {
                Debug.Log($"GPU upload operations: {numOperations}, GPU upload bytes: {totalUploadBytes}");
            }
#endif

            // BeginUpdate()
            Profiler.BeginSample("StartUpdate");

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
            // Todo: Do only on first culling pass?
            Profiler.BeginSample("UploadAllBlits");
            UploadBlitJob uploadJob = new UploadBlitJob()
            {
                BlitList               = context.valueBlits,
                ThreadedSparseUploader = m_ThreadedGPUUploader
            };
            Profiler.EndSample();

            uploadJob.Schedule(context.valueBlits.Length, 1).Complete();
            context.valueBlits.Clear();

            Profiler.BeginSample("EndUpdate");
            try
            {
                m_GPUUploader.EndAndCommit(m_ThreadedGPUUploader);
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
            m_burstCompatibleTypeArray.Dispose(default);
        }

        private void ComputeUploadSizeRequirements(
            int numGpuUploadOperations,
            NativeArray<GpuUploadOperation> gpuUploadOperations,
            NativeList<ValueBlitDescriptor> valueBlits,
            out int _numOperations,
            out int _totalUploadBytes,
            out int _biggestUploadBytes)
        {
            var numOperations      = numGpuUploadOperations + valueBlits.Length;
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

                for (int i = 0; i < valueBlits.Length; ++i)
                {
                    var numBytes        = valueBlits[i].BytesRequiredInUploadBuffer;
                    totalUploadBytes   += numBytes;
                    biggestUploadBytes  = math.max(biggestUploadBytes, numBytes);
                }
            }).Run();

            _numOperations      = numOperations;
            _totalUploadBytes   = totalUploadBytes;
            _biggestUploadBytes = biggestUploadBytes;
        }

        [BurstCompile]
        struct ComputeOperationsJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<EntitiesGraphicsChunkInfo> EntitiesGraphicsChunkInfo;
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

