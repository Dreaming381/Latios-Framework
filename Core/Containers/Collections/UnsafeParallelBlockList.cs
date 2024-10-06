using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Latios.Unsafe
{
    /// <summary>
    /// An unsafe container which can be written to by multiple threads and multiple jobs.
    /// The container is lock-free and items never change addresses once written.
    /// Items written in the same thread in the same job will be read back in the same order.
    /// This container is type-erased, but all elements are expected to be of the same size.
    /// Unlike Unity's Unsafe* containers, it is safe to copy this type by value.
    /// Alignment is guaranteed to be the greatest common factor of the element size and 16.
    /// </summary>
    public unsafe struct UnsafeParallelBlockList : INativeDisposable
    {
        private UnsafeIndexedBlockList m_blockList;

        /// <summary>
        /// Construct a new UnsafeParallelBlockList using a UnityEngine allocator
        /// </summary>
        /// <param name="elementSize">The size of each element in bytes that will be stored</param>
        /// <param name="elementsPerBlock">
        /// The number of elements stored per native thread index before needing to perform an additional allocation.
        /// Higher values may allocate more memory that is left unused. Lower values may perform more allocations.
        /// </param>
        /// <param name="allocator">The allocator to use for allocations</param>
        public UnsafeParallelBlockList(int elementSize, int elementsPerBlock, AllocatorManager.AllocatorHandle allocator)
        {
            m_blockList = new UnsafeIndexedBlockList(elementSize, elementsPerBlock, JobsUtility.MaxJobThreadCount, allocator);
        }

        /// <summary>
        /// The size of each element as defined when this instance was constructed.
        /// </summary>
        public int elementSize => m_blockList.elementSize;

        //The thread index is passed in because otherwise job reflection can't inject it through a pointer.
        /// <summary>
        /// Write an element for a given thread index
        /// </summary>
        /// <typeparam name="T">It is assumed the size of T is the same as what was passed into elementSize during construction</typeparam>
        /// <param name="value">The value to write</param>
        /// <param name="threadIndex">The thread index to use when writing. This should come from [NativeSetThreadIndex] or JobsUtility.ThreadIndex.</param>
        public void Write<T>(T value, int threadIndex) where T : unmanaged
        {
            m_blockList.Write(value, threadIndex);
        }

        /// <summary>
        /// Reserve memory for an element and return the fixed memory address.
        /// </summary>
        /// <param name="threadIndex">The thread index to use when allocating. This should come from [NativeSetThreadIndex] or JobsUtility.ThreadIndex.</param>
        /// <returns>A pointer where an element can be copied to</returns>
        public void* Allocate(int threadIndex)
        {
            return m_blockList.Allocate(threadIndex);
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
        /// Returns true if the struct is not in a default uninitialized state.
        /// This may report true incorrectly if the memory where this instance
        /// exists was left uninitialized rather than cleared.
        /// </summary>
        public bool isCreated => m_blockList.isCreated;

        /// <summary>
        /// Gets all the pointers for all elements stored.
        /// This does not actually traverse the memory but instead calculates memory addresses from metadata,
        /// which is often faster, especially for large elements.
        /// </summary>
        /// <param name="ptrs">An array in which the pointers should be stored. Its Length should be equal to Count().</param>
        public void GetElementPtrs(NativeArray<UnsafeIndexedBlockList.ElementPtr> ptrs)
        {
            m_blockList.GetElementPtrs(ptrs);
        }

        /// <summary>
        /// Copies all the elements stored into values.
        /// </summary>
        /// <typeparam name="T">It is assumed the size of T is the same as what was passed into elementSize during construction</typeparam>
        /// <param name="values">An array where the elements should be copied to. Its Length should be equal to Count().</param>
        public void GetElementValues<T>(NativeArray<T> values) where T : struct
        {
            m_blockList.GetElementValues(values);
        }

        /// <summary>
        /// Copies all the elements from the blocklists into the contiguous memory region beginning at ptr.
        /// </summary>
        /// <param name="dstPtr">The first address of a contiguous memory region large enough to store all values in the blocklists</param>
        public void CopyElementsRaw(void* dstPtr)
        {
            m_blockList.CopyElementsRaw(dstPtr);
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

        /// <summary>
        /// Gets an enumerator for one of the thread indices in the job.
        /// </summary>
        /// <param name="nativeThreadIndex">
        /// The thread index that was used when the elements were written.
        /// This does not have to be the thread index of the reader.
        /// In fact, you usually want to iterate through all threads.
        /// </param>
        /// <returns></returns>
        public UnsafeIndexedBlockList.Enumerator GetEnumerator(int nativeThreadIndex)
        {
            return m_blockList.GetEnumerator(nativeThreadIndex);
        }

        /// <summary>
        /// Gets an enumerator for all thread indices
        /// </summary>
        public UnsafeIndexedBlockList.AllIndicesEnumerator GetEnumerator()
        {
            return m_blockList.GetEnumerator();
        }
    }

    public unsafe struct UnsafeIndexedBlockList : INativeDisposable
    {
        [NativeDisableUnsafePtrRestriction] private PerIndexBlockList* m_perIndexBlockList;

        private readonly int                     m_elementSize;
        private readonly int                     m_blockSize;
        private readonly int                     m_elementsPerBlock;
        private readonly int                     m_indexCount;
        private AllocatorManager.AllocatorHandle m_allocator;

        #region Base API
        /// <summary>
        /// Construct a new UnsafeIndexedBlockList using a UnityEngine allocator
        /// </summary>
        /// <param name="elementSize">The size of each element in bytes that will be stored</param>
        /// <param name="elementsPerBlock">
        /// The number of elements stored per native thread index before needing to perform an additional allocation.
        /// Higher values may allocate more memory that is left unused. Lower values may perform more allocations.
        /// </param>
        /// <param name="indexCount">The number of stream indicecs in the block list.</param>
        /// <param name="allocator">The allocator to use for allocations</param>
        public UnsafeIndexedBlockList(int elementSize, int elementsPerBlock, int indexCount, AllocatorManager.AllocatorHandle allocator)
        {
            m_elementSize      = elementSize;
            m_elementsPerBlock = elementsPerBlock;
            m_blockSize        = elementSize * elementsPerBlock;
            m_indexCount       = indexCount;
            m_allocator        = allocator;

            m_perIndexBlockList = AllocatorManager.Allocate<PerIndexBlockList>(allocator, m_indexCount);
            for (int i = 0; i < m_indexCount; i++)
            {
                m_perIndexBlockList[i].blocks                 = default;
                m_perIndexBlockList[i].lastByteAddressInBlock = null;
                m_perIndexBlockList[i].nextWriteAddress       = null;
                m_perIndexBlockList[i].nextWriteAddress++;
                m_perIndexBlockList[i].elementCount = 0;
                m_perIndexBlockList[i].atomic       = 0;
            }
        }

        /// <summary>
        /// The size of each element as defined when this instance was constructed.
        /// </summary>
        public int elementSize => m_elementSize;

        /// <summary>
        /// The number of index streams in this instance.
        /// </summary>
        public int indexCount => m_indexCount;

        /// <summary>
        /// Returns true if the struct is not in a default uninitialized state.
        /// This may report true incorrectly if the memory where this instance
        /// exists was left uninitialized rather than cleared.
        /// </summary>
        public bool isCreated => m_perIndexBlockList != null;

        /// <summary>
        /// Write an element for a given index
        /// </summary>
        /// <typeparam name="T">It is assumed the size of T is the same as what was passed into elementSize during construction</typeparam>
        /// <param name="value">The value to write</param>
        /// <param name="index">The index to use when writing.</param>
        public void Write<T>(T value, int index) where T : unmanaged
        {
            var blockList = m_perIndexBlockList + index;
            if (blockList->nextWriteAddress > blockList->lastByteAddressInBlock)
            {
                if (!blockList->blocks.IsCreated)
                {
                    blockList->blocks = new UnsafeList<BlockPtr>(8, m_allocator);
                }
                BlockPtr newBlockPtr = new BlockPtr
                {
                    ptr = AllocatorManager.Allocate<byte>(m_allocator, m_blockSize)
                };
                UnityEngine.Debug.Assert(CollectionHelper.IsAligned(newBlockPtr.ptr, 16));
                blockList->nextWriteAddress       = newBlockPtr.ptr;
                blockList->lastByteAddressInBlock = newBlockPtr.ptr + m_blockSize - 1;
                blockList->blocks.Add(newBlockPtr);
            }

            UnsafeUtility.CopyStructureToPtr(ref value, blockList->nextWriteAddress);
            blockList->nextWriteAddress += m_elementSize;
            blockList->elementCount++;
        }

        /// <summary>
        /// Reserve memory for an element and return the fixed memory address.
        /// </summary>
        /// <param name="index">The index to use when allocating.</param>
        /// <returns>A pointer where an element can be copied to</returns>
        public void* Allocate(int index)
        {
            var blockList = m_perIndexBlockList + index;
            if (blockList->nextWriteAddress > blockList->lastByteAddressInBlock)
            {
                if (!blockList->blocks.IsCreated)
                {
                    blockList->blocks = new UnsafeList<BlockPtr>(8, m_allocator);
                }
                BlockPtr newBlockPtr = new BlockPtr
                {
                    ptr = AllocatorManager.Allocate<byte>(m_allocator, m_blockSize),
                };
                UnityEngine.Debug.Assert(CollectionHelper.IsAligned(newBlockPtr.ptr, 16));
                blockList->nextWriteAddress       = newBlockPtr.ptr;
                blockList->lastByteAddressInBlock = newBlockPtr.ptr + m_blockSize - 1;
                blockList->blocks.Add(newBlockPtr);
            }

            var result                   = blockList->nextWriteAddress;
            blockList->nextWriteAddress += m_elementSize;
            blockList->elementCount++;
            return result;
        }

        /// <summary>
        /// Count the total number of elements across all indices. Do this once and cache the result.
        /// </summary>
        /// <returns>The number of elements stored</returns>
        public int Count()
        {
            int result = 0;
            for (int i = 0; i < m_indexCount; i++)
            {
                result += m_perIndexBlockList[i].elementCount;
            }
            return result;
        }

        /// <summary>
        /// Count the number of elements for a specific index.
        /// </summary>
        /// <param name="index">The index at which to count the number of elements within</param>
        /// <returns>The number of elements added to the specified index</returns>
        public int CountForIndex(int index)
        {
            return m_perIndexBlockList[index].elementCount;
        }

        /// <summary>
        /// A pointer to an element stored
        /// </summary>
        public struct ElementPtr
        {
            public byte* ptr;
        }

        /// <summary>
        /// Gets all the pointers for all elements stored.
        /// This does not actually traverse the memory but instead calculates memory addresses from metadata,
        /// which is often faster, especially for large elements.
        /// </summary>
        /// <param name="ptrs">An array in which the pointers should be stored. Its Length should be equal to Count().</param>
        public void GetElementPtrs(NativeArray<ElementPtr> ptrs)
        {
            int dst = 0;

            for (int threadBlockId = 0; threadBlockId < m_indexCount; threadBlockId++)
            {
                var blockList = m_perIndexBlockList + threadBlockId;
                if (blockList->elementCount > 0)
                {
                    int src = 0;
                    for (int blockId = 0; blockId < blockList->blocks.Length - 1; blockId++)
                    {
                        var address = blockList->blocks[blockId].ptr;
                        for (int i = 0; i < m_elementsPerBlock; i++)
                        {
                            ptrs[dst] = new ElementPtr { ptr  = address };
                            address                          += m_elementSize;
                            src++;
                            dst++;
                        }
                    }
                    {
                        var address = blockList->blocks[blockList->blocks.Length - 1].ptr;
                        for (int i = src; i < blockList->elementCount; i++)
                        {
                            ptrs[dst] = new ElementPtr { ptr  = address };
                            address                          += m_elementSize;
                            dst++;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Copies all the elements stored into values.
        /// </summary>
        /// <typeparam name="T">It is assumed the size of T is the same as what was passed into elementSize during construction</typeparam>
        /// <param name="values">An array where the elements should be copied to. Its Length should be equal to Count().</param>
        public void GetElementValues<T>(NativeArray<T> values) where T : struct
        {
            int dst = 0;

            for (int threadBlockId = 0; threadBlockId < m_indexCount; threadBlockId++)
            {
                var blockList = m_perIndexBlockList + threadBlockId;
                if (blockList->elementCount > 0)
                {
                    int src = 0;
                    CheckBlockCountMatchesCount(blockList->elementCount, blockList->blocks.Length);
                    for (int blockId = 0; blockId < blockList->blocks.Length - 1; blockId++)
                    {
                        var address = blockList->blocks[blockId].ptr;
                        for (int i = 0; i < m_elementsPerBlock; i++)
                        {
                            UnsafeUtility.CopyPtrToStructure(address, out T temp);
                            values[dst]  = temp;
                            address     += m_elementSize;
                            src++;
                            dst++;
                        }
                    }
                    {
                        var address = blockList->blocks[blockList->blocks.Length - 1].ptr;
                        for (int i = src; i < blockList->elementCount; i++)
                        {
                            UnsafeUtility.CopyPtrToStructure(address, out T temp);
                            values[dst]  = temp;
                            address     += m_elementSize;
                            dst++;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Copies all the elements from the blocklists into the contiguous memory region beginning at ptr.
        /// </summary>
        /// <param name="dstPtr">The first address of a contiguous memory region large enough to store all values in the blocklists</param>
        public void CopyElementsRaw(void* dstPtr)
        {
            byte* dst = (byte*)dstPtr;

            for (int threadBlockId = 0; threadBlockId < m_indexCount; threadBlockId++)
            {
                var blockList = m_perIndexBlockList + threadBlockId;
                if (blockList->elementCount > 0)
                {
                    int src = 0;
                    CheckBlockCountMatchesCount(blockList->elementCount, blockList->blocks.Length);
                    for (int blockId = 0; blockId < blockList->blocks.Length - 1; blockId++)
                    {
                        var address = blockList->blocks[blockId].ptr;
                        UnsafeUtility.MemCpy(dst, address, m_blockSize);
                        dst += m_blockSize;
                        src += m_elementsPerBlock;
                    }
                    {
                        var address = blockList->blocks[blockList->blocks.Length - 1].ptr;
                        if (src < blockList->elementCount)
                            UnsafeUtility.MemCpy(dst, address, (blockList->elementCount - src) * m_elementSize);
                    }
                }
            }
        }

        /// <summary>
        /// Steals elements from the other UnsafeIndexedBlockList with the same block sizes and allocator and
        /// adds them to this instance at the same indices. Relative ordering of elements and memory addresses
        /// in the other UnsafeIndexedBlockList may not be preserved.
        /// </summary>
        /// <param name="other">The other UnsafeIndexedBlockList to steal from</param>
        public void ConcatenateAndStealFromUnordered(ref UnsafeIndexedBlockList other)
        {
            CheckBlockListsMatch(ref other);
            for (int threadBlockId = 0; threadBlockId < m_indexCount; threadBlockId++)
            {
                var blockList      = m_perIndexBlockList + threadBlockId;
                var otherBlockList = other.m_perIndexBlockList + threadBlockId;

                ConcatenateBlockList(blockList, otherBlockList, m_allocator);
            }
        }

        /// <summary>
        /// Moves elements from one index into another. Relative ordering of elements and memory addresses
        /// from the source index may not be preserved.
        /// </summary>
        /// <param name="sourceIndex">The index where elements should be moved away from</param>
        /// <param name="destinationIndex">The index where elements should be moved to</param>
        public void MoveIndexToOtherIndexUnordered(int sourceIndex, int destinationIndex)
        {
            ConcatenateBlockList(m_perIndexBlockList + destinationIndex, m_perIndexBlockList + sourceIndex, m_allocator);
        }

        /// <summary>
        /// Removes all elements from an index
        /// </summary>
        /// <param name="index"></param>
        public void ClearIndex(int index)
        {
            var blockList = m_perIndexBlockList + index;
            if (blockList->blocks.IsCreated)
            {
                foreach (var block in blockList->blocks)
                {
                    AllocatorManager.Free(m_allocator, block.ptr, m_blockSize);
                }

                blockList->blocks.Clear();
                blockList->elementCount           = 0;
                blockList->lastByteAddressInBlock = null;
                blockList->nextWriteAddress       = null;
                blockList->nextWriteAddress++;
            }
        }

        /// <summary>
        /// Uses a job to dispose this container
        /// </summary>
        /// <param name="inputDeps">A JobHandle for all jobs which should finish before disposal.</param>
        /// <returns>A JobHandle for the disposal job.</returns>
        public JobHandle Dispose(JobHandle inputDeps)
        {
            var jh = new DisposeJob { uibl = this }.Schedule(inputDeps);
            m_perIndexBlockList            = null;
            return jh;
        }

        /// <summary>
        /// Disposes the container immediately. It is legal to call this from within a job,
        /// as long as no other jobs or threads are using it.
        /// </summary>
        public void Dispose()
        {
            for (int i = 0; i < m_indexCount; i++)
            {
                if (m_perIndexBlockList[i].elementCount > 0)
                {
                    for (int j = 0; j < m_perIndexBlockList[i].blocks.Length; j++)
                    {
                        var block = m_perIndexBlockList[i].blocks[j];
                        AllocatorManager.Free(m_allocator, block.ptr, m_blockSize);
                    }
                    m_perIndexBlockList[i].blocks.Dispose();
                }
            }
            AllocatorManager.Free(m_allocator, m_perIndexBlockList, m_indexCount);
        }
        #endregion

        #region Enumerator
        /// <summary>
        /// Gets an enumerator for one of the indices in the job.
        /// </summary>
        /// <param name="index">
        /// The index that was used when the elements were written.
        /// </param>
        public Enumerator GetEnumerator(int index)
        {
            return new Enumerator(m_perIndexBlockList + index, m_elementSize, m_elementsPerBlock);
        }

        /// <summary>
        /// An enumerator which can be used for iterating over the elements written by a single index.
        /// It is allowed to have multiple enumerators for the same thread index.
        /// </summary>
        public struct Enumerator
        {
            private PerIndexBlockList* m_perThreadBlockList;
            private byte*              m_readAddress;
            private byte*              m_lastByteAddressInBlock;
            private int                m_blockIndex;
            private int                m_elementSize;
            private int                m_elementsPerBlock;

            internal Enumerator(void* perThreadBlockList, int elementSize, int elementsPerBlock)
            {
                m_perThreadBlockList = (PerIndexBlockList*)perThreadBlockList;
                m_readAddress        = null;
                m_readAddress++;
                m_lastByteAddressInBlock = null;
                m_blockIndex             = -1;
                m_elementSize            = elementSize;
                m_elementsPerBlock       = elementsPerBlock;
            }

            /// <summary>
            /// Advance to the next element
            /// </summary>
            /// <returns>Returns false if the previous element was the last, true otherwise</returns>
            public bool MoveNext()
            {
                m_readAddress += m_elementSize;
                if (m_readAddress > m_lastByteAddressInBlock)
                {
                    m_blockIndex++;
                    if (m_perThreadBlockList->elementCount == 0 || m_blockIndex >= m_perThreadBlockList->blocks.Length)
                        return false;

                    int elementsInBlock      = math.min(m_elementsPerBlock, m_perThreadBlockList->elementCount - m_blockIndex * m_elementsPerBlock);
                    var blocks               = m_perThreadBlockList->blocks.Ptr;
                    m_lastByteAddressInBlock = blocks[m_blockIndex].ptr + elementsInBlock * m_elementSize - 1;
                    m_readAddress            = blocks[m_blockIndex].ptr;
                }
                return true;
            }

            /// <summary>
            /// Retrieves the current element, copying it to a variable of the specified type.
            /// </summary>
            /// <typeparam name="T">It is assumed the size of T is the same as what was passed into elementSize during construction</typeparam>
            /// <returns>A value containing a copy of the element stored, reinterpreted with the strong type</returns>
            public T GetCurrent<T>() where T : unmanaged
            {
                UnsafeUtility.CopyPtrToStructure(m_readAddress, out T t);
                return t;
            }

            /// <summary>
            /// Retrieves the current element by ref of the specified type.
            /// </summary>
            /// <typeparam name="T">It is assumed the size of T is the same as what was passed into elementSize during construction</typeparam>
            /// <returns>A ref of the element stored, reinterpreted with the strong type</returns>
            public ref T GetCurrentAsRef<T>() where T : unmanaged
            {
                return ref UnsafeUtility.AsRef<T>(m_readAddress);
            }

            internal Enumerator GetNextIndexEnumerator()
            {
                return new Enumerator(m_perThreadBlockList + 1, m_elementSize, m_elementsPerBlock);
            }
        }

        /// <summary>
        /// Gets an enumerator for all indices.
        /// </summary>
        public AllIndicesEnumerator GetEnumerator()
        {
            return new AllIndicesEnumerator(new Enumerator(m_perIndexBlockList, m_elementSize, m_elementsPerBlock), m_indexCount);
        }

        /// <summary>
        /// An enumerator which can be used for iterating over the elements written by all indices.
        /// </summary>
        public struct AllIndicesEnumerator
        {
            Enumerator m_enumerator;
            int        m_index;
            int        m_indexCount;

            internal AllIndicesEnumerator(Enumerator thread0Enumerator, int indexCount)
            {
                m_enumerator = thread0Enumerator;
                m_index      = 0;
                m_indexCount = indexCount;
            }

            /// <summary>
            /// Advance to the next element
            /// </summary>
            /// <returns>Returns false if the previous element was the last, true otherwise</returns>
            public bool MoveNext()
            {
                while (!m_enumerator.MoveNext())
                {
                    m_index++;
                    if (m_index >= m_indexCount)
                        return false;
                    m_enumerator = m_enumerator.GetNextIndexEnumerator();
                }
                return true;
            }

            /// <summary>
            /// Retrieves the current element, copying it to a variable of the specified type.
            /// </summary>
            /// <typeparam name="T">It is assumed the size of T is the same as what was passed into elementSize during construction</typeparam>
            /// <returns>A value containing a copy of the element stored, reinterpreted with the strong type</returns>
            public T GetCurrent<T>() where T : unmanaged => m_enumerator.GetCurrent<T>();

            /// <summary>
            /// Retrieves the current element by ref of the specified type.
            /// </summary>
            /// <typeparam name="T">It is assumed the size of T is the same as what was passed into elementSize during construction</typeparam>
            /// <returns>A ref of the element stored, reinterpreted with the strong type</returns>
            public ref T GetCurrentAsRef<T>() where T : unmanaged => ref m_enumerator.GetCurrentAsRef<T>();

            // Schedule for 128 iterations
            //[BurstCompile]
            //struct ExampleReadJob : IJobFor
            //{
            //    [NativeDisableUnsafePtrRestriction] public UnsafeParallelBlockList blockList;
            //
            //    public void Execute(int index)
            //    {
            //        var enumerator = blockList.GetEnumerator(index);
            //
            //        while (enumerator.MoveNext())
            //        {
            //            int number = enumerator.GetCurrent<int>();
            //
            //            if (number == 381)
            //                UnityEngine.Debug.Log("You found me!");
            //            else if (number == 380)
            //                UnityEngine.Debug.Log("Where?");
            //        }
            //    }
            //}
        }
        #endregion

        #region Threading
        /// <summary>
        /// Uses atomics to lock an index for thread-safe access. If already locked by the same value, this method returns.
        /// </summary>
        /// <param name="index">The index to lock.</param>
        /// <param name="lockValue">A value to represent this context owns the lock. If the index has already been locked by this value, the method returns.</param>
        /// <param name="unlockValue">A value that specifies the index is not currently locked.</param>
        /// <returns>Returns true if the lock was acquired while the value was previously unlockValue. Returns false if the index was already locked by lockValue.</returns>
        public bool LockIndexReentrant(int index, int lockValue, int unlockValue = 0)
        {
            ref var blockList = ref m_perIndexBlockList[index];
            int     current;
            do
            {
                current = Interlocked.CompareExchange(ref blockList.atomic, lockValue, unlockValue);
            }
            while (current != lockValue && current != unlockValue);
            return current == unlockValue;
        }

        /// <summary>
        /// Unlocks the index, resetting its internal value to the unlocked state.
        /// </summary>
        /// <param name="index">The index this context locked.</param>
        /// <param name="lockValue">The lock value that was stored.</param>
        /// <param name="unlockValue">The unlock value that should be left in the index.</param>
        public void UnlockIndex(int index, int lockValue, int unlockValue = 0)
        {
            ref var blockList = ref m_perIndexBlockList[index];
            Interlocked.CompareExchange(ref blockList.atomic, unlockValue, lockValue);
        }
        #endregion

        #region Impl
        void ConcatenateBlockList(PerIndexBlockList* blockList, PerIndexBlockList* otherBlockList, AllocatorManager.AllocatorHandle otherAllocator)
        {
            if (otherBlockList->elementCount == 0)
                return;

            if (blockList->elementCount == 0)
            {
                (*blockList, *otherBlockList) = (*otherBlockList, *blockList);
                return;
            }

            var elementsInLastBlock        = blockList->elementCount % m_elementsPerBlock;
            var elementsStillNeededInBlock = math.select(m_elementsPerBlock - elementsInLastBlock, 0, elementsInLastBlock == 0);
            var elementsInOtherLastBlock   = otherBlockList->elementCount % m_elementsPerBlock;
            elementsInOtherLastBlock       = math.select(elementsInOtherLastBlock, m_elementsPerBlock, elementsInOtherLastBlock == 0);
            if (elementsInOtherLastBlock <= elementsStillNeededInBlock)
            {
                var otherBlock    = otherBlockList->blocks[otherBlockList->blocks.Length - 1];
                var src           = otherBlock.ptr;
                var blockToAppend = blockList->blocks[blockList->blocks.Length - 1];
                var dst           = blockToAppend.ptr + elementsInLastBlock * m_elementSize;
                UnsafeUtility.MemCpy(dst, src, elementsInOtherLastBlock * m_elementSize);
                AllocatorManager.Free(otherAllocator, otherBlock.ptr, m_blockSize);
                elementsInLastBlock        += elementsInOtherLastBlock;
                elementsStillNeededInBlock -= elementsInOtherLastBlock;
                otherBlockList->blocks.Length--;
                blockList->nextWriteAddress += elementsInOtherLastBlock * m_elementSize;
                elementsInOtherLastBlock     = math.select(m_elementsPerBlock, 0, otherBlockList->blocks.Length == 0);
            }
            if (elementsInOtherLastBlock > elementsStillNeededInBlock)
            {
                var indexToStealFrom = elementsInOtherLastBlock - elementsStillNeededInBlock;
                var otherBlock       = otherBlockList->blocks[otherBlockList->blocks.Length - 1];
                var src              = otherBlock.ptr + indexToStealFrom * m_elementSize;
                var blockToAppend    = blockList->blocks[blockList->blocks.Length - 1];
                var dst              = blockToAppend.ptr + elementsInLastBlock * m_elementSize;
                if (elementsStillNeededInBlock > 0)
                    UnsafeUtility.MemCpy(dst, src, elementsStillNeededInBlock * m_elementSize);
                otherBlockList->nextWriteAddress       = src;
                otherBlockList->lastByteAddressInBlock = otherBlock.ptr + m_blockSize - 1;
            }
            if (otherBlockList->blocks.Length > 0)
            {
                blockList->blocks.AddRange(otherBlockList->blocks);
                blockList->nextWriteAddress       = otherBlockList->nextWriteAddress;
                blockList->lastByteAddressInBlock = otherBlockList->lastByteAddressInBlock;
            }
            blockList->elementCount += otherBlockList->elementCount;

            otherBlockList->blocks.Clear();
            otherBlockList->elementCount           = 0;
            otherBlockList->lastByteAddressInBlock = null;
            otherBlockList->nextWriteAddress       = null;
            otherBlockList->nextWriteAddress++;
        }

        //This catches race conditions if I accidentally pass in 0 for thread index in the parallel writer because copy and paste.
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckBlockCountMatchesCount(int count, int blockCount)
        {
            int expectedBlocks = count / m_elementsPerBlock;
            if (count % m_elementsPerBlock > 0)
                expectedBlocks++;
            if (blockCount != expectedBlocks)
                throw new System.InvalidOperationException($"Block count: {blockCount} does not match element count: {count}");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckBlockListsMatch(ref UnsafeIndexedBlockList other)
        {
            if (m_blockSize != other.m_blockSize || m_elementSize != other.m_elementSize || m_indexCount != other.m_indexCount || m_allocator != other.m_allocator)
                throw new System.InvalidOperationException("UnsafeIndexedBlockLists do not match.");
        }

        [BurstCompile]
        struct DisposeJob : IJob
        {
            public UnsafeIndexedBlockList uibl;

            public void Execute()
            {
                uibl.Dispose();
            }
        }

        private struct BlockPtr
        {
            public byte* ptr;
        }

        [StructLayout(LayoutKind.Sequential, Size = 64)]
        private struct PerIndexBlockList
        {
            public UnsafeList<BlockPtr> blocks;
            public byte*                nextWriteAddress;
            public byte*                lastByteAddressInBlock;
            public int                  elementCount;
            public int                  atomic;
        }
        #endregion
    }
}

