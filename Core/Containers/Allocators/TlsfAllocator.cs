using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using AOT;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities.Exposed;
using Unity.Mathematics;

namespace Latios.Unsafe
{
    /// <summary>
    /// A two-level segregated fit allocator which allocates with cache line granularity.
    /// This is NOT a thread-safe allocator.
    /// </summary>
    [BurstCompile]
    public unsafe struct TlsfAllocator : AllocatorManager.IAllocator
    {
        #region State
        [StructLayout(LayoutKind.Sequential, Size = 64)]
        struct AllocationHeader
        {
            public uint4                            footprint;
            public ulong                            byteCount;  // Does not include header
            public AllocationHeader*                previousFreeHeader;
            public AllocationHeader*                nextFreeHeader;
            public AllocationHeader*                headerBeforeInPool;
            public AllocationHeader*                headerAfterInPool;
            public AllocatorManager.AllocatorHandle allocator;
            public bool                             free;
        }

        struct Pool
        {
            public byte* ptr;
            public long  byteCount;
            public int   elementSize;
            public int   numElements;
            public int   alignment;
        }

        UnsafeList<Pool> m_pools;
        // Size 2048
        AllocationHeader** m_freeBlocks;
        // Size 32
        BitField64*                      m_secondLevelFreeBits;
        BitField32                       m_firstLevelFreeBits;
        AllocatorManager.AllocatorHandle m_thisAllocator;
        AllocatorManager.AllocatorHandle m_backingAllocator;
        long                             m_standardPoolSize;
        ulong                            m_bytesTotal;
        ulong                            m_bytesUsed;
        bool                             m_warnIfPoolIsAllocatedDuringAllocationRequest;

        static readonly uint4 kFootprint = new uint4('L', 'a', 't', 'i');
        #endregion

        #region Lifecycle
        public TlsfAllocator(AllocatorManager.AllocatorHandle parentAllocator, long poolSize, bool warnIfPoolIsAllocatedDuringallocationRequest)
        {
            CheckMultipleOf64((ulong)poolSize);

            m_pools                                        = new UnsafeList<Pool>(8, parentAllocator);
            m_freeBlocks                                   = (AllocationHeader**)AllocatorManager.Allocate<IntPtr>(parentAllocator, 2048);
            m_secondLevelFreeBits                          = AllocatorManager.Allocate<BitField64>(parentAllocator, 32);
            m_firstLevelFreeBits                           = default;
            m_backingAllocator                             = parentAllocator;
            m_thisAllocator                                = default;
            m_standardPoolSize                             = poolSize;
            m_bytesTotal                                   = 0;
            m_bytesUsed                                    = 0;
            m_warnIfPoolIsAllocatedDuringAllocationRequest = warnIfPoolIsAllocatedDuringallocationRequest;

            UnsafeUtility.MemClear(m_freeBlocks,          UnsafeUtility.SizeOf<IntPtr>() * 2048);
            UnsafeUtility.MemClear(m_secondLevelFreeBits, UnsafeUtility.SizeOf<BitField64>() * 32);
        }

        public void Dispose()
        {
            foreach (var pool in m_pools)
            {
                AllocatorManager.Free(m_backingAllocator, pool.ptr, pool.elementSize, pool.alignment, pool.numElements);
            }
            m_pools.Dispose();
            AllocatorManager.Free(m_backingAllocator, (IntPtr*)m_freeBlocks, 2048);
            AllocatorManager.Free(m_backingAllocator, m_secondLevelFreeBits, 32);
        }

        public void AllocatePool(long minimumSize)
        {
            CheckMultipleOf64((ulong)minimumSize);
            var poolSize    = math.max(minimumSize, m_standardPoolSize);
            var elementSize = (int)poolSize;
            var numElements = 1;
            if (poolSize > (1 << 31))
            {
                poolSize    = CollectionHelper.Align(poolSize, 1 << 31);
                elementSize = (1 << 31);
                numElements = (int)(poolSize / (1 << 31));
            }
            var pool = new Pool
            {
                ptr         = (byte*)AllocatorManager.Allocate(m_backingAllocator, elementSize, 64, numElements),
                byteCount   = poolSize,
                alignment   = 64,
                elementSize = elementSize,
                numElements = numElements
            };
            m_pools.Add(pool);

            var header = new AllocationHeader
            {
                allocator          = m_thisAllocator,
                byteCount          = (ulong)poolSize - 64,
                footprint          = kFootprint,
                free               = true,
                headerAfterInPool  = null,
                headerBeforeInPool = null,
                nextFreeHeader     = null,
                previousFreeHeader = null,
            };
            UnsafeUtility.CopyStructureToPtr(ref header, pool.ptr);
            AddBlockToFreeStore((AllocationHeader*)pool.ptr);
            m_bytesTotal += (ulong)poolSize;
        }

