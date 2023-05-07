#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
using System;
using Latios.Unsafe;
using Unity.Burst;
using Unity.Burst.CompilerServices;
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
    public partial struct ParentChangeSystem : ISystem
    {
        LatiosWorldUnmanaged latiosWorld;

        EntityQuery m_newChildrenIdentityQuery;
        EntityQuery m_newChildrenNotCopyParentQuery;
        EntityQuery m_deadChildrenQuery;
        EntityQuery m_allChildrenQuery;
        EntityQuery m_deadParentsQuery;
        EntityQuery m_copyParentCorrectionQuery;
        EntityQuery m_parentlessCorrectionQuery;
        EntityQuery m_removeAllFromChildTagQuery;

        struct RemoveAllFromChildTag : IComponentData
        { }

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();

            m_newChildrenIdentityQuery      = QueryBuilder().WithAll<Parent, CopyParentWorldTransformTag>().WithNone<PreviousParent>().Build();
            m_newChildrenNotCopyParentQuery = QueryBuilder().WithAll<Parent>().WithNone<PreviousParent, CopyParentWorldTransformTag>().Build();
            m_deadChildrenQuery             = QueryBuilder().WithAll<PreviousParent>().WithNone<Parent>().Build();
            m_allChildrenQuery              = QueryBuilder().WithAny<Parent>().WithAnyRW<PreviousParent>().Build();
            m_deadParentsQuery              = QueryBuilder().WithAll<Child>().WithNone<WorldTransform>().Build();
            m_copyParentCorrectionQuery     = QueryBuilder().WithAll<CopyParentWorldTransformTag>().WithAny<LocalTransform, ParentToWorldTransform>().Build();
            m_parentlessCorrectionQuery     = QueryBuilder().WithNone<Parent, PreviousParent>().WithAny<LocalTransform, ParentToWorldTransform,
                                                                                                        CopyParentWorldTransformTag>().Build();
            m_removeAllFromChildTagQuery = QueryBuilder().WithAll<RemoveAllFromChildTag>().Build();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var childrenWithNullParentsBlocklist = new UnsafeParallelBlockList(UnsafeUtility.SizeOf<Entity>(), 256, state.WorldUpdateAllocator);

            state.Dependency = new FindOrphanedChildrenJob
            {
                entityHandle                     = GetEntityTypeHandle(),
                childHandle                      = GetBufferTypeHandle<Child>(true),
                parentLookup                     = GetComponentLookup<Parent>(true),
                childrenWithNullParentsBlocklist = childrenWithNullParentsBlocklist
            }.ScheduleParallel(m_deadParentsQuery, state.Dependency);

            var newChildrenCount = m_newChildrenIdentityQuery.CalculateEntityCountWithoutFiltering() +
                                   m_newChildrenNotCopyParentQuery.CalculateEntityCountWithoutFiltering();
            var newChildrenList                    = new NativeList<Entity>(newChildrenCount, state.WorldUpdateAllocator);
            var changeOpsBlocklist                 = new UnsafeParallelBlockList(UnsafeUtility.SizeOf<ChangeOperation>(), 256, state.WorldUpdateAllocator);
            var parentsWithoutChildBufferBlocklist = new UnsafeParallelBlockList(UnsafeUtility.SizeOf<Entity>(), 256, state.WorldUpdateAllocator);

            state.Dependency = new FindChildrenWithChangesJob
            {
                entityHandle                       = GetEntityTypeHandle(),
                localTransformHandle               = GetComponentTypeHandle<LocalTransform>(true),
                parentHandle                       = GetComponentTypeHandle<Parent>(true),
                previousParentHandle               = GetComponentTypeHandle<PreviousParent>(false),
                childLookup                        = GetBufferLookup<Child>(true),
                worldTransformLookup               = GetComponentLookup<WorldTransform>(true),
                newChildrenList                    = newChildrenList.AsParallelWriter(),
                changeOpsBlocklist                 = changeOpsBlocklist,
                childrenWithNullParentsBlocklist   = childrenWithNullParentsBlocklist,
                parentsWithoutChildBufferBlocklist = parentsWithoutChildBufferBlocklist,
                lastSystemVersion                  = state.LastSystemVersion
            }.ScheduleParallel(m_allChildrenQuery, state.Dependency);

            var parentsWithoutChildBufferList = new NativeList<Entity>(state.WorldUpdateAllocator);
            var parentsWithoutChildBufferJH   = new SortEntitiesJob
            {
                entityBlocklist = parentsWithoutChildBufferBlocklist,
                sortedEntities  = parentsWithoutChildBufferList
            }.Schedule(state.Dependency);

            var childrenWithNullParentsList = new NativeList<Entity>(state.WorldUpdateAllocator);
            var childrenWithNullParentsJH   = new SortEntitiesJob
            {
                entityBlocklist = childrenWithNullParentsBlocklist,
                sortedEntities  = childrenWithNullParentsList
            }.Schedule(state.Dependency);

            var changeOpsList                = new NativeList<ChangeOperation>(state.WorldUpdateAllocator);
            var parentRanges                 = new NativeList<int2>(state.WorldUpdateAllocator);
            var parentsWithEmptyChildBuffers = new NativeList<Entity>(state.WorldUpdateAllocator);
            var sortChangeOpsJH              = new SortOperationsJob
            {
                operationBlockList           = changeOpsBlocklist,
                sortedOperations             = changeOpsList,
                parentRanges                 = parentRanges,
                parentsWithEmptyChildBuffers = parentsWithEmptyChildBuffers
            }.Schedule(state.Dependency);

            JobHandle.ScheduleBatchedJobs();

            var flags = latiosWorld.worldBlackboardEntity.GetComponentData<RuntimeFeatureFlags>().flags;

            ComponentTypeSet removeDeadChildrenSet;
            ComponentTypeSet addNewChildrenNotCopyParentSet;
            ComponentTypeSet addNewChildrenCopyParentExtremeSet = default;
            ComponentTypeSet removeParentlessCorrectionSet;
            ComponentTypeSet removeCopyParentCorrectionSet;
            ComponentTypeSet removeChildrenWithNullSet;
            if ((flags & RuntimeFeatureFlags.Flags.ExtremeTransforms) != RuntimeFeatureFlags.Flags.None)
            {
                var typeList = new FixedList128Bytes<ComponentType>();
                typeList.Add(ComponentType.ReadWrite<PreviousParent>());
                typeList.Add(ComponentType.ReadWrite<LocalTransform>());
                typeList.Add(ComponentType.ReadWrite<ParentToWorldTransform>());
                typeList.Add(ComponentType.ReadWrite<CopyParentWorldTransformTag>());
                typeList.Add(ComponentType.ReadWrite<Depth>());
                typeList.Add(ComponentType.ChunkComponent<ChunkDepthMask>());
                removeDeadChildrenSet          = new ComponentTypeSet(typeList);
                addNewChildrenNotCopyParentSet = new ComponentTypeSet(ComponentType.ReadWrite<PreviousParent>(),
                                                                      ComponentType.ReadWrite<LocalTransform>(),
                                                                      ComponentType.ReadWrite<ParentToWorldTransform>(),
                                                                      ComponentType.ReadWrite<Depth>(),
                                                                      ComponentType.ChunkComponent<ChunkDepthMask>());
                addNewChildrenCopyParentExtremeSet = new ComponentTypeSet(ComponentType.ReadWrite<PreviousParent>(),
                                                                          ComponentType.ReadWrite<Depth>(),
                                                                          ComponentType.ChunkComponent<ChunkDepthMask>());
                removeParentlessCorrectionSet = new ComponentTypeSet(ComponentType.ReadWrite<LocalTransform>(),
                                                                     ComponentType.ReadWrite<ParentToWorldTransform>(),
                                                                     ComponentType.ReadWrite<CopyParentWorldTransformTag>(),
                                                                     ComponentType.ReadWrite<Depth>(),
                                                                     ComponentType.ChunkComponent<ChunkDepthMask>());
                removeCopyParentCorrectionSet = new ComponentTypeSet(ComponentType.ReadWrite<LocalTransform>(),
                                                                     ComponentType.ReadWrite<ParentToWorldTransform>(),
                                                                     ComponentType.ReadWrite<Depth>(),
                                                                     ComponentType.ChunkComponent<ChunkDepthMask>());
                typeList.Add(ComponentType.ReadWrite<Parent>());
                typeList.Add(ComponentType.ReadOnly<RemoveAllFromChildTag>());
                removeChildrenWithNullSet = new ComponentTypeSet(typeList);
            }
            else
            {
                removeDeadChildrenSet = new ComponentTypeSet(ComponentType.ReadWrite<PreviousParent>(),
                                                             ComponentType.ReadWrite<LocalTransform>(),
                                                             ComponentType.ReadWrite<ParentToWorldTransform>(),
                                                             ComponentType.ReadWrite<CopyParentWorldTransformTag>());
                addNewChildrenNotCopyParentSet = new ComponentTypeSet(ComponentType.ReadWrite<PreviousParent>(),
                                                                      ComponentType.ReadWrite<LocalTransform>(),
                                                                      ComponentType.ReadWrite<ParentToWorldTransform>());
                removeParentlessCorrectionSet = new ComponentTypeSet(ComponentType.ReadWrite<LocalTransform>(),
                                                                     ComponentType.ReadWrite<ParentToWorldTransform>(),
                                                                     ComponentType.ReadWrite<CopyParentWorldTransformTag>());
                removeCopyParentCorrectionSet = new ComponentTypeSet(ComponentType.ReadWrite<LocalTransform>(),
                                                                     ComponentType.ReadWrite<ParentToWorldTransform>());
                removeChildrenWithNullSet = new ComponentTypeSet(ComponentType.ReadWrite<Parent>(),
                                                                 ComponentType.ReadWrite<PreviousParent>(),
                                                                 ComponentType.ReadWrite<LocalTransform>(),
                                                                 ComponentType.ReadWrite<ParentToWorldTransform>(),
                                                                 ComponentType.ReadWrite<CopyParentWorldTransformTag>());
            }

            state.CompleteDependency();

            state.EntityManager.RemoveComponent<Child>(m_deadParentsQuery);
            state.EntityManager.RemoveComponent(       m_deadChildrenQuery, removeDeadChildrenSet);
            state.EntityManager.AddComponent( m_newChildrenNotCopyParentQuery, addNewChildrenNotCopyParentSet);
            if ((flags & RuntimeFeatureFlags.Flags.ExtremeTransforms) != RuntimeFeatureFlags.Flags.None)
                state.EntityManager.AddComponent(m_newChildrenIdentityQuery, addNewChildrenCopyParentExtremeSet);
            else
                state.EntityManager.AddComponent<PreviousParent>(m_newChildrenIdentityQuery);
            state.EntityManager.RemoveComponent(m_parentlessCorrectionQuery, removeParentlessCorrectionSet);
            state.EntityManager.RemoveComponent(m_copyParentCorrectionQuery, removeCopyParentCorrectionSet);

            parentsWithoutChildBufferJH.Complete();
            state.EntityManager.AddComponent<Child>(parentsWithoutChildBufferList.AsArray());

            childrenWithNullParentsJH.Complete();
            if ((flags & RuntimeFeatureFlags.Flags.ExtremeTransforms) != RuntimeFeatureFlags.Flags.None)
            {
                state.EntityManager.AddComponent<RemoveAllFromChildTag>(childrenWithNullParentsList.AsArray());
                state.EntityManager.RemoveComponent(m_removeAllFromChildTagQuery, removeChildrenWithNullSet);
            }
            else
                state.EntityManager.RemoveComponent(childrenWithNullParentsList.AsArray(), removeChildrenWithNullSet);
            sortChangeOpsJH.Complete();
            state.EntityManager.RemoveComponent<Child>(parentsWithEmptyChildBuffers.AsArray());

            state.Dependency = new UpdateChildBuffersJob
            {
                childLookup      = GetBufferLookup<Child>(false),
                parentRanges     = parentRanges.AsArray(),
                sortedOperations = changeOpsList.AsArray(),
            }.ScheduleParallel(parentRanges.Length, 8, default);

            state.Dependency = new UpdateNewPreviousParentsJob
            {
                entities             = newChildrenList.AsArray(),
                localTransformLookup = GetComponentLookup<LocalTransform>(false),
                parentLookup         = GetComponentLookup<Parent>(true),
                previousLookup       = GetComponentLookup<PreviousParent>(false)
            }.ScheduleParallel(newChildrenList.Length, 32, state.Dependency);
        }

        struct ChangeOperation : IComparable<ChangeOperation>
        {
            public Entity parent;
            public Entity child;
            public int    cachedChildCount;  // Only valid for remove operations
            public bool   isAddOperation;  // otherwise, it is remove operation

            public int CompareTo(ChangeOperation other)
            {
                var result = parent.CompareTo(other.parent);
                if (result == 0)
                {
                    result = isAddOperation.CompareTo(other.isAddOperation);
                    if (result == 0)
                    {
                        return child.CompareTo(other.child);
                    }
                }
                return result;
            }
        }

        [BurstCompile]
        struct FindOrphanedChildrenJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle        entityHandle;
            [ReadOnly] public BufferTypeHandle<Child> childHandle;
            [ReadOnly] public ComponentLookup<Parent> parentLookup;

            public UnsafeParallelBlockList childrenWithNullParentsBlocklist;

            [NativeSetThreadIndex]
            int threadIndex;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var entities     = chunk.GetNativeArray(entityHandle);
                var childBuffers = chunk.GetBufferAccessor(ref childHandle);

                for (int i = 0; i < chunk.Count; i++)
                {
                    foreach (var child in childBuffers[i].Reinterpret<Entity>())
                    {
                        if (parentLookup.TryGetComponent(child, out var parent) && parent.parent == entities[i])
                            childrenWithNullParentsBlocklist.Write(child, threadIndex);
                    }
                }
            }
        }

        [BurstCompile]
        struct FindChildrenWithChangesJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle                    entityHandle;
            [ReadOnly] public ComponentTypeHandle<LocalTransform> localTransformHandle;
            [ReadOnly] public ComponentTypeHandle<Parent>         parentHandle;
            public ComponentTypeHandle<PreviousParent>            previousParentHandle;

            [ReadOnly] public BufferLookup<Child>             childLookup;
            [ReadOnly] public ComponentLookup<WorldTransform> worldTransformLookup;

            public NativeList<Entity>.ParallelWriter newChildrenList;
            public UnsafeParallelBlockList           changeOpsBlocklist;
            public UnsafeParallelBlockList           childrenWithNullParentsBlocklist;
            public UnsafeParallelBlockList           parentsWithoutChildBufferBlocklist;

            public uint lastSystemVersion;

            [NativeSetThreadIndex]
            int threadIndex;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                bool hasParent   = chunk.Has(ref parentHandle);
                bool hasPrevious = chunk.Has(ref previousParentHandle);

                if (hasParent && !hasPrevious)
                    DoNewChildren(in chunk);
                else if (!hasParent && hasPrevious)
                    DoDeadChildren(in chunk);
                else if (chunk.DidChange(ref parentHandle, lastSystemVersion))
                    DoChangedChildren(in chunk);
            }

            [SkipLocalsInit]
            unsafe void DoNewChildren(in ArchetypeChunk chunk)
            {
                Entity* newEntityCache = stackalloc Entity[128];
                int     cacheCount     = 0;
                int     runStart       = 0;

                var parents  = chunk.GetNativeArray(ref parentHandle).Reinterpret<Entity>();
                var children = chunk.GetNativeArray(entityHandle);

                for (int i = 0; i < chunk.Count; i++)
                {
                    // If the parent is null or deleted, we want to remove the parent components from the child
                    if (Hint.Unlikely(parents[i] == Entity.Null || !worldTransformLookup.HasComponent(parents[i])))
                    {
                        if (runStart < i)
                        {
                            var runCount = i - runStart;
                            UnsafeUtility.MemCpy(newEntityCache + cacheCount, children.GetSubArray(runStart, runCount).GetUnsafeReadOnlyPtr(), runCount * sizeof(Entity));
                            cacheCount += runCount;
                        }
                        runStart = i + 1;
                        childrenWithNullParentsBlocklist.Write(children[i], threadIndex);
                        continue;
                    }

                    if (!childLookup.HasBuffer(parents[i]))
                    {
                        parentsWithoutChildBufferBlocklist.Write(parents[i], threadIndex);
                    }

                    changeOpsBlocklist.Write(new ChangeOperation
                    {
                        parent         = parents[i],
                        child          = children[i],
                        isAddOperation = true
                    }, threadIndex);
                }

                if (runStart < chunk.Count)
                {
                    var runCount = chunk.Count - runStart;
                    UnsafeUtility.MemCpy(newEntityCache + cacheCount, children.GetSubArray(runStart, runCount).GetUnsafeReadOnlyPtr(), runCount * sizeof(Entity));
                    cacheCount += runCount;
                }

                // If the parent needs a default LocalToParentTransform, we remap the index from the range [0 int.Max] to [-1 int.Min]
                if (!chunk.Has(ref localTransformHandle))
                {
                    for (int i = 0; i < cacheCount; i++)
                    {
                        var entity        = newEntityCache[i];
                        entity.Index      = -entity.Index - 1;
                        newEntityCache[i] = entity;
                    }
                }

                if (cacheCount > 0)
                    newChildrenList.AddRangeNoResize(newEntityCache, cacheCount);
            }

            void DoDeadChildren(in ArchetypeChunk chunk)
            {
                // This updates the version number, but we don't care since we'll be removing the component from these chunks anyways.
                var parents  = chunk.GetNativeArray(ref previousParentHandle).Reinterpret<Entity>();
                var children = chunk.GetNativeArray(entityHandle);

                for (int i = 0; i < chunk.Count; i++)
                {
                    // If the parent is dead or doesn't have the child buffer yet, no need to record the dead child
                    if (worldTransformLookup.HasComponent(parents[i]) && childLookup.HasBuffer(parents[i]))
                    {
                        changeOpsBlocklist.Write(new ChangeOperation
                        {
                            parent           = parents[i],
                            child            = children[i],
                            cachedChildCount = childLookup[parents[i]].Length,
                            isAddOperation   = false
                        }, threadIndex);
                    }
                }
            }

            void DoChangedChildren(in ArchetypeChunk chunk)
            {
                var newParents = chunk.GetNativeArray(ref parentHandle).Reinterpret<Entity>();
                var oldParents = chunk.GetNativeArray(ref previousParentHandle).Reinterpret<Entity>();
                var children   = chunk.GetNativeArray(entityHandle);

                for (int i = 0; i < chunk.Count; i++)
                {
                    if (newParents[i] != oldParents[i])
                    {
                        // If the old parent is dead or doesn't have the child buffer yet, no need to record the removed child
                        if (worldTransformLookup.HasComponent(oldParents[i]) && childLookup.HasBuffer(oldParents[i]))
                        {
                            changeOpsBlocklist.Write(new ChangeOperation
                            {
                                parent           = oldParents[i],
                                child            = children[i],
                                cachedChildCount = childLookup[oldParents[i]].Length,
                                isAddOperation   = false
                            }, threadIndex);
                        }

                        // If the new parent is null or deleted, we want to remove the parent components from the child
                        if (Hint.Unlikely(newParents[i] == Entity.Null || !worldTransformLookup.HasComponent(newParents[i])))
                            childrenWithNullParentsBlocklist.Write(children[i], threadIndex);
                        else
                        {
                            if (!childLookup.HasBuffer(newParents[i]))
                            {
                                parentsWithoutChildBufferBlocklist.Write(newParents[i], threadIndex);
                            }

                            changeOpsBlocklist.Write(new ChangeOperation
                            {
                                parent         = newParents[i],
                                child          = children[i],
                                isAddOperation = true
                            }, threadIndex);
                            oldParents[i] = newParents[i];
                        }
                    }
                }
            }
        }

        [BurstCompile]
        struct SortEntitiesJob : IJob
        {
            public UnsafeParallelBlockList entityBlocklist;
            public NativeList<Entity>      sortedEntities;

            public void Execute()
            {
                var count = entityBlocklist.Count();
                if (count == 0)
                    return;
                sortedEntities.ResizeUninitialized(count);
                entityBlocklist.GetElementValues(sortedEntities.AsArray());
                sortedEntities.Sort();
            }
        }

        [BurstCompile]
        struct SortOperationsJob : IJob
        {
            public UnsafeParallelBlockList     operationBlockList;
            public NativeList<ChangeOperation> sortedOperations;
            public NativeList<int2>            parentRanges;
            public NativeList<Entity>          parentsWithEmptyChildBuffers;

            public void Execute()
            {
                var count = operationBlockList.Count();
                if (count == 0)
                    return;
                sortedOperations.ResizeUninitialized(count);
                operationBlockList.GetElementValues(sortedOperations.AsArray());
                sortedOperations.Sort();

                Entity  lastEntity        = Entity.Null;
                int2    nullCounts        = default;
                ref var currentStartCount = ref nullCounts;
                for (int i = 0; i < count; i++)
                {
                    if (sortedOperations[i].parent != lastEntity)
                    {
                        parentRanges.Add(new int2(i, 1));
                        currentStartCount = ref parentRanges.ElementAt(parentRanges.Length - 1);
                        lastEntity        = sortedOperations[i].parent;
                    }
                    else
                        currentStartCount.y++;
                }

                for (int i = 0; i < parentRanges.Length; i++)
                {
                    var range     = parentRanges[i];
                    var operation = sortedOperations[range.x + range.y - 1];
                    if (operation.isAddOperation == false && operation.cachedChildCount == range.y)
                    {
                        // All the children are being removed. Discard entry and remove buffer.
                        parentsWithEmptyChildBuffers.Add(operation.parent);
                        parentRanges.RemoveAtSwapBack(i);
                        i--;
                    }
                }
            }
        }

        [BurstCompile]
        struct UpdateChildBuffersJob : IJobFor
        {
            [ReadOnly] public NativeArray<ChangeOperation> sortedOperations;
            [ReadOnly] public NativeArray<int2>            parentRanges;

            [NativeDisableParallelForRestriction] public BufferLookup<Child> childLookup;

            public void Execute(int index)
            {
                var range = parentRanges[index];

                var buffer = childLookup[sortedOperations[range.x].parent].Reinterpret<Entity>();

                for (int operationIndex = 0; operationIndex < range.y; operationIndex++)
                {
                    var operation = sortedOperations[range.x + operationIndex];

                    if (operation.isAddOperation)
                        buffer.Add(operation.child);
                    else
                    {
                        for (int i = 0; i < buffer.Length; i++)
                        {
                            if (buffer[i] == operation.child)
                            {
                                buffer.RemoveAt(i);
                                break;
                            }
                        }
                    }
                }
            }
        }

        [BurstCompile]
        struct UpdateNewPreviousParentsJob : IJobFor
        {
            [ReadOnly] public NativeArray<Entity>                                        entities;
            [ReadOnly] public ComponentLookup<Parent>                                    parentLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<PreviousParent> previousLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<LocalTransform> localTransformLookup;

            public void Execute(int index)
            {
                var  entity                     = entities[index];
                bool needsDefaultLocalTransform = entity.Index < 0;
                if (needsDefaultLocalTransform)
                {
                    entity.Index = math.abs(entity.Index + 1);

                    // We need to check in case the entity has an IdentityLocalToParentTransformTag
                    if (localTransformLookup.HasComponent(entity))
                        localTransformLookup[entity] = new LocalTransform { localTransform = TransformQvs.identity };
                }

                previousLookup[entity] = new PreviousParent { previousParent = parentLookup[entity].parent };
            }
        }
    }
}
#endif

