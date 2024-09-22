using System.Runtime.InteropServices;
using Latios;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace Latios
{
    /// <summary>
    /// An unmanaged representation of the LatiosWorld. You can copy and use this structure inside a Burst-Compiled ISystem.
    /// Up to 1023 LatiosWorldUnmanaged instances can be present at once.
    /// </summary>
    public unsafe struct LatiosWorldUnmanaged
    {
        internal LatiosWorldUnmanagedImpl* m_impl;
        internal int                       m_index;
        internal int                       m_version;

        /// <summary>
        /// Checks if this instance is valid. Will throw if the instance used to be valid but was disposed.
        /// </summary>
        public bool isValid
        {
            get
            {
                if (m_impl == null)
                    return false;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (!LatiosWorldUnmanagedTracking.CheckHandle(m_index, m_version))
                    throw new System.InvalidOperationException("LatiosWorldUnmanaged is uninitialized. You must fetch a valid instance from SystemState.");
#endif

                return true;
            }
        }

        /// <summary>
        /// If set to true, the World will stop updating systems if an exception is caught by the LatiosWorld.
        /// </summary>
        public bool zeroToleranceForExceptions
        {
            get
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (!LatiosWorldUnmanagedTracking.CheckHandle(m_index, m_version))
                    throw new System.InvalidOperationException("LatiosWorldUnmanaged is uninitialized. You must fetch a valid instance from SystemState.");
#endif
                return m_impl->m_zeroToleranceForExceptionsEnabled;
            }

            set
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (!LatiosWorldUnmanagedTracking.CheckHandle(m_index, m_version))
                    throw new System.InvalidOperationException("LatiosWorldUnmanaged is uninitialized. You must fetch a valid instance from SystemState.");
#endif
                m_impl->m_zeroToleranceForExceptionsEnabled = value;
            }
        }

        /// <summary>
        /// Obtains the managed LatiosWorld. Not Burst-compatible.
        /// </summary>
        public LatiosWorld latiosWorld
        {
            get
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (!LatiosWorldUnmanagedTracking.CheckHandle(m_index, m_version))
                    throw new System.InvalidOperationException("LatiosWorldUnmanaged is uninitialized. You must fetch a valid instance from SystemState.");
#endif
                return m_impl->m_worldUnmanaged.EntityManager.World as LatiosWorld;
            }
        }

        #region blackboards
        /// <summary>
        /// The worldBlackboardEntity associated with this world
        /// </summary>
        public BlackboardEntity worldBlackboardEntity
        {
            get
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (!LatiosWorldUnmanagedTracking.CheckHandle(m_index, m_version))
                    throw new System.InvalidOperationException("LatiosWorldUnmanaged is uninitialized. You must fetch a valid instance from SystemState.");
#endif
                return m_impl->m_worldBlackboardEntity;
            }
        }

        /// <summary>
        /// The current sceneBlackboardEntity associated with this world
        /// </summary>
        public BlackboardEntity sceneBlackboardEntity
        {
            get
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (!LatiosWorldUnmanagedTracking.CheckHandle(m_index, m_version))
                    throw new System.InvalidOperationException("LatiosWorldUnmanaged is uninitialized. You must fetch a valid instance from SystemState.");

                if (m_impl->m_sceneBlackboardEntity == Entity.Null)
                {
                    throw new System.InvalidOperationException(
                        "The sceneBlackboardEntity has not been initialized yet. If you are trying to access this entity in OnCreate(), please use OnNewScene() or another callback instead.");
                }
#endif
                return m_impl->m_sceneBlackboardEntity;
            }
        }

        // HasComponent checks Exists internally.
        internal bool isSceneBlackboardEntityCreated => m_impl->m_worldUnmanaged.EntityManager.HasComponent<SceneBlackboardTag>(m_impl->m_sceneBlackboardEntity);

        internal BlackboardEntity CreateSceneBlackboardEntity()
        {
            m_impl->m_sceneBlackboardEntity = new BlackboardEntity(m_impl->m_worldUnmanaged.EntityManager.CreateEntity(), this);
            sceneBlackboardEntity.AddComponentData(new SceneBlackboardTag());
            m_impl->m_worldUnmanaged.EntityManager.SetName(sceneBlackboardEntity, "Scene Blackboard Entity");
            return m_impl->m_sceneBlackboardEntity;
        }
        #endregion

        #region sync point
        /// <summary>
        /// The main syncPoint system from which to get command buffers.
        /// Command buffers retrieved from this property from within a system will have dependencies managed automatically
        /// </summary>
        public ref Systems.SyncPointPlaybackSystem syncPoint
        {
            get
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (!LatiosWorldUnmanagedTracking.CheckHandle(m_index, m_version))
                    throw new System.InvalidOperationException("LatiosWorldUnmanaged is uninitialized. You must fetch a valid instance from SystemState.");

                if (m_impl->m_syncPointPlaybackSystem == null)
                {
                    throw new System.InvalidOperationException("There is no initialized SyncPointPlaybackSystem in the World.");
                }
#endif
                return ref m_impl->GetSyncPoint();
            }
        }
        #endregion

        #region managed structs
        /// <summary>
        /// Adds a managed struct component to the entity. This implicitly adds the managed struct component's AssociatedComponentType as well.
        /// If the entity already contains the managed struct component, the managed struct component will be overwritten with the new value.
        /// </summary>
        /// <typeparam name="T">The struct type implementing IManagedComponent</typeparam>
        /// <param name="entity">The entity to add the managed struct component to</param>
        /// <param name="component">The data for the managed struct component</param>
        /// <returns>False if the component was already present, true otherwise</returns>
        public bool AddManagedStructComponent<T>(Entity entity, T managedStructComponent) where T : struct, IManagedStructComponent,
        InternalSourceGen.StaticAPI.IManagedStructComponentSourceGenerated
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!LatiosWorldUnmanagedTracking.CheckHandle(m_index, m_version))
                throw new System.InvalidOperationException("LatiosWorldUnmanaged is uninitialized. You must fetch a valid instance from SystemState.");
