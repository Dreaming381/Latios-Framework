using System.Runtime.CompilerServices;
using Unity.Burst.Intrinsics;
using Unity.Entities;

namespace Latios
{
    public struct ChunkEntityWithIndexEnumerator
    {
        ChunkEntityEnumerator m_enumerator;
        int                   m_entityInQueryIndex;

        public ChunkEntityWithIndexEnumerator(bool useEnabledMask, v128 chunkEnabledMask, int chunkEntityCount, int baseEntityInQueryIndexForChunk)
        {
            m_enumerator         = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunkEntityCount);
            m_entityInQueryIndex = baseEntityInQueryIndexForChunk;
        }

        /// <summary>
        /// Use as the condition in a while loop
        /// </summary>
        /// <param name="indexInChunk">The index in the chunk for accessing arrays in chunks</param>
        /// <param name="indexInEntityQuery">The index in the query for accessing external arrays</param>
        /// <returns>If true, the indices are valid for processing. If false, there are no more entities to process.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool NextEntityIndex(out int indexInChunk, out int indexInEntityQuery)
        {
            indexInEntityQuery = m_entityInQueryIndex;
            m_entityInQueryIndex++;
            return m_enumerator.NextEntityIndex(out indexInChunk);
        }
    }
}

