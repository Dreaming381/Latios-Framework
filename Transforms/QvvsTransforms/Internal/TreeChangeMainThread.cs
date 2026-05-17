#if !LATIOS_TRANSFORMS_UNITY
//#define VALIDATE

using System;
using System.Diagnostics;
using Latios.Unsafe;
using static Latios.Transforms.TransformTools;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Mathematics;

namespace Latios.Transforms
{
    internal static unsafe class TreeChangeMainThread
    {
        public static void SetParent(EntityManager em,
                                     Entity parent,
                                     Entity child,
                                     InheritanceFlags flags,
                                     SetParentOptions options)
        {
            TreeChangeSafetyChecks.CheckChangeParent(em, parent, child, flags, options);

            var parentClassification = TreeKernels.ClassifyAlive(em, parent);
            var childClassification  = TreeKernels.ClassifyAlive(em, child);

            if (parentClassification.role == TreeKernels.TreeClassification.TreeRole.InternalNoChildren ||
                parentClassification.role == TreeKernels.TreeClassification.TreeRole.InternalWithChildren)
            {
                TreeChangeSafetyChecks.CheckInternalParentHasValidRootReference(em, parent, in parentClassification);
            }
            if (childClassification.role == TreeKernels.TreeClassification.TreeRole.InternalNoChildren ||
                childClassification.role == TreeKernels.TreeClassification.TreeRole.InternalWithChildren)
            {
                var rr     = new RootReference { m_rootEntity = childClassification.root, m_indexInHierarchy = childClassification.indexInHierarchy };
                var handle                                                                                   = rr.ToHandle(em);
                if (handle.entity != child)
                {
                    TreeChangeSafetyChecks.CheckLegEntitiesHaveValidRootReferences(em, child);
                    // The entity was just instantiated and has a bad RootReference, but it doesn't have children with this issue. We can just remove the bad RootReference and correct the role.
                    em.RemoveComponent<RootReference>(child);
                    childClassification      = default;
                    childClassification.role = TreeKernels.TreeClassification.TreeRole.Solo;
                }
                else if (handle.bloodParent.entity == parent)
                {
                    // Todo: We should always be able to return here, but we need a dedicated method for setting the InheritanceFlags.
                    if (flags == handle.inheritanceFlags)
                        return;
                }
            }

            switch (childClassification.role, parentClassification.role)
            {
                case (TreeKernels.TreeClassification.TreeRole.Solo, TreeKernels.TreeClassification.TreeRole.Solo):
                    AddSoloChildToSoloParent(em, parent, child, flags, options);
                    break;
                case (TreeKernels.TreeClassification.TreeRole.Solo, TreeKernels.TreeClassification.TreeRole.Root):
                    AddSoloChildToRootParent(em, parent, child, flags, options);
                    break;
                case (TreeKernels.TreeClassification.TreeRole.Solo, TreeKernels.TreeClassification.TreeRole.InternalNoChildren):
                case (TreeKernels.TreeClassification.TreeRole.Solo, TreeKernels.TreeClassification.TreeRole.InternalWithChildren):
                    AddSoloChildToInternalParent(em, parent, parentClassification, child, flags, options);
                    break;
                case (TreeKernels.TreeClassification.TreeRole.Root, TreeKernels.TreeClassification.TreeRole.Solo):
                    AddRootChildToSoloParent(em, parent, child, flags, options);
                    break;
                case (TreeKernels.TreeClassification.TreeRole.Root, TreeKernels.TreeClassification.TreeRole.Root):
                    AddRootChildToRootParent(em, parent, child, flags, options);
                    break;
                case (TreeKernels.TreeClassification.TreeRole.Root, TreeKernels.TreeClassification.TreeRole.InternalNoChildren):
                case (TreeKernels.TreeClassification.TreeRole.Root, TreeKernels.TreeClassification.TreeRole.InternalWithChildren):
                    TreeChangeSafetyChecks.CheckNotAssigningRootChildToDescendant(parent, child, parentClassification);
                    AddRootChildToInternalParent(em, parent, parentClassification, child, flags, options);
                    break;
                case (TreeKernels.TreeClassification.TreeRole.InternalNoChildren, TreeKernels.TreeClassification.TreeRole.Solo):
                    AddInternalChildWithoutSubtreeToSoloParent(em, parent, child, childClassification, flags, options);
                    break;
                case (TreeKernels.TreeClassification.TreeRole.InternalNoChildren, TreeKernels.TreeClassification.TreeRole.Root):
                    if (parent == childClassification.root)
                        AddInternalChildWithoutSubtreeToRootParentSameRoot(em, parent, child, childClassification, flags);
                    else
                        AddInternalChildWithoutSubtreeToRootParentDifferentRoot(em, parent, child, childClassification, flags, options);
                    break;
                case (TreeKernels.TreeClassification.TreeRole.InternalNoChildren, TreeKernels.TreeClassification.TreeRole.InternalNoChildren):
                case (TreeKernels.TreeClassification.TreeRole.InternalNoChildren, TreeKernels.TreeClassification.TreeRole.InternalWithChildren):
                    if (parentClassification.root == childClassification.root)
                    {
                        TreeChangeSafetyChecks.CheckNotAssigningChildToDescendant(em, parent, child, parentClassification, childClassification);
                        AddInternalChildWithoutSubtreeToInternalParentSameRoot(em, parent, parentClassification, child, childClassification, flags);
                    }
                    else
                        AddInternalChildWithoutSubtreeToInternalParentDifferentRoots(em, parent, parentClassification, child, childClassification, flags, options);
                    break;
                case (TreeKernels.TreeClassification.TreeRole.InternalWithChildren, TreeKernels.TreeClassification.TreeRole.Solo):
                    AddInternalChildWithSubtreeToSoloParent(em, parent, child, childClassification, flags, options);
                    break;
                case (TreeKernels.TreeClassification.TreeRole.InternalWithChildren, TreeKernels.TreeClassification.TreeRole.Root):
                    if (parent == childClassification.root)
                        AddInternalChildWithSubtreeToRootParentSameRoot(em, parent, child, childClassification, flags);
                    else
                        AddInternalChildWithSubtreeToRootParentDifferentRoot(em, parent, child, childClassification, flags, options);
                    break;
                case (TreeKernels.TreeClassification.TreeRole.InternalWithChildren, TreeKernels.TreeClassification.TreeRole.InternalNoChildren):
                case (TreeKernels.TreeClassification.TreeRole.InternalWithChildren, TreeKernels.TreeClassification.TreeRole.InternalWithChildren):
                    if (parentClassification.root == childClassification.root)
                    {
                        TreeChangeSafetyChecks.CheckNotAssigningChildToDescendant(em, parent, child, parentClassification, childClassification);
                        AddInternalChildWithSubtreeToInternalParentSameRoot(em, parent, parentClassification, child, childClassification, flags);
                    }
                    else
                        AddInternalChildWithSubtreeToInternalParentDifferentRoots(em, parent, parentClassification, child, childClassification, flags, options);
                    break;
            }

            var childHandle = em.GetComponentData<RootReference>(child).ToHandle(em);
            if (flags.HasCopyParent())
            {
                // Set WorldTransform of child and propagate.
                Span<Propagate.WriteCommand> command = stackalloc Propagate.WriteCommand[1];
                command[0]                           = new Propagate.WriteCommand
                {
                    indexInHierarchy = childHandle.indexInHierarchy,
                    writeType        = Propagate.WriteCommand.WriteType.CopyParentParentChanged
                };
                Span<TransformQvvs> dummy = stackalloc TransformQvvs[1];
                em.CompleteDependencyBeforeRW<WorldTransform>();
                var transformLookup = em.GetComponentLookup<WorldTransform>(false);
                if (em.HasComponent<WorldTransform>(child))
                {
                    var ema = new EntityManagerAccess(em);
                    Propagate.WriteAndPropagate(childHandle.m_hierarchy, childHandle.m_extraHierarchy, dummy, command, ref ema, ref ema);
                }
                if (em.HasComponent<TickedWorldTransform>(child))
                {
                    var ema = new TickedEntityManagerAccess(em);
                    Propagate.WriteAndPropagate(childHandle.m_hierarchy, childHandle.m_extraHierarchy, dummy, command, ref ema, ref ema);
                }
            }
            else
            {
                // Compute new local transforms (and propagate if necessary)
                if (em.HasComponent<WorldTransform>(child))
                {
                    var childTransform = em.GetComponentData<WorldTransform>(child);
                    SetWorldTransform(child, in childTransform.worldTransform, em);
                }
                if (em.HasComponent<TickedWorldTransform>(child))
                {
                    var childTransform = em.GetComponentData<TickedWorldTransform>(child);
                    SetTickedWorldTransform(child, in childTransform.worldTransform, em);
                }
            }
        }

        public static void CleanHierarchy(this EntityManager em, Entity entityBelongingToHierarchy)
        {
            var foundRoot = TreeKernels.FindRoot(em, entityBelongingToHierarchy);
            if (!foundRoot.found)
                return;

            var tsa = ThreadStackAllocator.GetAllocator();

            var root      = foundRoot.root;
            var isAlive   = foundRoot.isRootAlive;
            var hierarchy = isAlive ? em.GetBuffer<EntityInHierarchy>(root, false) : em.GetBuffer<EntityInHierarchyCleanup>(root, false).Reinterpret<EntityInHierarchy>();
            var old       = TreeKernels.CopyHierarchyEntities(ref tsa, hierarchy.AsNativeArray());
            CleanHierarchy(ref tsa, em, root, ref hierarchy, isAlive, out bool removeLeg);
            TreeKernels.UpdateRootReferencesFromDiff(hierarchy.AsNativeArray(), old, em);
            if (hierarchy.Length < 2)
                TreeKernels.RemoveRootComponents(em, root, removeLeg);
            else if (removeLeg)
                em.RemoveComponent<LinkedEntityGroup>(root);

            tsa.Dispose();
        }

