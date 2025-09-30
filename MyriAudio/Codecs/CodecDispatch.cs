using System;
using Latios.Unsafe;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace Latios.Myri
{
    public enum Codec : byte
    {
        Uncompressed,
        ADPCM,
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
                {
                    ref var data = ref builder.Allocate(ref UnsafeUtility.As<BlobPtr<byte>, BlobPtr<UncompressedCodecData> >(ref codecStruct));
                    data.Encode(ref builder, monoSamplesToEncode, ref context);
                    break;
                }
                case Codec.ADPCM:
                {
                    ref var data = ref builder.Allocate(ref UnsafeUtility.As<BlobPtr<byte>, BlobPtr<ADPCMCodecData> >(ref codecStruct));
                    data.Encode(ref builder, monoSamplesToEncode, ref context);
                    break;
                }
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
                {
                    ref var data = ref builder.Allocate(ref UnsafeUtility.As<BlobPtr<byte>, BlobPtr<UncompressedCodecData> >(ref codecStruct));
                    data.Encode(ref builder, leftSamplesToEncode, rightSamplesToEncode, ref context);
                    break;
                }
                case Codec.ADPCM:
                {
                    ref var data = ref builder.Allocate(ref UnsafeUtility.As<BlobPtr<byte>, BlobPtr<ADPCMCodecData> >(ref codecStruct));
                    data.Encode(ref builder, leftSamplesToEncode, rightSamplesToEncode, ref context);
                    break;
                }
            }
        }

        public static ReadOnlySpan<float> DecodeMono(Codec codec, ref BlobPtr<byte> codecStruct, int startSample, int sampleCount, ref CodecContext context)
        {
            switch (codec)
            {
                case Codec.Uncompressed:
                {
                    ref var data = ref UnsafeUtility.As<BlobPtr<byte>, BlobPtr<UncompressedCodecData> >(ref codecStruct).Value;
                    return data.DecodeSingleChannel(startSample, sampleCount, false);
                }
                case Codec.ADPCM:
                {
                    ref var data = ref UnsafeUtility.As<BlobPtr<byte>, BlobPtr<ADPCMCodecData> >(ref codecStruct).Value;
                    return data.DecodeSingleChannel(startSample, sampleCount, false, ref context);
                }
            }
            return default;
        }

        public static ReadOnlySpan<float> DecodeChannel(Codec codec, ref BlobPtr<byte> codecStruct, bool rightChannel, int startSample, int sampleCount, ref CodecContext context)
        {
            switch (codec)
            {
                case Codec.Uncompressed:
                {
                    ref var data = ref UnsafeUtility.As<BlobPtr<byte>, BlobPtr<UncompressedCodecData> >(ref codecStruct).Value;
                    return data.DecodeSingleChannel(startSample, sampleCount, rightChannel);
                }
                case Codec.ADPCM:
                {
                    ref var data = ref UnsafeUtility.As<BlobPtr<byte>, BlobPtr<ADPCMCodecData> >(ref codecStruct).Value;
                    return data.DecodeSingleChannel(startSample, sampleCount, rightChannel, ref context);
                }
            }
            return default;
        }

        public static StereoSamples DecodeStereo(Codec codec, ref BlobPtr<byte> codecStruct, bool rightChannel, int startSample, int sampleCount, ref CodecContext context)
        {
            switch (codec)
            {
                case Codec.Uncompressed:
                {
                    ref var data = ref UnsafeUtility.As<BlobPtr<byte>, BlobPtr<UncompressedCodecData> >(ref codecStruct).Value;
                    return data.DecodeBothChannels(startSample, sampleCount);
                }
                case Codec.ADPCM:
                {
                    ref var data = ref UnsafeUtility.As<BlobPtr<byte>, BlobPtr<ADPCMCodecData> >(ref codecStruct).Value;
                    return data.DecodeBothChannels(startSample, sampleCount, ref context);
                }
            }
            return default;
        }

        public static float GetCompressionRatio(Codec codec, ref BlobPtr<byte> codecStruct)
        {
            switch (codec)
            {
                case Codec.Uncompressed:
                {
                    return 1f;
                }
                case Codec.ADPCM:
                {
                    // 4 bits per sample. The seek table is a single bit per 64 samples, so we don't count it.
                    return 8f;
                }
            }
            return default;
        }

        public static float GetSignalToNoiseRatio(Codec codec, ref BlobPtr<byte> codecStruct)
        {
            switch (codec)
            {
                case Codec.Uncompressed:
                {
                    return 1f;
                }
                case Codec.ADPCM:
                {
                    ref var data = ref UnsafeUtility.As<BlobPtr<byte>, BlobPtr<ADPCMCodecData> >(ref codecStruct).Value;
                    return data.signalToNoiseRatio;
                }
            }
            return default;
        }
    }
}

