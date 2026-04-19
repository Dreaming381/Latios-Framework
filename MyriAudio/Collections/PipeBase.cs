using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Latios.Myri
{
    /// <summary>
    /// A span of memory which can be stored as a field inside of any object stored within a CommandPipe or FeedbackPipe.
    /// These can be nested.
    /// </summary>
    /// <typeparam name="T">The type of element stored within the span</typeparam>
    public unsafe struct PipeSpan<T> where T : unmanaged
    {
        /// <summary>
        /// The number of elements in the span
        /// </summary>
        public int length => m_length;
        /// <summary>
        /// Gets the pointer to the raw memory of the span
        /// </summary>
        /// <returns></returns>
        public T* GetUnsafePtr() => m_ptr;
        /// <summary>
        /// Gets an element of the span by ref
        /// </summary>
        /// <param name="index">The index of the element to fetch</param>
        /// <returns>The element at the specified index</returns>
        public ref T this[int index] => ref AsSpan()[index];
        /// <summary>
        /// Gets an enumerator over the span
        /// </summary>
        /// <returns></returns>
        public Span<T>.Enumerator GetEnumerator() => AsSpan().GetEnumerator();
        /// <summary>
        /// Returns the PipeSpan as a .NET Span
        /// </summary>
        /// <returns></returns>
        public Span<T> AsSpan()
        {
            MegaPipe.CheckNotNull(m_ptr);
            return new Span<T>(m_ptr, length);
        }

        /// <summary>
        /// Implicitly converts this PipeSpan into a DynamicPipeSpan.
        /// </summary>
        public static implicit operator DynamicPipeSpan(PipeSpan<T> streamSpan)
        {
            return new DynamicPipeSpan
            {
                m_ptr      = streamSpan.m_ptr,
                m_length   = streamSpan.m_length,
                m_typeHash = BurstRuntime.GetHashCode32<T>()
            };
        }

        internal T*  m_ptr;
        internal int m_length;
    }

    /// <summary>
    /// A type-punned version of PipeSpan whic stores the type hash for safety referencing.
    /// </summary>
    public unsafe struct DynamicPipeSpan
    {
        /// <summary>
        /// Try to retrieve the PipeSpan of the specified type. Throws if the type hash doesn't match.
        /// </summary>
        public PipeSpan<T> GetSpan<T>() where T : unmanaged
        {
            CheckTypeHash<T>();
            MegaPipe.CheckNotNull(m_ptr);
            return new PipeSpan<T> { m_ptr = (T*)m_ptr, m_length = m_length };
        }

        internal void* m_ptr;
        internal int   m_length;
        internal int   m_typeHash;

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckTypeHash<T>() where T : unmanaged
        {
            if (m_typeHash != BurstRuntime.GetHashCode32<T>())
                throw new InvalidOperationException($"Attempted to access a PipeSpan from a DynamicPipeSpan using the wrong type.");
        }
    }

    [StructLayout(LayoutKind.Sequential, Size = 256)]
    internal unsafe struct MegaPipe : IDisposable
    {
        struct MessageHeader
        {
            public byte*          dataPtr;
            public MessageHeader* nextHeader;
        }

        struct LinkedMessageList
        {
            public MessageHeader* first;
            public MessageHeader* last;
        }

        struct BlockPtr
        {
            public byte* ptr;
            public int   byteCount;
        }

        struct HeaderBlockPtr
        {
            public MessageHeader* ptr;
            public int            headerCount;
        }

        UnsafeHashMap<long, LinkedMessageList> m_typeHashToLinkedMessageListMap;
        AllocatorManager.AllocatorHandle       m_allocator;
        UnsafeList<BlockPtr>                   m_blocks;
        UnsafeList<HeaderBlockPtr>             m_headerBlocks;
        byte*                                  m_nextFreeAddress;
        MessageHeader*                         m_nextFreeHeaderAddress;
        int                                    m_bytesRemainingInBlock;
        int                                    m_headersRemainingInBlock;

        public MegaPipe(AllocatorManager.AllocatorHandle allocator)
        {
            m_typeHashToLinkedMessageListMap = new UnsafeHashMap<long, LinkedMessageList>(32, allocator);
            m_allocator                      = allocator;
            m_blocks                         = new UnsafeList<BlockPtr>(8, allocator);
            m_headerBlocks                   = new UnsafeList<HeaderBlockPtr>(8, allocator);
            m_nextFreeAddress                = null;
            m_nextFreeHeaderAddress          = null;
            m_bytesRemainingInBlock          = 0;
            m_headersRemainingInBlock        = 0;
        }

        public bool isCreated => m_typeHashToLinkedMessageListMap.IsCreated;

        public T* AllocateMessage<T>() where T : unmanaged
        {
            var neededBytes = UnsafeUtility.SizeOf<T>();
            var typeHash    = BurstRuntime.GetHashCode64<T>();
            return (T*)AllocateMessage(typeHash, neededBytes, UnsafeUtility.AlignOf<T>());
        }

        public void* AllocateMessage(long typeHash, int sizeInBytes, int alignInBytes)
        {
            // Step 1: AllocateMessage value
            var result = AllocateData(sizeInBytes, alignInBytes);

            // Step 2: AllocateMessage header
            if (Hint.Unlikely(m_headersRemainingInBlock <= 0))
            {
                if (Hint.Unlikely(!m_headerBlocks.IsCreated))
                {
                    m_headerBlocks = new UnsafeList<HeaderBlockPtr>(8, m_allocator);
                }
                var newBlock = new HeaderBlockPtr
                {
                    headerCount = 1024,
                    ptr         = AllocatorManager.Allocate<MessageHeader>(m_allocator, 1024)
                };
                m_headerBlocks.Add(newBlock);
                m_nextFreeHeaderAddress   = newBlock.ptr;
                m_headersRemainingInBlock = 1024;
            }
            var headerAddress = m_nextFreeHeaderAddress;
            m_nextFreeHeaderAddress++;
            m_headersRemainingInBlock--;

            // Step 3: Insert header
            *headerAddress = new MessageHeader
            {
                dataPtr    = (byte*)result,
                nextHeader = null
            };
            if (m_typeHashToLinkedMessageListMap.TryGetValue(typeHash, out var linkedList))
            {
                linkedList.last->nextHeader                = headerAddress;
                linkedList.last                            = headerAddress;
                m_typeHashToLinkedMessageListMap[typeHash] = linkedList;
            }
            else
            {
                m_typeHashToLinkedMessageListMap.Add(typeHash, new LinkedMessageList
                {
                    first = headerAddress,
                    last  = headerAddress
                });
            }

            return result;
        }

        // Used both internally and for spans
        public void* AllocateData(int sizeInBytes, int alignInBytes)
        {
            var neededBytes = sizeInBytes;
            if (Hint.Unlikely(!CollectionHelper.IsAligned(m_nextFreeAddress, alignInBytes)))
            {
                var newAddress           = (byte*)CollectionHelper.Align((ulong)m_nextFreeAddress, (ulong)alignInBytes);
                var diff                 = newAddress - m_nextFreeAddress;
                m_bytesRemainingInBlock -= (int)diff;
                m_nextFreeAddress        = newAddress;
            }

            if (Hint.Unlikely(neededBytes > m_bytesRemainingInBlock))
            {
                if (Hint.Unlikely(!m_blocks.IsCreated))
                {
                    m_blocks = new UnsafeList<BlockPtr>(8, m_allocator);
                }
                var blockSize = math.max(neededBytes, 16 * 1024);
                var newBlock  = new BlockPtr
                {
                    byteCount = blockSize,
                    ptr       = AllocatorManager.Allocate<byte>(m_allocator, blockSize)
                };
                UnityEngine.Debug.Assert(CollectionHelper.IsAligned(newBlock.ptr, alignInBytes));
                m_blocks.Add(newBlock);
                m_nextFreeAddress       = newBlock.ptr;
                m_bytesRemainingInBlock = blockSize;
            }

            var result               = m_nextFreeAddress;
            m_bytesRemainingInBlock -= neededBytes;
            m_nextFreeAddress       += neededBytes;

            return result;
        }

        public void Dispose()
        {
            foreach (var block in m_headerBlocks)
                AllocatorManager.Free(m_allocator, block.ptr, block.headerCount);
            foreach (var block in m_blocks)
                AllocatorManager.Free(m_allocator, block.ptr, block.byteCount);
            m_headerBlocks.Dispose();
            m_blocks.Dispose();
            m_typeHashToLinkedMessageListMap.Dispose();
        }

        public Enumerator GetEnumerator(long typeHash) => new Enumerator(typeHash, ref this);

        public struct Enumerator
        {
            MessageHeader* m_currentHeader;
            public Enumerator(long typeHash, ref MegaPipe pipe)
            {
                if (pipe.m_typeHashToLinkedMessageListMap.TryGetValue(typeHash, out var linkedList))
                {
                    m_currentHeader = linkedList.first;
                }
                else
                    m_currentHeader = null;
            }
            public void* Current => m_currentHeader->dataPtr;
            public bool MoveNext()
            {
                if (m_currentHeader == null)
                    return false;
                if (m_currentHeader->nextHeader == null)
                    return false;
                m_currentHeader = m_currentHeader->nextHeader;
                return true;
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        internal static void CheckNotNull(void* rawPtr)
        {
            if (rawPtr == null)
                throw new InvalidOperationException("Attempted to access a typed allocation from a null object.");
        }
    }
}

