using Unity.Audio;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Myri
{
    [BurstCompile(CompileSynchronously = true)]
    public unsafe struct MixStereoPortsNode : IAudioKernel<MixStereoPortsNode.Parameters, MixStereoPortsNode.SampleProviders>
    {
        public enum Parameters
        {
            Unused
        }
        public enum SampleProviders
        {
            Unused
        }

        public void Initialize()
        {
        }

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
        }

        public void Dispose()
        {
        }
    }
}

