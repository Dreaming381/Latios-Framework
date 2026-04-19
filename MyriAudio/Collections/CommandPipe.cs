using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Latios.Myri
{
    public unsafe struct CommandPipeWriter
    {
        internal NativeReference<UnsafeList<MegaPipe> > m_perThreadPipes;
        internal AllocatorManager.AllocatorHandle       m_allocator;
        [NativeSetThreadIndex] int                      m_threadIndex;

        ref MegaPipe GetPipe()
        {
            ref var pipe = ref m_perThreadPipes.Value.ElementAt(m_threadIndex);
            if (!pipe.isCreated)
            {
                pipe = new MegaPipe(m_allocator);
            }
            return ref pipe;
        }

        /// <summary>
        /// Writes a message to the pipe. Shorthand for creating a message and assigning to it.
        /// </summary>
        public void WriteMessage<T>(in T message) where T : unmanaged
        {
            CreateMessage<T>() = message;
        }

        /// <summary>
        /// Allocates a message in the pipe, and then returns a reference for further configuration.
        /// </summary>
        public ref T CreateMessage<T>() where T : unmanaged
        {
            var m = GetPipe().AllocateMessage<T>();
            *   m = default;
            return ref *m;
        }

        /// <summary>
        /// Allocates a message without explicitly calling out the type generically, and then returns a pointer
        /// to the allocation. The allocation will not be zero-initialized.
        /// </summary>
        /// <param name="typeHash">The BurstRuntime.GetHashCode64() of the type</param>
        /// <param name="sizeInBytes">The size of the type in bytes</param>
        /// <param name="alignInBytes">The alignment of the allocation</param>
        /// <returns>A pointer to the allocation</returns>
        public void* CreateMessageDynamic(long typeHash, int sizeInBytes, int alignInBytes)
        {
            return GetPipe().AllocateMessage(typeHash, sizeInBytes, alignInBytes);
        }

        /// <summary>
        /// Allocates an array in the pipe which can be assigned to any message (or another PipeSpan) in the pipe.
        /// </summary>
        /// <param name="elementCount">The number of elements to be allocated</param>
        /// <returns>The array within the pipe</returns>
        public PipeSpan<T> CreatePipeSpan<T>(int elementCount) where T : unmanaged
        {
            return new PipeSpan<T>
            {
                m_ptr    = (T*)GetPipe().AllocateData(UnsafeUtility.SizeOf<T>() * elementCount, UnsafeUtility.AlignOf<T>()),
                m_length = elementCount
            };
        }
    }

    public unsafe struct CommandPipeReader
    {
        internal UnsafeList<MegaPipe> m_perThreadPipes;

        /// <summary>
        /// Use in a foreach expression to iterate all messages of the specified type.
        /// Messages are in a non-deterministic order.
        /// </summary>
        public Enumerator<T> Each<T>() where T : unmanaged
        {
            return new Enumerator<T>
            {
                m_perThreadPipes     = m_perThreadPipes,
                m_currentThreadIndex = -1
            };
        }

        /// <summary>
        /// Use in a foreach expression to iterate all messages of the specified type,
        /// without concretely specifying the type (provides void* elements).
        /// Messages are in a non-deterministic order.
        /// </summary>
        public EnumeratorUntyped Each(long typeHash)
        {
            return new EnumeratorUntyped
            {
                m_perThreadPipes     = m_perThreadPipes,
                m_typeHash           = typeHash,
                m_currentThreadIndex = -1
            };
        }

        public struct Enumerator<T> where T : unmanaged
        {
            internal UnsafeList<MegaPipe> m_perThreadPipes;
            internal int                  m_currentThreadIndex;
            MegaPipe.Enumerator           m_pipeEnumerator;

            public Enumerator<T> GetEnumerator() => this;
            public ref T Current => ref *(T*)m_pipeEnumerator.Current;
            public bool MoveNext()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                // This forces a safety check.
                if (m_currentThreadIndex >= m_perThreadPipes.Length)
                    return false;
#endif

                if (m_pipeEnumerator.MoveNext())
                    return true;

                m_currentThreadIndex++;
                for (; m_currentThreadIndex < m_perThreadPipes.Length; m_currentThreadIndex++)
                {
                    ref var pipe = ref m_perThreadPipes.ElementAt(m_currentThreadIndex);
                    if (!pipe.isCreated)
                        continue;
                    var candidateEnumerator = pipe.GetEnumerator(BurstRuntime.GetHashCode64<T>());
                    if (candidateEnumerator.MoveNext())
                    {
                        m_pipeEnumerator = candidateEnumerator;
                        return true;
                    }
                }

                return false;
            }
        }

        public struct EnumeratorUntyped
        {
            internal UnsafeList<MegaPipe> m_perThreadPipes;
            internal long                 m_typeHash;
            internal int                  m_currentThreadIndex;
            MegaPipe.Enumerator           m_pipeEnumerator;

            public EnumeratorUntyped GetEnumerator() => this;
            public void* Current => m_pipeEnumerator.Current;
            public bool MoveNext()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                // This forces a safety check.
                if (m_currentThreadIndex >= m_perThreadPipes.Length)
                    return false;
#endif

                if (m_pipeEnumerator.MoveNext())
                    return true;

                m_currentThreadIndex++;
                for (; m_currentThreadIndex < m_perThreadPipes.Length; m_currentThreadIndex++)
                {
                    ref var pipe = ref m_perThreadPipes.ElementAt(m_currentThreadIndex);
                    if (!pipe.isCreated)
                        continue;
                    var candidateEnumerator = pipe.GetEnumerator(m_typeHash);
                    if (candidateEnumerator.MoveNext())
                    {
                        m_pipeEnumerator = candidateEnumerator;
                        return true;
                    }
                }

                return false;
            }
        }
    }
}

