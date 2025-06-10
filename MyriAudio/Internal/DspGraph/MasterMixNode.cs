using Latios.Myri.DSP;
using Unity.Audio;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

namespace Latios.Myri
{
    [BurstCompile(CompileSynchronously = true)]
    internal unsafe struct MasterMixNode : IAudioKernel<MasterMixNode.Parameters, MasterMixNode.SampleProviders>
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
            var outputBuffer = context.Outputs.GetSampleBuffer(0);

            for (int input = 0; input < context.Inputs.Count; input++)
            {
                var inputBuffer = context.Inputs.GetSampleBuffer(input);
                for (int c = 0; c < math.min(outputBuffer.Channels, inputBuffer.Channels); c++)
                {
                    var inputSamples  = inputBuffer.GetBuffer(c);
                    var outputSamples = outputBuffer.GetBuffer(c);
                    for (int i = 0; i < outputSamples.Length; i++)
                    {
                        outputSamples[i] += inputSamples[i];
                    }
                }
            }
            if (outputBuffer.Channels <= 1)
            {
                ZeroSampleBuffer(outputBuffer);
                return;
            }
            if (context.Inputs.Count <= 0)
            {
                ZeroSampleBuffer(outputBuffer);
                return;
            }

            if (context.DSPBufferSize != m_expectedSampleBufferSize)
            {
                m_limiter.releasePerSampleDB *= (float)m_expectedSampleBufferSize / context.DSPBufferSize;
                m_expectedSampleBufferSize    = context.DSPBufferSize;
            }

            var outputL = outputBuffer.GetBuffer(0);
            var outputR = outputBuffer.GetBuffer(1);
            int length  = outputL.Length;

            for (int i = 0; i < length; i++)
            {
                m_limiter.ProcessSample(outputL[i], outputR[i], out var leftOut, out var rightOut);
                outputL[i] = leftOut;
                outputR[i] = rightOut;
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
    internal struct MasterMixNodeUpdate : IAudioKernelUpdate<MasterMixNode.Parameters, MasterMixNode.SampleProviders, MasterMixNode>
    {
        public BrickwallLimiterSettings settings;

        public void Update(ref MasterMixNode audioKernel)
        {
            audioKernel.m_limiter.preGain            = settings.preGain;
            audioKernel.m_limiter.volume             = settings.volume;
            audioKernel.m_limiter.releasePerSampleDB = settings.releasePerSampleDB;
            audioKernel.m_limiter.SetLookaheadSampleCount(settings.lookaheadSampleCount);
        }
    }
}

