using System;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine.Profiling;

namespace Latios
{
    public static class EntityManagerCollectionsExtensions
    {
        /// <summary>
        /// Adds a managed component to the entity. This implicitly adds the managed component's AssociatedComponentType as well.
        /// </summary>
        /// <typeparam name="T">The struct type implementing IManagedComponent</typeparam>
        /// <param name="entity">The entity to add the managed component to</param>
        /// <param name="component">The data for the managed component</param>
        public static void AddManagedComponent<T>(this EntityManager em, Entity entity, T component) where T : struct, IManagedComponent
        {
            var storage = GetComponentStorage(em);
            em.AddComponent(                                    entity, component.AssociatedComponentType);
            em.AddComponent<ManagedComponentSystemStateTag<T> >(entity);
            storage.AddComponent(entity, component);
        }

        /// <summary>
        /// Removes a managed component from the entity. This implicitly removes the managed component's AssociatedComponentType as well.
        /// </summary>
        /// <typeparam name="T">The struct type implementing IManagedComponent</typeparam>
        /// <param name="entity">The entity to remove the managed component from</param>
        public static void RemoveManagedComponent<T>(this EntityManager em, Entity entity) where T : struct, IManagedComponent
        {
            var storage = GetComponentStorage(em);
            if (storage.HasComponent<T>(entity))
            {
                em.RemoveComponent(                                    entity, new T().AssociatedComponentType);
                em.RemoveComponent<ManagedComponentSystemStateTag<T> >(entity);
                storage.RemoveComponent<T>(entity);
            }
        }

        /// <summary>
        /// Gets the managed component instance from the entity
        /// </summary>
        /// <typeparam name="T">The struct type implementing IManagedComponent</typeparam>
        public static T GetManagedComponent<T>(this EntityManager em, Entity entity) where T : struct, IManagedComponent
        {
            var storage = GetComponentStorage(em);
            return storage.GetComponent<T>(entity);
        }

        /// <summary>
        /// Sets the managed component instance for the entity.
        /// Throws if the entity does not have the managed component
        /// </summary>
        /// <typeparam name="T">The struct type implementing IManagedComponent</typeparam>
        /// <param name="entity">The entity which has the managed component to be replaced</param>
        /// <param name="component">The new managed component value</param>
        public static void SetManagedComponent<T>(this EntityManager em, Entity entity, T component) where T : struct, IManagedComponent
        {
            var storage = GetComponentStorage(em);
            storage.SetComponent(entity, component);
        }

        /// <summary>
        /// Returns true if the entity has the managed component. False otherwise.
        /// </summary>
        /// <typeparam name="T">The struct type implementing IManagedComponent</typeparam>
        public static bool HasManagedComponent<T>(this EntityManager em, Entity entity) where T : struct, IManagedComponent
        {
            var storage = GetComponentStorage(em);
            return storage.HasComponent<T>(entity) && em.HasComponent(entity, storage.GetAssociatedType<T>());
        }

        /// <summary>
        /// Adds a collection component to the entity. This implicitly adds the collection component's AssociatedComponentType as well.
        /// The collection component's dependency is automatically updated with the final Dependency of the currently running system.
        /// </summary>
        /// <typeparam name="T">The struct type implementing ICollectionComponent</typeparam>
        /// <param name="entity">The entity in which the collection component should be added</param>
        /// <param name="collectionComponent">The collection component value</param>
        /// <param name="isInitialized">If true, the collection component should be automatically disposed when replaced or removed</param>
        public static void AddCollectionComponent<T>(this EntityManager em, Entity entity, T collectionComponent, bool isInitialized = true) where T : struct, ICollectionComponent
        {
            var storage = GetCollectionStorage(em, out LatiosWorld lw);
            em.AddComponent(                                       entity, collectionComponent.AssociatedComponentType);
            em.AddComponent<CollectionComponentSystemStateTag<T> >(entity);
            storage.AddCollectionComponent(entity, collectionComponent, !isInitialized);
            lw.MarkCollectionDirty<T>(entity, false);
        }

