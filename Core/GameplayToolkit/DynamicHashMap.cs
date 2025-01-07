using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios
{
    /// <summary>
    /// A wrapper type around a DynamicBuffer which provides HashMap capabilities while maintaining full serialization support.
    /// </summary>
    /// <remarks>
    /// The general strategy of this map is that for a given power of two capacity, the first half is allocated for buckets,
    /// while the second half is an overflow region. There is always one "element" in the overflow region to ensure that if the
    /// buffer were to be trimmed, it could be recovered again. That element may or may not be occupied, and uses the bucket linked
    /// list index as the count of real elements in the hashmap.
    /// </remarks>
    /// <typeparam name="TKey"></typeparam>
    /// <typeparam name="TValue"></typeparam>
    public struct DynamicHashMap<TKey, TValue> : IEnumerable<(TKey, TValue)>
        where TKey : unmanaged, IEquatable<TKey> where TValue : unmanaged
    {
        #region Construction
        /// <summary>
        /// Construct a DynamicHashMap instance that uses the passed in DynamicBuffer as a backing storage
        /// </summary>
        /// <param name="buffer">The dynamic buffer that contains the underlying data for the DynamicHashMap</param>
        public DynamicHashMap(DynamicBuffer<Pair> buffer)
        {
            m_buffer   = buffer;
            m_count    = m_buffer.IsEmpty ? 0 : m_buffer[m_buffer.Length - 1].nextIndex;
            m_capacity = math.max(2, math.ceilpow2(m_buffer.Length));
            m_mask     = m_capacity / 2 - 1;
        }

        /// <summary>
        /// Implicitly converts the DynamicBuffer into the corresponding DynamicHashMap
        /// </summary>
        public static implicit operator DynamicHashMap<TKey, TValue>(DynamicBuffer<Pair> buffer)
        {
            return new DynamicHashMap<TKey, TValue>(buffer);
        }
        #endregion

        #region API
        /// <summary>
        /// True if this DynamicHashMap is backed by a valid DynamicBuffer. False otherwise.
        /// </summary>
        public bool isCreated => m_buffer.IsCreated;

        /// <summary>
        /// The number of key-value pairs in the hashmap.
        /// </summary>
        public int count => m_count;

        /// <summary>
        /// The current capacity of the hashmap.
        /// </summary>
        public int capacity => m_capacity;

        /// <summary>
        /// True if the hashmap is empty.
        /// </summary>
        public bool isEmpty => m_count == 0;

        /// <summary>
        /// Removes all elements from the hashmap.
        /// </summary>
        public void Clear() => m_buffer.Clear();

        /// <summary>
        /// Ensure that the hashmap can contain the number of elements requested,
        /// even if all elements computed the same hash code. If the requested
        /// capacity is greater than half the existing capacity, this method will
        /// reallocate.
        /// </summary>
        /// <param name="requiredCapacity">The number of elements that need to fit in this hashmap</param>
        public void EnsureCapacity(int requiredCapacity)
        {
            Tidy();
            if (requiredCapacity * 2 <= m_capacity)
                return;

            if (isEmpty)
            {
                m_capacity = math.max(2, math.ceilpow2(requiredCapacity));
                Tidy();
                return;
            }

            ReallocUp(math.max(2, math.ceilpow2(requiredCapacity)));
        }

        /// <summary>
        /// Adds a new key-value pair.
        /// </summary>
        /// <remarks>If the key is already present, this method returns false without modifying the hash map.</remarks>
        /// <param name="key">The key to add.</param>
        /// <param name="value">The value to add.</param>
        /// <returns>True if the key-value pair was added.</returns>
        public bool TryAdd(in TKey key, in TValue value) => TryAdd(in key, in value, false);

        /// <summary>
        /// Adds a new key-value pair if the key is not present, or replaces the value in the pair if the key is present.
        /// </summary>
        /// <param name="key">The key to add.</param>
        /// <param name="value">The value to add.</param>
        /// <returns>True if the key-value pair was added.</returns>
        public bool AddOrSet(in TKey key, in TValue value) => TryAdd(in key, in value, true);

        /// <summary>
        /// Retrieves the value associated with a key if the key is present.
        /// </summary>
        /// <param name="key">The key to look up.</param>
        /// <param name="item">Outputs the value associated with the key. Outputs default if the key was not present.</param>
        /// <returns>True if the key was present.</returns>
        public unsafe bool TryGetValue(in TKey key, out TValue value)
        {
            var result = Find(in key, (Pair*)m_buffer.GetUnsafeReadOnlyPtr());
            if (result == null)
            {
                value = default;
                return false;
            }
            value = *result;
            return true;
        }

        /// <summary>
        /// Returns true if a given key is present in this hash map.
        /// </summary>
        /// <param name="key">The key to look up.</param>
        /// <returns>True if the key was present.</returns>
        public unsafe bool ContainsKey(TKey key)
        {
            return Find(in key, (Pair*)m_buffer.GetUnsafeReadOnlyPtr()) != null;
        }

        /// <summary>
        /// Removes a key-value pair.
        /// </summary>
        /// <param name="key">The key to remove.</param>
        /// <returns>True if a key-value pair was removed.</returns>
        public bool Remove(in TKey key)
        {
            Tidy();
            if (isEmpty)
                return false;

            var     bucket    = GetBucket(in key);
            ref var candidate = ref m_buffer.ElementAt(bucket);
            if (candidate.isOccupied)
            {
                if (candidate.key.Equals(key))
                {
                    if (candidate.nextIndex != 0)
                    {
                        var     indexToBackfill = candidate.nextIndex;
                        ref var replacement     = ref m_buffer.ElementAt(indexToBackfill);
                        if (candidate.nextIndex >= m_buffer.Length - 1)
                            replacement.nextIndex = 0;
                        candidate                 = replacement;
                        Backfill(indexToBackfill);
                        DecrementCount();
                        return true;
                    }
                    candidate.key        = default;
                    candidate.value      = default;
                    candidate.isOccupied = false;
                    DecrementCount();
                    return true;
                }

                if (candidate.nextIndex == 0)
                    return false;

                for (int safetyBreakout = 0; safetyBreakout < m_buffer.Length; safetyBreakout++)
                {
                    ref var previousCandidate = ref candidate;
                    candidate                 = ref m_buffer.ElementAt(candidate.nextIndex);

                    if (candidate.isOccupied && candidate.key.Equals(key))
                    {
                        var indexToBackfill = previousCandidate.nextIndex;
                        if (previousCandidate.nextIndex >= m_buffer.Length - 1)
                            previousCandidate.nextIndex = 0;
                        else
                            previousCandidate.nextIndex = candidate.nextIndex;
                        Backfill(indexToBackfill);
                        DecrementCount();
                        return true;
                    }

                    if (candidate.nextIndex == 0 || previousCandidate.nextIndex >= m_buffer.Length - 1)
                    {
                        return false;
                    }
                }

                UnityEngine.Debug.LogError(
                    "DynamicHashMap is corrupted and has circular references. Either the buffer is being wrongly interpreted or this is a Latios Framework bug.");
                return false;
            }
            return false;
        }

        public Enumerator GetEnumerator() => new Enumerator {
            m_enumerator = m_buffer.GetEnumerator()
        };

        IEnumerator IEnumerable.GetEnumerator() {
            throw new NotImplementedException();
        }
        IEnumerator<(TKey, TValue)> IEnumerable<(TKey, TValue)>.GetEnumerator() {
            throw new NotImplementedException();
        }

        /// <summary>
        /// The type which should be the only field inside an IBufferElementData for the corresponding DynamicHashMap.
        /// </summary>
        public struct Pair
        {
            internal uint meta;
            [Unity.Properties.CreateProperty(ReadOnly = true)]
            internal TKey key;
            [Unity.Properties.CreateProperty(ReadOnly = true)]
            internal TValue value;

            [Unity.Properties.CreateProperty(ReadOnly = true)]
            internal bool isOccupied
            {
                get => (meta & 0x80000000) != 0;
                set => meta = (meta & 0x7fffffff) | math.select(0u, 1u, value) << 31;
            }

            internal int nextIndex
            {
                get => (int)(meta & 0x7fffffff);
                set => meta = (meta & 0x80000000) | (uint)value;
            }
        }

        /// <summary>
        /// An enumerator for the DynamicHashMap
        /// </summary>
        public unsafe struct Enumerator : IEnumerator<(TKey, TValue)>
        {
            internal NativeArray<Pair>.Enumerator m_enumerator;

            public (TKey, TValue)Current => (m_enumerator.Current.key, m_enumerator.Current.value);

            object IEnumerator.Current => (m_enumerator.Current.key, m_enumerator.Current.value);

            public void Dispose()
            {
                m_enumerator.Dispose();
            }

            public bool MoveNext()
            {
                while (m_enumerator.MoveNext())
                {
                    if (m_enumerator.Current.isOccupied)
                        return true;
                }
                return false;
            }

            public void Reset()
            {
                m_enumerator.Reset();
            }
        }
        #endregion

        #region Implementation
        DynamicBuffer<Pair> m_buffer;
        int                 m_count;
        int                 m_capacity;
        int                 m_mask;

        int GetBucket(in TKey key)
        {
            return key.GetHashCode() & m_mask;
        }

        void Tidy()
        {
            // We set Capacity directly because with EnsureCapacity Unity will try to at least double the capacity when really we just want
            // to untrim to the next power of two.
            m_buffer.Capacity = m_capacity;
            m_mask            = m_capacity / 2 - 1;
            if (m_buffer.Length == 0)
            {
                for (int i = 0; i <= m_capacity / 2; i++)
                    m_buffer.Add(default);
            }
        }

        void IncrementCount()
        {
            m_count++;
            m_buffer.ElementAt(m_buffer.Length - 1).nextIndex = m_count;
        }

        void DecrementCount()
        {
            m_count--;
            m_buffer.ElementAt(m_buffer.Length - 1).nextIndex = m_count;
        }

        void Backfill(int index)
        {
            if (index == m_buffer.Length - 1)
            {
                // Assume that we don't need to correct the thing pointing to this
                if (index > m_capacity / 2)
                {
                    m_buffer.Length--;
                    // The count should be updated after a backfill automatically
                }
                else
                {
                    // Preserve one element so that we can recover from a trim after serialization
                    m_buffer.ElementAt(index) = default;
                    // The count should be updated after a backfill automatically
                }
                return;
            }

            ref var elementToFill = ref m_buffer.ElementAt(index);
            ref var elementToMove = ref m_buffer.ElementAt(m_buffer.Length - 1);
            // Find the thing pointing to the element we are going to move, so that we can patch it.
            ref var candidate  = ref m_buffer.ElementAt(GetBucket(in elementToMove.key));
            bool    reinserted = false;
            for (int i = 0; i < m_buffer.Length; i++)
            {
                if (candidate.nextIndex == m_buffer.Length - 1)
                {
                    if (!reinserted)
                    {
                        candidate.nextIndex     = index;
                        elementToFill           = elementToMove;
                        elementToFill.nextIndex = 0;
                    }
                    else
                    {
                        candidate.nextIndex = 0;
                    }
                    Backfill(m_buffer.Length - 1);
                    return;
                }
                else if (candidate.nextIndex > index)
                {
                    var fillNext            = candidate.nextIndex;
                    candidate.nextIndex     = index;
                    elementToFill           = elementToMove;
                    elementToFill.nextIndex = fillNext;
                    reinserted              = true;
                }
                candidate = ref m_buffer.ElementAt(candidate.nextIndex);
            }

            UnityEngine.Debug.LogError(
                "DynamicHashMap is corrupted and has circular references. Either the buffer is being wrongly interpreted or this is a Latios Framework bug.");
        }

        unsafe void ReallocUp(int newCapacity)
        {
            // Resize and move old elements to second half of new allocation
            var oldOverflowCount = m_buffer.Length - m_capacity / 2;
            if (!m_buffer.ElementAt(m_buffer.Length - 1).isOccupied)
                oldOverflowCount--;
            var oldCapacity   = m_capacity;
            m_capacity        = newCapacity;
            m_mask            = m_capacity / 2 - 1;
            m_buffer.Capacity = m_capacity;
            m_buffer.ResizeUninitialized(m_capacity);
            var buffer = m_buffer.AsNativeArray();
            buffer.GetSubArray(0, oldCapacity).CopyTo(buffer.GetSubArray(newCapacity / 2, oldCapacity));
            buffer.GetSubArray(0, newCapacity / 2).AsSpan().Clear();
            var bufferPtr = (Pair*)buffer.GetUnsafePtr();

            // The first half of the old capacity were items that fell directly in the buckets.
            // Because our bucket assignment is a lower bit mask, all these items will also fall
            // directly into the buckets in the new bucket range. Either they will be in the same
            // bucket as before, or they will be in a bucket of newCapacity/4 higher.
            var oldBuckets = bufferPtr + m_capacity / 2;
            for (int i = 0; i < oldCapacity / 2; i++)
            {
                if (oldBuckets[i].isOccupied)
                {
                    var bucket                  = GetBucket(oldBuckets[i].key);
                    bufferPtr[bucket]           = oldBuckets[i];
                    bufferPtr[bucket].nextIndex = 0;
                    oldBuckets[i]               = default;
                }
            }

            // Now we do the overflow region of the old items. These will either continue to
            // go into the overflow region, or they may use new buckets.
            int overflowCount = 0;
            var oldOverflow   = oldBuckets + oldCapacity / 2;
            for (int i = 0; i < oldOverflowCount; i++)
            {
                var bucket = GetBucket(oldOverflow[i].key);
                if (bufferPtr[bucket].isOccupied)
                {
                    ref var tail = ref bufferPtr[bucket];
                    while (tail.nextIndex != 0)
                        tail                            = ref bufferPtr[tail.nextIndex];
                    tail.nextIndex                      = m_capacity / 2 + overflowCount;
                    oldBuckets[overflowCount]           = oldOverflow[i];
                    oldBuckets[overflowCount].nextIndex = 0;
                    overflowCount++;
                }
                else
                {
                    bufferPtr[bucket]           = oldOverflow[i];
                    bufferPtr[bucket].nextIndex = 0;
                }
            }

            // Clean up
            var lastIndex = m_capacity / 2 + overflowCount - 1;
            if (overflowCount == 0)
            {
                lastIndex++;
                bufferPtr[lastIndex] = default;
            }
            bufferPtr[lastIndex].nextIndex = m_count;
            m_buffer.Length                = lastIndex + 1;
        }

        bool TryAdd(in TKey key, in TValue value, bool replace)
        {
            Tidy();
            var     bucket    = GetBucket(in key);
            ref var candidate = ref m_buffer.ElementAt(bucket);
            if (candidate.isOccupied)
            {
                for (int safetyBreakout = 0; safetyBreakout < m_buffer.Length; safetyBreakout++)
                {
                    if (candidate.key.Equals(key))
                    {
                        if (replace)
                            candidate.value = value;
                        return false;
                    }

                    if (candidate.nextIndex == 0 || candidate.nextIndex >= m_buffer.Length - 1)
                    {
                        ref var last = ref m_buffer.ElementAt(m_buffer.Length - 1);
                        if (!last.isOccupied)
                        {
                            // Last is likely padding to ensure the capacity can be computed correctly.
                            last.isOccupied     = true;
                            last.key            = key;
                            last.value          = value;
                            candidate.nextIndex = m_buffer.Length - 1;
                            IncrementCount();
                            return true;
                        }
                        else if (candidate.nextIndex != 0 && last.key.Equals(key))
                        {
                            if (replace)
                                last.value = value;
                            return false;
                        }

                        if (m_buffer.Length == m_capacity)
                        {
                            ReallocUp(m_capacity * 2);

                            // Start over
                            return TryAdd(in key, in value);
                        }
                        last.nextIndex              = 0;
                        candidate.nextIndex         = m_buffer.Length;
                        m_buffer.Add(new Pair { key = key, value = value, meta = (uint)m_count | 0x80000000 });
                        IncrementCount();
                        return true;
                    }
                    candidate = ref m_buffer.ElementAt(candidate.nextIndex);
                }

                UnityEngine.Debug.LogError(
                    "DynamicHashMap is corrupted and has circular references. Either the buffer is being wrongly interpreted or this is a Latios Framework bug.");
                return false;
            }
            candidate.key        = key;
            candidate.value      = value;
            candidate.isOccupied = true;
            IncrementCount();
            return true;
        }

        unsafe TValue* Find(in TKey key, Pair* bufferPtr)
        {
            if (isEmpty)
                return null;

            var bucket    = GetBucket(in key);
            var candidate = bufferPtr + bucket;
            if (candidate->isOccupied)
            {
                if (candidate->key.Equals(key))
                    return &candidate->value;

                if (candidate->nextIndex == 0)
                    return null;

                for (int safetyBreakout = 0; safetyBreakout < m_buffer.Length; safetyBreakout++)
                {
                    var previousCandidate = candidate;
                    candidate             = bufferPtr + candidate->nextIndex;

                    if (candidate->isOccupied && candidate->key.Equals(key))
                        return &candidate->value;

                    if (candidate->nextIndex == 0 || previousCandidate->nextIndex >= m_buffer.Length - 1)
                        return null;
                }

                UnityEngine.Debug.LogError(
                    "DynamicHashMap is corrupted and has circular references. Either the buffer is being wrongly interpreted or this is a Latios Framework bug.");
                return null;
            }
            return null;
        }
        #endregion
    }
}

