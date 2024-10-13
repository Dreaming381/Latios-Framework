using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine.Rendering;

using static Unity.Entities.SystemAPI;

namespace Latios.Kinemation.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
#if UNITY_6000_0_OR_NEWER
    public partial struct CopyDeformCullingSystem : ISystem
#else
    public partial struct CopyDeformWithCullingSystem : ISystem
#endif
    {
        EntityQuery          m_metaQuery;
        LatiosWorldUnmanaged latiosWorld;

        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();

            m_metaQuery = state.Fluent().With<ChunkHeader>(true).With<ChunkCopyDeformTag>(true).With<ChunkPerFrameCullingMask>(true)
                          .With<ChunkPerCameraCullingMask>(false).With<ChunkPerCameraCullingSplitsMask>(false).UseWriteGroups().Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var chunkList = new NativeList<ArchetypeChunk>(m_metaQuery.CalculateEntityCountWithoutFiltering(), state.WorldUpdateAllocator);

            state.Dependency = new FindChunksNeedingCopyingJob
            {
                chunkHeaderHandle          = GetComponentTypeHandle<ChunkHeader>(true),
                chunksToProcess            = chunkList.AsParallelWriter(),
                perCameraCullingMaskHandle = GetComponentTypeHandle<ChunkPerCameraCullingMask>(true)
            }.ScheduleParallel(m_metaQuery, state.Dependency);

            var skinCopier = new SkinCopier
            {
#if !UNITY_6000_0_OR_NEWER
                deformClassificationMap    = latiosWorld.worldBlackboardEntity.GetCollectionComponent<DeformClassificationMap>(true).deformClassificationMap,
                materialMaskHandle         = GetComponentTypeHandle<ChunkMaterialPropertyDirtyMask>(false),
                materialPropertyTypeLookup = GetBufferLookup<MaterialPropertyComponentType>(true),
                worldBlackboardEntity      = latiosWorld.worldBlackboardEntity,

                currentDeformHandle        = GetComponentTypeHandle<CurrentDeformShaderIndex>(false),
                currentDqsVertexHandle     = GetComponentTypeHandle<CurrentDqsVertexSkinningShaderIndex>(false),
                currentMatrixVertexHandle  = GetComponentTypeHandle<CurrentMatrixVertexSkinningShaderIndex>(false),
                legacyComputeDeformHandle  = GetComponentTypeHandle<LegacyComputeDeformShaderIndex>(false),
                legacyDotsDeformHandle     = GetComponentTypeHandle<LegacyDotsDeformParamsShaderIndex>(false),
                legacyLbsHandle            = GetComponentTypeHandle<LegacyLinearBlendSkinningShaderIndex>(false),
                previousDeformHandle       = GetComponentTypeHandle<PreviousDeformShaderIndex>(false),
                previousDqsVertexHandle    = GetComponentTypeHandle<PreviousDqsVertexSkinningShaderIndex>(false),
                previousMatrixVertexHandle = GetComponentTypeHandle<PreviousMatrixVertexSkinningShaderIndex>(false),
                twoAgoDeformHandle         = GetComponentTypeHandle<TwoAgoDeformShaderIndex>(false),
                twoAgoDqsVertexHandle      = GetComponentTypeHandle<TwoAgoDqsVertexSkinningShaderIndex>(false),
                twoAgoMatrixVertexHandle   = GetComponentTypeHandle<TwoAgoMatrixVertexSkinningShaderIndex>(false),

                currentDeformLookup        = GetComponentLookup<CurrentDeformShaderIndex>(false),
                currentDqsVertexLookup     = GetComponentLookup<CurrentDqsVertexSkinningShaderIndex>(false),
                currentMatrixVertexLookup  = GetComponentLookup<CurrentMatrixVertexSkinningShaderIndex>(false),
                legacyComputeDeformLookup  = GetComponentLookup<LegacyComputeDeformShaderIndex>(false),
                legacyDotsDeformLookup     = GetComponentLookup<LegacyDotsDeformParamsShaderIndex>(false),
                legacyLbsLookup            = GetComponentLookup<LegacyLinearBlendSkinningShaderIndex>(false),
                previousDeformLookup       = GetComponentLookup<PreviousDeformShaderIndex>(false),
                previousDqsVertexLookup    = GetComponentLookup<PreviousDqsVertexSkinningShaderIndex>(false),
                previousMatrixVertexLookup = GetComponentLookup<PreviousMatrixVertexSkinningShaderIndex>(false),
                twoAgoDeformLookup         = GetComponentLookup<TwoAgoDeformShaderIndex>(false),
                twoAgoDqsVertexLookup      = GetComponentLookup<TwoAgoDqsVertexSkinningShaderIndex>(false),
                twoAgoMatrixVertexLookup   = GetComponentLookup<TwoAgoMatrixVertexSkinningShaderIndex>(false),
#endif
            };

            var cullRequestType = latiosWorld.worldBlackboardEntity.GetComponentData<CullingContext>().viewType;
            if (cullRequestType == BatchCullingViewType.Light)
            {
                state.Dependency = new MultiSplitCopySkinJob
                {
                    chunkPerCameraMaskHandle       = GetComponentTypeHandle<ChunkPerCameraCullingMask>(false),
                    chunkPerCameraSplitsMaskHandle = GetComponentTypeHandle<ChunkPerCameraCullingSplitsMask>(false),
                    chunkPerFrameMaskHandle        = GetComponentTypeHandle<ChunkPerFrameCullingMask>(true),
                    chunksToProcess                = chunkList.AsDeferredJobArray(),
                    esiLookup                      = GetEntityStorageInfoLookup(),
                    referenceHandle                = GetComponentTypeHandle<CopyDeformFromEntity>(true),
                    skinCopier                     = skinCopier,
                }.Schedule(chunkList, 1, state.Dependency);
            }
            else
            {
                state.Dependency = new SingleSplitCopySkinJob
                {
                    chunkPerCameraMaskHandle = GetComponentTypeHandle<ChunkPerCameraCullingMask>(false),
                    chunkPerFrameMaskHandle  = GetComponentTypeHandle<ChunkPerFrameCullingMask>(true),
                    chunksToProcess          = chunkList.AsDeferredJobArray(),
                    esiLookup                = GetEntityStorageInfoLookup(),
                    referenceHandle          = GetComponentTypeHandle<CopyDeformFromEntity>(true),
                    skinCopier               = skinCopier,
                }.Schedule(chunkList, 1, state.Dependency);
            }
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        struct FindChunksNeedingCopyingJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<ChunkPerCameraCullingMask> perCameraCullingMaskHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkHeader>               chunkHeaderHandle;

            public NativeList<ArchetypeChunk>.ParallelWriter chunksToProcess;

            [Unity.Burst.CompilerServices.SkipLocalsInit]
            public unsafe void Execute(in ArchetypeChunk metaChunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var chunksCache = stackalloc ArchetypeChunk[128];
                int chunksCount = 0;
                var masks       = metaChunk.GetNativeArray(ref perCameraCullingMaskHandle);
                var headers     = metaChunk.GetNativeArray(ref chunkHeaderHandle);
                for (int i = 0; i < metaChunk.Count; i++)
                {
                    var mask = masks[i];
                    if ((mask.lower.Value | mask.upper.Value) != 0)
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
        unsafe struct SingleSplitCopySkinJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<ArchetypeChunk> chunksToProcess;

            [ReadOnly] public ComponentTypeHandle<ChunkPerFrameCullingMask> chunkPerFrameMaskHandle;
            [ReadOnly] public ComponentTypeHandle<CopyDeformFromEntity>     referenceHandle;

            [ReadOnly] public EntityStorageInfoLookup esiLookup;

            public ComponentTypeHandle<ChunkPerCameraCullingMask> chunkPerCameraMaskHandle;
            public SkinCopier                                     skinCopier;

            public void Execute(int i)
            {
                Execute(chunksToProcess[i]);
            }

            void Execute(in ArchetypeChunk chunk)
            {
                SkinCopier.Context context = default;
                skinCopier.InitContext();

                var references                 = chunk.GetNativeArray(ref referenceHandle);
                var invertedFrameMasks         = chunk.GetChunkComponentData(ref chunkPerFrameMaskHandle);
                invertedFrameMasks.lower.Value = ~invertedFrameMasks.lower.Value;
                invertedFrameMasks.upper.Value = ~invertedFrameMasks.upper.Value;
                ref var cameraMask             = ref chunk.GetChunkComponentRefRW(ref chunkPerCameraMaskHandle);

                var inMask = cameraMask.lower.Value;
                for (int i = math.tzcnt(inMask); i < 64; inMask ^= 1ul << i, i = math.tzcnt(inMask))
                {
                    bool isIn = IsReferenceVisible(in chunk,
                                                   references[i].sourceDeformedEntity,
                                                   invertedFrameMasks.lower.IsSet(i),
                                                   i,
                                                   ref context);
                    cameraMask.lower.Value &= ~(math.select(1ul, 0ul, isIn) << i);
                }
                inMask = cameraMask.upper.Value;
                for (int i = math.tzcnt(inMask); i < 64; inMask ^= 1ul << i, i = math.tzcnt(inMask))
                {
                    bool isIn = IsReferenceVisible(in chunk,
                                                   references[i + 64].sourceDeformedEntity,
                                                   invertedFrameMasks.upper.IsSet(i),
                                                   i + 64,
                                                   ref context);
                    cameraMask.upper.Value &= ~(math.select(1ul, 0ul, isIn) << i);
                }
            }

            bool IsReferenceVisible(in ArchetypeChunk chunk, Entity reference, bool needsCopy, int entityIndex, ref SkinCopier.Context context)
            {
                if (reference == Entity.Null || !esiLookup.Exists(reference))
                    return false;

                var  info          = esiLookup[reference];
                var  referenceMask = info.Chunk.GetChunkComponentData(ref chunkPerCameraMaskHandle);
                bool result;
                if (info.IndexInChunk >= 64)
                    result = referenceMask.upper.IsSet(info.IndexInChunk - 64);
                else
                    result = referenceMask.lower.IsSet(info.IndexInChunk);
                if (result && needsCopy)
                {
                    skinCopier.CopySkin(ref context, in chunk, entityIndex, reference);
                }
                return result;
            }
        }

        [BurstCompile]
        unsafe struct MultiSplitCopySkinJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<ArchetypeChunk> chunksToProcess;

            [ReadOnly] public ComponentTypeHandle<ChunkPerFrameCullingMask> chunkPerFrameMaskHandle;
            [ReadOnly] public ComponentTypeHandle<CopyDeformFromEntity>     referenceHandle;

            [ReadOnly] public EntityStorageInfoLookup esiLookup;

            public ComponentTypeHandle<ChunkPerCameraCullingMask>       chunkPerCameraMaskHandle;
            public ComponentTypeHandle<ChunkPerCameraCullingSplitsMask> chunkPerCameraSplitsMaskHandle;

            public SkinCopier skinCopier;

            public void Execute(int i)
            {
                Execute(chunksToProcess[i]);
            }

            void Execute(in ArchetypeChunk chunk)
            {
                SkinCopier.Context context = default;
                skinCopier.InitContext();

                var references                 = chunk.GetNativeArray(ref referenceHandle);
                var invertedFrameMasks         = chunk.GetChunkComponentData(ref chunkPerFrameMaskHandle);
                invertedFrameMasks.lower.Value = ~invertedFrameMasks.lower.Value;
                invertedFrameMasks.upper.Value = ~invertedFrameMasks.upper.Value;
                ref var cameraMask             = ref chunk.GetChunkComponentRefRW(ref chunkPerCameraMaskHandle);
                ref var cameraSplitsMask       = ref chunk.GetChunkComponentRefRW(ref chunkPerCameraSplitsMaskHandle);
                cameraSplitsMask               = default;

                var inMask = cameraMask.lower.Value;
                for (int i = math.tzcnt(inMask); i < 64; inMask ^= 1ul << i, i = math.tzcnt(inMask))
                {
                    bool isIn = IsReferenceVisible(in chunk,
                                                   references[i].sourceDeformedEntity,
                                                   invertedFrameMasks.lower.IsSet(i),
                                                   i,
                                                   ref context,
                                                   out var splits);
                    cameraMask.lower.Value         &= ~(math.select(1ul, 0ul, isIn) << i);
                    cameraSplitsMask.splitMasks[i]  = splits;
                }
                inMask = cameraMask.upper.Value;
                for (int i = math.tzcnt(inMask); i < 64; inMask ^= 1ul << i, i = math.tzcnt(inMask))
                {
                    bool isIn = IsReferenceVisible(in chunk,
                                                   references[i + 64].sourceDeformedEntity,
                                                   invertedFrameMasks.upper.IsSet(i),
                                                   i + 64,
                                                   ref context,
                                                   out var splits);
                    cameraMask.upper.Value              &= ~(math.select(1ul, 0ul, isIn) << i);
                    cameraSplitsMask.splitMasks[i + 64]  = splits;
                }
            }

            bool IsReferenceVisible(in ArchetypeChunk chunk, Entity reference, bool needsCopy, int entityIndex, ref SkinCopier.Context context, out byte splits)
            {
                splits = default;
                if (reference == Entity.Null || !esiLookup.Exists(reference))
                    return false;

                var  info          = esiLookup[reference];
                var  referenceMask = info.Chunk.GetChunkComponentRefRO(ref chunkPerCameraMaskHandle);
                bool result;
                if (info.IndexInChunk >= 64)
                    result = referenceMask.ValueRO.upper.IsSet(info.IndexInChunk - 64);
                else
                    result = referenceMask.ValueRO.lower.IsSet(info.IndexInChunk);

                if (result)
                {
                    var referenceSplits = info.Chunk.GetChunkComponentRefRO(ref chunkPerCameraSplitsMaskHandle);
                    splits              = referenceSplits.ValueRO.splitMasks[info.IndexInChunk];
                }

                if (result && needsCopy)
                {
                    skinCopier.CopySkin(ref context, in chunk, entityIndex, reference);
                }
                return result;
            }
        }

#if UNITY_6000_0_OR_NEWER
        struct SkinCopier
        {
            public struct Context { }
            public void CopySkin(ref Context context, in ArchetypeChunk chunk, int entityIndex, Entity srcEntity)
            {
                // Dummy
            }
            public void InitContext()
            {
                // Dummy
            }
        }
#endif
    }

