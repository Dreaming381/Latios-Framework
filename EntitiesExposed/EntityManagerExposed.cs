using System;
using Unity.Collections;

namespace Unity.Entities.Exposed
{
    public unsafe struct EntityLocationInChunk : IEquatable<EntityLocationInChunk>, IComparable<EntityLocationInChunk>
    {
        public ArchetypeChunk chunk;
        public int            indexInChunk;

        public ulong ChunkAddressAsUlong => (ulong)chunk.m_Chunk;

        public int CompareTo(EntityLocationInChunk other)
        {
            ulong lhs          = (ulong)chunk.m_Chunk;
            ulong rhs          = (ulong)other.chunk.m_Chunk;
            int   chunkCompare = lhs < rhs ? -1 : 1;
            int   indexCompare = indexInChunk - other.indexInChunk;
            return (lhs != rhs) ? chunkCompare : indexCompare;
        }

        public bool Equals(EntityLocationInChunk other)
        {
            return chunk.Equals(other.chunk) && indexInChunk.Equals(other.indexInChunk);
        }
    }

    public static unsafe class EntityManagerExposed
    {
        [BurstCompatible]
        public static EntityLocationInChunk GetEntityLocationInChunk(this EntityManager entityManager, Entity entity)
        {
            var ecs           = entityManager.GetCheckedEntityDataAccess()->EntityComponentStore;
            var entityInChunk = ecs->GetEntityInChunk(entity);
            return new EntityLocationInChunk
            {
                chunk        = new ArchetypeChunk(entityInChunk.Chunk, ecs),
                indexInChunk = entityInChunk.IndexInChunk
            };
        }
    }
}

