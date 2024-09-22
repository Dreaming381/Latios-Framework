using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Jobs;

using static Unity.Entities.SystemAPI;

namespace Latios.Kinemation
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct ApplyDispatchMasksToFrameMasksSystem : ISystem
    {
        EntityQuery m_metaQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            m_metaQuery = state.Fluent().With<ChunkPerDispatchCullingMask, ChunkPerFrameCullingMask>(false).With<ChunkHeader>(true).Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new Job
            {
                frameHandle    = GetComponentTypeHandle<ChunkPerFrameCullingMask>(false),
                dispatchHandle = GetComponentTypeHandle<ChunkPerDispatchCullingMask>(false)
            }.ScheduleParallel(m_metaQuery, state.Dependency);
        }

        [BurstCompile]
        struct Job : IJobChunk
        {
            public ComponentTypeHandle<ChunkPerFrameCullingMask>    frameHandle;
            public ComponentTypeHandle<ChunkPerDispatchCullingMask> dispatchHandle;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var dispatches = chunk.GetComponentDataPtrRW(ref dispatchHandle);
                var frames     = chunk.GetComponentDataPtrRW(ref frameHandle);
                for (int i = 0; i < chunk.Count; i++)
                {
                    frames[i].lower.Value |= dispatches[i].lower.Value;
                    frames[i].upper.Value |= dispatches[i].upper.Value;
                    dispatches[i]          = default;
                }
            }
        }
    }
}

