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
            var result = m_buffer[m_buffer.Length - 1];
            m_buffer.RemoveAt(m_buffer.Length - 1);
            return result;
        }
        public T Peek()
        {
            var result = m_buffer[m_buffer.Length - 1];
            return result;
        }
        /// <summary> Function to pop stack, and return the new resulting last item. Will not pop root.</summary>
        public T RemoveExceptRoot()
        {
            if (m_buffer.Length > 1)
                Pop();
            return Peek();
        }
        public void Clear() => m_buffer.Clear();
    }
}

