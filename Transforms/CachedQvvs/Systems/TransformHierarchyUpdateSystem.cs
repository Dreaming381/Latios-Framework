#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

using static Unity.Entities.SystemAPI;

namespace Latios.Transforms.Systems
{
    [DisableAutoCreation]
    [RequireMatchingQueriesForUpdate]
    [BurstCompile]
    public partial struct TransformHierarchyUpdateSystem : ISystem
    {
        EntityQuery m_query;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            m_query = state.Fluent().With<WorldTransform>(true).With<Child>(true).Without<PreviousParent>().Build();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var job = new Job
            {
                worldTransformHandle      = GetComponentTypeHandle<WorldTransform>(true),
                childHandle               = GetBufferTypeHandle<Child>(true),
                childLookup               = GetBufferLookup<Child>(true),
                localTransformLookup      = GetComponentLookup<LocalTransform>(false),
                parentLookup              = GetComponentLookup<PreviousParent>(true),
                parentToWorldLookup       = GetComponentLookup<ParentToWorldTransform>(false),
                worldTransformLookup      = GetComponentLookup<WorldTransform>(false),
                hierarchyUpdateModeLookup = GetComponentLookup<HierarchyUpdateMode>(true),
                lastSystemVersion         = state.LastSystemVersion
            };
            state.Dependency = job.ScheduleParallelByRef(m_query, state.Dependency);
        }

        [BurstCompile]
        struct Job : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<WorldTransform>  worldTransformHandle;
            [ReadOnly] public BufferTypeHandle<Child>              childHandle;
            [ReadOnly] public BufferLookup<Child>                  childLookup;
            [ReadOnly] public ComponentLookup<PreviousParent>      parentLookup;
            [ReadOnly] public ComponentLookup<HierarchyUpdateMode> hierarchyUpdateModeLookup;

            [NativeDisableParallelForRestriction] public ComponentLookup<LocalTransform>         localTransformLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<ParentToWorldTransform> parentToWorldLookup;
            [NativeDisableContainerSafetyRestriction] public ComponentLookup<WorldTransform>     worldTransformLookup;

            public uint lastSystemVersion;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var worldTransformArrayPtr = (TransformQvvs*)chunk.GetRequiredComponentDataPtrRO(ref worldTransformHandle);
                var childAccessor          = chunk.GetBufferAccessor(ref childHandle);

                bool worldTransformsDirty = chunk.DidChange(ref worldTransformHandle, lastSystemVersion);
                bool childBufferDirty     = chunk.DidChange(ref childHandle, lastSystemVersion);

                bool worldTransformValid = true;

                for (int i = 0; i < chunk.Count; i++)
                {
                    foreach (var child in childAccessor[i])
                    {
                        // We can safely pass in default for the parent argument since parentWorldTransformValid is forced true.
                        // The child doesn't need to lazy load the parentWorldTransform.
                        UpdateChildRecurse(ref worldTransformArrayPtr[i], ref worldTransformValid, default, child.child, worldTransformsDirty, childBufferDirty);
                    }
                }
            }

            void UpdateChildRecurse(ref TransformQvvs parentWorldTransform,
                                    ref bool parentWorldTransformValid,
                                    Entity parent,
                                    Entity entity,
                                    bool parentTransformDirty,
                                    bool childBufferDirty)
            {
                bool needsUpdate              = parentTransformDirty;
                bool hasMutableLocalTransform = localTransformLookup.HasComponent(entity);
                if (!parentTransformDirty && hasMutableLocalTransform)
                {
                    needsUpdate  = localTransformLookup.DidChange(entity, lastSystemVersion);
                    needsUpdate |= parentTransformDirty;
                    needsUpdate |= parentLookup.DidChange(entity, lastSystemVersion) && childBufferDirty;
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
                        parentToWorldLookup[entity] = new ParentToWorldTransform { parentToWorldTransform = parentWorldTransform };
                        ref var worldTransform                                                            = ref worldTransformLookup.GetRefRW(entity).ValueRW;
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

                if (childLookup.HasBuffer(entity))
                {
                    bool childBufferChanged    = childLookup.DidChange(entity, lastSystemVersion);
                    bool worldTransformIsValid = needsUpdate;
                    foreach (var child in childLookup[entity])
                        UpdateChildRecurse(ref worldTransformToPropagate, ref worldTransformIsValid, entity, child.child, needsUpdate, childBufferChanged);
                }
            }
        }
    }
}
#endif

