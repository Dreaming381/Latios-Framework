using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

// This is a straight-up copy and paste of the Unity system except with different attributes.
// This is easier than trying to move the system to the correct location during the bootstrap.

namespace Latios.Transforms.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    partial class CompanionGameObjectUpdateSystem : SystemBase
    {
        private EntityQuery toActivate;
        private EntityQuery toDeactivate;
        private EntityQuery toCleanup;

        protected override void OnCreate()
        {
            base.OnCreate();
            toActivate = new EntityQueryBuilder(Allocator.Temp)
                         .WithAll<CompanionLink>()
                         .WithNone<CompanionGameObjectActiveCleanup, Disabled>()
                         .Build(this);
            toDeactivate = new EntityQueryBuilder(Allocator.Temp)
                           .WithAny<Disabled, Prefab>()
                           .WithAll<CompanionGameObjectActiveCleanup, CompanionLink>()
                           .WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab)
                           .Build(this);
            toCleanup = new EntityQueryBuilder(Allocator.Temp)
                        .WithAll<CompanionGameObjectActiveCleanup>()
                        .WithNone<CompanionLink>()
                        .Build(this);
        }

        protected override void OnUpdate()
        {
            Entities
            .WithNone<CompanionGameObjectActiveCleanup, Disabled>()
            .WithAll<CompanionLink>()
            .ForEach((CompanionLink link) => link.Companion.SetActive(true)).WithoutBurst().Run();
            EntityManager.AddComponent<CompanionGameObjectActiveCleanup>(toActivate);

            Entities
            .WithAny<Disabled, Prefab>()
            .WithAll<CompanionGameObjectActiveCleanup, CompanionLink>()
            .WithEntityQueryOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab)
            .ForEach((CompanionLink link) => link.Companion.SetActive(false)).WithoutBurst().Run();
            EntityManager.RemoveComponent<CompanionGameObjectActiveCleanup>(toDeactivate);

            EntityManager.RemoveComponent<CompanionGameObjectActiveCleanup>(toCleanup);
        }
    }
}

