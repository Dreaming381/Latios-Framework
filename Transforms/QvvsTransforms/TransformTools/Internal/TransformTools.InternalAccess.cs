#if !LATIOS_TRANSFORMS_UNITY
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Mathematics;

namespace Latios.Transforms
{
    public static unsafe partial class TransformTools
    {
        #region Alive
        internal interface IAlive
        {
            bool IsAlive(Entity entity);
        }

        internal struct EsilAlive : IAlive
        {
            public EntityStorageInfoLookup esil;
            public bool IsAlive(Entity entity) => esil.IsAlive(entity);

            public static ref EsilAlive From(ref EntityStorageInfoLookup esil) => ref UnsafeUtility.As<EntityStorageInfoLookup, EsilAlive>(ref esil);
        }
        #endregion

        #region WorldTransform
        internal interface IWorldTransform
        {
            WorldTransform GetWorldTransform(Entity entity);
            RefRW<WorldTransform> GetWorldTransformRefRW(Entity entity);
            bool HasWorldTransform(Entity entity);
            bool isTicked { get; }
        }

        internal struct LookupWorldTransform : IWorldTransform
        {
            public ComponentLookup<WorldTransform> lookup;
            public WorldTransform GetWorldTransform(Entity entity) => lookup[entity];
            public RefRW<WorldTransform> GetWorldTransformRefRW(Entity entity) => lookup.GetRefRW(entity);
            public bool HasWorldTransform(Entity entity) => lookup.HasComponent(entity);
            public bool isTicked => false;

            public static ref LookupWorldTransform From(ref ComponentLookup<WorldTransform> lookup) => ref UnsafeUtility.As<ComponentLookup<WorldTransform>, LookupWorldTransform>(
                ref lookup);
        }

        internal struct LookupTickedWorldTransform : IWorldTransform
        {
            public ComponentLookup<TickedWorldTransform> lookup;
            public WorldTransform GetWorldTransform(Entity entity) => lookup[entity].ToUnticked();
            public RefRW<WorldTransform> GetWorldTransformRefRW(Entity entity)
            {
                var result = lookup.GetRefRW(entity);
                return UnsafeUtility.As<RefRW<TickedWorldTransform>, RefRW<WorldTransform> >(ref result);
            }
            public bool HasWorldTransform(Entity entity) => lookup.HasComponent(entity);
            public bool isTicked => true;

            public static ref LookupWorldTransform From(ref ComponentLookup<TickedWorldTransform> lookup)
            {
                return ref UnsafeUtility.As<ComponentLookup<TickedWorldTransform>, LookupWorldTransform>(ref lookup);
            }
        }
        #endregion

        #region Hierarchy

        internal interface IHierarchy
        {
            bool TryGetRootReference(Entity entity, out RootReference rootRef);
            bool TryGetEntityInHierarchy(Entity entity, out DynamicBuffer<EntityInHierarchy> hierarchy);
            bool TryGetEntityInHierarchyCleanup(Entity entity, out DynamicBuffer<EntityInHierarchyCleanup> hierarchy);
        }

        internal struct LookupHierarchy : IHierarchy
        {
            public ComponentLookup<RootReference>         rootRefLookup;
            public BufferLookup<EntityInHierarchy>        entityInHierarchyLookup;
            public BufferLookup<EntityInHierarchyCleanup> entityInHierarchyCleanupLookup;

            public LookupHierarchy(ComponentLookup<RootReference>         rootRefLookup,
                                   BufferLookup<EntityInHierarchy>        entityInHierarchyLookup,
                                   BufferLookup<EntityInHierarchyCleanup> entityInHierarchyCleanupLookup)
            {
                this.rootRefLookup                  = rootRefLookup;
                this.entityInHierarchyLookup        = entityInHierarchyLookup;
                this.entityInHierarchyCleanupLookup = entityInHierarchyCleanupLookup;
            }
            public void WriteBack(ref ComponentLookup<RootReference> rootRefLookup, ref BufferLookup<EntityInHierarchy> entityInHierarchyLookup,
                                  ref BufferLookup<EntityInHierarchyCleanup> entityInHierarchyCleanupLookup)
            {
                rootRefLookup                  = this.rootRefLookup;
                entityInHierarchyLookup        = this.entityInHierarchyLookup;
                entityInHierarchyCleanupLookup = this.entityInHierarchyCleanupLookup;
            }

