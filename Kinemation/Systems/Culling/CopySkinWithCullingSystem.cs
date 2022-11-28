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

namespace Latios.Kinemation.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct CopySkinWithCullingSystem : ISystem
    {
        EntityQuery          m_metaQuery;
        LatiosWorldUnmanaged latiosWorld;

        FindChunksNeedingCopyingJob m_findJob;
        SingleSplitCopySkinJob      m_singleJob;
        MultiSplitCopySkinJob       m_multiJob;

        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();

            m_metaQuery = state.Fluent().WithAll<ChunkHeader>(true).WithAll<ChunkCopySkinShaderData>(true).WithAll<ChunkPerFrameCullingMask>(true)
                          .WithAll<ChunkPerCameraCullingMask>(false).WithAll<ChunkPerCameraCullingSplitsMask>(false).UseWriteGroups().Build();

            m_findJob = new FindChunksNeedingCopyingJob
            {
                perCameraCullingMaskHandle = state.GetComponentTypeHandle<ChunkPerCameraCullingMask>(true),
                chunkHeaderHandle          = state.GetComponentTypeHandle<ChunkHeader>(true)
            };

            m_singleJob = new SingleSplitCopySkinJob
            {
                chunkPerFrameMaskHandle        = state.GetComponentTypeHandle<ChunkPerFrameCullingMask>(true),
                referenceHandle                = state.GetComponentTypeHandle<ShareSkinFromEntity>(true),
                esiLookup                      = state.GetEntityStorageInfoLookup(),
                chunkPerCameraMaskHandle       = state.GetComponentTypeHandle<ChunkPerCameraCullingMask>(false),
                chunkMaterialPropertyDirtyMask = state.GetComponentTypeHandle<ChunkMaterialPropertyDirtyMask>(false),
                computeLookup                  = state.GetComponentLookup<ComputeDeformShaderIndex>(false),
                linearBlendLookup              = state.GetComponentLookup<LinearBlendSkinningShaderIndex>(false),
                computeDeformHandle            = state.GetComponentTypeHandle<ComputeDeformShaderIndex>(false),
                linearBlendHandle              = state.GetComponentTypeHandle<LinearBlendSkinningShaderIndex>(false),
            };

            m_multiJob = new MultiSplitCopySkinJob
            {
                chunkPerFrameMaskHandle        = m_singleJob.chunkPerFrameMaskHandle,
                referenceHandle                = m_singleJob.referenceHandle,
                esiLookup                      = m_singleJob.esiLookup,
                chunkPerCameraMaskHandle       = m_singleJob.chunkPerCameraMaskHandle,
                chunkPerCameraSplitsMaskHandle = state.GetComponentTypeHandle<ChunkPerCameraCullingSplitsMask>(false),
                chunkMaterialPropertyDirtyMask = m_singleJob.chunkMaterialPropertyDirtyMask,
                computeLookup                  = m_singleJob.computeLookup,
                linearBlendLookup              = m_singleJob.linearBlendLookup,
                computeDeformHandle            = m_singleJob.computeDeformHandle,
                linearBlendHandle              = m_singleJob.linearBlendHandle
            };
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var chunkList = new NativeList<ArchetypeChunk>(m_metaQuery.CalculateEntityCountWithoutFiltering(), state.WorldUpdateAllocator);

            m_findJob.chunkHeaderHandle.Update(ref state);
            m_findJob.chunksToProcess = chunkList.AsParallelWriter();
            m_findJob.perCameraCullingMaskHandle.Update(ref state);
            state.Dependency = m_findJob.ScheduleParallelByRef(m_metaQuery, state.Dependency);

            int linearBlendIndex = latiosWorld.worldBlackboardEntity.GetBuffer<MaterialPropertyComponentType>(true).Reinterpret<ComponentType>().AsNativeArray()
                                   .IndexOf(ComponentType.ReadOnly<LinearBlendSkinningShaderIndex>());
            ulong linearBlendMaterialMaskLower = (ulong)linearBlendIndex >= 64UL ? 0UL : (1UL << linearBlendIndex);
            ulong linearBlendMaterialMaskUpper = (ulong)linearBlendIndex >= 64UL ? (1UL << (linearBlendIndex - 64)) : 0UL;

            int deformIndex = latiosWorld.worldBlackboardEntity.GetBuffer<MaterialPropertyComponentType>(true).Reinterpret<ComponentType>().AsNativeArray()
                              .IndexOf(ComponentType.ReadOnly<ComputeDeformShaderIndex>());
            ulong deformMaterialMaskLower = (ulong)deformIndex >= 64UL ? 0UL : (1UL << deformIndex);
            ulong deformMaterialMaskUpper = (ulong)deformIndex >= 64UL ? (1UL << (deformIndex - 64)) : 0UL;

            var cullRequestType = latiosWorld.worldBlackboardEntity.GetComponentData<CullingContext>().viewType;
            if (cullRequestType == BatchCullingViewType.Light)
            {
                m_multiJob.chunkPerFrameMaskHandle.Update(ref state);
                m_multiJob.referenceHandle.Update(ref state);
                m_multiJob.esiLookup.Update(ref state);
                m_multiJob.chunkPerCameraMaskHandle.Update(ref state);
                m_multiJob.chunkPerCameraSplitsMaskHandle.Update(ref state);
                m_multiJob.chunkMaterialPropertyDirtyMask.Update(ref state);
                m_multiJob.computeLookup.Update(ref state);
                m_multiJob.linearBlendLookup.Update(ref state);
                m_multiJob.computeDeformHandle.Update(ref state);
                m_multiJob.linearBlendHandle.Update(ref state);
                m_multiJob.linearBlendMaterialMaskLower = linearBlendMaterialMaskLower;
                m_multiJob.linearBlendMaterialMaskUpper = linearBlendMaterialMaskUpper;
                m_multiJob.deformMaterialMaskLower      = deformMaterialMaskLower;
                m_multiJob.deformMaterialMaskUpper      = deformMaterialMaskUpper;
                m_multiJob.chunksToProcess              = chunkList.AsDeferredJobArray();

                state.Dependency = m_multiJob.ScheduleByRef(chunkList, 1, state.Dependency);
            }
            else
            {
                m_singleJob.chunkPerFrameMaskHandle.Update(ref state);
                m_singleJob.referenceHandle.Update(ref state);
                m_singleJob.esiLookup.Update(ref state);
                m_singleJob.chunkPerCameraMaskHandle.Update(ref state);
                m_singleJob.chunkMaterialPropertyDirtyMask.Update(ref state);
                m_singleJob.computeLookup.Update(ref state);
                m_singleJob.linearBlendLookup.Update(ref state);
                m_singleJob.computeDeformHandle.Update(ref state);
                m_singleJob.linearBlendHandle.Update(ref state);
                m_singleJob.linearBlendMaterialMaskLower = linearBlendMaterialMaskLower;
                m_singleJob.linearBlendMaterialMaskUpper = linearBlendMaterialMaskUpper;
                m_singleJob.deformMaterialMaskLower      = deformMaterialMaskLower;
                m_singleJob.deformMaterialMaskUpper      = deformMaterialMaskUpper;
                m_singleJob.chunksToProcess              = chunkList.AsDeferredJobArray();

                state.Dependency = m_singleJob.ScheduleByRef(chunkList, 1, state.Dependency);
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
            [ReadOnly] public ComponentTypeHandle<ShareSkinFromEntity>      referenceHandle;

            [ReadOnly] public EntityStorageInfoLookup esiLookup;

            public ComponentTypeHandle<ChunkPerCameraCullingMask>      chunkPerCameraMaskHandle;
            public ComponentTypeHandle<ChunkMaterialPropertyDirtyMask> chunkMaterialPropertyDirtyMask;

            [NativeDisableParallelForRestriction] public ComponentLookup<ComputeDeformShaderIndex>       computeLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<LinearBlendSkinningShaderIndex> linearBlendLookup;

            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<ComputeDeformShaderIndex>       computeDeformHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<LinearBlendSkinningShaderIndex> linearBlendHandle;

            public ulong linearBlendMaterialMaskLower;
            public ulong linearBlendMaterialMaskUpper;
            public ulong deformMaterialMaskLower;
            public ulong deformMaterialMaskUpper;

            public void Execute(int i)
            {
                Execute(chunksToProcess[i]);
            }

            void Execute(in ArchetypeChunk chunk)
            {
                var context = new MaterialContext();
                context.Begin(in chunk, in linearBlendLookup, in computeLookup, in linearBlendHandle, in computeDeformHandle);

                var references                 = chunk.GetNativeArray(ref referenceHandle);
                var invertedFrameMasks         = chunk.GetChunkComponentData(ref chunkPerFrameMaskHandle);
                invertedFrameMasks.lower.Value = ~invertedFrameMasks.lower.Value;
                invertedFrameMasks.upper.Value = ~invertedFrameMasks.upper.Value;
                ref var cameraMask             = ref chunk.GetChunkComponentRefRW(in chunkPerCameraMaskHandle);

                var inMask = cameraMask.lower.Value;
                for (int i = math.tzcnt(inMask); i < 64; inMask ^= 1ul << i, i = math.tzcnt(inMask))
                {
                    bool isIn = IsReferenceVisible(references[i].sourceSkinnedEntity,
                                                   invertedFrameMasks.lower.IsSet(i),
                                                   i,
                                                   ref context);
                    cameraMask.lower.Value &= ~(math.select(1ul, 0ul, isIn) << i);
                }
                inMask = cameraMask.upper.Value;
                for (int i = math.tzcnt(inMask); i < 64; inMask ^= 1ul << i, i = math.tzcnt(inMask))
                {
                    bool isIn = IsReferenceVisible(references[i + 64].sourceSkinnedEntity,
                                                   invertedFrameMasks.upper.IsSet(i),
                                                   i + 64,
                                                   ref context);
                    cameraMask.upper.Value &= ~(math.select(1ul, 0ul, isIn) << i);
                }

                if (context.linearBlendDirty || context.computeDeformDirty)
                {
                    ref var dirtyMask = ref chunk.GetChunkComponentRefRW(in chunkMaterialPropertyDirtyMask);
                    if (context.linearBlendDirty)
                    {
                        dirtyMask.lower.Value |= linearBlendMaterialMaskLower;
                        dirtyMask.upper.Value |= linearBlendMaterialMaskUpper;
                    }
                    if (context.computeDeformDirty)
                    {
                        dirtyMask.lower.Value |= deformMaterialMaskLower;
                        dirtyMask.upper.Value |= deformMaterialMaskUpper;
                    }
                }

                context.End(ref linearBlendLookup, ref computeLookup, ref linearBlendHandle, ref computeDeformHandle);
            }

            bool IsReferenceVisible(Entity reference, bool needsCopy, int entityIndex, ref MaterialContext context)
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
                    context.CopySkin(entityIndex, reference);
                }
                return result;
            }
        }

        [BurstCompile]
        unsafe struct MultiSplitCopySkinJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<ArchetypeChunk> chunksToProcess;

            [ReadOnly] public ComponentTypeHandle<ChunkPerFrameCullingMask> chunkPerFrameMaskHandle;
            [ReadOnly] public ComponentTypeHandle<ShareSkinFromEntity>      referenceHandle;

            [ReadOnly] public EntityStorageInfoLookup esiLookup;

            public ComponentTypeHandle<ChunkPerCameraCullingMask>       chunkPerCameraMaskHandle;
            public ComponentTypeHandle<ChunkPerCameraCullingSplitsMask> chunkPerCameraSplitsMaskHandle;
            public ComponentTypeHandle<ChunkMaterialPropertyDirtyMask>  chunkMaterialPropertyDirtyMask;

            [NativeDisableParallelForRestriction] public ComponentLookup<ComputeDeformShaderIndex>       computeLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<LinearBlendSkinningShaderIndex> linearBlendLookup;

            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<ComputeDeformShaderIndex>       computeDeformHandle;
            [NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<LinearBlendSkinningShaderIndex> linearBlendHandle;

            public ulong linearBlendMaterialMaskLower;
            public ulong linearBlendMaterialMaskUpper;
            public ulong deformMaterialMaskLower;
            public ulong deformMaterialMaskUpper;

            public void Execute(int i)
            {
                Execute(chunksToProcess[i]);
            }

            void Execute(in ArchetypeChunk chunk)
            {
                var context = new MaterialContext();
                context.Begin(in chunk, in linearBlendLookup, in computeLookup, in linearBlendHandle, in computeDeformHandle);

                var references                 = chunk.GetNativeArray(ref referenceHandle);
                var invertedFrameMasks         = chunk.GetChunkComponentData(ref chunkPerFrameMaskHandle);
                invertedFrameMasks.lower.Value = ~invertedFrameMasks.lower.Value;
                invertedFrameMasks.upper.Value = ~invertedFrameMasks.upper.Value;
                ref var cameraMask             = ref chunk.GetChunkComponentRefRW(in chunkPerCameraMaskHandle);
                ref var cameraSplitsMask       = ref chunk.GetChunkComponentRefRW(in chunkPerCameraSplitsMaskHandle);
                cameraSplitsMask               = default;

                var inMask = cameraMask.lower.Value;
                for (int i = math.tzcnt(inMask); i < 64; inMask ^= 1ul << i, i = math.tzcnt(inMask))
                {
                    bool isIn = IsReferenceVisible(references[i].sourceSkinnedEntity,
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
                    bool isIn = IsReferenceVisible(references[i + 64].sourceSkinnedEntity,
                                                   invertedFrameMasks.upper.IsSet(i),
                                                   i + 64,
                                                   ref context,
                                                   out var splits);
                    cameraMask.upper.Value              &= ~(math.select(1ul, 0ul, isIn) << i);
                    cameraSplitsMask.splitMasks[i + 64]  = splits;
                }

                if (context.linearBlendDirty || context.computeDeformDirty)
                {
                    ref var dirtyMask = ref chunk.GetChunkComponentRefRW(in chunkMaterialPropertyDirtyMask);
                    if (context.linearBlendDirty)
                    {
                        dirtyMask.lower.Value |= linearBlendMaterialMaskLower;
                        dirtyMask.upper.Value |= linearBlendMaterialMaskUpper;
                    }
                    if (context.computeDeformDirty)
                    {
                        dirtyMask.lower.Value |= deformMaterialMaskLower;
                        dirtyMask.upper.Value |= deformMaterialMaskUpper;
                    }
                }

                context.End(ref linearBlendLookup, ref computeLookup, ref linearBlendHandle, ref computeDeformHandle);
            }

            bool IsReferenceVisible(Entity reference, bool needsCopy, int entityIndex, ref MaterialContext context, out byte splits)
            {
                splits = default;
                if (reference == Entity.Null || !esiLookup.Exists(reference))
                    return false;

                var  info          = esiLookup[reference];
                var  referenceMask = info.Chunk.GetChunkComponentRefRO(in chunkPerCameraMaskHandle);
                bool result;
                if (info.IndexInChunk >= 64)
                    result = referenceMask.ValueRO.upper.IsSet(info.IndexInChunk - 64);
                else
                    result = referenceMask.ValueRO.lower.IsSet(info.IndexInChunk);

                if (result)
                {
                    var referenceSplits = info.Chunk.GetChunkComponentRefRO(in chunkPerCameraSplitsMaskHandle);
                    splits              = referenceSplits.ValueRO.splitMasks[info.IndexInChunk];
                }

                if (result && needsCopy)
                {
                    context.CopySkin(entityIndex, reference);
                }
                return result;
            }
        }

        // A context object used to copy skinning indices while preserving caches as much as possible.
        struct MaterialContext
        {
            bool                                                newChunk;
            ArchetypeChunk                                      currentChunk;
            NativeArray<LinearBlendSkinningShaderIndex>         linearBlendChunkArray;
            NativeArray<ComputeDeformShaderIndex>               computeDeformChunkArray;
            bool                                                hasLinearBlend;
            bool                                                hasComputeDeform;
            ComponentTypeHandle<LinearBlendSkinningShaderIndex> copySkinLinearBlendHandle;
            ComponentLookup<LinearBlendSkinningShaderIndex>     referenceLinearBlendLookup;
            ComponentTypeHandle<ComputeDeformShaderIndex>       copySkinComputeDeformHandle;
            ComponentLookup<ComputeDeformShaderIndex>           referenceComputeDeformLookup;

            public void Begin(in ArchetypeChunk chunk, in ComponentLookup<LinearBlendSkinningShaderIndex> lbsLookup, in ComponentLookup<ComputeDeformShaderIndex> cdsLookup,
                              in ComponentTypeHandle<LinearBlendSkinningShaderIndex> lbsHandle, in ComponentTypeHandle<ComputeDeformShaderIndex> cdsHandle)
            {
                copySkinLinearBlendHandle    = lbsHandle;
                referenceLinearBlendLookup   = lbsLookup;
                copySkinComputeDeformHandle  = cdsHandle;
                referenceComputeDeformLookup = cdsLookup;
                newChunk                     = true;
                hasComputeDeform             = false;
                hasLinearBlend               = false;
                currentChunk                 = chunk;
            }

            public void End(ref ComponentLookup<LinearBlendSkinningShaderIndex> lbsLookup, ref ComponentLookup<ComputeDeformShaderIndex> cdsLookup,
                            ref ComponentTypeHandle<LinearBlendSkinningShaderIndex> lbsHandle, ref ComponentTypeHandle<ComputeDeformShaderIndex> cdsHandle)
            {
                lbsHandle = copySkinLinearBlendHandle;
                lbsLookup = referenceLinearBlendLookup;
                cdsHandle = copySkinComputeDeformHandle;
                cdsLookup = referenceComputeDeformLookup;
            }

            public bool linearBlendDirty => hasLinearBlend;
            public bool computeDeformDirty => hasComputeDeform;

            public void CopySkin(int entityIndex, Entity reference)
            {
                if (Hint.Unlikely(newChunk))
                {
                    newChunk         = false;
                    hasLinearBlend   = currentChunk.Has(ref copySkinLinearBlendHandle);
                    hasComputeDeform = currentChunk.Has(ref copySkinComputeDeformHandle);
                    if (hasLinearBlend)
                        linearBlendChunkArray = currentChunk.GetNativeArray(ref copySkinLinearBlendHandle);
                    if (hasComputeDeform)
                        computeDeformChunkArray = currentChunk.GetNativeArray(ref copySkinComputeDeformHandle);
                }

                if (hasLinearBlend)
                    linearBlendChunkArray[entityIndex] = referenceLinearBlendLookup[reference];
                if (hasComputeDeform)
                    computeDeformChunkArray[entityIndex] = referenceComputeDeformLookup[reference];
            }
        }
    }
}

