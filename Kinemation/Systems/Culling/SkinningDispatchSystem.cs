using System.Collections.Generic;
using Latios.Kinemation.InternalSourceGen;
using Latios.Psyshock;
using Latios.Transforms;
using Latios.Transforms.Abstract;
using Latios.Unsafe;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Kinemation.Systems
{
    [DisableAutoCreation]
    public partial struct SkinningDispatchSystem : ISystem, ICullingComputeDispatchSystem<SkinningDispatchSystem.CollectState, SkinningDispatchSystem.WriteState>
    {
        LatiosWorldUnmanaged latiosWorld;

#if UNITY_ANDROID
        // Android devices often have buggy drivers that struggle with groupshared memory.
        // They may also not have enough memory for our compute shaders.
        const int kBatchThreshold = 0;
#else
        const int kBatchThreshold = 682;
#endif

        WorldTransformReadOnlyAspect.TypeHandle m_worldTransformHandle;
        WorldTransformReadOnlyAspect.Lookup     m_worldTransformLookup;

        EntityQuery m_skeletonQuery;
        EntityQuery m_skinnedMeshQuery;
        EntityQuery m_skinnedMeshMetaQuery;

        UnityObjectRef<UnityEngine.ComputeShader> m_batchSkinningShader;
        UnityObjectRef<UnityEngine.ComputeShader> m_expansionShader;
        UnityObjectRef<UnityEngine.ComputeShader> m_meshSkinningShader;
        int                                       m_batchSkinningKernelIndex;

        CullingComputeDispatchData<CollectState, WriteState> m_data;

        // Compute bindings
        int _dstTransforms;
        int _dstVertices;
        int _srcVertices;
        int _boneWeights;
        int _bindPoses;
        int _boneOffsets;
        int _metaBuffer;
        int _skeletonQvvsTransforms;
        int _startOffset;
        int _boneTransforms;

        // Shader bindings
        int _latiosBindPoses;
        int _latiosBoneTransforms;
        int _latiosDeformBuffer;

        // Legacy
        int _DeformedMeshData;
        int _PreviousFrameDeformedMeshData;
        int _SkinMatrices;

        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();

            m_data = new CullingComputeDispatchData<CollectState, WriteState>(latiosWorld);

            m_worldTransformHandle = new WorldTransformReadOnlyAspect.TypeHandle(ref state);
            m_worldTransformLookup = new WorldTransformReadOnlyAspect.Lookup(ref state);

            m_skeletonQuery        = state.Fluent().With<DependentSkinnedMesh>(true).With<ChunkPerCameraSkeletonCullingMask>(true, true).Build();
            m_skinnedMeshQuery     = state.Fluent().With<SkeletonDependent>(true).With<ChunkPerDispatchCullingMask>(true, true).Build();
            m_skinnedMeshMetaQuery = state.Fluent().With<ChunkHeader>(true).With<ChunkPerDispatchCullingMask>(true).Build();

            state.RequireForUpdate(m_skeletonQuery);
            state.RequireForUpdate(m_skinnedMeshQuery);

            m_batchSkinningKernelIndex = UnityEngine.SystemInfo.maxComputeWorkGroupSizeX < 1024 ? 1 : 0;
            m_batchSkinningShader      = latiosWorld.latiosWorld.LoadFromResourcesAndPreserve<UnityEngine.ComputeShader>("BatchSkinning");
            m_expansionShader          = latiosWorld.latiosWorld.LoadFromResourcesAndPreserve<UnityEngine.ComputeShader>("SkeletonMeshExpansion");
            m_meshSkinningShader       = latiosWorld.latiosWorld.LoadFromResourcesAndPreserve<UnityEngine.ComputeShader>("MeshSkinning");

            // Compute
            _dstTransforms          = UnityEngine.Shader.PropertyToID("_dstTransforms");
            _dstVertices            = UnityEngine.Shader.PropertyToID("_dstVertices");
            _srcVertices            = UnityEngine.Shader.PropertyToID("_srcVertices");
            _boneWeights            = UnityEngine.Shader.PropertyToID("_boneWeights");
            _bindPoses              = UnityEngine.Shader.PropertyToID("_bindPoses");
            _boneOffsets            = UnityEngine.Shader.PropertyToID("_boneOffsets");
            _metaBuffer             = UnityEngine.Shader.PropertyToID("_metaBuffer");
            _skeletonQvvsTransforms = UnityEngine.Shader.PropertyToID("_skeletonQvvsTransforms");
            _startOffset            = UnityEngine.Shader.PropertyToID("_startOffset");
            _boneTransforms         = UnityEngine.Shader.PropertyToID("_boneTransforms");

            // Shaders
            _latiosBindPoses      = UnityEngine.Shader.PropertyToID("_latiosBindPoses");
            _latiosBoneTransforms = UnityEngine.Shader.PropertyToID("_latiosBoneTransforms");
            _latiosDeformBuffer   = UnityEngine.Shader.PropertyToID("_latiosDeformBuffer");

            // Legacy shaders
            _DeformedMeshData              = UnityEngine.Shader.PropertyToID("_DeformedMeshData");
            _PreviousFrameDeformedMeshData = UnityEngine.Shader.PropertyToID("_PreviousFrameDeformedMeshData");
            _SkinMatrices                  = UnityEngine.Shader.PropertyToID("_SkinMatrices");
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state) => m_data.DoUpdate(ref state, ref this);

        public CollectState Collect(ref SystemState state)
        {
            var skeletonChunkCount = m_skeletonQuery.CalculateChunkCountWithoutFiltering();

            var skinningStream     = new NativeStream(skeletonChunkCount, state.WorldUpdateAllocator);
            var perChunkPrefixSums = CollectionHelper.CreateNativeArray<PerChunkPrefixSums>(skeletonChunkCount,
                                                                                            state.WorldUpdateAllocator,
                                                                                            NativeArrayOptions.UninitializedMemory);
            var meshChunks        = new NativeList<ArchetypeChunk>(m_skinnedMeshMetaQuery.CalculateEntityCountWithoutFiltering(), state.WorldUpdateAllocator);
            var requestsBlockList =
                new UnsafeParallelBlockList(UnsafeUtility.SizeOf<MeshSkinningRequestWithSkeletonTarget>(), 256, state.WorldUpdateAllocator);
            var groupedSkinningRequestsStartsAndCounts   = new NativeList<int2>(state.WorldUpdateAllocator);
            var groupedSkinningRequests                  = new NativeList<MeshSkinningRequest>(state.WorldUpdateAllocator);
            var skeletonEntityToSkinningRequestsGroupMap = new NativeHashMap<Entity, int>(1, state.WorldUpdateAllocator);
            var bufferLayouts                            = new NativeReference<BufferLayouts>(state.WorldUpdateAllocator, NativeArrayOptions.UninitializedMemory);
            var deformClassificationMap                  = latiosWorld.worldBlackboardEntity.GetCollectionComponent<DeformClassificationMap>(true);

            var collectJh = new FindMeshChunksNeedingSkinningJob
            {
                chunkHeaderHandle            = SystemAPI.GetComponentTypeHandle<ChunkHeader>(true),
                chunksToProcess              = meshChunks.AsParallelWriter(),
                perDispatchCullingMaskHandle = SystemAPI.GetComponentTypeHandle<ChunkPerDispatchCullingMask>(true),
                perFrameCullingMaskHandle    = SystemAPI.GetComponentTypeHandle<ChunkPerFrameCullingMask>(true),
                skeletonDependentHandle      = SystemAPI.GetComponentTypeHandle<SkeletonDependent>(true)
            }.ScheduleParallel(m_skinnedMeshMetaQuery, state.Dependency);

            collectJh = new CollectVisibleMeshesJob
            {
                chunksToProcess            = meshChunks.AsDeferredJobArray(),
                currentDeformHandle        = SystemAPI.GetComponentTypeHandle<CurrentDeformShaderIndex>(true),
                currentDqsVertexHandle     = SystemAPI.GetComponentTypeHandle<CurrentDqsVertexSkinningShaderIndex>(true),
                currentMatrixVertexHandle  = SystemAPI.GetComponentTypeHandle<CurrentMatrixVertexSkinningShaderIndex>(true),
                deformClassificationMap    = deformClassificationMap.deformClassificationMap,
                legacyComputeDeformHandle  = SystemAPI.GetComponentTypeHandle<LegacyComputeDeformShaderIndex>(true),
                legacyDotsDeformHandle     = SystemAPI.GetComponentTypeHandle<LegacyDotsDeformParamsShaderIndex>(true),
                legacyLbsHandle            = SystemAPI.GetComponentTypeHandle<LegacyLinearBlendSkinningShaderIndex>(true),
                previousDeformHandle       = SystemAPI.GetComponentTypeHandle<PreviousDeformShaderIndex>(true),
                previousDqsVertexHandle    = SystemAPI.GetComponentTypeHandle<PreviousDqsVertexSkinningShaderIndex>(true),
                previousMatrixVertexHandle = SystemAPI.GetComponentTypeHandle<PreviousMatrixVertexSkinningShaderIndex>(true),
                skeletonDependentHandle    = SystemAPI.GetComponentTypeHandle<SkeletonDependent>(true),
                twoAgoDeformHandle         = SystemAPI.GetComponentTypeHandle<TwoAgoDeformShaderIndex>(true),
                twoAgoDqsVertexHandle      = SystemAPI.GetComponentTypeHandle<TwoAgoDqsVertexSkinningShaderIndex>(true),
                twoAgoMatrixVertexHandle   = SystemAPI.GetComponentTypeHandle<TwoAgoMatrixVertexSkinningShaderIndex>(true),
                perDispatchMaskHandle      = SystemAPI.GetComponentTypeHandle<ChunkPerDispatchCullingMask>(true),
                perFrameMaskHandle         = SystemAPI.GetComponentTypeHandle<ChunkPerFrameCullingMask>(true),
                requestsBlockList          = requestsBlockList
            }.Schedule(meshChunks, 1, collectJh);

            collectJh = new GroupRequestsBySkeletonJob
            {
                groupedSkinningRequests                  = groupedSkinningRequests,
                groupedSkinningRequestsStartsAndCounts   = groupedSkinningRequestsStartsAndCounts,
                skeletonEntityToSkinningRequestsGroupMap = skeletonEntityToSkinningRequestsGroupMap,
                requestsBlockList                        = requestsBlockList
            }.Schedule(collectJh);

            collectJh = new GenerateSkinningCommandsJob
            {
                boneReferenceBufferHandle                = SystemAPI.GetBufferTypeHandle<BoneReference>(true),
                entityHandle                             = SystemAPI.GetEntityTypeHandle(),
                groupedSkinningRequests                  = groupedSkinningRequests.AsDeferredJobArray(),
                groupedSkinningRequestsStartsAndCounts   = groupedSkinningRequestsStartsAndCounts.AsDeferredJobArray(),
                optimizedBoneBufferHandle                = SystemAPI.GetBufferTypeHandle<OptimizedBoneTransform>(true),
                perChunkPrefixSums                       = perChunkPrefixSums,
                skeletonEntityToSkinningRequestsGroupMap = skeletonEntityToSkinningRequestsGroupMap,
                skinnedMeshesBufferHandle                = SystemAPI.GetBufferTypeHandle<DependentSkinnedMesh>(true),
                skinningStream                           = skinningStream.AsWriter()
            }.ScheduleParallel(m_skeletonQuery, collectJh);

            state.Dependency = new PrefixSumCountsJob
            {
                bufferLayouts               = bufferLayouts,
                maxRequiredDeformDataLookup = SystemAPI.GetComponentLookup<MaxRequiredDeformData>(false),
                perChunkPrefixSums          = perChunkPrefixSums,
                worldBlackboardEntity       = latiosWorld.worldBlackboardEntity
            }.Schedule(collectJh);

            var graphicsBroker = latiosWorld.worldBlackboardEntity.GetComponentData<GraphicsBufferBroker>();

            return new CollectState
            {
                broker             = graphicsBroker,
                perChunkPrefixSums = perChunkPrefixSums,
                skinningStream     = skinningStream,
                layouts            = bufferLayouts
            };
        }

        public WriteState Write(ref SystemState state, ref CollectState collectState)
        {
            var layouts = collectState.layouts.Value;
            if (layouts.requiredUploadTransforms == 0)
            {
                // skip rest of loop.
                return default;
            }

            var graphicsBroker = collectState.broker;

            m_worldTransformHandle.Update(ref state);
            m_worldTransformLookup.Update(ref state);

            var skinningMetaBuffer   = graphicsBroker.GetMetaUint4UploadBuffer(layouts.requiredMetaSize);
            var boneTransformsBuffer = graphicsBroker.GetBonesBuffer(layouts.requiredUploadTransforms);
            var skinningMetaArray    = skinningMetaBuffer.LockBufferForWrite<uint4>(0, (int)layouts.requiredMetaSize);
            var boneTransformsArray  = boneTransformsBuffer.LockBufferForWrite<TransformQvvs>(0, (int)layouts.requiredUploadTransforms);

            var boneOffsetsBuffer = latiosWorld.worldBlackboardEntity.GetCollectionComponent<BoneOffsetsGpuManager>(true).offsets.AsDeferredJobArray();

            state.Dependency = new WriteBuffersJob
            {
                boneOffsetsBuffer            = boneOffsetsBuffer,
                boneReferenceBufferHandle    = SystemAPI.GetBufferTypeHandle<BoneReference>(true),
                boneTransformsUploadBuffer   = boneTransformsArray,
                bufferLayouts                = collectState.layouts,
                metaBuffer                   = skinningMetaArray,
                optimizedBoneBufferHandle    = SystemAPI.GetBufferTypeHandle<OptimizedBoneTransform>(true),
                optimizedSkeletonStateHandle = SystemAPI.GetComponentTypeHandle<OptimizedSkeletonState>(true),
                perChunkPrefixSums           = collectState.perChunkPrefixSums,
                worldTransformHandle         = m_worldTransformHandle,
                worldTransformLookup         = m_worldTransformLookup,
                skinnedMeshesBufferHandle    = SystemAPI.GetBufferTypeHandle<DependentSkinnedMesh>(true),
                skinningStream               = collectState.skinningStream.AsReader(),
#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
                previousTransformHandle = SystemAPI.GetComponentTypeHandle<PreviousTransform>(true),
                previousTransformLookup = SystemAPI.GetComponentLookup<PreviousTransform>(true),
                twoAgoTransformHandle   = SystemAPI.GetComponentTypeHandle<TwoAgoTransform>(true),
                twoAgoTransformLookup   = SystemAPI.GetComponentLookup<TwoAgoTransform>(true),
#endif
            }.ScheduleParallel(m_skeletonQuery, state.Dependency);

            return new WriteState
            {
                broker               = graphicsBroker,
                boneTransformsBuffer = boneTransformsBuffer,
                skinningMetaBuffer   = skinningMetaBuffer,
                layouts              = layouts
            };
        }

        public void Dispatch(ref SystemState state, ref WriteState writeState)
        {
            if (!writeState.broker.isCreated)
                return;

            var graphicsBroker       = writeState.broker;
            var skinningMetaBuffer   = writeState.skinningMetaBuffer;
            var boneTransformsBuffer = writeState.boneTransformsBuffer;
            var layouts              = writeState.layouts;

            skinningMetaBuffer.UnlockBufferAfterWrite<uint4>((int)layouts.requiredMetaSize);
            boneTransformsBuffer.UnlockBufferAfterWrite<TransformQvvs>((int)layouts.requiredUploadTransforms);

            var requiredDeformSizes    = latiosWorld.worldBlackboardEntity.GetComponentData<MaxRequiredDeformData>();
            var shaderTransformsBuffer = graphicsBroker.GetSkinningTransformsBuffer(requiredDeformSizes.maxRequiredBoneTransformsForVertexSkinning);
            var shaderDeformBuffer     = graphicsBroker.GetDeformBuffer(requiredDeformSizes.maxRequiredDeformVertices);

            //UnityEngine.Debug.Log($"Vertex Skinning Buffer size: {requiredDeformSizes.maxRequiredBoneTransformsForVertexSkinning}");

            m_batchSkinningShader.SetBuffer(0, _dstTransforms,          shaderTransformsBuffer);
            m_batchSkinningShader.SetBuffer(0, _dstVertices,            shaderDeformBuffer);
            m_batchSkinningShader.SetBuffer(0, _srcVertices,            graphicsBroker.GetMeshVerticesBuffer());
            m_batchSkinningShader.SetBuffer(0, _boneWeights,            graphicsBroker.GetMeshWeightsBufferRO());
            m_batchSkinningShader.SetBuffer(0, _bindPoses,              graphicsBroker.GetMeshBindPosesBufferRO());
            m_batchSkinningShader.SetBuffer(0, _boneOffsets,            graphicsBroker.GetBoneOffsetsBufferRO());
            m_batchSkinningShader.SetBuffer(0, _metaBuffer,             skinningMetaBuffer);
            m_batchSkinningShader.SetBuffer(0, _skeletonQvvsTransforms, boneTransformsBuffer);

            for (int dispatchesRemaining = (int)layouts.batchSkinningHeadersCount, offset = 0; dispatchesRemaining > 0;)
            {
                int dispatchCount = math.min(dispatchesRemaining, 65535);
                m_batchSkinningShader.SetInt(_startOffset, offset);
                m_batchSkinningShader.Dispatch(m_batchSkinningKernelIndex, dispatchCount, 1, 1);
                offset              += dispatchCount;
                dispatchesRemaining -= dispatchCount;
            }

            m_expansionShader.SetBuffer(0, _dstTransforms,          shaderTransformsBuffer);
            m_expansionShader.SetBuffer(0, _bindPoses,              graphicsBroker.GetMeshBindPosesBufferRO());
            m_expansionShader.SetBuffer(0, _boneOffsets,            graphicsBroker.GetBoneOffsetsBufferRO());
            m_expansionShader.SetBuffer(0, _metaBuffer,             skinningMetaBuffer);
            m_expansionShader.SetBuffer(0, _skeletonQvvsTransforms, boneTransformsBuffer);

            for (int dispatchesRemaining = (int)layouts.expansionHeadersCount, offset = (int)layouts.expansionHeadersStart; dispatchesRemaining > 0;)
            {
                int dispatchCount = math.min(dispatchesRemaining, 65535);
                m_expansionShader.SetInt(_startOffset, offset);
                m_expansionShader.Dispatch(0, dispatchCount, 1, 1);
                offset              += dispatchCount;
                dispatchesRemaining -= dispatchCount;
            }

            m_meshSkinningShader.SetBuffer(0, _dstVertices,    shaderDeformBuffer);
            m_meshSkinningShader.SetBuffer(0, _srcVertices,    graphicsBroker.GetMeshVerticesBuffer());
            m_meshSkinningShader.SetBuffer(0, _boneWeights,    graphicsBroker.GetMeshWeightsBufferRO());
            m_meshSkinningShader.SetBuffer(0, _bindPoses,      graphicsBroker.GetMeshBindPosesBufferRO());
            m_meshSkinningShader.SetBuffer(0, _metaBuffer,     skinningMetaBuffer);
            m_meshSkinningShader.SetBuffer(0, _boneTransforms, shaderTransformsBuffer);

            for (int dispatchesRemaining = (int)layouts.meshSkinningCommandsCount, offset = (int)layouts.meshSkinningCommandsStart; dispatchesRemaining > 0;)
            {
                int dispatchCount = math.min(dispatchesRemaining, 65535);
                m_meshSkinningShader.SetInt(_startOffset, offset);
                m_meshSkinningShader.Dispatch(0, dispatchCount, 1, 1);
                offset              += dispatchCount;
                dispatchesRemaining -= dispatchCount;
            }

            GraphicsUnmanaged.SetGlobalBuffer(_DeformedMeshData,              shaderDeformBuffer);
            GraphicsUnmanaged.SetGlobalBuffer(_PreviousFrameDeformedMeshData, shaderDeformBuffer);
            GraphicsUnmanaged.SetGlobalBuffer(_SkinMatrices,                  shaderTransformsBuffer);
            GraphicsUnmanaged.SetGlobalBuffer(_latiosBindPoses,               graphicsBroker.GetMeshBindPosesBufferRO());
            GraphicsUnmanaged.SetGlobalBuffer(_latiosBoneTransforms,          shaderTransformsBuffer);
            GraphicsUnmanaged.SetGlobalBuffer(_latiosDeformBuffer,            shaderDeformBuffer);
        }

        public struct CollectState
        {
            internal GraphicsBufferBroker            broker;
            internal NativeReference<BufferLayouts>  layouts;
            internal NativeArray<PerChunkPrefixSums> perChunkPrefixSums;
            internal NativeStream                    skinningStream;
        }

        public struct WriteState
        {
            internal GraphicsBufferBroker    broker;
            internal GraphicsBufferUnmanaged skinningMetaBuffer;
            internal GraphicsBufferUnmanaged boneTransformsBuffer;
            internal BufferLayouts           layouts;
        }

        #region Utility Structs
        struct MeshSkinningRequest
        {
            // The strange ordering helps with sorting.
            public enum ShaderUsage : byte
            {
                CurrentDqsDeform = 0,
                CurrentDqsVertex = 1,
                CurrentMatrixDeform = 2,
                CurrentMatrixVertex = 3,
                PreviousDqsDeform = 4,
                PreviousDqsVertex = 5,
                PreviousMatrixDeform = 6,
                PreviousMatrixVertex = 7,
                TwoAgoDqsDeform = 8,
                TwoAgoDqsVertex = 9,
                TwoAgoMatrixDeform = 10,
                TwoAgoMatrixVertex = 11,
                UseVerticesInDst = 0x80,
                PropertyMask = 0x0f,
                AlgorithmMaskAsIfCurrent = 0x03,
            }

            // No one in their right mind will need 16 million meshes attached to a single skeleton,
            // so we borrow the high byte for the flags to keep data compact and aligned.
            public uint indexInSkeletonBufferShaderUsageHigh8;
            public uint shaderDstIndex;

            public uint indexInSkeletonBuffer => indexInSkeletonBufferShaderUsageHigh8 & 0x00ffffffu;
            public ShaderUsage shaderUsage => (ShaderUsage)(indexInSkeletonBufferShaderUsageHigh8 >> 24);
            public bool isDqsDeform => (indexInSkeletonBufferShaderUsageHigh8 & 0x03000000u) == 0x00000000u;
            public bool isDqsVertex => (indexInSkeletonBufferShaderUsageHigh8 & 0x03000000u) == 0x01000000u;
            public bool isMatrixDeform => (indexInSkeletonBufferShaderUsageHigh8 & 0x03000000u) == 0x02000000u;
            public bool isMatrixVertex => (indexInSkeletonBufferShaderUsageHigh8 & 0x03000000u) == 0x03000000u;
            public bool isDqs => (indexInSkeletonBufferShaderUsageHigh8 & 0x02000000u) == 0x00000000u;
            public bool isMatrix => (indexInSkeletonBufferShaderUsageHigh8 & 0x02000000u) == 0x02000000u;
            public bool useVerticesInDst => (indexInSkeletonBufferShaderUsageHigh8 & 0x80000000u) == 0x80000000u;
        }

        struct MeshSkinningRequestWithSkeletonTarget
        {
            public Entity              skeletonEntity;
            public MeshSkinningRequest request;
        }

        struct SkinningStreamHeader
        {
            public enum LoadOp : byte
            {
                Virtual = 0,
                Qvvs = 1,
                Matrix = 2,
                Dqs = 3,
                Current = 0x10,
                Previous = 0x20,
                TwoAgo = 0x30,
                LargeSkeleton = 0x80,
                OpMask = 0x03,
                HistoryMask = 0x30
            }

            public uint   meshCommandCount;
            public short  boneTransformCount;  // If less than actual bones, then offsets should be prebaked
            public byte   indexInSkeletonChunk;
            public LoadOp loadOp;
        }

        struct SkinningStreamMeshCommand
        {
            public enum BatchOp : byte
            {
                CvtGsQvvsToMatReplaceGs = 0,
                MulGsMatWithOffsetBindposesStoreGs = 1,
                MulGsMatWithBindposesStoreGs = 2,
                LoadQvvsMulMatWithOffsetBindposesStoreGs = 3,
                LoadQvvsMulMatWithBindposesStoreGs = 4,
                GsTfStoreDst = 5,
                SkinMatVertInSrc = 6,
                SkinMatVertInDst = 7,
                CvtGsQvvsToDqsWithOffsetStoreDst = 8,
                CvtGsQvvsToDqsWithOffsetStoreGsCopyBindposeToGs = 9,
                LoadCvtQvvsToDqsWithOffsetStoreGsCopyBindposeToGs = 10,
                LoadBindposeDqsStoreGs = 11,
                CvtGsQvvsToDqsWithOffsetStoreGs = 12,
                CvtGsQvvsToDqsStoreGs = 13,
                LoadCvtQvvsToDqsWithOffsetStoreGs = 14,
                LoadCvtQvvsToDqsStoreGs = 15,
                SkinDqsBindPoseVertInSrc = 17,
                SkinDqsBindPoseVertInDst = 18,
                SkinDqsWorldVertInDst = 19,
                UseSkeletonCountAsGsBaseAddress = 0x80,
                OpMask = 0x7f
            }

            public enum LargeSkeletonExpansionOp : byte
            {
                MatsWithOffsets = 0,
                Mats = 1,
                DqsWorldWithOffsets = 2,
                DqsWorld = 3,
            }

            public enum LargeSkeletonSkinningOp : byte
            {
                MatVertInSrc = 0,
                MatVertInDst = 1,
                DqsVertInSrc = 2,
                DqsVertInDst = 3,
            }

            public enum LargeSkeletonOptions : byte
            {
                TransformsOnly = 1,
                TransformsUsePrefixSum = 2,
                TransformsFromShader = 3,
            }

            public int                      indexInDependentBuffer;
            public uint                     gpuDstStart;  // for large skeletons, this is the expansion pass dst
            public uint                     largeSkeletonMeshDstStart;
            public BatchOp                  batchOp;
            public LargeSkeletonExpansionOp largeSkeletonExpansionOp;
            public LargeSkeletonSkinningOp  largeSkeletonSkinningOp;
            public LargeSkeletonOptions     largeSkeletonOptions;
        }

        internal struct PerChunkPrefixSums
        {
            public uint boneTransformsToUpload;
            public uint batchSkinningHeadersCount;
            public uint batchSkinningMeshCommandsCount;
            public uint expansionHeadersCount;
            public uint expansionMeshCommandsCount;
            public uint meshSkinningCommandsCount;
            public uint meshSkinningExtraBoneTransformsCount;
        }

        internal struct BufferLayouts
        {
            public uint requiredMetaSize;
            public uint requiredUploadTransforms;
            public uint requiredMeshSkinningExtraTransforms;

            // these are all absolute offsets in sizes of uint4
            public uint batchSkinningMeshCommandsStart;
            public uint expansionHeadersStart;
            public uint expansionMeshCommandsStart;
            public uint meshSkinningCommandsStart;

            public uint meshSkinningExtraBoneTransformsStart;

            public uint batchSkinningHeadersCount;
            public uint expansionHeadersCount;
            public uint meshSkinningCommandsCount;
        }
        #endregion

        #region Collect Jobs
        [BurstCompile]
        struct FindMeshChunksNeedingSkinningJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<ChunkPerDispatchCullingMask> perDispatchCullingMaskHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkPerFrameCullingMask>    perFrameCullingMaskHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkHeader>                 chunkHeaderHandle;
            [ReadOnly] public ComponentTypeHandle<SkeletonDependent>           skeletonDependentHandle;

            public NativeList<ArchetypeChunk>.ParallelWriter chunksToProcess;

            [Unity.Burst.CompilerServices.SkipLocalsInit]
            public unsafe void Execute(in ArchetypeChunk metaChunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var chunksCache   = stackalloc ArchetypeChunk[128];
                int chunksCount   = 0;
                var dispatchMasks = metaChunk.GetNativeArray(ref perDispatchCullingMaskHandle);
                var frameMasks    = metaChunk.GetNativeArray(ref perFrameCullingMaskHandle);
                var headers       = metaChunk.GetNativeArray(ref chunkHeaderHandle);
                for (int i = 0; i < metaChunk.Count; i++)
                {
                    var dispatchMask = dispatchMasks[i];
                    var frameMask    = frameMasks[i];
                    var lower        = dispatchMask.lower.Value & (~frameMask.lower.Value);
                    var upper        = dispatchMask.upper.Value & (~frameMask.upper.Value);
                    if ((lower | upper) != 0 && headers[i].ArchetypeChunk.Has(ref skeletonDependentHandle))
                    {
                        chunksCache[chunksCount] = headers[i].ArchetypeChunk;
                        chunksCount++;
                    }
                }

                if (chunksCount > 0)
                {
                    chunksToProcess.AddRangeNoResize(chunksCache, chunksCount);
                }
            }
        }

        [BurstCompile]
        struct CollectVisibleMeshesJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<ArchetypeChunk> chunksToProcess;

            [ReadOnly] public ComponentTypeHandle<ChunkPerDispatchCullingMask> perDispatchMaskHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkPerFrameCullingMask>    perFrameMaskHandle;
            [ReadOnly] public ComponentTypeHandle<SkeletonDependent>           skeletonDependentHandle;

            [ReadOnly] public NativeParallelHashMap<ArchetypeChunk, DeformClassification> deformClassificationMap;

            [ReadOnly] public ComponentTypeHandle<LegacyLinearBlendSkinningShaderIndex> legacyLbsHandle;
            [ReadOnly] public ComponentTypeHandle<LegacyComputeDeformShaderIndex>       legacyComputeDeformHandle;
            [ReadOnly] public ComponentTypeHandle<LegacyDotsDeformParamsShaderIndex>    legacyDotsDeformHandle;

            [ReadOnly] public ComponentTypeHandle<CurrentMatrixVertexSkinningShaderIndex>  currentMatrixVertexHandle;
            [ReadOnly] public ComponentTypeHandle<PreviousMatrixVertexSkinningShaderIndex> previousMatrixVertexHandle;
            [ReadOnly] public ComponentTypeHandle<TwoAgoMatrixVertexSkinningShaderIndex>   twoAgoMatrixVertexHandle;
            [ReadOnly] public ComponentTypeHandle<CurrentDqsVertexSkinningShaderIndex>     currentDqsVertexHandle;
            [ReadOnly] public ComponentTypeHandle<PreviousDqsVertexSkinningShaderIndex>    previousDqsVertexHandle;
            [ReadOnly] public ComponentTypeHandle<TwoAgoDqsVertexSkinningShaderIndex>      twoAgoDqsVertexHandle;

            [ReadOnly] public ComponentTypeHandle<CurrentDeformShaderIndex>  currentDeformHandle;
            [ReadOnly] public ComponentTypeHandle<PreviousDeformShaderIndex> previousDeformHandle;
            [ReadOnly] public ComponentTypeHandle<TwoAgoDeformShaderIndex>   twoAgoDeformHandle;

            public UnsafeParallelBlockList requestsBlockList;

            [NativeSetThreadIndex]
            int m_nativeThreadIndex;

            public void Execute(int i)
            {
                Execute(chunksToProcess[i]);
            }

            void Execute(in ArchetypeChunk chunk)
            {
                var dispatchMask = chunk.GetChunkComponentData(ref perDispatchMaskHandle);
                var frameMask    = chunk.GetChunkComponentData(ref perFrameMaskHandle);
                var lower        = dispatchMask.lower.Value & (~frameMask.lower.Value);
                var upper        = dispatchMask.upper.Value & (~frameMask.upper.Value);

                var depsArray      = chunk.GetNativeArray(ref skeletonDependentHandle);
                var classification = deformClassificationMap[chunk];

                if ((classification & DeformClassification.CurrentVertexMatrix) != DeformClassification.None)
                {
                    var indices    = chunk.GetNativeArray(ref currentMatrixVertexHandle);
                    var enumerator = new ChunkEntityEnumerator(true, new v128(lower, upper), chunk.Count);
                    while (enumerator.NextEntityIndex(out int i))
                    {
                        requestsBlockList.Write(new MeshSkinningRequestWithSkeletonTarget
                        {
                            skeletonEntity = depsArray[i].root,
                            request        = new MeshSkinningRequest
                            {
                                indexInSkeletonBufferShaderUsageHigh8 = ((uint)MeshSkinningRequest.ShaderUsage.CurrentMatrixVertex << 24) | (uint)depsArray[i].indexInDependentSkinnedMeshesBuffer,
                                shaderDstIndex                        = indices[i].firstMatrixIndex
                            }
                        }, m_nativeThreadIndex);
                    }
                }
                else if ((classification & DeformClassification.LegacyLbs) != DeformClassification.None)
                {
                    var indices    = chunk.GetNativeArray(ref legacyLbsHandle);
                    var enumerator = new ChunkEntityEnumerator(true, new v128(lower, upper), chunk.Count);
                    while (enumerator.NextEntityIndex(out int i))
                    {
                        requestsBlockList.Write(new MeshSkinningRequestWithSkeletonTarget
                        {
                            skeletonEntity = depsArray[i].root,
                            request        = new MeshSkinningRequest
                            {
                                indexInSkeletonBufferShaderUsageHigh8 = ((uint)MeshSkinningRequest.ShaderUsage.CurrentMatrixVertex << 24) | (uint)depsArray[i].indexInDependentSkinnedMeshesBuffer,
                                shaderDstIndex                        = indices[i].firstMatrixIndex
                            }
                        }, m_nativeThreadIndex);
                    }
                }
                if ((classification & DeformClassification.PreviousVertexMatrix) != DeformClassification.None)
                {
                    var indices    = chunk.GetNativeArray(ref previousMatrixVertexHandle);
                    var enumerator = new ChunkEntityEnumerator(true, new v128(lower, upper), chunk.Count);
                    while (enumerator.NextEntityIndex(out int i))
                    {
                        requestsBlockList.Write(new MeshSkinningRequestWithSkeletonTarget
                        {
                            skeletonEntity = depsArray[i].root,
                            request        = new MeshSkinningRequest
                            {
                                indexInSkeletonBufferShaderUsageHigh8 = ((uint)MeshSkinningRequest.ShaderUsage.PreviousMatrixVertex << 24) | (uint)depsArray[i].indexInDependentSkinnedMeshesBuffer,
                                shaderDstIndex                        = indices[i].firstMatrixIndex
                            }
                        }, m_nativeThreadIndex);
                    }
                }
                if ((classification & DeformClassification.TwoAgoVertexMatrix) != DeformClassification.None)
                {
                    var indices    = chunk.GetNativeArray(ref twoAgoMatrixVertexHandle);
                    var enumerator = new ChunkEntityEnumerator(true, new v128(lower, upper), chunk.Count);
                    while (enumerator.NextEntityIndex(out int i))
                    {
                        requestsBlockList.Write(new MeshSkinningRequestWithSkeletonTarget
                        {
                            skeletonEntity = depsArray[i].root,
                            request        = new MeshSkinningRequest
                            {
                                indexInSkeletonBufferShaderUsageHigh8 = ((uint)MeshSkinningRequest.ShaderUsage.TwoAgoMatrixVertex << 24) | (uint)depsArray[i].indexInDependentSkinnedMeshesBuffer,
                                shaderDstIndex                        = indices[i].firstMatrixIndex
                            }
                        }, m_nativeThreadIndex);
                    }
                }
                if ((classification & DeformClassification.CurrentVertexDqs) != DeformClassification.None)
                {
                    var indices    = chunk.GetNativeArray(ref currentDqsVertexHandle);
                    var enumerator = new ChunkEntityEnumerator(true, new v128(lower, upper), chunk.Count);
                    while (enumerator.NextEntityIndex(out int i))
                    {
                        requestsBlockList.Write(new MeshSkinningRequestWithSkeletonTarget
                        {
                            skeletonEntity = depsArray[i].root,
                            request        = new MeshSkinningRequest
                            {
                                indexInSkeletonBufferShaderUsageHigh8 = ((uint)MeshSkinningRequest.ShaderUsage.CurrentDqsVertex << 24) | (uint)depsArray[i].indexInDependentSkinnedMeshesBuffer,
                                shaderDstIndex                        = indices[i].firstDqsWorldIndex
                            }
                        }, m_nativeThreadIndex);
                    }
                }
                if ((classification & DeformClassification.PreviousVertexDqs) != DeformClassification.None)
                {
                    var indices    = chunk.GetNativeArray(ref previousDqsVertexHandle);
                    var enumerator = new ChunkEntityEnumerator(true, new v128(lower, upper), chunk.Count);
                    while (enumerator.NextEntityIndex(out int i))
                    {
                        requestsBlockList.Write(new MeshSkinningRequestWithSkeletonTarget
                        {
                            skeletonEntity = depsArray[i].root,
                            request        = new MeshSkinningRequest
                            {
                                indexInSkeletonBufferShaderUsageHigh8 = ((uint)MeshSkinningRequest.ShaderUsage.PreviousDqsVertex << 24) | (uint)depsArray[i].indexInDependentSkinnedMeshesBuffer,
                                shaderDstIndex                        = indices[i].firstDqsWorldIndex
                            }
                        }, m_nativeThreadIndex);
                    }
                }
                if ((classification & DeformClassification.TwoAgoVertexDqs) != DeformClassification.None)
                {
                    var indices    = chunk.GetNativeArray(ref twoAgoDqsVertexHandle);
                    var enumerator = new ChunkEntityEnumerator(true, new v128(lower, upper), chunk.Count);
                    while (enumerator.NextEntityIndex(out int i))
                    {
                        requestsBlockList.Write(new MeshSkinningRequestWithSkeletonTarget
                        {
                            skeletonEntity = depsArray[i].root,
                            request        = new MeshSkinningRequest
                            {
                                indexInSkeletonBufferShaderUsageHigh8 = ((uint)MeshSkinningRequest.ShaderUsage.TwoAgoDqsVertex << 24) | (uint)depsArray[i].indexInDependentSkinnedMeshesBuffer,
                                shaderDstIndex                        = indices[i].firstDqsWorldIndex
                            }
                        }, m_nativeThreadIndex);
                    }
                }
                bool vertsInDst = (classification & (DeformClassification.RequiresUploadDynamicMesh | DeformClassification.RequiresGpuComputeBlendShapes)) !=
                                  DeformClassification.None;
                bool isDqs = (classification & DeformClassification.RequiresGpuComputeDqsSkinning) != DeformClassification.None;
                if ((classification & DeformClassification.CurrentDeform) != DeformClassification.None)
                {
                    var indices     = chunk.GetNativeArray(ref currentDeformHandle);
                    var enumerator  = new ChunkEntityEnumerator(true, new v128(lower, upper), chunk.Count);
                    var usage       = isDqs ? MeshSkinningRequest.ShaderUsage.CurrentDqsDeform : MeshSkinningRequest.ShaderUsage.CurrentMatrixDeform;
                    usage          |= vertsInDst ? MeshSkinningRequest.ShaderUsage.UseVerticesInDst : usage;
                    while (enumerator.NextEntityIndex(out int i))
                    {
                        requestsBlockList.Write(new MeshSkinningRequestWithSkeletonTarget
                        {
                            skeletonEntity = depsArray[i].root,
                            request        = new MeshSkinningRequest
                            {
                                indexInSkeletonBufferShaderUsageHigh8 = ((uint)usage << 24) | (uint)depsArray[i].indexInDependentSkinnedMeshesBuffer,
                                shaderDstIndex                        = indices[i].firstVertexIndex
                            }
                        }, m_nativeThreadIndex);
                    }
                }
                else if ((classification & DeformClassification.LegacyCompute) != DeformClassification.None)
                {
                    var indices     = chunk.GetNativeArray(ref legacyComputeDeformHandle);
                    var enumerator  = new ChunkEntityEnumerator(true, new v128(lower, upper), chunk.Count);
                    var usage       = MeshSkinningRequest.ShaderUsage.CurrentMatrixDeform;
                    usage          |= vertsInDst ? MeshSkinningRequest.ShaderUsage.UseVerticesInDst : usage;
                    while (enumerator.NextEntityIndex(out int i))
                    {
                        requestsBlockList.Write(new MeshSkinningRequestWithSkeletonTarget
                        {
                            skeletonEntity = depsArray[i].root,
                            request        = new MeshSkinningRequest
                            {
                                indexInSkeletonBufferShaderUsageHigh8 = ((uint)usage << 24) | (uint)depsArray[i].indexInDependentSkinnedMeshesBuffer,
                                shaderDstIndex                        = indices[i].firstVertexIndex
                            }
                        }, m_nativeThreadIndex);
                    }
                }
                else if ((classification & DeformClassification.LegacyDotsDefom) != DeformClassification.None)
                {
                    var indices     = chunk.GetNativeArray(ref legacyDotsDeformHandle);
                    var enumerator  = new ChunkEntityEnumerator(true, new v128(lower, upper), chunk.Count);
                    var usage       = MeshSkinningRequest.ShaderUsage.CurrentMatrixDeform;
                    usage          |= vertsInDst ? MeshSkinningRequest.ShaderUsage.UseVerticesInDst : usage;
                    while (enumerator.NextEntityIndex(out int i))
                    {
                        requestsBlockList.Write(new MeshSkinningRequestWithSkeletonTarget
                        {
                            skeletonEntity = depsArray[i].root,
                            request        = new MeshSkinningRequest
                            {
                                indexInSkeletonBufferShaderUsageHigh8 = ((uint)usage << 24) | (uint)depsArray[i].indexInDependentSkinnedMeshesBuffer,
                                shaderDstIndex                        = indices[i].parameters.x
                            }
                        }, m_nativeThreadIndex);
                    }
                }
                if ((classification & DeformClassification.PreviousDeform) != DeformClassification.None)
                {
                    var indices     = chunk.GetNativeArray(ref previousDeformHandle);
                    var enumerator  = new ChunkEntityEnumerator(true, new v128(lower, upper), chunk.Count);
                    var usage       = isDqs ? MeshSkinningRequest.ShaderUsage.PreviousDqsDeform : MeshSkinningRequest.ShaderUsage.PreviousMatrixDeform;
                    usage          |= vertsInDst ? MeshSkinningRequest.ShaderUsage.UseVerticesInDst : usage;
                    while (enumerator.NextEntityIndex(out int i))
                    {
                        requestsBlockList.Write(new MeshSkinningRequestWithSkeletonTarget
                        {
                            skeletonEntity = depsArray[i].root,
                            request        = new MeshSkinningRequest
                            {
                                indexInSkeletonBufferShaderUsageHigh8 = ((uint)usage << 24) | (uint)depsArray[i].indexInDependentSkinnedMeshesBuffer,
                                shaderDstIndex                        = indices[i].firstVertexIndex
                            }
                        }, m_nativeThreadIndex);
                    }
                }
                else if ((classification & DeformClassification.LegacyDotsDefom) != DeformClassification.None)
                {
                    var indices     = chunk.GetNativeArray(ref legacyDotsDeformHandle);
                    var enumerator  = new ChunkEntityEnumerator(true, new v128(lower, upper), chunk.Count);
                    var usage       = MeshSkinningRequest.ShaderUsage.PreviousMatrixDeform;
                    usage          |= vertsInDst ? MeshSkinningRequest.ShaderUsage.UseVerticesInDst : usage;
                    while (enumerator.NextEntityIndex(out int i))
                    {
                        requestsBlockList.Write(new MeshSkinningRequestWithSkeletonTarget
                        {
                            skeletonEntity = depsArray[i].root,
                            request        = new MeshSkinningRequest
                            {
                                indexInSkeletonBufferShaderUsageHigh8 = ((uint)usage << 24) | (uint)depsArray[i].indexInDependentSkinnedMeshesBuffer,
                                shaderDstIndex                        = indices[i].parameters.y
                            }
                        }, m_nativeThreadIndex);
                    }
                }
                if ((classification & DeformClassification.TwoAgoDeform) != DeformClassification.None)
                {
                    var indices     = chunk.GetNativeArray(ref twoAgoDeformHandle);
                    var enumerator  = new ChunkEntityEnumerator(true, new v128(lower, upper), chunk.Count);
                    var usage       = isDqs ? MeshSkinningRequest.ShaderUsage.TwoAgoDqsDeform : MeshSkinningRequest.ShaderUsage.TwoAgoMatrixDeform;
                    usage          |= vertsInDst ? MeshSkinningRequest.ShaderUsage.UseVerticesInDst : usage;
                    while (enumerator.NextEntityIndex(out int i))
                    {
                        requestsBlockList.Write(new MeshSkinningRequestWithSkeletonTarget
                        {
                            skeletonEntity = depsArray[i].root,
                            request        = new MeshSkinningRequest
                            {
                                indexInSkeletonBufferShaderUsageHigh8 = ((uint)usage << 24) | (uint)depsArray[i].indexInDependentSkinnedMeshesBuffer,
                                shaderDstIndex                        = indices[i].firstVertexIndex
                            }
                        }, m_nativeThreadIndex);
                    }
                }
            }
        }

        [BurstCompile]
        struct GroupRequestsBySkeletonJob : IJob
        {
            public UnsafeParallelBlockList requestsBlockList;

            public NativeList<MeshSkinningRequest> groupedSkinningRequests;
            public NativeList<int2>                groupedSkinningRequestsStartsAndCounts;
            public NativeHashMap<Entity, int>      skeletonEntityToSkinningRequestsGroupMap;

            public void Execute()
            {
                var count = requestsBlockList.Count();
                if (count == 0)
                    return;
                var dstIndices                                    = new NativeArray<int>(count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                skeletonEntityToSkinningRequestsGroupMap.Capacity = count;

                int skeletonCount = 0;
                {
                    int    i                = 0;
                    Entity previousSkeleton = Entity.Null;
                    var    enumerator       = requestsBlockList.GetEnumerator();
                    while (enumerator.MoveNext())
                    {
                        var request = enumerator.GetCurrent<MeshSkinningRequestWithSkeletonTarget>();
                        if (request.skeletonEntity == previousSkeleton)
                        {
                            dstIndices[i] = dstIndices[i - 1];
                        }
                        else if (skeletonEntityToSkinningRequestsGroupMap.TryGetValue(request.skeletonEntity, out var skeletonIndex))
                        {
                            dstIndices[i]    = skeletonIndex;
                            previousSkeleton = request.skeletonEntity;
                        }
                        else
                        {
                            dstIndices[i]    = skeletonCount;
                            previousSkeleton = request.skeletonEntity;
                            skeletonEntityToSkinningRequestsGroupMap.Add(request.skeletonEntity, skeletonCount);
                            skeletonCount++;
                        }
                        i++;
                    }
                }

                groupedSkinningRequestsStartsAndCounts.ResizeUninitialized(skeletonCount);
                var startsAndCounts = groupedSkinningRequestsStartsAndCounts.AsArray();
                var sums            = new NativeArray<int>(skeletonCount, Allocator.Temp);
                for (int i = 0; i < count; i++)
                {
                    sums[dstIndices[i]]++;
                }

                int totalProcessed = 0;
                for (int i = 0; i < skeletonCount; i++)
                {
                    startsAndCounts[i]  = new int2(totalProcessed, sums[i]);
                    totalProcessed     += sums[i];
                    sums[i]             = 0;
                }

                for (int i = 0; i < count; i++)
                {
                    int skeletonIndex = dstIndices[i];
                    dstIndices[i]     = startsAndCounts[skeletonIndex].x + sums[skeletonIndex];
                    sums[skeletonIndex]++;
                }

                groupedSkinningRequests.ResizeUninitialized(count);
                var requests = groupedSkinningRequests.AsArray();
                {
                    int i          = 0;
                    var enumerator = requestsBlockList.GetEnumerator();
                    while (enumerator.MoveNext())
                    {
                        requests[dstIndices[i]] = enumerator.GetCurrent<MeshSkinningRequestWithSkeletonTarget>().request;
                        i++;
                    }
                }
            }
        }

        [BurstCompile]
        struct GenerateSkinningCommandsJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle                       entityHandle;
            [ReadOnly] public BufferTypeHandle<DependentSkinnedMesh> skinnedMeshesBufferHandle;

            [ReadOnly] public BufferTypeHandle<BoneReference>          boneReferenceBufferHandle;
            [ReadOnly] public BufferTypeHandle<OptimizedBoneTransform> optimizedBoneBufferHandle;

            [ReadOnly] public NativeHashMap<Entity, int>                                  skeletonEntityToSkinningRequestsGroupMap;
            [ReadOnly] public NativeArray<int2>                                           groupedSkinningRequestsStartsAndCounts;
            [NativeDisableParallelForRestriction] public NativeArray<MeshSkinningRequest> groupedSkinningRequests;  // Mostly read, but requires sorting.

            [NativeDisableParallelForRestriction] public NativeStream.Writer             skinningStream;
            [NativeDisableParallelForRestriction] public NativeArray<PerChunkPrefixSums> perChunkPrefixSums;

            PerChunkPrefixSums chunkPrefixSums;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                if (groupedSkinningRequests.Length == 0)
                {
                    perChunkPrefixSums[unfilteredChunkIndex] = default;
                    return;
                }

                skinningStream.BeginForEachIndex(unfilteredChunkIndex);
                chunkPrefixSums = default;

                if (chunk.Has(ref boneReferenceBufferHandle))
                {
                    ProcessExposed(in chunk);
                }
                else if (chunk.Has(ref optimizedBoneBufferHandle))
                {
                    ProcessOptimized(in chunk);
                }

                perChunkPrefixSums[unfilteredChunkIndex] = chunkPrefixSums;
                skinningStream.EndForEachIndex();
            }

            void ProcessExposed(in ArchetypeChunk chunk)
            {
                var entityArray           = chunk.GetNativeArray(entityHandle);
                var skinnedMeshesAccessor = chunk.GetBufferAccessor(ref skinnedMeshesBufferHandle);
                var boneBufferAccessor    = chunk.GetBufferAccessor(ref boneReferenceBufferHandle);

                for (int i = 0; i < chunk.Count; i++)
                {
                    //var mask = i >= 64 ? dispatchMask.upper : dispatchMask.lower;
                    //if (mask.IsSet(i % 64))
                    {
                        var boneCount = boneBufferAccessor[i].Length;
                        ProcessRequests(entityArray[i], skinnedMeshesAccessor[i].AsNativeArray(), i, boneCount);
                    }
                }
            }

            void ProcessOptimized(in ArchetypeChunk chunk)
            {
                var entityArray           = chunk.GetNativeArray(entityHandle);
                var skinnedMeshesAccessor = chunk.GetBufferAccessor(ref skinnedMeshesBufferHandle);
                var boneBufferAccessor    = chunk.GetBufferAccessor(ref optimizedBoneBufferHandle);

                for (int i = 0; i < chunk.Count; i++)
                {
                    //var mask = i >= 64 ? dispatchMask.upper : dispatchMask.lower;
                    //if (mask.IsSet(i % 64))
                    {
                        var boneCount = boneBufferAccessor[i].Length / 6;
                        ProcessRequests(entityArray[i], skinnedMeshesAccessor[i].AsNativeArray(), i, boneCount);
                    }
                }
            }

            void ProcessRequests(Entity skeletonEntity, NativeArray<DependentSkinnedMesh> meshes, int indexInChunk, int skeletonBonesCount)
            {
                if (!skeletonEntityToSkinningRequestsGroupMap.TryGetValue(skeletonEntity, out var startAndCountIndex))
                    return;

                var startAndCount = groupedSkinningRequestsStartsAndCounts[startAndCountIndex];
                var requests      = groupedSkinningRequests.GetSubArray(startAndCount.x, startAndCount.y);

                int startOfPrevious = -1;
                int startOfTwoAgo   = -1;
                int countToRemove   = 0;
                for (int i = 0; i < requests.Length - countToRemove; i++)
                {
                    var request = requests[i];

                    // Validate bone count
                    var index     = request.indexInSkeletonBuffer;
                    var meshBones = meshes[(int)index].boneOffsetsCount;
                    if (meshBones > skeletonBonesCount)
                    {
                        UnityEngine.Debug.LogError($"Skeleton {skeletonEntity} does not have enough bones ({skeletonBonesCount} to skin the mesh requiring {meshBones} bones.");
                        requests[i] = requests[requests.Length - 1 - countToRemove];
                        countToRemove++;
                        i--;
                        continue;
                    }

                    // Check for starts of history chains
                    var usageHistory = (request.indexInSkeletonBufferShaderUsageHigh8 >> 26) & 0x03;
                    if (startOfPrevious == -1 && usageHistory >= 1)
                        startOfPrevious = i;
                    if (startOfTwoAgo == -1 && usageHistory == 2)
                        startOfTwoAgo = i;
                }
                if (countToRemove > 0)
                    requests = requests.GetSubArray(0, requests.Length - countToRemove);

                requests.Sort(new RequestSorter { meshes = meshes });

                if (startOfPrevious < 0)
                    ProcessChain(requests, meshes, indexInChunk, skeletonBonesCount);
                else if (startOfPrevious > 0)
                    ProcessChain(requests.GetSubArray(0, startOfPrevious), meshes, indexInChunk, skeletonBonesCount);
                if (startOfTwoAgo < 0 && startOfPrevious >= 0)
                    ProcessChain(requests.GetSubArray(startOfPrevious, requests.Length - startOfPrevious), meshes, indexInChunk, skeletonBonesCount);
                else if (startOfPrevious >= 0 && startOfPrevious != startOfTwoAgo)
                    ProcessChain(requests.GetSubArray(startOfPrevious, startOfTwoAgo), meshes, indexInChunk, skeletonBonesCount);
                if (startOfTwoAgo >= 0)
                    ProcessChain(requests.GetSubArray(startOfTwoAgo, requests.Length - startOfTwoAgo), meshes, indexInChunk, skeletonBonesCount);
            }

            void ProcessChain(NativeArray<MeshSkinningRequest> requests, NativeArray<DependentSkinnedMesh> meshes, int indexInChunk, int skeletonBonesCount)
            {
                uint firstMeshIndex    = requests[0].indexInSkeletonBuffer;
                uint maxMeshBoneCount  = meshes[(int)firstMeshIndex].boneOffsetsCount;
                bool hasMultipleMeshes = false;

                for (int i = 1; i < requests.Length; i++)
                {
                    var  meshIndex     = requests[i].indexInSkeletonBuffer;
                    uint boneCount     = meshes[(int)meshIndex].boneOffsetsCount;
                    hasMultipleMeshes |= meshIndex != firstMeshIndex;
                    maxMeshBoneCount   = math.max(boneCount, maxMeshBoneCount);
                }

                if (hasMultipleMeshes)
                {
                    if (maxMeshBoneCount > kBatchThreshold)
                        BuildCommandsMultiMeshExpanded(requests, meshes, indexInChunk, skeletonBonesCount);
                    else
                        BuildCommandsMultiMeshBatched(requests, meshes, indexInChunk, skeletonBonesCount);
                }
                else
                {
                    if (maxMeshBoneCount > kBatchThreshold)
                        BuildCommandsSingleMeshExpanded(requests, meshes, indexInChunk, skeletonBonesCount);
                    else
                        BuildCommandsSingleMeshBatched(requests, meshes, indexInChunk, skeletonBonesCount);
                }
            }

            unsafe void BuildCommandsSingleMeshBatched(NativeArray<MeshSkinningRequest>  requests,
                                                       NativeArray<DependentSkinnedMesh> meshes,
                                                       int indexInChunk,
                                                       int skeletonBonesCount)
            {
                ref var header = ref skinningStream.Allocate<SkinningStreamHeader>();
                header         = new SkinningStreamHeader
                {
                    boneTransformCount   = (short)meshes[(int)requests[0].indexInSkeletonBuffer].boneOffsetsCount,
                    meshCommandCount     = 0,
                    indexInSkeletonChunk = (byte)indexInChunk,
                    loadOp               = SkinningStreamHeader.LoadOp.Virtual | HistoryFromShaderUsage(requests[0].indexInSkeletonBufferShaderUsageHigh8)
                };
                chunkPrefixSums.batchSkinningHeadersCount++;
                chunkPrefixSums.boneTransformsToUpload += (uint)header.boneTransformCount;

                int indexInDependentBuffer = (int)requests[0].indexInSkeletonBuffer;

                if (header.boneTransformCount > 341)
                {
                    // We know we cannot share QVVS transforms
                    if (requests[0].isDqsDeform)
                    {
                        // The load op needs to be virtual, because we need to skin the bindposes first.
                        skinningStream.Write(new SkinningStreamMeshCommand
                        {
                            batchOp                = SkinningStreamMeshCommand.BatchOp.LoadBindposeDqsStoreGs,
                            indexInDependentBuffer = indexInDependentBuffer,
                            gpuDstStart            = requests[0].shaderDstIndex,
                        });
                        header.meshCommandCount++;
                        bool firstUsageRequiresVertsInDst = requests[0].useVerticesInDst;
                        skinningStream.Write(new SkinningStreamMeshCommand
                        {
                            batchOp                = firstUsageRequiresVertsInDst ? SkinningStreamMeshCommand.BatchOp.SkinDqsBindPoseVertInDst : SkinningStreamMeshCommand.BatchOp.SkinDqsBindPoseVertInSrc,
                            indexInDependentBuffer = indexInDependentBuffer,
                            gpuDstStart            = requests[0].shaderDstIndex
                        });
                        header.meshCommandCount++;
                        skinningStream.Write(new SkinningStreamMeshCommand
                        {
                            batchOp                = SkinningStreamMeshCommand.BatchOp.LoadCvtQvvsToDqsStoreGs,
                            indexInDependentBuffer = indexInDependentBuffer,
                            gpuDstStart            = requests[0].shaderDstIndex
                        });
                        header.meshCommandCount++;
                        skinningStream.Write(new SkinningStreamMeshCommand
                        {
                            batchOp                = SkinningStreamMeshCommand.BatchOp.SkinDqsWorldVertInDst,
                            indexInDependentBuffer = indexInDependentBuffer,
                            gpuDstStart            = requests[0].shaderDstIndex
                        });
                        header.meshCommandCount++;
                    }
                    else if (requests[0].isDqsVertex)
                    {
                        header.loadOp |= SkinningStreamHeader.LoadOp.Dqs;
                        skinningStream.Write(new SkinningStreamMeshCommand
                        {
                            batchOp                = SkinningStreamMeshCommand.BatchOp.GsTfStoreDst,
                            indexInDependentBuffer = indexInDependentBuffer,
                            gpuDstStart            = requests[0].shaderDstIndex
                        });
                        header.meshCommandCount++;
                    }
                    else if (requests[0].isMatrixDeform)
                    {
                        // The loadOp needs to be virtual, because we need to load the bindposes first to do the matrix multiplication
                        skinningStream.Write(new SkinningStreamMeshCommand
                        {
                            batchOp                = SkinningStreamMeshCommand.BatchOp.LoadQvvsMulMatWithBindposesStoreGs,
                            indexInDependentBuffer = indexInDependentBuffer,
                            gpuDstStart            = requests[0].shaderDstIndex
                        });
                        header.meshCommandCount++;
                        bool firstUsageRequiresVertsInDst = requests[0].useVerticesInDst;
                        skinningStream.Write(new SkinningStreamMeshCommand
                        {
                            batchOp                = firstUsageRequiresVertsInDst ? SkinningStreamMeshCommand.BatchOp.SkinMatVertInDst : SkinningStreamMeshCommand.BatchOp.SkinMatVertInSrc,
                            indexInDependentBuffer = indexInDependentBuffer,
                            gpuDstStart            = requests[0].shaderDstIndex
                        });
                        header.meshCommandCount++;
                    }
                    else  // isMatrixVertex
                    {
                        // The loadOp needs to be virtual, because we need to load the bindposes first to do the matrix multiplication
                        skinningStream.Write(new SkinningStreamMeshCommand
                        {
                            batchOp                = SkinningStreamMeshCommand.BatchOp.LoadQvvsMulMatWithBindposesStoreGs,
                            indexInDependentBuffer = indexInDependentBuffer,
                            gpuDstStart            = requests[0].shaderDstIndex
                        });
                        header.meshCommandCount++;
                        skinningStream.Write(new SkinningStreamMeshCommand
                        {
                            batchOp                = SkinningStreamMeshCommand.BatchOp.GsTfStoreDst,
                            indexInDependentBuffer = indexInDependentBuffer,
                            gpuDstStart            = requests[0].shaderDstIndex
                        });
                        header.meshCommandCount++;
                    }

                    for (int nextRequest = 1; nextRequest < requests.Length; nextRequest++)
                    {
                        if (requests[nextRequest].isDqsVertex)
                        {
                            // This only can happen as the second request.
                            // We know we just dispatched a DQS world deform, so we can immediately write out the world DQS
                            skinningStream.Write(new SkinningStreamMeshCommand
                            {
                                batchOp                = SkinningStreamMeshCommand.BatchOp.GsTfStoreDst,
                                indexInDependentBuffer = indexInDependentBuffer,
                                gpuDstStart            = requests[nextRequest].shaderDstIndex
                            });
                            header.meshCommandCount++;
                        }
                        else if (requests[nextRequest].isMatrixDeform)
                        {
                            skinningStream.Write(new SkinningStreamMeshCommand
                            {
                                batchOp                = SkinningStreamMeshCommand.BatchOp.LoadQvvsMulMatWithBindposesStoreGs,
                                indexInDependentBuffer = indexInDependentBuffer,
                                gpuDstStart            = requests[nextRequest].shaderDstIndex
                            });
                            header.meshCommandCount++;
                            bool nextUsageRequiresVertsInDst = requests[0].useVerticesInDst;
                            skinningStream.Write(new SkinningStreamMeshCommand
                            {
                                batchOp                = nextUsageRequiresVertsInDst ? SkinningStreamMeshCommand.BatchOp.SkinMatVertInDst : SkinningStreamMeshCommand.BatchOp.SkinMatVertInSrc,
                                indexInDependentBuffer = indexInDependentBuffer,
                                gpuDstStart            = requests[nextRequest].shaderDstIndex
                            });
                            header.meshCommandCount++;
                        }
                        else if (requests[nextRequest - 1].isMatrixDeform)
                        {
                            // We just skinned the matrices, so write them out.
                            skinningStream.Write(new SkinningStreamMeshCommand
                            {
                                batchOp                = SkinningStreamMeshCommand.BatchOp.GsTfStoreDst,
                                indexInDependentBuffer = indexInDependentBuffer,
                                gpuDstStart            = requests[nextRequest].shaderDstIndex
                            });
                            header.meshCommandCount++;
                        }
                        else
                        {
                            skinningStream.Write(new SkinningStreamMeshCommand
                            {
                                batchOp                = SkinningStreamMeshCommand.BatchOp.LoadQvvsMulMatWithBindposesStoreGs,
                                indexInDependentBuffer = indexInDependentBuffer,
                                gpuDstStart            = requests[nextRequest].shaderDstIndex
                            });
                            header.meshCommandCount++;
                            skinningStream.Write(new SkinningStreamMeshCommand
                            {
                                batchOp                = SkinningStreamMeshCommand.BatchOp.GsTfStoreDst,
                                indexInDependentBuffer = indexInDependentBuffer,
                                gpuDstStart            = requests[nextRequest].shaderDstIndex
                            });
                            header.meshCommandCount++;
                        }
                    }
                }
                else
                {
                    // If the first is a Dqs, load as Qvvs, otherwise load as Matrix
                    bool firstIsMatrix  = requests[0].isMatrix;
                    header.loadOp      |= firstIsMatrix ? SkinningStreamHeader.LoadOp.Matrix : SkinningStreamHeader.LoadOp.Qvvs;

                    for (int nextRequest = 0; nextRequest < requests.Length; nextRequest++)
                    {
                        if (requests[nextRequest].isDqsDeform)
                        {
                            skinningStream.Write(new SkinningStreamMeshCommand
                            {
                                batchOp                = SkinningStreamMeshCommand.BatchOp.LoadBindposeDqsStoreGs | SkinningStreamMeshCommand.BatchOp.UseSkeletonCountAsGsBaseAddress,
                                indexInDependentBuffer = indexInDependentBuffer,
                                gpuDstStart            = requests[nextRequest].shaderDstIndex,
                            });
                            header.meshCommandCount++;
                            bool requiresVertsInDst = requests[nextRequest].useVerticesInDst;
                            skinningStream.Write(new SkinningStreamMeshCommand
                            {
                                batchOp =
                                    (requiresVertsInDst ? SkinningStreamMeshCommand.BatchOp.SkinDqsBindPoseVertInDst : SkinningStreamMeshCommand.BatchOp.SkinDqsBindPoseVertInSrc) | SkinningStreamMeshCommand.BatchOp.UseSkeletonCountAsGsBaseAddress,
                                indexInDependentBuffer = indexInDependentBuffer,
                                gpuDstStart            = requests[nextRequest].shaderDstIndex
                            });
                            header.meshCommandCount++;
                            skinningStream.Write(new SkinningStreamMeshCommand
                            {
                                batchOp                = SkinningStreamMeshCommand.BatchOp.CvtGsQvvsToDqsStoreGs | SkinningStreamMeshCommand.BatchOp.UseSkeletonCountAsGsBaseAddress,
                                indexInDependentBuffer = indexInDependentBuffer,
                                gpuDstStart            = requests[nextRequest].shaderDstIndex
                            });
                            header.meshCommandCount++;
                            skinningStream.Write(new SkinningStreamMeshCommand
                            {
                                batchOp                = SkinningStreamMeshCommand.BatchOp.SkinDqsWorldVertInDst | SkinningStreamMeshCommand.BatchOp.UseSkeletonCountAsGsBaseAddress,
                                indexInDependentBuffer = indexInDependentBuffer,
                                gpuDstStart            = requests[nextRequest].shaderDstIndex
                            });
                            header.meshCommandCount++;
                        }
                        else if (requests[nextRequest].isDqsVertex)
                        {
                            if (nextRequest > 0 && requests[nextRequest - 1].isDqsDeform)
                            {
                                // We know we just dispatched a DQS world deform, so we can immediately write out the world DQS
                                skinningStream.Write(new SkinningStreamMeshCommand
                                {
                                    batchOp                = SkinningStreamMeshCommand.BatchOp.GsTfStoreDst,
                                    indexInDependentBuffer = indexInDependentBuffer,
                                    gpuDstStart            = requests[nextRequest].shaderDstIndex
                                });
                                header.meshCommandCount++;
                            }
                            else
                            {
                                skinningStream.Write(new SkinningStreamMeshCommand
                                {
                                    batchOp                = SkinningStreamMeshCommand.BatchOp.CvtGsQvvsToDqsStoreGs | SkinningStreamMeshCommand.BatchOp.UseSkeletonCountAsGsBaseAddress,
                                    indexInDependentBuffer = indexInDependentBuffer,
                                    gpuDstStart            = requests[nextRequest].shaderDstIndex
                                });
                                header.meshCommandCount++;
                                skinningStream.Write(new SkinningStreamMeshCommand
                                {
                                    batchOp                = SkinningStreamMeshCommand.BatchOp.GsTfStoreDst,
                                    indexInDependentBuffer = indexInDependentBuffer,
                                    gpuDstStart            = requests[nextRequest].shaderDstIndex
                                });
                                header.meshCommandCount++;
                            }
                        }
                        else if (requests[nextRequest].isMatrixDeform)
                        {
                            if (!firstIsMatrix)
                            {
                                skinningStream.Write(new SkinningStreamMeshCommand
                                {
                                    batchOp                = SkinningStreamMeshCommand.BatchOp.CvtGsQvvsToMatReplaceGs,
                                    indexInDependentBuffer = indexInDependentBuffer,
                                    gpuDstStart            = requests[nextRequest].shaderDstIndex
                                });
                                header.meshCommandCount++;
                            }
                            skinningStream.Write(new SkinningStreamMeshCommand
                            {
                                batchOp                = SkinningStreamMeshCommand.BatchOp.MulGsMatWithBindposesStoreGs,
                                indexInDependentBuffer = indexInDependentBuffer,
                                gpuDstStart            = requests[nextRequest].shaderDstIndex
                            });
                            header.meshCommandCount++;
                            bool requiresVertsInDst = requests[0].useVerticesInDst;
                            skinningStream.Write(new SkinningStreamMeshCommand
                            {
                                batchOp =
                                    (requiresVertsInDst ? SkinningStreamMeshCommand.BatchOp.SkinMatVertInDst : SkinningStreamMeshCommand.BatchOp.SkinMatVertInSrc) | SkinningStreamMeshCommand.BatchOp.UseSkeletonCountAsGsBaseAddress,
                                indexInDependentBuffer = indexInDependentBuffer,
                                gpuDstStart            = requests[nextRequest].shaderDstIndex
                            });
                            header.meshCommandCount++;
                        }
                        else  // isMatrixVertex
                        {
                            if (nextRequest > 0 && requests[nextRequest - 1].isMatrixDeform)
                            {
                                skinningStream.Write(new SkinningStreamMeshCommand
                                {
                                    batchOp                = SkinningStreamMeshCommand.BatchOp.GsTfStoreDst | SkinningStreamMeshCommand.BatchOp.UseSkeletonCountAsGsBaseAddress,
                                    indexInDependentBuffer = indexInDependentBuffer,
                                    gpuDstStart            = requests[nextRequest].shaderDstIndex
                                });
                                header.meshCommandCount++;
                            }
                            else
                            {
                                if (!firstIsMatrix)
                                {
                                    skinningStream.Write(new SkinningStreamMeshCommand
                                    {
                                        batchOp                = SkinningStreamMeshCommand.BatchOp.CvtGsQvvsToMatReplaceGs,
                                        indexInDependentBuffer = indexInDependentBuffer,
                                        gpuDstStart            = requests[nextRequest].shaderDstIndex
                                    });
                                    header.meshCommandCount++;
                                }
                                skinningStream.Write(new SkinningStreamMeshCommand
                                {
                                    batchOp                = SkinningStreamMeshCommand.BatchOp.MulGsMatWithBindposesStoreGs,
                                    indexInDependentBuffer = indexInDependentBuffer,
                                    gpuDstStart            = requests[nextRequest].shaderDstIndex
                                });
                                header.meshCommandCount++;
                                skinningStream.Write(new SkinningStreamMeshCommand
                                {
                                    batchOp                = SkinningStreamMeshCommand.BatchOp.GsTfStoreDst | SkinningStreamMeshCommand.BatchOp.UseSkeletonCountAsGsBaseAddress,
                                    indexInDependentBuffer = indexInDependentBuffer,
                                    gpuDstStart            = requests[nextRequest].shaderDstIndex
                                });
                                header.meshCommandCount++;
                            }
                        }
                    }
                }

                chunkPrefixSums.batchSkinningMeshCommandsCount += header.meshCommandCount;
            }

            unsafe void BuildCommandsMultiMeshBatched(NativeArray<MeshSkinningRequest>  requests,
                                                      NativeArray<DependentSkinnedMesh> meshes,
                                                      int indexInChunk,
                                                      int skeletonBonesCount)
            {
                ref var header = ref skinningStream.Allocate<SkinningStreamHeader>();
                header         = new SkinningStreamHeader
                {
                    boneTransformCount   = (short)skeletonBonesCount,
                    meshCommandCount     = 0,
                    indexInSkeletonChunk = (byte)indexInChunk,
                    loadOp               = SkinningStreamHeader.LoadOp.Virtual | HistoryFromShaderUsage(requests[0].indexInSkeletonBufferShaderUsageHigh8)
                };
                chunkPrefixSums.batchSkinningHeadersCount++;
                chunkPrefixSums.boneTransformsToUpload += (uint)header.boneTransformCount;

                bool requiresDqsBindposePassThatFit     = false;
                bool requiresDqsBindposePassThatDontFit = false;
                int  dqsThatDontFit                     = 0;
                int  dqsThatFit                         = 0;
                int  matricesThatFit                    = 0;
                int  matricesThatDontFit                = 0;

                foreach (var r in requests)
                {
                    if (r.isDqs)
                    {
                        if (meshes[(int)r.indexInSkeletonBuffer].boneOffsetsCount + skeletonBonesCount > 682)
                        {
                            dqsThatDontFit++;
                            if (r.isDqsDeform)
                                requiresDqsBindposePassThatDontFit = true;
                        }
                        else
                        {
                            dqsThatFit++;
                            if (r.isDqsDeform)
                                requiresDqsBindposePassThatFit = true;
                        }
                    }
                    else
                    {
                        if (meshes[(int)r.indexInSkeletonBuffer].boneOffsetsCount + skeletonBonesCount > 682)
                            matricesThatDontFit++;
                        else
                            matricesThatFit++;
                    }
                }

                if (dqsThatFit > 0)
                    header.loadOp |= SkinningStreamHeader.LoadOp.Qvvs;
                else if (matricesThatFit > 0)
                    header.loadOp |= SkinningStreamHeader.LoadOp.Matrix;

                if (requiresDqsBindposePassThatFit)
                {
                    for (int i = 0; i < dqsThatFit; i++)
                    {
                        if (!requests[i].isDqsDeform)
                            continue;

                        skinningStream.Write(new SkinningStreamMeshCommand
                        {
                            batchOp                = SkinningStreamMeshCommand.BatchOp.LoadBindposeDqsStoreGs | SkinningStreamMeshCommand.BatchOp.UseSkeletonCountAsGsBaseAddress,
                            indexInDependentBuffer = (int)requests[i].indexInSkeletonBuffer,
                            gpuDstStart            = requests[i].shaderDstIndex
                        });
                        header.meshCommandCount++;

                        var op = requests[i].useVerticesInDst ? SkinningStreamMeshCommand.BatchOp.SkinDqsBindPoseVertInDst :
                                 SkinningStreamMeshCommand.BatchOp.SkinDqsBindPoseVertInSrc;

                        skinningStream.Write(new SkinningStreamMeshCommand
                        {
                            batchOp                = op | SkinningStreamMeshCommand.BatchOp.UseSkeletonCountAsGsBaseAddress,
                            indexInDependentBuffer = (int)requests[i].indexInSkeletonBuffer,
                            gpuDstStart            = requests[i].shaderDstIndex
                        });
                        header.meshCommandCount++;
                    }
                }

                uint prevMesh = ~0x0u;
                for (int i = 0; i < dqsThatFit; i++)
                {
                    var request = requests[i];
                    if (request.isDqsDeform)
                    {
                        skinningStream.Write(new SkinningStreamMeshCommand
                        {
                            batchOp                = SkinningStreamMeshCommand.BatchOp.CvtGsQvvsToDqsWithOffsetStoreGs | SkinningStreamMeshCommand.BatchOp.UseSkeletonCountAsGsBaseAddress,
                            indexInDependentBuffer = (int)requests[i].indexInSkeletonBuffer,
                            gpuDstStart            = requests[i].shaderDstIndex
                        });
                        header.meshCommandCount++;
                        skinningStream.Write(new SkinningStreamMeshCommand
                        {
                            batchOp                = SkinningStreamMeshCommand.BatchOp.SkinDqsWorldVertInDst | SkinningStreamMeshCommand.BatchOp.UseSkeletonCountAsGsBaseAddress,
                            indexInDependentBuffer = (int)requests[i].indexInSkeletonBuffer,
                            gpuDstStart            = requests[i].shaderDstIndex
                        });
                        header.meshCommandCount++;
                    }
                    else if (request.indexInSkeletonBuffer == prevMesh)
                    {
                        skinningStream.Write(new SkinningStreamMeshCommand
                        {
                            batchOp                = SkinningStreamMeshCommand.BatchOp.GsTfStoreDst | SkinningStreamMeshCommand.BatchOp.UseSkeletonCountAsGsBaseAddress,
                            indexInDependentBuffer = (int)requests[i].indexInSkeletonBuffer,
                            gpuDstStart            = requests[i].shaderDstIndex
                        });
                        header.meshCommandCount++;
                    }
                    else
                    {
                        skinningStream.Write(new SkinningStreamMeshCommand
                        {
                            batchOp                = SkinningStreamMeshCommand.BatchOp.CvtGsQvvsToDqsWithOffsetStoreDst | SkinningStreamMeshCommand.BatchOp.UseSkeletonCountAsGsBaseAddress,
                            indexInDependentBuffer = (int)requests[i].indexInSkeletonBuffer,
                            gpuDstStart            = requests[i].shaderDstIndex
                        });
                        header.meshCommandCount++;
                    }
                    prevMesh = request.indexInSkeletonBuffer;
                }

                // Transition from Qvvs to Matrix
                if (matricesThatFit > 0 && dqsThatFit > 0)
                {
                    skinningStream.Write(new SkinningStreamMeshCommand
                    {
                        batchOp                = SkinningStreamMeshCommand.BatchOp.CvtGsQvvsToMatReplaceGs,
                        indexInDependentBuffer = (int)requests[dqsThatFit + dqsThatDontFit].indexInSkeletonBuffer,
                        gpuDstStart            = requests[dqsThatFit + dqsThatDontFit].shaderDstIndex
                    });
                    header.meshCommandCount++;
                }

                prevMesh = ~0x0u;
                for (int i = dqsThatFit + dqsThatDontFit; i < dqsThatFit + dqsThatDontFit + matricesThatFit; i++)
                {
                    var request = requests[i];
                    if (request.isMatrixDeform)
                    {
                        skinningStream.Write(new SkinningStreamMeshCommand
                        {
                            batchOp                = SkinningStreamMeshCommand.BatchOp.MulGsMatWithOffsetBindposesStoreGs | SkinningStreamMeshCommand.BatchOp.UseSkeletonCountAsGsBaseAddress,
                            indexInDependentBuffer = (int)requests[i].indexInSkeletonBuffer,
                            gpuDstStart            = requests[i].shaderDstIndex
                        });
                        header.meshCommandCount++;
                        var op = request.useVerticesInDst ? SkinningStreamMeshCommand.BatchOp.SkinMatVertInDst : SkinningStreamMeshCommand.BatchOp.SkinMatVertInSrc;
                        skinningStream.Write(new SkinningStreamMeshCommand
                        {
                            batchOp                = op | SkinningStreamMeshCommand.BatchOp.UseSkeletonCountAsGsBaseAddress,
                            indexInDependentBuffer = (int)requests[i].indexInSkeletonBuffer,
                            gpuDstStart            = requests[i].shaderDstIndex
                        });
                        header.meshCommandCount++;
                    }
                    else if (request.indexInSkeletonBuffer == prevMesh)
                    {
                        skinningStream.Write(new SkinningStreamMeshCommand
                        {
                            batchOp                = SkinningStreamMeshCommand.BatchOp.GsTfStoreDst | SkinningStreamMeshCommand.BatchOp.UseSkeletonCountAsGsBaseAddress,
                            indexInDependentBuffer = (int)requests[i].indexInSkeletonBuffer,
                            gpuDstStart            = requests[i].shaderDstIndex
                        });
                        header.meshCommandCount++;
                    }
                    else
                    {
                        skinningStream.Write(new SkinningStreamMeshCommand
                        {
                            batchOp                = SkinningStreamMeshCommand.BatchOp.MulGsMatWithOffsetBindposesStoreGs | SkinningStreamMeshCommand.BatchOp.UseSkeletonCountAsGsBaseAddress,
                            indexInDependentBuffer = (int)requests[i].indexInSkeletonBuffer,
                            gpuDstStart            = requests[i].shaderDstIndex
                        });
                        header.meshCommandCount++;
                        skinningStream.Write(new SkinningStreamMeshCommand
                        {
                            batchOp                = SkinningStreamMeshCommand.BatchOp.GsTfStoreDst | SkinningStreamMeshCommand.BatchOp.UseSkeletonCountAsGsBaseAddress,
                            indexInDependentBuffer = (int)requests[i].indexInSkeletonBuffer,
                            gpuDstStart            = requests[i].shaderDstIndex
                        });
                        header.meshCommandCount++;
                    }
                    prevMesh = request.indexInSkeletonBuffer;
                }

                if (requiresDqsBindposePassThatDontFit)
                {
                    for (int i = dqsThatFit; i < dqsThatFit + dqsThatDontFit; i++)
                    {
                        if (!requests[i].isDqsDeform)
                            continue;

                        skinningStream.Write(new SkinningStreamMeshCommand
                        {
                            batchOp                = SkinningStreamMeshCommand.BatchOp.LoadBindposeDqsStoreGs,
                            indexInDependentBuffer = (int)requests[i].indexInSkeletonBuffer,
                            gpuDstStart            = requests[i].shaderDstIndex
                        });
                        header.meshCommandCount++;

                        var op = requests[i].useVerticesInDst ? SkinningStreamMeshCommand.BatchOp.SkinDqsBindPoseVertInDst :
                                 SkinningStreamMeshCommand.BatchOp.SkinDqsBindPoseVertInSrc;

                        skinningStream.Write(new SkinningStreamMeshCommand
                        {
                            batchOp                = op,
                            indexInDependentBuffer = (int)requests[i].indexInSkeletonBuffer,
                            gpuDstStart            = requests[i].shaderDstIndex
                        });
                        header.meshCommandCount++;
                    }
                }

                prevMesh = ~0x0u;
                for (int i = dqsThatFit; i < dqsThatFit + dqsThatDontFit; i++)
                {
                    var request = requests[i];
                    if (request.isDqsDeform)
                    {
                        skinningStream.Write(new SkinningStreamMeshCommand
                        {
                            batchOp                = SkinningStreamMeshCommand.BatchOp.LoadCvtQvvsToDqsWithOffsetStoreGs,
                            indexInDependentBuffer = (int)requests[i].indexInSkeletonBuffer,
                            gpuDstStart            = requests[i].shaderDstIndex
                        });
                        header.meshCommandCount++;
                        skinningStream.Write(new SkinningStreamMeshCommand
                        {
                            batchOp                = SkinningStreamMeshCommand.BatchOp.SkinDqsWorldVertInDst,
                            indexInDependentBuffer = (int)requests[i].indexInSkeletonBuffer,
                            gpuDstStart            = requests[i].shaderDstIndex
                        });
                        header.meshCommandCount++;
                    }
                    else if (request.indexInSkeletonBuffer == prevMesh)
                    {
                        skinningStream.Write(new SkinningStreamMeshCommand
                        {
                            batchOp                = SkinningStreamMeshCommand.BatchOp.GsTfStoreDst,
                            indexInDependentBuffer = (int)requests[i].indexInSkeletonBuffer,
                            gpuDstStart            = requests[i].shaderDstIndex
                        });
                        header.meshCommandCount++;
                    }
                    else
                    {
                        skinningStream.Write(new SkinningStreamMeshCommand
                        {
                            batchOp                = SkinningStreamMeshCommand.BatchOp.LoadCvtQvvsToDqsWithOffsetStoreGs,
                            indexInDependentBuffer = (int)requests[i].indexInSkeletonBuffer,
                            gpuDstStart            = requests[i].shaderDstIndex
                        });
                        header.meshCommandCount++;
                        skinningStream.Write(new SkinningStreamMeshCommand
                        {
                            batchOp                = SkinningStreamMeshCommand.BatchOp.GsTfStoreDst,
                            indexInDependentBuffer = (int)requests[i].indexInSkeletonBuffer,
                            gpuDstStart            = requests[i].shaderDstIndex
                        });
                        header.meshCommandCount++;
                    }
                    prevMesh = request.indexInSkeletonBuffer;
                }

                for (int i = dqsThatFit + dqsThatDontFit + matricesThatFit; i < dqsThatFit + dqsThatDontFit + matricesThatFit + matricesThatDontFit; i++)
                {
                    var request = requests[i];
                    if (request.isMatrixDeform)
                    {
                        skinningStream.Write(new SkinningStreamMeshCommand
                        {
                            batchOp                = SkinningStreamMeshCommand.BatchOp.LoadQvvsMulMatWithOffsetBindposesStoreGs,
                            indexInDependentBuffer = (int)requests[i].indexInSkeletonBuffer,
                            gpuDstStart            = requests[i].shaderDstIndex
                        });
                        header.meshCommandCount++;
                        var op = request.useVerticesInDst ? SkinningStreamMeshCommand.BatchOp.SkinMatVertInDst : SkinningStreamMeshCommand.BatchOp.SkinMatVertInSrc;
                        skinningStream.Write(new SkinningStreamMeshCommand
                        {
                            batchOp                = op,
                            indexInDependentBuffer = (int)requests[i].indexInSkeletonBuffer,
                            gpuDstStart            = requests[i].shaderDstIndex
                        });
                        header.meshCommandCount++;
                    }
                    else if (request.indexInSkeletonBuffer == prevMesh)
                    {
                        skinningStream.Write(new SkinningStreamMeshCommand
                        {
                            batchOp                = SkinningStreamMeshCommand.BatchOp.GsTfStoreDst,
                            indexInDependentBuffer = (int)requests[i].indexInSkeletonBuffer,
                            gpuDstStart            = requests[i].shaderDstIndex
                        });
                        header.meshCommandCount++;
                    }
                    else
                    {
                        skinningStream.Write(new SkinningStreamMeshCommand
                        {
                            batchOp                = SkinningStreamMeshCommand.BatchOp.LoadQvvsMulMatWithOffsetBindposesStoreGs,
                            indexInDependentBuffer = (int)requests[i].indexInSkeletonBuffer,
                            gpuDstStart            = requests[i].shaderDstIndex
                        });
                        header.meshCommandCount++;
                        skinningStream.Write(new SkinningStreamMeshCommand
                        {
                            batchOp                = SkinningStreamMeshCommand.BatchOp.GsTfStoreDst | SkinningStreamMeshCommand.BatchOp.UseSkeletonCountAsGsBaseAddress,
                            indexInDependentBuffer = (int)requests[i].indexInSkeletonBuffer,
                            gpuDstStart            = requests[i].shaderDstIndex
                        });
                        header.meshCommandCount++;
                    }
                    prevMesh = request.indexInSkeletonBuffer;
                }

                chunkPrefixSums.batchSkinningMeshCommandsCount += header.meshCommandCount;
            }

            unsafe void BuildCommandsSingleMeshExpanded(NativeArray<MeshSkinningRequest>  requests,
                                                        NativeArray<DependentSkinnedMesh> meshes,
                                                        int indexInChunk,
                                                        int skeletonBonesCount)
            {
                ref var header = ref skinningStream.Allocate<SkinningStreamHeader>();
                header         = new SkinningStreamHeader
                {
                    boneTransformCount   = (short)meshes[(int)requests[0].indexInSkeletonBuffer].boneOffsetsCount,
                    meshCommandCount     = 0,
                    indexInSkeletonChunk = (byte)indexInChunk,
                    loadOp               = SkinningStreamHeader.LoadOp.Virtual | HistoryFromShaderUsage(requests[0].indexInSkeletonBufferShaderUsageHigh8) |
                                           SkinningStreamHeader.LoadOp.LargeSkeleton
                };
                chunkPrefixSums.expansionHeadersCount++;
                chunkPrefixSums.boneTransformsToUpload += (uint)header.boneTransformCount;

                SkinningStreamMeshCommand dummy           = default;
                ref var                   currentCommand  = ref dummy;
                MeshSkinningRequest       previousRequest = default;
                foreach (var request in requests)
                {
                    if (request.isDqs != previousRequest.isDqs)
                    {
                        currentCommand = ref skinningStream.Allocate<SkinningStreamMeshCommand>();

                        var expansionOp = request.isDqs ? SkinningStreamMeshCommand.LargeSkeletonExpansionOp.DqsWorld :
                                          SkinningStreamMeshCommand.LargeSkeletonExpansionOp.Mats;
                        var options = request.isDqsDeform || request.isMatrixDeform ?
                                      SkinningStreamMeshCommand.LargeSkeletonOptions.TransformsUsePrefixSum :
                                      SkinningStreamMeshCommand.LargeSkeletonOptions.TransformsOnly;
                        SkinningStreamMeshCommand.LargeSkeletonSkinningOp skinningOp = default;
                        if (request.isDqsDeform && request.useVerticesInDst)
                            skinningOp = SkinningStreamMeshCommand.LargeSkeletonSkinningOp.DqsVertInDst;
                        else if (request.isDqsDeform && !request.useVerticesInDst)
                            skinningOp = SkinningStreamMeshCommand.LargeSkeletonSkinningOp.DqsVertInSrc;
                        else if (request.isMatrixDeform && request.useVerticesInDst)
                            skinningOp = SkinningStreamMeshCommand.LargeSkeletonSkinningOp.MatVertInDst;
                        else if (request.isMatrixDeform && !request.useVerticesInDst)
                            skinningOp = SkinningStreamMeshCommand.LargeSkeletonSkinningOp.MatVertInSrc;

                        currentCommand = new SkinningStreamMeshCommand
                        {
                            gpuDstStart               = request.shaderDstIndex,
                            indexInDependentBuffer    = (int)request.indexInSkeletonBuffer,
                            largeSkeletonExpansionOp  = expansionOp,
                            largeSkeletonMeshDstStart = request.shaderDstIndex,
                            largeSkeletonOptions      = options,
                            largeSkeletonSkinningOp   = skinningOp,
                        };
                        header.meshCommandCount++;
                        if (request.isDqsDeform || request.isMatrixDeform)
                        {
                            chunkPrefixSums.meshSkinningCommandsCount++;
                            chunkPrefixSums.meshSkinningExtraBoneTransformsCount += (uint)header.boneTransformCount;
                        }
                    }
                    else
                    {
                        // Patch for vertex skinning
                        currentCommand.gpuDstStart                            = request.shaderDstIndex;
                        currentCommand.largeSkeletonOptions                  |= SkinningStreamMeshCommand.LargeSkeletonOptions.TransformsFromShader;
                        chunkPrefixSums.meshSkinningExtraBoneTransformsCount -= (uint)header.boneTransformCount;
                    }
                    previousRequest = request;

                    chunkPrefixSums.expansionMeshCommandsCount += header.meshCommandCount;
                }
            }

            unsafe void BuildCommandsMultiMeshExpanded(NativeArray<MeshSkinningRequest>  requests,
                                                       NativeArray<DependentSkinnedMesh> meshes,
                                                       int indexInChunk,
                                                       int skeletonBonesCount)
            {
                ref var header = ref skinningStream.Allocate<SkinningStreamHeader>();
                header         = new SkinningStreamHeader
                {
                    boneTransformCount   = (short)skeletonBonesCount,
                    meshCommandCount     = 0,
                    indexInSkeletonChunk = (byte)indexInChunk,
                    loadOp               = SkinningStreamHeader.LoadOp.Virtual | HistoryFromShaderUsage(requests[0].indexInSkeletonBufferShaderUsageHigh8) |
                                           SkinningStreamHeader.LoadOp.LargeSkeleton
                };
                chunkPrefixSums.expansionHeadersCount++;
                chunkPrefixSums.boneTransformsToUpload += (uint)header.boneTransformCount;

                uint                      lastMesh        = ~0x0u;
                SkinningStreamMeshCommand dummy           = default;
                ref var                   currentCommand  = ref dummy;
                MeshSkinningRequest       previousRequest = default;
                foreach (var request in requests)
                {
                    if (request.indexInSkeletonBuffer != lastMesh || request.isDqs != previousRequest.isDqs)
                    {
                        currentCommand = ref skinningStream.Allocate<SkinningStreamMeshCommand>();

                        var expansionOp = request.isDqs ? SkinningStreamMeshCommand.LargeSkeletonExpansionOp.DqsWorldWithOffsets :
                                          SkinningStreamMeshCommand.LargeSkeletonExpansionOp.MatsWithOffsets;
                        var options = request.isDqsDeform || request.isMatrixDeform ?
                                      SkinningStreamMeshCommand.LargeSkeletonOptions.TransformsUsePrefixSum :
                                      SkinningStreamMeshCommand.LargeSkeletonOptions.TransformsOnly;
                        SkinningStreamMeshCommand.LargeSkeletonSkinningOp skinningOp = default;
                        if (request.isDqsDeform && request.useVerticesInDst)
                            skinningOp = SkinningStreamMeshCommand.LargeSkeletonSkinningOp.DqsVertInDst;
                        else if (request.isDqsDeform && !request.useVerticesInDst)
                            skinningOp = SkinningStreamMeshCommand.LargeSkeletonSkinningOp.DqsVertInSrc;
                        else if (request.isMatrixDeform && request.useVerticesInDst)
                            skinningOp = SkinningStreamMeshCommand.LargeSkeletonSkinningOp.MatVertInDst;
                        else if (request.isMatrixDeform && !request.useVerticesInDst)
                            skinningOp = SkinningStreamMeshCommand.LargeSkeletonSkinningOp.MatVertInSrc;

                        currentCommand = new SkinningStreamMeshCommand
                        {
                            gpuDstStart               = request.shaderDstIndex,
                            indexInDependentBuffer    = (int)request.indexInSkeletonBuffer,
                            largeSkeletonExpansionOp  = expansionOp,
                            largeSkeletonMeshDstStart = request.shaderDstIndex,
                            largeSkeletonOptions      = options,
                            largeSkeletonSkinningOp   = skinningOp,
                        };
                        header.meshCommandCount++;
                        if (request.isDqsDeform || request.isMatrixDeform)
                        {
                            chunkPrefixSums.meshSkinningCommandsCount++;
                            chunkPrefixSums.meshSkinningExtraBoneTransformsCount += (uint)header.boneTransformCount;
                        }
                    }
                    else
                    {
                        // Patch for vertex skinning
                        currentCommand.gpuDstStart                            = request.shaderDstIndex;
                        currentCommand.largeSkeletonOptions                  |= SkinningStreamMeshCommand.LargeSkeletonOptions.TransformsFromShader;
                        chunkPrefixSums.meshSkinningExtraBoneTransformsCount -= (uint)header.boneTransformCount;
                    }
                    previousRequest = request;
                    lastMesh        = request.indexInSkeletonBuffer;
                }

                chunkPrefixSums.expansionMeshCommandsCount += header.meshCommandCount;
            }

            SkinningStreamHeader.LoadOp HistoryFromShaderUsage(uint indexAndShaderUsage)
            {
                var usageHistory = (indexAndShaderUsage >> 26) & 0x0f;
                if (usageHistory == 0)
                    return SkinningStreamHeader.LoadOp.Current;
                else if (usageHistory == 1)
                    return SkinningStreamHeader.LoadOp.Previous;
                else
                    return SkinningStreamHeader.LoadOp.TwoAgo;
            }

            struct RequestSorter : IComparer<MeshSkinningRequest>
            {
                public NativeArray<DependentSkinnedMesh> meshes;

                public int Compare(MeshSkinningRequest a, MeshSkinningRequest b)
                {
                    // Start by ordering by history, and then algorithm minus Vertex vs Deform.
                    uint preferredShaderOrderStrong = a.indexInSkeletonBufferShaderUsageHigh8 & 0x0e000000u;
                    var  result                     = preferredShaderOrderStrong.CompareTo(b.indexInSkeletonBufferShaderUsageHigh8 & 0x0e000000u);
                    if (result == 0)
                    {
                        // Next, sort by bone count.
                        result = ExtractMeshBoneCount(a.indexInSkeletonBufferShaderUsageHigh8).CompareTo(ExtractMeshBoneCount(b.indexInSkeletonBufferShaderUsageHigh8));
                        if (result == 0)
                        {
                            // Next, sort by mesh index in case two meshes have the same bone count.
                            var index = a.indexInSkeletonBufferShaderUsageHigh8 & 0x00ffffffu;
                            result    = index.CompareTo(b.indexInSkeletonBufferShaderUsageHigh8 & 0x00ffffffu);
                            if (result == 0)
                            {
                                // Sort by Vertex vs Deform
                                result = (a.indexInSkeletonBufferShaderUsageHigh8 & 0x7fffffffu).CompareTo(b.indexInSkeletonBufferShaderUsageHigh8 & 0x7fffffffu);

                                // The only bit not checked at this point is the vertexInDst bit, which should not be a differentiator.
                            }
                        }
                    }
                    return result;
                }

                uint ExtractMeshBoneCount(uint indexAndShaderUsage)
                {
                    var index = indexAndShaderUsage & 0x00ffffffu;
                    return meshes[(int)index].boneOffsetsCount;
                }
            }
        }

        [BurstCompile]
        struct PrefixSumCountsJob : IJob
        {
            public NativeArray<PerChunkPrefixSums>        perChunkPrefixSums;
            public NativeReference<BufferLayouts>         bufferLayouts;
            public ComponentLookup<MaxRequiredDeformData> maxRequiredDeformDataLookup;
            public Entity                                 worldBlackboardEntity;

            public void Execute()
            {
                PerChunkPrefixSums running = default;
                for (int i = 0; i < perChunkPrefixSums.Length; i++)
                {
                    var temp                                      = perChunkPrefixSums[i];
                    perChunkPrefixSums[i]                         = running;
                    running.boneTransformsToUpload               += temp.boneTransformsToUpload;
                    running.batchSkinningHeadersCount            += temp.batchSkinningHeadersCount;
                    running.batchSkinningMeshCommandsCount       += temp.batchSkinningMeshCommandsCount;
                    running.expansionHeadersCount                += temp.expansionHeadersCount;
                    running.expansionMeshCommandsCount           += temp.expansionMeshCommandsCount;
                    running.meshSkinningCommandsCount            += temp.meshSkinningCommandsCount;
                    running.meshSkinningExtraBoneTransformsCount += temp.meshSkinningExtraBoneTransformsCount;
                }

                var maxData          = maxRequiredDeformDataLookup.GetRefRW(worldBlackboardEntity);
                var shaderTransforms = maxData.ValueRW.maxRequiredBoneTransformsForVertexSkinning;
                bufferLayouts.Value  = new BufferLayouts
                {
                    requiredMetaSize = running.batchSkinningHeadersCount + running.batchSkinningMeshCommandsCount + running.expansionHeadersCount +
                                       running.expansionMeshCommandsCount + running.meshSkinningCommandsCount * 2,
                    requiredUploadTransforms            = running.boneTransformsToUpload,
                    requiredMeshSkinningExtraTransforms = running.meshSkinningExtraBoneTransformsCount,

                    batchSkinningMeshCommandsStart = running.batchSkinningHeadersCount,
                    expansionHeadersStart          = running.batchSkinningHeadersCount + running.batchSkinningMeshCommandsCount,
                    expansionMeshCommandsStart     = running.batchSkinningHeadersCount + running.batchSkinningMeshCommandsCount + running.expansionHeadersCount,
                    meshSkinningCommandsStart      = running.batchSkinningHeadersCount + running.batchSkinningMeshCommandsCount + running.expansionHeadersCount +
                                                     running.expansionMeshCommandsCount,
                    meshSkinningExtraBoneTransformsStart = shaderTransforms,

                    batchSkinningHeadersCount = running.batchSkinningHeadersCount,
                    expansionHeadersCount     = running.expansionHeadersCount,
                    meshSkinningCommandsCount = running.meshSkinningCommandsCount,
                };
                maxData.ValueRW.maxRequiredBoneTransformsForVertexSkinning += running.meshSkinningExtraBoneTransformsCount;
            }
        }
        #endregion

        [BurstCompile]
        struct WriteBuffersJob : IJobChunk
        {
            [ReadOnly] public BufferTypeHandle<DependentSkinnedMesh> skinnedMeshesBufferHandle;
            [ReadOnly] public NativeArray<short>                     boneOffsetsBuffer;

            [ReadOnly] public BufferTypeHandle<BoneReference>         boneReferenceBufferHandle;
            [ReadOnly] public WorldTransformReadOnlyAspect.TypeHandle worldTransformHandle;
            [ReadOnly] public WorldTransformReadOnlyAspect.Lookup     worldTransformLookup;
#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
            [ReadOnly] public ComponentTypeHandle<PreviousTransform> previousTransformHandle;
            [ReadOnly] public ComponentLookup<PreviousTransform>     previousTransformLookup;
            [ReadOnly] public ComponentTypeHandle<TwoAgoTransform>   twoAgoTransformHandle;
            [ReadOnly] public ComponentLookup<TwoAgoTransform>       twoAgoTransformLookup;
#endif

            [ReadOnly] public BufferTypeHandle<OptimizedBoneTransform>    optimizedBoneBufferHandle;
            [ReadOnly] public ComponentTypeHandle<OptimizedSkeletonState> optimizedSkeletonStateHandle;

            [ReadOnly] public NativeStream.Reader             skinningStream;
            [ReadOnly] public NativeArray<PerChunkPrefixSums> perChunkPrefixSums;
            [ReadOnly] public NativeReference<BufferLayouts>  bufferLayouts;

            [NativeDisableParallelForRestriction] public NativeArray<TransformQvvs> boneTransformsUploadBuffer;
            [NativeDisableParallelForRestriction] public NativeArray<uint4>         metaBuffer;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                int streamItemsCount = skinningStream.BeginForEachIndex(unfilteredChunkIndex);
                if (streamItemsCount == 0)
                {
                    skinningStream.EndForEachIndex();
                    return;
                }

                var prefixSums = perChunkPrefixSums[unfilteredChunkIndex];

                if (chunk.Has(ref boneReferenceBufferHandle))
                {
                    ProcessExposed(in chunk, ref prefixSums, streamItemsCount);
                }
                else if (chunk.Has(ref optimizedBoneBufferHandle))
                {
                    ProcessOptimized(in chunk, ref prefixSums, streamItemsCount);
                }

                skinningStream.EndForEachIndex();
            }

            void ProcessExposed(in ArchetypeChunk chunk, ref PerChunkPrefixSums prefixSums, int streamItemsCount)
            {
                var meshesAccessor = chunk.GetBufferAccessor(ref skinnedMeshesBufferHandle);

                var bonesAccessor           = chunk.GetBufferAccessor(ref boneReferenceBufferHandle);
                var skeletonWorldTransforms = worldTransformHandle.Resolve(chunk);
#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
                var skeletonPreviousTransforms = chunk.GetNativeArray(ref previousTransformHandle);
                var skeletonTwoAgoTransforms   = chunk.GetNativeArray(ref twoAgoTransformHandle);
#elif !LATIOS_TRANSFORMS_UNCACHED_QVVS && LATIOS_TRANSFORMS_UNITY
                var skeletonPreviousTransforms = skeletonWorldTransforms;
                var skeletonTwoAgoTransform    = skeletonWorldTransforms;
#endif

                var layouts = bufferLayouts.Value;

                if ((prefixSums.batchSkinningHeadersCount == 0 && prefixSums.batchSkinningMeshCommandsCount != 0) ||
                    prefixSums.batchSkinningMeshCommandsCount == 0 && prefixSums.batchSkinningHeadersCount != 0)
                {
                    UnityEngine.Debug.LogError(
                        $"Skinning order corruption occurred. HeaderCount: {prefixSums.batchSkinningHeadersCount}, meshCommandsCount: {prefixSums.batchSkinningMeshCommandsCount}");
                }

                for (int streamReads = 0; streamReads < streamItemsCount;)
                {
                    var header = skinningStream.Read<SkinningStreamHeader>();
                    streamReads++;

                    var bones = bonesAccessor[header.indexInSkeletonChunk].AsNativeArray();

                    // The header is nearly identical for both batch skinning and expansion.
                    // The exception is the z value which is patched for expansion below.
                    uint4 headerCommand = new uint4
                    {
                        x = (ushort)header.boneTransformCount | ((uint)(header.loadOp & SkinningStreamHeader.LoadOp.OpMask) << 16),
                        y = prefixSums.boneTransformsToUpload,
                        z = prefixSums.batchSkinningMeshCommandsCount + layouts.batchSkinningMeshCommandsStart,
                        w = header.meshCommandCount
                    };

                    var meshes = meshesAccessor[header.indexInSkeletonChunk].AsNativeArray();

                    var history = header.loadOp & SkinningStreamHeader.LoadOp.HistoryMask;
#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
                    if (history == SkinningStreamHeader.LoadOp.Current)
#endif
                    {
                        // Todo: Need to switch this to be bone index 0 for better compliance with documentation.
                        // This should use a check if it and the skeleton entity are the same.
                        var skeletonWorldTransform = skeletonWorldTransforms[header.indexInSkeletonChunk].worldTransformQvvs;
                        if (bones.Length == header.boneTransformCount)
                        {
                            boneTransformsUploadBuffer[(int)prefixSums.boneTransformsToUpload] = TransformQvvs.identity;
                            for (int i = 1; i < bones.Length; i++)
                            {
                                var entity                                                             = bones[i].bone;
                                var boneWorldTransform                                                 = worldTransformLookup[entity].worldTransformQvvs;
                                var boneToRoot                                                         = qvvs.inversemulqvvs(in skeletonWorldTransform, in boneWorldTransform);
                                boneTransformsUploadBuffer[(int)prefixSums.boneTransformsToUpload + i] = boneToRoot;
                            }

                            prefixSums.boneTransformsToUpload += (uint)bones.Length;
                        }
                        else
                        {
                            // Prebake offsets
                            var soloMeshCommand = skinningStream.Peek<SkinningStreamMeshCommand>();
                            var meshData        = meshes[soloMeshCommand.indexInDependentBuffer];
                            var offsets         = boneOffsetsBuffer.GetSubArray((int)meshData.boneOffsetsStart, (int)meshData.boneOffsetsCount);

                            for (int i = 0; i < offsets.Length; i++)
                            {
                                if (offsets[i] == 0)
                                {
                                    boneTransformsUploadBuffer[(int)prefixSums.boneTransformsToUpload] = TransformQvvs.identity;
                                    continue;
                                }
                                var entity                                                             = bones[offsets[i]].bone;
                                var boneWorldTransform                                                 = worldTransformLookup[entity].worldTransformQvvs;
                                var boneToRoot                                                         = qvvs.inversemulqvvs(in skeletonWorldTransform, in boneWorldTransform);
                                boneTransformsUploadBuffer[(int)prefixSums.boneTransformsToUpload + i] = boneToRoot;
                            }

                            prefixSums.boneTransformsToUpload += (uint)offsets.Length;
                        }
                    }
#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
                    else if (history == SkinningStreamHeader.LoadOp.Previous)
                    {
                        var skeletonWorldTransform = skeletonPreviousTransforms[header.indexInSkeletonChunk].worldTransform;
                        if (bones.Length == header.boneTransformCount)
                        {
                            boneTransformsUploadBuffer[(int)prefixSums.boneTransformsToUpload] = TransformQvvs.identity;
                            for (int i = 1; i < bones.Length; i++)
                            {
                                var entity = bones[i].bone;
                                if (Hint.Unlikely(!previousTransformLookup.TryGetComponent(entity, out var previousTransform)))
                                {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                                    UnityEngine.Debug.LogError(
                                        $"Bone {entity.ToFixedString()} at index {i} does not have the required PreviousTransform. Using WorldTansform instead.");
#endif
                                    previousTransform.worldTransform = worldTransformLookup[entity].worldTransformQvvs;
                                }
                                var boneToRoot = qvvs.inversemulqvvs(in skeletonWorldTransform,
                                                                     in previousTransform.worldTransform);
                                boneTransformsUploadBuffer[(int)prefixSums.boneTransformsToUpload + i] = boneToRoot;
                            }

                            prefixSums.boneTransformsToUpload += (uint)bones.Length;
                        }
                        else
                        {
                            // Prebake offsets
                            var soloMeshCommand = skinningStream.Peek<SkinningStreamMeshCommand>();
                            var meshData        = meshes[soloMeshCommand.indexInDependentBuffer];
                            var offsets         = boneOffsetsBuffer.GetSubArray((int)meshData.boneOffsetsStart, (int)meshData.boneOffsetsCount);

                            for (int i = 0; i < offsets.Length; i++)
                            {
                                if (offsets[i] == 0)
                                {
                                    boneTransformsUploadBuffer[(int)prefixSums.boneTransformsToUpload] = TransformQvvs.identity;
                                    continue;
                                }
                                var entity = bones[offsets[i]].bone;
                                if (Hint.Unlikely(!previousTransformLookup.TryGetComponent(entity, out var previousTransform)))
                                {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                                    UnityEngine.Debug.LogError(
                                        $"Bone {entity.ToFixedString()} at index {i} does not have the required PreviousTransform. Using WorldTansform instead.");
#endif
                                    previousTransform.worldTransform = worldTransformLookup[entity].worldTransformQvvs;
                                }
                                var boneToRoot = qvvs.inversemulqvvs(in skeletonWorldTransform,
                                                                     in previousTransform.worldTransform);
                                boneTransformsUploadBuffer[(int)prefixSums.boneTransformsToUpload + i] = boneToRoot;
                            }

                            prefixSums.boneTransformsToUpload += (uint)offsets.Length;
                        }
                    }
                    else
                    {
                        var skeletonWorldTransform = skeletonTwoAgoTransforms[header.indexInSkeletonChunk].worldTransform;
                        if (bones.Length == header.boneTransformCount)
                        {
                            boneTransformsUploadBuffer[(int)prefixSums.boneTransformsToUpload] = TransformQvvs.identity;
                            for (int i = 1; i < bones.Length; i++)
                            {
                                var entity = bones[i].bone;
                                if (Hint.Unlikely(!twoAgoTransformLookup.TryGetComponent(entity, out var twoAgoTransform)))
                                {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                                    UnityEngine.Debug.LogError(
                                        $"Bone {entity.ToFixedString()} at index {i} does not have the required TwoAgoTransform. Using WorldTansform instead.");
#endif
                                    twoAgoTransform.worldTransform = worldTransformLookup[entity].worldTransformQvvs;
                                }
                                var boneToRoot = qvvs.inversemulqvvs(in skeletonWorldTransform,
                                                                     in twoAgoTransform.worldTransform);
                                boneTransformsUploadBuffer[(int)prefixSums.boneTransformsToUpload + i] = boneToRoot;
                            }

                            prefixSums.boneTransformsToUpload += (uint)bones.Length;
                        }
                        else
                        {
                            // Prebake offsets
                            var soloMeshCommand = skinningStream.Peek<SkinningStreamMeshCommand>();
                            var meshData        = meshes[soloMeshCommand.indexInDependentBuffer];
                            var offsets         = boneOffsetsBuffer.GetSubArray((int)meshData.boneOffsetsStart, (int)meshData.boneOffsetsCount);

                            for (int i = 0; i < offsets.Length; i++)
                            {
                                if (offsets[i] == 0)
                                {
                                    boneTransformsUploadBuffer[(int)prefixSums.boneTransformsToUpload] = TransformQvvs.identity;
                                    continue;
                                }
                                var entity = bones[offsets[i]].bone;
                                if (Hint.Unlikely(!twoAgoTransformLookup.TryGetComponent(entity, out var twoAgoTransform)))
                                {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                                    UnityEngine.Debug.LogError(
                                        $"Bone {entity.ToFixedString()} at index {i} does not have the required TwoAgoTransform. Using WorldTansform instead.");
#endif
                                    twoAgoTransform.worldTransform = worldTransformLookup[entity].worldTransformQvvs;
                                }
                                var boneToRoot = qvvs.inversemulqvvs(in skeletonWorldTransform,
                                                                     in twoAgoTransform.worldTransform);
                                boneTransformsUploadBuffer[(int)prefixSums.boneTransformsToUpload + i] = boneToRoot;
                            }

                            prefixSums.boneTransformsToUpload += (uint)offsets.Length;
                        }
                    }
#endif

                    if ((header.loadOp & SkinningStreamHeader.LoadOp.LargeSkeleton) != SkinningStreamHeader.LoadOp.LargeSkeleton)
                    {
                        metaBuffer[(int)prefixSums.batchSkinningHeadersCount] = headerCommand;
                        prefixSums.batchSkinningHeadersCount++;
                        ProcessMeshesBatched(meshes, header.meshCommandCount, ref prefixSums, ref streamReads, in layouts);
                    }
                    else
                    {
                        headerCommand.z = prefixSums.expansionMeshCommandsCount +
                                          layouts.expansionMeshCommandsStart;
                        metaBuffer[(int)(prefixSums.expansionHeadersCount + layouts.expansionHeadersStart)] = headerCommand;
                        prefixSums.expansionHeadersCount++;
                        ProcessMeshesExpanded(meshes, header.meshCommandCount, ref prefixSums, ref streamReads, in layouts);
                    }
                }
            }

            void ProcessOptimized(in ArchetypeChunk chunk, ref PerChunkPrefixSums prefixSums, int streamItemsCount)
            {
                var meshesAccessor = chunk.GetBufferAccessor(ref skinnedMeshesBufferHandle);

                var bonesAccessor   = chunk.GetBufferAccessor(ref optimizedBoneBufferHandle);
                var optimizedStates = chunk.GetNativeArray(ref optimizedSkeletonStateHandle);

                var layouts = bufferLayouts.Value;

                for (int streamReads = 0; streamReads < streamItemsCount;)
                {
                    var header = skinningStream.Read<SkinningStreamHeader>();
                    streamReads++;

                    // The header is nearly identical for both batch skinning and expansion.
                    // The exception is the z value which is patched for expansion below.
                    uint4 headerCommand = new uint4
                    {
                        x = (ushort)header.boneTransformCount | ((uint)(header.loadOp & SkinningStreamHeader.LoadOp.OpMask) << 16),
                        y = prefixSums.boneTransformsToUpload,
                        z = prefixSums.batchSkinningMeshCommandsCount + layouts.batchSkinningMeshCommandsStart,
                        w = header.meshCommandCount
                    };

                    var meshes = meshesAccessor[header.indexInSkeletonChunk].AsNativeArray();

                    var history         = header.loadOp & SkinningStreamHeader.LoadOp.HistoryMask;
                    var bonesFullBuffer = bonesAccessor[header.indexInSkeletonChunk].AsNativeArray().Reinterpret<TransformQvvs>();
                    var state           = optimizedStates[header.indexInSkeletonChunk].state;
                    var rotationMask    = (byte)(state & OptimizedSkeletonState.Flags.RotationMask);
                    int rotation;
                    if (history == SkinningStreamHeader.LoadOp.Current)
                    {
                        rotation = (state & OptimizedSkeletonState.Flags.IsDirty) == OptimizedSkeletonState.Flags.IsDirty ?
                                   OptimizedSkeletonState.CurrentFromMask[rotationMask] : OptimizedSkeletonState.PreviousFromMask[rotationMask];
                    }
                    else if (history == SkinningStreamHeader.LoadOp.Previous)
                        rotation = OptimizedSkeletonState.PreviousFromMask[rotationMask];
                    else
                        rotation  = OptimizedSkeletonState.TwoAgoFromMask[rotationMask];
                    int boneCount = bonesFullBuffer.Length / 6;
                    var bones     = bonesFullBuffer.GetSubArray(rotation * boneCount * 2, boneCount);

                    if (boneCount == header.boneTransformCount)
                    {
                        boneTransformsUploadBuffer.GetSubArray((int)prefixSums.boneTransformsToUpload, boneCount).CopyFrom(bones);
                        boneTransformsUploadBuffer[(int)prefixSums.boneTransformsToUpload]  = TransformQvvs.identity;
                        prefixSums.boneTransformsToUpload                                  += (uint)boneCount;
                    }
                    else
                    {
                        // Prebake offsets
                        var soloMeshCommand = skinningStream.Peek<SkinningStreamMeshCommand>();
                        var meshData        = meshes[soloMeshCommand.indexInDependentBuffer];
                        var offsets         = boneOffsetsBuffer.GetSubArray((int)meshData.boneOffsetsStart, (int)meshData.boneOffsetsCount);

                        for (int i = 0; i < offsets.Length; i++)
                        {
                            if (offsets[i] == 0)
                                boneTransformsUploadBuffer[(int)prefixSums.boneTransformsToUpload + i] = TransformQvvs.identity;
                            else
                                boneTransformsUploadBuffer[(int)prefixSums.boneTransformsToUpload + i] = bones[offsets[i]];
                        }

                        prefixSums.boneTransformsToUpload += (uint)offsets.Length;
                    }

                    if ((header.loadOp & SkinningStreamHeader.LoadOp.LargeSkeleton) != SkinningStreamHeader.LoadOp.LargeSkeleton)
                    {
                        metaBuffer[(int)prefixSums.batchSkinningHeadersCount] = headerCommand;
                        prefixSums.batchSkinningHeadersCount++;
                        ProcessMeshesBatched(meshes, header.meshCommandCount, ref prefixSums, ref streamReads, in layouts);
                    }
                    else
                    {
                        headerCommand.z = prefixSums.expansionMeshCommandsCount +
                                          layouts.expansionMeshCommandsStart;
                        metaBuffer[(int)(prefixSums.expansionHeadersCount + layouts.expansionHeadersStart)] = headerCommand;
                        prefixSums.expansionHeadersCount++;
                        ProcessMeshesExpanded(meshes, header.meshCommandCount, ref prefixSums, ref streamReads, in layouts);
                    }
                }
            }

            void ProcessMeshesBatched(NativeArray<DependentSkinnedMesh> meshes,
                                      uint meshCommandCount,
                                      ref PerChunkPrefixSums prefixSums,
                                      ref int streamReads,
                                      in BufferLayouts layouts)
            {
                for (int i = 0; i < meshCommandCount; i++)
                {
                    var command = skinningStream.Read<SkinningStreamMeshCommand>();
                    streamReads++;

                    var mesh  = meshes[command.indexInDependentBuffer];
                    var x     = (uint)(command.batchOp & SkinningStreamMeshCommand.BatchOp.OpMask) << 16;
                    x        |= math.select(0u,
                                            1u << 24,
                                            (command.batchOp & SkinningStreamMeshCommand.BatchOp.UseSkeletonCountAsGsBaseAddress) ==
                                            SkinningStreamMeshCommand.BatchOp.UseSkeletonCountAsGsBaseAddress);
                    x             |= mesh.boneOffsetsCount;
                    uint4 optionA  = new uint4(x, mesh.meshBindPosesStart, mesh.boneOffsetsStart, command.gpuDstStart);
                    uint4 optionB  = new uint4(x, mesh.meshBindPosesStart + mesh.boneOffsetsCount, mesh.boneOffsetsStart, command.gpuDstStart);
                    uint4 optionC  = new uint4(x, mesh.meshVerticesStart, mesh.meshWeightsStart, command.gpuDstStart);

                    uint4 finalCommand = (command.batchOp & SkinningStreamMeshCommand.BatchOp.OpMask) switch
                    {
                        SkinningStreamMeshCommand.BatchOp.CvtGsQvvsToMatReplaceGs => optionA,
                        SkinningStreamMeshCommand.BatchOp.MulGsMatWithOffsetBindposesStoreGs => optionA,
                        SkinningStreamMeshCommand.BatchOp.MulGsMatWithBindposesStoreGs => optionA,
                        SkinningStreamMeshCommand.BatchOp.LoadQvvsMulMatWithOffsetBindposesStoreGs => optionA,
                        SkinningStreamMeshCommand.BatchOp.LoadQvvsMulMatWithBindposesStoreGs => optionA,
                        SkinningStreamMeshCommand.BatchOp.GsTfStoreDst => optionA,
                        SkinningStreamMeshCommand.BatchOp.SkinMatVertInSrc => optionC,
                        SkinningStreamMeshCommand.BatchOp.SkinMatVertInDst => optionC,
                        SkinningStreamMeshCommand.BatchOp.CvtGsQvvsToDqsWithOffsetStoreDst => optionB,
                        SkinningStreamMeshCommand.BatchOp.CvtGsQvvsToDqsWithOffsetStoreGsCopyBindposeToGs => optionB,
                        SkinningStreamMeshCommand.BatchOp.LoadCvtQvvsToDqsWithOffsetStoreGsCopyBindposeToGs => optionB,
                        SkinningStreamMeshCommand.BatchOp.LoadBindposeDqsStoreGs => optionB,
                        SkinningStreamMeshCommand.BatchOp.CvtGsQvvsToDqsWithOffsetStoreGs => optionB,
                        SkinningStreamMeshCommand.BatchOp.CvtGsQvvsToDqsStoreGs => optionB,
                        SkinningStreamMeshCommand.BatchOp.LoadCvtQvvsToDqsWithOffsetStoreGs => optionB,
                        SkinningStreamMeshCommand.BatchOp.LoadCvtQvvsToDqsStoreGs => optionB,
                        SkinningStreamMeshCommand.BatchOp.SkinDqsBindPoseVertInSrc => optionC,
                        SkinningStreamMeshCommand.BatchOp.SkinDqsBindPoseVertInDst => optionC,
                        SkinningStreamMeshCommand.BatchOp.SkinDqsWorldVertInDst => optionC,
                        _ => optionA,
                    };

                    metaBuffer[(int)(prefixSums.batchSkinningMeshCommandsCount + layouts.batchSkinningMeshCommandsStart)] = finalCommand;
                    prefixSums.batchSkinningMeshCommandsCount++;
                }
            }

            void ProcessMeshesExpanded(NativeArray<DependentSkinnedMesh> meshes,
                                       uint meshCommandCount,
                                       ref PerChunkPrefixSums prefixSums,
                                       ref int streamReads,
                                       in BufferLayouts layouts)
            {
                for (int i = 0; i < meshCommandCount; i++)
                {
                    var command = skinningStream.Read<SkinningStreamMeshCommand>();
                    streamReads++;

                    var mesh  = meshes[command.indexInDependentBuffer];
                    var x     = mesh.boneOffsetsCount;
                    x        |= (uint)command.largeSkeletonExpansionOp << 16;

                    uint transformTarget;
                    if (command.largeSkeletonOptions != SkinningStreamMeshCommand.LargeSkeletonOptions.TransformsUsePrefixSum)
                    {
                        transformTarget = command.gpuDstStart;
                    }
                    else
                    {
                        transformTarget                                  = prefixSums.meshSkinningExtraBoneTransformsCount + layouts.meshSkinningExtraBoneTransformsStart;
                        prefixSums.meshSkinningExtraBoneTransformsCount += mesh.boneOffsetsCount;
                    }

                    uint4 expansionCommand = new uint4(x, mesh.meshBindPosesStart, mesh.boneOffsetsStart, transformTarget);

                    metaBuffer[(int)(prefixSums.expansionMeshCommandsCount + layouts.expansionMeshCommandsStart)] = expansionCommand;
                    prefixSums.expansionMeshCommandsCount++;

                    if (command.largeSkeletonOptions == SkinningStreamMeshCommand.LargeSkeletonOptions.TransformsOnly)
                        continue;

                    x      = mesh.boneOffsetsCount;
                    x     |= (uint)command.largeSkeletonSkinningOp << 16;
                    var y  = mesh.meshBindPosesStart;
                    if (command.largeSkeletonSkinningOp == SkinningStreamMeshCommand.LargeSkeletonSkinningOp.DqsVertInSrc ||
                        command.largeSkeletonSkinningOp == SkinningStreamMeshCommand.LargeSkeletonSkinningOp.DqsVertInDst)
                        y              += mesh.boneOffsetsCount;
                    uint4 meshCommandA  = new uint4(x,
                                                    y,
                                                    transformTarget,
                                                    command.largeSkeletonMeshDstStart
                                                    );
                    uint4 meshCommandB = new uint4(x,
                                                   mesh.meshVerticesStart,
                                                   mesh.meshWeightsStart,
                                                   command.largeSkeletonMeshDstStart);
                    metaBuffer[(int)(prefixSums.meshSkinningCommandsCount * 2 + layouts.meshSkinningCommandsStart)]     = meshCommandA;
                    metaBuffer[(int)(prefixSums.meshSkinningCommandsCount * 2 + 1 + layouts.meshSkinningCommandsStart)] = meshCommandB;
                    prefixSums.meshSkinningCommandsCount++;
                }
            }
        }
    }
}

