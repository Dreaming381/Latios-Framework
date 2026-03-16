#if !LATIOS_TRANSFORMS_UNITY
using System.Diagnostics;
using static Latios.Transforms.TransformTools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Transforms
{
    /// <summary>
    /// A handle into an entity within a hierarchy. This type allows for fast hierarchy traversal.
    /// </summary>
    public unsafe struct EntityInHierarchyHandle
    {
        internal NativeArray<EntityInHierarchy> m_hierarchy;
        internal EntityInHierarchy*             m_extraHierarchy;
        internal int                            m_index;

        /// <summary>
        /// True if this handle is invalid.
        /// </summary>
        public bool isNull => !m_hierarchy.IsCreated;
        /// <summary>
        /// True if this handle refers to the root of the hierarchy. That is, this entity does not have
        /// a bloodParent. It is possible for an entity to not have an alive parent, but not be the root.
        /// </summary>
        public bool isRoot => m_index == 0 && m_hierarchy.IsCreated;
        /// <summary>
        /// The entity currently being referred to.
        /// </summary>
        public Entity entity => (isNull ? default : m_hierarchy[m_index].entity);
        /// <summary>
        /// The current index of this handle within the hierarchy.
        /// </summary>
        public int indexInHierarchy => m_index;
        /// <summary>
        /// The inheritance flags this handle has set
        /// </summary>
        public InheritanceFlags inheritanceFlags => (isRoot ? default : m_hierarchy[m_index].m_flags);
        /// <summary>
        /// The total number of entities (dead or alive) in the hierarchy.
        /// </summary>
        public int totalInHierarchy => m_hierarchy.Length;
        /// <summary>
        /// Returns the handle that refers to the root of the hierarchy.
        /// </summary>
        public EntityInHierarchyHandle root => new EntityInHierarchyHandle
        {
            m_hierarchy      = m_hierarchy,
            m_extraHierarchy = m_extraHierarchy,
            m_index          = 0,
        };
        /// <summary>
        /// Returns the handle that refers to the internal immediate parent of this current handle.
        /// That handle may or may not refer to a dead entity. If the current handle is the root,
        /// this returns a null handle.
        /// </summary>
        public EntityInHierarchyHandle bloodParent => (isRoot ? default : new EntityInHierarchyHandle
                                                           {
                                                               m_hierarchy      = m_hierarchy,
                                                               m_extraHierarchy = m_extraHierarchy,
                                                               m_index          = m_hierarchy[m_index].parentIndex
                                                           });
        /// <summary>
        /// Returns an indexable collection of the blood children of the current handle. Each child may
        /// or may not refer to a dead entity. And inherited orphaned children are not accounted for.
        /// </summary>
        public BloodChildrenIndexer bloodChildren => new BloodChildrenIndexer(this);
        /// <summary>
        /// Returns the index of this child handle within the bloodChildren collection of its parent
        /// </summary>
        public int bloodSiblingIndex
        {
            get
            {
                if (isRoot)
                    return 0;
                var firstChildIndex = m_hierarchy[m_hierarchy[m_index].parentIndex].firstChildIndex;
                return m_index - firstChildIndex;
            }
        }

        /// <summary>
        /// Returns true if this handle is alive
        /// </summary>
        public bool IsAlive(EntityManager entityManager) => entityManager.IsAlive(entity);
        /// <summary>
        /// Returns true if this handle is alive
        /// </summary>
        public bool IsAlive(EntityStorageInfoLookup lookup) => lookup.IsAlive(entity);

        /// <summary>
        /// Gets a handle in this hierarchy using the specified index.
        /// </summary>
        public EntityInHierarchyHandle GetFromIndexInHierarchy(int indexInHierarchy)
        {
            if (indexInHierarchy < 0 || indexInHierarchy >= m_hierarchy.Length)
                return default;
            return new EntityInHierarchyHandle
            {
                m_hierarchy      = m_hierarchy,
                m_extraHierarchy = m_extraHierarchy,
                m_index          = indexInHierarchy
            };
        }

        /// <summary>
        /// Finds the alive parent of the entity, if one exists
        /// </summary>
        /// <param name="entityManager">The EntityManager this entity is managed by</param>
        /// <returns>The alive parent's handle if found, otherwise a null handle</returns>
        public EntityInHierarchyHandle FindParent(EntityManager entityManager) => FindParent(ref EntityManagerAccess.From(ref entityManager));
        /// <summary>
        /// Finds the alive parent of the entity, if one exists
        /// </summary>
        /// <param name="entityStorageInfoLookup">An EntityStorageInfoLookup belonging the world the hierarchy is from</param>
        /// <returns>The alive parent's handle if found, otherwise a null handle</returns>
        public EntityInHierarchyHandle FindParent(EntityStorageInfoLookup entityStorageInfoLookup) => FindParent(ref EsilAlive.From(ref entityStorageInfoLookup));

        internal EntityInHierarchyHandle FindParent<T>(ref T alive) where T : unmanaged, IAlive
        {
            if (isRoot)
                return default;

            var bp = bloodParent;
            while (!bp.isRoot)
            {
                if (alive.IsAlive(bp.entity))
                    return bp;
                bp = bp.bloodParent;
            }
            if (alive.IsAlive(bp.entity))
                return bp;
            return default;
        }

        /// <summary>
        /// Counts the number of descendants.
        /// </summary>
        /// <returns>The number of descendants. Does not include this handle itself in the count.</returns>
        public int CountBloodDescendants()
        {
            if (isRoot)
                return m_hierarchy.Length - 1;

            var count           = 0;
            var nextFirstParent = m_index;
            var nextParentCount = 1;
            while (nextParentCount > 0)
            {
                var firstParent       = m_hierarchy[nextFirstParent];
                var lastParent        = m_hierarchy[nextFirstParent - 1 + nextParentCount];
                var firstChild        = firstParent.firstChildIndex;
                var onePastLastChild  = lastParent.firstChildIndex + lastParent.childCount;
                nextFirstParent       = firstChild;
                nextParentCount       = onePastLastChild - firstChild;
                count                += nextParentCount;
            }
            return count;
        }

        internal bool isCopyParent => !isNull && !isRoot && m_hierarchy[m_index].m_flags.HasCopyParent();

        /// <summary>
        /// A collection of blood children in the hierarchy
        /// </summary>
        public struct BloodChildrenIndexer
        {
            EntityInHierarchyHandle handle;

            internal BloodChildrenIndexer(EntityInHierarchyHandle eihh)
            {
                handle = eihh;
            }

            /// <summary>
            /// Gets a handle to the blood child at the specified index
            /// </summary>
            public EntityInHierarchyHandle this[int index]
            {
                get
                {
                    var current = handle.m_hierarchy[handle.m_index];
                    CheckBloodChildrenIndexLength(index, current.childCount, current.entity);
                    return new EntityInHierarchyHandle
                    {
                        m_hierarchy      = handle.m_hierarchy,
                        m_extraHierarchy = handle.m_extraHierarchy,
                        m_index          = current.firstChildIndex + index
                    };
                }
            }

            /// <summary>
            /// The number of blood children in this collection
            /// </summary>
            public int length => handle.m_hierarchy[handle.m_index].childCount;
            /// <summary>
            /// Returns true if there are no blood children in this collection
            /// </summary>
            public bool isEmpty => length == 0;
            /// <summary>
            /// Returns an enumerator over the collection of blood children
            /// </summary>
            public Enumerator GetEnumerator() => new Enumerator(handle);

            /// <summary>
            /// An enumerator over the collection of blood children
            /// </summary>
            public struct Enumerator
            {
                NativeArray<EntityInHierarchy> hierarchy;
                EntityInHierarchy*             extraHierarchy;
                int                            currentIndex;
                int                            lastIndex;

                internal Enumerator(EntityInHierarchyHandle handle)
                {
                    hierarchy      = handle.m_hierarchy;
                    extraHierarchy = handle.m_extraHierarchy;
                    var current    = hierarchy[handle.m_index];
                    currentIndex   = current.firstChildIndex - 1;
                    lastIndex      = currentIndex + current.childCount;
                }

                /// <summary>
                /// The current blood child of the enumerator. May point to a valid but incorrect handle if MoveNext() is not called initially.
                /// </summary>
                public EntityInHierarchyHandle Current => new EntityInHierarchyHandle
                {
                    m_hierarchy      = hierarchy,
                    m_extraHierarchy = extraHierarchy,
                    m_index          = currentIndex
                };
                /// <summary>
                /// Advance the enumerator
                /// </summary>
                public bool MoveNext()
                {
                    currentIndex++;
                    return currentIndex <= lastIndex;
                }
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            static void CheckBloodChildrenIndexLength(int index, int childCount, Entity owner)
            {
                if (index < 0 || index >= childCount)
                    throw new System.ArgumentOutOfRangeException(
                        $"Index {index} is outside the range of blood children owned by entity {owner.ToFixedString()} which has {childCount} blood children.");
            }
        }
    }
}
#endif

