using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;

namespace Latios.Kinemation.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct ClearPerFrameCullingMasksSystem : ISystem
    {
        EntityQuery m_metaQuery;

        ComponentTypeHandle<ChunkPerFrameCullingMask> m_handle;

        public void OnCreate(ref SystemState state)
        {
            m_metaQuery = state.Fluent().With<ChunkPerFrameCullingMask>(false).With<ChunkPerCameraCullingMask>(false).With<ChunkHeader>(true).Build();
            m_handle    = state.GetComponentTypeHandle<ChunkPerFrameCullingMask>(false);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            m_handle.Update(ref state);
            state.Dependency = new ClearJob
            {
                handle            = m_handle,
                lastSystemVersion = state.LastSystemVersion
            }.ScheduleParallel(m_metaQuery, state.Dependency);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        struct ClearJob : IJobChunk
        {
            public ComponentTypeHandle<ChunkPerFrameCullingMask> handle;
            public uint                                          lastSystemVersion;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                if (chunk.DidChange(ref handle, lastSystemVersion))
                {
                    var ptr = chunk.GetComponentDataPtrRW(ref handle);
                    UnsafeUtility.MemClear(ptr, sizeof(ChunkPerFrameCullingMask) * chunk.Count);
                }
            }
        }
    }
}

