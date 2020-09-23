using System;
using Unity.Entities;
using Unity.Jobs;

namespace Latios
{
    public static class EntityManagerExtensions
    {
        public static void AddManagedComponent<T>(this EntityManager em, Entity entity, T component) where T : struct, IManagedComponent
        {
            var storage = GetComponentStorage(em);
            em.AddComponent(                                    entity, component.AssociatedComponentType);
            em.AddComponent<ManagedComponentSystemStateTag<T> >(entity);
            storage.AddComponent(entity, component);
        }

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

        public static T GetManagedComponent<T>(this EntityManager em, Entity entity) where T : struct, IManagedComponent
        {
            var storage = GetComponentStorage(em);
            return storage.GetComponent<T>(entity);
        }

        public static void SetManagedComponent<T>(this EntityManager em, Entity entity, T component) where T : struct, IManagedComponent
        {
            var storage = GetComponentStorage(em);
            storage.SetComponent(entity, component);
        }

        public static bool HasManagedComponent<T>(this EntityManager em, Entity entity) where T : struct, IManagedComponent
        {
            return GetComponentStorage(em).HasComponent<T>(entity) && em.HasComponent(entity, new T().AssociatedComponentType);
        }

        public static void AddCollectionComponent<T>(this EntityManager em, Entity entity, T collectionComponent, bool isInitialized = true) where T : struct, ICollectionComponent
        {
            var storage = GetCollectionStorage(em, out LatiosWorld lw);
            em.AddComponent(                                       entity, collectionComponent.AssociatedComponentType);
            em.AddComponent<CollectionComponentSystemStateTag<T> >(entity);
            storage.AddCollectionComponent(entity, collectionComponent, !isInitialized);
            lw.MarkCollectionDirty<T>(entity, false);
        }

        public static void RemoveCollectionComponentAndDispose<T>(this EntityManager em, Entity entity) where T : struct, ICollectionComponent
        {
            var storage = GetCollectionStorage(em, out LatiosWorld lw);
            if (storage.HasCollectionComponent<T>(entity))
            {
                em.RemoveComponent(                                       entity, new T().AssociatedComponentType);
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

        public static void RemoveCollectionComponentAndDispose<T>(this EntityManager em, Entity entity, out JobHandle jobHandle) where T : struct, ICollectionComponent
        {
            var storage = GetCollectionStorage(em, out LatiosWorld lw);
            if (storage.HasCollectionComponent<T>(entity))
            {
                em.RemoveComponent(                                       entity, new T().AssociatedComponentType);
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

        public static T GetCollectionComponent<T>(this EntityManager em, Entity entity, bool readOnly, out JobHandle jobHandle) where T : struct, ICollectionComponent
        {
            var storage = GetCollectionStorage(em, out LatiosWorld lw);
            lw.MarkCollectionDirty<T>(entity, readOnly);
            return storage.GetCollectionComponent<T>(entity, readOnly, out jobHandle);
        }

        public static T GetCollectionComponent<T>(this EntityManager em, Entity entity, bool readOnly = false) where T : struct, ICollectionComponent
        {
            var result = GetCollectionComponent<T>(em, entity, readOnly, out JobHandle handle);
            GetCollectionStorage(em, out LatiosWorld lw);
            lw.UpdateOrCompleteDependency(handle);
            return result;
        }

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

        public static bool HasCollectionComponent<T>(this EntityManager em, Entity entity) where T : struct, ICollectionComponent
        {
            return GetCollectionStorage(em, out _).HasCollectionComponent<T>(entity) && em.HasComponent(entity, new T().AssociatedComponentType);
        }

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