        /// <summary>
        /// Removes the collection component from the entity and disposes it.
        /// This implicitly removes the collection component's AssociatedComponentType as well.
        /// If a SubSystem is currently running, its Dependency property is combined with the disposal job.
        /// Otherwise the disposal job is forced to complete.
        /// </summary>
        /// <typeparam name="T">The struct type implementing ICollectionComponent</typeparam>
        /// <param name="entity">The entity in that has the collection component which should be removed</param>
        public static void RemoveCollectionComponentAndDispose<T>(this EntityManager em, Entity entity) where T : struct, ICollectionComponent
        {
            var storage = GetCollectionStorage(em, out LatiosWorld lw);
            if (storage.HasCollectionComponent<T>(entity))
            {
                em.RemoveComponent(                                       entity, storage.GetAssociatedType<T>());
                em.RemoveComponent<CollectionComponentSystemStateTag<T> >(entity);
                bool isDisposable = storage.RemoveCollectionComponent(entity, out JobHandle readHandle, out JobHandle writeHandle, out T component);
                if (isDisposable)
                {
                    var jobHandle = component.Dispose(JobHandle.CombineDependencies(readHandle, writeHandle));
                    lw.UpdateOrCompleteDependency(jobHandle);
                    lw.MarkCollectionClean<T>(entity, false);
                }
            }
        }

        /// <summary>
        /// Removes the collection component from the entity and disposes it.
        /// This implicitly removes the collection component's AssociatedComponentType as well.
        /// </summary>
        /// <typeparam name="T">The struct type implementing ICollectionComponent</typeparam>
        /// <param name="entity">The entity in that has the collection component which should be removed</param>
        /// <param name="jobHandle"></param>
        public static void RemoveCollectionComponentAndDispose<T>(this EntityManager em, Entity entity, out JobHandle jobHandle) where T : struct, ICollectionComponent
        {
            var storage = GetCollectionStorage(em, out LatiosWorld lw);
            if (storage.HasCollectionComponent<T>(entity))
            {
                em.RemoveComponent(                                       entity, storage.GetAssociatedType<T>());
                em.RemoveComponent<CollectionComponentSystemStateTag<T> >(entity);
                bool isDisposable = storage.RemoveCollectionComponent(entity, out JobHandle readHandle, out JobHandle writeHandle, out T component);
                if (isDisposable)
                {
                    jobHandle = component.Dispose(JobHandle.CombineDependencies(readHandle, writeHandle));
                    lw.MarkCollectionClean<T>(entity, false);
                    return;
                }
            }
            jobHandle = default;
        }

        /// <summary>
        /// Gets the collection component and its dependency.
        /// The collection component's dependency will be updated by the currently running SubSystem,
        /// but the currently running SubSystem will not have its Dependency property updated.
        /// </summary>
        /// <typeparam name="T">The struct type implementing ICollectionComponent</typeparam>
        /// <param name="entity">The entity that has the collection component</param>
        /// <param name="readOnly">Specifies if the collection component will only be read by the system</param>
        /// <param name="jobHandle">The dependency of the collection component</param>
        /// <returns>The collection component instance</returns>
        public static T GetCollectionComponent<T>(this EntityManager em, Entity entity, bool readOnly, out JobHandle jobHandle) where T : struct, ICollectionComponent
        {
            var storage = GetCollectionStorage(em, out LatiosWorld lw);
            lw.MarkCollectionDirty<T>(entity, readOnly);
            return storage.GetCollectionComponent<T>(entity, readOnly, out jobHandle);
        }

