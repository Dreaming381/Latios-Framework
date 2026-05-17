using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.AuxEcs
{
    internal unsafe struct ArchetypeStore : IDisposable
    {
        UnsafeList<int> m_typeIds;  // indices into the allComponentsStore
        UnsafeList<int> m_entryData;  // Each entry is Entity.Index, Entity.Version, componentIndexOfTypeIds[0], componentIndexOfTypeIds[1], ect

        public ArchetypeStore(ReadOnlySpan<int> types, AllocatorManager.AllocatorHandle allocator)
        {
            m_typeIds = new UnsafeList<int>(types.Length, allocator);
            foreach (var type in types)
                m_typeIds.Add(type);
            m_entryData = new UnsafeList<int>((types.Length + 2) * 8, allocator);
        }

        public void Dispose()
        {
            m_typeIds.Dispose();
            m_entryData.Dispose();
        }

        public int instanceCount => m_entryData.Length / (m_typeIds.Length + 2);

        public int Add(Entity entity, ReadOnlySpan<int> componentIndices)
        {
            int payloadInts = m_typeIds.Length + 2;
            int result      = m_entryData.Length / payloadInts;
            m_entryData.Add(entity.Index);
            m_entryData.Add(entity.Version);
            foreach (var componentIndex in componentIndices)
                m_entryData.Add(componentIndex);
            return result;
        }

        public struct RemoveOperation
        {
            public Entity swappedBackEntity;
            public int    newIndex;
        }

        public RemoveOperation Remove(int indexInArchetype)
        {
            int payloadInts        = m_typeIds.Length + 2;
            int removeFirstIndex   = payloadInts * indexInArchetype;
            int swapBackFirstIndex = m_entryData.Length - payloadInts;
            if (swapBackFirstIndex == removeFirstIndex)
            {
                m_entryData.Length -= payloadInts;
                return default;
            }
            for (int i = 0; i < payloadInts; i++)
            {
                m_entryData[removeFirstIndex + i] = m_entryData[swapBackFirstIndex + i];
            }
            m_entryData.Length -= payloadInts;
            return new RemoveOperation
            {
                newIndex          = removeFirstIndex / payloadInts,
                swappedBackEntity = new Entity { Index = m_entryData[removeFirstIndex], Version = m_entryData[removeFirstIndex + 1] }
            };
        }

        public int GetTypeIndexInArchetype(int typeId)
        {
            return m_typeIds.BinarySearch(typeId);
        }

        public ReadOnlySpan<int> typeIds => new ReadOnlySpan<int>(m_typeIds.Ptr, m_typeIds.Length);

        public ReadOnlySpan<int> GetComponentIndicesForEntityIndex(int entityIndexInArchetype)
        {
            int payloadInts = m_typeIds.Length + 2;
            var start       = payloadInts * entityIndexInArchetype + 2;
            var span        = new ReadOnlySpan<int>(m_entryData.Ptr, m_entryData.Length);
            return span.Slice(start, m_typeIds.Length);
        }

        public Entity GetEntity(int entityIndexInArchetype)
        {
            int payloadInts           = m_typeIds.Length + 2;
            var start                 = payloadInts * entityIndexInArchetype;
            return new Entity { Index = m_entryData[start], Version = m_entryData[start + 1] };
        }

        public bool TryMatch(ReadOnlySpan<int> typeIdsToMatch, Span<int> typeIndicesInArchetypeResult)
        {
            for (int i = 0; i < typeIdsToMatch.Length; i++)
            {
                var index = GetTypeIndexInArchetype(typeIdsToMatch[i]);
                if (index < 0)
                    return false;
                typeIndicesInArchetypeResult[i] = index;
            }
            return true;
        }

        public bool Matches(ReadOnlySpan<int> typeIdsToMatch)
        {
            for (int i = 0; i < typeIdsToMatch.Length; i++)
            {
                var index = GetTypeIndexInArchetype(typeIdsToMatch[i]);
                if (index < 0)
                    return false;
            }
            return true;
        }
    }
}