            public bool TryGetRootReference(Entity entity, out RootReference rootRef)
            {
                return rootRefLookup.TryGetComponent(entity, out rootRef);
            }
            public bool TryGetEntityInHierarchy(Entity entity, out DynamicBuffer<EntityInHierarchy> hierarchy)
            {
                return entityInHierarchyLookup.TryGetBuffer(entity, out hierarchy);
            }
            public bool TryGetEntityInHierarchyCleanup(Entity entity, out DynamicBuffer<EntityInHierarchyCleanup> hierarchy)
            {
                return entityInHierarchyCleanupLookup.TryGetBuffer(entity, out hierarchy);
            }
        }
        #endregion

        #region EntityManager
        internal interface IEntityManager : IAlive, IWorldTransform, IHierarchy
        {
            public EntityManager entityManager { get; }
        }

        internal struct EntityManagerAccess : IEntityManager
        {
            EntityManager em;
            public EntityManagerAccess(EntityManager entityManager)
            {
                em = entityManager;
            }
            public static ref EntityManagerAccess From(ref EntityManager em) => ref UnsafeUtility.As<EntityManager, EntityManagerAccess>(ref em);

            public EntityManager entityManager => em;

            public bool TryGetRootReference(Entity entity, out RootReference rootRef)
            {
                if (em.HasComponent<RootReference>(entity))
                {
                    rootRef = em.GetComponentData<RootReference>(entity);
                    return true;
                }
                rootRef = default;
                return false;
            }

            public bool TryGetEntityInHierarchy(Entity entity, out DynamicBuffer<EntityInHierarchy> hierarchy)
            {
                if (em.HasBuffer<EntityInHierarchy>(entity))
                {
                    hierarchy = em.GetBuffer<EntityInHierarchy>(entity, true);
                    return true;
                }
                hierarchy = default;
                return false;
            }
            public bool TryGetEntityInHierarchyCleanup(Entity entity, out DynamicBuffer<EntityInHierarchyCleanup> hierarchy)
            {
                if (em.HasBuffer<EntityInHierarchyCleanup>(entity))
                {
                    hierarchy = em.GetBuffer<EntityInHierarchyCleanup>(entity, true);
                    return true;
                }
                hierarchy = default;
                return false;
            }

            public bool IsAlive(Entity entity) => em.IsAlive(entity);

            public WorldTransform GetWorldTransform(Entity entity) => em.GetComponentData<WorldTransform>(entity);
            public RefRW<WorldTransform> GetWorldTransformRefRW(Entity entity) => em.GetComponentDataRW<WorldTransform>(entity);
            public bool HasWorldTransform(Entity entity) => em.HasComponent<WorldTransform>(entity);
            public bool isTicked => false;
        }

        internal struct TickedEntityManagerAccess : IEntityManager
        {
            EntityManager em;
            public TickedEntityManagerAccess(EntityManager entityManager)
            {
                em = entityManager;
            }
            public static ref TickedEntityManagerAccess From(ref EntityManager em) => ref UnsafeUtility.As<EntityManager, TickedEntityManagerAccess>(ref em);

            public EntityManager entityManager => em;

            public bool TryGetRootReference(Entity entity, out RootReference rootRef)
            {
                if (em.HasComponent<RootReference>(entity))
                {
                    rootRef = em.GetComponentData<RootReference>(entity);
                    return true;
                }
                rootRef = default;
                return false;
            }

            public bool TryGetEntityInHierarchy(Entity entity, out DynamicBuffer<EntityInHierarchy> hierarchy)
            {
                if (em.HasBuffer<EntityInHierarchy>(entity))
                {
                    hierarchy = em.GetBuffer<EntityInHierarchy>(entity, true);
                    return true;
                }
                hierarchy = default;
                return false;
            }
            public bool TryGetEntityInHierarchyCleanup(Entity entity, out DynamicBuffer<EntityInHierarchyCleanup> hierarchy)
            {
                if (em.HasBuffer<EntityInHierarchyCleanup>(entity))
                {
                    hierarchy = em.GetBuffer<EntityInHierarchyCleanup>(entity, true);
                    return true;
                }
                hierarchy = default;
                return false;
            }

