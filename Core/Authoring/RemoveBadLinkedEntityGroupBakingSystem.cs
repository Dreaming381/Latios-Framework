using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace Latios.Authoring.Systems
{
    [UpdateInGroup(typeof(PostBakingSystemGroup))]
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [BurstCompile]
    public partial struct RemoveBadLinkedEntityGroupBakingSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            NativeList<Entity> entities = new NativeList<Entity>(128, Allocator.Temp);
            foreach ((var leg, var entity) in SystemAPI.Query<DynamicBuffer<LinkedEntityGroup> >().WithEntityAccess()
                     .WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities))
            {
                if (leg.Length == 1)
                    entities.Add(entity);
            }

            state.EntityManager.RemoveComponent<LinkedEntityGroup>(entities.AsArray());
        }
    }
}

