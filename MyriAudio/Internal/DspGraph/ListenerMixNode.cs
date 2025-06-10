using Latios.Myri.DSP;
using Unity.Audio;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
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

        internal BrickwallLimiter m_limiter;
        int                       m_expectedSampleBufferSize;
        internal int              m_leftChannelCount;

        public void Initialize()
        {
            m_limiter = new BrickwallLimiter(BrickwallLimiter.kDefaultPreGain,
                                             BrickwallLimiter.kDefaultVolume,
                                             BrickwallLimiter.kDefaultReleaseDBPerSample,
                                             BrickwallLimiter.kDefaultLookaheadSampleCount,
                                             Allocator.AudioKernel);
            m_expectedSampleBufferSize = 1024;
        }

        public void Dispose() => m_limiter.Dispose();

        public void Execute(ref ExecuteContext<Parameters, SampleProviders> context)
        {
            if (context.Outputs.Count <= 0)
                return;
            var mixedOutputSampleBuffer = context.Outputs.GetSampleBuffer(0);
            ZeroSampleBuffer(mixedOutputSampleBuffer);
            if (mixedOutputSampleBuffer.Channels < 2)
                return;

            var leftBuffer = mixedOutputSampleBuffer.GetBuffer(0);
            for (int c = 0; c < math.min(context.Inputs.Count, m_leftChannelCount); c++)
            {
                var inputBuffer = context.Inputs.GetSampleBuffer(c).GetBuffer(0);
                for (int i = 0; i < leftBuffer.Length; i++)
                {
                    leftBuffer[i] += inputBuffer[i];
                }
            }

            var rightBuffer = mixedOutputSampleBuffer.GetBuffer(1);
            for (int c = m_leftChannelCount; c < context.Inputs.Count; c++)
            {
                var inputBuffer = context.Inputs.GetSampleBuffer(c).GetBuffer(0);
                for (int i = 0; i < rightBuffer.Length; i++)
                {
                    rightBuffer[i] += inputBuffer[i];
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
        public int leftChannelCount;

        public void Update(ref ListenerMixNode audioKernel)
        {
            audioKernel.m_leftChannelCount = leftChannelCount;
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

