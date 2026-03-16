#if !LATIOS_TRANSFORMS_UNITY
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Transforms
{
    public enum SetParentOptions : byte
    {
        /// <summary>
        /// If the child is already a child of a previous hierarchy, then it and its descendants are removed from all
        /// old ancestor LinkedEntityGroups. Every entity that was removed from the old root is added to the new root's
        /// LinkedEntityGroup. The child retains its own LinkedEntityGroup. If the child was formerly a root, then every
        /// entity in the hierarchy present in its own LinkedEntityGroup is added to to the new root's LinkedEntityGroup.
        /// If no LinkedEntityGroup exists on the child nor its root, then only the child is added to the new root's
        /// LinkedEntityGroup. EntityInHierarchyCleanup is added to the new root if not all entities in the child's
        /// descendants are added to the new root.
        /// </summary>
        AttachLinkedEntityGroup,
        /// <summary>
        /// Same as AttachLinkedEntityGroup, except that every entity added to the new root is removed from the child's
        /// LinkedEntityGroup (other than the child itself).
        /// </summary>
        TransferLinkedEntityGroup,
        /// <summary>
        /// LinkedEntityGroup is left untouched. EntityInHierarchyCleanup is added to the new root to ensure no dangling
        /// RootReferences are left behind. This has a cost associated with it.
        /// </summary>
        IgnoreLinkedEntityGroup
    }

    public enum ClearParentOptions : byte
    {
        /// <summary>
        /// The child and all of its descendants are removed from the LinkedEntityGroup of all old ancestors. Every entity
        /// that was removed from the old root is added to the child's LinkedEntityGroup. If the child has children of its
        /// own, a LinkedEntityGroup is added to the child if it does not already exist.
        /// </summary>
        TransferLinkedEntityGroup,
        /// <summary>
        /// The child and every descendant which is part of the old root's LinkedEntityGroup is added to the child's
        /// LinkedEntityGroup. If the child has children of its own, a LinkedEntityGroup is added to the child if it
        /// does not already exist.
        /// </summary>
        AddLinkedEntityGroup,
        /// <summary>
        /// LinkedEntityGroup is left untouched. EntityInHierarchyCleanup may be added to the child to ensure no dangling
        /// RootReferences on its descendants are left behind if the child were to be destroyed. This has a cost associated
        /// with it.
        /// </summary>
        IgnoreLinkedEntityGroup
    }

    public static unsafe partial class TransformTools
    {
        /// <summary>
        /// Assigns a new parent to the entity, updating all hierarchy information between the two entities involved.
        /// If the child entity is missing its WorldTransform (or TickedWorldTransform if it has TickedEntityTag),
        /// then that component will be added. The parent and all ancestry will have WorldTransform and/or
        /// TickedWorldTransform added to match what is present on the child. During the process, all hierarchies
        /// touched are cleaned. If any entity sees its LinkedEntityGroup size decrease below 2, then LinkedEntityGroup
        /// is removed from that entity.
        /// </summary>
        /// <param name="child">The entity which should have its parent assigned</param>
        /// <param name="parent">The target parent</param>
        /// <param name="inheritanceFlags">The inheritance flags the child will use</param>
        /// <param name="setParentOptions">The options for handling LinkedEntityGroup on the entities</param>
        public static void SetParent(this EntityManager em,
                                     Entity child,
                                     Entity parent,
                                     InheritanceFlags inheritanceFlags = InheritanceFlags.Normal,
                                     SetParentOptions setParentOptions = SetParentOptions.AttachLinkedEntityGroup)
        {
            TreeChangeMainThread.SetParent(em, parent, child, inheritanceFlags, setParentOptions);
            return;
        }

        /// <summary>
        /// If the entity belongs to a hierarchy, the hierarchy is pruned of all dead entities. This may force a root to
        /// become a solo entity and potentially lose its LinkedEntityGroup if the size of either becomes less than 2.
        /// If the entity does not belong to a hierarchy, this method does nothing.
        /// </summary>
        /// <param name="entity">The entity whose hierarchy should be cleaned</param>
        public static void CleanHierarchy(this EntityManager em, Entity entity)
        {
            TreeChangeMainThread.CleanHierarchy(em, entity);
        }

        /// <summary>
        /// Removes the child and its descendants (if any) from its parent. If the entity is not a child, this method does nothing.
        /// During the process, all hierarchies touched are cleaned. If any entity sees its LinkedEntityGroup size decrease below 2,
        /// then LinkedEneityGroup will be removed from that entity.
        /// </summary>
        /// <param name="child">The entity which should be removed from its parent</param>
        /// <param name="clearParentOptions">The options for handling LinkedEntityGroup on the entities</param>
        public static void ClearParent(this EntityManager em, Entity child, ClearParentOptions clearParentOptions = ClearParentOptions.TransferLinkedEntityGroup)
        {
            TreeChangeMainThread.ClearParent(em, child, clearParentOptions);
        }
    }
}
#endif

