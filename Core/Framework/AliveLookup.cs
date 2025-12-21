using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Mathematics;

namespace Latios
{
    public static class AliveExtensions
    {
        /// <summary>
        /// Returns true if the entity exists and is not in a cleanup state
        /// </summary>
        public static bool IsAlive(this EntityManager entityManager, Entity entity)
        {
            if (!entityManager.Exists(entity))
                return false;
            return !entityManager.GetChunk(entity).Archetype.IsCleanup();
        }
        /// <summary>
        /// Returns true if the entity exists and is not in a cleanup state
        /// </summary>
        public static bool IsAlive(this EntityStorageInfoLookup lookup, Entity entity)
        {
            if (!lookup.Exists(entity))
                return false;
            return !lookup[entity].Chunk.Archetype.IsCleanup();
        }
    }
}

