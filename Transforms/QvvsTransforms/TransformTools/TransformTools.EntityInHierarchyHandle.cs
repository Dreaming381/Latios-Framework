#if !LATIOS_TRANSFORMS_UNITY
using Unity.Entities;

namespace Latios.Transforms
{
    public static unsafe partial class TransformTools
    {
        #region Extensions
        /// <summary>
        /// Resolves the EntityInHierarchyHandle for the specified RootReference, allowing for fast hierarchy traversal.
        /// </summary>
        /// <param name="entityManager">The EntityManager which manages the entity this RootReference came from</param>
        /// <returns>An EntityInHierarchyHandle referring to the spot in the hierarchy that the entity this RootReference
        /// belongs to is located</returns>
        public static EntityInHierarchyHandle ToHandle(this RootReference rootRef, EntityManager entityManager)
        {
            return rootRef.ToHandle(ref EntityManagerAccess.From(ref entityManager));
        }

        /// <summary>
        /// Resolves the EntityInHierarchyHandle for the specified RootReference, allowing for fast hierarchy traversal.
        /// </summary>
        /// <param name="componentBroker">A ComponentBroker with read access to EntityInHierarchy and EntityInHierarchyCleanup</param>
        /// <returns>An EntityInHierarchyHandle referring to the spot in the hierarchy that the entity this RootReference
        /// belongs to is located</returns>
        public static EntityInHierarchyHandle ToHandle(this RootReference rootRef, ref ComponentBroker componentBroker)
        {
            return rootRef.ToHandle(ref ComponentBrokerAccess.From(ref componentBroker));
        }

        /// <summary>
        /// Resolves the EntityInHierarchyHandle for the specified RootReference, allowing for fast hierarchy traversal.
        /// </summary>
        /// <param name="entityInHierarchyLookupRO">A readonly BufferLookup to the EntityInHierarchy dynamic buffer</param>
        /// <param name="entityInHierarchyCleanupLookupRO">A readonly BufferLookup to the EntityInHierarchyCleanup dynamic buffer</param>
        /// <returns>An EntityInHierarchyHandle referring to the spot in the hierarchy that the entity this RootReference
        /// belongs to is located</returns>
        public static EntityInHierarchyHandle ToHandle(this RootReference rootRef, ref BufferLookup<EntityInHierarchy> entityInHierarchyLookupRO,
                                                       ref BufferLookup<EntityInHierarchyCleanup> entityInHierarchyCleanupLookupRO)
        {
            ComponentLookup<RootReference> dummy           = default;
            var                            hierarchyAccess = new LookupHierarchy(dummy, entityInHierarchyLookupRO, entityInHierarchyCleanupLookupRO);
            var                            result          = rootRef.ToHandle(ref hierarchyAccess);
            hierarchyAccess.WriteBack(ref dummy, ref entityInHierarchyLookupRO, ref entityInHierarchyCleanupLookupRO);
            return result;
        }

        internal static unsafe EntityInHierarchyHandle ToHandle<T>(this RootReference rootRef, ref T hierarchyAccess) where T : unmanaged, IHierarchy
        {
            bool hasHierarchy = hierarchyAccess.TryGetEntityInHierarchy(rootRef.rootEntity, out var hierarchy);
            bool hasCleanup   = hierarchyAccess.TryGetEntityInHierarchyCleanup(rootRef.rootEntity, out var cleanup);
            if (hasHierarchy && !hasCleanup)
            {
                return new EntityInHierarchyHandle
                {
                    m_hierarchy      = hierarchy.AsNativeArray(),
                    m_extraHierarchy = null,
                    m_index          = rootRef.indexInHierarchy,
                };
            }
            else if (hasHierarchy && hasCleanup)
            {
                return new EntityInHierarchyHandle
                {
                    m_hierarchy      = hierarchy.AsNativeArray(),
                    m_extraHierarchy = (EntityInHierarchy*)cleanup.GetUnsafeReadOnlyPtr(),
                    m_index          = 0
                };
            }
            return new EntityInHierarchyHandle
            {
                m_hierarchy      = cleanup.Reinterpret<EntityInHierarchy>().AsNativeArray(),
                m_extraHierarchy = null,
                m_index          = 0
            };
        }
        #endregion

