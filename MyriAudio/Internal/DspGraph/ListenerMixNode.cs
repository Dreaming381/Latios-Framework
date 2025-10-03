using Latios.Myri.DSP;
using Unity.Audio;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Myri
{
    [BurstCompile(CompileSynchronously = true)]
    internal unsafe struct ListenerMixNode : IAudioKernel<ListenerMixNode.Parameters, ListenerMixNode.SampleProviders>
    {
        public enum Parameters
        {
            Unused
        }
        public enum SampleProviders
        {
            Unused
        }

        internal struct SvfState
        {
            public StateVariableFilter.Channel      channel;
            public StateVariableFilter.Coefficients coefficients;
        }

        internal BrickwallLimiter                        m_limiter;
        int                                              m_expectedSampleBufferSize;
        internal BlobAssetReference<ListenerProfileBlob> m_blob;
        internal UnsafeList<SvfState>                    m_svfs;
        internal UnsafeList<float>                       m_channelStepReferences;
        internal bool                                    m_firstFrame;

        public void Initialize()
        {
            m_limiter = new BrickwallLimiter(BrickwallLimiter.kDefaultPreGain,
                                             BrickwallLimiter.kDefaultVolume,
                                             BrickwallLimiter.kDefaultReleaseDBPerSample,
                                             BrickwallLimiter.kDefaultLookaheadSampleCount,
                                             Allocator.AudioKernel);
            m_expectedSampleBufferSize = 1024;
            m_blob                     = default;
            m_svfs                     = new UnsafeList<SvfState>(8, Allocator.AudioKernel);
            m_channelStepReferences    = new UnsafeList<float>(8, Allocator.AudioKernel);
            m_firstFrame               = true;
        }

        public void Dispose()
        {
            m_limiter.Dispose();
            m_svfs.Dispose();
            m_channelStepReferences.Dispose();
        }

        public void Execute(ref ExecuteContext<Parameters, SampleProviders> context)
        {
            if (context.Outputs.Count <= 0)
                return;
            var mixedOutputSampleBuffer = context.Outputs.GetSampleBuffer(0);
            ZeroSampleBuffer(mixedOutputSampleBuffer);
            if (mixedOutputSampleBuffer.Channels < 2)
                return;
            if (m_blob == default)
                return;

            var leftBuffer  = mixedOutputSampleBuffer.GetBuffer(0);
            int filterStart = 0;
            for (int c = 0; c < math.min(context.Inputs.Count, m_blob.Value.channelDspsLeft.Length); c++)
            {
                var   inputBuffer = context.Inputs.GetSampleBuffer(c).GetBuffer(0);
                var   stepBuffer  = context.Inputs.GetSampleBuffer(c).GetBuffer(1);
                var   filterCount = m_blob.Value.channelDspsLeft[c].filters.Length;
                var   volume      = m_blob.Value.channelDspsLeft[c].volume;
                float step        = m_channelStepReferences[c] - inputBuffer[0];
                if (m_firstFrame)
                    step = 0f;
                if (leftBuffer.Length != 0)
                    m_channelStepReferences[c] = stepBuffer[0];
                else
                    m_channelStepReferences[c] = 0f;

                for (int i = 0; i < leftBuffer.Length; i++)
                {
                    var sample  = inputBuffer[i];
                    sample     += math.remap(0f, 127f, step, 0f, math.min(i, 127f));

                    for (int f = 0; f < filterCount; f++)
                    {
                        ref var filter = ref m_svfs.ElementAt(filterStart + f);
                        sample         = StateVariableFilter.ProcessSample(ref filter.channel, in filter.coefficients, sample);
                    }

                    leftBuffer[i] += sample * volume;
                }
                filterStart += filterCount;
            }

            var rightBuffer = mixedOutputSampleBuffer.GetBuffer(1);
            for (int r = 0, c = m_blob.Value.channelDspsLeft.Length; c < context.Inputs.Count; c++, r++)
            {
                var   inputBuffer = context.Inputs.GetSampleBuffer(c).GetBuffer(0);
                var   stepBuffer  = context.Inputs.GetSampleBuffer(c).GetBuffer(1);
                var   filterCount = m_blob.Value.channelDspsRight[r].filters.Length;
                var   volume      = m_blob.Value.channelDspsRight[r].volume;
                float step        = m_channelStepReferences[c] - inputBuffer[0];
                if (m_firstFrame)
                    step = 0f;
                if (leftBuffer.Length != 0)
                    m_channelStepReferences[c] = stepBuffer[0];
                else
                    m_channelStepReferences[c] = 0f;
                for (int i = 0; i < rightBuffer.Length; i++)
                {
                    var sample  = inputBuffer[i];
                    sample     += math.remap(0f, 127f, step, 0f, math.min(i, 127f));

                    for (int f = 0; f < filterCount; f++)
                    {
                        ref var filter = ref m_svfs.ElementAt(filterStart + f);
                        sample         = StateVariableFilter.ProcessSample(ref filter.channel, in filter.coefficients, sample);
                    }

                    rightBuffer[i] += sample * volume;
                }
            }

            if (context.DSPBufferSize != m_expectedSampleBufferSize)
            {
                m_limiter.releasePerSampleDB *= (float)m_expectedSampleBufferSize / context.DSPBufferSize;
                m_expectedSampleBufferSize    = context.DSPBufferSize;
            }

            var length = leftBuffer.Length;
            for (int i = 0; i < length; i++)
            {
                m_limiter.ProcessSample(leftBuffer[i], rightBuffer[i], out var leftOut, out var rightOut);
                leftBuffer[i]  = leftOut;
                rightBuffer[i] = rightOut;
            }

            m_firstFrame = false;
        }

        void ZeroSampleBuffer(SampleBuffer sb)
        {
            for (int c = 0; c < sb.Channels; c++)
            {
                var b = sb.GetBuffer(c);
                b.AsSpan().Clear();
            }
        }
    }

    [BurstCompile(CompileSynchronously = true)]
    internal unsafe struct ListenerMixNodeChannelUpdate : IAudioKernelUpdate<ListenerMixNode.Parameters, ListenerMixNode.SampleProviders, ListenerMixNode>
    {
        public BlobAssetReference<ListenerProfileBlob> blob;
        public int                                     sampleRate;

        public void Update(ref ListenerMixNode audioKernel)
        {
            audioKernel.m_blob = blob;
            audioKernel.m_svfs.Clear();
            audioKernel.m_channelStepReferences.Clear();
            audioKernel.m_firstFrame = true;
            for (int channel = 0; channel < blob.Value.channelDspsLeft.Length; channel++)
            {
                foreach (var filter in blob.Value.channelDspsLeft[channel].filters.AsSpan())
                {
                    audioKernel.m_svfs.Add(new ListenerMixNode.SvfState
                    {
                        channel      = default,
                        coefficients = StateVariableFilter.CreateFilterCoefficients(filter.type, filter.cutoff, filter.q, filter.gainInDecibels, sampleRate)
                    });
                }
                audioKernel.m_channelStepReferences.Add(0f);
            }
            for (int channel = 0; channel < blob.Value.channelDspsRight.Length; channel++)
            {
                foreach (var filter in blob.Value.channelDspsRight[channel].filters.AsSpan())
                {
                    audioKernel.m_svfs.Add(new ListenerMixNode.SvfState
                    {
                        channel      = default,
                        coefficients = StateVariableFilter.CreateFilterCoefficients(filter.type, filter.cutoff, filter.q, filter.gainInDecibels, sampleRate)
                    });
                }
                audioKernel.m_channelStepReferences.Add(0f);
            }
        }
    }

    [BurstCompile(CompileSynchronously = true)]
    internal unsafe struct ListenerMixNodeVolumeUpdate : IAudioKernelUpdate<ListenerMixNode.Parameters, ListenerMixNode.SampleProviders, ListenerMixNode>
    {
        public BrickwallLimiterSettings settings;

        public void Update(ref ListenerMixNode audioKernel)
        {
            audioKernel.m_limiter.preGain            = settings.preGain;
            audioKernel.m_limiter.volume             = settings.volume;
            audioKernel.m_limiter.releasePerSampleDB = settings.releasePerSampleDB;
            audioKernel.m_limiter.SetLookaheadSampleCount(settings.lookaheadSampleCount);
        }
    }
}