        public static void ClearParent(EntityManager em, Entity child, ClearParentOptions options)
        {
            TreeChangeSafetyChecks.CheckChildToRemoveIsAlive(em, child);

            if (!em.HasComponent<RootReference>(child))
                return; // Child has no parent. Do nothing.

            var tsa = ThreadStackAllocator.GetAllocator();

            // Get old hierarchy
            var  rootReference = em.GetComponentData<RootReference>(child);
            bool isRootAlive   = em.IsAlive(rootReference.rootEntity);
            var  hierarchy     =
                isRootAlive ? em.GetBuffer<EntityInHierarchy>(rootReference.rootEntity) : em.GetBuffer<EntityInHierarchyCleanup>(rootReference.rootEntity).Reinterpret<EntityInHierarchy>();
            var oldEntities = TreeKernels.CopyHierarchyEntities(ref tsa, hierarchy.AsNativeArray());

            if (hierarchy[rootReference.indexInHierarchy].childCount > 0)
            {
                var subtree = TreeKernels.ExtractSubtree(ref tsa, hierarchy.AsNativeArray(), rootReference.indexInHierarchy);

                // Find LEG entities to remove from ancestry except root
                Span<Entity> ancestorsWithLegsToRemove = default;
                if (options == ClearParentOptions.TransferLinkedEntityGroup)
                {
                    var hierarchyArray = hierarchy.AsNativeArray();
                    int ancestorCount  = 0;
                    for (int i = hierarchyArray[rootReference.indexInHierarchy].parentIndex; i > 0; i = hierarchyArray[i].parentIndex)
                        ancestorCount++;
                    ancestorsWithLegsToRemove = tsa.AllocateAsSpan<Entity>(ancestorCount);
                    ancestorCount             = 0;
                    for (int i = hierarchyArray[rootReference.indexInHierarchy].parentIndex; i > 0; i = hierarchyArray[i].parentIndex)
                    {
                        if (em.HasBuffer<LinkedEntityGroup>(hierarchyArray[i].entity))
                        {
                            var leg = em.GetBuffer<LinkedEntityGroup>(hierarchyArray[i].entity, false);
                            TreeKernels.RemoveHierarchyEntitiesFromLeg(ref leg, subtree);
                            if (i > 0 && leg.Length < 2)
                            {
                                ancestorsWithLegsToRemove[ancestorCount] = hierarchyArray[i].entity;
                                ancestorCount++;
                            }
                        }
                    }
                    ancestorsWithLegsToRemove = ancestorsWithLegsToRemove.Slice(0, ancestorCount);
                }

                // Find LEG entities to copy or remove from root
                Span<Entity> entitiesToAddToNewLeg = default;
                bool         needsCleanup          = true;
                if (options != ClearParentOptions.IgnoreLinkedEntityGroup && em.HasBuffer<LinkedEntityGroup>(rootReference.rootEntity))
                {
                    var leg = em.GetBuffer<LinkedEntityGroup>(rootReference.rootEntity, options != ClearParentOptions.TransferLinkedEntityGroup);

                    bool matchedAll;
                    if (options == ClearParentOptions.TransferLinkedEntityGroup)
                        entitiesToAddToNewLeg = TreeKernels.GetAndRemoveHierarchyEntitiesFromLeg(ref tsa, ref leg, subtree, out matchedAll);
                    else
                        entitiesToAddToNewLeg = TreeKernels.GetHierarchyEntitiesInLeg(ref tsa, subtree, leg.Reinterpret<Entity>().AsNativeArray(), out matchedAll);
                    needsCleanup              = !matchedAll;
                }

                // Update old hierarchy
                TreeKernels.RemoveSubtreeFromHierarchy(ref tsa, ref hierarchy, rootReference.indexInHierarchy, subtree);
                CleanHierarchy(ref tsa, em, rootReference.rootEntity, ref hierarchy, true, out var removeLegFromRoot);
                if (isRootAlive && em.HasBuffer<EntityInHierarchyCleanup>(rootReference.rootEntity))
                {
                    var cleanup = em.GetBuffer<EntityInHierarchyCleanup>(rootReference.rootEntity);
                    TreeKernels.CopyHierarchyToCleanup(in hierarchy, ref cleanup);
                }
                TreeKernels.UpdateRootReferencesFromDiff(hierarchy.AsNativeArray(), oldEntities, em);

                // Remove components
                bool convertRootToSolo = hierarchy.Length < 2;
                em.RemoveComponent<RootReference>(child);
                if (convertRootToSolo)
                    TreeKernels.RemoveRootComponents(em, rootReference.rootEntity, removeLegFromRoot);
                else if (removeLegFromRoot)
                    em.RemoveComponent<LinkedEntityGroup>(rootReference.rootEntity);
                foreach (var e in ancestorsWithLegsToRemove)
                    em.RemoveComponent<LinkedEntityGroup>(e);

                // Add or update child LEG if required
                if ((entitiesToAddToNewLeg.Length > 1 || (entitiesToAddToNewLeg.Length == 1 && entitiesToAddToNewLeg[0] != child)) &&
                    !em.HasBuffer<LinkedEntityGroup>(child))
                {
                    var leg = em.AddBuffer<LinkedEntityGroup>(child).Reinterpret<Entity>();
                    leg.Add(child);
                    foreach (var e in entitiesToAddToNewLeg)
                    {
                        if (e != child)
                            leg.Add(e);
                    }
                }
                else if (options != ClearParentOptions.IgnoreLinkedEntityGroup && entitiesToAddToNewLeg.Length > 2 && em.HasBuffer<LinkedEntityGroup>(child))
                {
                    var leg = em.GetBuffer<LinkedEntityGroup>(child).Reinterpret<Entity>();
                    foreach (var e in entitiesToAddToNewLeg)
                    {
                        if (e == child)
                            continue;
                        // If the entity was added to the parent with AttachLinkedEntityGroup, then it may already have the LEG entities. We don't want to duplicate.
                        if (leg.AsNativeArray().Contains(child))
                            continue;
                        leg.Add(e);
                    }
                }

                // Add to new hierarchy, then clean it. There is a small chance we might have to undo adding it and the LinkedEntityGroup if all the descendants are dead.
                var newHierarchy    = em.AddBuffer<EntityInHierarchy>(child);
                newHierarchy.Length = subtree.Length;
                subtree.CopyTo(newHierarchy.AsNativeArray().AsSpan());
                CleanHierarchy(ref tsa, em, child, ref newHierarchy, options != ClearParentOptions.IgnoreLinkedEntityGroup, out var removeLegFromChild);
                if (newHierarchy.Length < 2)
                    TreeKernels.RemoveRootComponents(em, child, removeLegFromChild);
                else
                {
                    TreeKernels.UpdateRootReferencesFromDiff(newHierarchy.AsNativeArray(), default, em);
                    if (needsCleanup)
                    {
                        var newCleanup = em.AddBuffer<EntityInHierarchyCleanup>(child);
                        newHierarchy   = em.GetBuffer<EntityInHierarchy>(child);
                        TreeKernels.CopyHierarchyToCleanup(in hierarchy, ref newCleanup);
                    }
                    if (removeLegFromChild)
                        em.RemoveComponent<LinkedEntityGroup>(child);
                }
            }
            else
            {
                // Find the child to remove from ancestry LEGs including root
                Span<Entity> ancestorsWithLegsToRemove = default;
                if (options == ClearParentOptions.TransferLinkedEntityGroup)
                {
                    var hierarchyArray = hierarchy.AsNativeArray();
                    int ancestorCount  = 0;
                    for (int i = hierarchyArray[rootReference.indexInHierarchy].parentIndex; i >= 0; i = hierarchyArray[i].parentIndex)
                        ancestorCount++;
                    ancestorsWithLegsToRemove = tsa.AllocateAsSpan<Entity>(ancestorCount - 1);
                    ancestorCount             = 0;
                    for (int i = hierarchyArray[rootReference.indexInHierarchy].parentIndex; i >= 0; i = hierarchyArray[i].parentIndex)
                    {
                        if (em.HasBuffer<LinkedEntityGroup>(hierarchyArray[i].entity))
                        {
                            var leg = em.GetBuffer<LinkedEntityGroup>(hierarchyArray[i].entity, false);
                            TreeKernels.RemoveEntityFromLeg(ref leg, child, out _);
                            if (i > 0 && leg.Length < 2)
                            {
                                ancestorsWithLegsToRemove[ancestorCount] = hierarchyArray[i].entity;
                                ancestorCount++;
                            }
                        }
                    }
                    ancestorsWithLegsToRemove = ancestorsWithLegsToRemove.Slice(0, ancestorCount);
                }

                // Update old hierarchy
                TreeKernels.RemoveSoloFromHierarchy(ref hierarchy, rootReference.indexInHierarchy);
                CleanHierarchy(ref tsa, em, rootReference.rootEntity, ref hierarchy, true, out var removeLegFromRoot);
                if (isRootAlive && em.HasBuffer<EntityInHierarchyCleanup>(rootReference.rootEntity))
                {
                    var cleanup = em.GetBuffer<EntityInHierarchyCleanup>(rootReference.rootEntity);
                    TreeKernels.CopyHierarchyToCleanup(in hierarchy, ref cleanup);
                }
                TreeKernels.UpdateRootReferencesFromDiff(hierarchy.AsNativeArray(), oldEntities, em);

                // Remove components
                bool convertRootToSolo = hierarchy.Length < 2;
                em.RemoveComponent<RootReference>(child);
                if (convertRootToSolo)
                    TreeKernels.RemoveRootComponents(em, rootReference.rootEntity, removeLegFromRoot);
                else if (removeLegFromRoot)
                    em.RemoveComponent<LinkedEntityGroup>(rootReference.rootEntity);
                foreach (var e in ancestorsWithLegsToRemove)
                    em.RemoveComponent<LinkedEntityGroup>(e);
            }

            tsa.Dispose();
        }