#endif
            if (m_impl->m_managedStructStorage == default)
            {
                var managedStructStorage       = new ManagedStructComponentStorage();
                m_impl->m_managedStructStorage = GCHandle.Alloc(managedStructStorage, GCHandleType.Normal);
            }

            var  em            = m_impl->m_worldUnmanaged.EntityManager;
            bool hasAssociated = em.AddComponent(entity, managedStructComponent.componentType);
            em.AddComponent(entity, managedStructComponent.cleanupType);
            (m_impl->m_managedStructStorage.Target as ManagedStructComponentStorage).AddComponent(entity, managedStructComponent);
            return hasAssociated;
        }

        /// <summary>
        /// Removes a managed struct component from the entity. This implicitly removes the managed struct component's AssociatedComponentType as well.
        /// </summary>
        /// <typeparam name="T">The struct type implementing IManagedComponent</typeparam>
        /// <param name="entity">The entity to remove the managed struct component from</param>
        /// <returns>Returns true if the entity had the managed struct component, false otherwise</returns>
        public bool RemoveManagedStructComponent<T>(Entity entity) where T : struct, IManagedStructComponent, InternalSourceGen.StaticAPI.IManagedStructComponentSourceGenerated
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!LatiosWorldUnmanagedTracking.CheckHandle(m_index, m_version))
                throw new System.InvalidOperationException("LatiosWorldUnmanaged is uninitialized. You must fetch a valid instance from SystemState.");
#endif
            if (m_impl->m_managedStructStorage == default)
            {
                var managedStructStorage       = new ManagedStructComponentStorage();
                m_impl->m_managedStructStorage = GCHandle.Alloc(managedStructStorage, GCHandleType.Normal);
            }

            var  storage       = (m_impl->m_managedStructStorage.Target as ManagedStructComponentStorage);
            var  em            = m_impl->m_worldUnmanaged.EntityManager;
            bool hadAssociated = em.RemoveComponent(entity, storage.GetExistType<T>());
            em.RemoveComponent(entity, storage.GetCleanupType<T>());

            storage.RemoveComponent<T>(entity);
            return hadAssociated;
        }

        /// <summary>
        /// Gets the managed struct component instance from the entity
        /// </summary>
        /// <typeparam name="T">The struct type implementing IManagedComponent</typeparam>
        public T GetManagedStructComponent<T>(Entity entity) where T : struct, IManagedStructComponent, InternalSourceGen.StaticAPI.IManagedStructComponentSourceGenerated
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!LatiosWorldUnmanagedTracking.CheckHandle(m_index, m_version))
                throw new System.InvalidOperationException("LatiosWorldUnmanaged is uninitialized. You must fetch a valid instance from SystemState.");
#endif
            if (m_impl->m_managedStructStorage == default)
            {
                var managedStructStorage       = new ManagedStructComponentStorage();
                m_impl->m_managedStructStorage = GCHandle.Alloc(managedStructStorage, GCHandleType.Normal);
            }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!m_impl->m_worldUnmanaged.EntityManager.HasComponent(entity, (m_impl->m_managedStructStorage.Target as ManagedStructComponentStorage).GetExistType<T>()))
                throw new System.InvalidOperationException($"Entity {entity} does not have a component of type: {typeof(T).Name}");