        /// <summary>
        /// Gets the collection component and its dependency.
        /// The collection component's dependency will be updated by the final Dependency of the currently running SubSystem,
        /// and the currently running SubSystem will have its Dependency property updated by the collection component's dependency
        /// at the time of retrieval.
        /// </summary>
        /// <typeparam name="T">The struct type implementing ICollectionComponent</typeparam>
        /// <param name="entity">The entity that has the collection component</param>
        /// <param name="readOnly">Specifies if the collection component will only be read by the system</param>
        /// <returns>The collection component instance</returns>
        public static T GetCollectionComponent<T>(this EntityManager em, Entity entity, bool readOnly = false) where T : struct, ICollectionComponent
        {
            var result = GetCollectionComponent<T>(em, entity, readOnly, out JobHandle handle);
            GetCollectionStorage(em, out LatiosWorld lw);
            lw.UpdateOrCompleteDependency(handle);
            return result;
        }

        /// <summary>
        /// Replaces the collection component's content with the new value, disposing the old instance.
        /// If a SubSystem is currently running, its Dependency property is combined with the disposal job.
        /// Otherwise the disposal job is forced to complete.
        /// The new collection component's dependency will be updated by the final Dependency of the currently running SubSystem.
        /// </summary>
        /// <typeparam name="T">The struct type implementing ICollectionComponent</typeparam>
        /// <param name="entity">The entity that has the collection component to be replaced</param>
        /// <param name="collectionComponent">The new collection component value</param>
        public static void SetCollectionComponentAndDisposeOld<T>(this EntityManager em, Entity entity, T collectionComponent) where T : struct, ICollectionComponent
        {
            var  storage         = GetCollectionStorage(em, out LatiosWorld lw);
            bool isOldDisposable = storage.SetCollectionComponent(entity, collectionComponent, out JobHandle readHandle, out JobHandle writeHandle, out T oldComponent);
            lw.UpdateOrCompleteDependency(readHandle, writeHandle);
            if (isOldDisposable)
            {
                var jobHandle = oldComponent.Dispose(JobHandle.CombineDependencies(readHandle, writeHandle));
                lw.UpdateOrCompleteDependency(jobHandle);
            }
            lw.MarkCollectionDirty<T>(entity, false);
        }

        /// <summary>
        /// Returns true if the entity has the collection component type
        /// </summary>
        /// <typeparam name="T">The struct type implementing ICollectionComponent</typeparam>
        public static bool HasCollectionComponent<T>(this EntityManager em, Entity entity) where T : struct, ICollectionComponent
        {
            var storage = GetCollectionStorage(em, out _);
            return storage.HasCollectionComponent<T>(entity) && em.HasComponent(entity, storage.GetAssociatedType<T>());
        }

        /// <summary>
        /// Provides a dependency for the collection component attached to the entity.
        /// The collection component will no longer be automatically updated with the final Dependency of the currently running SubSystem.
        /// </summary>
        /// <typeparam name="T">The struct type implementing ICollectionComponent</typeparam>
        /// <param name="entity">The entity with the collection component whose dependency should be updated</param>
        /// <param name="handle">The new dependency for the collection component</param>
        /// <param name="wasReadOnly">True if the dependency to update only read the collection component</param>
        public static void UpdateCollectionComponentDependency<T>(this EntityManager em, Entity entity, JobHandle handle, bool wasReadOnly) where T : struct, ICollectionComponent
        {
            var storage = GetCollectionStorage(em, out LatiosWorld lw);
            if (wasReadOnly)
            {
                storage.UpdateReadHandle(entity, typeof(T), handle);
            }
            else
            {
                storage.UpdateWriteHandle(entity, typeof(T), handle);
            }
            lw.MarkCollectionClean<T>(entity, wasReadOnly);
        }

        private static ManagedStructComponentStorage GetComponentStorage(EntityManager em)
        {
            if (!(em.World is LatiosWorld latiosWorld))
                throw new InvalidOperationException("The EntityManager must belong to a LatiosWorld in order to use IComponent");
            return latiosWorld.ManagedStructStorage;
        }

        private static CollectionComponentStorage GetCollectionStorage(EntityManager em, out LatiosWorld latiosWorld)
        {
            if (!(em.World is LatiosWorld lw))
                throw new InvalidOperationException("The EntityManager must belong to a LatiosWorld in order to use ICollectionComponent");
            latiosWorld = lw;
            return latiosWorld.CollectionComponentStorage;
        }
    }
}