            public bool IsAlive(Entity entity) => em.IsAlive(entity);

            public WorldTransform GetWorldTransform(Entity entity) => em.GetComponentData<TickedWorldTransform>(entity).ToUnticked();
            public RefRW<WorldTransform> GetWorldTransformRefRW(Entity entity)
            {
                var result = em.GetComponentDataRW<TickedWorldTransform>(entity);
                return UnsafeUtility.As<RefRW<TickedWorldTransform>, RefRW<WorldTransform> >(ref result);
            }
            public bool HasWorldTransform(Entity entity) => em.HasComponent<TickedWorldTransform>(entity);
            public bool isTicked => true;
        }
        #endregion

        #region ComponentBroker
        internal struct ComponentBrokerAccess : IAlive, IHierarchy, IWorldTransform
        {
            ComponentBroker broker;
            public static ref ComponentBrokerAccess From(ref ComponentBroker componentBroker) => ref UnsafeUtility.As<ComponentBroker, ComponentBrokerAccess>(ref componentBroker);

            public bool IsAlive(Entity entity) => broker.entityStorageInfoLookup.IsAlive(entity);

            public bool TryGetRootReference(Entity entity, out RootReference rootRef)
            {
                var ro = broker.GetRO<RootReference>(entity);
                if (ro.IsValid)
                {
                    rootRef = ro.ValueRO;
                    return true;
                }
                rootRef = default;
                return false;
            }
            public bool TryGetEntityInHierarchy(Entity entity, out DynamicBuffer<EntityInHierarchy> hierarchy)
            {
                hierarchy = broker.GetBuffer<EntityInHierarchy>(entity);
                return hierarchy.IsCreated;
            }
            public bool TryGetEntityInHierarchyCleanup(Entity entity, out DynamicBuffer<EntityInHierarchyCleanup> hierarchy)
            {
                hierarchy = broker.GetBuffer<EntityInHierarchyCleanup>(entity);
                return hierarchy.IsCreated;
            }

            public WorldTransform GetWorldTransform(Entity entity) => broker.GetRO<WorldTransform>(entity).ValueRO;
            public RefRW<WorldTransform> GetWorldTransformRefRW(Entity entity) => broker.GetRW<WorldTransform>(entity);
            public bool HasWorldTransform(Entity entity) => broker.Has<WorldTransform>(entity);
            public bool isTicked => false;
        }

        internal struct ComponentBrokerParallelAccess : IAlive, IHierarchy, IWorldTransform
        {
            ComponentBroker broker;
            public static ref ComponentBrokerParallelAccess From(ref ComponentBroker componentBroker) => ref UnsafeUtility.As<ComponentBroker, ComponentBrokerParallelAccess>(
                ref componentBroker);

            public bool IsAlive(Entity entity) => broker.entityStorageInfoLookup.IsAlive(entity);

            public bool TryGetRootReference(Entity entity, out RootReference rootRef)
            {
                var ro = broker.GetROIgnoreParallelSafety<RootReference>(entity);
                if (ro.IsValid)
                {
                    rootRef = ro.ValueRO;
                    return true;
                }
                rootRef = default;
                return false;
            }
            public bool TryGetEntityInHierarchy(Entity entity, out DynamicBuffer<EntityInHierarchy> hierarchy)
            {
                hierarchy = broker.GetBufferIgnoreParallelSafety<EntityInHierarchy>(entity);
                return hierarchy.IsCreated;
            }
            public bool TryGetEntityInHierarchyCleanup(Entity entity, out DynamicBuffer<EntityInHierarchyCleanup> hierarchy)
            {
                hierarchy = broker.GetBufferIgnoreParallelSafety<EntityInHierarchyCleanup>(entity);
                return hierarchy.IsCreated;
            }

            public WorldTransform GetWorldTransform(Entity entity) => broker.GetROIgnoreParallelSafety<WorldTransform>(entity).ValueRO;
            public RefRW<WorldTransform> GetWorldTransformRefRW(Entity entity) => broker.GetRWIgnoreParallelSafety<WorldTransform>(entity);
            public bool HasWorldTransform(Entity entity) => broker.Has<WorldTransform>(entity);
            public bool isTicked => false;
        }