#if UNITY_6000_0_OR_NEWER
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct CopyDeformMaterialsSystem : ISystem
    {
        EntityQuery m_metaQuery;
        LatiosWorldUnmanaged latiosWorld;

        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();

            m_metaQuery = state.Fluent().With<ChunkHeader>(true).With<ChunkCopyDeformTag>(true).With<ChunkPerFrameCullingMask>(true)
                          .With<ChunkPerCameraCullingMask>(false).With<ChunkPerCameraCullingSplitsMask>(false).UseWriteGroups().Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var chunkList = new NativeList<ArchetypeChunk>(m_metaQuery.CalculateEntityCountWithoutFiltering(), state.WorldUpdateAllocator);

            state.Dependency = new FindChunksNeedingCopyingJob
            {
                chunkHeaderHandle            = GetComponentTypeHandle<ChunkHeader>(true),
                chunksToProcess              = chunkList.AsParallelWriter(),
                perDispatchCullingMaskHandle = GetComponentTypeHandle<ChunkPerDispatchCullingMask>(true)
            }.ScheduleParallel(m_metaQuery, state.Dependency);

            var skinCopier = new SkinCopier
            {
                deformClassificationMap    = latiosWorld.worldBlackboardEntity.GetCollectionComponent<DeformClassificationMap>(true).deformClassificationMap,
                materialMaskHandle         = GetComponentTypeHandle<ChunkMaterialPropertyDirtyMask>(false),
                materialPropertyTypeLookup = GetBufferLookup<MaterialPropertyComponentType>(true),
                worldBlackboardEntity      = latiosWorld.worldBlackboardEntity,

                currentDeformHandle        = GetComponentTypeHandle<CurrentDeformShaderIndex>(false),
                currentDqsVertexHandle     = GetComponentTypeHandle<CurrentDqsVertexSkinningShaderIndex>(false),
                currentMatrixVertexHandle  = GetComponentTypeHandle<CurrentMatrixVertexSkinningShaderIndex>(false),
                legacyComputeDeformHandle  = GetComponentTypeHandle<LegacyComputeDeformShaderIndex>(false),
                legacyDotsDeformHandle     = GetComponentTypeHandle<LegacyDotsDeformParamsShaderIndex>(false),
                legacyLbsHandle            = GetComponentTypeHandle<LegacyLinearBlendSkinningShaderIndex>(false),
                previousDeformHandle       = GetComponentTypeHandle<PreviousDeformShaderIndex>(false),
                previousDqsVertexHandle    = GetComponentTypeHandle<PreviousDqsVertexSkinningShaderIndex>(false),
                previousMatrixVertexHandle = GetComponentTypeHandle<PreviousMatrixVertexSkinningShaderIndex>(false),
                twoAgoDeformHandle         = GetComponentTypeHandle<TwoAgoDeformShaderIndex>(false),
                twoAgoDqsVertexHandle      = GetComponentTypeHandle<TwoAgoDqsVertexSkinningShaderIndex>(false),
                twoAgoMatrixVertexHandle   = GetComponentTypeHandle<TwoAgoMatrixVertexSkinningShaderIndex>(false),

                currentDeformLookup        = GetComponentLookup<CurrentDeformShaderIndex>(false),
                currentDqsVertexLookup     = GetComponentLookup<CurrentDqsVertexSkinningShaderIndex>(false),
                currentMatrixVertexLookup  = GetComponentLookup<CurrentMatrixVertexSkinningShaderIndex>(false),
                legacyComputeDeformLookup  = GetComponentLookup<LegacyComputeDeformShaderIndex>(false),
                legacyDotsDeformLookup     = GetComponentLookup<LegacyDotsDeformParamsShaderIndex>(false),
                legacyLbsLookup            = GetComponentLookup<LegacyLinearBlendSkinningShaderIndex>(false),
                previousDeformLookup       = GetComponentLookup<PreviousDeformShaderIndex>(false),
                previousDqsVertexLookup    = GetComponentLookup<PreviousDqsVertexSkinningShaderIndex>(false),
                previousMatrixVertexLookup = GetComponentLookup<PreviousMatrixVertexSkinningShaderIndex>(false),
                twoAgoDeformLookup         = GetComponentLookup<TwoAgoDeformShaderIndex>(false),
                twoAgoDqsVertexLookup      = GetComponentLookup<TwoAgoDqsVertexSkinningShaderIndex>(false),
                twoAgoMatrixVertexLookup   = GetComponentLookup<TwoAgoMatrixVertexSkinningShaderIndex>(false),
            };

            state.Dependency = new CopySkinJob
            {
                chunkPerDispatchMaskHandle = GetComponentTypeHandle<ChunkPerDispatchCullingMask>(true),
                chunkPerFrameMaskHandle    = GetComponentTypeHandle<ChunkPerFrameCullingMask>(true),
                chunksToProcess            = chunkList.AsDeferredJobArray(),
                esiLookup                  = GetEntityStorageInfoLookup(),
                referenceHandle            = GetComponentTypeHandle<CopyDeformFromEntity>(true),
                skinCopier                 = skinCopier,
            }.Schedule(chunkList, 1, state.Dependency);
        }

        [BurstCompile]
        struct FindChunksNeedingCopyingJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<ChunkPerDispatchCullingMask> perDispatchCullingMaskHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkHeader> chunkHeaderHandle;

            public NativeList<ArchetypeChunk>.ParallelWriter chunksToProcess;

            [Unity.Burst.CompilerServices.SkipLocalsInit]
            public unsafe void Execute(in ArchetypeChunk metaChunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var chunksCache = stackalloc ArchetypeChunk[128];
                int chunksCount = 0;
                var masks       = metaChunk.GetNativeArray(ref perDispatchCullingMaskHandle);
                var headers     = metaChunk.GetNativeArray(ref chunkHeaderHandle);
                for (int i = 0; i < metaChunk.Count; i++)
                {
                    var mask = masks[i];
                    if ((mask.lower.Value | mask.upper.Value) != 0)
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
        unsafe struct CopySkinJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<ArchetypeChunk> chunksToProcess;

            [ReadOnly] public ComponentTypeHandle<ChunkPerFrameCullingMask> chunkPerFrameMaskHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkPerDispatchCullingMask> chunkPerDispatchMaskHandle;
            [ReadOnly] public ComponentTypeHandle<CopyDeformFromEntity> referenceHandle;
            [ReadOnly] public EntityStorageInfoLookup esiLookup;

            public SkinCopier skinCopier;

            public void Execute(int i)
            {
                Execute(chunksToProcess[i]);
            }

            void Execute(in ArchetypeChunk chunk)
            {
                SkinCopier.Context context = default;
                skinCopier.InitContext();

                var references   = chunk.GetNativeArray(ref referenceHandle);
                var frameMask    = chunk.GetChunkComponentData(ref chunkPerFrameMaskHandle);
                var dispatchMask = chunk.GetChunkComponentData(ref chunkPerDispatchMaskHandle);
                var maskLower    = dispatchMask.lower.Value & (~frameMask.lower.Value);
                var maskUpper    = dispatchMask.upper.Value & (~frameMask.upper.Value);

                for (int i = math.tzcnt(maskLower); i < 64; maskLower ^= 1ul << i, i = math.tzcnt(maskLower))
                {
                    var reference = references[i].sourceDeformedEntity;
                    if (reference == Entity.Null || !esiLookup.Exists(reference))
                        continue;
                    skinCopier.CopySkin(ref context, in chunk, i, reference);
                }
                for (int i = math.tzcnt(maskUpper); i < 64; maskUpper ^= 1ul << i, i = math.tzcnt(maskUpper))
                {
                    var reference = references[i + 64].sourceDeformedEntity;
                    if (reference == Entity.Null || !esiLookup.Exists(reference))
                        continue;
                    skinCopier.CopySkin(ref context, in chunk, i + 64, reference);
                }
            }
        }
    }
