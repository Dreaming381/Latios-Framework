#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

using static Unity.Entities.SystemAPI;

// Despite best efforts, it is really easy for WorldTransform of children
// to get out of sync. This system corrects for that.

namespace Latios.Transforms.Authoring.Systems
{
    [DisableAutoCreation]
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    [UpdateInGroup(typeof(TransformBakingSystemGroup))]
    [UpdateAfter(typeof(TransformBakingSystem))]
    [BurstCompile]
    public partial struct TransformHierarchySyncBakingSystem : ISystem
    {
        EntityQuery m_childQuery;
        EntityQuery m_parentQuery;

        public void OnCreate(ref SystemState state)
        {
            m_childQuery  = state.Fluent().With<Parent>().With<WorldTransform>().IncludeDisabledEntities().IncludePrefabs().Build();
            m_parentQuery = state.Fluent().With<WorldTransform>().Without<Parent>().IncludeDisabledEntities().IncludePrefabs().Build();

            state.RequireForUpdate(m_childQuery);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            int count = m_childQuery.CalculateEntityCountWithoutFiltering();
            var map   = new NativeParallelMultiHashMap<Entity, Entity>(count * 2, Allocator.TempJob);

            state.Dependency = new FindChildrenJob
            {
                entityHandle        = GetEntityTypeHandle(),
                parentHandle        = GetComponentTypeHandle<Parent>(true),
                parentToChildrenMap = map.AsParallelWriter()
            }.ScheduleParallel(m_childQuery, state.Dependency);

            state.Dependency = new UpdateJob
            {
                entityHandle              = GetEntityTypeHandle(),
                worldTransformHandle      = GetComponentTypeHandle<WorldTransform>(true),
                localTransformLookup      = GetComponentLookup<LocalTransform>(false),
                parentLookup              = GetComponentLookup<Parent>(true),
                parentToChildrenMap       = map,
                worldTransformLookup      = GetComponentLookup<WorldTransform>(false),
                hierarchyUpdateModeLookup = GetComponentLookup<HierarchyUpdateMode>(true),
                lastSystemVersion         = state.LastSystemVersion
            }.ScheduleParallel(m_parentQuery, state.Dependency);

            state.Dependency = map.Dispose(state.Dependency);
        }

        [BurstCompile]
        struct FindChildrenJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle            entityHandle;
            [ReadOnly] public ComponentTypeHandle<Parent> parentHandle;

            public NativeParallelMultiHashMap<Entity, Entity>.ParallelWriter parentToChildrenMap;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var children = chunk.GetNativeArray(entityHandle);
                var parents  = chunk.GetNativeArray(ref parentHandle).Reinterpret<Entity>();

                for (int i = 0; i < chunk.Count; i++)
                {
                    parentToChildrenMap.Add(parents[i], children[i]);
                }
            }
        }

        [BurstCompile]
        struct UpdateJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle                           entityHandle;
            [ReadOnly] public ComponentTypeHandle<WorldTransform>        worldTransformHandle;
            [ReadOnly] public ComponentLookup<Parent>                    parentLookup;
            [ReadOnly] public ComponentLookup<HierarchyUpdateMode>       hierarchyUpdateModeLookup;
            [ReadOnly] public NativeParallelMultiHashMap<Entity, Entity> parentToChildrenMap;

            [NativeDisableParallelForRestriction] public ComponentLookup<LocalTransform>     localTransformLookup;
            [NativeDisableContainerSafetyRestriction] public ComponentLookup<WorldTransform> worldTransformLookup;

            public uint lastSystemVersion;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var parents                = chunk.GetNativeArray(entityHandle);
                var worldTransformArrayPtr = (TransformQvvs*)chunk.GetRequiredComponentDataPtrRO(ref worldTransformHandle);

                bool worldTransformsDirty = chunk.DidChange(ref worldTransformHandle, lastSystemVersion);

                bool worldTransformValid = true;

                for (int i = 0; i < chunk.Count; i++)
                {
                    foreach (var child in parentToChildrenMap.GetValuesForKey(parents[i]))
                    {
                        // We can safely pass in default for the parent argument since parentWorldTransformValid is forced true.
                        // The child doesn't need to lazy load the parentWorldTransform.
                        UpdateChildRecurse(ref worldTransformArrayPtr[i], ref worldTransformValid, default, child, worldTransformsDirty);
                    }
                }
            }

            void UpdateChildRecurse(ref TransformQvvs parentWorldTransform,
                                    ref bool parentWorldTransformValid,
                                    Entity parent,
                                    Entity entity,
                                    bool parentTransformDirty)
            {
                bool needsUpdate              = parentTransformDirty;
                bool hasMutableLocalTransform = localTransformLookup.HasComponent(entity);
                if (!parentTransformDirty && hasMutableLocalTransform)
                {
                    needsUpdate  = localTransformLookup.DidChange(entity, lastSystemVersion);
                    needsUpdate |= parentTransformDirty;
                    needsUpdate |= parentLookup.DidChange(entity, lastSystemVersion);

                    // This shouldn't matter because if there was no change, then the old and new world transforms are the same.
                    //needsUpdate |= hierarchyUpdateModeLookup.DidChange(entity, lastSystemVersion);
                }

                TransformQvvs worldTransformToPropagate = default;

                if (needsUpdate)
                {
                    if (!parentWorldTransformValid)
                    {
                        parentWorldTransform      = worldTransformLookup[parent].worldTransform;
                        parentWorldTransformValid = true;
                    }

                    if (hasMutableLocalTransform)
                    {
                        ref var worldTransform = ref worldTransformLookup.GetRefRW(entity).ValueRW;
                        if (hierarchyUpdateModeLookup.TryGetComponent(entity, out var flags))
                        {
                            HierarchyInternalUtilities.UpdateTransform(ref worldTransform.worldTransform,
                                                                       ref localTransformLookup.GetRefRW(entity).ValueRW.localTransform,
                                                                       in parentWorldTransform,
                                                                       flags.modeFlags);
                        }
                        else
                        {
                            qvvs.mul(ref worldTransform.worldTransform, in parentWorldTransform, in localTransformLookup.GetRefRO(entity).ValueRO.localTransform);
                        }
                        worldTransformToPropagate = worldTransform.worldTransform;
                    }
                    else
                    {
                        worldTransformLookup[entity] = new WorldTransform { worldTransform = parentWorldTransform };
                        worldTransformToPropagate                                          = parentWorldTransform;
                    }
                }
                // If we had a WriteGroup, we would apply it here.

                bool worldTransformIsValid = needsUpdate;
                foreach (var child in parentToChildrenMap.GetValuesForKey(entity))
                    UpdateChildRecurse(ref worldTransformToPropagate, ref worldTransformIsValid, entity, child, needsUpdate);
            }
        }
    }
}
#endif

