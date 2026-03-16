using Unity.Collections;

namespace Latios.Calligraphics
{
    internal struct FixedStack512Bytes<T> where T : unmanaged
    {
        FixedList512Bytes<T> m_buffer;
        public bool IsEmpty => m_buffer.IsEmpty;
        public void Add(in T item) => m_buffer.Add(in item);
        public T Pop()
        {
            var result = m_buffer[^1];
            m_buffer.RemoveAt(m_buffer.Length - 1);
            return result;
        }
        public T Peek()
        {
            var result = m_buffer[^1];
            return result;
        }
        /// <summary> Function to pop stack, and return the new resulting last item. Will not pop root.</summary>
        public T RemoveExceptRoot()
        {
            if (m_buffer.Length > 1)
                Pop();
            return Peek();
        }
        public T this[int index] { get => m_buffer[index]; set => m_buffer[index] = value; }
        public void Clear() => m_buffer.Clear();
        public readonly int Length => m_buffer.Length;        
    }
}