        public void AddMemoryToPool(byte* ptr, int elementSize, int numElements, AllocatorManager.AllocatorHandle backingAllocator)
        {
            var poolSize = elementSize * (long)numElements;
            var pool     = new Pool
            {
                ptr         = ptr,
                byteCount   = poolSize,
                alignment   = 64,
                elementSize = elementSize,
                numElements = numElements
            };
            m_pools.Add(pool);

            var header = new AllocationHeader
            {
                allocator          = m_thisAllocator,
                byteCount          = (ulong)poolSize - 64,
                footprint          = kFootprint,
                free               = true,
                headerAfterInPool  = null,
                headerBeforeInPool = null,
                nextFreeHeader     = null,
                previousFreeHeader = null,
            };
            UnsafeUtility.CopyStructureToPtr(ref header, pool.ptr);
            AddBlockToFreeStore((AllocationHeader*)pool.ptr);
            m_bytesTotal += (ulong)poolSize;
        }

        public void GetStats(out ulong bytesUsed, out ulong bytesTotal)
        {
            bytesUsed  = m_bytesUsed;
            bytesTotal = m_bytesTotal;
        }
        #endregion

        #region IAllocator
        public AllocatorManager.TryFunction Function => Try;

        public AllocatorManager.AllocatorHandle Handle
        {
            get => m_thisAllocator;
            set => m_thisAllocator = value;
        }

        public Allocator ToAllocator => m_thisAllocator.ToAllocator;

        public bool IsCustomAllocator => m_thisAllocator.IsCustomAllocator;

        public int Try(ref AllocatorManager.Block block)
        {
            if (block.Range.Pointer == IntPtr.Zero)
            {
                CheckAlignmentIs64(block.Alignment);
                block.Range.Pointer = (IntPtr)Allocate(block.Bytes);
                return 0;
            }

            if (block.Range.Items == 0)
            {
                Free((void*)block.Range.Pointer);
                m_thisAllocator.RemoveSafetyHandles();  // Bug workaround to prevent AtomicSafetyHandle memory leak.
                return 0;
            }

            return -1;
        }

        [BurstCompile]
        [MonoPInvokeCallback(typeof(AllocatorManager.TryFunction))]
        static int Try(IntPtr state, ref AllocatorManager.Block block)
        {
            unsafe { return ((TlsfAllocator*)state)->Try(ref block); }
        }
        #endregion

        #region Impl
        void AddBlockToFreeStore(AllocationHeader* header)
        {
            header->free               = true;
            header->nextFreeHeader     = null;
            header->previousFreeHeader = null;

            var cacheLineCount      = header->byteCount / 64;
            var firstLevelIndex     = Log2Ceil(cacheLineCount / 64);
            var firstLevelInterval  = 1 << math.max(firstLevelIndex - 1, 0) + 6;
            var secondLevelInterval = firstLevelIndex / 64;
            var secondLevelIndex    = (int)(((long)cacheLineCount - firstLevelInterval) / secondLevelInterval);

            var freeBlockPointersIndex = firstLevelIndex * 64 + secondLevelIndex;
            var previousPtr            = m_freeBlocks[freeBlockPointersIndex];
            header->nextFreeHeader     = previousPtr;
            if (previousPtr != null)
            {
                previousPtr->previousFreeHeader = header;
            }
            m_freeBlocks[freeBlockPointersIndex] = header;

            m_secondLevelFreeBits[firstLevelIndex].SetBits(secondLevelIndex, true);
            m_firstLevelFreeBits.SetBits(firstLevelIndex, true);
        }

        void ClearFreeBlocksForByteCount(ulong byteCount)
        {
            var cacheLineCount      = byteCount / 64;
            var firstLevelIndex     = Log2Ceil(cacheLineCount / 64);
            var firstLevelInterval  = 1 << math.max(firstLevelIndex - 1, 0) + 6;
            var secondLevelInterval = firstLevelIndex / 64;
            var secondLevelIndex    = (int)(((long)cacheLineCount - firstLevelInterval) / secondLevelInterval);
            m_secondLevelFreeBits[firstLevelIndex].SetBits(secondLevelIndex, false);
            if (m_secondLevelFreeBits[firstLevelIndex].Value == 0)
                m_firstLevelFreeBits.SetBits(firstLevelIndex, false);
        }

