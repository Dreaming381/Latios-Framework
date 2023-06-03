using System;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine.Profiling;

namespace Latios
{
    public static class EntityManagerCollectionsExtensions
    {
        /// <summary>
        /// Adds a managed struct component to the entity. This implicitly adds the managed struct component's AssociatedComponentType as well.
        /// If the entity already contains the managed struct component, the managed struct component will be overwritten with the new value.
        /// </summary>
        /// <typeparam name="T">The struct type implementing IManagedComponent</typeparam>
        /// <param name="entity">The entity to add the managed struct component to</param>
        /// <param name="component">The data for the managed struct component</param>
        /// <returns>False if the component was already present, true otherwise</returns>
        public static bool AddManagedStructComponent<T>(this EntityManager em, Entity entity, T component) where T : struct, IManagedStructComponent,
        InternalSourceGen.StaticAPI.IManagedStructComponentSourceGenerated
        {
            return em.GetLatiosWorldUnmanaged().AddManagedStructComponent(entity, component);
        }

        /// <summary>
        /// Removes a managed struct component from the entity. This implicitly removes the managed struct component's AssociatedComponentType as well.
        /// </summary>
        /// <typeparam name="T">The struct type implementing IManagedComponent</typeparam>
        /// <param name="entity">The entity to remove the managed struct component from</param>
        /// <returns>Returns true if the entity had the managed struct component, false otherwise</returns>
        public static bool RemoveManagedStructComponent<T>(this EntityManager em, Entity entity) where T : struct, IManagedStructComponent,
        InternalSourceGen.StaticAPI.IManagedStructComponentSourceGenerated
        {
            return em.GetLatiosWorldUnmanaged().RemoveManagedStructComponent<T>(entity);
        }

        /// <summary>
        /// Gets the managed struct component instance from the entity
        /// </summary>
        /// <typeparam name="T">The struct type implementing IManagedComponent</typeparam>
        public static T GetManagedStructComponent<T>(this EntityManager em, Entity entity) where T : struct, IManagedStructComponent,
        InternalSourceGen.StaticAPI.IManagedStructComponentSourceGenerated
        {
            return em.GetLatiosWorldUnmanaged().GetManagedStructComponent<T>(entity);
        }

        /// <summary>
        /// Sets the managed struct component instance for the entity.
        /// Throws if the entity does not have the managed struct component
        /// </summary>
        /// <typeparam name="T">The struct type implementing IManagedComponent</typeparam>
        /// <param name="entity">The entity which has the managed struct component to be replaced</param>
        /// <param name="component">The new managed struct component value</param>
        public static void SetManagedStructComponent<T>(this EntityManager em, Entity entity, T component) where T : struct, IManagedStructComponent,
        InternalSourceGen.StaticAPI.IManagedStructComponentSourceGenerated
        {
            em.GetLatiosWorldUnmanaged().SetManagedStructComponent(entity, component);
        }

        /// <summary>
        /// Returns true if the entity has the managed struct component. False otherwise.
        /// </summary>
        /// <typeparam name="T">The struct type implementing IManagedComponent</typeparam>
        public static bool HasManagedStructComponent<T>(this EntityManager em, Entity entity) where T : struct, IManagedStructComponent,
        InternalSourceGen.StaticAPI.IManagedStructComponentSourceGenerated
        {
            return em.GetLatiosWorldUnmanaged().HasManagedStructComponent<T>(entity);
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
        /// <param name="entity">The entity in which the collection component should be added</param>
        /// <param name="collectionComponent">The collection component value</param>
        /// <returns>True if the component was added, false if it was set</returns>
        public static bool AddOrSetCollectionComponentAndDisposeOld<T>(this EntityManager em, Entity entity, T collectionComponent) where T : unmanaged, ICollectionComponent,
        InternalSourceGen.StaticAPI.ICollectionComponentSourceGenerated
        {
            return em.GetLatiosWorldUnmanaged().AddOrSetCollectionComponentAndDisposeOld(entity, collectionComponent);
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
        public static bool RemoveCollectionComponentAndDispose<T>(this EntityManager em, Entity entity) where T : unmanaged, ICollectionComponent,
        InternalSourceGen.StaticAPI.ICollectionComponentSourceGenerated
        {
            return em.GetLatiosWorldUnmanaged().RemoveCollectionComponentAndDispose<T>(entity);
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
        public static T GetCollectionComponent<T>(this EntityManager em, Entity entity, bool readOnly = false) where T : unmanaged, ICollectionComponent,
        InternalSourceGen.StaticAPI.ICollectionComponentSourceGenerated
        {
            return em.GetLatiosWorldUnmanaged().GetCollectionComponent<T>(entity, readOnly);
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
        public static void SetCollectionComponentAndDisposeOld<T>(this EntityManager em, Entity entity, T collectionComponent) where T : unmanaged, ICollectionComponent,
        InternalSourceGen.StaticAPI.ICollectionComponentSourceGenerated
        {
            em.GetLatiosWorldUnmanaged().SetCollectionComponentAndDisposeOld(entity, collectionComponent);
        }

        /// <summary>
        /// Returns true if the entity has the associated component type for the collection component type
        /// </summary>
        /// <typeparam name="T">The struct type implementing ICollectionComponent</typeparam>
        public static bool HasCollectionComponent<T>(this EntityManager em, Entity entity) where T : unmanaged, ICollectionComponent,
        InternalSourceGen.StaticAPI.ICollectionComponentSourceGenerated
        {
            return em.GetLatiosWorldUnmanaged().HasCollectionComponent<T>(entity);
        }

        /// <summary>
        /// Provides a dependency for the collection component attached to the entity.
        /// The collection component will no longer be automatically updated with the final Dependency of the currently executing system.
        /// If the collection component was retrieved, added, or set outside of a tracked system execution, then you must call this method
        /// to ensure correct behavior.
        /// </summary>
        /// <typeparam name="T">The struct type implementing ICollectionComponent</typeparam>
        /// <param name="entity">The entity with the collection component whose dependency should be updated</param>
        /// <param name="handle">The new dependency for the collection component</param>
        /// <param name="isReadOnlyHandle">True if the dependency to update only read the collection component</param>
        public static void UpdateCollectionComponentDependency<T>(this EntityManager em, Entity entity, JobHandle handle, bool isReadOnlyHandle) where T : unmanaged,
        ICollectionComponent, InternalSourceGen.StaticAPI.ICollectionComponentSourceGenerated
        {
            em.GetLatiosWorldUnmanaged().UpdateCollectionComponentDependency<T>(entity, handle, isReadOnlyHandle);
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
        public static void UpdateCollectionComponentMainThreadAccess<T>(this EntityManager em, Entity entity, bool wasAccessedAsReadOnly) where T : unmanaged, ICollectionComponent,
        InternalSourceGen.StaticAPI.ICollectionComponentSourceGenerated
        {
            em.GetLatiosWorldUnmanaged().UpdateCollectionComponentMainThreadAccess<T>(entity, wasAccessedAsReadOnly);
        }
    }
}

