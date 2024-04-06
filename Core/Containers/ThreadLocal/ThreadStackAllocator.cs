using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Latios.Unsafe
{
    public unsafe struct ThreadStackAllocator : IDisposable
    {
        #region API
        public static int maxAllocatorsPerThread
        {
            get => s_settings.Data.maxDepths;
            set => s_settings.Data.maxDepths = value;
        }

        public static int defaultBlockSize
        {
            get => s_settings.Data.defaultBlockSize;
            set => s_settings.Data.defaultBlockSize = value;
        }

        public static ThreadStackAllocator GetAllocator()
        {
            return s_states.Data[JobsUtility.ThreadIndex]->CreateAllocator();
        }

        public void Dispose()
        {
            CheckAllocatorIsValid();
            m_statePtr->DisposeAllocator(this);
        }

        public T* Allocate<T>(int count) where T : unmanaged
        {
            CheckAllocatorIsValid();
            return (T*)m_statePtr->Allocate((ulong)UnsafeUtility.SizeOf<T>(), (ulong)UnsafeUtility.AlignOf<T>(), (ulong)count);
        }

        // Slightly faster than getting the allocator globally.
        public ThreadStackAllocator CreateChildAllocator()
        {
            CheckAllocatorIsValid();
            return m_statePtr->CreateAllocator();
        }
        #endregion

        #region Implementation
        State* m_statePtr;
        int    m_firstAllocationIndex;
        int    m_depth;

        static readonly SharedStatic<Settings>   s_settings = SharedStatic<Settings>.GetOrCreate<Settings>();
        static readonly SharedStatic<StateArray> s_states   = SharedStatic<StateArray>.GetOrCreate<StateArray>();

        [StructLayout(LayoutKind.Sequential, Size = JobsUtility.CacheLineSize)]
        struct State
        {
            struct Block
            {
                public byte* ptr;
                public ulong size;
            }

            struct Allocation
            {
                public ulong byteOffset;
                public ulong byteCount;
                public int   blockIndex;
            }

            UnsafeList<Block>      m_blocks;
            UnsafeList<Allocation> m_allocations;
            State*                 m_selfPtr;
            int                    m_allocatorDepth;

            public ThreadStackAllocator CreateAllocator()
            {
                CheckForDepthLeaks(m_allocatorDepth + 1);
                m_allocatorDepth++;
                return new ThreadStackAllocator
                {
                    m_depth                = m_allocatorDepth,
                    m_firstAllocationIndex = m_allocations.Length,
                    m_statePtr             = m_selfPtr
                };
            }

            public byte* Allocate(ulong sizeOfElement, ulong alignOfElement, ulong numElements)
            {
                if (numElements == 0)
                    return null;
                var neededBytes    = numElements * sizeOfElement;
                int nextBlockIndex = 0;
                if (!m_allocations.IsEmpty)
                {
                    var lastAllocation                 = m_allocations[m_allocations.Length - 1];
                    var bytesUsedInBlock               = lastAllocation.byteOffset + lastAllocation.byteCount;
                    var block                          = m_blocks[lastAllocation.blockIndex];
                    var bytesUsedInBlockAfterAlignment = CollectionHelper.Align(bytesUsedInBlock, alignOfElement);
                    var freeBytes                      = block.size - bytesUsedInBlockAfterAlignment;
                    if (neededBytes <= freeBytes)
                    {
                        m_allocations.Add(new Allocation
                        {
                            blockIndex = lastAllocation.blockIndex,
                            byteCount  = neededBytes,
                            byteOffset = bytesUsedInBlockAfterAlignment
                        });
                        return block.ptr + bytesUsedInBlockAfterAlignment;
                    }
                    nextBlockIndex = lastAllocation.blockIndex + 1;
                }
                if (m_blocks.Length > nextBlockIndex)
                {
                    // We have at least one completely empty block to try and allocate into.
                    var nextBlock = m_blocks[nextBlockIndex];
                    if (nextBlock.size < neededBytes)
                    {
                        // The next free block is too small. Destroy all the free blocks and let the new block allocator continue.
                        for (int i = nextBlockIndex; i < m_blocks.Length; i++)
                        {
                            UnsafeUtility.FreeTracked(m_blocks[i].ptr, Allocator.Persistent);
                        }
                        m_blocks.Length = nextBlockIndex;
                    }
                    else
                    {
                        m_allocations.Add(new Allocation
                        {
                            blockIndex = nextBlockIndex,
                            byteCount  = neededBytes,
                            byteOffset = 0
                        });
                        return nextBlock.ptr;
                    }
                }
                // At this point, we simply want to allocate a new block at the end of the list.
                var bytesRequiredForBlock = math.max(neededBytes, (ulong)s_settings.Data.defaultBlockSize);
                var newBlock              = new Block
                {
                    ptr  = (byte*)UnsafeUtility.MallocTracked((long)bytesRequiredForBlock, (int)alignOfElement, Allocator.Persistent, 0),
                    size = bytesRequiredForBlock,
                };
                m_blocks.Add(newBlock);
                m_allocations.Add(new Allocation
                {
                    blockIndex = nextBlockIndex,
                    byteCount  = neededBytes,
                    byteOffset = 0
                });
                return newBlock.ptr;
            }

            public void DisposeAllocator(ThreadStackAllocator allocator)
            {
                CheckForDepthLeaks(allocator.m_depth, m_allocatorDepth);
                m_allocatorDepth     = allocator.m_depth - 1;
                m_allocations.Length = allocator.m_firstAllocationIndex;
            }

            public static void Init(State* state)
            {
                state->m_blocks         = new UnsafeList<Block>(4, Allocator.Persistent, NativeArrayOptions.ClearMemory);
                state->m_allocations    = new UnsafeList<Allocation>(64, Allocator.Persistent, NativeArrayOptions.ClearMemory);
                state->m_allocatorDepth = 0;
                state->m_selfPtr        = state;
            }

            public static void Destroy(State* state)
            {
                for (int i = 0; i < state->m_blocks.Length; i++)
                {
                    UnsafeUtility.FreeTracked(state->m_blocks[i].ptr, Allocator.Persistent);
                }
                state->m_blocks.Dispose();
                state->m_allocations.Dispose();
            }
        }

        struct StateArray
        {
            struct State16
            {
                public State s0, s1, s2, s3, s4, s5, s6, s7, s8, s9, sa, sb, sc, sd, se, sf;
            }

            struct State128
            {
                public State16 s0, s1, s2, s3, s4, s5, s6, s7;
            }

            State128 m_array;

            public State* this[int index]
            {
                get
                {
                    fixed (State* ptr = &m_array.s0.s0 ) {
                        return ptr + index;
                    }
                }
            }

            public void Init()
            {
                UnityEngine.Assertions.Assert.AreEqual(128, JobsUtility.MaxJobThreadCount);
                for (int i = 0; i < 128; i++)
                {
                    State.Init(this[i]);
                }
            }

            public void Destroy()
            {
                for (int i = 0; i < 128; i++)
                {
                    State.Destroy(this[i]);
                }
            }
        }

        struct Settings
        {
            public int maxDepths;
            public int defaultBlockSize;
        }

        // Setup and teardown
        static bool s_initialized;

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#else
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.AfterAssembliesLoaded)]
#endif
        internal static void Initialize()
        {
            if (s_initialized)
                return;

            // important: this will always be called from a special unload thread (main thread will be blocking on this)
            AppDomain.CurrentDomain.DomainUnload += (_, __) => { Shutdown(); };

            // There is no domain unload in player builds, so we must be sure to shutdown when the process exits.
            AppDomain.CurrentDomain.ProcessExit += (_, __) => { Shutdown(); };

            s_settings.Data = new Settings { defaultBlockSize = 1024 * 1024, maxDepths = 32 };
            s_states.Data.Init();
            s_initialized = true;
        }

        static void Shutdown()
        {
            if (s_initialized)
                s_states.Data.Destroy();
        }

        #endregion

        #region Safety
        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckAllocatorIsValid()
        {
            if (m_statePtr == null)
                throw new System.InvalidOperationException("ThreadStackAllocator is not initialized.");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void CheckForDepthLeaks(int depth)
        {
            if (depth > s_settings.Data.maxDepths)
                throw new System.InvalidOperationException(
                    $"Thread has too many ThreadStackAllocators compared to the max threshold of {s_settings.Data.maxDepths}. This may be a sign of a leak caused by allocator instances not being disposed.");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void CheckForDepthLeaks(int depthOfFreed, int depthsTotal)
        {
            if (depthOfFreed < depthsTotal)
                throw new System.InvalidOperationException(
                    $"A ThreadStackAllocator is being deallocated before {depthsTotal - depthOfFreed} subsequently created ThreadStackAllocators have been disposed. This is a leak caused by allocator instances not being disposed.");
        }
        #endregion
    }
}

