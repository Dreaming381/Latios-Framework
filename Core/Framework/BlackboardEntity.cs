using Unity.Entities;
using Unity.Jobs;

namespace Latios
{
    /// <summary>
    /// An entity and its associated EntityManager, which provides shorthands for manipulating the entity's components
    /// </summary>
    public unsafe struct BlackboardEntity
    {
        private Entity               entity;
        private LatiosWorldUnmanaged latiosWorld;
        private EntityManager em => latiosWorld.m_impl->m_worldUnmanaged.EntityManager;

        /// <summary>
        /// Create a blackboard entity
        /// </summary>
        /// <param name="entity">The existing entity to use</param>
        /// <param name="entityManager">The entity's associated EntityManager</param>
        public BlackboardEntity(Entity entity, LatiosWorldUnmanaged latiosWorld)
        {
            this.entity      = entity;
            this.latiosWorld = latiosWorld;
        }

        /// <summary>
        /// Implicitly fetch the entity of the BlackboardEntity
        /// </summary>
        public static implicit operator Entity(BlackboardEntity entity)
        {
            return entity.entity;
        }

        /// <summary>
        /// Adds a component to the blackboard entity
        /// </summary>
        /// <typeparam name="T">The type of component to add</typeparam>
        /// <returns>True if the blackboard entity did not have the component prior to this call, false otherwise.</returns>
        public bool AddComponent<T>() where T : unmanaged, IComponentData
        {
            return em.AddComponent<T>(entity);
        }

        /// <summary>
        /// Removes a component from the blackboard entity
        /// </summary>
        /// <typeparam name="T">The type of component to remove</typeparam>
        /// <returns>True if the blackboard entity had the component prior to this call, false otherwise.</returns>
        public bool RemoveComponent<T>() where T : unmanaged, IComponentData
        {
            return em.RemoveComponent<T>(entity);
        }

        /// <summary>
        /// Adds or sets the component on the blackboard entity
        /// </summary>
        /// <typeparam name="T">The type of component to add or set</typeparam>
        /// <param name="data">The value of the component to add or set</param>
        /// <returns>True if the blackboard entity did not have the component prior to this call, false otherwise.</returns>
        public bool AddComponentData<T>(T data) where T : unmanaged, IComponentData
        {
            return em.AddComponentData(entity, data);
        }

        /// <summary>
        /// Adds the component with the value on the blackboard entity only if a component of that type does not already exist
        /// </summary>
        /// <typeparam name="T">The type of component to add</typeparam>
        /// <param name="data">The value of the component to add</param>
        /// <returns>True if the component was added, false otherwise</returns>
        public bool AddComponentDataIfMissing<T>(T data) where T : unmanaged, IComponentData
        {
            if (em.HasComponent<T>(entity))
                return false;
            em.AddComponentData(entity, data);
            return true;
        }

        /// <summary>
        /// Sets the component's value on the blackboard entity
        /// </summary>
        /// <typeparam name="T">The type of component to set</typeparam>
        /// <param name="data">The value to set</param>
        public void SetComponentData<T>(T data) where T : unmanaged, IComponentData
        {
            em.SetComponentData(entity, data);
        }

        /// <summary>
        /// Retrieves the component's value from the blackboard entity
        /// </summary>
        /// <typeparam name="T">The type of component to retrieve</typeparam>
        /// <returns>The retrieved value</returns>
        public T GetComponentData<T>() where T : unmanaged, IComponentData
        {
            return em.GetComponentData<T>(entity);
        }

        /// <summary>
        /// Returns true if the blackboard entity has a component of the specified type
        /// </summary>
        public bool HasComponent<T>() where T : unmanaged, IComponentData
        {
            return em.HasComponent<T>(entity);
        }

        /// <summary>
        /// Returns true if the blackboard entity has a component of the specified ComponentType
        /// </summary>
        public bool HasComponent(ComponentType componentType)
        {
            return em.HasComponent(entity, componentType);
        }

        /// <summary>
        /// Adds or sets a shared component to the blackboard entity
        /// </summary>
        /// <typeparam name="T">The type of shared component to add or set</typeparam>
        /// <param name="data">The value of the shared component to add or set</param>
        /// <returns>True if the blackboard entity did not have the type of shared component prior to this call, false otherwise</returns>
        public bool AddSharedComponentData<T>(T data) where T : unmanaged, ISharedComponentData
        {
            return em.AddSharedComponent(entity, data);
        }

        /// <summary>
        /// Sets the shared component on the blackboard entity
        /// </summary>
        /// <typeparam name="T">The type of shared component to set</typeparam>
        /// <param name="data"The value of the shared component to set></param>
        public void SetSharedComponentData<T>(T data) where T : unmanaged, ISharedComponentData
        {
            em.SetSharedComponent(entity, data);
        }

        /// <summary>
        /// Sets the shared component on the blackboard entity
        /// </summary>
        /// <typeparam name="T">The type of shared component to set</typeparam>
        /// <param name="data"The value of the shared component to set></param>
        public void SetSharedComponentDataManaged<T>(T data) where T : struct, ISharedComponentData
        {
            em.SetSharedComponentManaged(entity, data);
        }

