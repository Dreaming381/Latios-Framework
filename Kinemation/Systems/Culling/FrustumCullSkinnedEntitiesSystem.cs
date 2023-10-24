using Latios.Kinemation.InternalSourceGen;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Rendering;

using static Unity.Entities.SystemAPI;

namespace Latios.Kinemation.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct FrustumCullSkinnedEntitiesSystem : ISystem
    {
        EntityQuery          m_metaQuery;
        LatiosWorldUnmanaged latiosWorld;

        FindChunksNeedingFrustumCullingJob m_findJob;
        SingleSplitCullingJob              m_singleJob;
        MultiSplitCullingJob               m_multiJob;

        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();

            m_metaQuery = state.Fluent().With<ChunkHeader>(true).With<ChunkSkinningCullingTag>(true).With<ChunkPerFrameCullingMask>(true)
                          .With<ChunkPerCameraCullingMask>(false).With<ChunkPerCameraCullingSplitsMask>(false).UseWriteGroups().Build();

            m_findJob = new FindChunksNeedingFrustumCullingJob
            {
                perCameraCullingMaskHandle = state.GetComponentTypeHandle<ChunkPerCameraCullingMask>(true),
                chunkHeaderHandle          = state.GetComponentTypeHandle<ChunkHeader>(true),
                postProcessMatrixHandle    = state.GetComponentTypeHandle<PostProcessMatrix>(true)
            };

            m_singleJob = new SingleSplitCullingJob
            {
                dependentHandle          = state.GetComponentTypeHandle<SkeletonDependent>(true),
                chunkSkeletonMaskHandle  = state.GetComponentTypeHandle<ChunkPerCameraSkeletonCullingMask>(true),
                esiLookup                = state.GetEntityStorageInfoLookup(),
                chunkPerCameraMaskHandle = state.GetComponentTypeHandle<ChunkPerCameraCullingMask>(false),
            };

            m_multiJob = new MultiSplitCullingJob
            {
                dependentHandle                = m_singleJob.dependentHandle,
                chunkSkeletonMaskHandle        = m_singleJob.chunkSkeletonMaskHandle,
                chunkSkeletonSplitsMaskHandle  = state.GetComponentTypeHandle<ChunkPerCameraSkeletonCullingSplitsMask>(true),
                esiLookup                      = m_singleJob.esiLookup,
                chunkPerCameraMaskHandle       = m_singleJob.chunkPerCameraMaskHandle,
                chunkPerCameraSplitsMaskHandle = state.GetComponentTypeHandle<ChunkPerCameraCullingSplitsMask>(false),
            };
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var chunkList = new NativeList<ArchetypeChunk>(m_metaQuery.CalculateEntityCountWithoutFiltering(), state.WorldUpdateAllocator);

            m_findJob.chunkHeaderHandle.Update(ref state);
            m_findJob.chunksToProcess = chunkList.AsParallelWriter();
            m_findJob.perCameraCullingMaskHandle.Update(ref state);
            m_findJob.postProcessMatrixHandle.Update(ref state);
            state.Dependency = m_findJob.ScheduleParallelByRef(m_metaQuery, state.Dependency);

            var cullRequestType = latiosWorld.worldBlackboardEntity.GetComponentData<CullingContext>().viewType;
            if (cullRequestType == BatchCullingViewType.Light)
            {
                m_multiJob.dependentHandle.Update(ref state);
                m_multiJob.chunkSkeletonMaskHandle.Update(ref state);
                m_multiJob.chunkSkeletonSplitsMaskHandle.Update(ref state);
                m_multiJob.esiLookup.Update(ref state);
                m_multiJob.chunkPerCameraMaskHandle.Update(ref state);
                m_multiJob.chunkPerCameraSplitsMaskHandle.Update(ref state);
                m_multiJob.chunksToProcess = chunkList.AsDeferredJobArray();

                state.Dependency = m_multiJob.ScheduleByRef(chunkList, 1, state.Dependency);
            }
            else
            {
                m_singleJob.dependentHandle.Update(ref state);
                m_singleJob.chunkSkeletonMaskHandle.Update(ref state);
                m_singleJob.esiLookup.Update(ref state);
                m_singleJob.chunkPerCameraMaskHandle.Update(ref state);
                m_singleJob.chunksToProcess = chunkList.AsDeferredJobArray();

                state.Dependency = m_singleJob.ScheduleByRef(chunkList, 1, state.Dependency);
            }
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        struct FindChunksNeedingFrustumCullingJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<ChunkPerCameraCullingMask> perCameraCullingMaskHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkHeader>               chunkHeaderHandle;
            [ReadOnly] public ComponentTypeHandle<PostProcessMatrix>         postProcessMatrixHandle;

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
                    // Skinned PostProcessMatrix entities are handled in a separate system.
                    if ((mask.lower.Value | mask.upper.Value) != 0 && !headers[i].ArchetypeChunk.Has(ref postProcessMatrixHandle))
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
        unsafe struct SingleSplitCullingJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<ArchetypeChunk> chunksToProcess;

            [ReadOnly] public ComponentTypeHandle<SkeletonDependent>                 dependentHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkPerCameraSkeletonCullingMask> chunkSkeletonMaskHandle;
            [ReadOnly] public EntityStorageInfoLookup                                esiLookup;

            public ComponentTypeHandle<ChunkPerCameraCullingMask> chunkPerCameraMaskHandle;

            public void Execute(int i)
            {
                Execute(chunksToProcess[i]);
            }

            void Execute(in ArchetypeChunk chunk)
            {
                ref var cameraMask = ref chunk.GetChunkComponentRefRW(ref chunkPerCameraMaskHandle);
                if (!chunk.Has(ref dependentHandle))
                {
                    cameraMask = default;
                    return;
                }

                var rootRefs = chunk.GetNativeArray(ref dependentHandle);

                var inMask = cameraMask.lower.Value;
                for (int i = math.tzcnt(inMask); i < 64; inMask ^= 1ul << i, i = math.tzcnt(inMask))
                {
                    bool isIn               = IsSkeletonVisible(rootRefs[i].root);
                    cameraMask.lower.Value &= ~(math.select(1ul, 0ul, isIn) << i);
                }
                inMask = cameraMask.upper.Value;
                for (int i = math.tzcnt(inMask); i < 64; inMask ^= 1ul << i, i = math.tzcnt(inMask))
                {
                    bool isIn               = IsSkeletonVisible(rootRefs[i + 64].root);
                    cameraMask.upper.Value &= ~(math.select(1ul, 0ul, isIn) << i);
                }
            }

            bool IsSkeletonVisible(Entity root)
            {
                if (root == Entity.Null || !esiLookup.Exists(root))
                    return false;

                var info         = esiLookup[root];
                var skeletonMask = info.Chunk.GetChunkComponentData(ref chunkSkeletonMaskHandle);
                if (info.IndexInChunk >= 64)
                    return skeletonMask.upper.IsSet(info.IndexInChunk - 64);
                else
                    return skeletonMask.lower.IsSet(info.IndexInChunk);
            }
        }

        [BurstCompile]
        unsafe struct MultiSplitCullingJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<ArchetypeChunk> chunksToProcess;

            [ReadOnly] public ComponentTypeHandle<SkeletonDependent>                       dependentHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkPerCameraSkeletonCullingMask>       chunkSkeletonMaskHandle;
            [ReadOnly] public ComponentTypeHandle<ChunkPerCameraSkeletonCullingSplitsMask> chunkSkeletonSplitsMaskHandle;
            [ReadOnly] public EntityStorageInfoLookup                                      esiLookup;

            public ComponentTypeHandle<ChunkPerCameraCullingMask>       chunkPerCameraMaskHandle;
            public ComponentTypeHandle<ChunkPerCameraCullingSplitsMask> chunkPerCameraSplitsMaskHandle;

            public void Execute(int i)
            {
                Execute(chunksToProcess[i]);
            }

            void Execute(in ArchetypeChunk chunk)
            {
                ref var cameraMask       = ref chunk.GetChunkComponentRefRW(ref chunkPerCameraMaskHandle);
                ref var cameraSplitsMask = ref chunk.GetChunkComponentRefRW(ref chunkPerCameraSplitsMaskHandle);
                cameraSplitsMask         = default;

                var rootRefs = chunk.GetNativeArray(ref dependentHandle);
                if (!chunk.Has(ref dependentHandle))
                {
                    cameraMask = default;
                    return;
                }

                var inMask = cameraMask.lower.Value;
                for (int i = math.tzcnt(inMask); i < 64; inMask ^= 1ul << i, i = math.tzcnt(inMask))
                {
                    bool isIn                       = IsSkeletonVisible(rootRefs[i].root, out var splits);
                    cameraMask.lower.Value         &= ~(math.select(1ul, 0ul, isIn) << i);
                    cameraSplitsMask.splitMasks[i]  = splits;
                }
                inMask = cameraMask.upper.Value;
                for (int i = math.tzcnt(inMask); i < 64; inMask ^= 1ul << i, i = math.tzcnt(inMask))
                {
                    bool isIn                            = IsSkeletonVisible(rootRefs[i + 64].root, out var splits);
                    cameraMask.upper.Value              &= ~(math.select(1ul, 0ul, isIn) << i);
                    cameraSplitsMask.splitMasks[i + 64]  = splits;
                }
            }

            bool IsSkeletonVisible(Entity root, out byte splits)
            {
                splits = default;
                if (root == Entity.Null || !esiLookup.Exists(root))
                    return false;

                var  info         = esiLookup[root];
                var  skeletonMask = info.Chunk.GetChunkComponentData(ref chunkSkeletonMaskHandle);
                bool result;
                if (info.IndexInChunk >= 64)
                    result = skeletonMask.upper.IsSet(info.IndexInChunk - 64);
                else
                    result = skeletonMask.lower.IsSet(info.IndexInChunk);

                if (result)
                {
                    var referenceSplits = info.Chunk.GetChunkComponentRefRO(ref chunkSkeletonSplitsMaskHandle);
                    splits              = referenceSplits.ValueRO.splitMasks[info.IndexInChunk];
                }
                return result;
            }
        }
    }
}

