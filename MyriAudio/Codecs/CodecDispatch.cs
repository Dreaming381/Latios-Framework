using System;
using Latios.Unsafe;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace Latios.Myri
{
    public enum Codec : byte
    {
        Uncompressed,
        ADPCM,
    }

    // Note: Dispose is only required when created with a dspAllocator inside AudioECS.
    internal unsafe struct CodecContext : IDisposable
    {
        public ThreadStackAllocator             threadStackAllocator;
        public AllocatorManager.AllocatorHandle dspAllocator;
        UnsafeList<Allocation>                  allocations;
        public int                              sampleRate;

        struct Allocation
        {
            public void* ptr;
            public int   length;
            public int   elementSize;
            public int   alignment;
        }

        public Span<T> AllocateSpan<T>(int length) where T : unmanaged
        {
            if (threadStackAllocator.isCreated)
                return threadStackAllocator.AllocateAsSpan<T>(length);
            if (!allocations.IsCreated)
                allocations = new UnsafeList<Allocation>(8, dspAllocator);
            var ptr         = AllocatorManager.Allocate(dspAllocator, UnsafeUtility.SizeOf<T>(), UnsafeUtility.AlignOf<T>(), length);
            allocations.Add(new Allocation
            {
                ptr         = ptr,
                elementSize = UnsafeUtility.SizeOf<T>(),
                alignment   = UnsafeUtility.AlignOf<T>(),
                length      = length
            });
            return new Span<T>(ptr, length);
        }

        public void Dispose()
        {
            if (allocations.IsCreated)
            {
                foreach (var allocation in allocations)
                {
                    AllocatorManager.Free(dspAllocator, allocation.ptr, allocation.elementSize, allocation.alignment, allocation.length);
                }
                allocations.Dispose();
            }
        }
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