        #region Solo Children
        static void AddSoloChildToSoloParent(EntityManager em, Entity parent, Entity child, InheritanceFlags flags, SetParentOptions options)
        {
            // For this case, we know upfront whether we need LEG or Cleanup. Apply structural changes immediately.
            var childAddSet  = TreeKernels.GetChildComponentsToAdd(em, child, TreeKernels.TreeClassification.TreeRole.Solo, flags);
            var parentAddSet = TreeKernels.GetParentComponentsToAdd(em, parent, TreeKernels.TreeClassification.TreeRole.Solo, childAddSet, options);
            TreeKernels.AddComponents(em, childAddSet);
            TreeKernels.AddComponents(em, parentAddSet);

            // Construct the hierarchy and copy it to cleanup if needed.
            var hierarchy = em.GetBuffer<EntityInHierarchy>(parent, false);
            TreeKernels.BuildOriginalParentChildHierarchy(ref hierarchy, parent, child, flags);
            if (parentAddSet.entityInHierarchyCleanup)
            {
                var cleanup = em.GetBuffer<EntityInHierarchyCleanup>(parent, false);
                TreeKernels.CopyHierarchyToCleanup(in hierarchy, ref cleanup);
            }
            TreeKernels.UpdateRootReferencesFromDiff(hierarchy.AsNativeArray(), default, em);

            // If we need LEG, add the child to the parent. Then optionally remove the child's LEG.
            if (options != SetParentOptions.IgnoreLinkedEntityGroup)
            {
                var leg = em.GetBuffer<LinkedEntityGroup>(parent, false);
                TreeKernels.AddEntityToLeg(ref leg, child);
                if (options == SetParentOptions.TransferLinkedEntityGroup && em.HasBuffer<LinkedEntityGroup>(child))
                {
                    var childLeg = em.GetBuffer<LinkedEntityGroup>(child, true);
                    if (childLeg.Length < 2)
                        em.RemoveComponent<LinkedEntityGroup>(child);
                }
            }

            Validate(em, parent, child);
        }

        static void AddSoloChildToRootParent(EntityManager em, Entity parent, Entity child, InheritanceFlags flags, SetParentOptions options)
        {
            var tsa = ThreadStackAllocator.GetAllocator();

            // For this case, we know upfront whether we need LEG or Cleanup. Apply structural changes immediately.
            var childAddSet  = TreeKernels.GetChildComponentsToAdd(em, child, TreeKernels.TreeClassification.TreeRole.Solo, flags);
            var parentAddSet = TreeKernels.GetParentComponentsToAdd(em, parent, TreeKernels.TreeClassification.TreeRole.Root, childAddSet, options);
            TreeKernels.AddComponents(em, childAddSet);
            TreeKernels.AddComponents(em, parentAddSet);

            // Clean the root parent, then add the child to it. We can handle cleanup now too since all components have been added.
            var hierarchy = em.GetBuffer<EntityInHierarchy>(parent, false);
            var old       = TreeKernels.CopyHierarchyEntities(ref tsa, hierarchy.AsNativeArray());
            CleanHierarchy(ref tsa, em, parent, ref hierarchy, !parentAddSet.linkedEntityGroup, out var removeParentLeg);
            TreeKernels.InsertSoloEntityIntoHierarchy(ref hierarchy, 0, child, flags);
            if (parentAddSet.entityInHierarchyCleanup || em.HasBuffer<EntityInHierarchyCleanup>(parent))
            {
                var cleanup = em.GetBuffer<EntityInHierarchyCleanup>(parent, false);
                TreeKernels.CopyHierarchyToCleanup(in hierarchy, ref cleanup);
            }
            TreeKernels.UpdateRootReferencesFromDiff(hierarchy.AsNativeArray(), old, em);

            // Add child to parent's LEG, then optionally remove LEG from child. Also, cleaning might cause parent to drop LEG.
            if (options != SetParentOptions.IgnoreLinkedEntityGroup)
            {
                var leg = em.GetBuffer<LinkedEntityGroup>(parent, false);
                TreeKernels.AddEntityToLeg(ref leg, child);
                if (options == SetParentOptions.TransferLinkedEntityGroup && em.HasBuffer<LinkedEntityGroup>(child))
                {
                    var childLeg = em.GetBuffer<LinkedEntityGroup>(child, true);
                    if (childLeg.Length < 2)
                        em.RemoveComponent<LinkedEntityGroup>(child);
                }
            }
            else if (removeParentLeg)
                em.RemoveComponent<LinkedEntityGroup>(parent);

            Validate(em, parent, child);

            tsa.Dispose();
        }

        static void AddSoloChildToInternalParent(EntityManager em,
                                                 Entity parent,
                                                 TreeKernels.TreeClassification parentClassification,
                                                 Entity child,
                                                 InheritanceFlags flags,
                                                 SetParentOptions options)
        {
            var tsa = ThreadStackAllocator.GetAllocator();

            // For this case, we know upfront whether we need LEG or Cleanup. Apply structural changes immediately.
            var childAddSet     = TreeKernels.GetChildComponentsToAdd(em, child, TreeKernels.TreeClassification.TreeRole.Solo, flags);
            var root            = parentClassification.root;
            var hierarchy       = GetRootHierarchy(em, parentClassification, true);
            var ancestryAddSets = TreeKernels.GetAncestorComponentsToAdd(ref tsa, em, hierarchy.AsNativeArray(), parentClassification, childAddSet, options);
            var rootAddSet      = ancestryAddSets[ancestryAddSets.Length - 1];
            TreeKernels.AddComponents(em, childAddSet);
            TreeKernels.AddComponents(em, ancestryAddSets);

            // We insert the new entity into the hierarchy before cleaning, because otherwise we lose where the parent it.
            hierarchy = GetRootHierarchy(em, parentClassification, false);
            var old   = TreeKernels.CopyHierarchyEntities(ref tsa, hierarchy.AsNativeArray());
            TreeKernels.UpdateLocalTransformsOfNewAncestorComponents(ancestryAddSets, hierarchy.AsNativeArray());
            TreeKernels.InsertSoloEntityIntoHierarchy(ref hierarchy, parentClassification.indexInHierarchy, child, flags);
            CleanHierarchy(ref tsa, em, parentClassification.root, ref hierarchy, !rootAddSet.linkedEntityGroup, out var removeRootLeg);
            if (rootAddSet.entityInHierarchyCleanup || (parentClassification.isRootAlive && em.HasBuffer<EntityInHierarchyCleanup>(root)))
            {
                var cleanup = em.GetBuffer<EntityInHierarchyCleanup>(root, false);
                TreeKernels.CopyHierarchyToCleanup(in hierarchy, ref cleanup);
            }
            TreeKernels.UpdateRootReferencesFromDiff(hierarchy.AsNativeArray(), old, em);

            // Add child to parent's LEG, then optionally remove LEG from child. Also, cleaning might cause root to drop LEG.
            if (options != SetParentOptions.IgnoreLinkedEntityGroup)
            {
                var leg = em.GetBuffer<LinkedEntityGroup>(root, false);
                TreeKernels.AddEntityToLeg(ref leg, child);
                if (options == SetParentOptions.TransferLinkedEntityGroup && em.HasBuffer<LinkedEntityGroup>(child))
                {
                    var childLeg = em.GetBuffer<LinkedEntityGroup>(child, true);
                    if (childLeg.Length < 2)
                        em.RemoveComponent<LinkedEntityGroup>(child);
                }
            }
            else if (removeRootLeg)
                em.RemoveComponent<LinkedEntityGroup>(root);

            Validate(em, parent, child);

            tsa.Dispose();
        }
        #endregion

        #region Root Children
        static void AddRootChildToSoloParent(EntityManager em, Entity parent, Entity child, InheritanceFlags flags, SetParentOptions options)
        {
            var tsa = ThreadStackAllocator.GetAllocator();

            // Get the components to add, but don't add them yet, because we don't yet know if the parent will need cleanup.
            var childAddSet  = TreeKernels.GetChildComponentsToAdd(em, child, TreeKernels.TreeClassification.TreeRole.Root, flags);
            var parentAddSet = TreeKernels.GetParentComponentsToAdd(em, parent, TreeKernels.TreeClassification.TreeRole.Solo, childAddSet, options);

            // Clean child hierarchy
            var oldChildHierarchy = em.GetBuffer<EntityInHierarchy>(child);
            CleanHierarchy(ref tsa, em, child, ref oldChildHierarchy, true, out var removeChildLeg);

            // Extract LEG entities
            ProcessRootChildLeg(ref tsa, em, child, oldChildHierarchy.AsNativeArray(), options, out var removeChildLeg2, out var dstHierarchyNeedsCleanup,
                                out var childLegEntities);
            parentAddSet.entityInHierarchyCleanup |= dstHierarchyNeedsCleanup;
            removeChildLeg                        |= removeChildLeg2;

            // Now we can add the components
            TreeKernels.AddComponents(em, childAddSet);
            TreeKernels.AddComponents(em, parentAddSet);

            // Build new hierarchy
            var hierarchy     = em.GetBuffer<EntityInHierarchy>(parent, false);
            oldChildHierarchy = em.GetBuffer<EntityInHierarchy>(child, true);
            TreeKernels.BuildOriginalParentWithDescendantHierarchy(ref hierarchy, parent, oldChildHierarchy.AsNativeArray(), flags);
            if (parentAddSet.entityInHierarchyCleanup)
            {
                var cleanup = em.GetBuffer<EntityInHierarchyCleanup>(parent, false);
                TreeKernels.CopyHierarchyToCleanup(in hierarchy, ref cleanup);
            }
            TreeKernels.UpdateRootReferencesFromDiff(hierarchy.AsNativeArray(), default, em);

            // Add LEG entities
            if (options != SetParentOptions.IgnoreLinkedEntityGroup)
            {
                var leg = em.GetBuffer<LinkedEntityGroup>(parent, false);
                if (childLegEntities.Length > 0)
                    TreeKernels.AddEntitiesToLeg(ref leg, childLegEntities);
                else
                    TreeKernels.AddEntityToLeg(ref leg, child);
            }

            // Remove old root components from child
            TreeKernels.RemoveRootComponents(em, child, removeChildLeg);

            Validate(em, parent, child);

            tsa.Dispose();
        }

