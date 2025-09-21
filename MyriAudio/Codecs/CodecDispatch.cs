using System;
using Latios.Unsafe;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Myri
{
    public enum Codec : byte
    {
        Uncompressed,
    }

    internal struct CodecContext
    {
        public ThreadStackAllocator threadStackAllocator;
        public int                  sampleRate;
    }

    internal ref struct StereoSamples
    {
        public ReadOnlySpan<float> left;
        public ReadOnlySpan<float> right;
    }

    internal static class CodecDispatch
    {
        public static void Encode(Codec codec, ref BlobBuilder builder, ref BlobPtr<byte> codecStruct, Span<float> monoSamplesToEncode, ref CodecContext context)
        {
            switch (codec)
            {
                case Codec.Uncompressed:
                    ref var data = ref builder.Allocate(ref UnsafeUtility.As<BlobPtr<byte>, BlobPtr<UncompressedCodecData> >(ref codecStruct));
                    data.Encode(ref builder, monoSamplesToEncode, ref context);
                    break;
            }
        }

        public static void Encode(Codec codec,
                                  ref BlobBuilder builder,
                                  ref BlobPtr<byte> codecStruct,
                                  Span<float>       leftSamplesToEncode,
                                  Span<float>       rightSamplesToEncode,
                                  ref CodecContext context)
        {
            switch (codec)
            {
                case Codec.Uncompressed:
                    ref var data = ref builder.Allocate(ref UnsafeUtility.As<BlobPtr<byte>, BlobPtr<UncompressedCodecData> >(ref codecStruct));
                    data.Encode(ref builder, leftSamplesToEncode, rightSamplesToEncode, ref context);
                    break;
            }
        }

        public static ReadOnlySpan<float> DecodeMono(Codec codec, ref BlobPtr<byte> codecStruct, int startSample, int sampleCount, ref CodecContext context)
        {
            switch (codec)
            {
                case Codec.Uncompressed:
                    ref var data = ref UnsafeUtility.As<BlobPtr<byte>, BlobPtr<UncompressedCodecData> >(ref codecStruct).Value;
                    return data.DecodeSingleChannel(startSample, sampleCount, false);
            }
            return default;
        }

        public static ReadOnlySpan<float> DecodeChannel(Codec codec, ref BlobPtr<byte> codecStruct, bool rightChannel, int startSample, int sampleCount, ref CodecContext context)
        {
            switch (codec)
            {
                case Codec.Uncompressed:
                    ref var data = ref UnsafeUtility.As<BlobPtr<byte>, BlobPtr<UncompressedCodecData> >(ref codecStruct).Value;
                    return data.DecodeSingleChannel(startSample, sampleCount, rightChannel);
            }
            return default;
        }

        public static StereoSamples DecodeStereo(Codec codec, ref BlobPtr<byte> codecStruct, bool rightChannel, int startSample, int sampleCount, ref CodecContext context)
        {
            switch (codec)
            {
                case Codec.Uncompressed:
                    ref var data = ref UnsafeUtility.As<BlobPtr<byte>, BlobPtr<UncompressedCodecData> >(ref codecStruct).Value;
                    return data.DecodeBothChannels(startSample, sampleCount);
            }
            return default;
        }
    }
}

