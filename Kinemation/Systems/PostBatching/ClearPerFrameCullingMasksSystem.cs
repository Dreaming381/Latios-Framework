using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;

using static Unity.Entities.SystemAPI;

namespace Latios.Kinemation.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct ClearPerFrameCullingMasksSystem : ISystem
    {
        EntityQuery m_renderMetaQuery;
        EntityQuery m_skeletonMetaQuery;

        public void OnCreate(ref SystemState state)
        {
            m_renderMetaQuery   = state.Fluent().With<ChunkPerFrameCullingMask>(false).With<ChunkPerCameraCullingMask>(false).With<ChunkHeader>(true).Build();
            m_skeletonMetaQuery = state.Fluent().With<RenderVisibilityFeedbackFlag>(false).With<SkeletonRootTag>(true).Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new ClearRenderJob
            {
                frameHandle       = GetComponentTypeHandle<ChunkPerFrameCullingMask>(),
                dispatchHandle    = GetComponentTypeHandle<ChunkPerDispatchCullingMask>(),
                lastSystemVersion = state.LastSystemVersion
            }.ScheduleParallel(m_renderMetaQuery, state.Dependency);

            if (!m_skeletonMetaQuery.IsEmptyIgnoreFilter)
            {
                state.Dependency = new ClearSkeletonsJob
                {
                    flagHandle = GetComponentTypeHandle<RenderVisibilityFeedbackFlag>()
                }.ScheduleParallel(m_skeletonMetaQuery, state.Dependency);
            }
        }

        [BurstCompile]
        struct ClearRenderJob : IJobChunk
        {
            public ComponentTypeHandle<ChunkPerFrameCullingMask>    frameHandle;
            public ComponentTypeHandle<ChunkPerDispatchCullingMask> dispatchHandle;
            public uint                                             lastSystemVersion;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                if (chunk.DidChange(ref frameHandle, lastSystemVersion))
                {
                    var ptr = chunk.GetComponentDataPtrRW(ref frameHandle);
                    UnsafeUtility.MemClear(ptr, sizeof(ChunkPerFrameCullingMask) * chunk.Count);
                }
                if (chunk.DidChange(ref dispatchHandle, lastSystemVersion))
                {
                    var ptr = chunk.GetComponentDataPtrRW(ref dispatchHandle);
                    UnsafeUtility.MemClear(ptr, sizeof(ChunkPerDispatchCullingMask) * chunk.Count);
                }
            }
        }

        [BurstCompile]
        struct ClearSkeletonsJob : IJobChunk
        {
            public ComponentTypeHandle<RenderVisibilityFeedbackFlag> flagHandle;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                chunk.SetComponentEnabledForAll(ref flagHandle, false);
            }
        }
    }
}