        static void AddRootChildToRootParent(EntityManager em, Entity parent, Entity child, InheritanceFlags flags, SetParentOptions options)
        {
            var tsa = ThreadStackAllocator.GetAllocator();

            // Get the components, but only apply to the child so that it has root reference. We don't yet know if the parent needs cleanup.
            var childAddSet  = TreeKernels.GetChildComponentsToAdd(em, child, TreeKernels.TreeClassification.TreeRole.Root, flags);
            var parentAddSet = TreeKernels.GetParentComponentsToAdd(em, parent, TreeKernels.TreeClassification.TreeRole.Root, childAddSet, options);
            TreeKernels.AddComponents(em, childAddSet);

            // We know the parent is a root with a valid hierarchy, so we can apply the hierarchy changes and cleaning now.
            var oldChildHierarchy = em.GetBuffer<EntityInHierarchy>(child);
            CleanHierarchy(ref tsa, em, child,  ref oldChildHierarchy, true,                            out var removeChildLeg);
            var hierarchy = em.GetBuffer<EntityInHierarchy>(parent, false);
            var old       = TreeKernels.CopyHierarchyEntities(ref tsa, hierarchy.AsNativeArray());
            CleanHierarchy(ref tsa, em, parent, ref hierarchy,         !parentAddSet.linkedEntityGroup, out var removeParentLeg);
            TreeKernels.InsertSubtreeIntoHierarchy(ref hierarchy, 0, oldChildHierarchy.AsNativeArray().AsReadOnlySpan(), flags);
            TreeKernels.UpdateRootReferencesFromDiff(hierarchy.AsNativeArray(), old, em);

            // Extract LEG entities
            ProcessRootChildLeg(ref tsa, em, child, oldChildHierarchy.AsNativeArray(), options, out var removeChildLeg2, out var dstHierarchyNeedsCleanup,
                                out var childLegEntities);
            parentAddSet.entityInHierarchyCleanup |= dstHierarchyNeedsCleanup;
            removeChildLeg                        |= removeChildLeg2;

            // Now we can add the components and process cleanup
            TreeKernels.AddComponents(em, parentAddSet);
            if (parentAddSet.entityInHierarchyCleanup || em.HasBuffer<EntityInHierarchyCleanup>(parent))
            {
                var cleanup = em.GetBuffer<EntityInHierarchyCleanup>(parent, false);
                TreeKernels.CopyHierarchyToCleanup(in hierarchy, ref cleanup);
            }

            // Add LEG entities, and maybe remove the parent LEG after cleanup
            if (options != SetParentOptions.IgnoreLinkedEntityGroup)
            {
                var leg = em.GetBuffer<LinkedEntityGroup>(parent, false);
                if (childLegEntities.Length > 0)
                    TreeKernels.AddEntitiesToLeg(ref leg, childLegEntities);
                else
                    TreeKernels.AddEntityToLeg(ref leg, child);
            }
            else if (removeParentLeg)
                em.RemoveComponent<LinkedEntityGroup>(parent);

            // Remove old root components from child
            TreeKernels.RemoveRootComponents(em, child, removeChildLeg);

            Validate(em, parent, child);

            tsa.Dispose();
        }

        static void AddRootChildToInternalParent(EntityManager em,
                                                 Entity parent,
                                                 TreeKernels.TreeClassification parentClassification,
                                                 Entity child,
                                                 InheritanceFlags flags,
                                                 SetParentOptions options)
        {
            var tsa = ThreadStackAllocator.GetAllocator();

            // Get the components to add, but only apply them to the child, since we don't know yet if the parent's root needs cleanup.
            var     childAddSet     = TreeKernels.GetChildComponentsToAdd(em, child, TreeKernels.TreeClassification.TreeRole.Root, flags);
            var     root            = parentClassification.root;
            var     hierarchy       = GetRootHierarchy(em, parentClassification, true);
            var     ancestryAddSets = TreeKernels.GetAncestorComponentsToAdd(ref tsa, em, hierarchy.AsNativeArray(), parentClassification, childAddSet, options);
            ref var rootAddSet      = ref ancestryAddSets[ancestryAddSets.Length - 1];
            TreeKernels.AddComponents(em, childAddSet);

            // Clean the old hierarchy, then insert it into the new hierarchy while we know the new hierarchy's parent index, and then clean the new hierarchy.
            // Todo: We redundantly clean the entities that move between hierarchies. This could be improved.
            hierarchy             = GetRootHierarchy(em, parentClassification, false);
            var oldChildHierarchy = em.GetBuffer<EntityInHierarchy>(child);
            CleanHierarchy(ref tsa, em, child, ref oldChildHierarchy, true, out var removeChildLeg);
            TreeKernels.UpdateLocalTransformsOfNewAncestorComponents(ancestryAddSets, hierarchy.AsNativeArray());
            var old = TreeKernels.CopyHierarchyEntities(ref tsa, hierarchy.AsNativeArray());
            TreeKernels.InsertSubtreeIntoHierarchy(ref hierarchy, parentClassification.indexInHierarchy, oldChildHierarchy.AsNativeArray().AsReadOnlySpan(), flags);
            CleanHierarchy(ref tsa, em, root, ref hierarchy, !rootAddSet.linkedEntityGroup, out var removeRootLeg);
            TreeKernels.UpdateRootReferencesFromDiff(hierarchy.AsNativeArray(), old, em);

            // Extract LEG entities
            ProcessRootChildLeg(ref tsa, em, child, oldChildHierarchy.AsNativeArray(), options, out var removeChildLeg2, out var dstHierarchyNeedsCleanup,
                                out var childLegEntities);
            rootAddSet.entityInHierarchyCleanup |= dstHierarchyNeedsCleanup;
            removeChildLeg                      |= removeChildLeg2;

            // Now we can add the ancestry components and perform cleanup.
            TreeKernels.AddComponents(em, ancestryAddSets);
            if (rootAddSet.entityInHierarchyCleanup || (parentClassification.isRootAlive && em.HasBuffer<EntityInHierarchyCleanup>(parent)))
            {
                hierarchy   = em.GetBuffer<EntityInHierarchy>(root, true);
                var cleanup = em.GetBuffer<EntityInHierarchyCleanup>(root, false);
                TreeKernels.CopyHierarchyToCleanup(in hierarchy, ref cleanup);
            }

            // Add LEG entities. Also, cleaning might result in us removing LEG from the root.
            if (options != SetParentOptions.IgnoreLinkedEntityGroup)
            {
                var leg = em.GetBuffer<LinkedEntityGroup>(root, false);
                if (childLegEntities.Length > 0)
                    TreeKernels.AddEntitiesToLeg(ref leg, childLegEntities);
                else
                    TreeKernels.AddEntityToLeg(ref leg, child);
            }
            else if (removeRootLeg)
                em.RemoveComponent<LinkedEntityGroup>(root);

            // Remove old root components from child
            TreeKernels.RemoveRootComponents(em, child, removeChildLeg);

            Validate(em, parent, child);

            tsa.Dispose();
        }

        #endregion

        #region Internal Children without Subtrees
        static void AddInternalChildWithoutSubtreeToSoloParent(EntityManager em,
                                                               Entity parent,
                                                               Entity child,
                                                               TreeKernels.TreeClassification childClassification,
                                                               InheritanceFlags flags,
                                                               SetParentOptions options)
        {
            var tsa = ThreadStackAllocator.GetAllocator();

            // We are only moving one entity, so we know the components to add up front.
            var oldRoot      = childClassification.root;
            var childAddSet  = TreeKernels.GetChildComponentsToAdd(em, child, TreeKernels.TreeClassification.TreeRole.InternalNoChildren, flags);
            var parentAddSet = TreeKernels.GetParentComponentsToAdd(em, parent, TreeKernels.TreeClassification.TreeRole.Solo, childAddSet, options);
            TreeKernels.AddComponents(em, childAddSet);
            TreeKernels.AddComponents(em, parentAddSet);

            // We remove the child from the old hierarchy, clean the old hierarchy, and dispatch root references.
            // Note: ProcessInternalChildLegNoSubtree can make structural changes and invalidate buffers.
            var oldChildHierarchy   = GetRootHierarchy(em, childClassification, false);
            var oldRootEntities     = TreeKernels.CopyHierarchyEntities(ref tsa, oldChildHierarchy.AsNativeArray());
            var oldAncestorEntities = GetAncestorEntitiesIfNeededForLeg(ref tsa, oldChildHierarchy.AsNativeArray(), childClassification.indexInHierarchy, options);
            TreeKernels.RemoveSoloFromHierarchy(ref oldChildHierarchy, childClassification.indexInHierarchy);
            CleanHierarchy(ref tsa, em, oldRoot, ref oldChildHierarchy, true, out var removeOldRootLeg);
            TreeKernels.UpdateRootReferencesFromDiff(oldChildHierarchy.AsNativeArray(), oldRootEntities, em);
            bool convertOldRootToSolo = oldChildHierarchy.Length < 2;

            // And then we construct the new hierarchy, and optionally apply cleanup.
            var hierarchy = em.GetBuffer<EntityInHierarchy>(parent, false);
            TreeKernels.BuildOriginalParentChildHierarchy(ref hierarchy, parent, child, flags);
            if (parentAddSet.entityInHierarchyCleanup)
            {
                var cleanup = em.GetBuffer<EntityInHierarchyCleanup>(parent, false);
                TreeKernels.CopyHierarchyToCleanup(in hierarchy, ref cleanup);
            }
            TreeKernels.UpdateRootReferencesFromDiff(hierarchy.AsNativeArray(), default, em);

            // Now process LEG
            ProcessInternalChildLegNoSubtree(em, oldRoot, childClassification.isRootAlive, child, oldAncestorEntities, options, out bool removeChildLeg,
                                             out bool removeOldRootLeg2);
            removeOldRootLeg |= removeOldRootLeg2;
            if (options != SetParentOptions.IgnoreLinkedEntityGroup)
            {
                var leg = em.GetBuffer<LinkedEntityGroup>(parent, false);
                TreeKernels.AddEntityToLeg(ref leg, child);
            }

            // Remove old root components
            if (convertOldRootToSolo)
                TreeKernels.RemoveRootComponents(em, oldRoot, removeOldRootLeg);
            else if (removeOldRootLeg)
                em.RemoveComponent<LinkedEntityGroup>(oldRoot);

            if (removeChildLeg)
                em.RemoveComponent<LinkedEntityGroup>(child);

            Validate(em, parent, child);

            tsa.Dispose();
        }

