using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Myri
{
    internal struct ADPCMCodecData
    {
        BlobArray<byte> leftOrMonoEncoded;
        BlobArray<byte> rightEncoded;

        public void Encode(ref BlobBuilder builder, ReadOnlySpan<float> monoSamplesToEncode, ref CodecContext context)
        {
            var array = builder.Allocate(ref leftOrMonoEncoded, (monoSamplesToEncode.Length + 1) / 2);

            ADPCMCodec.EncodeMonoChannel(monoSamplesToEncode, ref array, context.sampleRate);
            int originalDataSize = monoSamplesToEncode.Length;
            context.compressionRatio = GetRatio(originalDataSize, array.Length);
        }

        public void Encode(ref BlobBuilder builder, ReadOnlySpan<float> leftSamplesToEncode, ReadOnlySpan<float> rightSamplesToEncode, ref CodecContext context)
        {
            var leftArray = builder.Allocate(ref leftOrMonoEncoded, (leftSamplesToEncode.Length + 1) / 2);
            var rightArray   = builder.Allocate(ref rightEncoded, (leftSamplesToEncode.Length + 1) / 2);

            ADPCMCodec.EncodeStereoChannels(leftSamplesToEncode, rightSamplesToEncode, ref leftArray, ref rightArray, context.sampleRate);
            int originalDataSize = (leftSamplesToEncode.Length *2);
            context.compressionRatio = GetRatio(originalDataSize, (leftArray.Length + rightArray.Length));
        }

        public ReadOnlySpan<float> DecodeSingleChannel(int start, int count, bool rightChannel, ref CodecContext context)
        {
            var decoded = context.threadStackAllocator.AllocateAsSpan<float>(count);
            if (rightChannel)
                ADPCMCodec.DecodeMonoChannel(rightEncoded.AsSpan(), ref decoded, context.sampleRate, start);
            else
                ADPCMCodec.DecodeMonoChannel(leftOrMonoEncoded.AsSpan(), ref decoded, context.sampleRate, start);
            return decoded;
            
        }

        public StereoSamples DecodeBothChannels(int start, int count, ref CodecContext context)
        {
            return new StereoSamples { 
                left = DecodeSingleChannel(start, count, false, ref context),
                right = DecodeSingleChannel(start, count, true, ref context) 
            };
        }

        private float GetRatio(int originalDataSize, int decodedSize) => decodedSize > 0 || originalDataSize > 0 ? (originalDataSize * sizeof(float)) / decodedSize : 0f;
    }
}