        /// <summary>
        /// Retrieves the value of the shared component from the blackboard entity
        /// </summary>
        /// <typeparam name="T">The type of shared component to retrieve</typeparam>
        /// <returns>The retrieved shared component value</returns>
        public T GetSharedComponentData<T>() where T : unmanaged, ISharedComponentData
        {
            return em.GetSharedComponent<T>(entity);
        }

        /// <summary>
        /// Retrieves the value of the shared component from the blackboard entity
        /// </summary>
        /// <typeparam name="T">The type of shared component to retrieve</typeparam>
        /// <returns>The retrieved shared component value</returns>
        public T GetSharedComponentDataManaged<T>() where T : struct, ISharedComponentData
        {
            return em.GetSharedComponentManaged<T>(entity);
        }

        /// <summary>
        /// Adds or replaces a DynamicBuffer on the blackboard entity
        /// </summary>
        /// <typeparam name="T">The type of element in the DynamicBuffer to add or replace</typeparam>
        /// <returns>The new DynamicBuffer which can be resized and populated</returns>
        public DynamicBuffer<T> AddBuffer<T>() where T : unmanaged, IBufferElementData
        {
            return em.AddBuffer<T>(entity);
        }

        /// <summary>
        /// Retrieves the DynamicBuffer from the blackboard entity
        /// </summary>
        /// <typeparam name="T">The type of element in the DynamicBuffer to retrieve</typeparam>
        /// <param name="readOnly">If true, the DynamicBuffer will be retrieved in ReadOnly mode</param>
        /// <returns>The retrieved DynamicBuffer</returns>
        public DynamicBuffer<T> GetBuffer<T>(bool readOnly = false) where T : unmanaged, IBufferElementData
        {
            return em.GetBuffer<T>(entity, readOnly);
        }

        /// <summary>
        /// Adds a managed struct component to the entity. This implicitly adds the managed struct component's AssociatedComponentType as well.
        /// If the entity already contains the managed struct component, the managed struct component will be overwritten with the new value.
        /// </summary>
        /// <typeparam name="T">The struct type implementing IManagedComponent</typeparam>
        /// <param name="component">The data for the managed struct component</param>
        /// <returns>False if the component was already present, true otherwise</returns>
        public bool AddManagedStructComponent<T>(T component) where T : struct, IManagedStructComponent, InternalSourceGen.StaticAPI.IManagedStructComponentSourceGenerated
        {
            return latiosWorld.AddManagedStructComponent(entity, component);
        }

        /// <summary>
        /// Removes a managed struct component from the entity. This implicitly removes the managed struct component's AssociatedComponentType as well.
        /// </summary>
        /// <typeparam name="T">The struct type implementing IManagedComponent</typeparam>
        /// <returns>Returns true if the entity had the managed struct component, false otherwise</returns>
        public bool RemoveManagedStructComponent<T>() where T : struct, IManagedStructComponent, InternalSourceGen.StaticAPI.IManagedStructComponentSourceGenerated
        {
            return latiosWorld.RemoveManagedStructComponent<T>(entity);
        }

        /// <summary>
        /// Gets the managed struct component instance from the entity
        /// </summary>
        /// <typeparam name="T">The struct type implementing IManagedComponent</typeparam>
        public T GetManagedStructComponent<T>() where T : struct, IManagedStructComponent, InternalSourceGen.StaticAPI.IManagedStructComponentSourceGenerated
        {
            return latiosWorld.GetManagedStructComponent<T>(entity);
        }

        /// <summary>
        /// Sets the managed struct component instance for the entity.
        /// Throws if the entity does not have the managed struct component
        /// </summary>
        /// <typeparam name="T">The struct type implementing IManagedComponent</typeparam>
        /// <param name="component">The new managed struct component value</param>
        public void SetManagedStructComponent<T>(T component) where T : struct, IManagedStructComponent, InternalSourceGen.StaticAPI.IManagedStructComponentSourceGenerated
        {
            latiosWorld.SetManagedStructComponent(entity, component);
        }

        /// <summary>
        /// Returns true if the entity has the managed struct component. False otherwise.
        /// </summary>
        /// <typeparam name="T">The struct type implementing IManagedComponent</typeparam>
        public bool HasManagedStructComponent<T>() where T : struct, IManagedStructComponent, InternalSourceGen.StaticAPI.IManagedStructComponentSourceGenerated
        {
            return latiosWorld.HasManagedStructComponent<T>(entity);
        }

        /// <summary>
        /// Adds a collection component to the entity. This implicitly adds the collection component's AssociatedComponentType as well.
        /// If the currently executing system is tracked by a Latios ComponentSystemGroup, then the collection component's dependency
        /// is automatically updated with the final Dependency of the currently running system and any generated Dispose handle is merged
        /// with the currently executing system's Dependency.
        /// This method implicitly adds the collection component's associated component type to the entity as well.
        /// If the entity already has the associated component type, this method returns false. The collection component will be set regardless.
        /// </summary>
        /// <typeparam name="T">The struct type implementing ICollectionComponent</typeparam>
        /// <param name="value">The collection component value</param>
        /// <returns>True if the component was added, false if it was set</returns>
        public void AddOrSetCollectionComponentAndDisposeOld<T>(T value) where T : unmanaged, ICollectionComponent, InternalSourceGen.StaticAPI.ICollectionComponentSourceGenerated
        {
            latiosWorld.AddOrSetCollectionComponentAndDisposeOld(entity, value);
        }

