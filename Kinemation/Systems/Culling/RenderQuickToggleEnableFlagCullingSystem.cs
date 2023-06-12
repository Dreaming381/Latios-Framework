using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Jobs;
using Unity.Mathematics;

using static Unity.Entities.SystemAPI;

namespace Latios.Kinemation
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct RenderQuickToggleEnableFlagCullingSystem : ISystem
    {
        EntityQuery                m_query;
        DynamicComponentTypeHandle m_flagHandle;

        public void OnCreate(ref SystemState state)
        {
            m_query      = state.Fluent().WithAll<RenderQuickToggleEnableFlag>(true).WithAll<ChunkPerCameraCullingMask>(false, true).IgnoreEnableableBits().Build();
            m_flagHandle = state.GetDynamicComponentTypeHandle(ComponentType.ReadOnly<RenderQuickToggleEnableFlag>());
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            m_flagHandle.Update(ref state);
            state.Dependency = new Job
            {
                maskHandle = GetComponentTypeHandle<ChunkPerCameraCullingMask>(false),
                flagHandle = m_flagHandle
            }.ScheduleParallel(m_query, state.Dependency);
        }

        [BurstCompile]
        struct Job : IJobChunk
        {
            // Dynamic handle has a better API for this use case for some reason
            [ReadOnly] public DynamicComponentTypeHandle          flagHandle;
            public ComponentTypeHandle<ChunkPerCameraCullingMask> maskHandle;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var     v         = chunk.GetEnableableBits(ref flagHandle);
                var     lower     = v.ULong0;
                var     upper     = v.ULong1;
                ref var mask      = ref chunk.GetChunkComponentRefRW(ref maskHandle);
                mask.lower.Value &= lower;
                mask.upper.Value &= upper;
            }
        }
    }
}

