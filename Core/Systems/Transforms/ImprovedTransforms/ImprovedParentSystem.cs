using System;
using System.Diagnostics;
using Unity.Assertions;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Profiling;
using Unity.Transforms;

namespace Latios.Systems
{
    [UpdateInGroup(typeof(TransformSystemGroup))]
    [UpdateBefore(typeof(ParentSystem))]
    [DisableAutoCreation]
    [BurstCompile]

    [RequireMatchingQueriesForUpdate]
    public partial struct ImprovedParentSystem : ISystem
    {
        EntityQuery m_NewParentsQuery;
        EntityQuery m_RemovedParentsQuery;
        EntityQuery m_ExistingParentsQuery;
        EntityQuery m_DeletedParentsQuery;

        static readonly ProfilerMarker k_ProfileDeletedParents = new ProfilerMarker("ImprovedParentSystem.DeletedParents");
        static readonly ProfilerMarker k_ProfileRemoveParents  = new ProfilerMarker("ImprovedParentSystem.RemoveParents");
        static readonly ProfilerMarker k_ProfileChangeParents  = new ProfilerMarker("ImprovedParentSystem.ChangeParents");
        static readonly ProfilerMarker k_ProfileNewParents     = new ProfilerMarker("ImprovedParentSystem.NewParents");

        private BufferLookup<Child>                 _childLookupRo;
        private BufferLookup<Child>                 _childLookupRw;
        private ComponentLookup<Parent>             ParentFromEntityRO;
        private ComponentTypeHandle<PreviousParent> PreviousParentTypeHandleRW;
        private EntityTypeHandle                    EntityTypeHandle;
        private ComponentTypeHandle<Parent>         ParentTypeHandleRO;