#endif

    struct SkinCopier
    {
        [ReadOnly] public NativeParallelHashMap<ArchetypeChunk, DeformClassification> deformClassificationMap;

        public ComponentTypeHandle<ChunkMaterialPropertyDirtyMask>            materialMaskHandle;
        [NativeDisableContainerSafetyRestriction, NoAlias] NativeArray<ulong> propertyTypeMasks;
        [ReadOnly] public BufferLookup<MaterialPropertyComponentType>         materialPropertyTypeLookup;
        public Entity                                                         worldBlackboardEntity;

        [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<LegacyLinearBlendSkinningShaderIndex> legacyLbsHandle;
        [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<LegacyComputeDeformShaderIndex>       legacyComputeDeformHandle;
        [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<LegacyDotsDeformParamsShaderIndex>    legacyDotsDeformHandle;

        [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CurrentMatrixVertexSkinningShaderIndex>  currentMatrixVertexHandle;
        [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<PreviousMatrixVertexSkinningShaderIndex> previousMatrixVertexHandle;
        [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<TwoAgoMatrixVertexSkinningShaderIndex>   twoAgoMatrixVertexHandle;
        [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CurrentDqsVertexSkinningShaderIndex>     currentDqsVertexHandle;
        [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<PreviousDqsVertexSkinningShaderIndex>    previousDqsVertexHandle;
        [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<TwoAgoDqsVertexSkinningShaderIndex>      twoAgoDqsVertexHandle;

        [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CurrentDeformShaderIndex>  currentDeformHandle;
        [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<PreviousDeformShaderIndex> previousDeformHandle;
        [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<TwoAgoDeformShaderIndex>   twoAgoDeformHandle;

        [NativeDisableParallelForRestriction] public ComponentLookup<LegacyLinearBlendSkinningShaderIndex> legacyLbsLookup;
        [NativeDisableParallelForRestriction] public ComponentLookup<LegacyComputeDeformShaderIndex>       legacyComputeDeformLookup;
        [NativeDisableParallelForRestriction] public ComponentLookup<LegacyDotsDeformParamsShaderIndex>    legacyDotsDeformLookup;

        [NativeDisableParallelForRestriction] public ComponentLookup<CurrentMatrixVertexSkinningShaderIndex>  currentMatrixVertexLookup;
        [NativeDisableParallelForRestriction] public ComponentLookup<PreviousMatrixVertexSkinningShaderIndex> previousMatrixVertexLookup;
        [NativeDisableParallelForRestriction] public ComponentLookup<TwoAgoMatrixVertexSkinningShaderIndex>   twoAgoMatrixVertexLookup;
        [NativeDisableParallelForRestriction] public ComponentLookup<CurrentDqsVertexSkinningShaderIndex>     currentDqsVertexLookup;
        [NativeDisableParallelForRestriction] public ComponentLookup<PreviousDqsVertexSkinningShaderIndex>    previousDqsVertexLookup;
        [NativeDisableParallelForRestriction] public ComponentLookup<TwoAgoDqsVertexSkinningShaderIndex>      twoAgoDqsVertexLookup;

        [NativeDisableParallelForRestriction] public ComponentLookup<CurrentDeformShaderIndex>  currentDeformLookup;
        [NativeDisableParallelForRestriction] public ComponentLookup<PreviousDeformShaderIndex> previousDeformLookup;
        [NativeDisableParallelForRestriction] public ComponentLookup<TwoAgoDeformShaderIndex>   twoAgoDeformLookup;

        public struct Context
        {
            public ArchetypeChunk       cachedChunk;
            public DeformClassification classification;

            public NativeArray<uint>  legacyLbsArray;
            public NativeArray<uint>  legacyComputeDeformArray;
            public NativeArray<uint4> legacyDotsDeformArray;

            public NativeArray<uint>                                 currentMatrixArray;
            public NativeArray<uint>                                 previousMatrixArray;
            public NativeArray<uint>                                 twoAgoMatrixArray;
            public NativeArray<CurrentDqsVertexSkinningShaderIndex>  currentDqsArray;
            public NativeArray<PreviousDqsVertexSkinningShaderIndex> previousDqsArray;
            public NativeArray<TwoAgoDqsVertexSkinningShaderIndex>   twoAgoDqsArray;

            public NativeArray<uint> currentDeformArray;
            public NativeArray<uint> previousDeformArray;
            public NativeArray<uint> twoAgoDeformArray;
        }

        public void InitContext()
        {
            if (!propertyTypeMasks.IsCreated)
            {
                var materialProperties = materialPropertyTypeLookup[worldBlackboardEntity].AsNativeArray().Reinterpret<ComponentType>();
                propertyTypeMasks      = new NativeArray<ulong>(2 * 12, Allocator.Temp);

                var index            = materialProperties.IndexOf(ComponentType.ReadOnly<CurrentDeformShaderIndex>());
                propertyTypeMasks[0] = index >= 64 ? 0ul : (1ul << index);
                propertyTypeMasks[1] = index >= 64 ? (1ul << (index - 64)) : 0;

                index                = materialProperties.IndexOf(ComponentType.ReadOnly<PreviousDeformShaderIndex>());
                propertyTypeMasks[2] = index >= 64 ? 0ul : (1ul << index);
                propertyTypeMasks[3] = index >= 64 ? (1ul << (index - 64)) : 0;

                index                = materialProperties.IndexOf(ComponentType.ReadOnly<TwoAgoDeformShaderIndex>());
                propertyTypeMasks[4] = index >= 64 ? 0ul : (1ul << index);
                propertyTypeMasks[5] = index >= 64 ? (1ul << (index - 64)) : 0;

                index                = materialProperties.IndexOf(ComponentType.ReadOnly<CurrentMatrixVertexSkinningShaderIndex>());
                propertyTypeMasks[6] = index >= 64 ? 0ul : (1ul << index);
                propertyTypeMasks[7] = index >= 64 ? (1ul << (index - 64)) : 0;

                index                = materialProperties.IndexOf(ComponentType.ReadOnly<PreviousMatrixVertexSkinningShaderIndex>());
                propertyTypeMasks[8] = index >= 64 ? 0ul : (1ul << index);
                propertyTypeMasks[9] = index >= 64 ? (1ul << (index - 64)) : 0;

                index                 = materialProperties.IndexOf(ComponentType.ReadOnly<TwoAgoMatrixVertexSkinningShaderIndex>());
                propertyTypeMasks[10] = index >= 64 ? 0ul : (1ul << index);
                propertyTypeMasks[11] = index >= 64 ? (1ul << (index - 64)) : 0;

                index                 = materialProperties.IndexOf(ComponentType.ReadOnly<CurrentDqsVertexSkinningShaderIndex>());
                propertyTypeMasks[12] = index >= 64 ? 0ul : (1ul << index);
                propertyTypeMasks[13] = index >= 64 ? (1ul << (index - 64)) : 0;

                index                 = materialProperties.IndexOf(ComponentType.ReadOnly<PreviousDqsVertexSkinningShaderIndex>());
                propertyTypeMasks[14] = index >= 64 ? 0ul : (1ul << index);
                propertyTypeMasks[15] = index >= 64 ? (1ul << (index - 64)) : 0;

                index                 = materialProperties.IndexOf(ComponentType.ReadOnly<TwoAgoDqsVertexSkinningShaderIndex>());
                propertyTypeMasks[16] = index >= 64 ? 0ul : (1ul << index);
                propertyTypeMasks[17] = index >= 64 ? (1ul << (index - 64)) : 0;

                index                 = materialProperties.IndexOf(ComponentType.ReadOnly<LegacyLinearBlendSkinningShaderIndex>());
                propertyTypeMasks[18] = index >= 64 ? 0ul : (1ul << index);
                propertyTypeMasks[19] = index >= 64 ? (1ul << (index - 64)) : 0;

                index                 = materialProperties.IndexOf(ComponentType.ReadOnly<LegacyComputeDeformShaderIndex>());
                propertyTypeMasks[20] = index >= 64 ? 0ul : (1ul << index);
                propertyTypeMasks[21] = index >= 64 ? (1ul << (index - 64)) : 0;

                index                 = materialProperties.IndexOf(ComponentType.ReadOnly<LegacyDotsDeformParamsShaderIndex>());
                propertyTypeMasks[22] = index >= 64 ? 0ul : (1ul << index);
                propertyTypeMasks[23] = index >= 64 ? (1ul << (index - 64)) : 0;
            }
        }

        public void CopySkin(ref Context context, in ArchetypeChunk chunk, int indexInChunk, Entity srcEntity)
        {
            if (chunk != context.cachedChunk)
            {
                context.cachedChunk    = chunk;
                context.classification = deformClassificationMap[chunk];
                var classification     = context.classification;

                ulong lower = 0ul;
                ulong upper = 0ul;

                if ((classification & DeformClassification.CurrentDeform) != DeformClassification.None)
                {
                    lower                      |= propertyTypeMasks[0];
                    upper                      |= propertyTypeMasks[1];
                    context.currentDeformArray  = chunk.GetNativeArray(ref currentDeformHandle).Reinterpret<uint>();
                }
                if ((classification & DeformClassification.PreviousDeform) != DeformClassification.None)
                {
                    lower                       |= propertyTypeMasks[2];
                    upper                       |= propertyTypeMasks[3];
                    context.previousDeformArray  = chunk.GetNativeArray(ref previousDeformHandle).Reinterpret<uint>();
                }
                if ((classification & DeformClassification.TwoAgoDeform) != DeformClassification.None)
                {
                    lower                     |= propertyTypeMasks[4];
                    upper                     |= propertyTypeMasks[5];
                    context.twoAgoDeformArray  = chunk.GetNativeArray(ref twoAgoDeformHandle).Reinterpret<uint>();
                }
                if ((classification & DeformClassification.CurrentVertexMatrix) != DeformClassification.None)
                {
                    lower                      |= propertyTypeMasks[6];
                    upper                      |= propertyTypeMasks[7];
                    context.currentMatrixArray  = chunk.GetNativeArray(ref currentMatrixVertexHandle).Reinterpret<uint>();
                }
                if ((classification & DeformClassification.PreviousVertexMatrix) != DeformClassification.None)
                {
                    lower                       |= propertyTypeMasks[8];
                    upper                       |= propertyTypeMasks[9];
                    context.previousMatrixArray  = chunk.GetNativeArray(ref previousMatrixVertexHandle).Reinterpret<uint>();
                }
                if ((classification & DeformClassification.TwoAgoVertexMatrix) != DeformClassification.None)
                {
                    lower                     |= propertyTypeMasks[10];
                    upper                     |= propertyTypeMasks[11];
                    context.twoAgoMatrixArray  = chunk.GetNativeArray(ref twoAgoMatrixVertexHandle).Reinterpret<uint>();
                }
                if ((classification & DeformClassification.CurrentVertexDqs) != DeformClassification.None)
                {
                    lower                   |= propertyTypeMasks[12];
                    upper                   |= propertyTypeMasks[13];
                    context.currentDqsArray  = chunk.GetNativeArray(ref currentDqsVertexHandle);
                }
                if ((classification & DeformClassification.PreviousVertexDqs) != DeformClassification.None)
                {
                    lower                    |= propertyTypeMasks[14];
                    upper                    |= propertyTypeMasks[15];
                    context.previousDqsArray  = chunk.GetNativeArray(ref previousDqsVertexHandle);
                }
                if ((classification & DeformClassification.TwoAgoVertexDqs) != DeformClassification.None)
                {
                    lower                  |= propertyTypeMasks[16];
                    upper                  |= propertyTypeMasks[17];
                    context.twoAgoDqsArray  = chunk.GetNativeArray(ref twoAgoDqsVertexHandle);
                }
                if ((classification & DeformClassification.LegacyLbs) != DeformClassification.None)
                {
                    lower                  |= propertyTypeMasks[18];
                    upper                  |= propertyTypeMasks[19];
                    context.legacyLbsArray  = chunk.GetNativeArray(ref legacyLbsHandle).Reinterpret<uint>();
                }
                if ((classification & DeformClassification.LegacyCompute) != DeformClassification.None)
                {
                    lower                            |= propertyTypeMasks[20];
                    upper                            |= propertyTypeMasks[21];
                    context.legacyComputeDeformArray  = chunk.GetNativeArray(ref legacyComputeDeformHandle).Reinterpret<uint>();
                }
                if ((classification & DeformClassification.LegacyDotsDefom) != DeformClassification.None)
                {
                    lower                         |= propertyTypeMasks[22];
                    upper                         |= propertyTypeMasks[23];
                    context.legacyDotsDeformArray  = chunk.GetNativeArray(ref legacyDotsDeformHandle).Reinterpret<uint4>();
                }

                ref var mask      = ref chunk.GetChunkComponentRefRW(ref materialMaskHandle);
                mask.lower.Value |= lower;
                mask.upper.Value |= upper;
            }

            if ((context.classification & DeformClassification.CurrentDeform) != DeformClassification.None)
                context.currentDeformArray[indexInChunk] = currentDeformLookup[srcEntity].firstVertexIndex;
            if ((context.classification & DeformClassification.PreviousDeform) != DeformClassification.None)
                context.previousDeformArray[indexInChunk] = previousDeformLookup[srcEntity].firstVertexIndex;
            if ((context.classification & DeformClassification.TwoAgoDeform) != DeformClassification.None)
                context.twoAgoDeformArray[indexInChunk] = twoAgoDeformLookup[srcEntity].firstVertexIndex;
            if ((context.classification & DeformClassification.CurrentVertexMatrix) != DeformClassification.None)
                context.currentMatrixArray[indexInChunk] = currentMatrixVertexLookup[srcEntity].firstMatrixIndex;
            if ((context.classification & DeformClassification.PreviousVertexMatrix) != DeformClassification.None)
                context.previousMatrixArray[indexInChunk] = previousMatrixVertexLookup[srcEntity].firstMatrixIndex;
            if ((context.classification & DeformClassification.TwoAgoVertexMatrix) != DeformClassification.None)
                context.twoAgoMatrixArray[indexInChunk] = twoAgoMatrixVertexLookup[srcEntity].firstMatrixIndex;
            if ((context.classification & DeformClassification.CurrentVertexDqs) != DeformClassification.None)
                context.currentDqsArray[indexInChunk] = currentDqsVertexLookup[srcEntity];
            if ((context.classification & DeformClassification.PreviousVertexDqs) != DeformClassification.None)
                context.previousDqsArray[indexInChunk] = previousDqsVertexLookup[srcEntity];
            if ((context.classification & DeformClassification.TwoAgoVertexDqs) != DeformClassification.None)
                context.twoAgoDqsArray[indexInChunk] = twoAgoDqsVertexLookup[srcEntity];
            if ((context.classification & DeformClassification.LegacyLbs) != DeformClassification.None)
                context.legacyLbsArray[indexInChunk] = legacyLbsLookup[srcEntity].firstMatrixIndex;
            if ((context.classification & DeformClassification.LegacyCompute) != DeformClassification.None)
                context.legacyComputeDeformArray[indexInChunk] = legacyComputeDeformLookup[srcEntity].firstVertexIndex;
            if ((context.classification & DeformClassification.LegacyDotsDefom) != DeformClassification.None)
                context.legacyDotsDeformArray[indexInChunk] = legacyDotsDeformLookup[srcEntity].parameters;
        }
    }
}

