using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Calligraphics
{
    /// <summary>
    /// A Burst-compatible string type that wraps a DynamicBuffer of bytes
    /// </summary>
    public struct CalliString :
        INativeList<byte>,
            IUTF8Bytes,
            IComparable<string>,
            IEquatable<string>,
            IComparable<CalliString>,
            IEquatable<CalliString>,
            IComparable<NativeText>,
            IEquatable<NativeText>,
            IComparable<FixedString32Bytes>,
            IEquatable<FixedString32Bytes>,
            IComparable<FixedString64Bytes>,
            IEquatable<FixedString64Bytes>,
            IComparable<FixedString128Bytes>,
            IEquatable<FixedString128Bytes>,
            IComparable<FixedString512Bytes>,
            IEquatable<FixedString512Bytes>,
            IComparable<FixedString4096Bytes>,
            IEquatable<FixedString4096Bytes>
    {
        DynamicBuffer<byte> m_stringBuffer;

        public CalliString(DynamicBuffer<CalliByte> buffer)
        {
            m_stringBuffer = buffer.Reinterpret<byte>();
            if (m_stringBuffer.IsEmpty)
                m_stringBuffer.Add(0);
        }

        public CalliString(DynamicBuffer<byte> buffer)
        {
            m_stringBuffer = buffer;
            if (m_stringBuffer.IsEmpty)
                m_stringBuffer.Add(0);
        }

        /// <summary>
        /// The byte at an index.
        /// </summary>
        /// <param name="index">A zero-based byte index.</param>
        /// <value>The byte at the index.</value>
        /// <exception cref="IndexOutOfRangeException">Thrown if the index is out of bounds.</exception>
        public byte this[int index] { get => m_stringBuffer[index]; set => m_stringBuffer[index] = value; }

        /// <summary>
        /// The current capacity in bytes of this string.
        /// </summary>
        /// <remarks>
        /// The null-terminator byte is not included in the capacity, so the string's character buffer is `Capacity + 1` in size.
        /// </remarks>
        /// <value>The current capacity in bytes of the string.</value>
        public int Capacity { get => m_stringBuffer.Capacity - 1; set => m_stringBuffer.Capacity = value + 1; }

        /// <summary>
        /// Whether this string has no characters.
        /// </summary>
        /// <value>True if this string has no characters or the string has not been constructed.</value>
        /// <exception cref="NotSupportedException">Thrown if ENABLE_UNITY_COLLECTIONS_CHECKS is defined and a write is attempted.</exception>
        public bool IsEmpty => Length == 0;

        /// <summary>
        /// The current length in bytes of this string.
        /// </summary>
        /// <remarks>
        /// The length does not include the null terminator byte.
        /// </remarks>
        /// <value>The current length in bytes of the UTF-8 encoded string.</value>
        public int Length
        {
            get => math.max(m_stringBuffer.Length - 1, 0);
            set
            {
                m_stringBuffer.Resize(value + 1, NativeArrayOptions.UninitializedMemory);
                m_stringBuffer[value] = 0;
            }
        }

        /// <summary>
        /// Sets the length to 0.
        /// </summary>
        public void Clear() => Length = 0;

        /// <summary>
        /// Returns a reference to the byte (not character) at an index.
        /// </summary>
        /// <remarks>
        /// Deallocating or reallocating this string's character buffer makes the reference invalid.
        /// </remarks>
        /// <param name="index">A byte index.</param>
        /// <returns>A reference to the byte at the index.</returns>
        /// <exception cref="IndexOutOfRangeException">Thrown if the index is out of bounds.</exception>
        public ref byte ElementAt(int index) => ref m_stringBuffer.ElementAt(index);

        /// <summary>
        /// Returns a pointer to this string's character buffer.
        /// </summary>
        /// <remarks>
        /// The pointer is made invalid by operations that reallocate the character buffer, such as setting <see cref="Capacity"/>.
        /// </remarks>
        /// <returns>A pointer to this string's character buffer.</returns>
        public unsafe byte* GetUnsafePtr() => (byte*)m_stringBuffer.GetUnsafePtr();

        /// <summary>
        /// Returns a pointer to this string's character buffer for readonly access.
        /// </summary>
        /// <remarks>
        /// The pointer is made invalid by operations that reallocate the character buffer, such as setting <see cref="Capacity"/>.
        /// </remarks>
        /// <returns>A pointer to this string's character buffer.</returns>
        public unsafe byte* GetUnsafeReadOnlyPtr() => (byte*)m_stringBuffer.GetUnsafeReadOnlyPtr();

        /// <summary>
        /// Attempt to set the length in bytes of this string.
        /// </summary>
        /// <param name="newLength">The new length in bytes of the string.</param>
        /// <param name="clearOptions">Whether any bytes added should be zeroed out.</param>
        /// <returns>Always true.</returns>
        public bool TryResize(int newLength, NativeArrayOptions clearOptions = NativeArrayOptions.ClearMemory)
        {
            Length = newLength;
            return true;
        }

        public static implicit operator CalliString(DynamicBuffer<CalliByte> buffer)
        {
            return new CalliString(buffer);
        }

        /// <summary>
        /// An enumerator over the characters (not bytes) of a CalliString.
        /// </summary>
        /// <remarks>
        /// In an enumerator's initial state, its index is invalid. The first <see cref="MoveNext"/> call advances the enumerator's index to the first character.
        /// </remarks>
        public struct Enumerator : IEnumerator<Unicode.Rune>
        {
            CalliString target;
            int m_currentByteIndex;
            int m_currentCharIndex;
            Unicode.Rune current;

            /// <summary>
            /// Initializes and returns an instance of CalliString.Enumerator.
            /// </summary>
            /// <param name="source">A NativeText for which to create an enumerator.</param>
            public Enumerator(CalliString source)
            {
                target = source;
                m_currentByteIndex = 0;
                m_currentCharIndex = 0;
                current = default;
            }

            /// <summary>
            /// Does nothing.
            /// </summary>
            public void Dispose()
            {
            }

            /// <summary>
            /// Sets offset to provided byte (not character!) position <see cref="Current"/> is valid to read afterwards.
            /// </summary>
            /// <returns>True if <see cref="Current"/> is valid to read after the call.</returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool GotoByteIndex(int bytePosition)
            {
                if (bytePosition >= target.Length)
                    return false;

                m_currentByteIndex = bytePosition;

                return true;
            }

            /// <summary>
            /// Advances the enumerator to the next character, returning true if <see cref="Current"/> is valid to read afterwards.
            /// </summary>
            /// <returns>True if <see cref="Current"/> is valid to read after the call.</returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                if (m_currentByteIndex >= target.Length)
                    return false;

                unsafe
                {
                    Unicode.Utf8ToUcs(out current, target.GetUnsafeReadOnlyPtr(), ref m_currentByteIndex, target.Length);
                    m_currentCharIndex += 1;
                }

                return true;
            }

            public bool MovePrevious()
            {
                if (m_currentByteIndex >= current.LengthInUtf8Bytes())
                {
                    m_currentByteIndex -= current.LengthInUtf8Bytes();
                    return true;
                }
                return false;
            }

            /// <summary>
            /// Resets the enumerator to its initial state.
            /// </summary>
            public void Reset()
            {
                m_currentByteIndex = 0;
                current = default;
            }

            object IEnumerator.Current
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => Current;
            }

            /// <summary>
            /// The current character.
            /// </summary>
            /// <value>The current character.</value>
            public Unicode.Rune Current => current;

            /// <summary>
            /// The startIndex in bytes of the current character.
            /// </summary>
            /// <value>The current character byte index.</value>
            public int CurrentByteIndex => m_currentByteIndex;
            /// <summary>
            /// The index of the current character in chars.
            /// </summary>
            /// <value>The current character char index</value>
            public int CurrentCharIndex => m_currentCharIndex;
        }

        /// <summary>
        /// Returns an enumerator for iterating over the characters of the NativeText.
        /// </summary>
        /// <returns>An enumerator for iterating over the characters of the NativeText.</returns>
        public Enumerator GetEnumerator()
        {
            return new Enumerator(this);
        }

        public bool Equals(string other)
        {
            throw new NotImplementedException();
        }

        public bool Equals(NativeText other)
        {
            throw new NotImplementedException();
        }

        public bool Equals(CalliString other)
        {
            throw new NotImplementedException();
        }

        public bool Equals(FixedString32Bytes other)
        {
            throw new NotImplementedException();
        }

        public bool Equals(FixedString64Bytes other)
        {
            throw new NotImplementedException();
        }

        public bool Equals(FixedString128Bytes other)
        {
            throw new NotImplementedException();
        }

        public bool Equals(FixedString512Bytes other)
        {
            throw new NotImplementedException();
        }

        public bool Equals(FixedString4096Bytes other)
        {
            throw new NotImplementedException();
        }

        public int CompareTo(string other)
        {
            throw new NotImplementedException();
        }

        public int CompareTo(NativeText other)
        {
            throw new NotImplementedException();
        }

        public int CompareTo(CalliString other)
        {
            throw new NotImplementedException();
        }

        public int CompareTo(FixedString32Bytes other)
        {
            throw new NotImplementedException();
        }

        public int CompareTo(FixedString64Bytes other)
        {
            throw new NotImplementedException();
        }

        public int CompareTo(FixedString128Bytes other)
        {
            throw new NotImplementedException();
        }

        public int CompareTo(FixedString512Bytes other)
        {
            throw new NotImplementedException();
        }

        public int CompareTo(FixedString4096Bytes other)
        {
            throw new NotImplementedException();
        }
    }
}

