#if !LATIOS_TRANSFORMS_UNITY
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

using static Unity.Entities.SystemAPI;

namespace Latios.Transforms.Authoring
{
    [BakingType]
    internal struct RequestedInheritanceFlags : IComponentData
    {
        public Entity           entity;
        public InheritanceFlags flags;
    }

    [BakingType]
    internal struct MergedInheritanceFlags : IComponentData
    {
        public InheritanceFlags flags;
    }

    public static class InheritanceFlagsBakerExtensions
    {
        /// <summary>
        /// Adds InheritanceFlags to the entity within its hierarchy.
        /// Flags are logically ORed by all invocations targeting the same entity.
        /// Flags are dropped if the entity is a root.
        /// </summary>
        /// <param name="entity">The target entity, which does not have to be from this baker</param>
        /// <param name="flags">The flags to set.</param>
        public static void AddInheritanceFlags(this IBaker baker, Entity entity, InheritanceFlags flags)
        {
            var bakingEntity = baker.CreateAdditionalEntity(TransformUsageFlags.None, true);
            baker.AddComponent(bakingEntity, new RequestedInheritanceFlags
            {
                entity = entity,
                flags  = flags
            });
        }
    }
}

namespace Latios.Transforms.Authoring.Systems
{
    [DisableAutoCreation]
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    [UpdateInGroup(typeof(TransformBakingSystemGroup))]
    [UpdateBefore(typeof(TransformBakingSystem))]
    [UpdateAfter(typeof(UserPreTransformsBakingSystemGroup))]
    [RequireMatchingQueriesForUpdate]
    [BurstCompile]
    public partial struct InheritanceFlagsBakingSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            int flagCount = QueryBuilder().WithAll<RequestedInheritanceFlags>().WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities)
                            .Build().CalculateEntityCountWithoutFiltering();

            var flagMap = new NativeParallelMultiHashMap<Entity, InheritanceFlags>(flagCount, state.WorldUpdateAllocator);
            var ecbA    = new EntityCommandBuffer(state.WorldUpdateAllocator);
            var ecbB    = new EntityCommandBuffer(state.WorldUpdateAllocator);

            new JobA
            {
                mergedLookup = GetComponentLookup<MergedInheritanceFlags>(true),
                flagMap      = flagMap.AsParallelWriter(),
                ecbA         = ecbA.AsParallelWriter(),
            }.ScheduleParallel();

            state.CompleteDependency();
            ecbA.Playback(state.EntityManager);

            new JobB
            {
                flagMap = flagMap,
                ecbB    = ecbB.AsParallelWriter(),
            }.ScheduleParallel();
            state.CompleteDependency();
            ecbB.Playback(state.EntityManager);
        }

        [WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities)]
        [BurstCompile]
        partial struct JobA : IJobEntity
        {
            [ReadOnly] public ComponentLookup<MergedInheritanceFlags>                  mergedLookup;
            public NativeParallelMultiHashMap<Entity, InheritanceFlags>.ParallelWriter flagMap;
            public EntityCommandBuffer.ParallelWriter                                  ecbA;

            public void Execute([ChunkIndexInQuery] int chunkIndexInQuery, in RequestedInheritanceFlags request)
            {
                if (!mergedLookup.HasComponent(request.entity))
                    ecbA.AddComponent<MergedInheritanceFlags>(chunkIndexInQuery, request.entity);
                flagMap.Add(request.entity, request.flags);
            }
        }

        [WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities)]
        [BurstCompile]
        partial struct JobB : IJobEntity
        {
            [ReadOnly] public NativeParallelMultiHashMap<Entity, InheritanceFlags> flagMap;
            public EntityCommandBuffer.ParallelWriter                              ecbB;

            public void Execute(Entity entity, [ChunkIndexInQuery] int chunkIndexInQuery, ref MergedInheritanceFlags mode)
            {
                mode.flags = InheritanceFlags.Normal;
                if (flagMap.TryGetFirstValue(entity, out var request, out var it))
                {
                    mode.flags |= request;
                    while (flagMap.TryGetNextValue(out request, ref it))
                        mode.flags |= request;
                }
                else
                {
                    ecbB.RemoveComponent<MergedInheritanceFlags>(chunkIndexInQuery, entity);
                }
            }
        }
    }
}
#endif

