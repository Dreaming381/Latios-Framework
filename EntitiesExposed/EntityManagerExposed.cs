using System;
using System.Diagnostics;
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

        // Todo: Make a Burst-Compatible version.
        [NotBurstCompatible]
        public static void CopySharedComponent(this EntityManager entityManager, Entity src, Entity dst, ComponentType componentType)
        {
            CheckComponentTypeIsSharedComponent(componentType);

            entityManager.AddComponent(dst, componentType);
            var handle = entityManager.GetDynamicSharedComponentTypeHandle(componentType);
            var chunk  = entityManager.GetStorageInfo(src);
            var index  = chunk.Chunk.GetSharedComponentIndex(handle);
            var box    = index != 0 ? chunk.Chunk.GetSharedComponentDataBoxed(handle, entityManager) : null;
            entityManager.SetSharedComponentDataBoxedDefaultMustBeNull(dst, componentType.TypeIndex, box);
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        private static void CheckComponentTypeIsSharedComponent(ComponentType type)
        {
            if (!type.IsSharedComponent)
                throw new ArgumentException($"Attempted to call EntityManager.CopySharedComponent on {type} which is not an ISharedComponentData");
        }
    }
}

