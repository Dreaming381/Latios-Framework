#if !LATIOS_TRANSFORMS_UNITY
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Latios.Unsafe;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Transforms
{
    internal static class TreeChangeInstantiate
    {
        static readonly Unity.Profiling.ProfilerMarker specializedMarker = new Unity.Profiling.ProfilerMarker("Specialized");

        public static void AddChildren(ref IInstantiateCommand.Context context, bool hasLocalTransformsToWrite)
        {
            var entities        = context.entities;
            var em              = context.entityManager;
            var childWorkStates = new NativeArray<ChildWorkState>(entities.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            if (hasLocalTransformsToWrite)
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    var command        = context.ReadCommand<ParentAndLocalTransformCommand>(i);
                    childWorkStates[i] = new ChildWorkState
                    {
                        child                = entities[i],
                        parent               = command.parent,
                        flags                = command.inheritanceFlags,
                        options              = command.options,
                        localTransform       = command.newLocalTransform,
                        tickedLocalTransform = command.newLocalTransform,
                    };
                }
            }
            else
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    var command        = context.ReadCommand<ParentCommand>(i);
                    childWorkStates[i] = new ChildWorkState
                    {
                        child   = entities[i],
                        parent  = command.parent,
                        flags   = command.inheritanceFlags,
                        options = command.options
                    };
                }
            }

            var batchedAddSetsStream = new NativeStream(childWorkStates.Length, Allocator.TempJob);
            var classifyJh           = new ClassifyJob
            {
                children              = childWorkStates,
                batchAddSetsStream    = batchedAddSetsStream.AsWriter(),
                esil                  = em.GetEntityStorageInfoLookup(),
                transformLookup       = em.GetComponentLookup<WorldTransform>(true),
                tickedTransformLookup = em.GetComponentLookup<TickedWorldTransform>(true),
                rootReferenceLookup   = em.GetComponentLookup<RootReference>(true),
                hierarchyLookup       = em.GetBufferLookup<EntityInHierarchy>(true),
                cleanupLookup         = em.GetBufferLookup<EntityInHierarchyCleanup>(true),
                legLookup             = em.GetBufferLookup<LinkedEntityGroup>(true),
                hasLocalTransforms    = hasLocalTransformsToWrite
            }.ScheduleParallel(childWorkStates.Length, 32, default);
            var batchedAddSets = new NativeList<BatchedAddSet>(Allocator.TempJob);
            var sortChildAddJh = new SortAndMergeBatchAddSetsJob
            {
                batchAddSetsStream = batchedAddSetsStream,
                outputBatchAddSets = batchedAddSets
            }.Schedule(classifyJh);
            var rootWorkStates = new NativeList<RootWorkState>(Allocator.TempJob);
            var childIndices   = new NativeArray<int>(childWorkStates.Length, Allocator.TempJob, NativeArrayOptions.ClearMemory);
            var groupRootsJh   = new GroupRootsJob
            {
                childIndices    = childIndices,
                rootWorkStates  = rootWorkStates,
                childWorkStates = childWorkStates
            }.Schedule(classifyJh);
            sortChildAddJh.Complete();
            var entityCacheList = new NativeList<Entity>(batchedAddSets.Length, Allocator.Temp);
            for (int i = 0; i < batchedAddSets.Length; )
            {
                entityCacheList.Clear();
                var addSet = batchedAddSets[i].addSet;
                var mask   = addSet.changeFlags;
                entityCacheList.Add(addSet.entity);
                for (int subCount = 1; i + subCount < batchedAddSets.Length; subCount++)
                {
                    var nextAddSet = batchedAddSets[i + subCount].addSet;
                    if (nextAddSet.changeFlags != mask)
                        break;
                    entityCacheList.Add(nextAddSet.entity);
                }
                TreeKernels.AddComponentsBatched(em, addSet, entityCacheList.AsArray());
                i += entityCacheList.Length;
            }
            var ecb       = new EntityCommandBuffer(Allocator.TempJob);
            var buffersJh = new ProcessBuffersJob
            {
                batchAddSetStream          = batchedAddSetsStream.AsReader(),
                childIndices               = childIndices,
                childWorkStates            = childWorkStates,
                cleanupHandle              = em.GetBufferTypeHandle<EntityInHierarchyCleanup>(false),
                esil                       = em.GetEntityStorageInfoLookup(),
                hierarchyHandle            = em.GetBufferTypeHandle<EntityInHierarchy>(false),
                legHandle                  = em.GetBufferTypeHandle<LinkedEntityGroup>(false),
                rootWorkStates             = rootWorkStates.AsDeferredJobArray(),
                tickedWorldTransformLookup = em.GetComponentLookup<TickedWorldTransform>(false),
                worldTransformLookup       = em.GetComponentLookup<WorldTransform>(false),
                rootReferenceLookup        = em.GetComponentLookup<RootReference>(false),
                ecb                        = ecb.AsParallelWriter()
            }.Schedule(rootWorkStates, 4, groupRootsJh);
            buffersJh.Complete();

            specializedMarker.Begin();
            ecb.Playback(em);
            ecb.Dispose();

            for (int i = 0; i < childWorkStates.Length; i++)
            {
                var child = childWorkStates[i];
                if (child.parentIsDead)
                {
                    context.RequestDestroyEntity(child.child);
                    continue;
                }
            }
            specializedMarker.End();

            childWorkStates.Dispose();
            batchedAddSetsStream.Dispose();
            batchedAddSets.Dispose();
            rootWorkStates.Dispose();
            childIndices.Dispose();
        }

        #region Types
        struct ChildWorkState
        {
            public Entity                         parent;
            public Entity                         child;
            public InheritanceFlags               flags;
            public SetParentOptions               options;
            public bool                           parentIsDead;
            public bool                           addedLeg;
            public TransformQvvs                  localTransform;
            public TransformQvvs                  tickedLocalTransform;
            public TreeKernels.TreeClassification parentClassification;
            public TreeKernels.TreeClassification childClassification;
            public TreeKernels.ComponentAddSet    childAddSet;
        }

        struct RootWorkState
        {
            public int childStart;
            public int childCount;
        }

        struct BatchedAddSet : IComparable<BatchedAddSet>
        {
            public TreeKernels.ComponentAddSet addSet;
            public int                         chunkOrder;
            public int                         indexInChunk;

            public int CompareTo(BatchedAddSet other)
            {
                var result = addSet.changeFlags.CompareTo(other.addSet.changeFlags);
                if (result == 0)
                {
                    result = chunkOrder.CompareTo(other.chunkOrder);
                    if (result == 0)
                        result = indexInChunk.CompareTo(other.indexInChunk);
                }
                return result;
            }
        }
        #endregion

        #region Jobs
        [BurstCompile]
        struct ClassifyJob : IJobFor
        {
            public NativeArray<ChildWorkState> children;
            public NativeStream.Writer         batchAddSetsStream;

            [ReadOnly] public ComponentLookup<WorldTransform>        transformLookup;
            [ReadOnly] public ComponentLookup<TickedWorldTransform>  tickedTransformLookup;
            [ReadOnly] public EntityStorageInfoLookup                esil;
            [ReadOnly] public ComponentLookup<RootReference>         rootReferenceLookup;
            [ReadOnly] public BufferLookup<EntityInHierarchy>        hierarchyLookup;
            [ReadOnly] public BufferLookup<EntityInHierarchyCleanup> cleanupLookup;
            [ReadOnly] public BufferLookup<LinkedEntityGroup>        legLookup;

            public bool hasLocalTransforms;

            HasChecker<TickedEntityTag>    tickedEntityChecker;
            HasChecker<LiveBakedTag>       liveBakedChecker;
            HasChecker<LiveAddedParentTag> liveAddedParentChecker;
            HasChecker<LinkedEntityGroup>  legChecker;

            public void Execute(int i)
            {
                var workState          = children[i];
                workState.parentIsDead = !esil.IsAlive(workState.parent);
                if (workState.parentIsDead)
                {
                    children[i] = new ChildWorkState { parentIsDead = true };
                    return;
                }

                batchAddSetsStream.BeginForEachIndex(i);

                bool hadNormal = transformLookup.TryGetComponent(workState.child, out var worldTransform);
                bool hadTicked = tickedTransformLookup.TryGetComponent(workState.child, out var tickedTransform);
                if (!hasLocalTransforms)
                {
                    workState.localTransform       = hadNormal ? worldTransform.worldTransform : TransformQvvs.identity;
                    workState.tickedLocalTransform = hadTicked ? tickedTransform.worldTransform : workState.localTransform;
                    if (hadTicked && !hadNormal)
                        workState.localTransform = workState.tickedLocalTransform;
                }

                workState.childClassification  = TreeKernels.ClassifyAlive(ref rootReferenceLookup, ref hierarchyLookup, ref cleanupLookup, workState.child);
                workState.parentClassification = TreeKernels.ClassifyAlive(ref rootReferenceLookup, ref hierarchyLookup, ref cleanupLookup, workState.parent);
                CheckDeadRootLegRules(in workState.parentClassification, workState.options);
                CheckNewEntitiesAreSupported(workState.child, in workState.childClassification);

                // If we pass the safety checks, then we know we can ignore the children of the child.
                if (workState.childClassification.role == TreeKernels.TreeClassification.TreeRole.InternalWithChildren)
                    workState.childClassification.role = TreeKernels.TreeClassification.TreeRole.InternalNoChildren;

                var childStorageInfo  = esil[workState.child];
                workState.childAddSet = GetChildComponentsToAdd(workState.child, workState.childClassification.role, workState.flags, hadNormal, hadTicked);
                batchAddSetsStream.Write(new BatchedAddSet
                {
                    addSet       = workState.childAddSet,
                    chunkOrder   = childStorageInfo.Chunk.GetHashCode(),
                    indexInChunk = childStorageInfo.IndexInChunk,
                });

                if (workState.parentClassification.role == TreeKernels.TreeClassification.TreeRole.Solo ||
                    workState.parentClassification.role == TreeKernels.TreeClassification.TreeRole.Root)
                {
                    var addSet         = GetParentComponentsToAdd(workState.parent, workState.parentClassification.role, workState.childAddSet, workState.options);
                    workState.addedLeg = addSet.addSet.linkedEntityGroup;
                    batchAddSetsStream.Write(addSet);
                }
                else
                {
                    var hierarchy =
                        (workState.parentClassification.isRootAlive ? hierarchyLookup[workState.parentClassification.root] : cleanupLookup[workState.parentClassification.root].
                         Reinterpret<EntityInHierarchy>()).AsNativeArray();
                    GetAncestorComponentsToAdd(hierarchy, workState.parentClassification, workState.childAddSet, workState.options, out workState.addedLeg);
                }

                children[i] = workState;

                batchAddSetsStream.EndForEachIndex();
            }

            TreeKernels.ComponentAddSet GetChildComponentsToAdd(Entity child,
                                                                TreeKernels.TreeClassification.TreeRole role,
                                                                InheritanceFlags flags,
                                                                bool hasWorldTransform,
                                                                bool hasTickedTransform)
            {
                TreeKernels.ComponentAddSet addSet = default;
                addSet.entity                      = child;
                if (role == TreeKernels.TreeClassification.TreeRole.Solo || role == TreeKernels.TreeClassification.TreeRole.Root)
                    addSet.rootReference = true;

                var childChunk = esil[child].Chunk;
#if UNITY_EDITOR
                if (liveBakedChecker[childChunk] && !liveAddedParentChecker[childChunk])
                    addSet.liveAddedParent = true;
#endif
                var isTicked = tickedEntityChecker[childChunk];
                var isNormal = hasWorldTransform;
                if (!isTicked && !isNormal)
                {
                    addSet.isNormal            = true;
                    addSet.setNormalToIdentity = true;
                    addSet.worldTransform      = true;
                }
                else
                {
                    if (isTicked)
                    {
                        addSet.isTicked             = true;
                        addSet.tickedWorldTransform = !hasTickedTransform;
                        if (addSet.tickedWorldTransform && isNormal)
                            addSet.copyNormalToTicked = true;
                        else if (addSet.tickedWorldTransform)
                            addSet.setTickedToIdentity = true;
                    }
                    if (isNormal)
                    {
                        addSet.isNormal = true;
                    }
                }
                return addSet;
            }

            BatchedAddSet GetParentComponentsToAdd(Entity parent,
                                                   TreeKernels.TreeClassification.TreeRole role,
                                                   TreeKernels.ComponentAddSet childAddSet,
                                                   SetParentOptions options,
                                                   bool considerTransforms = true)
            {
                TreeKernels.ComponentAddSet addSet = default;
                addSet.entity                      = parent;
                addSet.isTicked                    = childAddSet.isTicked;
                addSet.isNormal                    = childAddSet.isNormal;
                if (role == TreeKernels.TreeClassification.TreeRole.Solo)
                    addSet.entityInHierarchy = true;

                if (considerTransforms)
                {
                    bool hasNormal   = transformLookup.HasComponent(parent);
                    bool hasTicked   = tickedTransformLookup.HasComponent(parent);
                    addSet.isNormal |= hasNormal;
                    addSet.isTicked |= hasTicked;
                    if (childAddSet.isNormal && !hasNormal)
                    {
                        addSet.worldTransform = true;
                        if (hasTicked)
                            addSet.copyTickedToNormal = true;
                        else
                            addSet.setNormalToIdentity = true;
                    }
                    if (childAddSet.isTicked && !hasTicked)
                    {
                        addSet.tickedWorldTransform = true;
                        if (hasNormal)
                            addSet.copyNormalToTicked = true;
                        else
                            addSet.setTickedToIdentity = true;
                    }
                }

                var esi = esil[parent];

                if (role == TreeKernels.TreeClassification.TreeRole.Solo || role == TreeKernels.TreeClassification.TreeRole.Root)
                {
                    if (options != SetParentOptions.IgnoreLinkedEntityGroup && !legChecker[esi.Chunk])
                        addSet.linkedEntityGroup = true;
                    if (options == SetParentOptions.IgnoreLinkedEntityGroup)
                        addSet.entityInHierarchyCleanup = true;
                }
                return new BatchedAddSet
                {
                    addSet       = addSet,
                    chunkOrder   = esi.Chunk.GetHashCode(),
                    indexInChunk = esi.IndexInChunk,
                };
            }

            void GetAncestorComponentsToAdd(ReadOnlySpan<EntityInHierarchy> hierarchy,
                                            TreeKernels.TreeClassification parentClassification,
                                            TreeKernels.ComponentAddSet childAddSet,
                                            SetParentOptions options,
                                            out bool addedLeg)
            {
                var  parentAddSet         = GetParentComponentsToAdd(hierarchy[parentClassification.indexInHierarchy].entity, parentClassification.role, childAddSet, options);
                bool allTransformsPresent = false;
                if (!parentAddSet.addSet.noChange)
                {
                    batchAddSetsStream.Write(parentAddSet);

                    for (int index = hierarchy[parentClassification.indexInHierarchy].parentIndex; index > 0; index = hierarchy[index].parentIndex)
                    {
                        var newAddSet = GetParentComponentsToAdd(hierarchy[index].entity, TreeKernels.TreeClassification.TreeRole.InternalWithChildren, childAddSet, options);
                        if (newAddSet.addSet.noChange)
                        {
                            allTransformsPresent = true;
                            break;
                        }
                        if (hierarchy[index].m_flags.HasCopyParent())
                            newAddSet.addSet.isCopyParent = true;
                        newAddSet.addSet.indexInHierarchy = index;
                        newAddSet.addSet.parent           = hierarchy[hierarchy[index].parentIndex].entity;
                        batchAddSetsStream.Write(newAddSet);
                    }
                }

                var rootAddSet = GetParentComponentsToAdd(parentClassification.root, TreeKernels.TreeClassification.TreeRole.Root, childAddSet, options, !allTransformsPresent);
                addedLeg       = rootAddSet.addSet.linkedEntityGroup;
                batchAddSetsStream.Write(rootAddSet);
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void CheckDeadRootLegRules(in TreeKernels.TreeClassification parentClassification, SetParentOptions options)
            {
                if (options != SetParentOptions.IgnoreLinkedEntityGroup &&
                    (parentClassification.role == TreeKernels.TreeClassification.TreeRole.InternalNoChildren ||
                     parentClassification.role == TreeKernels.TreeClassification.TreeRole.InternalWithChildren))
                {
                    if (!esil.IsAlive(parentClassification.root))
                        throw new InvalidOperationException(
                            $"Cannot add LinkedEntityGroup to a new hierarchy whose root has been destroyed. Root: {parentClassification.root.ToFixedString()}");
                }
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void CheckNewEntitiesAreSupported(Entity child, in TreeKernels.TreeClassification childClassification)
            {
                if (!legLookup.TryGetBuffer(child, out var legBuffer))
                    return;
                var leg = legBuffer.Reinterpret<Entity>().AsNativeArray();
                if (leg.Length < 2)
                    return;
                if (childClassification.role == TreeKernels.TreeClassification.TreeRole.InternalWithChildren)
                {
                    for (int i = 1; i < leg.Length; i++)
                    {
                        var e = leg[i];
                        if (rootReferenceLookup.HasComponent(e))
                        {
                            throw new NotSupportedException(
                                "An instantiated entity with a RootReference component based on an entity with children has other entities in its LinkedEntityGroup with RootReference components not referencing the instantiated entity. This can happen if you instantiate a child entity in a hierarchy which itself has children. This is not supported at this time.");
                        }
                    }
                }
                else if (childClassification.role == TreeKernels.TreeClassification.TreeRole.Solo)
                {
                    for (int i = 1; i < leg.Length; i++)
                    {
                        var e = leg[i];
                        if (rootReferenceLookup.HasComponent(e))
                        {
                            throw new NotSupportedException(
                                "An instantiated entity that is not a hierarchy root nor has a RootReference component has other entities in its LinkedEntityGroup with RootReference components not referencing the instantiated entity. This usually indicates mismanagement of the LinkedEntityGroup buffer. This is not supported at this time.");
                        }
                    }
                }
                else if (childClassification.role == TreeKernels.TreeClassification.TreeRole.InternalNoChildren)
                {
                    for (int i = 1; i < leg.Length; i++)
                    {
                        var e = leg[i];
                        if (rootReferenceLookup.HasComponent(e))
                        {
                            throw new NotSupportedException(
                                "An instantiated entity with a RootReference component based on an entity without children has other entities in its LinkedEntityGroup with RootReference components not referencing the instantiated entity. This is not supported at this time.");
                        }
                    }
                }
                else if (childClassification.role == TreeKernels.TreeClassification.TreeRole.Root)
                {
                    for (int i = 1; i < leg.Length; i++)
                    {
                        var e = leg[i];
                        if (rootReferenceLookup.TryGetComponent(e, out var rootRef) && rootRef.rootEntity != child)
                        {
                            throw new NotSupportedException(
                                "An instantiated root entity has other entities in its LinkedEntityGroup with RootReference components not referencing the instantiated entity. This is not supported at this time.");
                        }
                    }
                }
            }
        }

        [BurstCompile]
        struct SortAndMergeBatchAddSetsJob : IJob
        {
            [ReadOnly] public NativeStream   batchAddSetsStream;
            public NativeList<BatchedAddSet> outputBatchAddSets;

            static readonly Unity.Profiling.ProfilerMarker kReadStream   = new Unity.Profiling.ProfilerMarker("Read stream");
            static readonly Unity.Profiling.ProfilerMarker kHashChunks   = new Unity.Profiling.ProfilerMarker("Hash chunks");
            static readonly Unity.Profiling.ProfilerMarker kSortEntities = new Unity.Profiling.ProfilerMarker("Sort entities");
            static readonly Unity.Profiling.ProfilerMarker kMergeFlags   = new Unity.Profiling.ProfilerMarker("Merge flags");
            static readonly Unity.Profiling.ProfilerMarker kSortSets     = new Unity.Profiling.ProfilerMarker("Sort sets");

            public void Execute()
            {
                kReadStream.Begin();
                var addSets = batchAddSetsStream.ToNativeArray<BatchedAddSet>(Allocator.Temp);
                kReadStream.End();
                kHashChunks.Begin();
                var hashToOrderMap = new UnsafeHashMap<int, int>(addSets.Length, Allocator.Temp);
                for (int i = 0; i < addSets.Length; i++)
                {
                    var addSet = addSets[i];
                    if (addSet.addSet.entity == Entity.Null)
                        addSet.chunkOrder = -1;
                    else if (hashToOrderMap.TryGetValue(addSet.chunkOrder, out var order))
                        addSet.chunkOrder = order;
                    else
                    {
                        var hash          = addSet.chunkOrder;
                        addSet.chunkOrder = hashToOrderMap.Count;
                        hashToOrderMap.Add(hash, addSet.chunkOrder);
                    }
                    addSets[i] = addSet;
                }
                kHashChunks.End();
                kSortEntities.Begin();
                addSets.Sort(new EntitySorter());
                kSortEntities.End();

                // Matching entities should now be adjacent in memory. Merge the flags.
                kMergeFlags.Begin();
                outputBatchAddSets.Capacity = addSets.Length;
                for (int i = 0; i < addSets.Length; )
                {
                    var baseAddSet = addSets[i];
                    for (i = i + 1; i < addSets.Length; i++)
                    {
                        var addSet = addSets[i];
                        if (addSet.chunkOrder != baseAddSet.chunkOrder || addSet.indexInChunk != baseAddSet.indexInChunk)
                            break;

                        baseAddSet.addSet.packed |= addSet.addSet.packed;
                    }
                    outputBatchAddSets.Add(baseAddSet);
                }
                kMergeFlags.End();

                kSortSets.Begin();
                outputBatchAddSets.Sort();
                kSortSets.End();
            }

            struct EntitySorter : IComparer<BatchedAddSet>
            {
                public int Compare(BatchedAddSet x, BatchedAddSet y)
                {
                    var result = x.chunkOrder.CompareTo(y.chunkOrder);
                    if (result == 0)
                        result = x.indexInChunk.CompareTo(y.indexInChunk);
                    return result;
                }
            }
        }

        [BurstCompile]
        struct GroupRootsJob : IJob
        {
            public NativeArray<int>                       childIndices;
            public NativeList<RootWorkState>              rootWorkStates;
            [ReadOnly] public NativeArray<ChildWorkState> childWorkStates;

            public void Execute()
            {
                rootWorkStates.Capacity = childWorkStates.Length;
                var rootToIndexMap      = new UnsafeHashMap<Entity, int>(childWorkStates.Length, Allocator.Temp);
                var rootToIndexArray    = new NativeArray<int>(childWorkStates.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                for (int i = 0; i < childWorkStates.Length; i++)
                {
                    var    parentClassification = childWorkStates[i].parentClassification;
                    Entity root;
                    if (parentClassification.role == TreeKernels.TreeClassification.TreeRole.Solo || parentClassification.role == TreeKernels.TreeClassification.TreeRole.Root)
                        root = childWorkStates[i].parent;
                    else
                        root = parentClassification.root;

                    if (!rootToIndexMap.TryGetValue(root, out var index))
                    {
                        index                                             = rootWorkStates.Length;
                        rootWorkStates.Add(new RootWorkState { childStart = 0, childCount = 1 });
                        rootToIndexMap.Add(root, index);
                    }
                    else
                        rootWorkStates.ElementAt(index).childCount++;
                    rootToIndexArray[i] = index;
                }

                // Prefix sum
                int running = 0;
                for (int i = 0; i < rootWorkStates.Length; i++)
                {
                    ref var state     = ref rootWorkStates.ElementAt(i);
                    state.childStart  = running;
                    running          += state.childCount;
                    state.childCount  = 0;
                }

                // Write output
                for (int i = 0; i < rootToIndexArray.Length; i++)
                {
                    ref var state     = ref rootWorkStates.ElementAt(rootToIndexArray[i]);
                    var     dst       = state.childStart + state.childCount;
                    childIndices[dst] = i;
                    state.childCount++;
                }
            }
        }

        [BurstCompile]
        struct ProcessBuffersJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<int>                                       childIndices;
            [ReadOnly] public NativeStream.Reader                                    batchAddSetStream;
            public NativeArray<RootWorkState>                                        rootWorkStates;
            [NativeDisableParallelForRestriction] public NativeArray<ChildWorkState> childWorkStates;

            [ReadOnly] public EntityStorageInfoLookup                                          esil;
            public BufferTypeHandle<EntityInHierarchy>                                         hierarchyHandle;
            public BufferTypeHandle<EntityInHierarchyCleanup>                                  cleanupHandle;
            public BufferTypeHandle<LinkedEntityGroup>                                         legHandle;
            [NativeDisableParallelForRestriction] public ComponentLookup<WorldTransform>       worldTransformLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<TickedWorldTransform> tickedWorldTransformLookup;
            [NativeDisableParallelForRestriction] public ComponentLookup<RootReference>        rootReferenceLookup;

            public EntityCommandBuffer.ParallelWriter ecb;

            public unsafe void Execute(int rootIndex)
            {
                var rootWorkState       = rootWorkStates[rootIndex];
                var rootChildrenIndices = childIndices.GetSubArray(rootWorkState.childStart, rootWorkState.childCount);

                Entity root;
                bool   rootIsAlive;
                var    firstClassification = childWorkStates[rootChildrenIndices[0]].parentClassification;
                if (firstClassification.role == TreeKernels.TreeClassification.TreeRole.Solo || firstClassification.role == TreeKernels.TreeClassification.TreeRole.Root)
                {
                    root        = childWorkStates[rootChildrenIndices[0]].parent;
                    rootIsAlive = true;
                    // If the parent was destroyed, skip.
                    if (root == Entity.Null)
                        return;
                }
                else
                {
                    root        = firstClassification.root;
                    rootIsAlive = firstClassification.isRootAlive;
                }

                var                              esi = esil[root];
                DynamicBuffer<EntityInHierarchy> hierarchy;
                if (rootIsAlive)
                    hierarchy = esi.Chunk.GetBufferAccessorRW(ref hierarchyHandle)[esi.IndexInChunk];
                else
                    hierarchy = esi.Chunk.GetBufferAccessorRW(ref cleanupHandle)[esi.IndexInChunk].Reinterpret<EntityInHierarchy>();

                var tsa = ThreadStackAllocator.GetAllocator();
                foreach (var childIndex in rootChildrenIndices)
                {
                    int elementCount    = batchAddSetStream.BeginForEachIndex(childIndex);
                    var ancestryAddSets = tsa.AllocateAsSpan<TreeKernels.ComponentAddSet>(elementCount);
                    for (int i = 0; i < elementCount; i++)
                        ancestryAddSets[i] = batchAddSetStream.Read<BatchedAddSet>().addSet;
                    // We need to apply this backwards since the order is stored leaf-to-root and we want to apply root-to-leaf
                    for (int i = elementCount - 1; i >= 0; i--)
                        ApplyAddComponentsBatchedPostProcess(ancestryAddSets[i]);

                    TreeKernels.UpdateLocalTransformsOfNewAncestorComponents(ancestryAddSets, hierarchy.AsNativeArray());
                    batchAddSetStream.EndForEachIndex();
                }

                var oldHierarchy = TreeKernels.CopyHierarchyEntities(ref tsa, hierarchy.AsNativeArray());
                var rootLeg      = esi.Chunk.Has(ref legHandle) ? esi.Chunk.GetBufferAccessorRW(ref legHandle)[esi.IndexInChunk] : default;
                TreeKernels.RemoveDeadDescendantsFromHierarchyAndLeg(ref tsa, ref hierarchy, ref rootLeg, esil, ref worldTransformLookup, ref tickedWorldTransformLookup);
                if (hierarchy.Length == 0)
                {
                    hierarchy.Add(new EntityInHierarchy
                    {
                        m_childCount          = 0,
                        m_descendantEntity    = root,
                        m_firstChildIndex     = 1,
                        m_flags               = InheritanceFlags.Normal,
                        m_parentIndex         = -1,
                        m_localPosition       = default,
                        m_localScale          = 1f,
                        m_tickedLocalPosition = default,
                        m_tickedLocalScale    = 1f,
                    });
                }
                bool hadEnoughLegBefore = rootLeg.Length >= 2;
                foreach (var childIndex in rootChildrenIndices)
                {
                    var childWorkState = childWorkStates[childIndex];
                    var parentIndex    = TreeKernels.FindEntityAfterChange(hierarchy.AsNativeArray(), childWorkState.parent, childWorkState.parentClassification.indexInHierarchy);
                    if (childWorkState.childClassification.role == TreeKernels.TreeClassification.TreeRole.Solo ||
                        childWorkState.childClassification.role == TreeKernels.TreeClassification.TreeRole.InternalNoChildren)
                    {
                        TreeKernels.InsertSoloEntityIntoHierarchy(ref hierarchy, parentIndex, childWorkState.child, childWorkState.flags);
                        if (childWorkState.options != SetParentOptions.IgnoreLinkedEntityGroup)
                        {
                            TreeKernels.AddEntityToLeg(ref rootLeg, childWorkState.child);
                            if (childWorkState.options == SetParentOptions.TransferLinkedEntityGroup)
                            {
                                var childEsi = esil[childWorkState.child];
                                if (childEsi.Chunk.Has(ref legHandle))
                                {
                                    var childLeg = childEsi.Chunk.GetBufferAccessorRO(ref legHandle)[childEsi.IndexInChunk];
                                    if (childLeg.Length < 2)
                                        ecb.RemoveComponent<LinkedEntityGroup>(rootIndex, childWorkState.child);
                                }
                            }
                        }
                    }
                    else if (childWorkState.childClassification.role == TreeKernels.TreeClassification.TreeRole.Root)
                    {
                        var  childEsi       = esil[childWorkState.child];
                        var  childHierarchy = childEsi.Chunk.GetBufferAccessorRO(ref hierarchyHandle)[childEsi.IndexInChunk];
                        bool hasLeg         = childEsi.Chunk.Has(ref legHandle);
                        var  childLeg       = hasLeg ? childEsi.Chunk.GetBufferAccessorRW(ref legHandle)[childEsi.IndexInChunk] : default;
                        if (hasLeg)
                            TreeKernels.RemoveDeadEntitiesFromLeg(ref childLeg, esil);
                        TreeKernels.RemoveDeadAndUnreferencedDescendantsFromHierarchy(ref tsa,
                                                                                      ref childHierarchy,
                                                                                      esil,
                                                                                      ref worldTransformLookup,
                                                                                      ref tickedWorldTransformLookup,
                                                                                      ref rootReferenceLookup);

                        TreeKernels.InsertSubtreeIntoHierarchy(ref hierarchy, parentIndex, childHierarchy.AsNativeArray(), childWorkState.flags);

                        bool removeLeg = false;
                        if (childWorkState.options != SetParentOptions.IgnoreLinkedEntityGroup)
                        {
                            TreeKernels.AddHierarchyToLeg(ref rootLeg, childHierarchy.AsNativeArray());
                            if (childWorkState.options == SetParentOptions.TransferLinkedEntityGroup && hasLeg)
                            {
                                TreeKernels.RemoveHierarchyEntitiesFromLeg(ref childLeg, childHierarchy.AsNativeArray());
                                removeLeg = childLeg.Length < 2;
                            }
                        }

                        if (removeLeg)
                            ecb.RemoveComponent(rootIndex, childWorkState.child, new TypePack<EntityInHierarchy, EntityInHierarchyCleanup, LinkedEntityGroup>());
                        else
                            ecb.RemoveComponent(rootIndex, childWorkState.child, new TypePack<EntityInHierarchy, EntityInHierarchyCleanup>());
                    }
                    else if (childWorkState.childClassification.root == root)
                    {
                        throw new NotImplementedException();
                        //var childBlueprint = oldHierarchy[childWorkState.childClassification.indexInHierarchy];
                        //var blueprintIndexInHierarchy = TreeKernels.FindEntityAfterChange(hierarchy.AsNativeArray(), childBlueprint, childWorkState.childClassification.indexInHierarchy);
                        //var blueprintHierarchy = TreeKernels.ExtractSubtree(ref tsa, hierarchy.AsNativeArray(), blueprintIndexInHierarchy);
                        //var substituted = tsa.AllocateAsSpan<bool>(blueprintHierarchy.Length);
                        //substituted.Clear();
                        //blueprintHierarchy[0].m_descendantEntity = childWorkState.child;
                        //substituted[0] = true;
                        //
                        //var childEsi = esil[childWorkState.child];
                        //bool hasLeg = childEsi.Chunk.Has(ref legHandle);
                        //var childLeg = hasLeg ? childEsi.Chunk.GetBufferAccessorRW(ref legHandle)[childEsi.IndexInChunk] : default;
                        //if (hasLeg)
                        //{
                        //    TreeKernels.RemoveDeadEntitiesFromLeg(ref childLeg, esil);
                        //    for (int i = 1; i < childLeg.Length; i++)
                        //    {
                        //        var e = childLeg[i].Value;
                        //        if (!rootReferenceLookup.TryGetComponent(e, out var rr))
                        //            continue;
                        //        if (rr.rootEntity != root)
                        //        {
                        //            UnityEngine.Debug.LogError("A child entity was instantiated with a LinkedEntityGroup containing entities from other hierarchies.")
                        //            continue; // Todo: This is an error?
                        //        }
                        //        var eBlueprint = oldHierarchy[rr.indexInHierarchy];
                        //        var substitutionIndex = TreeKernels.FindEntityAfterChange(blueprintHierarchy, eBlueprint, math.max(rr.indexInHierarchy - childWorkState.childClassification.indexInHierarchy, 0));
                        //        if (substitutionIndex == -1)
                        //        {
                        //            // This entity got rearranged in the hierarchy.
                        //        }
                        //    }
                        //}
                    }
                    else if (childWorkState.childClassification.root != Entity.Null)
                    {
                        throw new NotImplementedException();
                    }
                }
                EntityInHierarchy* extraPtr = null;
                if (rootIsAlive && esi.Chunk.Has(ref cleanupHandle))
                {
                    var cleanup = esi.Chunk.GetBufferAccessorRW(ref cleanupHandle)[esi.IndexInChunk];
                    TreeKernels.CopyHierarchyToCleanup(in hierarchy, ref cleanup);
                    extraPtr = (EntityInHierarchy*)cleanup.GetUnsafePtr();
                }
                TreeKernels.UpdateRootReferencesFromDiff(hierarchy.AsNativeArray(), oldHierarchy, ref rootReferenceLookup);

                foreach (var childIndex in rootChildrenIndices)
                {
                    var childWorkState   = childWorkStates[childIndex];
                    var indexInHierarchy = TreeKernels.FindEntityAfterChange(hierarchy.AsNativeArray(), childWorkState.child, 0);
                    var handle           = new EntityInHierarchyHandle
                    {
                        m_hierarchy      = hierarchy.AsNativeArray(),
                        m_extraHierarchy = extraPtr,
                        m_index          = indexInHierarchy,
                    };
                    if (worldTransformLookup.HasComponent(childWorkState.child))
                        TransformTools.SetLocalTransform(handle, childWorkState.localTransform, ref worldTransformLookup, ref esil);
                    if (tickedWorldTransformLookup.HasComponent(childWorkState.child))
                        TransformTools.SetTickedLocalTransform(handle, childWorkState.localTransform, ref tickedWorldTransformLookup, ref esil);
                }

                if (hadEnoughLegBefore && rootLeg.Length < 2)
                    ecb.RemoveComponent<LinkedEntityGroup>(rootIndex, root);

                tsa.Dispose();
            }

            void ApplyAddComponentsBatchedPostProcess(TreeKernels.ComponentAddSet addSet)
            {
                if (addSet.noChange)
                    return;
                if (addSet.copyNormalToTicked)
                    tickedWorldTransformLookup[addSet.entity] = worldTransformLookup[addSet.entity].ToTicked();
                else if (addSet.copyTickedToNormal)
                    worldTransformLookup[addSet.entity] = tickedWorldTransformLookup[addSet.entity].ToUnticked();
                else
                {
                    if (addSet.parent != Entity.Null)
                    {
                        if (addSet.setNormalToIdentity)
                        {
                            var parentTransform = worldTransformLookup[addSet.parent];
                            if (!addSet.isCopyParent)
                                parentTransform.worldTransform.stretch = new float3(1f, 1f, 1f);
                            worldTransformLookup[addSet.entity]        = parentTransform;
                        }
                        if (addSet.setTickedToIdentity)
                        {
                            var parentTransform = tickedWorldTransformLookup[addSet.parent];
                            if (!addSet.isCopyParent)
                                parentTransform.worldTransform.stretch = new float3(1f, 1f, 1f);
                            tickedWorldTransformLookup[addSet.entity]  = parentTransform;
                        }
                    }
                    else
                    {
                        if (addSet.setNormalToIdentity)
                            worldTransformLookup[addSet.entity] = new WorldTransform { worldTransform = TransformQvvs.identity };
                        if (addSet.setTickedToIdentity)
                            tickedWorldTransformLookup[addSet.entity] = new TickedWorldTransform { worldTransform = TransformQvvs.identity };
                    }
                }

                if (addSet.linkedEntityGroup)
                {
                    var esi                               = esil[addSet.entity];
                    var leg                               = esi.Chunk.GetBufferAccessorRW(ref legHandle)[esi.IndexInChunk];
                    leg.Add(new LinkedEntityGroup { Value = addSet.entity });
                }
            }
        }
        #endregion
    }
}
#endif