        int FindChildIndex(DynamicBuffer<Child> children, Entity entity)
        {
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].Value == entity)
                    return i;
            }

            throw new InvalidOperationException("Child entity not in parent");
        }

        void RemoveChildFromParent(ref SystemState state, Entity childEntity, Entity parentEntity)
        {
            if (!state.EntityManager.HasComponent<Child>(parentEntity))
                return;

            var children   = state.EntityManager.GetBuffer<Child>(parentEntity);
            var childIndex = FindChildIndex(children, childEntity);
            children.RemoveAtSwapBack(childIndex);
            if (children.Length == 0)
            {
                state.EntityManager.RemoveComponent(parentEntity, ComponentType.FromTypeIndex(
                                                        TypeManager.GetTypeIndex<Child>()));
            }
        }

        [BurstCompile]
        struct GatherChangedParents : IJobChunk
        {
            public NativeMultiHashMap<Entity, Entity>.ParallelWriter ParentChildrenToAdd;
            public NativeMultiHashMap<Entity, Entity>.ParallelWriter ParentChildrenToRemove;
            public NativeParallelHashMap<Entity, int>.ParallelWriter UniqueParents;
            public ComponentTypeHandle<PreviousParent>               PreviousParentTypeHandle;
            [ReadOnly] public BufferLookup<Child>                    ChildLookup;

            [ReadOnly] public ComponentTypeHandle<Parent> ParentTypeHandle;
            [ReadOnly] public EntityTypeHandle            EntityTypeHandle;
            public uint                                   LastSystemVersion;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                Assert.IsFalse(useEnabledMask);

                if (chunk.DidChange(ParentTypeHandle, LastSystemVersion) ||
                    chunk.DidChange(PreviousParentTypeHandle, LastSystemVersion))
                {
                    var chunkPreviousParents = chunk.GetNativeArray(PreviousParentTypeHandle);
                    var chunkParents         = chunk.GetNativeArray(ParentTypeHandle);
                    var chunkEntities        = chunk.GetNativeArray(EntityTypeHandle);

                    for (int j = 0, chunkEntityCount = chunk.Count; j < chunkEntityCount; j++)
                    {
                        if (chunkParents[j].Value != chunkPreviousParents[j].Value)
                        {
                            var childEntity          = chunkEntities[j];
                            var parentEntity         = chunkParents[j].Value;
                            var previousParentEntity = chunkPreviousParents[j].Value;

                            ParentChildrenToAdd.Add(parentEntity, childEntity);
                            UniqueParents.TryAdd(parentEntity, 0);

                            if (ChildLookup.HasBuffer(previousParentEntity))
                            {
                                ParentChildrenToRemove.Add(previousParentEntity, childEntity);
                                UniqueParents.TryAdd(previousParentEntity, 0);
                            }

                            chunkPreviousParents[j] = new PreviousParent
                            {
                                Value = parentEntity
                            };
                        }
                    }
                }
            }
        }

        [BurstCompile]
        struct FindMissingChild : IJob
        {
            [ReadOnly] public NativeParallelHashMap<Entity, int> UniqueParents;
            [ReadOnly] public BufferLookup<Child>                ChildLookup;
            public NativeList<Entity>                            ParentsMissingChild;

            public void Execute()
            {
                var parents = UniqueParents.GetKeyArray(Allocator.Temp);
                for (int i = 0; i < parents.Length; i++)
                {
                    var parent = parents[i];
                    if (!ChildLookup.HasBuffer(parent))
                    {
                        ParentsMissingChild.Add(parent);
                    }
                }
                ParentsMissingChild.Sort();
            }
        }

        [BurstCompile]
        struct FixupChangedChildren : IJob
        {
            [ReadOnly] public NativeMultiHashMap<Entity, Entity> ParentChildrenToAdd;
            [ReadOnly] public NativeMultiHashMap<Entity, Entity> ParentChildrenToRemove;
            [ReadOnly] public NativeParallelHashMap<Entity, int> UniqueParents;

            public BufferLookup<Child> ChildLookup;

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            private static void ThrowChildEntityNotInParent()
            {
                throw new InvalidOperationException("Child entity not in parent");
            }

            int FindChildIndex(DynamicBuffer<Child> children, Entity entity)
            {
                for (int i = 0; i < children.Length; i++)
                {
                    if (children[i].Value == entity)
                        return i;
                }

                ThrowChildEntityNotInParent();
                return -1;
            }

            void RemoveChildrenFromParent(Entity parent, DynamicBuffer<Child> children, NativeList<Entity> entityCache)
            {
                if (ParentChildrenToRemove.TryGetFirstValue(parent, out var child, out var it))
                {
                    entityCache.Clear();
                    do
                    {
                        entityCache.Add(child);
                    }
                    while (ParentChildrenToRemove.TryGetNextValue(out child, ref it));

                    entityCache.Sort();
                    foreach (var entity in entityCache)
                    {
                        var childIndex = FindChildIndex(children, entity);
                        children.RemoveAtSwapBack(childIndex);
                    }
                }
            }

            void AddChildrenToParent(Entity parent, DynamicBuffer<Child> children, NativeList<Entity> entityCache)
            {
                if (ParentChildrenToAdd.TryGetFirstValue(parent, out var child, out var it))
                {
                    entityCache.Clear();
                    do
                    {
                        entityCache.Add(child);
                    }
                    while (ParentChildrenToAdd.TryGetNextValue(out child, ref it));

                    entityCache.Sort();
                    foreach (var entity in entityCache)
                    {
                        children.Add(new Child() {
                            Value = entity
                        });
                    }
                }
            }

            public void Execute()
            {
                var parents     = UniqueParents.GetKeyArray(Allocator.Temp);
                var entityCache = new NativeList<Entity>(128, Allocator.Temp);
                for (int i = 0; i < parents.Length; i++)
                {
                    var parent = parents[i];

                    if (ChildLookup.TryGetBuffer(parent, out var children))
                    {
                        RemoveChildrenFromParent(parent, children, entityCache);
                        AddChildrenToParent(parent, children, entityCache);
                    }
                }
            }
        }

        /// <inheritdoc cref="ISystem.OnCreate"/>
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            _childLookupRo             = state.GetBufferLookup<Child>(true);
            _childLookupRw             = state.GetBufferLookup<Child>();
            ParentFromEntityRO         = state.GetComponentLookup<Parent>(true);
            PreviousParentTypeHandleRW = state.GetComponentTypeHandle<PreviousParent>(false);
            ParentTypeHandleRO         = state.GetComponentTypeHandle<Parent>(true);
            EntityTypeHandle           = state.GetEntityTypeHandle();

            var builder0 = new EntityQueryBuilder(Allocator.Temp)
                           .WithAll<Parent>()
                           .WithNone<PreviousParent>()
                           .WithOptions(EntityQueryOptions.FilterWriteGroup);
            m_NewParentsQuery = state.GetEntityQuery(builder0);

            var builder1 = new EntityQueryBuilder(Allocator.Temp)
                           .WithAllRW<PreviousParent>()
                           .WithNone<Parent>()
                           .WithOptions(EntityQueryOptions.FilterWriteGroup);
            m_RemovedParentsQuery = state.GetEntityQuery(builder1);

            var builder2 = new EntityQueryBuilder(Allocator.Temp)
                           .WithAll<Parent>()
                           .WithAllRW<PreviousParent>()
                           .WithOptions(EntityQueryOptions.FilterWriteGroup);
            m_ExistingParentsQuery = state.GetEntityQuery(builder2);
            m_ExistingParentsQuery.ResetFilter();
            m_ExistingParentsQuery.AddChangedVersionFilter(ComponentType.ReadWrite<Parent>());
            m_ExistingParentsQuery.AddChangedVersionFilter(ComponentType.ReadWrite<PreviousParent>());

            var builder3 = new EntityQueryBuilder(Allocator.Temp)
                           .WithAllRW<Child>()
                           .WithNone<LocalToWorld>()
                           .WithOptions(EntityQueryOptions.FilterWriteGroup);
            m_DeletedParentsQuery = state.GetEntityQuery(builder3);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }

        void UpdateNewParents(ref SystemState state)
        {
            if (m_NewParentsQuery.IsEmptyIgnoreFilter)
                return;

            state.CompleteDependency();

            state.EntityManager.AddComponent(m_NewParentsQuery, ComponentType.FromTypeIndex(
                                                 TypeManager.GetTypeIndex<PreviousParent>()));
        }

        void UpdateRemoveParents(ref SystemState state)
        {
            if (m_RemovedParentsQuery.IsEmptyIgnoreFilter)
                return;

            state.CompleteDependency();
            var childEntities   = m_RemovedParentsQuery.ToEntityArray(state.WorldUpdateAllocator);
            var previousParents = m_RemovedParentsQuery.ToComponentDataArray<PreviousParent>(state.WorldUpdateAllocator);

            for (int i = 0; i < childEntities.Length; i++)
            {
                var childEntity          = childEntities[i];
                var previousParentEntity = previousParents[i].Value;

                RemoveChildFromParent(ref state, childEntity, previousParentEntity);
            }

            state.EntityManager.RemoveComponent(m_RemovedParentsQuery, ComponentType.FromTypeIndex(
                                                    TypeManager.GetTypeIndex<PreviousParent>()));
        }

        void UpdateChangeParents(ref SystemState state)
        {
            if (m_ExistingParentsQuery.IsEmptyIgnoreFilter)
                return;

            var count = m_ExistingParentsQuery.CalculateEntityCount() * 2;  // Potentially 2x changed: current and previous
            if (count == 0)
                return;

            state.CompleteDependency();

            // 1. Get (Parent,Child) to remove
            // 2. Get (Parent,Child) to add
            // 3. Get unique Parent change list
            // 4. Set PreviousParent to new Parent
            var parentChildrenToAdd    = new NativeMultiHashMap<Entity, Entity>(count, state.WorldUpdateAllocator);
            var parentChildrenToRemove = new NativeMultiHashMap<Entity, Entity>(count, state.WorldUpdateAllocator);
            var uniqueParents          = new NativeParallelHashMap<Entity, int>(count, state.WorldUpdateAllocator);

            ParentTypeHandleRO.Update(ref state);
            PreviousParentTypeHandleRW.Update(ref state);
            EntityTypeHandle.Update(ref state);
            _childLookupRw.Update(ref state);
            var gatherChangedParentsJob = new GatherChangedParents
            {
                ParentChildrenToAdd      = parentChildrenToAdd.AsParallelWriter(),
                ParentChildrenToRemove   = parentChildrenToRemove.AsParallelWriter(),
                UniqueParents            = uniqueParents.AsParallelWriter(),
                PreviousParentTypeHandle = PreviousParentTypeHandleRW,
                ChildLookup              = _childLookupRw,
                ParentTypeHandle         = ParentTypeHandleRO,
                EntityTypeHandle         = EntityTypeHandle,
                LastSystemVersion        = state.LastSystemVersion
            };
            var gatherChangedParentsJobHandle = gatherChangedParentsJob.ScheduleParallel(m_ExistingParentsQuery, default);
            gatherChangedParentsJobHandle.Complete();

            // 5. (Structural change) Add any missing Child to Parents
            var parentsMissingChild = new NativeList<Entity>(state.WorldUpdateAllocator);
            _childLookupRo.Update(ref state);
            var findMissingChildJob = new FindMissingChild
            {
                UniqueParents       = uniqueParents,
                ChildLookup         = _childLookupRo,
                ParentsMissingChild = parentsMissingChild
            };
            //var findMissingChildJobHandle = findMissingChildJob.Schedule();
            //findMissingChildJobHandle.Complete();
            findMissingChildJob.Execute();

            state.EntityManager.AddComponent(parentsMissingChild.AsArray(), ComponentType.FromTypeIndex(
                                                 TypeManager.GetTypeIndex<Child>()));

            // 6. Get Child[] for each unique Parent
            // 7. Update Child[] for each unique Parent
            _childLookupRw.Update(ref state);
            var fixupChangedChildrenJob = new FixupChangedChildren
            {
                ParentChildrenToAdd    = parentChildrenToAdd,
                ParentChildrenToRemove = parentChildrenToRemove,
                UniqueParents          = uniqueParents,
                ChildLookup            = _childLookupRw
            };

            //var fixupChangedChildrenJobHandle = fixupChangedChildrenJob.Schedule();
            //fixupChangedChildrenJobHandle.Complete();
            fixupChangedChildrenJob.Execute();
        }

        [BurstCompile]
        struct GatherChildEntities : IJob
        {
            [ReadOnly] public NativeArray<Entity>     Parents;
            public NativeList<Entity>                 Children;
            [ReadOnly] public BufferLookup<Child>     ChildLookup;
            [ReadOnly] public ComponentLookup<Parent> ParentFromEntity;

            public void Execute()
            {
                for (int i = 0; i < Parents.Length; i++)
                {
                    var parentEntity        = Parents[i];
                    var childEntitiesSource = ChildLookup[parentEntity].AsNativeArray();
                    for (int j = 0; j < childEntitiesSource.Length; j++)
                    {
                        var childEntity = childEntitiesSource[j].Value;
                        if (ParentFromEntity.HasComponent(childEntity) && ParentFromEntity[childEntity].Value == parentEntity)
                        {
                            Children.Add(childEntitiesSource[j].Value);
                        }
                    }
                }
            }
        }

        void UpdateDeletedParents(ref SystemState state)
        {
            if (m_DeletedParentsQuery.IsEmptyIgnoreFilter)
                return;

            state.CompleteDependency();

            var previousParents = m_DeletedParentsQuery.ToEntityArray(state.WorldUpdateAllocator);
            var childEntities   = new NativeList<Entity>(state.WorldUpdateAllocator);

            _childLookupRo.Update(ref state);
            ParentFromEntityRO.Update(ref state);
            var gatherChildEntitiesJob = new GatherChildEntities
            {
                Parents          = previousParents,
                Children         = childEntities,
                ChildLookup      = _childLookupRo,
                ParentFromEntity = ParentFromEntityRO,
            };
            //var gatherChildEntitiesJobHandle = gatherChildEntitiesJob.Schedule();
            //gatherChildEntitiesJobHandle.Complete();
            gatherChildEntitiesJob.Execute();

            var typesToRemove = new ComponentTypeSet(ComponentType.ReadWrite<Parent>(), ComponentType.ReadWrite<PreviousParent>(), ComponentType.ReadWrite<LocalToParent>());
            state.EntityManager.RemoveComponent( childEntities.AsArray(), typesToRemove);
            state.EntityManager.RemoveComponent( m_DeletedParentsQuery,   ComponentType.FromTypeIndex(TypeManager.GetTypeIndex<Child>()));
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            //inputDeps.Complete(); // #todo
            //state.Dependency.Complete();

            k_ProfileDeletedParents.Begin();
            UpdateDeletedParents(ref state);
            k_ProfileDeletedParents.End();

            k_ProfileRemoveParents.Begin();
            UpdateRemoveParents(ref state);
            k_ProfileRemoveParents.End();

            k_ProfileNewParents.Begin();
            UpdateNewParents(ref state);
            k_ProfileNewParents.End();

            k_ProfileChangeParents.Begin();
            UpdateChangeParents(ref state);
            k_ProfileChangeParents.End();
        }
    }
}

