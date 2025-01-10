using static Unity.Entities.SystemAPI;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Kinemation
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct CopyPerCameraMasksToPerDispatchMasksSystem : ISystem
    {
        EntityQuery m_renderMetaQuery;
        EntityQuery m_skeletonQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            m_renderMetaQuery = state.Fluent().With<ChunkPerCameraCullingMask, ChunkHeader>(true).With<ChunkPerDispatchCullingMask>(false).Build();
            m_skeletonQuery   = state.Fluent().With<RenderVisibilityFeedbackFlag>(false).With<ChunkPerCameraSkeletonCullingMask>(true, true).Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new RenderJob
            {
                perCameraHandle   = GetComponentTypeHandle<ChunkPerCameraCullingMask>(true),
                perDispatchHandle = GetComponentTypeHandle<ChunkPerDispatchCullingMask>(false)
            }.ScheduleParallel(m_renderMetaQuery, state.Dependency);

            if (!m_skeletonQuery.IsEmptyIgnoreFilter)
            {
                state.Dependency = new SkeletonJob
                {
                    perCameraHandle   = GetComponentTypeHandle<ChunkPerCameraSkeletonCullingMask>(true),
                    perDispatchHandle = GetComponentTypeHandle<RenderVisibilityFeedbackFlag>(false)
                }.ScheduleParallel(m_skeletonQuery, state.Dependency);
            }
        }

        [BurstCompile]
        struct RenderJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<ChunkPerCameraCullingMask> perCameraHandle;
            public ComponentTypeHandle<ChunkPerDispatchCullingMask>          perDispatchHandle;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var perCamera   = chunk.GetNativeArray(ref perCameraHandle).Reinterpret<ChunkPerDispatchCullingMask>();
                var perDispatch = chunk.GetNativeArray(ref perDispatchHandle);
                perDispatch.CopyFrom(perCamera);
            }
        }

        [BurstCompile]
        struct SkeletonJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<ChunkPerCameraSkeletonCullingMask> perCameraHandle;
            public ComponentTypeHandle<RenderVisibilityFeedbackFlag>                 perDispatchHandle;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var perCamera   = chunk.GetChunkComponentData(ref perCameraHandle);
                var perDispatch = chunk.GetEnabledMask(ref perDispatchHandle);
                for (int i = 0; i < math.min(chunk.Count, 64); i++)
                    perDispatch[i] |= perCamera.lower.IsSet(i);
                for (int i = 0; i < chunk.Count - 64; i++)
                    perDispatch[i + 64] |= perCamera.upper.IsSet(i);
            }
        }
    }
}

