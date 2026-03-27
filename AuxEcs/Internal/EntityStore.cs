using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.AuxEcs
{
    internal struct EntityStore : IDisposable
    {
        public struct Location
        {
            public int archetypeIndex;
            public int indexInArchetype;
        }

        UnsafeHashMap<Entity, Location> entityMap;

        public EntityStore(AllocatorManager.AllocatorHandle allocator)
        {
            entityMap = new UnsafeHashMap<Entity, Location>(128, allocator);
        }

        public void Dispose()
        {
            entityMap.Dispose();
        }

        public bool TryGetLocation(Entity entity, out Location location)
        {
            var result = entityMap.TryGetValue(entity, out location);
            if (!result)
                location.archetypeIndex = -1;
            return result;
        }

        public void SetLocation(Entity entity, Location location)
        {
            entityMap[entity] = location;
        }

        public void Remove(Entity entity)
        {
            entityMap.Remove(entity);
        }
    }
}