        void* Allocate(long size)
        {
            var requiredSize       = (ulong)CollectionHelper.Align(size, 64);
            var requiredCacheLines = requiredSize / 64;
            var firstLevelIndex    = Log2Ceil(requiredCacheLines / 64);
            var firstLevelBits     = m_firstLevelFreeBits;
            if (firstLevelIndex > 0)
                firstLevelBits.SetBits(0, false, firstLevelIndex);
            if (firstLevelBits.Value == 0)
            {
                if (m_warnIfPoolIsAllocatedDuringAllocationRequest)
                {
                    UnityEngine.Debug.LogWarning(
                        $"The TLSF allocator does not have enough free pool memory to allocate {size} bytes. Allocating a new pool for this. This operation may block the currently executing thread for a significant time.");
                }
                AllocatePool((long)requiredSize);
                return Allocate(size);
            }

            firstLevelIndex         = firstLevelBits.CountTrailingZeros();
            var firstLevelInterval  = 1 << math.max(firstLevelIndex - 1, 0) + 6;
            var secondLevelInterval = firstLevelIndex / 64;
            var secondLevelIndex    = math.max(0, (int)(((long)requiredCacheLines - firstLevelInterval) / secondLevelInterval));
            var secondLevelBits     = m_secondLevelFreeBits[firstLevelIndex];
            if (secondLevelIndex > 0)
                secondLevelBits.SetBits(0, false, secondLevelIndex);
            if (secondLevelBits.Value == 0)
            {
                // Try the first level higher
                firstLevelIndex++;
                firstLevelBits.SetBits(0, false, firstLevelIndex);
                if (firstLevelBits.Value == 0)
                {
                    if (m_warnIfPoolIsAllocatedDuringAllocationRequest)
                    {
                        UnityEngine.Debug.LogWarning(
                            $"The TLSF allocator does not have enough free pool memory to allocate {size} bytes. Allocating a new pool for this. This operation may block the currently executing thread for a significant time.");
                    }
                    AllocatePool((long)requiredSize);
                    return Allocate(size);
                }
                secondLevelBits = m_secondLevelFreeBits[firstLevelIndex];
            }

            secondLevelIndex           = secondLevelBits.CountTrailingZeros();
            var freeBlockPointersIndex = firstLevelIndex * 64 + secondLevelIndex;
            var headerTaken            = m_freeBlocks[freeBlockPointersIndex];
            if (headerTaken->nextFreeHeader != null)
            {
                headerTaken->nextFreeHeader->previousFreeHeader = null;
                m_freeBlocks[freeBlockPointersIndex]            = headerTaken->nextFreeHeader;
            }
            else
            {
                m_secondLevelFreeBits[firstLevelIndex].SetBits(secondLevelIndex, false);
                if (m_secondLevelFreeBits[firstLevelIndex].Value == 0)
                    m_firstLevelFreeBits.SetBits(firstLevelIndex, false);
            }

            headerTaken->free               = false;
            headerTaken->nextFreeHeader     = null;
            headerTaken->previousFreeHeader = null;
            var cacheLinesInBlock           = headerTaken->byteCount / 64;
            if (cacheLinesInBlock == requiredCacheLines)
            {
                // Perfect allocation.
                m_bytesUsed += (requiredCacheLines + 1) * 64;
                return headerTaken + 1;
            }

            var cacheLinesLeftover = cacheLinesInBlock - requiredCacheLines;
            var headerLeftover     = headerTaken + requiredCacheLines + 1;
            *headerLeftover        = new AllocationHeader
            {
                allocator          = m_thisAllocator,
                byteCount          = (cacheLinesLeftover - 1) * 64,
                footprint          = kFootprint,
                free               = true,
                headerAfterInPool  = headerTaken->headerAfterInPool,
                headerBeforeInPool = headerTaken,
                nextFreeHeader     = null,
                previousFreeHeader = null,
            };
            if (headerTaken->headerAfterInPool != null)
                headerTaken->headerAfterInPool->headerBeforeInPool = headerLeftover;

            AddBlockToFreeStore(headerLeftover);
            headerTaken->byteCount  = requiredCacheLines * 64;
            m_bytesUsed            += (requiredCacheLines + 1) * 64;
            return headerTaken + 1;
        }

