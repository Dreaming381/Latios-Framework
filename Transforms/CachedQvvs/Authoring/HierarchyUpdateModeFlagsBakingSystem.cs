#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

using static Unity.Entities.SystemAPI;

namespace Latios.Transforms.Authoring
{
    [BakingType]
    internal struct HierarchyUpdateModeRequestFlags : IComponentData
    {
        public Entity                    entity;
        public HierarchyUpdateMode.Flags flags;
    }

    public static class HierarchyUpdateModeBakerExtensions
    {
        /// <summary>
        /// Adds HierarchyUpdateMode.Flags to the entity, ensuring the entity has such a component after baking.
        /// Flags are logically ORed by all invocations targeting the same entity.
        /// </summary>
        /// <param name="entity">The target entity, which does not have to be from this baker</param>
        /// <param name="flags">The flags to set. Normal results in the component being present but without any flags set.</param>
        public static void AddHiearchyModeFlags(this IBaker baker, Entity entity, HierarchyUpdateMode.Flags flags)
        {
            var bakingEntity = baker.CreateAdditionalEntity(TransformUsageFlags.None, true);
            baker.AddComponent(bakingEntity, new HierarchyUpdateModeRequestFlags
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
    public partial struct HierarchyUpdateModeFlagsBakingSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            int flagCount = QueryBuilder().WithAll<HierarchyUpdateModeRequestFlags>().WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities)
                            .Build().CalculateEntityCountWithoutFiltering();

            var flagMap = new NativeParallelMultiHashMap<Entity, HierarchyUpdateMode.Flags>(flagCount, Allocator.TempJob);
            var ecbA    = new EntityCommandBuffer(Allocator.TempJob);
            var ecbB    = new EntityCommandBuffer(Allocator.TempJob);

            new JobA
            {
                modeLookup = GetComponentLookup<HierarchyUpdateMode>(true),
                flagMap    = flagMap.AsParallelWriter(),
                ecbA       = ecbA.AsParallelWriter(),
            }.ScheduleParallel();

            state.CompleteDependency();
            ecbA.Playback(state.EntityManager);
            ecbA.Dispose();

            new JobB
            {
                flagMap = flagMap,
                ecbB    = ecbB.AsParallelWriter(),
            }.ScheduleParallel();
            state.CompleteDependency();
            ecbB.Playback(state.EntityManager);
            ecbB.Dispose();
            flagMap.Dispose();
        }

        [WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities)]
        [BurstCompile]
        partial struct JobA : IJobEntity
        {
            [ReadOnly] public ComponentLookup<HierarchyUpdateMode>                              modeLookup;
            public NativeParallelMultiHashMap<Entity, HierarchyUpdateMode.Flags>.ParallelWriter flagMap;
            public EntityCommandBuffer.ParallelWriter                                           ecbA;

            public void Execute([ChunkIndexInQuery] int chunkIndexInQuery, in HierarchyUpdateModeRequestFlags request)
            {
                if (!modeLookup.HasComponent(request.entity))
                    ecbA.AddComponent<HierarchyUpdateMode>(chunkIndexInQuery, request.entity);
                flagMap.Add(request.entity, request.flags);
            }
        }

        [WithOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities)]
        [BurstCompile]
        partial struct JobB : IJobEntity
        {
            [ReadOnly] public NativeParallelMultiHashMap<Entity, HierarchyUpdateMode.Flags> flagMap;
            public EntityCommandBuffer.ParallelWriter                                       ecbB;

            public void Execute(Entity entity, [ChunkIndexInQuery] int chunkIndexInQuery, ref HierarchyUpdateMode mode)
            {
                mode.modeFlags = HierarchyUpdateMode.Flags.Normal;
                if (flagMap.TryGetFirstValue(entity, out var request, out var it))
                {
                    mode.modeFlags |= request;
                    while (flagMap.TryGetNextValue(out request, ref it))
                        mode.modeFlags |= request;
                }
                else
                {
                    ecbB.RemoveComponent<HierarchyUpdateMode>(chunkIndexInQuery, entity);
                }
            }
        }
    }
}
#endif

