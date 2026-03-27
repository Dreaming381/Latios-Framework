using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Latios.AuxEcs
{
    internal unsafe struct AllComponentsStore : IDisposable
    {
        UnsafeList<ComponentStore> componentStores;
        UnsafeHashMap<long, int>   typeHashToComponentIndexMap;

        public AllComponentsStore(AllocatorManager.AllocatorHandle allocator)
        {
            componentStores             = new UnsafeList<ComponentStore>(8, allocator);
            typeHashToComponentIndexMap = new UnsafeHashMap<long, int>(8, allocator);
        }

        public void Dispose()
        {
            foreach (var componentStore in componentStores)
            {
                componentStore.Dispose();
            }
            componentStores.Dispose();
            typeHashToComponentIndexMap.Dispose();
        }

        public ref ComponentStore GetOrAddStore<T>(out int typeId) where T : unmanaged
        {
            var hash = BurstRuntime.GetHashCode64<T>();
            if (typeHashToComponentIndexMap.TryGetValue(hash, out typeId))
                return ref componentStores.ElementAt(typeId);

            typeId = componentStores.Length;
            componentStores.Add(new ComponentStore(UnsafeUtility.SizeOf<T>(), UnsafeUtility.AlignOf<T>(), hash, componentStores.Allocator));
            typeHashToComponentIndexMap.Add(hash, typeId);
            return ref componentStores.ElementAt(typeId);
        }

        public ComponentStore* TryGetStore<T>(out int typeId) where T : unmanaged
        {
            var hash = BurstRuntime.GetHashCode64<T>();
            if (typeHashToComponentIndexMap.TryGetValue(hash, out typeId))
                return componentStores.Ptr + typeId;
            return null;
        }

        public ref ComponentStore this[int typeId] => ref componentStores.ElementAt(typeId);

        public ComponentStore* GetStorePtr(int typeId) => componentStores.Ptr + typeId;
    }
}