        static void AddInternalChildWithoutSubtreeToRootParentSameRoot(EntityManager em,
                                                                       Entity parent,
                                                                       Entity child,
                                                                       TreeKernels.TreeClassification childClassification,
                                                                       InheritanceFlags flags)
        {
            var tsa = ThreadStackAllocator.GetAllocator();

            // We do not need to account for ticked vs unticked in the ancestry, because the root should already have everything
            var hierarchy = em.GetBuffer<EntityInHierarchy>(parent, false);
            var old       = TreeKernels.CopyHierarchyEntities(ref tsa, hierarchy.AsNativeArray());
            TreeKernels.RemoveSoloFromHierarchy(ref hierarchy, childClassification.indexInHierarchy);
            TreeKernels.InsertSoloEntityIntoHierarchy(ref hierarchy, 0, child, flags);
            CleanHierarchy(ref tsa, em, parent, ref hierarchy, true, out var removeLeg);
            if (em.HasBuffer<EntityInHierarchyCleanup>(parent))
            {
                var cleanup = em.GetBuffer<EntityInHierarchyCleanup>(parent, false);
                TreeKernels.CopyHierarchyToCleanup(in hierarchy, ref cleanup);
            }
            TreeKernels.UpdateRootReferencesFromDiff(hierarchy.AsNativeArray(), old, em);

            // Cleaning can still result in LEG being removed.
            if (removeLeg)
                em.RemoveComponent<LinkedEntityGroup>(parent);

            Validate(em, parent, child);

            tsa.Dispose();
        }

        static void AddInternalChildWithoutSubtreeToRootParentDifferentRoot(EntityManager em,
                                                                            Entity parent,
                                                                            Entity child,
                                                                            TreeKernels.TreeClassification childClassification,
                                                                            InheritanceFlags flags,
                                                                            SetParentOptions options)
        {
            var tsa = ThreadStackAllocator.GetAllocator();

            // We are only moving one entity, so we know the components to add up front.
            var oldRoot      = childClassification.root;
            var childAddSet  = TreeKernels.GetChildComponentsToAdd(em, child, TreeKernels.TreeClassification.TreeRole.InternalNoChildren, flags);
            var parentAddSet = TreeKernels.GetParentComponentsToAdd(em, parent, TreeKernels.TreeClassification.TreeRole.Root, childAddSet, options);
            TreeKernels.AddComponents(em, childAddSet);
            TreeKernels.AddComponents(em, parentAddSet);

            // We remove the child from the old hierarchy, clean the old hierarchy, and dispatch root references.
            // Note: ProcessInternalChildLegNoSubtree can make structural changes and invalidate buffers.
            var oldChildHierarchy   = GetRootHierarchy(em, childClassification, false);
            var oldRootEntities     = TreeKernels.CopyHierarchyEntities(ref tsa, oldChildHierarchy.AsNativeArray());
            var oldAncestorEntities = GetAncestorEntitiesIfNeededForLeg(ref tsa, oldChildHierarchy.AsNativeArray(), childClassification.indexInHierarchy, options);
            TreeKernels.RemoveSoloFromHierarchy(ref oldChildHierarchy, childClassification.indexInHierarchy);
            CleanHierarchy(ref tsa, em, oldRoot, ref oldChildHierarchy, true, out var removeOldRootLeg);
            TreeKernels.UpdateRootReferencesFromDiff(oldChildHierarchy.AsNativeArray(), oldRootEntities, em);
            bool convertOldRootToSolo = oldChildHierarchy.Length < 2;

            // And then we insert the child into the new hierarchy. Since the parent is the root, we do so after cleaning.
            var hierarchy = em.GetBuffer<EntityInHierarchy>(parent, false);
            var old       = TreeKernels.CopyHierarchyEntities(ref tsa, hierarchy.AsNativeArray());
            CleanHierarchy(ref tsa, em, parent, ref hierarchy, !parentAddSet.linkedEntityGroup, out var removeParentLeg);
            TreeKernels.InsertSoloEntityIntoHierarchy(ref hierarchy, 0, child, flags);
            if (parentAddSet.entityInHierarchyCleanup || em.HasBuffer<EntityInHierarchyCleanup>(parent))
            {
                var cleanup = em.GetBuffer<EntityInHierarchyCleanup>(parent, false);
                TreeKernels.CopyHierarchyToCleanup(in hierarchy, ref cleanup);
            }
            TreeKernels.UpdateRootReferencesFromDiff(hierarchy.AsNativeArray(), old, em);

            // Now process LEG
            ProcessInternalChildLegNoSubtree(em, oldRoot, childClassification.isRootAlive, child, oldAncestorEntities, options, out bool removeChildLeg,
                                             out bool removeOldRootLeg2);
            removeOldRootLeg |= removeOldRootLeg2;
            if (options != SetParentOptions.IgnoreLinkedEntityGroup)
            {
                var leg = em.GetBuffer<LinkedEntityGroup>(parent, false);
                TreeKernels.AddEntityToLeg(ref leg, child);
            }

            // Remove old root components
            if (convertOldRootToSolo)
                TreeKernels.RemoveRootComponents(em, oldRoot, removeOldRootLeg);
            else if (removeOldRootLeg)
                em.RemoveComponent<LinkedEntityGroup>(oldRoot);

            if (removeChildLeg)
                em.RemoveComponent<LinkedEntityGroup>(child);
            if (removeParentLeg)
                em.RemoveComponent<LinkedEntityGroup>(parent);

            Validate(em, parent, child);

            tsa.Dispose();
        }

        static void AddInternalChildWithoutSubtreeToInternalParentSameRoot(EntityManager em,
                                                                           Entity parent,
                                                                           TreeKernels.TreeClassification parentClassification,
                                                                           Entity child,
                                                                           TreeKernels.TreeClassification childClassification,
                                                                           InheritanceFlags flags)
        {
            var tsa = ThreadStackAllocator.GetAllocator();

            // We still need to account for ticked vs unticked in the ancestry
            var childAddSet     = TreeKernels.GetChildComponentsToAdd(em, child, TreeKernels.TreeClassification.TreeRole.InternalNoChildren, flags);
            var hierarchy       = GetRootHierarchy(em, parentClassification, false);
            var ancestryAddSets = TreeKernels.GetAncestorComponentsToAdd(ref tsa, em, hierarchy.AsNativeArray(), parentClassification, childAddSet, default);
            TreeKernels.AddComponents(em, ancestryAddSets);

            hierarchy = GetRootHierarchy(em, parentClassification, false);
            TreeKernels.UpdateLocalTransformsOfNewAncestorComponents(ancestryAddSets, hierarchy.AsNativeArray());
            var old = TreeKernels.CopyHierarchyEntities(ref tsa, hierarchy.AsNativeArray());
            TreeKernels.RemoveSoloFromHierarchy(ref hierarchy, childClassification.indexInHierarchy);
            // When we remove from the hierarchy, our parent's index might have shifted by an index if the child preceeded the parent
            if (parentClassification.indexInHierarchy >= hierarchy.Length || hierarchy[parentClassification.indexInHierarchy].entity != parent)
                parentClassification.indexInHierarchy--;
            TreeKernels.InsertSoloEntityIntoHierarchy(ref hierarchy, parentClassification.indexInHierarchy, child, flags);
            CleanHierarchy(ref tsa, em, parentClassification.root, ref hierarchy, parentClassification.isRootAlive, out var removeLeg);
            if (parentClassification.isRootAlive && em.HasBuffer<EntityInHierarchyCleanup>(parentClassification.root))
            {
                var cleanup = em.GetBuffer<EntityInHierarchyCleanup>(parentClassification.root, false);
                TreeKernels.CopyHierarchyToCleanup(in hierarchy, ref cleanup);
            }
            TreeKernels.UpdateRootReferencesFromDiff(hierarchy.AsNativeArray(), old, em);

            // Cleaning can still result in LEG being removed.
            if (removeLeg)
                em.RemoveComponent<LinkedEntityGroup>(parentClassification.root);

            Validate(em, parent, child);

            tsa.Dispose();
        }

