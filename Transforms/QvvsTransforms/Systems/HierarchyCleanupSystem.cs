#if !LATIOS_TRANSFORMS_UNITY
using Latios.Systems;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Transforms.Systems
{
    // Todo: It really doesn't matter when this system runs, as long as it runs periodically.
    // Is there a more optimal opportunity to run it?
    [UpdateInGroup(typeof(PostSyncPointGroup))]
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct HierarchyCleanupSystem : ISystem, ILatiosApi
    {
        EntityQuery m_query;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            this.OnCreateForLatios(ref state);
            m_query = state.Fluent().With<EntityInHierarchyCleanup>(true).Without<EntityInHierarchy>().Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var api          = this.GetApi(ref state);
            state.Dependency = new Job
            {
                ecb = api.syncPoint.CreateEntityCommandBuffer().AsParallelWriter()
            }.Inject(api).ScheduleParallel(m_query, state.Dependency);
        }

        [BurstCompile]
        partial struct Job : IJobChunk, IInjectable
        {
            [ReadOnly, Inject] EntityTypeHandle                           entityHandle;
            [ReadOnly, Inject] BufferTypeHandle<EntityInHierarchyCleanup> cleanupHandle;
            [ReadOnly, Inject] EntityStorageInfoLookup                    esil;

            public EntityCommandBuffer.ParallelWriter ecb;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var roots   = chunk.GetEntityDataPtrRO(entityHandle);
                var buffers = chunk.GetBufferAccessor(ref cleanupHandle);
                for (int i = 0; i < chunk.Count; i++)
                {
                    var  buffer = buffers[i].AsNativeArray();
                    bool fail   = false;
                    for (int j = 1; j < buffer.Length; j++)
                    {
                        if (esil.IsAlive(buffer[j].entityInHierarchy.entity))
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

