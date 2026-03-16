#if !LATIOS_TRANSFORMS_UNITY
using System;
using System.Collections.Generic;
using Latios.Authoring;
using static Unity.Entities.SystemAPI;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Transforms.Authoring.Systems
{
    [DisableAutoCreation]
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    [UpdateInGroup(typeof(TransformBakingSystemGroup))]
    [BurstCompile]
    public partial struct TransformBakingSystem : ISystem
    {
        [BakingType] struct TrackedTag : ICleanupComponentData { }
        [BakingType] struct LocalOverrideCleanupTag : ICleanupComponentData { }
        [BakingType] struct ParentOverrideCleanupTag : ICleanupComponentData { }

        EntityQuery m_query;
        EntityQuery m_newQuery;
        EntityQuery m_deadQuery;
        EntityQuery m_newLocalOverrideQuery;
        EntityQuery m_deadLocalOverrideQuery;
        EntityQuery m_newParentOverrideQuery;
        EntityQuery m_deadParentOverrideQuery;

        Hierarchy m_hierarchy;

        public void OnCreate(ref SystemState state)
        {
            m_query                 = state.Fluent().With<TransformAuthoring>(true).IncludeDisabledEntities().IncludePrefabs().Build();
            m_newQuery              = state.Fluent().With<TransformAuthoring>(true).Without<TrackedTag>().IncludeDisabledEntities().IncludePrefabs().Build();
            m_deadQuery             = state.Fluent().With<TrackedTag>(true).Without<TransformAuthoring>().IncludeDisabledEntities().IncludePrefabs().Build();
            m_newLocalOverrideQuery =
                state.Fluent().With<BakedLocalTransformOverride>(true).Without<LocalOverrideCleanupTag>().IncludeDisabledEntities().IncludePrefabs().Build();
            m_deadLocalOverrideQuery =
                state.Fluent().With<LocalOverrideCleanupTag>(true).Without<BakedLocalTransformOverride>().IncludeDisabledEntities().IncludePrefabs().Build();
            m_newParentOverrideQuery  = state.Fluent().With<BakedParentOverride>(true).Without<ParentOverrideCleanupTag>().IncludeDisabledEntities().IncludePrefabs().Build();
            m_deadParentOverrideQuery = state.Fluent().With<ParentOverrideCleanupTag>(true).Without<BakedParentOverride>().IncludeDisabledEntities().IncludePrefabs().Build();
            m_hierarchy               = new Hierarchy(256, Allocator.Persistent);
        }

        public void OnDestroy(ref SystemState state)
        {
            m_hierarchy.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var entityCount   = m_query.CalculateEntityCountWithoutFiltering();
            var dirtyRootsSet = new NativeHashSet<Entity>(entityCount, state.WorldUpdateAllocator);

            var createDestroyJh = new CreateDestroyJob
            {
                newEntities  = m_newQuery.ToEntityArray(state.WorldUpdateAllocator),
                deadEntities = m_deadQuery.ToEntityArray(state.WorldUpdateAllocator),
                dirtyRoots   = dirtyRootsSet,
                hierarchy    = m_hierarchy,
            }.Schedule(default);

            state.EntityManager.RemoveComponent<TrackedTag>(m_deadQuery);
            state.EntityManager.AddComponent<TrackedTag>(m_newQuery);

            var ecb                           = new EntityCommandBuffer(state.WorldUpdateAllocator);
            var entityInHierarchyChangeStream = new NativeStream(m_query.CalculateChunkCountWithoutFiltering(), state.WorldUpdateAllocator);

            var classifyJh = new ClassifyJob
            {
                hierarchy                         = m_hierarchy,
                entityHandle                      = GetEntityTypeHandle(),
                transformAuthoringHandle          = GetComponentTypeHandle<TransformAuthoring>(true),
                transformAuthoringLookup          = GetComponentLookup<TransformAuthoring>(true),
                rootReferenceHandle               = GetComponentTypeHandle<RootReference>(true),
                authoringSiblingIndexHandle       = GetComponentTypeHandle<AuthoringSiblingIndex>(true),
                mergedInheritanceFlagsHandle      = GetComponentTypeHandle<MergedInheritanceFlags>(true),
                entityInHierarchyLookup           = GetBufferLookup<EntityInHierarchy>(true),
                bakedLocalTransformOverrideHandle = GetComponentTypeHandle<BakedLocalTransformOverride>(true),
                localOverrideCleanupTagHandle     = GetComponentTypeHandle<LocalOverrideCleanupTag>(true),
                bakedParentOverrideHandle         = GetComponentTypeHandle<BakedParentOverride>(true),
                parentOverrideCleanupTagHandle    = GetComponentTypeHandle<ParentOverrideCleanupTag>(true),
                staticHandle                      = GetComponentTypeHandle<Unity.Transforms.Static>(true),
                worldTransformHandle              = GetComponentTypeHandle<WorldTransform>(false),
                entityHierarchyChangeStream       = entityInHierarchyChangeStream.AsWriter(),
                ecb                               = ecb.AsParallelWriter(),
                lastSystemVersion                 = state.LastSystemVersion,
            }.ScheduleParallel(m_query, JobHandle.CombineDependencies(state.Dependency, createDestroyJh));

            var dirtyRootsList           = new NativeList<Entity>(state.WorldUpdateAllocator);
            var dirtyRootHasChildrenList = new NativeBitArray(1, state.WorldUpdateAllocator, NativeArrayOptions.UninitializedMemory);

            var applyHierarchyChangesJh = new ApplyHierarchyChangesJob
            {
                entityHierarchyChangeStream = entityInHierarchyChangeStream.AsReader(),
                hierarchy                   = m_hierarchy,
                dirtyRootsSet               = dirtyRootsSet,
                dirtyRootHasChildrenList    = dirtyRootHasChildrenList,
                dirtyRootsList              = dirtyRootsList,
            }.Schedule(classifyJh);

            classifyJh.Complete();
            ecb.Playback(state.EntityManager);
            state.EntityManager.RemoveComponent<LocalOverrideCleanupTag>(m_deadLocalOverrideQuery);
            state.EntityManager.AddComponent<LocalOverrideCleanupTag>(m_newLocalOverrideQuery);
            state.EntityManager.RemoveComponent<ParentOverrideCleanupTag>(m_deadParentOverrideQuery);
            state.EntityManager.AddComponent<ParentOverrideCleanupTag>(m_newParentOverrideQuery);
            applyHierarchyChangesJh.Complete();

            for (int i = 0; i < dirtyRootsList.Length; i++)
            {
                var root  = dirtyRootsList[i];
                var has   = state.EntityManager.HasBuffer<EntityInHierarchy>(root);
                var needs = dirtyRootHasChildrenList.IsSet(i);
                if (has && !needs)
                    state.EntityManager.RemoveComponent<EntityInHierarchy>(root);
                else if (!has && needs)
                    state.EntityManager.AddBuffer<EntityInHierarchy>(root);
            }

            state.Dependency = new RebuildHierarchiesJob
            {
                hierarchy                         = m_hierarchy,
                dirtyRoots                        = dirtyRootsList.AsArray(),
                transformAuthoringLookup          = GetComponentLookup<TransformAuthoring>(true),
                inheritanceFlagsLookup            = GetComponentLookup<MergedInheritanceFlags>(true),
                bakedLocalTransformOverrideLookup = GetComponentLookup<BakedLocalTransformOverride>(true),
                worldTransformLookup              = GetComponentLookup<WorldTransform>(false),
                rootReferenceLookup               = GetComponentLookup<RootReference>(false),
                entityInHierarchyLookup           = GetBufferLookup<EntityInHierarchy>(false),
            }.ScheduleParallel(dirtyRootsList.Length, 1, default);
        }

        struct Hierarchy
        {
            struct Node
            {
                public Entity             entity;
                public Entity             parent;
                public int                order;  // -1 = unordered.
                public UnsafeList<Entity> children;
            }

            NativeHashMap<Entity, int> entityToNodeIndexMap;
            NativeList<Node>           nodes;

            public Hierarchy(int initialCapacity, AllocatorManager.AllocatorHandle allocator)
            {
                entityToNodeIndexMap = new NativeHashMap<Entity, int>(initialCapacity, allocator);
                nodes                = new NativeList<Node>(initialCapacity, allocator);
            }

            public void Dispose()
            {
                foreach (var node in nodes)
                {
                    if (node.children.IsCreated)
                        node.children.Dispose();
                }
                nodes.Dispose();
                entityToNodeIndexMap.Dispose();
            }

            public void GetOrderAndParent(Entity entity, out int order, out Entity parent)
            {
                var node = nodes[entityToNodeIndexMap[entity]];
                order    = node.order;
                parent   = node.parent;
            }

            public void GetOrderAndChildren(Entity entity, out int order, out UnsafeList<Entity> children)
            {
                var node = nodes[entityToNodeIndexMap[entity]];
                order    = node.order;
                children = node.children;
            }

            public bool HasChildren(Entity entity)
            {
                var node = nodes[entityToNodeIndexMap[entity]];
                return !node.children.IsEmpty;
            }

            public int GetDeterministicIndex(Entity entity) => entityToNodeIndexMap[entity];

            public void Add(Entity entity)
            {
                var index                   = nodes.Length;
                nodes.Add(new Node { entity = entity, order = -1 });
                entityToNodeIndexMap.Add(entity, index);
            }

            public void Remove(Entity entity, ref NativeHashSet<Entity> dirtyRoots)
            {
                ref var node = ref nodes.ElementAt(entityToNodeIndexMap[entity]);
                if (node.children.IsCreated)
                {
                    foreach (var child in node.children)
                    {
                        ChangeParent(entity, -1, Entity.Null, ref dirtyRoots);
                    }
                    node.children.Dispose();
                }
            }

            public void ChangeOrder(Entity entity, int order, ref NativeHashSet<Entity> dirtyRoots)
            {
                ref var node = ref nodes.ElementAt(entityToNodeIndexMap[entity]);
                node.order   = order;
                if (node.parent != Entity.Null)
                    FindAndDirtyRoot(node.parent, ref dirtyRoots);
            }

            public void ChangeParent(Entity entity, int order, Entity newParent, ref NativeHashSet<Entity> dirtyRoots)
            {
                ref var node = ref nodes.ElementAt(entityToNodeIndexMap[entity]);
                if (node.parent != Entity.Null)
                {
                    FindAndDirtyRoot(node.parent, ref dirtyRoots);
                    ref var previousParentNode = ref nodes.ElementAt(entityToNodeIndexMap[node.parent]);
                    for (int i = 0; i < previousParentNode.children.Length; i++)
                    {
                        if (previousParentNode.children[i] == entity)
                        {
                            previousParentNode.children.RemoveAtSwapBack(i);
                            break;
                        }
                    }
                }
                node.parent = newParent;
                node.order  = order;
                if (newParent != Entity.Null)
                {
                    FindAndDirtyRoot(newParent, ref dirtyRoots);
                    ref var newParentNode = ref nodes.ElementAt(entityToNodeIndexMap[newParent]);
                    if (!newParentNode.children.IsCreated)
                        newParentNode.children = new UnsafeList<Entity>(8, Allocator.Persistent);
                    newParentNode.children.Add(entity);
                }
                else
                {
                    dirtyRoots.Add(entity);
                }
            }

            void FindAndDirtyRoot(Entity searchStart, ref NativeHashSet<Entity> dirtyRoots)
            {
                var search         = searchStart;
                var previousSearch = search;
                while (search != Entity.Null)
                {
                    previousSearch = search;
                    search         = nodes[entityToNodeIndexMap[search]].parent;
                }
                dirtyRoots.Add(previousSearch);
            }
        }

        struct EntityHierarchyChange
        {
            public Entity entity;
            public Entity parent;
            public int    order;
            public bool   parentChanged;
        }

        [BurstCompile]
        struct CreateDestroyJob : IJob
        {
            [ReadOnly] public NativeArray<Entity> newEntities;
            [ReadOnly] public NativeArray<Entity> deadEntities;
            public Hierarchy                      hierarchy;
            public NativeHashSet<Entity>          dirtyRoots;

            public void Execute()
            {
                foreach (var entity in deadEntities)
                    hierarchy.Remove(entity, ref dirtyRoots);
                foreach (var entity in newEntities)
                    hierarchy.Add(entity);
            }
        }

        [BurstCompile]
        struct ClassifyJob : IJobChunk
        {
            [ReadOnly] public Hierarchy                                        hierarchy;
            [ReadOnly] public EntityTypeHandle                                 entityHandle;
            [ReadOnly] public ComponentTypeHandle<TransformAuthoring>          transformAuthoringHandle;
            [ReadOnly] public ComponentLookup<TransformAuthoring>              transformAuthoringLookup;
            [ReadOnly] public ComponentTypeHandle<RootReference>               rootReferenceHandle;
            [ReadOnly] public ComponentTypeHandle<AuthoringSiblingIndex>       authoringSiblingIndexHandle;
            [ReadOnly] public ComponentTypeHandle<MergedInheritanceFlags>      mergedInheritanceFlagsHandle;
            [ReadOnly] public BufferLookup<EntityInHierarchy>                  entityInHierarchyLookup;
            [ReadOnly] public ComponentTypeHandle<BakedLocalTransformOverride> bakedLocalTransformOverrideHandle;
            [ReadOnly] public ComponentTypeHandle<LocalOverrideCleanupTag>     localOverrideCleanupTagHandle;
            [ReadOnly] public ComponentTypeHandle<BakedParentOverride>         bakedParentOverrideHandle;
            [ReadOnly] public ComponentTypeHandle<ParentOverrideCleanupTag>    parentOverrideCleanupTagHandle;
            [ReadOnly] public ComponentTypeHandle<Unity.Transforms.Static>     staticHandle;  // Despite the namespace, this is in Unity.Entities assembly
            public ComponentTypeHandle<WorldTransform>                         worldTransformHandle;

            public NativeStream.Writer                entityHierarchyChangeStream;
            public EntityCommandBuffer.ParallelWriter ecb;
            public uint                               lastSystemVersion;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                bool localOverrideChanged  = chunk.DidChange(ref bakedLocalTransformOverrideHandle, lastSystemVersion);
                bool parentOverrideChanged = chunk.DidChange(ref bakedParentOverrideHandle, lastSystemVersion);
                if (!chunk.DidChange(ref transformAuthoringHandle, lastSystemVersion) &&
                    !chunk.DidChange(ref authoringSiblingIndexHandle, lastSystemVersion) &&
                    !chunk.DidChange(ref mergedInheritanceFlagsHandle, lastSystemVersion) &&
                    !localOverrideChanged && !parentOverrideChanged &&
                    !chunk.DidOrderChange(lastSystemVersion))
                    return;

                entityHierarchyChangeStream.BeginForEachIndex(unfilteredChunkIndex);

                bool hasWorldTransform         = chunk.Has(ref worldTransformHandle);
                bool hasRootReference          = chunk.Has(ref rootReferenceHandle);
                bool hasAuthoringSiblingIndex  = chunk.Has(ref authoringSiblingIndexHandle);
                bool hasInheritanceFlags       = chunk.Has(ref mergedInheritanceFlagsHandle);
                bool hasStatic                 = chunk.Has(ref staticHandle);
                bool hasParentOverride         = chunk.Has(ref bakedParentOverrideHandle);
                localOverrideChanged          |= chunk.Has(ref bakedLocalTransformOverrideHandle) != chunk.Has(ref localOverrideCleanupTagHandle);
                parentOverrideChanged         |= hasParentOverride != chunk.Has(ref parentOverrideCleanupTagHandle);

                var entities                   = chunk.GetNativeArray(entityHandle);
                var transformAuthoringArray    = chunk.GetNativeArray(ref transformAuthoringHandle);
                var rootReferenceArray         = chunk.GetNativeArray(ref rootReferenceHandle);
                var authoringSiblingIndexArray = chunk.GetNativeArray(ref authoringSiblingIndexHandle);
                var inheritanceFlagsArray      = chunk.GetNativeArray(ref mergedInheritanceFlagsHandle);
                var worldTransformArray        = chunk.GetNativeArray(ref worldTransformHandle);
                var overrideParentArray        = chunk.GetNativeArray(ref bakedParentOverrideHandle);

                for (int i = 0; i < chunk.Count; i++)
                {
                    var transformAuthoring = transformAuthoringArray[i];

                    // Todo: Similar to Unity Transforms baking, we don't properly remove components when switching to Manual Override.
                    if ((transformAuthoring.RuntimeTransformUsage & RuntimeTransformComponentFlags.ManualOverride) != 0)
                    {
                        hierarchy.GetOrderAndParent(entities[i], out _, out Entity parent);
                        if (parent != Entity.Null)
                        {
                            entityHierarchyChangeStream.Write(new EntityHierarchyChange
                            {
                                entity        = entities[i],
                                parent        = Entity.Null,
                                order         = -1,
                                parentChanged = true
                            });
                        }
                        continue;
                    }

                    bool needsWorldTransform = false;
                    bool needsLocalTransform = false;
                    if (transformAuthoring.RuntimeTransformUsage == RuntimeTransformComponentFlags.None)
                    {
                        // Do nothing
                    }
                    else if (hasStatic)
                    {
                        needsWorldTransform = true;
                    }
                    else
                    {
                        needsWorldTransform = (transformAuthoring.RuntimeTransformUsage & RuntimeTransformComponentFlags.LocalToWorld) != 0;
                        needsLocalTransform = (transformAuthoring.RuntimeTransformUsage & RuntimeTransformComponentFlags.RequestParent) != 0 || hasParentOverride;
                    }

                    if (needsLocalTransform == false && needsWorldTransform == false && hasWorldTransform)
                    {
                        var cts = new ComponentTypeSet(ComponentType.ReadOnly<WorldTransform>(),
                                                       ComponentType.ReadOnly<RootReference>(),
                                                       ComponentType.ReadOnly<EntityInHierarchy>());
                        ecb.RemoveComponent(unfilteredChunkIndex, entities[i], cts);

                        hierarchy.GetOrderAndParent(entities[i], out _, out var parent);
                        if (parent != Entity.Null)
                        {
                            entityHierarchyChangeStream.Write(new EntityHierarchyChange
                            {
                                entity        = entities[i],
                                parent        = Entity.Null,
                                order         = -1,
                                parentChanged = true
                            });
                        }
                    }

                    if (needsWorldTransform == true && needsLocalTransform == false)
                    {
                        if (hasRootReference)
                        {
                            ecb.RemoveComponent<RootReference>(unfilteredChunkIndex, entities[i]);
                            hierarchy.GetOrderAndParent(entities[i], out _, out var parent);
                            if (parent != Entity.Null)
                            {
                                entityHierarchyChangeStream.Write(new EntityHierarchyChange
                                {
                                    entity        = entities[i],
                                    parent        = Entity.Null,
                                    order         = -1,
                                    parentChanged = true
                                });
                            }
                        }

                        TransformBakeUtils.GetScaleAndStretch(transformAuthoring.LocalScale, out var scale, out var stretch);

                        TransformQvvs worldTransform;
                        if (transformAuthoring.AuthoringParent != Entity.Null)
                        {
                            worldTransform = GetWorldTransform(in transformAuthoring, true);
                        }
                        else
                        {
                            worldTransform = new TransformQvvs
                            {
                                position  = transformAuthoring.LocalPosition,
                                rotation  = transformAuthoring.LocalRotation,
                                scale     = scale,
                                stretch   = stretch,
                                context32 = 0
                            };
                        }

                        if (hasWorldTransform)
                            worldTransformArray[i] = new WorldTransform { worldTransform = worldTransform };
                        else
                            ecb.AddComponent(unfilteredChunkIndex, entities[i], new WorldTransform { worldTransform = worldTransform });
                    }

                    if (needsWorldTransform && needsLocalTransform)
                    {
                        if (!hasWorldTransform || !hasRootReference)
                        {
                            var cts = new ComponentTypeSet(ComponentType.ReadOnly<WorldTransform>(), ComponentType.ReadOnly<RootReference>());
                            ecb.AddComponent(unfilteredChunkIndex, entities[i], cts);
                        }

                        bool inheritanceFlagsChanged = false;
                        if (hasRootReference && entityInHierarchyLookup.TryGetBuffer(rootReferenceArray[i].rootEntity, out var buffer))
                        {
                            var currentFlags        = hasInheritanceFlags ? inheritanceFlagsArray[i].flags : default;
                            var previousFlags       = buffer[rootReferenceArray[i].indexInHierarchy].m_flags;
                            inheritanceFlagsChanged = currentFlags != previousFlags;
                        }

                        hierarchy.GetOrderAndParent(entities[i], out var order, out var parent);
                        var targetParent  = hasParentOverride ? overrideParentArray[i].parent : transformAuthoring.RuntimeParent;
                        var parentChanged = parent != targetParent;
                        var currentOrder  = hasAuthoringSiblingIndex ? authoringSiblingIndexArray[i].index : -1;

                        // We check for override changes, flags, parent change, order change, and dirty transform data (change in positions, rotation, and scale)
                        if (localOverrideChanged || inheritanceFlagsChanged || parentChanged || order != currentOrder ||
                            ChangeVersionUtility.DidChange(transformAuthoring.ChangeVersion, lastSystemVersion))
                        {
                            entityHierarchyChangeStream.Write(new EntityHierarchyChange
                            {
                                entity        = entities[i],
                                parent        = targetParent,
                                order         = currentOrder,
                                parentChanged = parentChanged
                            });
                        }
                    }
                }

                entityHierarchyChangeStream.EndForEachIndex();
            }

            TransformQvvs GetWorldTransform(in TransformAuthoring transformAuthoring, bool useAuthoringParent = false)
            {
                if (transformAuthoring.RuntimeParent == Entity.Null && !(useAuthoringParent && transformAuthoring.AuthoringParent != Entity.Null))
                {
                    TransformBakeUtils.GetScaleAndStretch(transformAuthoring.LocalScale, out var scale, out var stretch);

                    return new TransformQvvs
                    {
                        position  = transformAuthoring.LocalPosition,
                        rotation  = transformAuthoring.LocalRotation,
                        scale     = scale,
                        stretch   = stretch,
                        context32 = 0
                    };
                }
                else
                {
                    var targetParent = transformAuthoring.RuntimeParent == Entity.Null &&
                                       useAuthoringParent ? transformAuthoring.AuthoringParent : transformAuthoring.RuntimeParent;
                    var parentWorldTransform = GetWorldTransform(transformAuthoringLookup[targetParent], useAuthoringParent);
                    TransformBakeUtils.GetScaleAndStretch(transformAuthoring.LocalScale, out var scale, out var stretch);
                    var worldTransform = new TransformQvvs
                    {
                        stretch = stretch,
                    };
                    var localTransform = new TransformQvs
                    {
                        position = transformAuthoring.LocalPosition,
                        rotation = transformAuthoring.LocalRotation,
                        scale    = scale
                    };
                    qvvs.mulclean(ref worldTransform, parentWorldTransform, localTransform);
                    return worldTransform;
                }
            }
        }

        [BurstCompile]
        struct ApplyHierarchyChangesJob : IJob
        {
            [ReadOnly] public NativeStream.Reader entityHierarchyChangeStream;
            public Hierarchy                      hierarchy;
            public NativeHashSet<Entity>          dirtyRootsSet;
            public NativeList<Entity>             dirtyRootsList;
            public NativeBitArray                 dirtyRootHasChildrenList;

            public void Execute()
            {
                var foreachCount = entityHierarchyChangeStream.ForEachCount;
                for (int chunk = 0; chunk < foreachCount; chunk++)
                {
                    var elements = entityHierarchyChangeStream.BeginForEachIndex(chunk);
                    for (int i = 0; i < elements; i++)
                    {
                        var change = entityHierarchyChangeStream.Read<EntityHierarchyChange>();
                        if (change.parentChanged)
                            hierarchy.ChangeParent(change.entity, change.order, change.parent, ref dirtyRootsSet);
                        else
                            hierarchy.ChangeOrder(change.entity, change.order, ref dirtyRootsSet); // Changing order will dirty the root even if the order didn't actually change
                    }
                    entityHierarchyChangeStream.EndForEachIndex();
                }

                var dirtyRootsToSort = new NativeArray<EntityAndNodeIndex>(dirtyRootsSet.Count, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                {
                    int i = 0;
                    foreach (var root in dirtyRootsSet)
                    {
                        dirtyRootsToSort[i] = new EntityAndNodeIndex { entity = root, index = hierarchy.GetDeterministicIndex(root) };
                        i++;
                    }
                }
                dirtyRootsToSort.Sort();
                dirtyRootsList.Capacity = dirtyRootsToSort.Length;
                dirtyRootHasChildrenList.Resize(dirtyRootsToSort.Length, NativeArrayOptions.ClearMemory);
                {
                    int i = 0;
                    foreach (var root in dirtyRootsToSort)
                    {
                        if (hierarchy.HasChildren(root.entity))
                        {
                            dirtyRootsList.Add(root.entity);
                            dirtyRootHasChildrenList.Set(i, true);
                        }
                        i++;
                    }
                }
            }

            struct EntityAndNodeIndex : IComparable<EntityAndNodeIndex>
            {
                public Entity entity;
                public int    index;

                public int CompareTo(EntityAndNodeIndex other) => this.index.CompareTo(other.index);
            }
        }

        [BurstCompile]
        struct RebuildHierarchiesJob : IJobFor
        {
            [ReadOnly] public Hierarchy                                                  hierarchy;
            [ReadOnly] public NativeArray<Entity>                                        dirtyRoots;
            [ReadOnly] public ComponentLookup<TransformAuthoring>                        transformAuthoringLookup;
            [ReadOnly] public ComponentLookup<MergedInheritanceFlags>                    inheritanceFlagsLookup;
            [ReadOnly] public ComponentLookup<BakedLocalTransformOverride>               bakedLocalTransformOverrideLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<WorldTransform> worldTransformLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<RootReference>  rootReferenceLookup;
            [NativeDisableParallelForRestriction] public BufferLookup<EntityInHierarchy> entityInHierarchyLookup;

            UnsafeQueue<EnqueuedChild> queue;
            UnsafeList<TransformQvvs>  computedTransforms;
            UnsafeList<EnqueuedChild>  childrenToSort;

            public void Execute(int index)
            {
                if (!queue.IsCreated)
                {
                    queue              = new UnsafeQueue<EnqueuedChild>(Allocator.Temp);
                    computedTransforms = new UnsafeList<TransformQvvs>(32, Allocator.Temp);
                    childrenToSort     = new UnsafeList<EnqueuedChild>(32, Allocator.Temp);
                }
                queue.Clear();
                computedTransforms.Clear();

                var root   = dirtyRoots[index];
                var buffer = entityInHierarchyLookup[root];
                buffer.Clear();
                hierarchy.GetOrderAndChildren(root, out _, out var childrenOfRoot);
                buffer.Add(new EntityInHierarchy
                {
                    m_childCount          = childrenOfRoot.Length,
                    m_descendantEntity    = root,
                    m_parentIndex         = -1,
                    m_firstChildIndex     = 1,
                    m_flags               = default,
                    m_localPosition       = default,
                    m_localScale          = 1f,
                    m_tickedLocalPosition = default,
                    m_tickedLocalScale    = 1f,
                });
                computedTransforms.Add(worldTransformLookup[root].worldTransform);
                EnqueueChildren(childrenOfRoot, 0);
                while (queue.TryDequeue(out var current))
                {
                    TransformQvs localTransform = TransformQvs.identity;
                    inheritanceFlagsLookup.TryGetComponent(current.child, out var flags);
                    if (flags.flags.HasCopyParent())
                        computedTransforms.Add(computedTransforms[current.parentIndex]);
                    else
                        computedTransforms.Add(ComputeWorldTransform(current.child, buffer[current.parentIndex].entity, computedTransforms[current.parentIndex],
                                                                     out localTransform));
                    var thisIndex = buffer.Length;

                    buffer.Add(new EntityInHierarchy
                    {
                        m_childCount          = current.children.Length,
                        m_descendantEntity    = current.child,
                        m_parentIndex         = current.parentIndex,
                        m_firstChildIndex     = -1,
                        m_flags               = flags.flags,
                        m_localPosition       = localTransform.position,
                        m_localScale          = localTransform.scale,
                        m_tickedLocalPosition = localTransform.position,
                        m_tickedLocalScale    = localTransform.scale,
                    });
                    ref var parentInHierarchy = ref buffer.ElementAt(current.parentIndex);
                    if (parentInHierarchy.firstChildIndex < 0)
                        parentInHierarchy.m_firstChildIndex = thisIndex;
                    EnqueueChildren(current.children, thisIndex);
                }

                int previousOffset = childrenOfRoot.Length + 1;
                for (int i = 1; i < computedTransforms.Length; i++)
                {
                    ref var element = ref buffer.ElementAt(i);
                    if (element.firstChildIndex < 0)
                        element.m_firstChildIndex = previousOffset;
                    else
                        previousOffset                  = element.firstChildIndex + element.childCount;
                    rootReferenceLookup[element.entity] = new RootReference
                    {
                        m_indexInHierarchy = i,
                        m_rootEntity       = root
                    };
                    worldTransformLookup[element.entity] = new WorldTransform { worldTransform = computedTransforms[i] };
                }
            }

            void EnqueueChildren(UnsafeList<Entity> children, int parentIndex)
            {
                childrenToSort.Clear();
                int nextUndefinedOrder = 0x08000000;
                for (int i = 0; i < children.Length; i++)
                {
                    var child = children[i];
                    hierarchy.GetOrderAndChildren(child, out var order, out var childrenOfChild);
                    if (order < 0)
                    {
                        order = nextUndefinedOrder;
                        nextUndefinedOrder++;
                    }
                    childrenToSort.Add(new EnqueuedChild
                    {
                        child       = child,
                        parentIndex = parentIndex,
                        order       = order,
                        children    = childrenOfChild
                    });
                }
                childrenToSort.Sort(new ChildSort());
                foreach (var child in childrenToSort)
                    queue.Enqueue(child);
            }

            TransformQvvs ComputeWorldTransform(Entity child, Entity parent, TransformQvvs parentTransform, out TransformQvs localTransform)
            {
                if (bakedLocalTransformOverrideLookup.TryGetComponent(child, out var overrideLocal))
                {
                    localTransform = new TransformQvs(overrideLocal.localTransform.position, overrideLocal.localTransform.rotation, overrideLocal.localTransform.scale);
                    return qvvs.mulclean(parentTransform, overrideLocal.localTransform);
                }

                var transformAuthoring = transformAuthoringLookup[child];
                TransformBakeUtils.GetScaleAndStretch(transformAuthoring.LocalScale, out var scale, out var stretch);
                var workingTransform = new TransformQvvs(transformAuthoring.LocalPosition, transformAuthoring.LocalRotation, scale, stretch);
                if (parent == transformAuthoring.AuthoringParent)
                {
                    localTransform = new TransformQvs(transformAuthoring.LocalPosition, transformAuthoring.LocalRotation, scale);
                    return qvvs.mulclean(parentTransform, workingTransform);
                }

                var nextParent = transformAuthoring.AuthoringParent;
                while (nextParent != parent)
                {
                    var intermediateAuthoring = transformAuthoringLookup[nextParent];
                    TransformBakeUtils.GetScaleAndStretch(intermediateAuthoring.LocalScale, out var interScale, out var interStretch);
                    var interTransform = new TransformQvvs(intermediateAuthoring.LocalPosition, intermediateAuthoring.LocalRotation, interScale, interStretch);
                    workingTransform   = qvvs.mulclean(interTransform, workingTransform);
                    nextParent         = transformAuthoring.AuthoringParent;
                }
                localTransform = new TransformQvs(workingTransform.position, workingTransform.rotation, workingTransform.scale);
                return qvvs.mulclean(parentTransform, workingTransform);
            }

            struct EnqueuedChild
            {
                public Entity             child;
                public int                parentIndex;
                public int                order;
                public UnsafeList<Entity> children;
            }

            struct ChildSort : IComparer<EnqueuedChild>
            {
                public int Compare(EnqueuedChild x, EnqueuedChild y) => x.order.CompareTo(y.order);
            }
        }
    }
}
#endif

