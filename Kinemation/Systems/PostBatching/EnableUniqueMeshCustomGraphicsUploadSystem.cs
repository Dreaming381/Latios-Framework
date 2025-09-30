using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Burst.Intrinsics;
using Unity.Entities.Exposed;

using static Unity.Entities.SystemAPI;

namespace Latios.Kinemation.Systems
{
    [UpdateInGroup(typeof(KinemationCustomGraphicsSetupSuperSystem))]
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct EnableUniqueMeshCustomGraphicsUploadSystem : ISystem
    {
        EntityQuery m_query;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            m_query = state.Fluent().WithEnabled<UniqueMeshConfig>(true).With<ChunkPerDispatchCullingMask>(false, true).Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new Job
            {
                maskHandle = GetComponentTypeHandle<ChunkPerDispatchCullingMask>(false),
            }.Schedule(m_query, state.Dependency);
        }

        [BurstCompile]
        struct Job : IJobChunk
        {
            public ComponentTypeHandle<ChunkPerDispatchCullingMask> maskHandle;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                BitField64 lower = default;
                lower.SetBits(0, true, chunk.Count);
                BitField64 upper = default;
                if (chunk.Count > 64)
                    upper.SetBits(0, true, chunk.Count - 64);
                ref var mask = ref chunk.GetChunkComponentRefRW(ref maskHandle);
                mask.lower.Value = useEnabledMask ? chunkEnabledMask.ULong0 : lower.Value;
                mask.upper.Value = useEnabledMask ? chunkEnabledMask.ULong1 : upper.Value;
            }
        }
    }
}