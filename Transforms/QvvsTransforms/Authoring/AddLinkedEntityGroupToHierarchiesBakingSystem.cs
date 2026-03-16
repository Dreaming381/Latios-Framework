#if !LATIOS_TRANSFORMS_UNITY
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

using static Unity.Entities.SystemAPI;

namespace Latios.Transforms.Authoring.Systems
{
    [UpdateInGroup(typeof(PostBakingSystemGroup))]
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct AddLinkedEntityGroupToHierarchiesBakingSystem : ISystem
    {
        EntityQuery m_missingLegQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            m_missingLegQuery = state.Fluent().With<EntityInHierarchy>(true).Without<LinkedEntityGroup>().IncludePrefabs().IncludeDisabledEntities().Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var entities = m_missingLegQuery.ToEntityArray(state.WorldUpdateAllocator);
            state.EntityManager.AddComponent<LinkedEntityGroup>(m_missingLegQuery);

            state.Dependency = new Job
            {
                entities        = entities,
                hierarchyLookup = GetBufferLookup<EntityInHierarchy>(true),
                legLookup       = GetBufferLookup<LinkedEntityGroup>(false)
            }.ScheduleParallel(entities.Length, 32, state.Dependency);
        }

        [BurstCompile]
        struct Job : IJobFor
        {
            [ReadOnly] public NativeArray<Entity>                                        entities;
            [ReadOnly] public BufferLookup<EntityInHierarchy>                            hierarchyLookup;
            [NativeDisableParallelForRestriction] public BufferLookup<LinkedEntityGroup> legLookup;

            public void Execute(int index)
            {
                var entity    = entities[index];
                var hierarchy = hierarchyLookup[entity].AsNativeArray();
                var leg       = legLookup[entity];

                leg.EnsureCapacity(hierarchy.Length);
                foreach (var element in hierarchy)
                {
                    leg.Add(new LinkedEntityGroup { Value = element.entity });
                }
            }
        }
    }
}
#endif

