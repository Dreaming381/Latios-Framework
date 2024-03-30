using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.Mathematics;

namespace Latios.Myri.DSP
{
    // Make public on release
    internal struct SampleQueue : IDisposable
    {
        NativeArray<float> m_buffer;
        int                m_nextEnqueueIndex;
        int                m_nextDequeueIndex;
        int                m_count;

        public SampleQueue(int capacity, AllocatorManager.AllocatorHandle allocator)
        {
            m_buffer           = CollectionHelper.CreateNativeArray<float>(capacity, allocator, NativeArrayOptions.UninitializedMemory);
            m_nextEnqueueIndex = 0;
            m_nextDequeueIndex = 0;
            m_count            = 0;
        }

        public void Dispose()
        {
            CollectionHelper.Dispose(m_buffer);
        }

        public int count => m_count;
        public int capacity => m_buffer.Length;
        public bool isCreated => m_buffer.IsCreated;

        public void Enqueue(float sample)
        {
            CheckEnqueue();
            m_buffer[m_nextEnqueueIndex] = sample;
            m_nextEnqueueIndex++;
            m_count++;
            m_nextEnqueueIndex = math.select(m_nextEnqueueIndex, 0, m_nextEnqueueIndex == m_buffer.Length);
        }

        public float Dequeue()
        {
            CheckDequeue();
            float result = m_buffer[m_nextDequeueIndex];
            m_nextDequeueIndex++;
            m_count--;
            m_nextDequeueIndex = math.select(m_nextDequeueIndex, 0, m_nextDequeueIndex == m_buffer.Length);
            return result;
        }

        public float this[int index]
        {
            get
            {
                CheckIndex(index);
                var slot = index + m_nextDequeueIndex;
                slot     = math.select(slot, slot - m_buffer.Length, slot >= m_buffer.Length);
                return m_buffer[slot];
            }
        }

        public void Clear()
        {
            m_nextEnqueueIndex = 0;
            m_nextDequeueIndex = 0;
            m_count            = 0;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckEnqueue()
        {
            if (m_count == m_buffer.Length)
                throw new System.InvalidOperationException("Failed to enqueue sample. The SampleQueue is full.");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckDequeue()
        {
            if (m_count == 0)
                throw new System.InvalidOperationException("Failed to dequeue sample. The SampleQueue is empty.");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckIndex(int index)
        {
            if (index < 0 || index >= m_count)
                throw new System.ArgumentOutOfRangeException("Failed to index sample. The sample was out of range.");
        }
    }
}

