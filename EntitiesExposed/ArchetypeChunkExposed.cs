
using System;
using Unity.Burst.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities.LowLevel.Unsafe;

namespace Unity.Entities.Exposed
{
    public static class ArchetypeChunkExposedExtensions
    {
        public static unsafe ref T GetChunkComponentRefRW<T>(in this ArchetypeChunk chunk, ref ComponentTypeHandle<T> chunkComponentTypeHandle) where T : unmanaged, IComponentData
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(chunkComponentTypeHandle.m_Safety);
#endif
            chunk.m_EntityComponentStore->AssertEntityHasComponent(chunk.m_Chunk.MetaChunkEntity, chunkComponentTypeHandle.m_TypeIndex);
            var ptr = chunk.m_EntityComponentStore->GetComponentDataWithTypeRW(chunk.m_Chunk.MetaChunkEntity,
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
            chunk.m_EntityComponentStore->AssertEntityHasComponent(chunk.m_Chunk.MetaChunkEntity, chunkComponentTypeHandle.m_TypeIndex);
            var ptr = chunk.m_EntityComponentStore->GetComponentDataWithTypeRO(chunk.m_Chunk.MetaChunkEntity, chunkComponentTypeHandle.m_TypeIndex);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            return new RefRO<T>(ptr, chunkComponentTypeHandle.m_Safety);
#else
            return new RefRO<T>(ptr);
#endif
        }

        public static unsafe Entity GetMetaEntity(in this ArchetypeChunk chunk)
        {
            return chunk.m_Chunk.MetaChunkEntity;
        }

        public static unsafe uint GetChunkIndexAsUint(in this ArchetypeChunk chunk)
        {
            return Mathematics.math.asuint(chunk.m_Chunk);
        }

        /// <summary>
        /// Provides access to a chunk's array of component values for a specific buffer component type.
        /// </summary>
        /// <param name="bufferTypeHandle">The type handle for the target component type.</param>
        /// <typeparam name="T">The target component type, which must inherit <see cref="IBufferElementData"/>.</typeparam>
        /// <returns>An interface to this chunk's component values for type <typeparamref name="T"/></returns>
        public unsafe static BufferAccessor<T> GetBufferAccessor<T>(in this ArchetypeChunk chunk, ref DynamicComponentTypeHandle bufferTypeHandle)
            where T : unmanaged, IBufferElementData
        {
            var typeIndex = TypeManager.GetTypeIndex<T>();
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (typeIndex != bufferTypeHandle.m_TypeIndex)
                throw new System.ArgumentException($"ArchetypeChunk.GetBufferAccessor<T> must be called only for the type stored in the DynamicComponentTypeHandle");
#endif

            var accessor = chunk.GetUntypedBufferAccessor(ref bufferTypeHandle);
            if (accessor.Length == 0)
                return default;

            ref readonly var info = ref TypeManager.GetTypeInfo(typeIndex);
            // Todo: Super dangerous. It would be much better to get the header pointer some other way.
            return new BufferAccessor<T>(UnsafeUtility.As<UnsafeUntypedBufferAccessor, BytePtr>(ref accessor).ptr, accessor.Length, info.SizeInChunk,
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                                         bufferTypeHandle.IsReadOnly, bufferTypeHandle.m_Safety0, bufferTypeHandle.m_Safety1,
#endif
                                         info.BufferCapacity);
        }

        unsafe struct BytePtr
        {
            public byte* ptr;
        }

        // Todo: Move to dedicated file if more extensions are needed.
        public static unsafe bool UsesEnabledFiltering(in this EntityQuery query)
        {
            return query._GetImpl()->_QueryData->HasEnableableComponents != 0;
        }

        public static unsafe ulong GetBloomMask(in this EntityArchetype archetype) => archetype.Archetype->BloomFilterMask;
        public static unsafe bool HasChunkHeader(in this EntityArchetype archetype) => archetype.Archetype->HasChunkHeader;
        public static unsafe bool HasSystemInstanceComponents(in this EntityArchetype archetype) => archetype.Archetype->HasSystemInstanceComponents;
        public static unsafe int GetChunkComponentCount(in this EntityArchetype archetype) => archetype.Archetype->NumChunkComponents;
        public static unsafe int GetBufferComponentCount(in this EntityArchetype archetype) => archetype.Archetype->NumBufferComponents;
        public static unsafe TypeIndex GetTypeAtIndex(in this EntityArchetype archetype, int index) => archetype.Archetype->Types[index].TypeIndex;
        public static unsafe ArchetypeChunk GetChunkAtIndex(in this EntityArchetype archetype, int index) => new ArchetypeChunk(archetype.Archetype->Chunks[index],
                                                                                                                                archetype.Archetype->EntityComponentStore);
    }
}