#endif

            return (m_impl->m_managedStructStorage.Target as ManagedStructComponentStorage).GetOrAddDefaultComponent<T>(entity);
        }

        /// <summary>
        /// Sets the managed struct component instance for the entity.
        /// Throws if the entity does not have the managed struct component
        /// </summary>
        /// <typeparam name="T">The struct type implementing IManagedComponent</typeparam>
        /// <param name="entity">The entity which has the managed struct component to be replaced</param>
        /// <param name="component">The new managed struct component value</param>
        public void SetManagedStructComponent<T>(Entity entity, T managedStructComponent) where T : struct, IManagedStructComponent,
        InternalSourceGen.StaticAPI.IManagedStructComponentSourceGenerated
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!LatiosWorldUnmanagedTracking.CheckHandle(m_index, m_version))
                throw new System.InvalidOperationException("LatiosWorldUnmanaged is uninitialized. You must fetch a valid instance from SystemState.");

            if (!m_impl->m_worldUnmanaged.EntityManager.HasComponent(entity, managedStructComponent.componentType))
                throw new System.InvalidOperationException($"Entity {entity} does not have a component of type: {typeof(T).Name}");
#endif
            if (m_impl->m_managedStructStorage == default)
            {
                var managedStructStorage       = new ManagedStructComponentStorage();
                m_impl->m_managedStructStorage = GCHandle.Alloc(managedStructStorage, GCHandleType.Normal);
            }

            (m_impl->m_managedStructStorage.Target as ManagedStructComponentStorage).AddComponent(entity, managedStructComponent);
        }

        /// <summary>
        /// Returns true if the entity has the managed struct component. False otherwise.
        /// </summary>
        /// <typeparam name="T">The struct type implementing IManagedComponent</typeparam>
        public bool HasManagedStructComponent<T>(Entity entity) where T : struct, IManagedStructComponent, InternalSourceGen.StaticAPI.IManagedStructComponentSourceGenerated
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!LatiosWorldUnmanagedTracking.CheckHandle(m_index, m_version))
                throw new System.InvalidOperationException("LatiosWorldUnmanaged is uninitialized. You must fetch a valid instance from SystemState.");
#endif
            var em = m_impl->m_worldUnmanaged.EntityManager;

            if (m_impl->m_managedStructStorage == default)
            {
                var managedStructStorage       = new ManagedStructComponentStorage();
                m_impl->m_managedStructStorage = GCHandle.Alloc(managedStructStorage, GCHandleType.Normal);
            }

            var storage = (m_impl->m_managedStructStorage.Target as ManagedStructComponentStorage);
            return em.HasComponent(entity, storage.GetExistType<T>());
        }

        internal ManagedStructComponentStorage GetManagedStructStorage()
        {
            if (m_impl->m_managedStructStorage == default)
            {
                var managedStructStorage       = new ManagedStructComponentStorage();
                m_impl->m_managedStructStorage = GCHandle.Alloc(managedStructStorage, GCHandleType.Normal);
                return managedStructStorage;
            }
            return (m_impl->m_managedStructStorage.Target as ManagedStructComponentStorage);
        }
        #endregion

        #region collection components
        /// <summary>
        /// Adds a collection component to the entity. This implicitly adds the collection component's AssociatedComponentType as well.
        /// If the currently executing system is tracked by a Latios ComponentSystemGroup, then the collection component's dependency
        /// is automatically updated with the final Dependency of the currently running system and any generated Dispose handle is merged
        /// with the currently executing system's Dependency.
        /// This function implicitly adds the collection component's associated component type to the entity as well.
        /// If the entity already has the associated component type, this method returns false. The collection component will be set regardless.
        /// </summary>
        /// <typeparam name="T">The struct type implementing ICollectionComponent</typeparam>
        /// <param name="entity">The entity in which the collection component should be added</param>
        /// <param name="collectionComponent">The collection component value</param>
        /// <returns>True if the component was added, false if it was set</returns>
        public bool AddOrSetCollectionComponentAndDisposeOld<T>(Entity entity, T collectionComponent) where T : unmanaged, ICollectionComponent,
        InternalSourceGen.StaticAPI.ICollectionComponentSourceGenerated
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!LatiosWorldUnmanagedTracking.CheckHandle(m_index, m_version))
                throw new System.InvalidOperationException("LatiosWorldUnmanaged is uninitialized. You must fetch a valid instance from SystemState.");
