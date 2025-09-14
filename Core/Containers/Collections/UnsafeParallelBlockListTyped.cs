using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Latios.Unsafe
{
    /// <summary>
    /// An unsafe container which can be written to by multiple threads and multiple jobs.
    /// The container is lock-free and items never change addresses once written.
    /// Items written in the same thread in the same job will be read back in the same order.
    /// Unlike Unity's Unsafe* containers, it is safe to copy this type by value.
    /// </summary>
    public unsafe struct UnsafeParallelBlockList<T> where T : unmanaged
    {
        private UnsafeParallelBlockList m_blockList;

        /// <summary>
        /// Construct a new UnsafeParallelBlockList using a UnityEngine allocator
        /// </summary>
        /// <param name="elementsPerBlock">
        /// The number of elements stored per native thread index before needing to perform an additional allocation.
        /// Higher values may allocate more memory that is left unused. Lower values may perform more allocations.
        /// </param>
        /// <param name="allocator">The allocator to use for allocations</param>
        public UnsafeParallelBlockList(int elementsPerBlock, AllocatorManager.AllocatorHandle allocator)
        {
            m_blockList = new UnsafeParallelBlockList(UnsafeUtility.SizeOf<T>(), elementsPerBlock, allocator);
        }

        /// <summary>
        /// Write an element for a given thread index
        /// </summary>
        /// <param name="value">The value to write</param>
        /// <param name="threadIndex">The thread index to use when writing. This should come from [NativeSetThreadIndex] or JobsUtility.ThreadIndex.</param>
        public void Write(in T value, int threadIndex)
        {
            m_blockList.Write(in value, threadIndex);
        }

        /// <summary>
        /// Reserve memory for an element and return the result by ref. The memory is NOT zero-initialized.
        /// </summary>
        /// <param name="threadIndex">The thread index to use when allocating. This should come from [NativeSetThreadIndex] or JobsUtility.ThreadIndex.</param>
        /// <returns>An uninitialized reference to the newly allocated data</returns>
        public ref T Allocate(int threadIndex)
        {
            return ref *(T*)m_blockList.Allocate(threadIndex);
        }

        /// <summary>
        /// Count the number of elements. Do this once and cache the result.
        /// </summary>
        /// <returns>The number of elements stored</returns>
        public int Count()
        {
            return m_blockList.Count();
        }

        /// <summary>
        /// Count the number of elements for a specific thread index
        /// </summary>
        /// <param name="threadIndex">The thread index to get the count of elements written</param>
        /// <returns>The number of elements written to the specifed thread index</returns>
        public int CountForThreadIndex(int threadIndex)
        {
            return m_blockList.CountForThreadIndex(threadIndex);
        }

        /// <summary>
        /// Returns true if the struct is not in a default uninitialized state.
        /// This may report true incorrectly if the memory where this instance
        /// exists was left uninitialized rather than cleared.
        /// </summary>
        public bool isCreated => m_blockList.isCreated;

        /// <summary>
        /// Copies all the elements stored into values.
        /// </summary>
        /// <param name="values">An array where the elements should be copied to. Its Length should be equal to Count().</param>
        public void GetElementValues(NativeArray<T> values)
        {
            m_blockList.GetElementValues(values);
        }

        /// <summary>
        /// Uses a job to dispose this container
        /// </summary>
        /// <param name="inputDeps">A JobHandle for all jobs which should finish before disposal.</param>
        /// <returns>A JobHandle for the disposal job.</returns>
        public JobHandle Dispose(JobHandle inputDeps)
        {
            return m_blockList.Dispose(inputDeps);
        }

        /// <summary>
        /// Disposes the container immediately. It is legal to call this from within a job,
        /// as long as no other jobs or threads are using it.
        /// </summary>
        public void Dispose()
        {
            m_blockList.Dispose();
        }
    }

    public unsafe struct UnsafeIndexedBlockList<T> : INativeDisposable where T : unmanaged
    {
        private UnsafeIndexedBlockList m_blockList;

        #region Base API
        /// <summary>
        /// Construct a new UnsafeIndexedBlockList using a UnityEngine allocator
        /// </summary>
        /// <param name="elementsPerBlock">
        /// The number of elements stored per native thread index before needing to perform an additional allocation.
        /// Higher values may allocate more memory that is left unused. Lower values may perform more allocations.
        /// </param>
        /// <param name="indexCount">The number of stream indicecs in the block list.</param>
        /// <param name="allocator">The allocator to use for allocations</param>
        public UnsafeIndexedBlockList(int elementsPerBlock, int indexCount, AllocatorManager.AllocatorHandle allocator)
        {
            m_blockList = new UnsafeIndexedBlockList(UnsafeUtility.SizeOf<T>(), elementsPerBlock, indexCount, allocator);
        }

        /// <summary>
        /// The number of index streams in this instance.
        /// </summary>
        public int indexCount => m_blockList.indexCount;

        /// <summary>
        /// Returns true if the struct is not in a default uninitialized state.
        /// This may report true incorrectly if the memory where this instance
        /// exists was left uninitialized rather than cleared.
        /// </summary>
        public bool isCreated => m_blockList.isCreated;

        /// <summary>
        /// Write an element for a given index
        /// </summary>
        /// <param name="value">The value to write</param>
        /// <param name="index">The index to use when writing.</param>
        public void Write(T value, int index) =>m_blockList.Write(value, index);

        /// <summary>
        /// Reserve memory for an element and return the fixed memory address.
        /// </summary>
        /// <param name="index">The index to use when allocating.</param>
        /// <returns>A pointer where an element can be copied to</returns>
        public ref T Allocate(int index)
        {
            return ref *(T*)m_blockList.Allocate(index);
        }

        /// <summary>
        /// Count the total number of elements across all indices. Do this once and cache the result.
        /// </summary>
        /// <returns>The number of elements stored</returns>
        public int Count() => m_blockList.Count();

        /// <summary>
        /// Count the number of elements for a specific index.
        /// </summary>
        /// <param name="index">The index at which to count the number of elements within</param>
        /// <returns>The number of elements added to the specified index</returns>
        public int CountForIndex(int index) => m_blockList.CountForIndex(index);

        /// <summary>
        /// Copies all the elements stored into values.
        /// </summary>
        /// <param name="values">An array where the elements should be copied to. Its Length should be equal to Count().</param>
        public void GetElementValues(NativeArray<T> values) => m_blockList.GetElementValues(values);

        /// <summary>
        /// Steals elements from the other UnsafeIndexedBlockList with the same block sizes and allocator and
        /// adds them to this instance at the same indices. Relative ordering of elements and memory addresses
        /// in the other UnsafeIndexedBlockList may not be preserved.
        /// </summary>
        /// <param name="other">The other UnsafeIndexedBlockList to steal from</param>
        public void ConcatenateAndStealFromUnordered(ref UnsafeIndexedBlockList<T> other) => m_blockList.ConcatenateAndStealFromUnordered(ref other.m_blockList);

        /// <summary>
        /// Moves elements from one index into another. Relative ordering of elements and memory addresses
        /// from the source index may not be preserved.
        /// </summary>
        /// <param name="sourceIndex">The index where elements should be moved away from</param>
        /// <param name="destinationIndex">The index where elements should be moved to</param>
        public void MoveIndexToOtherIndexUnordered(int sourceIndex, int destinationIndex) => m_blockList.MoveIndexToOtherIndexUnordered(sourceIndex, destinationIndex);

        /// <summary>
        /// Removes all elements from an index
        /// </summary>
        /// <param name="index"></param>
        public void ClearIndex(int index) => m_blockList.ClearIndex(index);

        /// <summary>
        /// Uses a job to dispose this container
        /// </summary>
        /// <param name="inputDeps">A JobHandle for all jobs which should finish before disposal.</param>
        /// <returns>A JobHandle for the disposal job.</returns>
        public JobHandle Dispose(JobHandle inputDeps) => m_blockList.Dispose(inputDeps);

        /// <summary>
        /// Disposes the container immediately. It is legal to call this from within a job,
        /// as long as no other jobs or threads are using it.
        /// </summary>
        public void Dispose() => m_blockList.Dispose();
        #endregion

        //#region Enumerator
        ///// <summary>
        ///// Gets an enumerator for one of the indices in the job.
        ///// </summary>
        ///// <param name="index">
        ///// The index that was used when the elements were written.
        ///// </param>
        //public Enumerator GetEnumerator(int index)
        //{
        //    return new Enumerator(m_perIndexBlockList + index, m_elementSize, m_elementsPerBlock);
        //}
        //
        ///// <summary>
        ///// An enumerator which can be used for iterating over the elements written by a single index.
        ///// It is allowed to have multiple enumerators for the same thread index.
        ///// </summary>
        //public struct Enumerator
        //{
        //    private PerIndexBlockList* m_perThreadBlockList;
        //    private byte* m_readAddress;
        //    private byte* m_lastByteAddressInBlock;
        //    private int m_blockIndex;
        //    private int m_elementSize;
        //    private int m_elementsPerBlock;
        //
        //    internal Enumerator(void* perThreadBlockList, int elementSize, int elementsPerBlock)
        //    {
        //        m_perThreadBlockList = (PerIndexBlockList*)perThreadBlockList;
        //        m_readAddress = null;
        //        m_readAddress++;
        //        m_lastByteAddressInBlock = null;
        //        m_blockIndex = -1;
        //        m_elementSize = elementSize;
        //        m_elementsPerBlock = elementsPerBlock;
        //    }
        //
        //    /// <summary>
        //    /// Advance to the next element
        //    /// </summary>
        //    /// <returns>Returns false if the previous element was the last, true otherwise</returns>
        //    public bool MoveNext()
        //    {
        //        m_readAddress += m_elementSize;
        //        if (m_readAddress > m_lastByteAddressInBlock)
        //        {
        //            m_blockIndex++;
        //            if (m_perThreadBlockList->elementCount == 0 || m_blockIndex >= m_perThreadBlockList->blocks.Length)
        //                return false;
        //
        //            int elementsInBlock = math.min(m_elementsPerBlock, m_perThreadBlockList->elementCount - m_blockIndex * m_elementsPerBlock);
        //            var blocks = m_perThreadBlockList->blocks.Ptr;
        //            m_lastByteAddressInBlock = blocks[m_blockIndex].ptr + elementsInBlock * m_elementSize - 1;
        //            m_readAddress = blocks[m_blockIndex].ptr;
        //        }
        //        return true;
        //    }
        //
        //    /// <summary>
        //    /// Retrieves the current element, copying it to a variable of the specified type.
        //    /// </summary>
        //    /// <typeparam name="T">It is assumed the size of T is the same as what was passed into elementSize during construction</typeparam>
        //    /// <returns>A value containing a copy of the element stored, reinterpreted with the strong type</returns>
        //    public T GetCurrent<T>() where T : unmanaged
        //    {
        //        UnsafeUtility.CopyPtrToStructure(m_readAddress, out T t);
        //        return t;
        //    }
        //
        //    /// <summary>
        //    /// Retrieves the current element by ref of the specified type.
        //    /// </summary>
        //    /// <typeparam name="T">It is assumed the size of T is the same as what was passed into elementSize during construction</typeparam>
        //    /// <returns>A ref of the element stored, reinterpreted with the strong type</returns>
        //    public ref T GetCurrentAsRef<T>() where T : unmanaged
        //    {
        //        return ref UnsafeUtility.AsRef<T>(m_readAddress);
        //    }
        //
        //    /// <summary>
        //    /// Returns the current element's raw address within the block list.
        //    /// </summary>
        //    public void* GetCurrentPtr() => m_readAddress;
        //
        //    internal Enumerator GetNextIndexEnumerator()
        //    {
        //        return new Enumerator(m_perThreadBlockList + 1, m_elementSize, m_elementsPerBlock);
        //    }
        //}
        //
        ///// <summary>
        ///// Gets an enumerator for all indices.
        ///// </summary>
        //public AllIndicesEnumerator GetEnumerator()
        //{
        //    return new AllIndicesEnumerator(new Enumerator(m_perIndexBlockList, m_elementSize, m_elementsPerBlock), m_indexCount);
        //}
        //
        ///// <summary>
        ///// An enumerator which can be used for iterating over the elements written by all indices.
        ///// </summary>
        //public struct AllIndicesEnumerator
        //{
        //    Enumerator m_enumerator;
        //    int m_index;
        //    int m_indexCount;
        //
        //    internal AllIndicesEnumerator(Enumerator thread0Enumerator, int indexCount)
        //    {
        //        m_enumerator = thread0Enumerator;
        //        m_index = 0;
        //        m_indexCount = indexCount;
        //    }
        //
        //    /// <summary>
        //    /// Advance to the next element
        //    /// </summary>
        //    /// <returns>Returns false if the previous element was the last, true otherwise</returns>
        //    public bool MoveNext()
        //    {
        //        while (!m_enumerator.MoveNext())
        //        {
        //            m_index++;
        //            if (m_index >= m_indexCount)
        //                return false;
        //            m_enumerator = m_enumerator.GetNextIndexEnumerator();
        //        }
        //        return true;
        //    }
        //
        //    /// <summary>
        //    /// Retrieves the current element, copying it to a variable of the specified type.
        //    /// </summary>
        //    /// <typeparam name="T">It is assumed the size of T is the same as what was passed into elementSize during construction</typeparam>
        //    /// <returns>A value containing a copy of the element stored, reinterpreted with the strong type</returns>
        //    public T GetCurrent<T>() where T : unmanaged => m_enumerator.GetCurrent<T>();
        //
        //    /// <summary>
        //    /// Retrieves the current element by ref of the specified type.
        //    /// </summary>
        //    /// <typeparam name="T">It is assumed the size of T is the same as what was passed into elementSize during construction</typeparam>
        //    /// <returns>A ref of the element stored, reinterpreted with the strong type</returns>
        //    public ref T GetCurrentAsRef<T>() where T : unmanaged => ref m_enumerator.GetCurrentAsRef<T>();
        //
        //    /// <summary>
        //    /// Returns the current element's raw address within the block list.
        //    /// </summary>
        //    public void* GetCurrentPtr() => m_enumerator.GetCurrentPtr();
        //
        //    // Schedule for 128 iterations
        //    //[BurstCompile]
        //    //struct ExampleReadJob : IJobFor
        //    //{
        //    //    [NativeDisableUnsafePtrRestriction] public UnsafeParallelBlockList blockList;
        //    //
        //    //    public void Execute(int index)
        //    //    {
        //    //        var enumerator = blockList.GetEnumerator(index);
        //    //
        //    //        while (enumerator.MoveNext())
        //    //        {
        //    //            int number = enumerator.GetCurrent<int>();
        //    //
        //    //            if (number == 381)
        //    //                UnityEngine.Debug.Log("You found me!");
        //    //            else if (number == 380)
        //    //                UnityEngine.Debug.Log("Where?");
        //    //        }
        //    //    }
        //    //}
        //}
        //#endregion

        #region Threading
        /// <summary>
        /// Uses atomics to lock an index for thread-safe access. If already locked by the same value, this method returns.
        /// </summary>
        /// <param name="index">The index to lock.</param>
        /// <param name="lockValue">A value to represent this context owns the lock. If the index has already been locked by this value, the method returns.</param>
        /// <param name="unlockValue">A value that specifies the index is not currently locked.</param>
        /// <returns>Returns true if the lock was acquired while the value was previously unlockValue. Returns false if the index was already locked by lockValue.</returns>
        public bool LockIndexReentrant(int index, int lockValue, int unlockValue = 0) => m_blockList.LockIndexReentrant(index, lockValue, unlockValue);

        /// <summary>
        /// Unlocks the index, resetting its internal value to the unlocked state.
        /// </summary>
        /// <param name="index">The index this context locked.</param>
        /// <param name="lockValue">The lock value that was stored.</param>
        /// <param name="unlockValue">The unlock value that should be left in the index.</param>
        public void UnlockIndex(int index, int lockValue, int unlockValue = 0) => m_blockList.UnlockIndex(index, lockValue, unlockValue);
        #endregion
    }
}

