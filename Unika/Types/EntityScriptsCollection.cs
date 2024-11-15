using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Unika
{
    /// <summary>
    /// Contains all scripts attached to an entity which can be iterated over. This is invalidated by a ScriptStructuralChange operation.
    /// </summary>
    public struct EntityScriptCollection : IScriptResolverBase
    {
        internal NativeArray<ScriptHeader> m_buffer;
        internal Entity                    m_entity;

        /// <summary>
        /// The entity these scripts belong to
        /// </summary>
        public Entity entity => m_entity;

        /// <summary>
        /// The number of scripts belonging to this entity.
        /// </summary>
        public int length => m_buffer.Length > 0 ? m_buffer[0].instanceCount : 0;

        /// <summary>
        /// Returns true if this entity has no scripts.
        /// </summary>
        public bool isEmpty => m_buffer.Length == 0;

        /// <summary>
        /// Retrives a resolved untyped script at the specified index
        /// </summary>
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

        /// <summary>
        /// Appends an IScriptFilter which applies filtering to the scripts in the collection. Filters are applied from first to last.
        /// </summary>
        /// <typeparam name="TNew">The type of new filter to append</typeparam>
        /// <param name="filter">The new filter instance to append</param>
        /// <returns>A cascade builder API</returns>
        public UntypedScriptFilterCascade<TNew> Where<TNew>(TNew filter) where TNew : unmanaged, IScriptFilterBase =>
        new UntypedScriptFilterCascade<TNew>(this, filter);

        /// <summary>
        /// Creates a filterable enumeration of all scripts in the collection which can be casted to the specified type.
        /// </summary>
        /// <typeparam name="T">The target representation of the script in the enumeration, either a Script<> or an IUnikaInterface.Interface</typeparam>
        /// <returns>A cascade builder API</returns>
        public TypedScriptFilterCascade<T> Of<T>() where T : unmanaged, IScriptTypedExtensionsApi => new TypedScriptFilterCascade<T>(this);

        /// <summary>
        /// Creates a filterable enumeration of all scripts in the collection which are of the specified type.
        /// </summary>
        /// <typeparam name="T">The target type of script to enumerate</typeparam>
        /// <returns>A cascade builder API</returns>
        public TypedScriptFilterCascade<Script<T> > OfType<T>() where T : unmanaged, IUnikaScript, IUnikaScriptGen => new TypedScriptFilterCascade<Script<T> >(this);

        /// <summary>
        /// Creates a NativeArray of resolved script handles
        /// </summary>
        /// <param name="allocator">The allocator that should be used to create the NativeArray</param>
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

        /// <summary>
        /// Gets an iterator for all the scripts on this entity
        /// </summary>
        public Enumerator GetEnumerator() => new Enumerator(this);

        bool IScriptResolverBase.TryGet(Entity entity, out EntityScriptCollection allScripts, bool throwSafetyErrorIfNotFound)
        {
            // Defer all validation to the next stage, since the error messages will be identical.
            allScripts = this;
            return true;
        }

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
        /// <summary>
        /// Gets the EntityScriptCollection derived from this script buffer and the passed in entity.
        /// WARNING: Passing a different entity than the one this buffer belongs to will result in crashes or memory corruption.
        /// </summary>
        /// <param name="entity">The entity this buffer belongs to</param>
        /// <returns>The EntityScriptCollection which can be used to index scripts within the buffer</returns>
        public static EntityScriptCollection AllScripts(this DynamicBuffer<UnikaScripts> buffer, Entity entity)
        {
            return new EntityScriptCollection
            {
                m_buffer = buffer.Reinterpret<ScriptHeader>().AsNativeArray(),
                m_entity = entity
            };
        }

        /// <summary>
        /// Gets the EntityScriptCollection derived from this script buffer and the passed in entity.
        /// WARNING: Passing a different entity than the one this buffer belongs to will result in crashes or memory corruption.
        /// </summary>
        /// <param name="entity">The entity this buffer belongs to</param>
        /// <returns>The EntityScriptCollection which can be used to index scripts within the buffer</returns>
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

