using Unity.Audio;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Myri
{
    [BurstCompile(CompileSynchronously = true)]
    public unsafe struct MixPortsToStereoNode : IAudioKernel<MixPortsToStereoNode.Parameters, MixPortsToStereoNode.SampleProviders>
    {
        public enum Parameters
        {
            Unused
        }
        public enum SampleProviders
        {
            Unused
        }

        internal int m_leftChannelCount;

        public void Initialize()
        {
        }

        public void Execute(ref ExecuteContext<Parameters, SampleProviders> context)
        {
            if (context.Outputs.Count <= 0)
                return;
            var mixedOutputSampleBuffer = context.Outputs.GetSampleBuffer(0);

            if (mixedOutputSampleBuffer.Channels > 0)
            {
                var leftBuffer = mixedOutputSampleBuffer.GetBuffer(0);
                for (int i = 0; i < leftBuffer.Length; i++)
                {
                    leftBuffer[i] = 0f;
                }
                for (int c = 0; c < math.min(context.Inputs.Count, m_leftChannelCount); c++)
                {
                    var inputBuffer = context.Inputs.GetSampleBuffer(c).GetBuffer(0);
                    for (int i = 0; i < leftBuffer.Length; i++)
                    {
                        leftBuffer[i] += inputBuffer[i];
                    }
                }
            }
            if (mixedOutputSampleBuffer.Channels > 1)
            {
                var rightBuffer = mixedOutputSampleBuffer.GetBuffer(1);
                for (int i = 0; i < rightBuffer.Length; i++)
                {
                    rightBuffer[i] = 0f;
                }
                for (int c = m_leftChannelCount; c < context.Inputs.Count; c++)
                {
                    var inputBuffer = context.Inputs.GetSampleBuffer(c).GetBuffer(0);
                    for (int i = 0; i < rightBuffer.Length; i++)
                    {
                        rightBuffer[i] += inputBuffer[i];
                    }
                }
            }

            for (int p = 1; p < context.Outputs.Count; p++)
            {
                var additionalOutputSampleBuffer = context.Outputs.GetSampleBuffer(p);
                if (mixedOutputSampleBuffer.Channels > 0 && additionalOutputSampleBuffer.Channels > 0)
                {
                    var leftBuffer           = mixedOutputSampleBuffer.GetBuffer(0);
                    var additionalLeftBuffer = additionalOutputSampleBuffer.GetBuffer(0);
                    for (int i = 0; i < leftBuffer.Length; i++)
                    {
                        additionalLeftBuffer[i] = leftBuffer[i];
                    }
                }
                if (mixedOutputSampleBuffer.Channels > 1 && additionalOutputSampleBuffer.Channels > 1)
                {
                    var rightBuffer           = mixedOutputSampleBuffer.GetBuffer(1);
                    var additionalRightBuffer = additionalOutputSampleBuffer.GetBuffer(1);
                    for (int i = 0; i < rightBuffer.Length; i++)
                    {
                        additionalRightBuffer[i] = rightBuffer[i];
                    }
                }
            }
        }

        public void Dispose()
        {
        }
    }

    [BurstCompile(CompileSynchronously = true)]
    internal unsafe struct MixPortsToStereoNodeUpdate : IAudioKernelUpdate<MixPortsToStereoNode.Parameters, MixPortsToStereoNode.SampleProviders, MixPortsToStereoNode>
    {
        public int leftChannelCount;

        public void Update(ref MixPortsToStereoNode audioKernel)
        {
            audioKernel.m_leftChannelCount = leftChannelCount;
        }
    }
}

