using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.AuxEcs
{
    internal struct AllArchetypesStore : IDisposable
    {
        UnsafeList<ArchetypeStore> archetypes;

        public AllArchetypesStore(AllocatorManager.AllocatorHandle allocator)
        {
            archetypes = new UnsafeList<ArchetypeStore>(8, allocator);
        }

        public void Dispose()
        {
            foreach (var archetype in archetypes)
                archetype.Dispose();
            archetypes.Dispose();
        }

        public AllocatorManager.AllocatorHandle allocator => archetypes.Allocator;
        public int archetypesCount => archetypes.Length;

        public ref ArchetypeStore this[int index] => ref archetypes.ElementAt(index);

        public ref ArchetypeStore GetOrAddArchetype(ReadOnlySpan<int> typeIds, out int archetypeIndex)
        {
            for (int i = 0; i < archetypes.Length; i++)
            {
                ref var archetype = ref archetypes.ElementAt(i);
                if (typeIds.Length != archetype.typeIds.Length)
                    continue;
                bool matched = true;
                for (int j = 0; j < typeIds.Length; j++)
                {
                    if (typeIds[j] != archetype.typeIds[j])
                    {
                        matched = false;
                        break;
                    }
                }
                if (matched)
                {
                    archetypeIndex = i;
                    return ref archetype;
                }
            }
            archetypes.Add(new ArchetypeStore(typeIds, archetypes.Allocator));
            archetypeIndex = archetypes.Length - 1;
            return ref archetypes.ElementAt(archetypes.Length - 1);
        }
    }
}

