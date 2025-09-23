using System;
using Unity.Burst;
using Unity.Collections;

namespace Latios.Myri
{
    [BurstCompile]
    public struct ADPCMEncodeContext: IDisposable
    {
        public int Channels;
        public int SampleRate;

        public NativeArray<float> OriginalClipData;
        public NativeArray<byte> EncodedClipData;


        public ADPCMEncodeContext(NativeArray<float> samples, int channels, int sampleRate = 44100)
        {
            Channels = channels;
            SampleRate = sampleRate;

            OriginalClipData = samples;
            EncodedClipData = new NativeArray<byte>((samples.Length + 1) / 2, Allocator.TempJob);
        }


        public void Dispose()
        {
            OriginalClipData.Dispose();
            EncodedClipData.Dispose();
        }

        public EncodeAudioJob Encode()
        {
            return new EncodeAudioJob
            {
                inputData = OriginalClipData,
                outputData = EncodedClipData,
                channels = Channels
            };
        }

        public ADPCMDecodeContext GetDecoder(){
            var copyEncoded = new NativeArray<byte>(EncodedClipData, Allocator.TempJob);
            return new ADPCMDecodeContext(copyEncoded, Channels, OriginalClipData.Length, SampleRate); 
        }
    }

    [BurstCompile]
    public struct ADPCMDecodeContext : IDisposable
    {
        public int Channels;
        public int SampleRate;

        public NativeArray<byte> EncodedClipData;
        public NativeArray<float> DecodedClipData;

        public ADPCMDecodeContext(NativeArray<byte> encoded, int channels, int sampleCount, int sampleRate = 44100)
        {
            Channels = channels;
            SampleRate = sampleRate;

            EncodedClipData = encoded;
            DecodedClipData = new NativeArray<float>(sampleCount, Allocator.TempJob);
        }


        public void Dispose()
        {
            EncodedClipData.Dispose();
            DecodedClipData.Dispose();
        }

        public DecodeAudioJob Decode(int startSample)
        {
            return new DecodeAudioJob
            {
                inputData = EncodedClipData,
                outputData = DecodedClipData,
                channels = Channels,
                startSample = startSample
            };
        }
    }
}
