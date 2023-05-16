using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios
{
    [BurstCompile]
    internal unsafe struct CollectionComponentStorage : IDisposable
    {
        struct Key : IEquatable<Key>
        {
            public long   typeHash;
            public Entity entity;

            public bool Equals(Key other)
            {
                return typeHash.Equals(other.typeHash) && entity.Equals(other.entity);
            }

            public override unsafe int GetHashCode()
            {
                fixed (void* ptr = &this)
                {
                    return ((Hash128*)ptr)->GetHashCode();
                }
            }
        }

        struct RegisteredType
        {
            public ComponentType existType;
            public ComponentType cleanupType;
            public int           typedStorageIndex;
        }

        struct StoragePtr
        {
            public void* ptr;

            public ref TypedCollectionComponentStorage<T> As<T>() where T : unmanaged, ICollectionComponent, InternalSourceGen.StaticAPI.ICollectionComponentSourceGenerated
            {
                return ref UnsafeUtility.AsRef<TypedCollectionComponentStorage<T> >(ptr);
            }
        }

        NativeHashMap<Key, int2>            m_twoLevelLookup;
        NativeHashMap<long, RegisteredType> m_registeredTypeLookup;
        NativeList<StoragePtr>              m_storagePtrs;
        Allocator                           m_allocator;

        public CollectionComponentStorage(Allocator allocator)
        {
            m_twoLevelLookup       = new NativeHashMap<Key, int2>(128, allocator);
            m_registeredTypeLookup = new NativeHashMap<long, RegisteredType>(32, allocator);
            m_storagePtrs          = new NativeList<StoragePtr>(32, allocator);
            m_allocator            = allocator;
        }

        delegate FunctionPointer<InternalSourceGen.StaticAPI.BurstDispatchCollectionComponentDelegate> GetFunctionPtrDelegate();
        static Dictionary<ComponentType, FunctionPointer<InternalSourceGen.StaticAPI.BurstDispatchCollectionComponentDelegate> > m_cleanupToFunctionLookup;

        public void Dispose()
        {
            if (m_cleanupToFunctionLookup == null)
                m_cleanupToFunctionLookup = new Dictionary<ComponentType, FunctionPointer<InternalSourceGen.StaticAPI.BurstDispatchCollectionComponentDelegate> >();
            foreach (var registeredType in m_registeredTypeLookup.GetValueArray(Allocator.Temp))
            {
                if (!m_cleanupToFunctionLookup.TryGetValue(registeredType.cleanupType, out var functionPtr))
                {
                    var managedType = registeredType.cleanupType.GetManagedType();
                    var method      = managedType.GetMethod("GetBurstDispatchFunctionPtr", BindingFlags.Static | BindingFlags.Public);
                    var invokable   = method.CreateDelegate(typeof(GetFunctionPtrDelegate)) as GetFunctionPtrDelegate;
                    functionPtr     = invokable();
                    m_cleanupToFunctionLookup.Add(registeredType.cleanupType, functionPtr);
                }
                DispatchDisposeToSourceGen(functionPtr, (CollectionComponentStorage*)UnsafeUtility.AddressOf(ref this), registeredType.typedStorageIndex);
            }
            m_twoLevelLookup.Dispose();
            m_registeredTypeLookup.Dispose();
            m_storagePtrs.Dispose();
        }

        [BurstCompile]
        public static void DispatchDisposeToSourceGen(FunctionPointer<InternalSourceGen.StaticAPI.BurstDispatchCollectionComponentDelegate> functionPtr,
                                                      CollectionComponentStorage*                                                           thisPtr,
                                                      int storageIndex)
        {
            InternalSourceGen.CollectionComponentOperations.DisposeCollectionStorage(functionPtr, thisPtr, storageIndex);
        }

        public ComponentType GetExistType<T>() where T : unmanaged, ICollectionComponent, InternalSourceGen.StaticAPI.ICollectionComponentSourceGenerated
        {
            var typeHash = BurstRuntime.GetHashCode64<T>();
            if (!m_registeredTypeLookup.TryGetValue(typeHash, out var element))
            {
                var t   = new T();
                element = new RegisteredType
                {
                    existType         = t.componentType,
                    typedStorageIndex = m_storagePtrs.Length,
                    cleanupType       = t.cleanupType
                };

                var storage                            = AllocatorManager.Allocate<TypedCollectionComponentStorage<T> >(m_allocator);
                storage->collectionComponents          = new NativeList<T>(m_allocator);
                storage->freeStack                     = new NativeList<int>(m_allocator);
                storage->readHandles                   = new NativeList<FixedList512Bytes<JobHandle> >(m_allocator);
                storage->writeHandles                  = new NativeList<JobHandle>(m_allocator);
                storage->typeIndex                     = element.typedStorageIndex;
                m_storagePtrs.Add(new StoragePtr { ptr = storage });
                m_registeredTypeLookup.Add(typeHash, element);
            }
            return element.existType;
        }

        public ComponentType GetCleanupType<T>() where T : unmanaged, ICollectionComponent, InternalSourceGen.StaticAPI.ICollectionComponentSourceGenerated
        {
            var typeHash = BurstRuntime.GetHashCode64<T>();
            if (!m_registeredTypeLookup.TryGetValue(typeHash, out var element))
            {
                var t   = new T();
                element = new RegisteredType
                {
                    existType         = t.componentType,
                    typedStorageIndex = m_storagePtrs.Length,
                    cleanupType       = t.cleanupType
                };

                var storage                            = AllocatorManager.Allocate<TypedCollectionComponentStorage<T> >(m_allocator);
                storage->collectionComponents          = new NativeList<T>(m_allocator);
                storage->freeStack                     = new NativeList<int>(m_allocator);
                storage->readHandles                   = new NativeList<FixedList512Bytes<JobHandle> >(m_allocator);
                storage->writeHandles                  = new NativeList<JobHandle>(m_allocator);
                storage->typeIndex                     = element.typedStorageIndex;
                m_storagePtrs.Add(new StoragePtr { ptr = storage });
                m_registeredTypeLookup.Add(typeHash, element);
            }
            return element.cleanupType;
        }

        public bool IsHandleValid(in CollectionComponentHandle handle)
        {
            var key = new Key { entity = handle.entity, typeHash = handle.typeHash };
            return m_twoLevelLookup.ContainsKey(key);
        }

        // Returns true if added
        public bool AddOrSetCollectionComponentAndDisposeOld<T>(Entity entity, T value, out JobHandle disposeHandle, out CollectionComponentRef<T> newRef) where T : unmanaged,
        ICollectionComponent, InternalSourceGen.StaticAPI.ICollectionComponentSourceGenerated
        {
            ref var tcs = ref GetTypedCollectionStorage<T>(entity, out int index);
            if (index >= 0)
            {
                tcs.TryDisposeIndexAndFree(index, out disposeHandle, true);
                tcs.collectionComponents[index] = value;
                newRef                          = new CollectionComponentRef<T>(ref tcs, entity, index);
                return false;
            }

            if (tcs.freeStack.IsEmpty)
            {
                index = tcs.collectionComponents.Length;
                tcs.collectionComponents.Add(value);
                tcs.writeHandles.Add(default);
                tcs.readHandles.Add(default);
            }
            else
            {
                index = tcs.freeStack[tcs.freeStack.Length - 1];
                tcs.freeStack.Length--;
                tcs.collectionComponents[index] = value;
            }

            m_twoLevelLookup.Add(new Key { entity = entity, typeHash = BurstRuntime.GetHashCode64<T>() }, new int2(tcs.typeIndex, index));
            disposeHandle                                            = default;
            newRef                                                   = new CollectionComponentRef<T>(ref tcs, entity, index);
            return true;
        }

        public CollectionComponentRef<T> GetCollectionComponent<T>(Entity entity) where T : unmanaged, ICollectionComponent,
        InternalSourceGen.StaticAPI.ICollectionComponentSourceGenerated
        {
            ref var tcs = ref GetTypedCollectionStorage<T>(entity, out int index);
            if (index < 0)
            {
                throw new InvalidOperationException($"Entity {entity} does not have a component of type: {typeof(T).Name}");
            }

            return new CollectionComponentRef<T>(ref tcs, entity, index);
        }

        public bool TryGetCollectionComponent<T>(Entity entity, out CollectionComponentRef<T> storedRef) where T : unmanaged, ICollectionComponent,
        InternalSourceGen.StaticAPI.ICollectionComponentSourceGenerated
        {
            ref var tcs = ref GetTypedCollectionStorage<T>(entity, out int index);
            if (index < 0)
            {
                storedRef = default;
                return false;
            }

            storedRef = new CollectionComponentRef<T>(ref tcs, entity, index);
            return true;
        }

        public CollectionComponentRef<T> GetOrAddDefaultCollectionComponent<T>(Entity entity) where T : unmanaged, ICollectionComponent,
        InternalSourceGen.StaticAPI.ICollectionComponentSourceGenerated
        {
            ref var tcs = ref GetTypedCollectionStorage<T>(entity, out int index);
            if (index < 0)
            {
                UnityEngine.Debug.Log($"Failed to find collection component. Getting default.");
                if (tcs.freeStack.IsEmpty)
                {
                    index = tcs.collectionComponents.Length;
                    tcs.collectionComponents.Add(default);
                    tcs.writeHandles.Add(default);
                    tcs.readHandles.Add(default);
                }
                else
                {
                    index = tcs.freeStack[tcs.freeStack.Length - 1];
                    tcs.freeStack.Length--;
                }

                m_twoLevelLookup.Add(new Key { entity = entity, typeHash = BurstRuntime.GetHashCode64<T>() }, new int2(tcs.typeIndex, index));
            }

            return new CollectionComponentRef<T>(ref tcs, entity, index);
        }

        public bool HasCollectionComponent<T>(Entity entity) where T : unmanaged, ICollectionComponent, InternalSourceGen.StaticAPI.ICollectionComponentSourceGenerated
        {
            GetTypedCollectionStorage<T>(entity, out int index);
            return index >= 0;
        }

        //Returns true if the component can be safely disposed.
        public bool RemoveIfPresentAndDisposeCollectionComponent<T>(Entity entity, out JobHandle disposeHandle) where T : unmanaged, ICollectionComponent,
        InternalSourceGen.StaticAPI.ICollectionComponentSourceGenerated
        {
            var tcs = GetTypedCollectionStorage<T>(entity, out int index);

            if (index < 0)
            {
                disposeHandle = default;
                return false;
            }
            tcs.TryDisposeIndexAndFree(index, out disposeHandle, false);
            var key = new Key { typeHash = BurstRuntime.GetHashCode64<T>(), entity = entity };
            m_twoLevelLookup.Remove(key);
            return true;
        }

        //Returns new ref.
        public CollectionComponentRef<T> SetCollectionComponentAndDisposeOld<T>(Entity entity, T value, out JobHandle disposeHandle) where T : unmanaged,
        ICollectionComponent, InternalSourceGen.StaticAPI.ICollectionComponentSourceGenerated
        {
            ref var tcs = ref GetTypedCollectionStorage<T>(entity, out int index);
            if (index < 0)
            {
                throw new InvalidOperationException($"Entity {entity} does not have a component of type: {typeof(T)}");
            }

            tcs.TryDisposeIndexAndFree(index, out disposeHandle, true);
            tcs.collectionComponents[index] = value;
            return new CollectionComponentRef<T>(ref tcs, entity, index);
        }

        public void DisposeTypeUsingSourceGenDispatch<T>(int storagePtrIndex) where T : unmanaged,
        ICollectionComponent, InternalSourceGen.StaticAPI.ICollectionComponentSourceGenerated
        {
            m_storagePtrs[storagePtrIndex].As<T>().Dispose();
            AllocatorManager.Free(m_allocator, (TypedCollectionComponentStorage<T>*)m_storagePtrs[storagePtrIndex].ptr);
        }

        private ref TypedCollectionComponentStorage<T> GetTypedCollectionStorage<T>(Entity entity, out int indexInTypedStorage) where T : unmanaged, ICollectionComponent,
        InternalSourceGen.StaticAPI.ICollectionComponentSourceGenerated
        {
            var key = new Key { typeHash = BurstRuntime.GetHashCode64<T>(), entity = entity };
            if (m_twoLevelLookup.TryGetValue(key, out var indices))
            {
                indexInTypedStorage = indices.y;
                return ref m_storagePtrs[indices.x].As<T>();
            }

            if (!m_registeredTypeLookup.TryGetValue(key.typeHash, out var element))
            {
                var t   = new T();
                element = new RegisteredType
                {
                    existType         = t.componentType,
                    typedStorageIndex = m_storagePtrs.Length,
                    cleanupType       = t.cleanupType
                };

                var storage                            = AllocatorManager.Allocate<TypedCollectionComponentStorage<T> >(m_allocator);
                storage->collectionComponents          = new NativeList<T>(m_allocator);
                storage->freeStack                     = new NativeList<int>(m_allocator);
                storage->readHandles                   = new NativeList<FixedList512Bytes<JobHandle> >(m_allocator);
                storage->writeHandles                  = new NativeList<JobHandle>(m_allocator);
                storage->typeIndex                     = element.typedStorageIndex;
                m_storagePtrs.Add(new StoragePtr { ptr = storage });
                m_registeredTypeLookup.Add(key.typeHash, element);
            }

            indexInTypedStorage = -1;
            return ref m_storagePtrs[element.typedStorageIndex].As<T>();
        }
    }

    internal struct TypedCollectionComponentStorage<T> : IDisposable where T : unmanaged, ICollectionComponent, InternalSourceGen.StaticAPI.ICollectionComponentSourceGenerated
    {
        public NativeList<T>                             collectionComponents;
        public NativeList<JobHandle>                     writeHandles;
        public NativeList<FixedList512Bytes<JobHandle> > readHandles;  // Todo: Can we get away with smaller?
        public NativeList<int>                           freeStack;
        public int                                       typeIndex;

        public void TryDisposeIndexAndFree(int index, out JobHandle disposeHandle, bool indexWillBeImmediatelyRecycled)
        {
            ref var rh         = ref readHandles.ElementAt(index);
            var     jobHandles = new NativeArray<JobHandle>(rh.Length + 1, Allocator.Temp);
            jobHandles[0]      = writeHandles[index];
            for (int i = 0; i < rh.Length; i++)
                jobHandles[i + 1] = rh[i];
            disposeHandle         = collectionComponents.ElementAt(index).TryDispose(JobHandle.CombineDependencies(jobHandles));
            rh.Clear();
            writeHandles[index]         = default;
            collectionComponents[index] = default;
            if (!indexWillBeImmediatelyRecycled)
                freeStack.Add(index);
        }

        public void Dispose()
        {
            foreach (var readHandleBatch in readHandles)
            {
                foreach (var readHandle in readHandleBatch)
                {
                    writeHandles.Add(readHandle);
                }
            }
            JobHandle.CompleteAll(writeHandles.AsArray());
            writeHandles.Clear();
            // We need to compact the array in order to ensure we only call TryDispose on valid instances.
            freeStack.Sort();
            for (int i = freeStack.Length - 1; i >= 0; i--)
            {
                collectionComponents.RemoveAtSwapBack(freeStack[i]);  // Buggy?
            }
            foreach (var collectionComponent in collectionComponents)
            {
                writeHandles.Add(collectionComponent.TryDispose(default));
            }
            JobHandle.CompleteAll(writeHandles.AsArray());
            collectionComponents.Dispose();
            writeHandles.Dispose();
            readHandles.Dispose();
            freeStack.Dispose();
        }
    }

    internal unsafe struct CollectionComponentRef<T> where T : unmanaged, ICollectionComponent, InternalSourceGen.StaticAPI.ICollectionComponentSourceGenerated
    {
        TypedCollectionComponentStorage<T>* m_storage;
        Entity                              m_entity;
        int                                 m_indexInStorage;

        public CollectionComponentRef(ref TypedCollectionComponentStorage<T> storage, Entity entity, int indexInStorage)
        {
            fixed(TypedCollectionComponentStorage<T>* ptr = &storage)
            {
                m_storage = ptr;
            }
            m_entity         = entity;
            m_indexInStorage = indexInStorage;
        }

        public ref T collectionRef => ref m_storage->collectionComponents.ElementAt(m_indexInStorage);

        public CollectionComponentHandle collectionHandle => new CollectionComponentHandle(m_storage->writeHandles,
                                                                                           m_storage->readHandles,
                                                                                           BurstRuntime.GetHashCode64<T>(),
                                                                                           m_entity,
                                                                                           m_indexInStorage);

        public ref JobHandle writeHandle => ref m_storage->writeHandles.ElementAt(m_indexInStorage);
        public ref FixedList512Bytes<JobHandle> readHandles => ref m_storage->readHandles.ElementAt(m_indexInStorage);
    }

    internal unsafe struct CollectionComponentHandle
    {
        NativeList<JobHandle>                     m_writeHandles;
        NativeList<FixedList512Bytes<JobHandle> > m_readHandles;
        long                                      m_typeHash;
        Entity                                    m_entity;
        int                                       m_indexInStorage;

        public CollectionComponentHandle(NativeList<JobHandle> writeHandles, NativeList<FixedList512Bytes<JobHandle> > readHandles, long typeHash, Entity entity,
                                         int indexInStorage)
        {
            m_writeHandles   = writeHandles;
            m_readHandles    = readHandles;
            m_typeHash       = typeHash;
            m_entity         = entity;
            m_indexInStorage = indexInStorage;
        }

        public Entity entity => m_entity;
        public long typeHash => m_typeHash;
        public ref JobHandle writeHandle => ref m_writeHandles.ElementAt(m_indexInStorage);
        public ref FixedList512Bytes<JobHandle> readHandles => ref m_readHandles.ElementAt(m_indexInStorage);
    }
}