#endif

            bool hasAssociated = m_impl->m_worldUnmanaged.EntityManager.AddComponent(entity, collectionComponent.componentType);
            m_impl->m_worldUnmanaged.EntityManager.AddComponent(entity, collectionComponent.cleanupType);
            var replaced = m_impl->m_collectionComponentStorage.AddOrSetCollectionComponentAndDisposeOld(entity, collectionComponent, out var disposeHandle, out var newRef);
            m_impl->m_collectionDependencies.Add(new LatiosWorldUnmanagedImpl.CollectionDependency
            {
                handle                    = newRef.collectionHandle,
                extraDisposeDependency    = disposeHandle,
                hasExtraDisposeDependency = replaced,
                wasReadOnly               = false
            });
            return hasAssociated;
        }

        /// <summary>
        /// Removes the collection component from the entity and disposes it.
        /// This implicitly removes the collection component's AssociatedComponentType as well.
        /// If the currently executing system is tracked by a Latios ComponentSystemGroup, its Dependency property is combined with the disposal job.
        /// Otherwise the disposal job is forced to complete.
        /// </summary>
        /// <typeparam name="T">The struct type implementing ICollectionComponent</typeparam>
        /// <param name="entity">The entity in that has the collection component which should be removed</param>
        /// <returns>True if the entity had the AssociatedComponentType, false otherwise</returns>
        public bool RemoveCollectionComponentAndDispose<T>(Entity entity) where T : unmanaged, ICollectionComponent, InternalSourceGen.StaticAPI.ICollectionComponentSourceGenerated
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!LatiosWorldUnmanagedTracking.CheckHandle(m_index, m_version))
                throw new System.InvalidOperationException("LatiosWorldUnmanaged is uninitialized. You must fetch a valid instance from SystemState.");
#endif

            var  type          = m_impl->m_collectionComponentStorage.GetExistType<T>();
            bool hadAssociated = m_impl->m_worldUnmanaged.EntityManager.RemoveComponent(entity, type);
            m_impl->m_worldUnmanaged.EntityManager.RemoveComponent(entity, m_impl->m_collectionComponentStorage.GetCleanupType<T>());
            var disposed = m_impl->m_collectionComponentStorage.RemoveIfPresentAndDisposeCollectionComponent<T>(entity, out var disposeHandle);
            if (disposed)
                m_impl->CompleteOrMergeDisposeDependency(disposeHandle);
            return hadAssociated;
        }

        /// <summary>
        /// Gets the collection component and its dependency.
        /// If the currently executing system is tracked by a Latios ComponentSystemGroup, then the collection component's dependency
        /// is automatically updated with the final Dependency of the currently running system, and all necessary JobHandles stored with the
        /// collection component are merged with the currently executing system's Dependency.
        /// </summary>
        /// <typeparam name="T">The struct type implementing ICollectionComponent</typeparam>
        /// <param name="entity">The entity that has the collection component</param>
        /// <param name="readOnly">Specifies if the collection component will only be read by the system</param>
        /// <returns>The collection component instance</returns>
        public T GetCollectionComponent<T>(Entity entity, bool readOnly) where T : unmanaged, ICollectionComponent, InternalSourceGen.StaticAPI.ICollectionComponentSourceGenerated
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!LatiosWorldUnmanagedTracking.CheckHandle(m_index, m_version))
                throw new System.InvalidOperationException("LatiosWorldUnmanaged is uninitialized. You must fetch a valid instance from SystemState.");

            var type = m_impl->m_collectionComponentStorage.GetExistType<T>();
            if (!m_impl->m_worldUnmanaged.EntityManager.HasComponent(entity, type))
                throw new System.InvalidOperationException($"Entity {entity} does not have a component of type: {typeof(T).Name}");
#endif
            var collectionRef = m_impl->m_collectionComponentStorage.GetOrAddDefaultCollectionComponent<T>(entity);
            m_impl->CompleteOrMergeDependencies(readOnly, ref collectionRef.readHandles, ref collectionRef.writeHandle);
            m_impl->m_collectionDependencies.Add(new LatiosWorldUnmanagedImpl.CollectionDependency
            {
                handle                    = collectionRef.collectionHandle,
                extraDisposeDependency    = default,
                hasExtraDisposeDependency = false,
                wasReadOnly               = readOnly
            });
            return collectionRef.collectionRef;
        }

        // Note: Always ReadWrite. This method is not recommended unless you know what you are doing.
        public T GetCollectionComponent<T>(Entity entity, out JobHandle combinedReadWriteHandle) where T : unmanaged, ICollectionComponent,
        InternalSourceGen.StaticAPI.ICollectionComponentSourceGenerated
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!LatiosWorldUnmanagedTracking.CheckHandle(m_index, m_version))
                throw new System.InvalidOperationException("LatiosWorldUnmanaged is uninitialized. You must fetch a valid instance from SystemState.");

            var type = m_impl->m_collectionComponentStorage.GetExistType<T>();
            if (!m_impl->m_worldUnmanaged.EntityManager.HasComponent(entity, type))
                throw new System.InvalidOperationException($"Entity {entity} does not have a component of type: {typeof(T).Name}");