        static void AddInternalChildWithoutSubtreeToInternalParentDifferentRoots(EntityManager em,
                                                                                 Entity parent,
                                                                                 TreeKernels.TreeClassification parentClassification,
                                                                                 Entity child,
                                                                                 TreeKernels.TreeClassification childClassification,
                                                                                 InheritanceFlags flags,
                                                                                 SetParentOptions options)
        {
            var tsa = ThreadStackAllocator.GetAllocator();

            // We are only moving one entity, so we know the components to add up front.
            var oldRoot         = childClassification.root;
            var root            = parentClassification.root;
            var childAddSet     = TreeKernels.GetChildComponentsToAdd(em, child, TreeKernels.TreeClassification.TreeRole.InternalNoChildren, flags);
            var hierarchy       = GetRootHierarchy(em, parentClassification, false);
            var ancestryAddSets = TreeKernels.GetAncestorComponentsToAdd(ref tsa, em, hierarchy.AsNativeArray(), parentClassification, childAddSet, options);
            var rootAddSet      = ancestryAddSets[ancestryAddSets.Length - 1];
            TreeKernels.AddComponents(em, childAddSet);
            TreeKernels.AddComponents(em, ancestryAddSets);

            // We remove the child from the old hierarchy, clean the old hierarchy, and dispatch root references.
            // Note: ProcessInternalChildLegNoSubtree can make structural changes and invalidate buffers.
            var oldChildHierarchy   = GetRootHierarchy(em, childClassification, false);
            var oldRootEntities     = TreeKernels.CopyHierarchyEntities(ref tsa, oldChildHierarchy.AsNativeArray());
            var oldAncestorEntities = GetAncestorEntitiesIfNeededForLeg(ref tsa, oldChildHierarchy.AsNativeArray(), childClassification.indexInHierarchy, options);
            TreeKernels.RemoveSoloFromHierarchy(ref oldChildHierarchy, childClassification.indexInHierarchy);
            CleanHierarchy(ref tsa, em, oldRoot, ref oldChildHierarchy, true, out var removeOldRootLeg);
            TreeKernels.UpdateRootReferencesFromDiff(oldChildHierarchy.AsNativeArray(), oldRootEntities, em);
            bool convertOldRootToSolo = oldChildHierarchy.Length < 2;

            // And then we insert the child into the new hierarchy. We do this before cleaning while we know the index of the parent.
            hierarchy = GetRootHierarchy(em, parentClassification, false);
            TreeKernels.UpdateLocalTransformsOfNewAncestorComponents(ancestryAddSets, hierarchy.AsNativeArray());
            var old = TreeKernels.CopyHierarchyEntities(ref tsa, hierarchy.AsNativeArray());
            TreeKernels.InsertSoloEntityIntoHierarchy(ref hierarchy, parentClassification.indexInHierarchy, child, flags);
            CleanHierarchy(ref tsa, em, root, ref hierarchy, !rootAddSet.linkedEntityGroup, out var removeRootLeg);
            if (rootAddSet.entityInHierarchyCleanup || em.HasBuffer<EntityInHierarchyCleanup>(parent))
            {
                var cleanup = em.GetBuffer<EntityInHierarchyCleanup>(root, false);
                TreeKernels.CopyHierarchyToCleanup(in hierarchy, ref cleanup);
            }
            TreeKernels.UpdateRootReferencesFromDiff(hierarchy.AsNativeArray(), old, em);

            // Now process LEG
            ProcessInternalChildLegNoSubtree(em, oldRoot, childClassification.isRootAlive, child, oldAncestorEntities, options, out bool removeChildLeg,
                                             out bool removeOldRootLeg2);
            removeOldRootLeg |= removeOldRootLeg2;
            if (options != SetParentOptions.IgnoreLinkedEntityGroup)
            {
                var leg = em.GetBuffer<LinkedEntityGroup>(root, false);
                TreeKernels.AddEntityToLeg(ref leg, child);
            }

            // Remove old root components
            if (convertOldRootToSolo)
                TreeKernels.RemoveRootComponents(em, oldRoot, removeOldRootLeg);
            else if (removeOldRootLeg)
                em.RemoveComponent<LinkedEntityGroup>(oldRoot);

            if (removeChildLeg)
                em.RemoveComponent<LinkedEntityGroup>(child);
            if (removeRootLeg)
                em.RemoveComponent<LinkedEntityGroup>(root);

            Validate(em, parent, child);

            tsa.Dispose();
        }
        #endregion

        #region Internal Children with Subtrees
        static void AddInternalChildWithSubtreeToSoloParent(EntityManager em,
                                                            Entity parent,
                                                            Entity child,
                                                            TreeKernels.TreeClassification childClassification,
                                                            InheritanceFlags flags,
                                                            SetParentOptions options)
        {
            var tsa = ThreadStackAllocator.GetAllocator();

            // Add only the components for the child. We don't yet know if the new root needs cleanup or not.
            var oldRoot      = childClassification.root;
            var childAddSet  = TreeKernels.GetChildComponentsToAdd(em, child, TreeKernels.TreeClassification.TreeRole.InternalWithChildren, flags);
            var parentAddSet = TreeKernels.GetParentComponentsToAdd(em, parent, TreeKernels.TreeClassification.TreeRole.Solo, childAddSet, options);
            TreeKernels.AddComponents(em, childAddSet);

            // We need the hierarchy index to extract the subtree, but we also need a clean subtree to get accurate LEG list.
            // Thus, we clean the hierarchy first, then find our entity in it. Then we can extract the subtree.
            var oldHierarchy        = GetRootHierarchy(em, childClassification, false);
            var oldChildEntities    = TreeKernels.CopyHierarchyEntities(ref tsa, oldHierarchy.AsNativeArray());
            var oldAncestorEntities = GetAncestorEntitiesIfNeededForLeg(ref tsa, oldHierarchy.AsNativeArray(), childClassification.indexInHierarchy, options);
            CleanHierarchy(ref tsa, em, oldRoot, ref oldHierarchy, childClassification.isRootAlive, out bool removeOldRootLeg);
            childClassification.indexInHierarchy = TreeKernels.FindEntityAfterChange(oldHierarchy.AsNativeArray(), child, childClassification.indexInHierarchy);
            var subtree                          = TreeKernels.ExtractSubtree(ref tsa, oldHierarchy.AsNativeArray(), childClassification.indexInHierarchy);
            TreeKernels.RemoveSubtreeFromHierarchy(ref tsa, ref oldHierarchy, childClassification.indexInHierarchy, subtree);
            TreeKernels.UpdateRootReferencesFromDiff(oldHierarchy.AsNativeArray(), oldChildEntities, em);
            bool convertOldRootToSolo = oldHierarchy.Length < 2;

            // Next, we need to remove the LEG from the old hierarchy
            ProcessInternalChildLegWithSubtree(ref tsa,
                                               em,
                                               oldRoot,
                                               childClassification.isRootAlive,
                                               child,
                                               oldAncestorEntities,
                                               subtree,
                                               options,
                                               out var removeChildLeg,
                                               out var removeOldRootLeg2,
                                               out var dstHierarchyNeedsCleanup,
                                               out var legEntitiesToAddToDst);
            parentAddSet.entityInHierarchyCleanup |= dstHierarchyNeedsCleanup;
            removeOldRootLeg                      |= removeOldRootLeg2;

            // Now we can add the root components
            TreeKernels.AddComponents(em, parentAddSet);

            // Build new hierarchy
            var hierarchy = em.GetBuffer<EntityInHierarchy>(parent, false);
            TreeKernels.BuildOriginalParentWithDescendantHierarchy(ref hierarchy, parent, subtree, flags);
            if (parentAddSet.entityInHierarchyCleanup)
            {
                var cleanup = em.GetBuffer<EntityInHierarchyCleanup>(parent, false);
                TreeKernels.CopyHierarchyToCleanup(in hierarchy, ref cleanup);
            }
            TreeKernels.UpdateRootReferencesFromDiff(hierarchy.AsNativeArray(), default, em);

            if (options != SetParentOptions.IgnoreLinkedEntityGroup)
            {
                var leg = em.GetBuffer<LinkedEntityGroup>(parent, false);
                if (legEntitiesToAddToDst.IsEmpty)
                    TreeKernels.AddEntityToLeg(ref leg, child);
                else
                    TreeKernels.AddEntitiesToLeg(ref leg, legEntitiesToAddToDst);
            }

            // Remove old root components
            if (convertOldRootToSolo)
                TreeKernels.RemoveRootComponents(em, oldRoot, removeOldRootLeg);
            else if (removeOldRootLeg)
                em.RemoveComponent<LinkedEntityGroup>(oldRoot);

            if (removeChildLeg)
                em.RemoveComponent<LinkedEntityGroup>(child);
            if (removeOldRootLeg)
                em.RemoveComponent<LinkedEntityGroup>(oldRoot);

            Validate(em, parent, child);

            tsa.Dispose();
        }

        static void AddInternalChildWithSubtreeToRootParentSameRoot(EntityManager em,
                                                                    Entity parent,
                                                                    Entity child,
                                                                    TreeKernels.TreeClassification childClassification,
                                                                    InheritanceFlags flags)
        {
            var tsa = ThreadStackAllocator.GetAllocator();

            // We do not need to account for ticked vs unticked in the ancestry, because the root should already have everything
            var hierarchy = em.GetBuffer<EntityInHierarchy>(parent, false);
            var old       = TreeKernels.CopyHierarchyEntities(ref tsa, hierarchy.AsNativeArray());
            var subtree   = TreeKernels.ExtractSubtree(ref tsa, hierarchy.AsNativeArray(), childClassification.indexInHierarchy);
            TreeKernels.RemoveSubtreeFromHierarchy(ref tsa, ref hierarchy, childClassification.indexInHierarchy, subtree);
            TreeKernels.InsertSubtreeIntoHierarchy(ref hierarchy, 0, subtree, flags);
            CleanHierarchy(ref tsa, em, parent, ref hierarchy, true, out var removeLeg);
            if (em.HasBuffer<EntityInHierarchyCleanup>(parent))
            {
                var cleanup = em.GetBuffer<EntityInHierarchyCleanup>(parent, false);
                TreeKernels.CopyHierarchyToCleanup(in hierarchy, ref cleanup);
            }
            TreeKernels.UpdateRootReferencesFromDiff(hierarchy.AsNativeArray(), old, em);

            // Cleaning can still result in LEG being removed.
            if (removeLeg)
                em.RemoveComponent<LinkedEntityGroup>(parent);

            Validate(em, parent, child);

            tsa.Dispose();
        }

