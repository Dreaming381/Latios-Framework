#if !LATIOS_TRANSFORMS_UNITY
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Transforms
{
    internal static unsafe partial class TreeKernels
    {
        public struct TreeClassification
        {
            public enum TreeRole : byte
            {
                Solo,
                Root,
                InternalNoChildren,
                InternalWithChildren
            }

            public Entity   root;
            public int      indexInHierarchy;
            public TreeRole role;
            public bool     isRootAlive;
        }

        public static TreeClassification ClassifyAlive(EntityManager em, Entity entity)
        {
            TreeClassification result = default;
            if (em.HasComponent<RootReference>(entity))
            {
                var rootRef             = em.GetComponentData<RootReference>(entity);
                result.root             = rootRef.rootEntity;
                result.indexInHierarchy = rootRef.indexInHierarchy;
                if (em.HasBuffer<EntityInHierarchy>(rootRef.rootEntity))
                {
                    var children       = em.GetBuffer<EntityInHierarchy>(rootRef.rootEntity, true)[rootRef.indexInHierarchy].childCount;
                    result.role        = children != 0 ? TreeClassification.TreeRole.InternalWithChildren : TreeClassification.TreeRole.InternalNoChildren;
                    result.isRootAlive = true;
                }
                else
                {
                    var children       = em.GetBuffer<EntityInHierarchyCleanup>(rootRef.rootEntity, true)[rootRef.indexInHierarchy].entityInHierarchy.childCount;
                    result.role        = children != 0 ? TreeClassification.TreeRole.InternalWithChildren : TreeClassification.TreeRole.InternalNoChildren;
                    result.isRootAlive = false;
                }
            }
            else if (em.HasBuffer<EntityInHierarchy>(entity))
                result.role = TreeClassification.TreeRole.Root;
            else
                result.role = TreeClassification.TreeRole.Solo;
            return result;
        }

        public static TreeClassification ClassifyAlive(ref ComponentLookup<RootReference>         rootRefLookupRO,
                                                       ref BufferLookup<EntityInHierarchy>        hierarchyLookupRO,
                                                       ref BufferLookup<EntityInHierarchyCleanup> cleanupLookupRO,
                                                       Entity entity)
        {
            TreeClassification result = default;
            if (rootRefLookupRO.TryGetComponent(entity, out var rootRef))
            {
                result.root             = rootRef.rootEntity;
                result.indexInHierarchy = rootRef.indexInHierarchy;
                if (hierarchyLookupRO.TryGetBuffer(rootRef.rootEntity, out var hierarchy))
                {
                    var children       = hierarchy[rootRef.indexInHierarchy].childCount;
                    result.role        = children != 0 ? TreeClassification.TreeRole.InternalWithChildren : TreeClassification.TreeRole.InternalNoChildren;
                    result.isRootAlive = true;
                }
                else
                {
                    var children       = cleanupLookupRO[rootRef.rootEntity][rootRef.indexInHierarchy].entityInHierarchy.childCount;
                    result.role        = children != 0 ? TreeClassification.TreeRole.InternalWithChildren : TreeClassification.TreeRole.InternalNoChildren;
                    result.isRootAlive = false;
                }
            }
            else if (hierarchyLookupRO.HasBuffer(entity))
                result.role = TreeClassification.TreeRole.Root;
            else
                result.role = TreeClassification.TreeRole.Solo;
            return result;
        }

        public struct FoundRoot
        {
            public Entity root;
            public int    indexInHierarchy;
            public bool   isRootAlive;
            public bool   found;
        }

        public static FoundRoot FindRoot(EntityManager em, Entity entity)
        {
            if (em.HasComponent<RootReference>(entity))
            {
                var rr = em.GetComponentData<RootReference>(entity);
                return new FoundRoot
                {
                    root             = rr.rootEntity,
                    indexInHierarchy = rr.indexInHierarchy,
                    isRootAlive      = em.IsAlive(rr.rootEntity),
                    found            = true
                };
            }
            else if (em.HasBuffer<EntityInHierarchy>(entity))
            {
                return new FoundRoot
                {
                    root        = entity,
                    isRootAlive = em.IsAlive(entity),
                    found       = true
                };
            }
            else if (em.HasBuffer<EntityInHierarchyCleanup>(entity))
            {
                return new FoundRoot
                {
                    root        = entity,
                    isRootAlive = em.IsAlive(entity),
                    found       = false
                };
            }
            else
                return default;
        }
    }
}
#endif