#endif
            var collectionRef = m_impl->m_collectionComponentStorage.GetOrAddDefaultCollectionComponent<T>(entity);

            {
                var handleArray = new NativeArray<JobHandle>(collectionRef.readHandles.Length + 1, Allocator.Temp);
                handleArray[0]  = collectionRef.writeHandle;
                for (int i = 0; i < collectionRef.readHandles.Length; i++)
                    handleArray[i + 1]  = collectionRef.readHandles[i];
                combinedReadWriteHandle = JobHandle.CombineDependencies(handleArray);
            }

            m_impl->m_collectionDependencies.Add(new LatiosWorldUnmanagedImpl.CollectionDependency
            {
                handle                    = collectionRef.collectionHandle,
                extraDisposeDependency    = default,
                hasExtraDisposeDependency = false,
                wasReadOnly               = false
            });
            return collectionRef.collectionRef;
        }

        /// <summary>
        /// Replaces the collection component's content with the new value, disposing the old instance.
        /// If the currently executing system is tracked by a Latios ComponentSystemGroup, then the collection component's dependency
        /// is automatically updated with the final Dependency of the currently running system and any generated Dispose handle is merged
        /// with the currently executing system's Dependency.
        /// </summary>
        /// <typeparam name="T">The struct type implementing ICollectionComponent</typeparam>
        /// <param name="entity">The entity that has the collection component to be replaced</param>
        /// <param name="collectionComponent">The new collection component value</param>
        public void SetCollectionComponentAndDisposeOld<T>(Entity entity, T collectionComponent) where T : unmanaged, ICollectionComponent,
        InternalSourceGen.StaticAPI.ICollectionComponentSourceGenerated
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!LatiosWorldUnmanagedTracking.CheckHandle(m_index, m_version))
                throw new System.InvalidOperationException("LatiosWorldUnmanaged is uninitialized. You must fetch a valid instance from SystemState.");

            if (!m_impl->m_worldUnmanaged.EntityManager.HasComponent(entity, collectionComponent.componentType))
                throw new System.InvalidOperationException($"Entity {entity} does not have a component of type: {typeof(T).Name}");
#endif
            var replaced = m_impl->m_collectionComponentStorage.AddOrSetCollectionComponentAndDisposeOld(entity, collectionComponent, out var disposeHandle, out var newRef);
            m_impl->m_collectionDependencies.Add(new LatiosWorldUnmanagedImpl.CollectionDependency
            {
                handle                    = newRef.collectionHandle,
                extraDisposeDependency    = disposeHandle,
                hasExtraDisposeDependency = replaced,
                wasReadOnly               = false
            });
        }

        /// <summary>
        /// Returns true if the entity has the associated component type for the collection component type
        /// </summary>
        /// <typeparam name="T">The struct type implementing ICollectionComponent</typeparam>
        public bool HasCollectionComponent<T>(Entity entity) where T : unmanaged, ICollectionComponent, InternalSourceGen.StaticAPI.ICollectionComponentSourceGenerated
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!LatiosWorldUnmanagedTracking.CheckHandle(m_index, m_version))
                throw new System.InvalidOperationException("LatiosWorldUnmanaged is uninitialized. You must fetch a valid instance from SystemState.");
#endif
            var type = m_impl->m_collectionComponentStorage.GetExistType<T>();
            return m_impl->m_worldUnmanaged.EntityManager.HasComponent(entity, type);
        }

        /// <summary>
        /// Provides a dependency for the collection component attached to the entity.
        /// The collection component will no longer be automatically updated with the final Dependency of the currently executing system.
        /// If the collection component was retrieved, added, or set outside of a tracked system execution and used in jobs, then you
        /// must call this method to ensure correct behavior.
        /// </summary>
        /// <typeparam name="T">The struct type implementing ICollectionComponent</typeparam>
        /// <param name="entity">The entity with the collection component whose dependency should be updated</param>
        /// <param name="handle">The new dependency for the collection component</param>
        /// <param name="isReadOnlyHandle">True if the dependency to update only read the collection component</param>
        public void UpdateCollectionComponentDependency<T>(Entity entity, JobHandle handle, bool isReadOnlyHandle) where T : unmanaged, ICollectionComponent,
        InternalSourceGen.StaticAPI.ICollectionComponentSourceGenerated
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!LatiosWorldUnmanagedTracking.CheckHandle(m_index, m_version))
                throw new System.InvalidOperationException("LatiosWorldUnmanaged is uninitialized. You must fetch a valid instance from SystemState.");
