using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

// Todo: Replace hashmap with one that has native fibonacci hashing support or come up with better hash function

namespace Latios.Unika
{
namespace InternalSourceGen
{
    public static unsafe partial class StaticAPI
    {
        public struct ContextPtr
        {
            public void* ptr;

            public static implicit operator ContextPtr(void* rawPte) => new ContextPtr { ptr = rawPte };
        }

        public delegate void BurstDispatchScriptDelegate(ContextPtr context, int operation);
    }
}

    internal struct ScriptVTable
    {
        private struct Key : IEquatable<Key>
        {
            int packed;

            public Key(short scriptId, short interfaceId)
            {
                int s  = scriptId;
                int i  = interfaceId;
                packed = (i << 16) | s;
            }

            public bool Equals(Key other)
            {
                return packed == other.packed;
            }

            public override int GetHashCode()
            {
                var hash = math.asuint(packed) * 2654435769u;
                // Normally in fibonacci hashing we would shift by 32 - log2(hashmap_slot_count)
                // However, we don't have the slot count. So instead we take the slower route of reversing
                // the bits, which has the same distribution properties just mapped to different slots.
                // Unfortunately, reversing bits is not a single instruction on x86, so something better
                // would be preferred.
                return math.asint(math.reversebits(hash));
            }
        }

        private static readonly SharedStatic<UnsafeHashMap<Key, FunctionPointer<InternalSourceGen.StaticAPI.BurstDispatchScriptDelegate> > > s_map =
            SharedStatic<UnsafeHashMap<Key, FunctionPointer<InternalSourceGen.StaticAPI.BurstDispatchScriptDelegate> > >.GetOrCreate<ScriptVTable>();

        public static void Add(short scriptId, short interfaceId, FunctionPointer<InternalSourceGen.StaticAPI.BurstDispatchScriptDelegate> interfaceVirtual)
        {
            s_map.Data.Add(new Key(scriptId, interfaceId), interfaceVirtual);
        }

        public static bool TryGet(short scriptId, short interfaceId, out FunctionPointer<InternalSourceGen.StaticAPI.BurstDispatchScriptDelegate> interfaceVirtual)
        {
            return s_map.Data.TryGetValue(new Key(scriptId, interfaceId), out interfaceVirtual);
        }

        public static bool Contains(short scriptId, short interfaceId)
        {
            return s_map.Data.ContainsKey(new Key(scriptId, interfaceId));
        }

        public static void InitializeStatics()
        {
            s_map.Data = new UnsafeHashMap<Key, FunctionPointer<InternalSourceGen.StaticAPI.BurstDispatchScriptDelegate> >(1024, Allocator.Persistent);
        }

        public static void DisposeStatics()
        {
            s_map.Data.Dispose();
        }
    }
}