        static void AddInternalChildWithSubtreeToRootParentDifferentRoot(EntityManager em,
                                                                         Entity parent,
                                                                         Entity child,
                                                                         TreeKernels.TreeClassification childClassification,
                                                                         InheritanceFlags flags,
                                                                         SetParentOptions options)
        {
            var tsa = ThreadStackAllocator.GetAllocator();

            // Add only the components for the child. We don't yet know if the new root needs cleanup or not.
            var oldRoot      = childClassification.root;
            var childAddSet  = TreeKernels.GetChildComponentsToAdd(em, child, TreeKernels.TreeClassification.TreeRole.InternalWithChildren, flags);
            var parentAddSet = TreeKernels.GetParentComponentsToAdd(em, parent, TreeKernels.TreeClassification.TreeRole.Solo, childAddSet, options);
            TreeKernels.AddComponents(em, childAddSet);

            // We need the hierarchy index to extract the subtree, but we also need a clean subtree to get accurate LEG list.
            // Thus, we clean the hierarchy first, then find our entity in it. Then we can extract the subtree.
            var oldHierarchy        = GetRootHierarchy(em, childClassification, false);
            var oldChildEntities    = TreeKernels.CopyHierarchyEntities(ref tsa, oldHierarchy.AsNativeArray());
            var oldAncestorEntities = GetAncestorEntitiesIfNeededForLeg(ref tsa, oldHierarchy.AsNativeArray(), childClassification.indexInHierarchy, options);
            CleanHierarchy(ref tsa, em, oldRoot, ref oldHierarchy, childClassification.isRootAlive, out bool removeOldRootLeg);
            childClassification.indexInHierarchy = TreeKernels.FindEntityAfterChange(oldHierarchy.AsNativeArray(), child, childClassification.indexInHierarchy);
            var subtree                          = TreeKernels.ExtractSubtree(ref tsa, oldHierarchy.AsNativeArray(), childClassification.indexInHierarchy);
            TreeKernels.RemoveSubtreeFromHierarchy(ref tsa, ref oldHierarchy, childClassification.indexInHierarchy, subtree);
            TreeKernels.UpdateRootReferencesFromDiff(oldHierarchy.AsNativeArray(), oldChildEntities, em);
            bool convertOldRootToSolo = oldHierarchy.Length < 2;

            // Next, we need to remove the LEG from the old hierarchy
            ProcessInternalChildLegWithSubtree(ref tsa,
                                               em,
                                               oldRoot,
                                               childClassification.isRootAlive,
                                               child,
                                               oldAncestorEntities,
                                               subtree,
                                               options,
                                               out var removeChildLeg,
                                               out var removeOldRootLeg2,
                                               out var dstHierarchyNeedsCleanup,
                                               out var legEntitiesToAddToDst);
            parentAddSet.entityInHierarchyCleanup |= dstHierarchyNeedsCleanup;
            removeOldRootLeg                      |= removeOldRootLeg2;

            // Now we can add the root components
            TreeKernels.AddComponents(em, parentAddSet);

            // Build new hierarchy
            var hierarchy = em.GetBuffer<EntityInHierarchy>(parent, false);
            var old       = TreeKernels.CopyHierarchyEntities(ref tsa, hierarchy.AsNativeArray());
            CleanHierarchy(ref tsa, em, parent, ref hierarchy, !parentAddSet.linkedEntityGroup, out var removeParentLeg);
            TreeKernels.InsertSubtreeIntoHierarchy(ref hierarchy, 0, subtree, flags);
            if (parentAddSet.entityInHierarchyCleanup)
            {
                var cleanup = em.GetBuffer<EntityInHierarchyCleanup>(parent, false);
                TreeKernels.CopyHierarchyToCleanup(in hierarchy, ref cleanup);
            }
            TreeKernels.UpdateRootReferencesFromDiff(hierarchy.AsNativeArray(), old, em);

            if (options != SetParentOptions.IgnoreLinkedEntityGroup)
            {
                var leg = em.GetBuffer<LinkedEntityGroup>(parent, false);
                if (legEntitiesToAddToDst.IsEmpty)
                    TreeKernels.AddEntityToLeg(ref leg, child);
                else
                    TreeKernels.AddEntitiesToLeg(ref leg, legEntitiesToAddToDst);
            }

            // Remove old root components
            if (convertOldRootToSolo)
                TreeKernels.RemoveRootComponents(em, oldRoot, removeOldRootLeg);
            else if (removeOldRootLeg)
                em.RemoveComponent<LinkedEntityGroup>(oldRoot);

            if (removeChildLeg)
                em.RemoveComponent<LinkedEntityGroup>(child);
            if (removeOldRootLeg)
                em.RemoveComponent<LinkedEntityGroup>(oldRoot);
            if (removeParentLeg)
                em.RemoveComponent<LinkedEntityGroup>(parent);

            Validate(em, parent, child);

            tsa.Dispose();
        }

        static void AddInternalChildWithSubtreeToInternalParentSameRoot(EntityManager em,
                                                                        Entity parent,
                                                                        TreeKernels.TreeClassification parentClassification,
                                                                        Entity child,
                                                                        TreeKernels.TreeClassification childClassification,
                                                                        InheritanceFlags flags)
        {
            var tsa = ThreadStackAllocator.GetAllocator();

            // We still need to account for ticked vs unticked in the ancestry
            var childAddSet     = TreeKernels.GetChildComponentsToAdd(em, child, TreeKernels.TreeClassification.TreeRole.InternalWithChildren, flags);
            var hierarchy       = GetRootHierarchy(em, parentClassification, false);
            var ancestryAddSets = TreeKernels.GetAncestorComponentsToAdd(ref tsa, em, hierarchy.AsNativeArray(), parentClassification, childAddSet, default);
            TreeKernels.AddComponents(em, ancestryAddSets);

            hierarchy = GetRootHierarchy(em, parentClassification, false);
            TreeKernels.UpdateLocalTransformsOfNewAncestorComponents(ancestryAddSets, hierarchy.AsNativeArray());
            var old     = TreeKernels.CopyHierarchyEntities(ref tsa, hierarchy.AsNativeArray());
            var subtree = TreeKernels.ExtractSubtree(ref tsa, hierarchy.AsNativeArray(), childClassification.indexInHierarchy);
            TreeKernels.RemoveSubtreeFromHierarchy(ref tsa, ref hierarchy, childClassification.indexInHierarchy, subtree);
            // When we remove from the hierarchy, our parent's index might have moved and we need to refind it
            parentClassification.indexInHierarchy = TreeKernels.FindEntityAfterChange(hierarchy.AsNativeArray(), parent, parentClassification.indexInHierarchy);
            TreeKernels.InsertSubtreeIntoHierarchy(ref hierarchy, parentClassification.indexInHierarchy, subtree, flags);
            CleanHierarchy(ref tsa, em, parentClassification.root, ref hierarchy, parentClassification.isRootAlive, out var removeLeg);
            if (parentClassification.isRootAlive && em.HasBuffer<EntityInHierarchyCleanup>(parentClassification.root))
            {
                var cleanup = em.GetBuffer<EntityInHierarchyCleanup>(parentClassification.root, false);
                TreeKernels.CopyHierarchyToCleanup(in hierarchy, ref cleanup);
            }
            TreeKernels.UpdateRootReferencesFromDiff(hierarchy.AsNativeArray(), old, em);

            // Cleaning can still result in LEG being removed.
            if (removeLeg)
                em.RemoveComponent<LinkedEntityGroup>(parentClassification.root);

            Validate(em, parent, child);

            tsa.Dispose();
        }

        static void AddInternalChildWithSubtreeToInternalParentDifferentRoots(EntityManager em,
                                                                              Entity parent,
                                                                              TreeKernels.TreeClassification parentClassification,
                                                                              Entity child,
                                                                              TreeKernels.TreeClassification childClassification,
                                                                              InheritanceFlags flags,
                                                                              SetParentOptions options)
        {
            var tsa = ThreadStackAllocator.GetAllocator();

            // Add only the components for the child. We don't yet know if the new root needs cleanup or not.
            var     oldRoot         = childClassification.root;
            var     root            = parentClassification.root;
            var     childAddSet     = TreeKernels.GetChildComponentsToAdd(em, child, TreeKernels.TreeClassification.TreeRole.InternalWithChildren, flags);
            var     hierarchy       = GetRootHierarchy(em, parentClassification, true);
            var     ancestryAddSets = TreeKernels.GetAncestorComponentsToAdd(ref tsa, em, hierarchy.AsNativeArray(), parentClassification, childAddSet, options);
            ref var rootAddSet      = ref ancestryAddSets[ancestryAddSets.Length - 1];
            TreeKernels.AddComponents(em, childAddSet);

            // We need the hierarchy index to extract the subtree, but we also need a clean subtree to get accurate LEG list.
            // Thus, we clean the hierarchy first, then find our entity in it. Then we can extract the subtree.
            var oldHierarchy        = GetRootHierarchy(em, childClassification, false);
            var oldChildEntities    = TreeKernels.CopyHierarchyEntities(ref tsa, oldHierarchy.AsNativeArray());
            var oldAncestorEntities = GetAncestorEntitiesIfNeededForLeg(ref tsa, oldHierarchy.AsNativeArray(), childClassification.indexInHierarchy, options);
            CleanHierarchy(ref tsa, em, oldRoot, ref oldHierarchy, childClassification.isRootAlive, out bool removeOldRootLeg);
            childClassification.indexInHierarchy = TreeKernels.FindEntityAfterChange(oldHierarchy.AsNativeArray(), child, childClassification.indexInHierarchy);
            var subtree                          = TreeKernels.ExtractSubtree(ref tsa, oldHierarchy.AsNativeArray(), childClassification.indexInHierarchy);
            TreeKernels.RemoveSubtreeFromHierarchy(ref tsa, ref oldHierarchy, childClassification.indexInHierarchy, subtree);
            TreeKernels.UpdateRootReferencesFromDiff(oldHierarchy.AsNativeArray(), oldChildEntities, em);
            bool convertOldRootToSolo = oldHierarchy.Length < 2;

            // Next, we need to remove the LEG from the old hierarchy
            ProcessInternalChildLegWithSubtree(ref tsa,
                                               em,
                                               oldRoot,
                                               childClassification.isRootAlive,
                                               child,
                                               oldAncestorEntities,
                                               subtree,
                                               options,
                                               out var removeChildLeg,
                                               out var removeOldRootLeg2,
                                               out var dstHierarchyNeedsCleanup,
                                               out var legEntitiesToAddToDst);
            rootAddSet.entityInHierarchyCleanup |= dstHierarchyNeedsCleanup;
            removeOldRootLeg                    |= removeOldRootLeg2;

            // Now we can add the other components
            TreeKernels.AddComponents(em, ancestryAddSets);

            // Build new hierarchy
            hierarchy = GetRootHierarchy(em, parentClassification, false);
            TreeKernels.UpdateLocalTransformsOfNewAncestorComponents(ancestryAddSets, hierarchy.AsNativeArray());
            var old = TreeKernels.CopyHierarchyEntities(ref tsa, hierarchy.AsNativeArray());
            CleanHierarchy(ref tsa, em, root, ref hierarchy, !rootAddSet.linkedEntityGroup, out var removeRootLeg);
            TreeKernels.InsertSubtreeIntoHierarchy(ref hierarchy, parentClassification.indexInHierarchy, subtree, flags);
            if (rootAddSet.entityInHierarchyCleanup)
            {
                var cleanup = em.GetBuffer<EntityInHierarchyCleanup>(root, false);
                TreeKernels.CopyHierarchyToCleanup(in hierarchy, ref cleanup);
            }
            TreeKernels.UpdateRootReferencesFromDiff(hierarchy.AsNativeArray(), old, em);

            if (options != SetParentOptions.IgnoreLinkedEntityGroup)
            {
                var leg = em.GetBuffer<LinkedEntityGroup>(root, false);
                if (legEntitiesToAddToDst.IsEmpty)
                    TreeKernels.AddEntityToLeg(ref leg, child);
                else
                    TreeKernels.AddEntitiesToLeg(ref leg, legEntitiesToAddToDst);
            }

            // Remove old root components
            if (convertOldRootToSolo)
                TreeKernels.RemoveRootComponents(em, oldRoot, removeOldRootLeg);
            else if (removeOldRootLeg)
                em.RemoveComponent<LinkedEntityGroup>(oldRoot);

            if (removeChildLeg)
                em.RemoveComponent<LinkedEntityGroup>(child);
            if (removeOldRootLeg)
                em.RemoveComponent<LinkedEntityGroup>(oldRoot);
            if (removeRootLeg)
                em.RemoveComponent<LinkedEntityGroup>(root);

            Validate(em, parent, child);

            tsa.Dispose();
        }
        #endregion

