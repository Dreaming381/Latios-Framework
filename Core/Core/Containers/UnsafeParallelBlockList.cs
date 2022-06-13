using System.Runtime.InteropServices;
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
    /// </summary>
    public unsafe struct UnsafeParallelBlockList : INativeDisposable
    {
        public readonly int  m_elementSize;
        private readonly int m_blockSize;
        private readonly int m_elementsPerBlock;
        private Allocator    m_allocator;

        [NativeDisableUnsafePtrRestriction] private PerThreadBlockList* m_perThreadBlockLists;

        /// <summary>
        /// Construct a new UnsafeParallelBlockList using a UnityEngine allocator
        /// </summary>
        /// <param name="elementSize">The size of each element in bytes that will be stored</param>
        /// <param name="elementsPerBlock">
        /// The number of elements stored per native thread index before needing to perform an additional allocation.
        /// Higher values may allocate more memory that is left unused. Lower values may perform more allocations.
        /// </param>
        /// <param name="allocator">The UnityEngine allocator to use for allocations</param>
        public UnsafeParallelBlockList(int elementSize, int elementsPerBlock, Allocator allocator)
        {
            m_elementSize      = elementSize;
            m_elementsPerBlock = elementsPerBlock;
            m_blockSize        = elementSize * elementsPerBlock;
            m_allocator        = allocator;

            m_perThreadBlockLists = (PerThreadBlockList*)UnsafeUtility.Malloc(64 * JobsUtility.MaxJobThreadCount, 64, allocator);
            for (int i = 0; i < JobsUtility.MaxJobThreadCount; i++)
            {
                m_perThreadBlockLists[i].lastByteAddressInBlock = null;
                m_perThreadBlockLists[i].nextWriteAddress       = null;
                m_perThreadBlockLists[i].nextWriteAddress++;
                m_perThreadBlockLists[i].elementCount = 0;
            }
        }

        //The thread index is passed in because otherwise job reflection can't inject it through a pointer.
        /// <summary>
        /// Write an element for a given thread index
        /// </summary>
        /// <typeparam name="T">It is assumed the size of T is the same as what was passed into elementSize during construction</typeparam>
        /// <param name="value">The value to write</param>
        /// <param name="threadIndex">The thread index to use when writing. This should come from [NativeSetThreadIndex].</param>
        public void Write<T>(T value, int threadIndex) where T : unmanaged
        {
            var blockList = m_perThreadBlockLists + threadIndex;
            if (blockList->nextWriteAddress > blockList->lastByteAddressInBlock)
            {
                if (blockList->elementCount == 0)
                {
                    blockList->blocks = new UnsafeList<BlockPtr>(8, m_allocator);
                }
                BlockPtr newBlockPtr = new BlockPtr
                {
                    ptr = (byte*)UnsafeUtility.Malloc(m_blockSize, UnsafeUtility.AlignOf<T>(), m_allocator)
                };
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
        /// <param name="threadIndex">The thread index to use when allocating. This should come from [NativeSetThreadIndex].</param>
        /// <returns>A pointer where an element can be copied to</returns>
        public void* Allocate(int threadIndex)
        {
            var blockList = m_perThreadBlockLists + threadIndex;
            if (blockList->nextWriteAddress > blockList->lastByteAddressInBlock)
            {
                if (blockList->elementCount == 0)
                {
                    blockList->blocks = new UnsafeList<BlockPtr>(8, m_allocator);
                }
                BlockPtr newBlockPtr = new BlockPtr
                {
                    ptr = (byte*)UnsafeUtility.Malloc(m_blockSize, UnsafeUtility.AlignOf<byte>(), m_allocator)
                };
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
        /// Count the number of elements. Do this once and cache the result.
        /// </summary>
        /// <returns>The number of elements stored</returns>
        public int Count()
        {
            int result = 0;
            for (int i = 0; i < JobsUtility.MaxJobThreadCount; i++)
            {
                result += m_perThreadBlockLists[i].elementCount;
            }
            return result;
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

            for (int threadBlockId = 0; threadBlockId < JobsUtility.MaxJobThreadCount; threadBlockId++)
            {
                var blockList = m_perThreadBlockLists + threadBlockId;
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
        [Unity.Burst.CompilerServices.IgnoreWarning(1371)]
        public void GetElementValues<T>(NativeArray<T> values) where T : struct
        {
            int dst = 0;

            for (int threadBlockId = 0; threadBlockId < JobsUtility.MaxJobThreadCount; threadBlockId++)
            {
                var blockList = m_perThreadBlockLists + threadBlockId;
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

        //This catches race conditions if I accidentally pass in 0 for thread index in the parallel writer because copy and paste.
        [BurstDiscard]
        void CheckBlockCountMatchesCount(int count, int blockCount)
        {
            int expectedBlocks = count / m_elementsPerBlock;
            if (count % m_elementsPerBlock > 0)
                expectedBlocks++;
            if (blockCount != expectedBlocks)
                throw new System.InvalidOperationException($"Block count: {blockCount} does not match element count: {count}");
        }

        [BurstCompile]
        struct DisposeJob : IJob
        {
            public UnsafeParallelBlockList upbl;

            public void Execute()
            {
                upbl.Dispose();
            }
        }

        /// <summary>
        /// Uses a job to dispose this container
        /// </summary>
        /// <param name="inputDeps">A JobHandle for all jobs which should finish before disposal.</param>
        /// <returns>A JobHandle for the disposal job.</returns>
        public JobHandle Dispose(JobHandle inputDeps)
        {
            var jh = new DisposeJob { upbl = this }.Schedule(inputDeps);
            m_perThreadBlockLists          = null;
            return jh;
        }

        /// <summary>
        /// Disposes the container immediately. It is legal to call this from within a job,
        /// as long as no other jobs or threads are using it.
        /// </summary>
        public void Dispose()
        {
            for (int i = 0; i < JobsUtility.MaxJobThreadCount; i++)
            {
                if (m_perThreadBlockLists[i].elementCount > 0)
                {
                    for (int j = 0; j < m_perThreadBlockLists[i].blocks.Length; j++)
                    {
                        UnsafeUtility.Free(m_perThreadBlockLists[i].blocks[j].ptr, m_allocator);
                    }
                    m_perThreadBlockLists[i].blocks.Dispose();
                }
            }
            UnsafeUtility.Free(m_perThreadBlockLists, m_allocator);
        }

        private struct BlockPtr
        {
            public byte* ptr;
        }

        [StructLayout(LayoutKind.Sequential, Size = 64)]
        private struct PerThreadBlockList
        {
            public UnsafeList<BlockPtr> blocks;
            public byte*                nextWriteAddress;
            public byte*                lastByteAddressInBlock;
            public int                  elementCount;
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
        public Enumerator GetEnumerator(int nativeThreadIndex)
        {
            return new Enumerator(m_perThreadBlockLists + nativeThreadIndex, m_elementSize, m_elementsPerBlock);
        }

        /// <summary>
        /// An enumerator which can be used for iterating over the elements written by a single thread index.
        /// It is allowed to have multiple enumerators for the same thread index.
        /// </summary>
        public struct Enumerator
        {
            private PerThreadBlockList* m_perThreadBlockList;
            private byte*               m_readAddress;
            private byte*               m_lastByteAddressInBlock;
            private int                 m_blockIndex;
            private int                 m_elementSize;
            private int                 m_elementsPerBlock;

            internal Enumerator(void* perThreadBlockList, int elementSize, int elementsPerBlock)
            {
                m_perThreadBlockList = (PerThreadBlockList*)perThreadBlockList;
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
                    if (m_blockIndex >= m_perThreadBlockList->blocks.Length)
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
            public T GetCurrent<T>() where T : struct
            {
                UnsafeUtility.CopyPtrToStructure(m_readAddress, out T t);
                return t;
            }
        }
    }

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

