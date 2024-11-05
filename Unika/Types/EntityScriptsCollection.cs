using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Unika
{
    public struct EntityScriptCollection : IScriptResolverBase
    {
        internal NativeArray<ScriptHeader> m_buffer;
        internal Entity                    m_entity;

        public Entity entity => m_entity;

        public int length => m_buffer.Length > 0 ? m_buffer[0].instanceCount : 0;

        public bool isEmpty => m_buffer.Length == 0;

        public Script this[int index]
        {
            get
            {
                CheckIndexInRange(index);
                var baseByteOffset = (1 + math.ceilpow2(length)) * UnsafeUtility.SizeOf<ScriptHeader>();
                return new Script
                {
                    m_scriptBuffer = m_buffer,
                    m_entity       = entity,
                    m_headerOffset = (1 + index) * UnsafeUtility.SizeOf<ScriptHeader>(),
                    m_byteOffset   = baseByteOffset + m_buffer[index + 1].byteOffset
                };
            }
        }

        public bool TryGet(Entity entity, out EntityScriptCollection allScripts, bool throwSafetyErrorIfNotFound = false)
        {
            // Defer all validation to the next stage, since the error messages will be identical.
            allScripts = this;
            return true;
        }

        public NativeArray<Script> ToNativeArray(AllocatorManager.AllocatorHandle allocator)
        {
            var result = CollectionHelper.CreateNativeArray<Script>(length, allocator, NativeArrayOptions.UninitializedMemory);
            int i      = 0;
            foreach (var s in this)
            {
                result[i] = s;
                i++;
            }
            return result;
        }

        public Enumerator GetEnumerator() => new Enumerator(this);

        public struct Enumerator
        {
            NativeArray<ScriptHeader> buffer;
            Entity                    entity;
            int                       index;
            int                       count;
            int                       baseByteOffset;

            public Enumerator(EntityScriptCollection collection)
            {
                buffer         = collection.m_buffer;
                entity         = collection.m_entity;
                index          = -1;
                count          = collection.length;
                baseByteOffset = (1 + math.ceilpow2(count)) * UnsafeUtility.SizeOf<ScriptHeader>();
            }

            public Script Current => new Script
            {
                m_scriptBuffer = buffer,
                m_entity       = entity,
                m_headerOffset = (1 + index) * UnsafeUtility.SizeOf<ScriptHeader>(),
                m_byteOffset   = baseByteOffset + buffer[index + 1].byteOffset
            };

            public bool MoveNext()
            {
                index++;
                return index < count;
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckIndexInRange(int index)
        {
            if (index < 0)
                throw new ArgumentOutOfRangeException($"Index {index} is negative.");
            if (index >= length)
                throw new ArgumentOutOfRangeException($"Index {index} is outside the range of scripts of entity {entity.ToFixedString()} which contains {length} scripts.");
        }
    }

    public static class ScriptsDynamicBufferExtensions
    {
        // Note: The wrong m_entity here could lead to crashes or other bad behavior
        public static EntityScriptCollection AllScripts(this DynamicBuffer<UnikaScripts> buffer, Entity entity)
        {
            return new EntityScriptCollection
            {
                m_buffer = buffer.Reinterpret<ScriptHeader>().AsNativeArray(),
                m_entity = entity
            };
        }

        public static EntityScriptCollection AllScripts(this NativeArray<UnikaScripts> buffer, Entity entity)
        {
            return new EntityScriptCollection
            {
                m_buffer = buffer.Reinterpret<ScriptHeader>(),
                m_entity = entity
            };
        }
    }
}

