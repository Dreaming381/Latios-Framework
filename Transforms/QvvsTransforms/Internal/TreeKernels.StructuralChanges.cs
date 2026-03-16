#if !LATIOS_TRANSFORMS_UNITY
using System;
using Latios.Unsafe;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Transforms
{
    internal static unsafe partial class TreeKernels
    {
        public struct ComponentAddSet
        {
            public Entity entity;
            public Entity parent;
            public int    indexInHierarchy;
            public uint   packed;
            public bool rootReference
            {
                get => Bits.GetBit(packed, 0);
                set => Bits.SetBit(ref packed, 0, value);
            }
            public bool entityInHierarchy
            {
                get => Bits.GetBit(packed, 1);
                set => Bits.SetBit(ref packed, 1, value);
            }
            public bool entityInHierarchyCleanup
            {
                get => Bits.GetBit(packed, 2);
                set => Bits.SetBit(ref packed, 2, value);
            }
            public bool linkedEntityGroup
            {
                get => Bits.GetBit(packed, 3);
                set => Bits.SetBit(ref packed, 3, value);
            }
            public bool worldTransform
            {
                get => Bits.GetBit(packed, 4);
                set => Bits.SetBit(ref packed, 4, value);
            }
            public bool tickedWorldTransform
            {
                get => Bits.GetBit(packed, 5);
                set => Bits.SetBit(ref packed, 5, value);
            }
            public bool liveAddedParent
            {
                get => Bits.GetBit(packed, 6);
                set => Bits.SetBit(ref packed, 6, value);
            }
            public bool liveRemovedParent
            {
                get => Bits.GetBit(packed, 7);
                set => Bits.SetBit(ref packed, 7, value);
            }
            public bool copyTickedToNormal
            {
                get => Bits.GetBit(packed, 8);
                set => Bits.SetBit(ref packed, 8, value);
            }
            public bool copyNormalToTicked
            {
                get => Bits.GetBit(packed, 9);
                set => Bits.SetBit(ref packed, 9, value);
            }
            public bool setNormalToIdentity
            {
                get => Bits.GetBit(packed, 10);
                set => Bits.SetBit(ref packed, 10, value);
            }
            public bool setTickedToIdentity
            {
                get => Bits.GetBit(packed, 11);
                set => Bits.SetBit(ref packed, 11, value);
            }
            public bool isNormal
            {
                get => Bits.GetBit(packed, 12);
                set => Bits.SetBit(ref packed, 12, value);
            }
            public bool isTicked
            {
                get => Bits.GetBit(packed, 13);
                set => Bits.SetBit(ref packed, 13, value);
            }
            public bool isCopyParent
            {
                get => Bits.GetBit(packed, 14);
                set => Bits.SetBit(ref packed, 14, value);
            }
            public uint changeFlags => Bits.GetBits(packed, 0, 8);
            public bool noChange => changeFlags == 0;
        }

        public static ComponentAddSet GetChildComponentsToAdd(EntityManager em, Entity child, TreeClassification.TreeRole role, InheritanceFlags flags)
        {
            ComponentAddSet addSet = default;
            addSet.entity          = child;
            if (role == TreeClassification.TreeRole.Solo || role == TreeClassification.TreeRole.Root)
                addSet.rootReference = true;
#if UNITY_EDITOR
            if (em.HasComponent<LiveBakedTag>(child) && !em.HasComponent<LiveAddedParentTag>(child))
                addSet.liveAddedParent = true;
#endif
            var isTicked = em.HasComponent<TickedEntityTag>(child);
            var isNormal = em.HasComponent<WorldTransform>(child);
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
                    addSet.tickedWorldTransform = !em.HasComponent<TickedWorldTransform>(child);
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

        public static ComponentAddSet GetParentComponentsToAdd(EntityManager em,
                                                               Entity parent,
                                                               TreeClassification.TreeRole role,
                                                               ComponentAddSet childAddSet,
                                                               SetParentOptions options,
                                                               bool considerTransforms = true)
        {
            ComponentAddSet addSet = default;
            addSet.entity          = parent;
            addSet.isTicked        = childAddSet.isTicked;
            addSet.isNormal        = childAddSet.isNormal;
            if (role == TreeClassification.TreeRole.Solo)
                addSet.entityInHierarchy = true;

            if (considerTransforms)
            {
                bool hasNormal   = em.HasComponent<WorldTransform>(parent);
                bool hasTicked   = em.HasComponent<TickedWorldTransform>(parent);
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

            if (role == TreeClassification.TreeRole.Solo || role == TreeClassification.TreeRole.Root)
            {
                if (options != SetParentOptions.IgnoreLinkedEntityGroup && !em.HasBuffer<LinkedEntityGroup>(parent))
                    addSet.linkedEntityGroup = true;
                if (options == SetParentOptions.IgnoreLinkedEntityGroup)
                    addSet.entityInHierarchyCleanup = true;
            }
            return addSet;
        }

        public static Span<ComponentAddSet> GetAncestorComponentsToAdd(ref ThreadStackAllocator tsa,
                                                                       EntityManager em,
                                                                       ReadOnlySpan<EntityInHierarchy> hierarchy,
                                                                       TreeClassification parentClassification,
                                                                       ComponentAddSet childAddSet,
                                                                       SetParentOptions options)
        {
            var  resultBuffer         = tsa.AllocateAsSpan<ComponentAddSet>(parentClassification.indexInHierarchy + 1);
            var  resultCount          = 0;
            var  parentAddSet         = GetParentComponentsToAdd(em, hierarchy[parentClassification.indexInHierarchy].entity, parentClassification.role, childAddSet, options);
            bool allTransformsPresent = false;
            if (!parentAddSet.noChange)
            {
                resultBuffer[0] = parentAddSet;
                resultCount++;

                for (int index = hierarchy[parentClassification.indexInHierarchy].parentIndex; index > 0; index = hierarchy[index].parentIndex)
                {
                    var newAddSet = GetParentComponentsToAdd(em, hierarchy[index].entity, TreeClassification.TreeRole.InternalWithChildren, childAddSet, options);
                    if (newAddSet.noChange)
                    {
                        allTransformsPresent = true;
                        break;
                    }
                    if (hierarchy[index].m_flags.HasCopyParent())
                        newAddSet.isCopyParent = true;
                    newAddSet.indexInHierarchy = index;
                    newAddSet.parent           = hierarchy[hierarchy[index].parentIndex].entity;
                    resultBuffer[resultCount]  = newAddSet;
                    resultCount++;
                }
            }

            var rootAddSet            = GetParentComponentsToAdd(em, parentClassification.root, TreeClassification.TreeRole.Root, childAddSet, options, !allTransformsPresent);
            resultBuffer[resultCount] = rootAddSet;
            resultCount++;
            return resultBuffer.Slice(0, resultCount);
        }

        public static void AddComponents(EntityManager em, ComponentAddSet addSet)
        {
            if (addSet.noChange)
                return;
            FixedList128Bytes<ComponentType> typesToAdd = default;
            if (addSet.rootReference)
                typesToAdd.Add(ComponentType.ReadOnly<RootReference>());
            if (addSet.entityInHierarchy)
                typesToAdd.Add(ComponentType.ReadOnly<EntityInHierarchy>());
            if (addSet.entityInHierarchyCleanup)
                typesToAdd.Add(ComponentType.ReadOnly<EntityInHierarchyCleanup>());
            if (addSet.linkedEntityGroup)
                typesToAdd.Add(ComponentType.ReadOnly<LinkedEntityGroup>());
            if (addSet.worldTransform)
                typesToAdd.Add(ComponentType.ReadOnly<WorldTransform>());
            if (addSet.tickedWorldTransform)
                typesToAdd.Add(ComponentType.ReadOnly<TickedWorldTransform>());
#if UNITY_EDITOR
            if (addSet.liveAddedParent)
            {
                if (em.HasComponent<LiveRemovedParentTag>(addSet.entity))
                    em.RemoveComponent<LiveRemovedParentTag>(addSet.entity);
                typesToAdd.Add(ComponentType.ReadOnly<LiveAddedParentTag>());
            }
            if (addSet.liveRemovedParent)
            {
                if (em.HasComponent<LiveAddedParentTag>(addSet.entity))
                    em.RemoveComponent<LiveAddedParentTag>(addSet.entity);
                typesToAdd.Add(ComponentType.ReadOnly<LiveRemovedParentTag>());
            }
#endif
            em.AddComponent(addSet.entity, new ComponentTypeSet(in typesToAdd));
            if (addSet.copyNormalToTicked)
                em.SetComponentData(addSet.entity, em.GetComponentData<WorldTransform>(addSet.entity).ToTicked());
            else if (addSet.copyTickedToNormal)
                em.SetComponentData(addSet.entity, em.GetComponentData<TickedWorldTransform>(addSet.entity).ToUnticked());
            else
            {
                if (addSet.parent != Entity.Null)
                {
                    if (addSet.setNormalToIdentity)
                    {
                        var parentTransform = em.GetComponentData<WorldTransform>(addSet.parent);
                        if (!addSet.isCopyParent)
                            parentTransform.worldTransform.stretch = new float3(1f, 1f, 1f);
                        em.SetComponentData(addSet.entity, parentTransform);
                    }
                    if (addSet.setTickedToIdentity)
                    {
                        var parentTransform = em.GetComponentData<TickedWorldTransform>(addSet.parent);
                        if (!addSet.isCopyParent)
                            parentTransform.worldTransform.stretch = new float3(1f, 1f, 1f);
                        em.SetComponentData(addSet.entity, parentTransform);
                    }
                }
                else
                {
                    if (addSet.setNormalToIdentity)
                        em.SetComponentData(addSet.entity, new WorldTransform { worldTransform = TransformQvvs.identity });
                    if (addSet.setTickedToIdentity)
                        em.SetComponentData(addSet.entity, new TickedWorldTransform { worldTransform = TransformQvvs.identity });
                }
            }

            if (addSet.linkedEntityGroup)
                em.GetBuffer<LinkedEntityGroup>(addSet.entity).Add(new LinkedEntityGroup { Value = addSet.entity });
        }

        public static void AddComponents(EntityManager em, Span<ComponentAddSet> addSets)
        {
            // Iterate backwards because we want to propagate newly added transforms if they are identity.
            for (int i = addSets.Length - 1; i >= 0; i--)
            {
                var set = addSets[i];
                AddComponents(em, set);
            }
        }

        public static void RemoveRootComponents(EntityManager em, Entity entity, bool removeLeg)
        {
            if (removeLeg)
                em.RemoveComponent(entity, new TypePack<EntityInHierarchy, EntityInHierarchyCleanup, LinkedEntityGroup>());
            else
                em.RemoveComponent(entity, new TypePack<EntityInHierarchy, EntityInHierarchyCleanup>());
        }

        public static void AddComponentsBatched(EntityManager em, ComponentAddSet addSet, NativeArray<Entity> batch)
        {
            if (addSet.noChange)
                return;
            FixedList128Bytes<ComponentType> typesToAdd = default;
            if (addSet.rootReference)
                typesToAdd.Add(ComponentType.ReadOnly<RootReference>());
            if (addSet.entityInHierarchy)
                typesToAdd.Add(ComponentType.ReadOnly<EntityInHierarchy>());
            if (addSet.entityInHierarchyCleanup)
                typesToAdd.Add(ComponentType.ReadOnly<EntityInHierarchyCleanup>());
            if (addSet.linkedEntityGroup)
                typesToAdd.Add(ComponentType.ReadOnly<LinkedEntityGroup>());
            if (addSet.worldTransform)
                typesToAdd.Add(ComponentType.ReadOnly<WorldTransform>());
            if (addSet.tickedWorldTransform)
                typesToAdd.Add(ComponentType.ReadOnly<TickedWorldTransform>());
#if UNITY_EDITOR
            if (addSet.liveAddedParent)
            {
                em.RemoveComponent<LiveRemovedParentTag>(batch);
                typesToAdd.Add(ComponentType.ReadOnly<LiveAddedParentTag>());
            }
            if (addSet.liveRemovedParent)
            {
                em.RemoveComponent<LiveAddedParentTag>(batch);
                typesToAdd.Add(ComponentType.ReadOnly<LiveRemovedParentTag>());
            }
#endif
            em.AddComponent(batch, new ComponentTypeSet(in typesToAdd));

            if (addSet.linkedEntityGroup)
            {
                foreach (var entity in batch)
                    em.GetBuffer<LinkedEntityGroup>(entity).Add(new LinkedEntityGroup { Value = entity });
            }
        }

        public static void ApplyAddComponentsBatchedPostProcess(EntityManager em, ComponentAddSet addSet)
        {
            if (addSet.noChange)
                return;
            if (addSet.copyNormalToTicked)
                em.SetComponentData(addSet.entity, em.GetComponentData<WorldTransform>(addSet.entity).ToTicked());
            else if (addSet.copyTickedToNormal)
                em.SetComponentData(addSet.entity, em.GetComponentData<TickedWorldTransform>(addSet.entity).ToUnticked());
            else
            {
                if (addSet.parent != Entity.Null)
                {
                    if (addSet.setNormalToIdentity)
                    {
                        var parentTransform = em.GetComponentData<WorldTransform>(addSet.parent);
                        if (!addSet.isCopyParent)
                            parentTransform.worldTransform.stretch = new float3(1f, 1f, 1f);
                        em.SetComponentData(addSet.entity, parentTransform);
                    }
                    if (addSet.setTickedToIdentity)
                    {
                        var parentTransform = em.GetComponentData<TickedWorldTransform>(addSet.parent);
                        if (!addSet.isCopyParent)
                            parentTransform.worldTransform.stretch = new float3(1f, 1f, 1f);
                        em.SetComponentData(addSet.entity, parentTransform);
                    }
                }
                else
                {
                    if (addSet.setNormalToIdentity)
                        em.SetComponentData(addSet.entity, new WorldTransform { worldTransform = TransformQvvs.identity });
                    if (addSet.setTickedToIdentity)
                        em.SetComponentData(addSet.entity, new TickedWorldTransform { worldTransform = TransformQvvs.identity });
                }
            }

            if (addSet.linkedEntityGroup)
                em.GetBuffer<LinkedEntityGroup>(addSet.entity).Add(new LinkedEntityGroup { Value = addSet.entity });
        }
    }
}
#endif

