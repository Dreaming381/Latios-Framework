using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Latios.Myri
{
    /// <summary>
    /// Job for encoding audio data
    /// </summary>
    [BurstCompile]
    public struct EncodeAudioJob : IJob
    {
        [ReadOnly] public NativeArray<float> inputData;
        [WriteOnly] public NativeArray<byte> outputData;
        public int channels;

        public void Execute()
        {
            ADPCMCodec.Encode(inputData, ref outputData, channels);
        }
    }
    /// <summary>
    /// Job for decoding audio data
    /// </summary>
    [BurstCompile]
    public struct DecodeAudioJob : IJob
    {
        [ReadOnly] public NativeArray<byte> inputData;
        [WriteOnly] public NativeArray<float> outputData;
        public int channels;
        public int startSample;

        public void Execute()
        {
            ADPCMCodec.Decode(inputData, ref outputData, channels, startSample);
        }
    }
}