        #region Subprocesses
        static DynamicBuffer<EntityInHierarchy> GetRootHierarchy(EntityManager em, TreeKernels.TreeClassification classification, bool isReadOnly)
        {
            if (classification.isRootAlive)
                return em.GetBuffer<EntityInHierarchy>(classification.root, isReadOnly);
            else
                return em.GetBuffer<EntityInHierarchyCleanup>(classification.root, isReadOnly).Reinterpret<EntityInHierarchy>();
        }

        static void CleanHierarchy(ref ThreadStackAllocator parentTsa,
                                   EntityManager em,
                                   Entity rootToClean,
                                   ref DynamicBuffer<EntityInHierarchy> hierarchy,
                                   bool checkForLeg,
                                   out bool removeLeg)
        {
            removeLeg = false;
            if (checkForLeg && em.HasBuffer<LinkedEntityGroup>(rootToClean))
            {
                var leg = em.GetBuffer<LinkedEntityGroup>(rootToClean, false);
                TreeKernels.RemoveDeadDescendantsFromHierarchyAndLeg(ref parentTsa, ref hierarchy, ref leg, em);
                removeLeg = leg.Length < 2;
            }
            else
                TreeKernels.RemoveDeadDescendantsFromHierarchy(ref parentTsa, ref hierarchy, em);
        }

        static void ProcessRootChildLeg(ref ThreadStackAllocator tsa,
                                        EntityManager em,
                                        Entity child,
                                        in ReadOnlySpan<EntityInHierarchy> hierarchy,
                                        SetParentOptions options,
                                        out bool removeLeg,
                                        out bool dstHierarchyNeedsCleanup,
                                        out Span<Entity>                   entitiesInLegAndHierarchy)
        {
            dstHierarchyNeedsCleanup = false;
            removeLeg                = false;
            if (options != SetParentOptions.IgnoreLinkedEntityGroup && em.HasBuffer<LinkedEntityGroup>(child))
            {
                var  childLeg = em.GetBuffer<LinkedEntityGroup>(child, options == SetParentOptions.AttachLinkedEntityGroup);
                bool matchedAll;
                if (options == SetParentOptions.AttachLinkedEntityGroup)
                    entitiesInLegAndHierarchy = TreeKernels.GetHierarchyEntitiesInLeg(ref tsa, hierarchy, childLeg.Reinterpret<Entity>().AsNativeArray(), out matchedAll);
                else
                {
                    entitiesInLegAndHierarchy = TreeKernels.GetAndRemoveHierarchyEntitiesFromLeg(ref tsa, ref childLeg, hierarchy, out matchedAll);
                    if (childLeg.Length < 2)
                        removeLeg = true;
                }
                dstHierarchyNeedsCleanup = !matchedAll;
            }
            else
            {
                dstHierarchyNeedsCleanup  = true;
                entitiesInLegAndHierarchy = default;
            }
        }

        static Span<Entity> GetAncestorEntitiesIfNeededForLeg(ref ThreadStackAllocator tsa, in ReadOnlySpan<EntityInHierarchy> hierarchy, int childIndex, SetParentOptions options)
        {
            if (options == SetParentOptions.IgnoreLinkedEntityGroup)
                return default;
            return TreeKernels.GetAncestryEntitiesExcludingRoot(ref tsa, hierarchy, childIndex);
        }

        static void ProcessInternalChildLegNoSubtree(EntityManager em,
                                                     Entity root,
                                                     bool isRootAlive,
                                                     Entity child,
                                                     in ReadOnlySpan<Entity> ancestorEntities,
                                                     SetParentOptions options,
                                                     out bool removeLegFromChild,
                                                     out bool removeLegFromRoot)
        {
            removeLegFromChild = false;
            removeLegFromRoot  = false;
            if (options == SetParentOptions.IgnoreLinkedEntityGroup)
                return;

            if (options == SetParentOptions.TransferLinkedEntityGroup && em.HasBuffer<LinkedEntityGroup>(child))
            {
                if (em.GetBuffer<LinkedEntityGroup>(child, true).Length < 2)
                    removeLegFromChild = true;
            }

            if (isRootAlive && em.HasBuffer<LinkedEntityGroup>(root))
            {
                var rootLeg = em.GetBuffer<LinkedEntityGroup>(root, false);
                TreeKernels.RemoveEntityFromLeg(ref rootLeg, child, out var matched);
                removeLegFromRoot = rootLeg.Length < 2;
            }

            foreach (var e in ancestorEntities)
            {
                if (em.IsAlive(e) && em.HasBuffer<LinkedEntityGroup>(e))
                {
                    var leg = em.GetBuffer<LinkedEntityGroup>(e);
                    TreeKernels.RemoveEntityFromLeg(ref leg, child, out _);
                    if (leg.Length < 2)
                        em.RemoveComponent<LinkedEntityGroup>(e);
                }
            }
        }

        static void ProcessInternalChildLegWithSubtree(ref ThreadStackAllocator tsa,
                                                       EntityManager em,
                                                       Entity root,
                                                       bool isRootAlive,
                                                       Entity child,
                                                       in ReadOnlySpan<Entity>            ancestorEntities,
                                                       in ReadOnlySpan<EntityInHierarchy> subtree,
                                                       SetParentOptions options,
                                                       out bool removeLegFromChild,
                                                       out bool removeLegFromRoot,
                                                       out bool dstHierarchyNeedsCleanup,
                                                       out Span<Entity>                   legEntitiesToAddToDst)
        {
            removeLegFromChild       = false;
            removeLegFromRoot        = false;
            dstHierarchyNeedsCleanup = true;
            legEntitiesToAddToDst    = default;
            if (options == SetParentOptions.IgnoreLinkedEntityGroup)
                return;

            if (options == SetParentOptions.TransferLinkedEntityGroup && em.HasBuffer<LinkedEntityGroup>(child))
            {
                var childLeg = em.GetBuffer<LinkedEntityGroup>(child, false);
                TreeKernels.RemoveHierarchyEntitiesFromLeg(ref childLeg, subtree);
                if (childLeg.Length < 2)
                    removeLegFromChild = true;
            }

            if (isRootAlive && em.HasBuffer<LinkedEntityGroup>(root))
            {
                var rootLeg              = em.GetBuffer<LinkedEntityGroup>(root, false);
                legEntitiesToAddToDst    = TreeKernels.GetAndRemoveHierarchyEntitiesFromLeg(ref tsa, ref rootLeg, subtree, out bool matchedAll);
                removeLegFromRoot        = rootLeg.Length < 2;
                dstHierarchyNeedsCleanup = !matchedAll;
            }

            foreach (var e in ancestorEntities)
            {
                if (em.IsAlive(e) && em.HasBuffer<LinkedEntityGroup>(e))
                {
                    var leg = em.GetBuffer<LinkedEntityGroup>(e);
                    TreeKernels.RemoveHierarchyEntitiesFromLeg(ref leg, subtree);
                    if (leg.Length < 2)
                        em.RemoveComponent<LinkedEntityGroup>(e);
                }
            }
        }
        #endregion

        [Conditional("VALIDATE")]
        static void Validate(EntityManager em, Entity parent, Entity child)
        {
            var rootRef = em.GetComponentData<RootReference>(child);
            var handle  = rootRef.ToHandle(em);
            if (handle.entity != child)
                throw new System.InvalidOperationException("Child handle is invalid.");
            var parentHandle = handle.bloodParent;
            if (parentHandle.entity != parent)
                throw new System.InvalidOperationException("Parent handle is invalid.");
            var last = handle.GetFromIndexInHierarchy(handle.totalInHierarchy - 1);
            if (last.m_hierarchy[last.indexInHierarchy].firstChildIndex != last.totalInHierarchy)
            {
                throw new System.InvalidOperationException($"Bad things happened during validation. Last did not match hierarchy length. root: {handle.root.entity}");
            }
            for (int i = 1; i < last.m_hierarchy.Length; i++)
            {
                var b = last.m_hierarchy[i];
                var a = last.m_hierarchy[i - 1];
                if (b.firstChildIndex != a.firstChildIndex + a.childCount)
                    throw new System.InvalidOperationException($"Bad things happened during validation. Index {i} has bad indexing. root: {handle.root.entity}");
            }
            if (handle.entity != child || parentHandle.entity != parent)
            {
                throw new System.InvalidOperationException("Our entities got mixed up.");
            }
        }
    }
}
#endif