        /// <summary>
        /// Removes the collection component from the entity and disposes it.
        /// This implicitly removes the collection component's AssociatedComponentType as well.
        /// If the currently executing system is tracked by a Latios ComponentSystemGroup, its Dependency property is combined with the disposal job.
        /// Otherwise the disposal job is forced to complete.
        /// </summary>
        /// <typeparam name="T">The struct type implementing ICollectionComponent</typeparam>
        /// <returns>True if the entity had the AssociatedComponentType, false otherwise</returns>
        public void RemoveCollectionComponentAndDispose<T>() where T : unmanaged, ICollectionComponent, InternalSourceGen.StaticAPI.ICollectionComponentSourceGenerated
        {
            latiosWorld.RemoveCollectionComponentAndDispose<T>(entity);
        }

        /// <summary>
        /// Gets the collection component and its dependency.
        /// If the currently executing system is tracked by a Latios ComponentSystemGroup, then the collection component's dependency
        /// is automatically updated with the final Dependency of the currently running system, and all necessary JobHandles stored with the
        /// collection component are merged with the currently executing system's Dependency.
        /// </summary>
        /// <typeparam name="T">The struct type implementing ICollectionComponent</typeparam>
        /// <param name="readOnly">Specifies if the collection component will only be read by the system</param>
        /// <returns>The collection component instance</returns>
        public T GetCollectionComponent<T>(bool readOnly = false) where T : unmanaged, ICollectionComponent, InternalSourceGen.StaticAPI.ICollectionComponentSourceGenerated
        {
            return latiosWorld.GetCollectionComponent<T>(entity, readOnly);
        }

        /// <summary>
        /// Replaces the collection component's content with the new value, disposing the old instance.
        /// If the currently executing system is tracked by a Latios ComponentSystemGroup, then the collection component's dependency
        /// is automatically updated with the final Dependency of the currently running system and any generated Dispose handle is merged
        /// with the currently executing system's Dependency.
        /// </summary>
        /// <typeparam name="T">The struct type implementing ICollectionComponent</typeparam>
        /// <param name="value">The new collection component value</param>
        public void SetCollectionComponentAndDisposeOld<T>(T value) where T : unmanaged, ICollectionComponent, InternalSourceGen.StaticAPI.ICollectionComponentSourceGenerated
        {
            latiosWorld.SetCollectionComponentAndDisposeOld(entity, value);
        }

        /// <summary>
        /// Returns true if the entity has the associated component type for the collection component type
        /// </summary>
        /// <typeparam name="T">The struct type implementing ICollectionComponent</typeparam>
        public bool HasCollectionComponent<T>() where T : unmanaged, ICollectionComponent, InternalSourceGen.StaticAPI.ICollectionComponentSourceGenerated
        {
            return latiosWorld.HasCollectionComponent<T>(entity);
        }

        /// <summary>
        /// Provides a dependency for the collection component attached to the entity.
        /// The collection component will no longer be automatically updated with the final Dependency of the currently executing system.
        /// If the collection component was retrieved, added, or set outside of a tracked system execution, then you must call this method
        /// to ensure correct behavior.
        /// </summary>
        /// <typeparam name="T">The struct type implementing ICollectionComponent</typeparam>
        /// <param name="handle">The new dependency for the collection component</param>
        /// <param name="isReadOnlyHandle">True if the dependency to update only read the collection component</param>
        public void UpdateJobDependency<T>(JobHandle handle, bool wasReadOnly) where T : unmanaged, ICollectionComponent,
        InternalSourceGen.StaticAPI.ICollectionComponentSourceGenerated
        {
            latiosWorld.UpdateCollectionComponentDependency<T>(entity, handle, wasReadOnly);
        }

        /// <summary>
        /// Specifies that the accessed collection component on the blackboard entity was operated fully by the main thread.
        /// The collection component will no longer be automatically updated with the final Dependency of the currently executing system.
        /// If the collection component was retrieved, added, or set outside of a tracked system execution but not used in jobs, then you
        /// must call this method to ensure correct behavior.
        /// </summary>
        /// <typeparam name="T">The struct type implementing ICollectionComponent</typeparam>
        /// <param name="wasAccessedAsReadOnly">True if the main thread requested the collection component as readOnly</param>
        public void UpdateMainThreadAccess<T>(bool wasReadOnly) where T : unmanaged, ICollectionComponent, InternalSourceGen.StaticAPI.ICollectionComponentSourceGenerated
        {
            latiosWorld.UpdateCollectionComponentMainThreadAccess<T>(entity, wasReadOnly);
        }
    }
}