        void Free(void* ptr)
        {
            CheckDeallocation(ptr);
            var header = (AllocationHeader*)ptr;
            header--;
            var byteCount = header->byteCount + 64;
            if (header->headerAfterInPool != null && header->headerAfterInPool->free)
            {
                // Merge header after with this header.
                // Remove the header after from its free list.
                var headerAfter = header->headerAfterInPool;
                if (headerAfter->nextFreeHeader != null)
                    headerAfter->nextFreeHeader->previousFreeHeader = headerAfter->previousFreeHeader;
                if (headerAfter->previousFreeHeader != null)
                    headerAfter->previousFreeHeader->nextFreeHeader = headerAfter->nextFreeHeader;
                else
                    ClearFreeBlocksForByteCount(headerAfter->byteCount);

                // Point the next header in this block to the current header
                if (headerAfter->headerAfterInPool != null)
                {
                    headerAfter->headerAfterInPool->headerBeforeInPool = header;
                    header->headerAfterInPool                          = headerAfter->headerAfterInPool;
                }
                // Collapse the header after
                header->byteCount      += headerAfter->byteCount + 64;
                headerAfter->footprint  = default;
            }

            if (header->headerBeforeInPool != null && header->headerBeforeInPool->free)
            {
                // Merge header before with this header.
                // Remove the header before from its free list.
                var headerBefore = header->headerBeforeInPool;
                if (headerBefore->nextFreeHeader != null)
                    headerBefore->nextFreeHeader->previousFreeHeader = headerBefore->previousFreeHeader;
                if (headerBefore->previousFreeHeader != null)
                    headerBefore->previousFreeHeader->nextFreeHeader = headerBefore->nextFreeHeader;
                else
                    ClearFreeBlocksForByteCount(headerBefore->byteCount);

                // Patch the header after
                headerBefore->headerAfterInPool = header->headerAfterInPool;
                if (headerBefore->headerAfterInPool != null)
                    headerBefore->headerAfterInPool->headerAfterInPool = headerBefore;

                // Collapse the current header into the header before
                headerBefore->byteCount += header->byteCount + 64;
                header->footprint        = default;
                header                   = headerBefore;
            }

            AddBlockToFreeStore(header);
            m_bytesUsed -= byteCount;
        }

        static int Log2Floor(ulong a)
        {
            return 63 - math.lzcnt(a);
        }

        static int Log2Ceil(ulong a)
        {
            return 64 - math.lzcnt(a);
        }
        #endregion

        #region Safety
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void CheckMultipleOf64(ulong size)
        {
            if (!CollectionHelper.IsAligned(size, 64))
                throw new InvalidOperationException($"The memory size {size} is not a multiple of 64. This TLSF allocator does not support that.");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckNewPoolIsSafe(byte* ptr, int elementSize, int numElements, AllocatorManager.AllocatorHandle backingAllocator)
        {
            if (!CollectionHelper.IsAligned(ptr, 64))
                throw new InvalidOperationException($"The pool pointer {(ulong)ptr:x} is not a multiple of 64. This TLSF allocator does not support that.");
            CheckMultipleOf64((ulong)elementSize * (ulong)numElements);
            if (backingAllocator != m_backingAllocator)
                throw new InvalidOperationException($"The specified backing allocator does not match the backing allocator this Tlsf allocator uses.");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void CheckAlignmentIs64(int alignment)
        {
            if (alignment != 64)
                throw new InvalidOperationException(
                    $"Requested alignment is {alignment}. This TLSF allocator requires the alignment be exactly 64. Smaller alignments should automatically be increased to 64 via AllocatorManager internals.");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckDeallocation(void* ptr)
        {
            if (ptr == null)
                throw new NullReferenceException("Attempted to free a null pointer.");
            var header = (AllocationHeader*)ptr;
            header--;
            if (!header->footprint.Equals(kFootprint))
                throw new InvalidOperationException(
                    $"The pointer does not appear to be allocated by a TLSF allocator. It may not point to the start of the allocation, or adjacent memory may have been stomped on.");
            if (header->allocator != m_thisAllocator)
                throw new InvalidOperationException($"The pointer was allocated with a different TLSF allocator then the one it is attempting to free with.");
            if (header->free)
                throw new InvalidOperationException($"The pointer has already been freed.");
        }
        #endregion
    }
}