        #region Internal
        internal static unsafe EntityInHierarchyHandle GetHierarchyHandle(Entity entity, EntityManager entityManager)
        {
            if (entityManager.HasComponent<RootReference>(entity))
            {
                var rootRef = entityManager.GetComponentData<RootReference>(entity);
                return rootRef.ToHandle(entityManager);
            }
            bool hasHierarchy = entityManager.HasBuffer<EntityInHierarchy>(entity);
            bool hasCleanup   = entityManager.HasBuffer<EntityInHierarchyCleanup>(entity);
            if (hasHierarchy && !hasCleanup)
            {
                return new EntityInHierarchyHandle
                {
                    m_hierarchy      = entityManager.GetBuffer<EntityInHierarchy>(entity, true).AsNativeArray(),
                    m_extraHierarchy = null,
                    m_index          = 0
                };
            }
            else if (hasHierarchy && hasCleanup)
            {
                return new EntityInHierarchyHandle
                {
                    m_hierarchy      = entityManager.GetBuffer<EntityInHierarchy>(entity, true).AsNativeArray(),
                    m_extraHierarchy = (EntityInHierarchy*)entityManager.GetBuffer<EntityInHierarchyCleanup>(entity, true).GetUnsafeReadOnlyPtr(),
                    m_index          = 0
                };
            }
            else if (hasCleanup)
            {
                return new EntityInHierarchyHandle
                {
                    m_hierarchy      = entityManager.GetBuffer<EntityInHierarchyCleanup>(entity, true).Reinterpret<EntityInHierarchy>().AsNativeArray(),
                    m_extraHierarchy = null,
                    m_index          = 0
                };
            }
            return default;
        }

        internal static unsafe EntityInHierarchyHandle GetHierarchyHandle(Entity entity, ref ComponentBroker broker)
        {
            var rootRefRO = broker.GetRO<RootReference>(entity);
            if (rootRefRO.IsValid)
            {
                return rootRefRO.ValueRO.ToHandle(ref broker);
            }
            var hierarchy = broker.GetBuffer<EntityInHierarchy>(entity);
            var cleanup   = broker.GetBuffer<EntityInHierarchyCleanup>(entity);
            if (hierarchy.IsCreated && !cleanup.IsCreated)
            {
                return new EntityInHierarchyHandle
                {
                    m_hierarchy      = hierarchy.AsNativeArray(),
                    m_extraHierarchy = null,
                    m_index          = 0
                };
            }
            else if (hierarchy.IsCreated && cleanup.IsCreated)
            {
                return new EntityInHierarchyHandle
                {
                    m_hierarchy      = hierarchy.AsNativeArray(),
                    m_extraHierarchy = (EntityInHierarchy*)cleanup.GetUnsafeReadOnlyPtr(),
                    m_index          = 0
                };
            }
            else if (cleanup.IsCreated)
            {
                return new EntityInHierarchyHandle
                {
                    m_hierarchy      = cleanup.Reinterpret<EntityInHierarchy>().AsNativeArray(),
                    m_extraHierarchy = null,
                    m_index          = 0
                };
            }
            return default;
        }

        internal static unsafe EntityInHierarchyHandle GetHierarchyHandle(Entity entity,
                                                                          ref ComponentLookup<RootReference>         rootReferenceLookupRO,
                                                                          ref BufferLookup<EntityInHierarchy>        entityInHierarchyLookupRO,
                                                                          ref BufferLookup<EntityInHierarchyCleanup> entityInHierarchyCleanupLookupRO)
        {
            if (rootReferenceLookupRO.TryGetComponent(entity, out var rootRef))
                return rootRef.ToHandle(ref entityInHierarchyLookupRO, ref entityInHierarchyCleanupLookupRO);

            bool hasHierarchy = entityInHierarchyLookupRO.TryGetBuffer(entity, out var hierarchy);
            bool hasCleanup   = entityInHierarchyCleanupLookupRO.TryGetBuffer(entity, out var cleanup);
            if (hasHierarchy && !hasCleanup)
            {
                return new EntityInHierarchyHandle
                {
                    m_hierarchy      = hierarchy.AsNativeArray(),
                    m_extraHierarchy = null,
                    m_index          = 0
                };
            }
            else if (hasHierarchy && hasCleanup)
            {
                return new EntityInHierarchyHandle
                {
                    m_hierarchy      = hierarchy.AsNativeArray(),
                    m_extraHierarchy = (EntityInHierarchy*)cleanup.GetUnsafeReadOnlyPtr(),
                    m_index          = 0
                };
            }
            else if (hasCleanup)
            {
                return new EntityInHierarchyHandle
                {
                    m_hierarchy      = cleanup.Reinterpret<EntityInHierarchy>().AsNativeArray(),
                    m_extraHierarchy = null,
                    m_index          = 0
                };
            }
            return default;
        }
        #endregion
    }
}
#endif

