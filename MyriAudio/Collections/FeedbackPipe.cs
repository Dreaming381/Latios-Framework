using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Latios.Myri
{
    public unsafe struct FeedbackPipeWriter
    {
        internal MegaPipe* m_megaPipe;

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
            var m = m_megaPipe->AllocateMessage<T>();
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
            return m_megaPipe->AllocateMessage(typeHash, sizeInBytes, alignInBytes);
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
                m_ptr    = (T*)m_megaPipe->AllocateData(UnsafeUtility.SizeOf<T>() * elementCount, UnsafeUtility.AlignOf<T>()),
                m_length = elementCount
            };
        }
    }

    public unsafe struct FeedbackPipeReader
    {
        // Length of 1, comes from a SubArray of all feedbacks to be processed.
        [ReadOnly] internal NativeArray<MegaPipe> m_megaPipe;
        internal int                              m_feedbackId;

        /// <summary>
        /// The audio frame ID that generated the messages in this pipe
        /// </summary>
        public int feedbackId => m_feedbackId;

        /// <summary>
        /// Use in a foreach expression to iterate all messages of the specified type.
        /// Messages are in the order they were written for the specific type.
        /// </summary>
        public Enumerator<T> Each<T>() where T : unmanaged
        {
            return new Enumerator<T>
            {
                m_megaPipe       = m_megaPipe,
                m_pipeEnumerator = m_megaPipe[0].GetEnumerator(BurstRuntime.GetHashCode64<T>())
            };
        }

        /// <summary>
        /// Use in a foreach expression to iterate all messages of the specified type,
        /// without concretely specifying the type (provides void* elements).
        /// Messages are in the order they were written for the specific type.
        /// </summary>
        public EnumeratorUntyped Each(long typeHash)
        {
            return new EnumeratorUntyped
            {
                m_megaPipe       = m_megaPipe,
                m_pipeEnumerator = m_megaPipe[0].GetEnumerator(typeHash)
            };
        }

        public struct Enumerator<T> where T : unmanaged
        {
            [ReadOnly] internal NativeArray<MegaPipe> m_megaPipe;
            internal MegaPipe.Enumerator              m_pipeEnumerator;

            public Enumerator<T> GetEnumerator() => this;
            public ref T Current => ref *(T*)m_pipeEnumerator.Current;
            public bool MoveNext()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (!m_megaPipe[0].isCreated)
                    return false;
#endif
                return m_pipeEnumerator.MoveNext();
            }
        }

        public struct EnumeratorUntyped
        {
            [ReadOnly] internal NativeArray<MegaPipe> m_megaPipe;
            internal MegaPipe.Enumerator              m_pipeEnumerator;

            public EnumeratorUntyped GetEnumerator() => this;
            public void* Current => m_pipeEnumerator.Current;
            public bool MoveNext()
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (!m_megaPipe[0].isCreated)
                    return false;
#endif
                return m_pipeEnumerator.MoveNext();
            }
        }
    }
}

