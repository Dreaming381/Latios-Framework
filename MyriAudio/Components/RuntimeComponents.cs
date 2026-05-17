using System.Threading;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Audio;

namespace Latios.Myri
{
    /// <summary>
    /// A read-only component on the worldBlackboardEntity which provides the current Unity audio format.
    /// Use this to read the sample rate, audio frame sample count, and channel configuration.
    /// This component is not added until the format is established. DO NOT MODIFY!
    /// </summary>
    public struct AudioEcsFormat : IComponentData
    {
        public AudioFormat audioFormat;
    }

    /// <summary>
    /// A read-only component on the worldBlackboardEntity that allows you to query the realtime state of the audio thread.
    /// Use this to estimate how far ahead in DSP time events need to be scheduled for.
    /// </summary>
    public unsafe struct AudioEcsAtomicFeedbackIds : IComponentData
    {
        internal long* m_atomicPackedIds;

        public struct Ids
        {
            public int feedbackIdStarted;
            public int maxCommandIdConsumed;
        }
        public Ids Read()
        {
            var packed = Interlocked.Read(ref *m_atomicPackedIds);
            return new Ids
            {
                feedbackIdStarted    = (int)(packed & 0xffffffff),
                maxCommandIdConsumed = (int)(packed >> 32),
            };
        }

        internal void Write(Ids ids)
        {
            var packed = ids.feedbackIdStarted + (((long)ids.maxCommandIdConsumed) << 32);
            Interlocked.Exchange(ref *m_atomicPackedIds, packed);
        }
    }

    /// <summary>
    /// A collection component on the worldBlackboardEntity that allows writing messages to be sent to the audio thread.
    /// Always declare access to this component and in jobs as read-only, even though you write messages to it.
    /// </summary>
    public partial struct AudioEcsCommandPipe : ICollectionComponent
    {
        internal CommandPipeWriter    m_pipe;
        internal NativeReference<int> m_commandBufferId;

        /// <summary>
        /// The entry-point that allows writing messages to be sent to the audio thread.
        /// </summary>
        public CommandPipeWriter pipe => m_pipe;
        /// <summary>
        /// The command ID that will be associated with any messages sent to the audio thread.
        /// This ID increases by one every visual frame.
        /// </summary>
        public int commandId => m_commandBufferId.Value;

        public JobHandle TryDispose(JobHandle inputDeps)
        {
            inputDeps.Complete();
            foreach (var p in m_pipe.m_perThreadPipes.Value)
            {
                if (p.isCreated)
                    p.Dispose();
            }
            m_pipe.m_perThreadPipes.Value.Dispose();
            m_pipe.m_perThreadPipes.Dispose();
            m_commandBufferId.Dispose();
            return default;
        }
    }

    /// <summary>
    /// A collection component on the worldBlackboardEntity that allows reading messages sent from the audio thread.
    /// Always declare access to this component and in jobs as read-only.
    /// </summary>
    public partial struct AudioEcsFeedbackPipe : ICollectionComponent
    {
        internal NativeList<MegaPipe> m_pipes;
        internal NativeList<int>      m_feedbackIds;

        /// <summary>
        /// The number of audio frames whose messages have been collected
        /// </summary>
        public int length => m_pipes.Length;
        /// <summary>
        /// Gets a message reader for all messages originating from a particular audio frame
        /// </summary>
        public FeedbackPipeReader this[int index] => new FeedbackPipeReader
        {
            m_megaPipe   = m_pipes.AsArray().GetSubArray(index, 1),
            m_feedbackId = m_feedbackIds[index]
        };

        public JobHandle TryDispose(JobHandle inputDeps)
        {
            return JobHandle.CombineDependencies(m_pipes.Dispose(inputDeps), m_feedbackIds.Dispose(inputDeps));
        }

        public Enumerator GetEnumerator() => new Enumerator(this);

        public struct Enumerator
        {
            AudioEcsFeedbackPipe m_pipe;
            int                  m_index;

            public Enumerator(AudioEcsFeedbackPipe pipe)
            {
                m_pipe  = pipe;
                m_index = -1;
            }

            public FeedbackPipeReader Current => m_pipe[m_index];
            public bool MoveNext()
            {
                m_index++;
                return m_index < m_pipe.length;
            }
        }
    }
}

