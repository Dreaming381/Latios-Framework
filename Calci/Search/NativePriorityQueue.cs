using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Calci
{
    /// <summary>
    /// A quaternary priority queue where smaller values are prioritized for dequeuing
    /// </summary>
    /// <typeparam name="TElement">The type of element stored in the queue, that can compare itself against other elements</typeparam>
    public struct NativePriorityQueue<TElement> : INativeDisposable where TElement : unmanaged, IComparable<TElement>
    {
        NativePriorityQueue<TElement, NativeSortExtension.DefaultComparer<TElement> > m_impl;

        public bool isCreated => m_impl.isCreated;
        public bool isEmpty => m_impl.isEmpty;
        public int count => m_impl.count;
        public int capacity => m_impl.capacity;
        public NativeArray<TElement>.ReadOnly unorderedItems => m_impl.unorderedItems;

        public NativePriorityQueue(int initialCapacity, AllocatorManager.AllocatorHandle allocator)
        {
            m_impl = new NativePriorityQueue<TElement, NativeSortExtension.DefaultComparer<TElement> >(initialCapacity, allocator, default);
        }

        public void Dispose() => m_impl.Dispose();
        public JobHandle Dispose(JobHandle inputDeps) => m_impl.Dispose(inputDeps);
        public void EnsureCapacity(int capacity) => m_impl.EnsureCapacity(capacity);
        public void Clear() => m_impl.Clear();
        public void Enqueue(in TElement element) => m_impl.Enqueue(in element);
        public bool TryPeek(out TElement element) => m_impl.TryPeek(out element);
        public bool TryDequeue(out TElement element) => m_impl.TryDequeue(out element);
        public bool TryDequeueEnqueue(out TElement dequeued, in TElement toEnqueue) => m_impl.TryDequeueEnqueue(out dequeued, in toEnqueue);
        public TElement EnqueueDequeue(in TElement toEnqueue) => m_impl.EnqueueDequeue(in toEnqueue);
    }

    /// <summary>
    /// A quaternary priority queue where smaller values are prioritized for dequeuing
    /// </summary>
    /// <typeparam name="TElement">The type of element stored in the queue</typeparam>
    /// <typeparam name="TComparer">A struct which defines a method of comparison</typeparam>
    public struct NativePriorityQueue<TElement, TComparer> : INativeDisposable where TElement : unmanaged where TComparer : struct, IComparer<TElement>
    {
        NativeList<TElement> m_heap;
        TComparer            m_comparer;

        public bool isCreated => m_heap.IsCreated;
        public bool isEmpty => m_heap.IsEmpty;
        public int count => m_heap.Length;
        public int capacity
        {
            get => m_heap.Capacity;
            set => m_heap.Capacity = value;
        }
        public NativeArray<TElement>.ReadOnly unorderedItems => m_heap.AsReadOnly();

        public NativePriorityQueue(int initialCapacity, AllocatorManager.AllocatorHandle allocator, TComparer comparer)
        {
            m_heap     = new NativeList<TElement>(initialCapacity, allocator);
            m_comparer = comparer;
        }

        public void Dispose() => m_heap.Dispose();
        public JobHandle Dispose(JobHandle inputDeps) => m_heap.Dispose(inputDeps);

        public void EnsureCapacity(int capacity)
        {
            if (capacity > m_heap.Capacity)
                m_heap.Capacity = capacity;
        }
        public void Clear() => m_heap.Clear();
        public void Enqueue(in TElement element)
        {
            var index = m_heap.Length;
            m_heap.Add(element);
            Prioritize(index);
        }
        public bool TryPeek(out TElement element)
        {
            bool empty = isEmpty;
            element    = (empty ? default : m_heap[0]);
            return !empty;
        }
        public bool TryDequeue(out TElement element)
        {
            var result = TryPeek(out element);
            if (result)
            {
                m_heap[0] = m_heap[m_heap.Length - 1];
                m_heap.Length--;
                if (!isEmpty)
                    DeprioritizeRoot();
            }
            return result;
        }
        public bool TryDequeueEnqueue(out TElement dequeued, in TElement toEnqueue)
        {
            var result = TryPeek(out dequeued);
            if (result)
                m_heap[0] = toEnqueue;
            else
                m_heap.Add(in toEnqueue);
            DeprioritizeRoot();
            return result;
        }
        public TElement EnqueueDequeue(in TElement toEnqueue)
        {
            if (!TryPeek(out var dequeue) || m_comparer.Compare(toEnqueue, dequeue) < 0)
                return toEnqueue;
            m_heap[0] = toEnqueue;
            DeprioritizeRoot();
            return dequeue;
        }

        void Prioritize(int i)
        {
            var current = m_heap[i];
            while (i != 0)
            {
                var     parent      = ParentOf(i);
                ref var parentValue = ref m_heap.ElementAt(parent);
                if (m_comparer.Compare(current, parentValue) >= 0)
                    break;

                m_heap[i] = parentValue;
                i         = parent;
            }
            m_heap[i] = current;
        }

        void DeprioritizeRoot()
        {
            int i          = 0;
            int firstChild = FirstChildOf(i);
            var current    = m_heap[0];
            while (firstChild < m_heap.Length && TryGetMinChildIndex(firstChild, out var minIndex))
            {
                ref var child = ref m_heap.ElementAt(minIndex);
                if (m_comparer.Compare(current, child) >= 0)
                {
                    m_heap[i]  = child;
                    i          = minIndex;
                    firstChild = FirstChildOf(i);
                }
                else
                    break;
            }
            m_heap[i] = current;
        }

        bool TryGetMinChildIndex(int firstChildIndex, out int minChildIndex)
        {
            var childrenCount = math.min(4, m_heap.Length - firstChildIndex);
            switch (childrenCount)
            {
                case 0:
                    minChildIndex = 0;
                    return false;
                case 1:
                    minChildIndex = firstChildIndex;
                    return true;
                case 2:
                    minChildIndex = math.select(firstChildIndex, firstChildIndex + 1, m_comparer.Compare(m_heap[firstChildIndex], m_heap[firstChildIndex + 1]) > 0);
                    return true;
                case 3:
                    minChildIndex = math.select(firstChildIndex, firstChildIndex + 1, m_comparer.Compare(m_heap[firstChildIndex], m_heap[firstChildIndex + 1]) > 0);
                    minChildIndex = math.select(minChildIndex, firstChildIndex + 2, m_comparer.Compare(m_heap[minChildIndex], m_heap[firstChildIndex + 2]) > 0);
                    return true;
                case 4:
                    var leftIndex  = math.select(firstChildIndex, firstChildIndex + 1, m_comparer.Compare(m_heap[firstChildIndex], m_heap[firstChildIndex + 1]) > 0);
                    var rightIndex = math.select(firstChildIndex + 2, firstChildIndex + 3, m_comparer.Compare(m_heap[firstChildIndex + 2], m_heap[firstChildIndex + 3]) > 0);
                    minChildIndex  = math.select(leftIndex, rightIndex, m_comparer.Compare(m_heap[leftIndex], m_heap[rightIndex]) > 0);
                    return true;
                default:
                    minChildIndex = 0;
                    return false;
            }
        }

        static int ParentOf(int i) => (i - 1) / 4;
        static int FirstChildOf(int i) => 4 * i + 1;
    }
}

