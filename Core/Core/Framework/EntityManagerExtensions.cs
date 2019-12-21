using System;
using Unity.Entities;
using Unity.Jobs;

namespace Latios
{
    public static class EntityManagerExtensions
    {
        public static void AddManagedComponent<T>(this EntityManager em, Entity entity, T component) where T : struct, IComponent
        {
            var storage = GetComponentStorage(em);
            em.AddComponent<ManagedComponentTag<T> >(           entity);
            em.AddComponent<ManagedComponentSystemStateTag<T> >(entity);
            storage.AddComponent(entity, component);
        }

        public static void RemoveManagedComponent<T>(this EntityManager em, Entity entity) where T : struct, IComponent
        {
            var storage = GetComponentStorage(em);
            em.RemoveComponent<ManagedComponentTag<T> >(           entity);
            em.RemoveComponent<ManagedComponentSystemStateTag<T> >(entity);
            storage.RemoveComponent<T>(entity);
        }

        public static T GetManagedComponent<T>(this EntityManager em, Entity entity) where T : struct, IComponent
        {
            var storage = GetComponentStorage(em);
            return storage.GetComponent<T>(entity);
        }

        public static void SetManagedComponent<T>(this EntityManager em, Entity entity, T component) where T : struct, IComponent
        {
            var storage = GetComponentStorage(em);
            storage.SetComponent(entity, component);
        }

        public static bool HasManagedComponent<T>(this EntityManager em, Entity entity) where T : struct, IComponent
        {
            var storage = GetComponentStorage(em);
            return storage.HasComponent<T>(entity) && em.HasComponent<ManagedComponentTag<T> >(entity);
        }

        public static void AddCollectionComponent<T>(this EntityManager em, Entity entity, T collectionComponent) where T : struct, ICollectionComponent
        {
            var storage = GetCollectionStorage(em, out LatiosWorld lw);
            em.AddComponent<CollectionComponentTag<T> >(           entity);
            em.AddComponent<CollectionComponentSystemStateTag<T> >(entity);
            storage.AddCollectionComponent(entity, collectionComponent);
            lw.MarkCollectionDirty<T>(entity, false);
        }

        public static void RemoveCollectionComponentAndDispose<T>(this EntityManager em, Entity entity) where T : struct, ICollectionComponent
        {
            var storage = GetCollectionStorage(em, out LatiosWorld lw);
            em.RemoveComponent<CollectionComponentTag<T> >(           entity);
            em.RemoveComponent<CollectionComponentSystemStateTag<T> >(entity);
            storage.RemoveCollectionComponent(entity, out JobHandle readHandle, out JobHandle writeHandle, out T component);
            JobHandle.CombineDependencies(readHandle, writeHandle).Complete();
            component.Dispose();
            lw.MarkCollectionClean<T>(entity, false);
        }

        public static T GetCollectionComponent<T>(this EntityManager em, Entity entity, bool readOnly, out JobHandle jobHandle) where T : struct, ICollectionComponent
        {
            var storage = GetCollectionStorage(em, out LatiosWorld lw);
            lw.MarkCollectionDirty<T>(entity, readOnly);
            return storage.GetCollectionComponent<T>(entity, readOnly, out jobHandle);
        }

        public static T GetCollectionComponent<T>(this EntityManager em, Entity entity) where T : struct, ICollectionComponent
        {
            var result = GetCollectionComponent<T>(em, entity, false, out JobHandle handle);
            handle.Complete();
            return result;
        }

        public static void SetCollectionComponent<T>(this EntityManager em, Entity entity, T collectionComponent) where T : struct, ICollectionComponent
        {
            var storage = GetCollectionStorage(em, out LatiosWorld lw);
            storage.SetCollectionComponent(entity, collectionComponent, out JobHandle readHandle, out JobHandle writeHandle, out T oldComponent);
            JobHandle.CombineDependencies(readHandle, writeHandle).Complete();
            oldComponent.Dispose();
            lw.MarkCollectionDirty<T>(entity, false);
        }

        public static bool HasCollectionComponent<T>(this EntityManager em, Entity entity) where T : struct, ICollectionComponent
        {
            return GetCollectionStorage(em, out _).HasCollectionComponent<T>(entity) && em.HasComponent<CollectionComponentTag<T> >(entity);
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