        internal struct TickedComponentBrokerAccess : IAlive, IHierarchy, IWorldTransform
        {
            ComponentBroker broker;
            public static ref TickedComponentBrokerAccess From(ref ComponentBroker componentBroker) => ref UnsafeUtility.As<ComponentBroker, TickedComponentBrokerAccess>(
                ref componentBroker);

            public bool IsAlive(Entity entity) => broker.entityStorageInfoLookup.IsAlive(entity);

            public bool TryGetRootReference(Entity entity, out RootReference rootRef)
            {
                var ro = broker.GetRO<RootReference>(entity);
                if (ro.IsValid)
                {
                    rootRef = ro.ValueRO;
                    return true;
                }
                rootRef = default;
                return false;
            }
            public bool TryGetEntityInHierarchy(Entity entity, out DynamicBuffer<EntityInHierarchy> hierarchy)
            {
                hierarchy = broker.GetBuffer<EntityInHierarchy>(entity);
                return hierarchy.IsCreated;
            }
            public bool TryGetEntityInHierarchyCleanup(Entity entity, out DynamicBuffer<EntityInHierarchyCleanup> hierarchy)
            {
                hierarchy = broker.GetBuffer<EntityInHierarchyCleanup>(entity);
                return hierarchy.IsCreated;
            }

            public WorldTransform GetWorldTransform(Entity entity) => broker.GetRO<TickedWorldTransform>(entity).ValueRO.ToUnticked();
            public RefRW<WorldTransform> GetWorldTransformRefRW(Entity entity)
            {
                var result = broker.GetRW<TickedWorldTransform>(entity);
                return UnsafeUtility.As<RefRW<TickedWorldTransform>, RefRW<WorldTransform> >(ref result);
            }
            public bool HasWorldTransform(Entity entity) => broker.Has<TickedWorldTransform>(entity);
            public bool isTicked => true;
        }

        internal struct TickedComponentBrokerParallelAccess : IAlive, IHierarchy, IWorldTransform
        {
            ComponentBroker broker;
            public static ref TickedComponentBrokerParallelAccess From(ref ComponentBroker componentBroker) => ref UnsafeUtility.As<ComponentBroker,
                                                                                                                                    TickedComponentBrokerParallelAccess>(
                ref componentBroker);

            public bool IsAlive(Entity entity) => broker.entityStorageInfoLookup.IsAlive(entity);

            public bool TryGetRootReference(Entity entity, out RootReference rootRef)
            {
                var ro = broker.GetROIgnoreParallelSafety<RootReference>(entity);
                if (ro.IsValid)
                {
                    rootRef = ro.ValueRO;
                    return true;
                }
                rootRef = default;
                return false;
            }
            public bool TryGetEntityInHierarchy(Entity entity, out DynamicBuffer<EntityInHierarchy> hierarchy)
            {
                hierarchy = broker.GetBufferIgnoreParallelSafety<EntityInHierarchy>(entity);
                return hierarchy.IsCreated;
            }
            public bool TryGetEntityInHierarchyCleanup(Entity entity, out DynamicBuffer<EntityInHierarchyCleanup> hierarchy)
            {
                hierarchy = broker.GetBufferIgnoreParallelSafety<EntityInHierarchyCleanup>(entity);
                return hierarchy.IsCreated;
            }

            public WorldTransform GetWorldTransform(Entity entity) => broker.GetROIgnoreParallelSafety<TickedWorldTransform>(entity).ValueRO.ToUnticked();
            public RefRW<WorldTransform> GetWorldTransformRefRW(Entity entity)
            {
                var result = broker.GetRWIgnoreParallelSafety<TickedWorldTransform>(entity);
                return UnsafeUtility.As<RefRW<TickedWorldTransform>, RefRW<WorldTransform> >(ref result);
            }
            public bool HasWorldTransform(Entity entity) => broker.Has<TickedWorldTransform>(entity);
            public bool isTicked => true;
        }
        #endregion

        #region Casts
        internal static WorldTransform ToUnticked(this in TickedWorldTransform ticked) => new WorldTransform
        {
            worldTransform = ticked.worldTransform
        };
        internal static TickedWorldTransform ToTicked(this in WorldTransform unticked) => new TickedWorldTransform
        {
            worldTransform = unticked.worldTransform
        };
        #endregion
    }
}
#endif

