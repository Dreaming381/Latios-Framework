#if !LATIOS_TRANSFORMS_UNITY
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

using Latios.Systems;
using static Unity.Entities.SystemAPI;

namespace Latios.Transforms.Systems
{
    // Todo: It really doesn't matter when this system runs, as long as it runs periodically.
    // Is there a more optimal opportunity to run it?
    [UpdateInGroup(typeof(PostSyncPointGroup))]
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct HierarchyCleanupSystem : ISystem
    {
        LatiosWorldUnmanaged latiosWorld;
        EntityQuery          m_query;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();
            m_query     = state.Fluent().With<EntityInHierarchyCleanup>(true).Without<EntityInHierarchy>().Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new Job
            {
                entityHandle  = GetEntityTypeHandle(),
                cleanupHandle = GetBufferTypeHandle<EntityInHierarchyCleanup>(true),
                esil          = GetEntityStorageInfoLookup(),
                ecb           = latiosWorld.syncPoint.CreateEntityCommandBuffer().AsParallelWriter()
            }.ScheduleParallel(m_query, state.Dependency);
        }

        [BurstCompile]
        struct Job : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle                           entityHandle;
            [ReadOnly] public BufferTypeHandle<EntityInHierarchyCleanup> cleanupHandle;
            [ReadOnly] public EntityStorageInfoLookup                    esil;

            public EntityCommandBuffer.ParallelWriter ecb;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var roots   = chunk.GetEntityDataPtrRO(entityHandle);
                var buffers = chunk.GetBufferAccessor(ref cleanupHandle);
                for (int i = 0; i < chunk.Count; i++)
                {
                    var  buffer = buffers[i].AsNativeArray();
                    bool fail   = false;
                    foreach (var element in buffer)
                    {
                        if (esil.Exists(element.entityInHierarchy.entity))
                        {
                            fail = true;
                            break;
                        }
                    }
                    if (!fail)
                        ecb.RemoveComponent<EntityInHierarchyCleanup>(unfilteredChunkIndex, roots[i]);
                }
            }
        }
    }
}
#endif

