#if !LATIOS_TRANSFORMS_UNITY
using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Transforms
{
    internal static class TreeChangeSafetyChecks
    {
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        public static void CheckChangeParent(EntityManager em, Entity parent, Entity child, InheritanceFlags flags, SetParentOptions options)
        {
            if (parent == child)
                throw new ArgumentException($"Cannot make an entity a child of itself. {parent.ToFixedString()}");
            if (!em.Exists(parent))
                throw new ArgumentException($"The parent does not exist. Parent: {parent.ToFixedString()}  Child: {child.ToFixedString()}");
            if (!em.IsAlive(parent))
                throw new ArgumentException($"The parent has been destroyed. Parent: {parent.ToFixedString()}  Child: {child.ToFixedString()}");
            if (!em.Exists(child))
                throw new ArgumentException($"The child does not exist. Parent: {parent.ToFixedString()}  Child: {child.ToFixedString()}");
            if (!em.IsAlive(child))
                throw new ArgumentException($"The child has been destroyed. Parent: {parent.ToFixedString()}  Child: {child.ToFixedString()}");
            if (options != SetParentOptions.IgnoreLinkedEntityGroup && em.HasComponent<RootReference>(parent))
            {
                var rootRef = em.GetComponentData<RootReference>(parent);
                if (!em.IsAlive(rootRef.rootEntity))
                    throw new InvalidOperationException(
                        $"Cannot add LinkedEntityGroup to a new hierarchy whose root has been destroyed. Root: {rootRef.rootEntity.ToFixedString()}");
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        public static void CheckLegEntitiesHaveValidRootReferences(EntityManager em, Entity child)
        {
            if (!em.HasBuffer<LinkedEntityGroup>(child))
                return;

            var leg = em.GetBuffer<LinkedEntityGroup>(child, true).Reinterpret<Entity>().AsNativeArray();
            for (int i = 1; i < leg.Length; i++)
            {
                var e = leg[i];
                if (em.HasComponent<RootReference>(e))
                {
                    var rr     = em.GetComponentData<RootReference>(e);
                    var handle = rr.ToHandle(em);
                    if (handle.entity != e)
                        throw new System.NotSupportedException(
                            $"Child {child.ToFixedString()} appears to be an instantiated entity, as its RootReference clones another entity. In it's LinkedEntityGroup, another entity also appears to be a clone. This is not supported at this time.");
                }
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        public static void CheckInternalParentHasValidRootReference(EntityManager em, Entity parent, in TreeKernels.TreeClassification parentClassification)
        {
            var rr     = new RootReference { m_rootEntity = parentClassification.root, m_indexInHierarchy = parentClassification.indexInHierarchy };
            var handle                                                                                    = rr.ToHandle(em);
            if (handle.entity != parent)
                throw new System.InvalidOperationException(
                    $"Parent {parent.ToFixedString()} appears to be an instantiated entity, as its RootReference clones another entity. It cannot become a parent until this issue is corrected.");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        public static void CheckNotAssigningChildToDescendant(EntityManager em,
                                                              Entity parent,
                                                              Entity child,
                                                              TreeKernels.TreeClassification parentClassification,
                                                              TreeKernels.TreeClassification childClassification)
        {
            if (childClassification.indexInHierarchy > parentClassification.indexInHierarchy)
                return;
            var buffer = em.HasBuffer<EntityInHierarchy>(parentClassification.root) ? em.GetBuffer<EntityInHierarchy>(parentClassification.root,
                                                                                                                      true) : em.GetBuffer<EntityInHierarchyCleanup>(
                parentClassification.root,
                true).Reinterpret<EntityInHierarchy>();
            var hierarchy = buffer.AsNativeArray();
            for (int i = hierarchy[parentClassification.indexInHierarchy].parentIndex; i > 0; i = hierarchy[i].parentIndex)
            {
                if (hierarchy[i].entity == child)
                    throw new System.ArgumentException(
                        $"Cannot make an entity a child of one of its own descendants. Reassign the descendant's parent first. Parent: {parent.ToFixedString()}  Child: {child.ToFixedString()}");
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        public static void CheckNotAssigningRootChildToDescendant(Entity parent, Entity rootChild, TreeKernels.TreeClassification parentClassification)
        {
            if (parentClassification.root == rootChild)
                throw new System.ArgumentException(
                    $"Cannot make an entity a child of one of its own descendants. Reassign the descendant's parent first. Parent: {parent.ToFixedString()}  Child: {rootChild.ToFixedString()}");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        public static void CheckChildToRemoveIsAlive(EntityManager em, Entity child)
        {
            if (!em.IsAlive(child))
                throw new System.ArgumentException("Cannot convert a dead child into a hierarchy root. Consider cleaning the hierarchy instead.");
        }
    }
}
#endif

