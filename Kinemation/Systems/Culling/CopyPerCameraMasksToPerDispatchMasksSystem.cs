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

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            m_renderMetaQuery = state.Fluent().With<ChunkPerCameraCullingMask, ChunkHeader>(true).With<ChunkPerDispatchCullingMask>(false).Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new RenderJob
            {
                perCameraHandle   = GetComponentTypeHandle<ChunkPerCameraCullingMask>(true),
                perDispatchHandle = GetComponentTypeHandle<ChunkPerDispatchCullingMask>(false)
            }.ScheduleParallel(m_renderMetaQuery, state.Dependency);
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
    }
}

