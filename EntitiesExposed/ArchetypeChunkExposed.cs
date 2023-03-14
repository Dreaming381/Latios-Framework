
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Entities.Exposed
{
    public static class ArchetypeChunkExposedExtensions
    {
        public static unsafe ref T GetChunkComponentRefRW<T>(in this ArchetypeChunk chunk, ref ComponentTypeHandle<T> chunkComponentTypeHandle) where T : unmanaged, IComponentData
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(chunkComponentTypeHandle.m_Safety);
#endif
            chunk.m_EntityComponentStore->AssertEntityHasComponent(chunk.m_Chunk->metaChunkEntity, chunkComponentTypeHandle.m_TypeIndex);
            var ptr = chunk.m_EntityComponentStore->GetComponentDataWithTypeRW(chunk.m_Chunk->metaChunkEntity,
                                                                               chunkComponentTypeHandle.m_TypeIndex,
                                                                               chunkComponentTypeHandle.GlobalSystemVersion);
            return ref UnsafeUtility.AsRef<T>(ptr);
        }

        public static unsafe RefRO<T> GetChunkComponentRefRO<T>(in this ArchetypeChunk chunk, ref ComponentTypeHandle<T> chunkComponentTypeHandle) where T : unmanaged,
        IComponentData
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(chunkComponentTypeHandle.m_Safety);
#endif
            chunk.m_EntityComponentStore->AssertEntityHasComponent(chunk.m_Chunk->metaChunkEntity, chunkComponentTypeHandle.m_TypeIndex);
            var ptr = chunk.m_EntityComponentStore->GetComponentDataWithTypeRO(chunk.m_Chunk->metaChunkEntity, chunkComponentTypeHandle.m_TypeIndex);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            return new RefRO<T>(ptr, chunkComponentTypeHandle.m_Safety);
#else
            return new RefRO<T>(ptr);
#endif
        }

        public static unsafe Entity GetMetaEntity(in this ArchetypeChunk chunk)
        {
            return chunk.m_Chunk->metaChunkEntity;
        }

        public static unsafe ulong GetChunkPtrAsUlong(in this ArchetypeChunk chunk)
        {
            return (ulong)chunk.m_Chunk;
        }
    }
}