#endif
            m_impl->ClearCollectionDependency(entity, BurstRuntime.GetHashCode64<T>());

            if (!m_impl->m_collectionComponentStorage.TryGetCollectionComponent<T>(entity, out var storedRef))
                return;
            if (isReadOnlyHandle)
            {
                if (storedRef.readHandles.Length == storedRef.readHandles.Capacity)
                {
                    var handleArray = new NativeArray<JobHandle>(storedRef.readHandles.Length + 1, Allocator.Temp);
                    handleArray[0]  = handle;
                    for (int i = 0; i < storedRef.readHandles.Length; i++)
                        handleArray[i + 1] = storedRef.readHandles[i];
                    storedRef.readHandles.Clear();
                    storedRef.readHandles.Add(JobHandle.CombineDependencies(handleArray));
                }
                else
                    storedRef.readHandles.Add(handle);
            }
            else
            {
                storedRef.writeHandle = handle;
                storedRef.readHandles.Clear();
            }
        }

        /// <summary>
        /// Specifies that the accessed collection component on the specified entity was operated fully by the main thread.
        /// The collection component will no longer be automatically updated with the final Dependency of the currently executing system.
        /// If the collection component was retrieved, added, or set outside of a tracked system execution but not used in jobs, then you
        /// must call this method to ensure correct behavior.
        /// </summary>
        /// <typeparam name="T">The struct type implementing ICollectionComponent</typeparam>
        /// <param name="entity">The entity with the collection component that was accessed, modified, or replaced</param>
        /// <param name="wasAccessedAsReadOnly">True if the main thread requested the collection component as readOnly</param>
        public void UpdateCollectionComponentMainThreadAccess<T>(Entity entity, bool wasAccessedAsReadOnly) where T : unmanaged, ICollectionComponent,
        InternalSourceGen.StaticAPI.ICollectionComponentSourceGenerated
        {
            m_impl->ClearCollectionDependency(entity, BurstRuntime.GetHashCode64<T>());
            if (wasAccessedAsReadOnly)
                return;

            if (m_impl->m_collectionComponentStorage.TryGetCollectionComponent<T>(entity, out var storedRef))
            {
                storedRef.writeHandle = default;
                storedRef.readHandles.Clear();
            }
        }

        /// <summary>
        /// Gets an instance of the Collection Aspect from the entity.
        /// </summary>
        /// <typeparam name="T">The struct type implementing ICollectionAspect</typeparam>
        /// <param name="entity">The entity that has the underlying components the ICollectionAspect expects</param>
        /// <returns>The Collection Aspect instance</returns>
        public T GetCollectionAspect<T>(Entity entity) where T : unmanaged, ICollectionAspect<T>
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!LatiosWorldUnmanagedTracking.CheckHandle(m_index, m_version))
                throw new System.InvalidOperationException("LatiosWorldUnmanaged is uninitialized. You must fetch a valid instance from SystemState.");
