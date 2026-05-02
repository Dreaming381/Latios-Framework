using System;
using Latios.AuxEcs;
using Latios.Myri.DSP;
using Latios.Unsafe;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Myri.AudioEcsBuiltin
{
    public unsafe partial struct ListenerStereoMix : IAuxDisposable, IVInterface
    {
        float*                           m_samples;
        short                            m_samplesPerChannel;
        bool                             m_hasSignal;
        AllocatorManager.AllocatorHandle m_allocator;

        Span<float> left => new Span<float>(m_samples, m_samplesPerChannel);
        Span<float> right => new Span<float>(m_samples + m_samplesPerChannel, m_samplesPerChannel);

        /// <summary>
        /// True if the buffers have a signal. False if the buffers have no signal, and should treated as if all values are 0f.
        /// </summary>
        public bool hasSignal => !m_hasSignal;
        public int samplesPerChannel => m_samplesPerChannel;

        public ListenerStereoMix(int samplesPerChannel, AllocatorManager.AllocatorHandle allocator)
        {
            m_samples           = AllocatorManager.Allocate<float>(allocator, samplesPerChannel * 2);
            m_samplesPerChannel = (short)samplesPerChannel;
            m_allocator         = allocator;
            m_hasSignal         = false;
        }

        public void Dispose()
        {
            if (m_samples != null)
            {
                AllocatorManager.Free(m_allocator, m_samples, m_samplesPerChannel * 2);
                this = default;
            }
        }

        /// <summary>
        /// Marks the signal as valid and returns the stereo buffers. Buffer contents are undefined.
        /// </summary>
        public void GetToOverwrite(out Span<float> left, out Span<float> right)
        {
            m_hasSignal = true;
            left        = this.left;
            right       = this.right;
        }

        /// <summary>
        /// Marks the signal as valid and returns the stereo buffers. If this is the first write in the mix,
        /// the buffers will be cleared to 0f.
        /// </summary>
        public void GetToAdd(out Span<float> left, out Span<float> right)
        {
            if (!m_hasSignal)
                Clear();
            GetToOverwrite(out left, out right);
        }

        /// <summary>
        /// Forces the stereo buffers to be safe for reading, and then returns them.
        /// Prefer to branch on hasSignal to skip unnecessary work, rather than immediately calling this method.
        /// </summary>
        public void GetToRead(out Span<float> left, out Span<float> right)
        {
            if (!m_hasSignal)
                Clear();
            left  = this.left;
            right = this.right;
        }

        /// <summary>
        /// Starts a new mix, resetting the stereo buffers for new signal accumulation.
        /// </summary>
        public void StartNewMix() => m_hasSignal = false;

        void Clear()
        {
            var span = new Span<float>(m_samples, m_samplesPerChannel * 2);
            span.Clear();
        }
    }

    public partial struct ListenerBrickwallLimiter : IVInterface, IAuxDisposable
    {
        public BrickwallLimiter brickwallLimiter;

        public void Dispose()
        {
            if (brickwallLimiter.isCreated)
                brickwallLimiter.Dispose();
            this = default;
        }
    }

    public partial struct AudioListenerChannelIDsList : IVInterface, IAuxDisposable
    {
        public UnsafeList<AudioListenerChannelID> channelIDs;

        public AudioListenerChannelIDsList(PipeSpan<AudioListenerChannelID> newChannelIDs, AllocatorManager.AllocatorHandle allocator)
        {
            channelIDs = new UnsafeList<AudioListenerChannelID>(newChannelIDs.length, allocator);
            foreach (var channelID in newChannelIDs)
                channelIDs.Add(channelID);
        }

        public void Dispose()
        {
            if (channelIDs.IsCreated)
                channelIDs.Dispose();
            this = default;
        }
    }
}

