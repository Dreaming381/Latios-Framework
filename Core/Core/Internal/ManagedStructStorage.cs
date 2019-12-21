using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine.Assertions;

namespace Latios
{
    internal class ManagedStructComponentStorage
    {
        private Dictionary<Type, TypedManagedStructStorageBase> m_typeMap = new Dictionary<Type, TypedManagedStructStorageBase>();

        public void AddComponent<T>(Entity entity, T value) where T : struct, IComponent
        {
            var tmss = GetTypedManagedStructStorage<T>();

            Assert.IsFalse(HasComponent<T>(entity));
            tmss.storage.Add(entity, value);
        }

        public T GetComponent<T>(Entity entity) where T : struct, IComponent
        {
            var tmss = GetTypedManagedStructStorage<T>();

            if (tmss.storage.TryGetValue(entity, out T component))
            {
                return component;
            }
            else
            {
                throw new InvalidOperationException($"Entity {entity} does not have a component of type: {typeof(T)}");
            }
        }

        public bool HasComponent<T>(Entity entity) where T : struct, IComponent
        {
            var tmss = GetTypedManagedStructStorage<T>();
            return tmss.storage.ContainsKey(entity);
        }

        public void RemoveComponent<T>(Entity entity) where T : struct, IComponent
        {
            var tmss = GetTypedManagedStructStorage<T>();

            Assert.IsTrue(HasComponent<T>(entity));
            tmss.storage.Remove(entity);
        }

        public void SetComponent<T>(Entity entity, T value) where T : struct, IComponent
        {
            var tmss = GetTypedManagedStructStorage<T>();

            Assert.IsTrue(HasComponent<T>(entity));
            tmss.storage[entity] = value;
        }

        public void CopyComponent(Entity src, Entity dst, Type type)
        {
            bool success = m_typeMap.TryGetValue(type, out TypedManagedStructStorageBase baseStorage);
            if (success)
            {
                baseStorage.CopyComponent(src, dst);
            }
        }

        private TypedManagedStructStorage<T> GetTypedManagedStructStorage<T>() where T : struct, IComponent
        {
            var ttype = typeof(T);
            if (!m_typeMap.ContainsKey(ttype))
            {
                m_typeMap.Add(ttype, new TypedManagedStructStorage<T>());
            }
            return m_typeMap[ttype] as TypedManagedStructStorage<T>;
        }

        private abstract class TypedManagedStructStorageBase
        {
            public abstract void CopyComponent(Entity src, Entity dst);
        }

        private class TypedManagedStructStorage<T> : TypedManagedStructStorageBase where T : struct, IComponent
        {
            public Dictionary<Entity, T> storage = new Dictionary<Entity, T>();

            public override void CopyComponent(Entity src, Entity dst)
            {
                bool success = storage.TryGetValue(src, out T srcVal);
                if (success)
                    storage[dst] = srcVal;
            }
        }
    }

    //Todo: Combine Read and Write handles into single struct with single dictionary.
    //Todo: Explore idea of using NativeHashmap for JobHandles.
    internal class CollectionComponentStorage : IDisposable
    {
        private Dictionary<Type, TypedCollectionStorageBase> m_typeMap = new Dictionary<Type, TypedCollectionStorageBase>();

        public void AddCollectionComponent<T>(Entity entity, T value) where T : struct, ICollectionComponent
        {
            var tcs = GetTypedCollectionStorage<T>();

            Assert.IsFalse(HasCollectionComponent<T>(entity));
            tcs.storage.Add(entity, value);
            tcs.writeHandles.Add(entity, new JobHandle());
            tcs.readHandles.Add(entity, new JobHandle());
        }

        public T GetCollectionComponent<T>(Entity entity, bool readOnly, out JobHandle handle) where T : struct, ICollectionComponent
        {
            var tcs = GetTypedCollectionStorage<T>();

            if (readOnly)
            {
                if (tcs.storage.TryGetValue(entity, out T component))
                {
                    handle = tcs.writeHandles[entity];
                    return component;
                }
                else
                {
                    throw new InvalidOperationException($"Entity {entity} does not have a component of type: {typeof(T)}");
                }
            }
            else
            {
                if (tcs.storage.TryGetValue(entity, out T component))
                {
                    var rHandle = tcs.readHandles[entity];
                    var wHandle = tcs.writeHandles[entity];
                    handle      = JobHandle.CombineDependencies(rHandle, wHandle);
                    return component;
                }
                else
                {
                    throw new InvalidOperationException("Entity " + entity + " does not have a component of type: " + typeof(T));
                }
            }
        }

