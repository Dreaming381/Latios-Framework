using System;
using System.Diagnostics;
using Unity.Burst;

namespace Latios
{
    /// <summary>
    /// A span of memory owned by a command buffer, which can be stored as a field inside of any
    /// command added to that command buffer. These can be nested.
    /// </summary>
    /// <typeparam name="T">The type of element stored within the span</typeparam>
    public unsafe struct CommandSpan<T> where T : unmanaged
    {
        /// <summary>
        /// The number of elements in the span
        /// </summary>
        public int length => m_length;
        /// <summary>
        /// Gets the pointer to the raw memory of the span
        /// </summary>
        /// <returns></returns>
        public T* GetUnsafePtr() => m_ptr;
        /// <summary>
        /// Gets an element of the span by ref
        /// </summary>
        /// <param name="index">The index of the element to fetch</param>
        /// <returns>The element at the specified index</returns>
        public ref T this[int index] => ref AsSpan()[index];
        /// <summary>
        /// Gets an enumerator over the span
        /// </summary>
        /// <returns></returns>
        public Span<T>.Enumerator GetEnumerator() => AsSpan().GetEnumerator();
        /// <summary>
        /// Returns the CommandSpan as a .NET Span
        /// </summary>
        /// <returns></returns>
        public Span<T> AsSpan() => new Span<T>(m_ptr, length);

        /// <summary>
        /// Implicitly converts this CommandSpan into a DynamicCommandSpan.
        /// </summary>
        public static implicit operator DynamicCommandSpan(CommandSpan<T> commandSpan)
        {
            return new DynamicCommandSpan
            {
                m_ptr      = commandSpan.m_ptr,
                m_length   = commandSpan.m_length,
                m_typeHash = BurstRuntime.GetHashCode32<T>()
            };
        }

        internal T*  m_ptr;
        internal int m_length;
    }

    /// <summary>
    /// A type-punned version of CommandSpan which stores the type hash for safety referencing.
    /// </summary>
    public unsafe struct DynamicCommandSpan
    {
        /// <summary>
        /// Try to retrieve the CommandSpan of the specified type. Throws if the type hash doesn't match.
        /// </summary>
        public CommandSpan<T> GetSpan<T>() where T : unmanaged
        {
            CheckTypeHash<T>();
            return new CommandSpan<T> { m_ptr = (T*)m_ptr, m_length = m_length };
        }

        internal void* m_ptr;
        internal int   m_length;
        internal int   m_typeHash;

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        void CheckTypeHash<T>() where T : unmanaged
        {
            if (m_typeHash != BurstRuntime.GetHashCode32<T>())
                throw new InvalidOperationException($"Attempted to access a CommandSpan from a DynamicCommandSpan using the wrong type.");
        }
    }
}