#endif
            return default(T).CreateCollectionAspect(this, m_impl->m_worldUnmanaged.EntityManager, entity);
        }
        #endregion
    }

    internal static class LatiosWorldUnmanagedTracking
    {
        public static readonly SharedStatic<FixedList4096Bytes<int> > s_handles = SharedStatic<FixedList4096Bytes<int> >.GetOrCreate<LatiosWorldUnmanagedImpl>();

        public static void CreateHandle(out int index, out int version)
        {
            if (s_handles.Data.Length == s_handles.Data.Capacity)
            {
                for (int i = 0; i < s_handles.Data.Length; i++)
                {
                    if (s_handles.Data[i] < 0)
                    {
                        index   = i;
                        version = s_handles.Data[i] = math.abs(s_handles.Data[i]) + 1;
                        return;
                    }
                }

                index   = 0;
                version = 0;
            }
            else
            {
                index   = s_handles.Data.Length;
                version = 1;
                s_handles.Data.Add(1);
            }
        }

        public static void DestroyHandle(int index, int version)
        {
            if (s_handles.Data[index] == version)
                s_handles.Data[index] = -version;
        }

        public static bool CheckHandle(int index, int version)
        {
            if (version == 0)
                return false;
            return s_handles.Data[index] == version;
        }
    }

    internal unsafe struct LatiosWorldUnmanagedImpl
    {
        public BlackboardEntity                 m_worldBlackboardEntity;
        public BlackboardEntity                 m_sceneBlackboardEntity;
        public GCHandle                         m_unmanagedSystemInterfacesDispatcher;
        public GCHandle                         m_managedStructStorage;
        public CollectionComponentStorage       m_collectionComponentStorage;
        public WorldUnmanaged                   m_worldUnmanaged;
        public UnsafeList<SystemHandle>         m_executingSystemStack;
        public UnsafeList<CollectionDependency> m_collectionDependencies;
        public bool                             m_zeroToleranceForExceptionsEnabled;
        public bool                             m_errorState;
        public bool                             m_registeredSystemOnce;

        public Systems.SyncPointPlaybackSystem* m_syncPointPlaybackSystem;

        public void BeginDependencyTracking(SystemHandle system)
        {
            // Clear dependencies registered due to OnCreate.
            // Todo: What if jobs were scheduled before first dependency tracking? Is there an earlier opportunity to detect this?
            if (!m_registeredSystemOnce)
            {
                m_collectionDependencies.Clear();
                m_registeredSystemOnce = true;
            }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (m_syncPointPlaybackSystem != null && m_syncPointPlaybackSystem->hasPendingJobHandlesToAquire)
            {
                if (m_executingSystemStack.IsEmpty)
                {
                    var text = m_worldUnmanaged.ResolveSystemStateRef(system).DebugName;
                    UnityEngine.Debug.LogError(
                        $"An unresolved sync point dependency was detected that originated outside of any Latios ComponentSystemGroup. See Core/\"Automatic Dependency Management Errors.md\" in the documentation. Beginning execution of {text}.");
                    m_errorState = true;
                }
                else
                {
                    var lastSystem = m_executingSystemStack[m_executingSystemStack.Length - 1];
                    var text       = m_worldUnmanaged.ResolveSystemStateRef(lastSystem).DebugName;
                    UnityEngine.Debug.LogError(
                        $"{text} has a pending auto-dependency on syncPoint but a new system has started executing. This is not allowed. See Core/\"Automatic Dependency Management Errors.md\" in the documentation.");
                    m_errorState = true;
                }
            }

            if (!m_collectionDependencies.IsEmpty)
            {
                if (m_executingSystemStack.IsEmpty)
                {
                    var text = m_worldUnmanaged.ResolveSystemStateRef(system).DebugName;
                    UnityEngine.Debug.LogError(
                        $"An unresolved collection component dependency was detected that originated outside of any Latios ComponentSystemGroup. See Core/\"Automatic Dependency Management Errors.md\" in the documentation. Beginning execution of {text}.");
                    m_errorState = true;
                }
                else
                {
                    var lastSystem = m_executingSystemStack[m_executingSystemStack.Length - 1];
                    var text       = m_worldUnmanaged.ResolveSystemStateRef(lastSystem).DebugName;
                    UnityEngine.Debug.LogError(
                        $"{text} has a pending auto-dependency on a collection component but a new system has started executing. This is not allowed. See Core/\"Automatic Dependency Management Errors.md\" in the documentation.");
                    m_errorState = true;
                }
            }
#endif

            m_executingSystemStack.Add(system);
        }

        public void EndDependencyTracking(SystemHandle system, bool hadError)
        {
            m_errorState |= hadError;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (m_executingSystemStack.IsEmpty || m_executingSystemStack[m_executingSystemStack.Length - 1] != system)
            {
                m_errorState = true;
                var currentSystemName = m_worldUnmanaged.ResolveSystemStateRef(system).DebugName;
                if (!m_executingSystemStack.IsEmpty)
                {
                    var lastSystem     = m_executingSystemStack[m_executingSystemStack.Length - 1];
                    var lastSystemName = m_worldUnmanaged.ResolveSystemStateRef(lastSystem).DebugName;
                    throw new System.InvalidOperationException(
                        $"LatiosWorldUnmanaged encountered a dependency tracking mismatch. Please report this bug! Current System: {currentSystemName}, previous: {lastSystemName}");
                }

                throw new System.InvalidOperationException(
                    $"LatiosWorldUnmanaged encountered a dependency tracking mismatch. Please report this bug! Current System: {currentSystemName}, empty stack");
            }
#endif
            var dependency = m_worldUnmanaged.ResolveSystemStateRef(system).Dependency;
            if (!dependency.Equals(default))
            {
                if (m_syncPointPlaybackSystem != null && m_syncPointPlaybackSystem->hasPendingJobHandlesToAquire)
                {
                    m_syncPointPlaybackSystem->AddJobHandleForProducer(dependency);
                }

                foreach (var dep in m_collectionDependencies)
                {
                    if (m_collectionComponentStorage.IsHandleValid(in dep.handle))
                    {
                        if (dep.wasReadOnly)
                        {
                            if (dep.handle.readHandles.Length == dep.handle.readHandles.Capacity)
                            {
                                var handleArray = new NativeArray<JobHandle>(dep.handle.readHandles.Length + 1, Allocator.Temp);
                                handleArray[0]  = dependency;
                                for (int i = 0; i < dep.handle.readHandles.Length; i++)
                                    handleArray[i + 1] = dep.handle.readHandles[i];
                                dep.handle.readHandles.Clear();
                                dep.handle.readHandles.Add(JobHandle.CombineDependencies(handleArray));
                            }
                            else
                                dep.handle.readHandles.Add(dependency);
                        }
                        else
                        {
                            dep.handle.writeHandle = dependency;
                            dep.handle.readHandles.Clear();
                        }
                    }
                }
                m_collectionDependencies.Clear();
            }
            else
            {
                if (m_syncPointPlaybackSystem != null && m_syncPointPlaybackSystem->hasPendingJobHandlesToAquire)
                {
                    m_syncPointPlaybackSystem->AddMainThreadCompletionForProducer();
                }

                foreach (var dep in m_collectionDependencies)
                {
                    if (!dep.wasReadOnly)
                    {
                        // Write job was completed. Clear everything.
                        if (m_collectionComponentStorage.IsHandleValid(in dep.handle))
                        {
                            dep.handle.writeHandle = default;
                            dep.handle.readHandles.Clear();
                        }
                    }
                }
                m_collectionDependencies.Clear();
            }

            m_executingSystemStack.Length--;
        }

        public void CompleteOrMergeDisposeDependency(JobHandle handle)
        {
            if (!m_executingSystemStack.IsEmpty && m_executingSystemStack[m_executingSystemStack.Length - 1] == m_worldUnmanaged.GetCurrentlyExecutingSystem())
            {
                ref var state    = ref m_worldUnmanaged.ResolveSystemStateRef(m_executingSystemStack[m_executingSystemStack.Length - 1]);
                state.Dependency = JobHandle.CombineDependencies(state.Dependency, handle);
            }
            else
            {
                handle.Complete();
            }
        }

        public void CompleteOrMergeDependencies(bool isReadOnly, ref FixedList512Bytes<JobHandle> readHandles, ref JobHandle writeHandle)
        {
            if (writeHandle.Equals(default(JobHandle)))
            {
                if (isReadOnly || readHandles.IsEmpty)
                    return;
            }

            if (!m_executingSystemStack.IsEmpty && m_executingSystemStack[m_executingSystemStack.Length - 1] == m_worldUnmanaged.GetCurrentlyExecutingSystem())
            {
                ref var state = ref m_worldUnmanaged.ResolveSystemStateRef(m_executingSystemStack[m_executingSystemStack.Length - 1]);
                if (isReadOnly)
                {
                    state.Dependency = JobHandle.CombineDependencies(state.Dependency, writeHandle);
                }
                else
                {
                    var handleArray = new NativeArray<JobHandle>(readHandles.Length + 2, Allocator.Temp);
                    handleArray[0]  = state.Dependency;
                    handleArray[1]  = writeHandle;
                    for (int i = 0; i < readHandles.Length; i++)
                        handleArray[i + 2] = readHandles[i];
                    state.Dependency       = JobHandle.CombineDependencies(handleArray);
                }
            }
            else
            {
                if (isReadOnly)
                {
                    writeHandle.Complete();
                    writeHandle = default;
                }
                else
                {
                    var handleArray = new NativeArray<JobHandle>(readHandles.Length + 1, Allocator.Temp);
                    handleArray[0]  = writeHandle;
                    for (int i = 0; i < readHandles.Length; i++)
                        handleArray[i + 1] = readHandles[i];
                    JobHandle.CompleteAll(handleArray);
                    writeHandle = default;
                    readHandles.Clear();
                }
            }
        }

        public void ClearCollectionDependency(Entity entity, long typeHash)
        {
            for (int i = 0; i < m_collectionDependencies.Length; i++)
            {
                ref var dep = ref m_collectionDependencies.ElementAt(i);
                if (dep.handle.entity == entity && dep.handle.typeHash == typeHash)
                {
                    m_collectionDependencies.RemoveAtSwapBack(i);
                    i--;
                }
            }
        }

        public ref Systems.SyncPointPlaybackSystem GetSyncPoint()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (m_syncPointPlaybackSystem == null)
                throw new System.InvalidOperationException("No SyncPointPlaybackSystem exists in the LatiosWorld.");
#endif
            return ref *m_syncPointPlaybackSystem;
        }

        public bool isAllowedToRun => !(m_zeroToleranceForExceptionsEnabled && m_errorState);

        public struct CollectionDependency
        {
            public CollectionComponentHandle handle;
            public JobHandle                 extraDisposeDependency;
            public bool                      wasReadOnly;
            public bool                      hasExtraDisposeDependency;
        }
    }
}