        public bool HasCollectionComponent<T>(Entity entity) where T : struct, ICollectionComponent
        {
            var tcs = GetTypedCollectionStorage<T>();
            return tcs.storage.ContainsKey(entity);
        }

        public void RemoveCollectionComponent<T>(Entity entity, out JobHandle oldReadHandle, out JobHandle oldWriteHandle, out T component) where T : struct, ICollectionComponent
        {
            var tcs = GetTypedCollectionStorage<T>();

            if (!tcs.storage.TryGetValue(entity, out component))
            {
                throw new InvalidOperationException($"Entity {entity} does not have a component of type: {typeof(T)}");
            }

            oldReadHandle  = tcs.readHandles[entity];
            oldWriteHandle = tcs.writeHandles[entity];
            tcs.storage.Remove(entity);
            tcs.readHandles.Remove(entity);
            tcs.writeHandles.Remove(entity);
        }

        public void SetCollectionComponent<T>(Entity entity, T value, out JobHandle oldReadHandle, out JobHandle oldWriteHandle, out T oldComponent) where T : struct,
        ICollectionComponent
        {
            var tcs = GetTypedCollectionStorage<T>();

            if (!tcs.storage.TryGetValue(entity, out oldComponent))
            {
                throw new InvalidOperationException($"Entity {entity} does not have a component of type: {typeof(T)}");
            }

            Assert.IsTrue(HasCollectionComponent<T>(entity));
            oldReadHandle            = tcs.readHandles[entity];
            oldWriteHandle           = tcs.writeHandles[entity];
            tcs.storage[entity]      = value;
            tcs.writeHandles[entity] = new JobHandle();
            tcs.readHandles[entity]  = new JobHandle();
        }

        public void UpdateReadHandle(Entity entity, Type type, JobHandle readHandle)
        {
            var t                 = m_typeMap[type];
            t.readHandles[entity] = JobHandle.CombineDependencies(readHandle, t.readHandles[entity]);
        }

        public void UpdateWriteHandle(Entity entity, Type type, JobHandle writeHandle)
        {
            var t                  = m_typeMap[type];
            var jh                 = JobHandle.CombineDependencies(writeHandle, t.readHandles[entity], t.writeHandles[entity]);
            t.readHandles[entity]  = jh;
            t.writeHandles[entity] = jh;
        }

        public void CopyComponent(Entity src, Entity dst, Type type)
        {
            bool success = m_typeMap.TryGetValue(type, out TypedCollectionStorageBase baseStorage);
            if (success)
            {
                baseStorage.CopyComponent(src, dst);
            }
        }

        private TypedCollectionStorage<T> GetTypedCollectionStorage<T>() where T : struct, ICollectionComponent
        {
            Type ttype = typeof(T);
            if (!m_typeMap.ContainsKey(ttype))
            {
                m_typeMap.Add(ttype, new TypedCollectionStorage<T>());
            }
            return m_typeMap[ttype] as TypedCollectionStorage<T>;
        }

        public void Dispose()
        {
            foreach (var storage in m_typeMap.Values)
            {
                storage.Dispose();
            }
        }

        private abstract class TypedCollectionStorageBase : IDisposable
        {
            public Dictionary<Entity, JobHandle> readHandles  = new Dictionary<Entity, JobHandle>();
            public Dictionary<Entity, JobHandle> writeHandles = new Dictionary<Entity, JobHandle>();

            public abstract void Dispose();
            public abstract void CopyComponent(Entity src, Entity dst);
        }

        private class TypedCollectionStorage<T> : TypedCollectionStorageBase where T : struct, ICollectionComponent
        {
            public Dictionary<Entity, T> storage = new Dictionary<Entity, T>();

            public override void CopyComponent(Entity src, Entity dst)
            {
                bool success = storage.TryGetValue(src, out T srcVal);
                if (success)
                {
                    storage[dst]      = srcVal;
                    readHandles[dst]  = readHandles[src];
                    writeHandles[dst] = writeHandles[src];
                }
            }

            public override void Dispose()
            {
                var jhs = new NativeArray<JobHandle>(readHandles.Values.Count + writeHandles.Values.Count, Allocator.TempJob);
                int i   = 0;
                foreach (var h in readHandles.Values)
                {
                    jhs[i] = h;
                    i++;
                }
                foreach (var h in writeHandles.Values)
                {
                    jhs[i] = h;
                    i++;
                }
                JobHandle.CompleteAll(jhs);
                jhs.Dispose();

                foreach (var collection in storage.Values)
                {
                    collection.Dispose();
                }
            }
        }
    }
}

