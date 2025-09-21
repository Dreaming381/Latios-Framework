using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Myri
{
    internal struct UncompressedCodecData
    {
        BlobArray<float> leftOrMono;
        BlobArray<float> right;

        public void Encode(ref BlobBuilder builder, ReadOnlySpan<float> monoSamplesToEncode, ref CodecContext context)
        {
            var array = builder.Allocate(ref leftOrMono, monoSamplesToEncode.Length, 16);
            for (int i = 0; i < monoSamplesToEncode.Length; i++)
                array[i] = monoSamplesToEncode[i];
        }

        public void Encode(ref BlobBuilder builder, ReadOnlySpan<float> leftSamplesToEncode, ReadOnlySpan<float> rightSamplesToEncode, ref CodecContext context)
        {
            var leftArray = builder.Allocate(ref leftOrMono, leftSamplesToEncode.Length, 16);
            for (int i = 0; i < leftSamplesToEncode.Length; i++)
                leftArray[i] = leftSamplesToEncode[i];
            var rightArray   = builder.Allocate(ref right, rightSamplesToEncode.Length, 16);
            for (int i = 0; i < rightSamplesToEncode.Length; i++)
                rightArray[i] = rightSamplesToEncode[i];
        }

        public ReadOnlySpan<float> DecodeSingleChannel(int start, int count, bool rightChannel)
        {
            if (rightChannel)
                return right.AsSpan().Slice(start, count);
            return leftOrMono.AsSpan().Slice(start, count);
        }

        public StereoSamples DecodeBothChannels(int start, int count)
        {
            return new StereoSamples { left = DecodeSingleChannel(start, count, false), right = DecodeSingleChannel(start, count, true) };
        }
    }
}

