using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios
{
    /// <summary>
    /// A header type of a DynamicMultiList<T>. Make this the sole field of an IBufferElementData.
    /// </summary>
    public struct MultiListHeader<T> where T : unmanaged
    {
        internal int startOffset;
        internal int length;  // -1 means the index is unallocated
        internal int capacity;
    }

    /// <summary>
    /// An element type of a DynamicMultiList<T>. Make this the sole field of an IBufferElementData
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public struct MultiListElement<T> where T : unmanaged
    {
        private T element;
    }

    /// <summary>
    /// A struct used to manage multiple lists packed within a pair of DynamicBuffers.
    /// Each list has an allocated index, which is persisted for the full lifetime of the list.
    /// Because of this, valid list indices may not be adjacent, and the DynamicMultiList
    /// does not know the number of of valid lists it contains (it must iterate and count).
    /// This data structure is best for when multiple things (such as Unika scripts) need to
    /// share a DynamicBuffer and the lists change their capacities infrequently.
    /// </summary>
    /// <typeparam name="T">The type of element contained within each list of the DynamicMultiList</typeparam>
    public struct DynamicMultiList<T> where T : unmanaged
    {
        DynamicBuffer<MultiListHeader<T> > m_headers;
        DynamicBuffer<T>                   m_elements;

        public DynamicMultiList(DynamicBuffer<MultiListHeader<T> > headerBuffer, DynamicBuffer<MultiListElement<T> > elementBuffer)
        {
            m_headers  = headerBuffer;
            m_elements = elementBuffer.Reinterpret<T>();
        }

        /// <summary>
        /// A list within a DynamicMultiList. The API should resemble typical resizeable list operations.
        /// </summary>
        public struct List : INativeList<T>
        {
            DynamicMultiList<T> m_multiList;
            int                 m_index;

            /// <summary>
            /// The index of this list within the DynamicMultiList. This index is stable for the lifetime of the List.
            /// </summary>
            public int indexInMultiList => m_index;

            /// <summary>
            /// Return a native array that aliases the original list contents.
            /// </summary>
            /// <remarks>You can only access the native array as long as any list within
            /// the DynamicMultiList did not change capacity. Several list operations,
            /// such as <see cref="Add"/> and <see cref="TrimExcess"/> can result in
            /// list capacity changes, as can adding or removing a list.</remarks>
            /// <returns>A NativeArray view of this list.</returns>
            public NativeArray<T> AsNativeArray()
            {
                var header = m_multiList.m_headers[m_index];
                return m_multiList.m_elements.AsNativeArray().GetSubArray(header.startOffset, header.length);
            }

            public NativeArray<T>.Enumerator GetEnumerator() => AsNativeArray().GetEnumerator();

            /// <summary>
            /// Array-like indexing operator.
            /// </summary>
            /// <param name="index">The zero-based index.</param>
            public T this[int index]
            {
                get => AsNativeArray()[index];
                set => AsNativeArray().AsSpan()[index] = value;
            }

            /// <summary>
            /// Gets the reference to the element at the given index.
            /// </summary>
            /// <param name="index">The zero-based index.</param>
            /// <returns>Returns the reference to the element at the index.</returns>
            public ref T ElementAt(int index) => ref AsNativeArray().AsSpan()[index];

            /// <summary>
            /// Reports whether container is empty.
            /// </summary>
            /// <value>True if this container empty.</value>
            public bool IsEmpty => Length == 0;

            /// <summary>
            /// The number of elements the list holds.
            /// </summary>
            public int Length
            {
                get => m_multiList.m_headers[m_index].length;
                set
                {
                    CheckWriteLength(value);
                    if (value > Capacity)
                        Capacity     = math.ceilpow2(value);
                    ref var header   = ref m_multiList.m_headers.ElementAt(m_index);
                    var     elements = m_multiList.m_elements.AsNativeArray();
                    for (int i = header.length; i < value; i++)
                        elements[header.startOffset + i] = default;
                    header.length                        = value;
                }
            }

            /// <summary>
            /// The number of elements the list can hold.
            /// </summary>
            /// <remarks>
            /// <paramref name="Capacity"/> can not be set lower than <see cref="Length"/> - this will raise an exception.
            /// No effort is made to avoid costly reallocations when <paramref name="Capacity"/> changes slightly;
            /// if <paramref name="Capacity"/> is incremented by 1, an array 1 element bigger is allocated.
            /// </remarks>
            public unsafe int Capacity
            {
                get => m_multiList.m_headers[m_index].capacity;
                set
                {
                    var originalHeader = m_multiList.m_headers[m_index];
                    if (value == originalHeader.capacity)
                        return;
                    CheckWriteCapacity(value, originalHeader.length);

                    // As of Entities 1.4.3, this line invokes CheckWriteAccessAndInvalidateArrayAliases()
                    // before checking if the capacity actually changed and earlying out. We use this to force
                    // the invalidation of subarrays.
                    m_multiList.m_elements.Capacity = m_multiList.m_elements.Capacity;

                    var oldCapacity = originalHeader.capacity;
                    var newCapacity = value;
                    var diff        = newCapacity - oldCapacity;
                    if (diff < 0)
                    {
                        var eraseStart = originalHeader.startOffset + value;
                        m_multiList.m_elements.RemoveRange(eraseStart, math.abs(diff));
                    }
                    else
                    {
                        var insertStart                = originalHeader.startOffset + oldCapacity;
                        var elementsToSlide            = m_multiList.m_elements.Length - insertStart;
                        m_multiList.m_elements.Length += diff;
                        var ptr                        = (T*)m_multiList.m_elements.GetUnsafePtr();
                        UnsafeUtility.MemMove(ptr + insertStart + diff, ptr + insertStart, elementsToSlide * (long)UnsafeUtility.SizeOf<T>());
                    }
                    var headers               = m_multiList.m_headers.AsNativeArray().AsSpan();
                    headers[m_index].capacity = newCapacity;
                    for (int i = m_index + 1; i < headers.Length; i++)
                        headers[i].startOffset += diff;
                }
            }

            /// <summary>
            /// Sets the list length to zero.
            /// </summary>
            /// <remarks>The capacity of the buffer remains unchanged. List memory
            /// is not overwritten.</remarks>
            public void Clear()
            {
                Length = 0;
            }

            /// <summary>
            /// Sets the length of this list, increasing the capacity if necessary.
            /// </summary>
            /// <remarks>If <paramref name="length"/> is less than the current
            /// length of the list, the length of the list is reduced while the
            /// capacity remains unchanged.</remarks>
            /// <param name="length">The new length of the list.</param>
            public void ResizeUninitialized(int newLength)
            {
                CheckWriteLength(newLength);
                if (newLength > Capacity)
                    Capacity                                    = math.ceilpow2(newLength);
                m_multiList.m_headers.ElementAt(m_index).length = newLength;
            }

            /// <summary>
            /// Adds an element to the end of the list, resizing as necessary.
            /// </summary>
            /// <remarks>The list is resized if it has no additional capacity.</remarks>
            /// <param name="elem">The element to add to the list.</param>
            /// <returns>The index of the added element, which is equal to the new length of the list minus one.</returns>
            public int Add(T elem)
            {
                int length = Length;
                ResizeUninitialized(length + 1);
                this[length] = elem;
                return length;
            }

            /// <summary>
            /// Inserts an element at the specified index, resizing as necessary.
            /// </summary>
            /// <remarks>The list is resized if it has no additional capacity.</remarks>
            /// <param name="index">The position at which to insert the new element.</param>
            /// <param name="elem">The element to add to the buffer.</param>
            public unsafe void Insert(int index, T elem)
            {
                int length = Length;
                CheckInsert(index, length);
                ResizeUninitialized(length + 1);
                var basePtr = (T*)AsNativeArray().GetUnsafePtr();
                UnsafeUtility.MemMove(basePtr + (index + 1), basePtr + index, (long)UnsafeUtility.SizeOf<T>() * (length - index));
                this[index] = elem;
            }

            /// <summary>
            /// Adds all the elements from <paramref name="newElems"/> to the end
            /// of the list, resizing as necessary.
            /// </summary>
            /// <remarks>The buffer is resized if it has no additional capacity.</remarks>
            /// <param name="newElems">The native array of elements to insert.</param>
            public unsafe void AddRange(NativeArray<T> newElems)
            {
                var oldHeader = m_multiList.m_headers[m_index];
                var basePtr   = (T*)m_multiList.m_elements.GetUnsafePtr();
                var arrayPtr  = (T*)newElems.GetUnsafeReadOnlyPtr();
                if (arrayPtr > basePtr && arrayPtr < basePtr + m_multiList.m_elements.Length)
                {
                    // Aliased insertion.
                    int  srcStart               = (int)(arrayPtr - basePtr);
                    int  srcCount               = newElems.Length;
                    bool offsetByCapacityChange = false;
                    if (arrayPtr + srcCount <= basePtr + oldHeader.startOffset + oldHeader.length)
                    {
                        // The array precedes the list's end. No indices will need to change.
                    }
                    else if (basePtr + oldHeader.startOffset + oldHeader.capacity <= arrayPtr)
                    {
                        offsetByCapacityChange = true;
                    }
                    else
                    {
                        ThrowStrangeAddRangeAlias(srcStart, srcCount, oldHeader.startOffset, oldHeader.length);
                    }
                    ResizeUninitialized(oldHeader.length + srcCount);
                    if (offsetByCapacityChange)
                        srcStart += Capacity - oldHeader.capacity;
                    UnsafeUtility.MemCpy(basePtr + oldHeader.startOffset + oldHeader.length, basePtr + srcStart, (long)UnsafeUtility.SizeOf<T>() * srcCount);
                    return;
                }
                ResizeUninitialized(oldHeader.length + newElems.Length);
                UnsafeUtility.MemCpy(basePtr + oldHeader.length, newElems.GetUnsafeReadOnlyPtr(), (long)UnsafeUtility.SizeOf<T>() * newElems.Length);
            }

            /// <summary>
            /// Removes the specified number of elements, starting with the element at the specified index.
            /// </summary>
            /// <remarks>The list capacity remains unchanged.</remarks>
            /// <param name="index">The first element to remove.</param>
            /// <param name="count">How many elements to remove.</param>
            public unsafe void RemoveRange(int index, int count)
            {
                CheckRemoveRange(index, count, Length);
                if (count == 0)
                    return;

                var basePtr = (T*)AsNativeArray().GetUnsafePtr();

                UnsafeUtility.MemMove(basePtr + index, basePtr + index + count, (long)UnsafeUtility.SizeOf<T>() * (Length - count - index));
                Length -= count;
            }

            /// <summary>
            /// Removes the specified number of elements, starting with the element at the specified index. It replaces the
            /// elements that were removed with a range of elements from the back of the buffer. This is more efficient
            /// than moving all elements following the removed elements, but does change the order of elements in the buffer.
            /// </summary>
            /// <remarks>The buffer capacity remains unchanged.</remarks>
            /// <param name="index">The first element to remove.</param>
            /// <param name="count">How many elements tot remove.</param>
            public unsafe void RemoveRangeSwapBack(int index, int count)
            {
                CheckRemoveRange(index, count, Length);
                if (count == 0)
                    return;

                var   l        = Length;
                var   basePtr  = (T*)AsNativeArray().GetUnsafePtr();
                int   copyFrom = math.max(l - count, index + count);
                void* dst      = basePtr + index;
                void* src      = basePtr + copyFrom;
                UnsafeUtility.MemMove(dst, src, (l - copyFrom) * (long)UnsafeUtility.SizeOf<T>());
                Length -= count;
            }

            /// <summary>
            /// Removes the element at the specified index.
            /// </summary>
            /// <param name="index">The index of the element to remove.</param>
            public void RemoveAt(int index)
            {
                RemoveRange(index, 1);
            }

            /// <summary>
            /// Removes the element at the specified index and swaps the last element into its place. This is more efficient
            /// than moving all elements following the removed element, but does change the order of elements in the buffer.
            /// </summary>
            /// <param name="index">The index of the element to remove.</param>
            public void RemoveAtSwapBack(int index)
            {
                var l = Length;
                CheckRemoveRange(index, 1, l);

                this[index] = this[l - 1];
                Length--;
            }

            /// <summary>
            /// Gets an <see langword="unsafe"/> read/write pointer to the contents of the buffer.
            /// </summary>
            /// <remarks>This function can only be called in unsafe code contexts.</remarks>
            /// <returns>A typed, unsafe pointer to the first element in the buffer.</returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public unsafe T* GetUnsafePtr()
            {
                return (T*)AsNativeArray().GetUnsafePtr();
            }

            /// <summary>
            /// Gets an <see langword="unsafe"/> read-only pointer to the contents of the buffer.
            /// </summary>
            /// <remarks>This function can only be called in unsafe code contexts.</remarks>
            /// <returns>A typed, unsafe pointer to the first element in the buffer.</returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public unsafe T* GetUnsafeReadOnlyPtr()
            {
                return (T*)AsNativeArray().GetUnsafeReadOnlyPtr();
            }

            internal List(DynamicMultiList<T> multilist, int index)
            {
                CheckListIndex(index, multilist.m_headers);
                m_multiList = multilist;
                m_index     = index;
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            static void CheckWriteLength(int newLength)
            {
                if (newLength < 0)
                    throw new System.ArgumentOutOfRangeException($"Length cannot be negative. Attempted to assign Length {newLength}.");
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            static void CheckWriteCapacity(int newCapacity, int length)
            {
                if (newCapacity < length)
                    throw new System.ArgumentOutOfRangeException($"Capacity cannot be less than the current Length of {length}. Attempted to assign Capacity {newCapacity}.");
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            static void CheckInsert(int index, int currentLength)
            {
                if (index < 0)
                    throw new System.ArgumentOutOfRangeException($"Cannot insert at the negative index {index}");
                if (index > currentLength)
                    throw new System.ArgumentOutOfRangeException($"Cannot insert at index {index} which is greater than the length {currentLength}");
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            static void CheckRemoveRange(int index, int count, int currentLength)
            {
                if (index < 0)
                    throw new System.ArgumentOutOfRangeException($"Cannot remove at the negative index {index}");
                if (index + count > currentLength)
                    throw new System.ArgumentOutOfRangeException(
                        $"Cannot remove range starting at index {index} with {count} elements, as this assumes a list of at least length {index + count} which is greater than the length {currentLength}");
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            static void ThrowStrangeAddRangeAlias(int srcStart, int srcCount, int listStart, int listCount)
            {
                throw new System.InvalidOperationException(
                    $"Attempting to call AddRange with a badly aliased array from the same DynamicMultiList. The array starts at index {srcStart} and has {srcCount} elements, while the destination List starts at {listStart} and has {listCount} elements.");
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            static void CheckListIndex(int listIndex, DynamicBuffer<MultiListHeader<T> > headers)
            {
                if (headers[listIndex].length < 0)
                    throw new System.ArgumentOutOfRangeException($"A list has not been created for the index {listIndex} or has already been destroyed.");
            }
        }

        /// <summary>
        /// Fetches the List at the specified index. Throws if the index does not refer to a valid list.
        /// </summary>
        public List this[int listIndex] => new List(this, listIndex);

        /// <summary>
        /// Creates a new List instance within the DynamicMultiList.
        /// </summary>
        /// <returns>The stable index of the new List. This index might be recycled from a previously destroyed List.</returns>
        public int CreateList()
        {
            var headers = m_headers.AsNativeArray().AsSpan();
            for (int i = 0; i < headers.Length; i++)
            {
                if (headers[i].length < 0)
                {
                    headers[i].length = 0;
                    return i;
                }
            }
            var nextStartOffset = 0;
            if (headers.Length > 0)
            {
                var previousHeader = headers[headers.Length - 1];
                nextStartOffset    = previousHeader.startOffset + previousHeader.capacity;
            }
            var result = m_headers.Length;
            m_headers.Add(new MultiListHeader<T>
            {
                length      = 0,
                capacity    = 0,
                startOffset = nextStartOffset
            });
            return result;
        }

        /// <summary>
        /// Destroys the list at the specified index. Throws if no list was created with that index.
        /// </summary>
        public void DestroyList(int listIndex)
        {
            var listToDestroy = this[listIndex];
            listToDestroy.Clear();
            listToDestroy.Capacity                = 0;
            m_headers.ElementAt(listIndex).length = -1;
        }

        /// <summary>
        /// Checks if a List is valid for the specified index
        /// </summary>
        public bool IsListCreatedForIndex(int index)
        {
            if (index < 0 || index >= m_headers.Length)
                return false;
            return m_headers[index].length >= 0;
        }

        public ListEnumerator GetEnumerator() => new ListEnumerator
        {
            index     = -1,
            multiList = this
        };

        public struct ListEnumerator
        {
            internal DynamicMultiList<T> multiList;
            internal int                 index;

            public List Current => multiList[index];
            public bool MoveNext()
            {
                var headers = multiList.m_headers.AsNativeArray();
                while (index + 1 < multiList.m_headers.Length)
                {
                    index++;
                    if (headers[index].length >= 0)
                        return true;
                }
                return false;
            }
        }
    }
}

