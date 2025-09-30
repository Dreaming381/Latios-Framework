using System;
using Unity.Entities;

namespace Latios.Myri
{
    internal struct ADPCMCodecData
    {
        struct Seek1024IMAState
        {
            public short previousSample;
            public short stepIndex;
        }

        BlobArray<byte>             leftOrMonoEncoded;
        BlobArray<byte>             rightEncoded;
        BlobArray<Seek1024IMAState> seekLeftOrMono;
        BlobArray<Seek1024IMAState> seekRight;
        internal float              signalToNoiseRatio;

        public unsafe void Encode(ref BlobBuilder builder, ReadOnlySpan<float> monoSamplesToEncode, ref CodecContext context)
        {
            var array = builder.Allocate(ref leftOrMonoEncoded, (monoSamplesToEncode.Length + 1) / 2);

            ADPCMCodec.EncodeChannel(monoSamplesToEncode, ref array);

            var decodedSamples = context.threadStackAllocator.AllocateAsSpan<float>(monoSamplesToEncode.Length);
            var imaStates      = context.threadStackAllocator.AllocateAsSpan<ADPCMCodec.IMAState>(monoSamplesToEncode.Length);

            ADPCMCodec.DecodeChannelWithIMAStates(new ReadOnlySpan<byte>(array.GetUnsafePtr(), array.Length), ref decodedSamples, ref imaStates);
            signalToNoiseRatio = DSP.DspTools.CalculateSignalToNoiseRatio(monoSamplesToEncode, decodedSamples);

            var seek = builder.Allocate(ref seekLeftOrMono, (monoSamplesToEncode.Length + 1023) / 1024);
            for (int i = 0; i < seek.Length; i++)
            {
                var ima = imaStates[1024 * i];
                seek[i] = new Seek1024IMAState { previousSample = (short)ima.previousSample, stepIndex = (short)ima.stepIndex };
            }
        }

        public unsafe void Encode(ref BlobBuilder builder, ReadOnlySpan<float> leftSamplesToEncode, ReadOnlySpan<float> rightSamplesToEncode, ref CodecContext context)
        {
            var leftArray  = builder.Allocate(ref leftOrMonoEncoded, (leftSamplesToEncode.Length + 1) / 2);
            var rightArray = builder.Allocate(ref rightEncoded, (rightSamplesToEncode.Length + 1) / 2);

            ADPCMCodec.EncodeChannel(leftSamplesToEncode,  ref leftArray);
            ADPCMCodec.EncodeChannel(rightSamplesToEncode, ref rightArray);

            var decodedLeft  = context.threadStackAllocator.AllocateAsSpan<float>(leftSamplesToEncode.Length);
            var decodedRight = context.threadStackAllocator.AllocateAsSpan<float>(rightSamplesToEncode.Length);
            var imaLeft      = context.threadStackAllocator.AllocateAsSpan<ADPCMCodec.IMAState>(leftSamplesToEncode.Length);
            var imaRight     = context.threadStackAllocator.AllocateAsSpan<ADPCMCodec.IMAState>(rightSamplesToEncode.Length);

            ADPCMCodec.DecodeChannelWithIMAStates(new ReadOnlySpan<byte>(leftArray.GetUnsafePtr(), leftArray.Length),   ref decodedLeft,  ref imaLeft);
            ADPCMCodec.DecodeChannelWithIMAStates(new ReadOnlySpan<byte>(rightArray.GetUnsafePtr(), rightArray.Length), ref decodedRight, ref imaRight);
            signalToNoiseRatio = DSP.DspTools.CalculateSignalToNoiseRatio(leftSamplesToEncode, rightSamplesToEncode, decodedLeft, decodedRight);

            var seekL = builder.Allocate(ref seekLeftOrMono, (leftSamplesToEncode.Length + 1023) / 1024);
            var seekR = builder.Allocate(ref seekRight, (rightSamplesToEncode.Length + 1023) / 1024);
            for (int i = 0; i < seekL.Length; i++)
            {
                var imaL = imaLeft[1024 * i];
                seekL[i] = new Seek1024IMAState { previousSample = (short)imaL.previousSample, stepIndex = (short)imaL.stepIndex };
                var imaR = imaRight[1024 * i];
                seekR[i] = new Seek1024IMAState { previousSample = (short)imaR.previousSample, stepIndex = (short)imaR.stepIndex };
            }
        }

        public ReadOnlySpan<float> DecodeSingleChannel(int start, int count, bool rightChannel, ref CodecContext context)
        {
            var                decoded   = context.threadStackAllocator.AllocateAsSpan<float>(count);
            var                seekIndex = start / 1024;
            Seek1024IMAState   seekIma;
            ReadOnlySpan<byte> encoded;
            if (rightChannel)
            {
                seekIma = seekRight[seekIndex];
                encoded = rightEncoded.AsSpan();
            }
            else
            {
                seekIma = seekLeftOrMono[seekIndex];
                encoded = leftOrMonoEncoded.AsSpan();
            }

            var ima = new ADPCMCodec.IMAState { previousSample = seekIma.previousSample, stepIndex = seekIma.stepIndex };
            ADPCMCodec.DecodeChannel(encoded, ref decoded, start, ima, seekIndex * 1024);
            return decoded;
        }

        public StereoSamples DecodeBothChannels(int start, int count, ref CodecContext context)
        {
            return new StereoSamples {
                left  = DecodeSingleChannel(start, count, false, ref context),
                right = DecodeSingleChannel(start, count, true, ref context)
            };
        }
    }
}

