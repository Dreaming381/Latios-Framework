using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

// Todo: Design public APIs for parameter blending.

namespace AclUnity
{
    public static unsafe class Decompression
    {
        public enum KeyframeInterpolationMode : byte
        {
            Interpolate = 0,
            Floor = 1,
            Ceil = 2,
            Nearest = 3
        }

        public static void SamplePose(void* compressedTransformsClip, NativeArray<Qvvs> outputBuffer, float time, KeyframeInterpolationMode keyframeInterpolationMode)
        {
            CheckCompressedClipIsValid(compressedTransformsClip);

            var header = ClipHeader.Read(compressedTransformsClip);

            if (header.clipType == ClipHeader.ClipType.Scalars)
            {
                ThrowIfWrongType();
            }

            CheckOutputArrayIsSufficient(outputBuffer, header.trackCount);

            compressedTransformsClip   = (byte*)compressedTransformsClip + 16;
            void* compressedScalesClip = header.clipType ==
                                         ClipHeader.ClipType.SkeletonWithUniformScales ? (byte*)compressedTransformsClip + header.offsetToUniformScalesStartInBytes : null;

            if (X86.Avx2.IsAvx2Supported)
            {
                AVX.samplePose(compressedTransformsClip, compressedScalesClip, (float*)outputBuffer.GetUnsafePtr(), time, (byte)keyframeInterpolationMode);
            }
            else
            {
                NoExtensions.samplePose(compressedTransformsClip, compressedScalesClip, (float*)outputBuffer.GetUnsafePtr(), time, (byte)keyframeInterpolationMode);
            }
        }

        public static void SamplePoseBlendedFirst(void*                     compressedTransformsClip,
                                                  NativeArray<Qvvs>         outputBuffer,
                                                  float blendFactor,
                                                  float time,
                                                  KeyframeInterpolationMode keyframeInterpolationMode)
        {
            CheckCompressedClipIsValid(compressedTransformsClip);

            var header = ClipHeader.Read(compressedTransformsClip);

            if (header.clipType == ClipHeader.ClipType.Scalars)
            {
                ThrowIfWrongType();
            }

            CheckOutputArrayIsSufficient(outputBuffer, header.trackCount);

            compressedTransformsClip   = (byte*)compressedTransformsClip + 16;
            void* compressedScalesClip = header.clipType ==
                                         ClipHeader.ClipType.SkeletonWithUniformScales ? (byte*)compressedTransformsClip + header.offsetToUniformScalesStartInBytes : null;

            if (X86.Avx2.IsAvx2Supported)
            {
                AVX.samplePoseBlendedFirst(compressedTransformsClip, compressedScalesClip, (float*)outputBuffer.GetUnsafePtr(), blendFactor, time, (byte)keyframeInterpolationMode);
            }
            else
            {
                NoExtensions.samplePoseBlendedFirst(compressedTransformsClip,
                                                    compressedScalesClip,
                                                    (float*)outputBuffer.GetUnsafePtr(),
                                                    blendFactor,
                                                    time,
                                                    (byte)keyframeInterpolationMode);
            }
        }

        public static void SamplePoseBlendedAdd(void*                     compressedTransformsClip,
                                                NativeArray<Qvvs>         outputBuffer,
                                                float blendFactor,
                                                float time,
                                                KeyframeInterpolationMode keyframeInterpolationMode)
        {
            CheckCompressedClipIsValid(compressedTransformsClip);

            var header = ClipHeader.Read(compressedTransformsClip);

            if (header.clipType == ClipHeader.ClipType.Scalars)
            {
                ThrowIfWrongType();
            }

            CheckOutputArrayIsSufficient(outputBuffer, header.trackCount);

            compressedTransformsClip   = (byte*)compressedTransformsClip + 16;
            void* compressedScalesClip = header.clipType ==
                                         ClipHeader.ClipType.SkeletonWithUniformScales ? (byte*)compressedTransformsClip + header.offsetToUniformScalesStartInBytes : null;

            if (X86.Avx2.IsAvx2Supported)
            {
                AVX.samplePoseBlendedAdd(compressedTransformsClip, compressedScalesClip, (float*)outputBuffer.GetUnsafePtr(), blendFactor, time, (byte)keyframeInterpolationMode);
            }
            else
            {
                NoExtensions.samplePoseBlendedAdd(compressedTransformsClip,
                                                  compressedScalesClip,
                                                  (float*)outputBuffer.GetUnsafePtr(),
                                                  blendFactor,
                                                  time,
                                                  (byte)keyframeInterpolationMode);
            }
        }

        public static void SamplePoseMasked(void*                     compressedTransformsClip,
                                            NativeArray<Qvvs>         outputBuffer,
                                            ReadOnlySpan<ulong>       mask,
                                            float time,
                                            KeyframeInterpolationMode keyframeInterpolationMode)
        {
            CheckCompressedClipIsValid(compressedTransformsClip);

            var header = ClipHeader.Read(compressedTransformsClip);

            if (header.clipType == ClipHeader.ClipType.Scalars)
            {
                ThrowIfWrongType();
            }

            CheckOutputArrayIsSufficient(outputBuffer, header.trackCount);
            CheckMaskSpanIsSufficient(mask, header.trackCount);

            compressedTransformsClip   = (byte*)compressedTransformsClip + 16;
            void* compressedScalesClip = header.clipType ==
                                         ClipHeader.ClipType.SkeletonWithUniformScales ? (byte*)compressedTransformsClip + header.offsetToUniformScalesStartInBytes : null;
            fixed (ulong* maskPtr = &mask[0])
            {
                if (X86.Avx2.IsAvx2Supported)
                {
                    AVX.samplePoseMasked(compressedTransformsClip, compressedScalesClip, (float*)outputBuffer.GetUnsafePtr(), maskPtr, time, (byte)keyframeInterpolationMode);
                }
                else
                {
                    NoExtensions.samplePoseMasked(compressedTransformsClip,
                                                  compressedScalesClip,
                                                  (float*)outputBuffer.GetUnsafePtr(),
                                                  maskPtr,
                                                  time,
                                                  (byte)keyframeInterpolationMode);
                }
            }
        }

        public static void SamplePoseMaskedBlendedFirst(void*                     compressedTransformsClip,
                                                        NativeArray<Qvvs>         outputBuffer,
                                                        ReadOnlySpan<ulong>       mask,
                                                        float blendFactor,
                                                        float time,
                                                        KeyframeInterpolationMode keyframeInterpolationMode)
        {
            CheckCompressedClipIsValid(compressedTransformsClip);

            var header = ClipHeader.Read(compressedTransformsClip);

            if (header.clipType == ClipHeader.ClipType.Scalars)
            {
                ThrowIfWrongType();
            }

            CheckOutputArrayIsSufficient(outputBuffer, header.trackCount);
            CheckMaskSpanIsSufficient(mask, header.trackCount);

            compressedTransformsClip   = (byte*)compressedTransformsClip + 16;
            void* compressedScalesClip = header.clipType ==
                                         ClipHeader.ClipType.SkeletonWithUniformScales ? (byte*)compressedTransformsClip + header.offsetToUniformScalesStartInBytes : null;

            fixed (ulong* maskPtr = &mask[0])
            {
                if (X86.Avx2.IsAvx2Supported)
                {
                    AVX.samplePoseMaskedBlendedFirst(compressedTransformsClip,
                                                     compressedScalesClip,
                                                     (float*)outputBuffer.GetUnsafePtr(),
                                                     maskPtr,
                                                     blendFactor,
                                                     time,
                                                     (byte)keyframeInterpolationMode);
                }
                else
                {
                    NoExtensions.samplePoseMaskedBlendedFirst(compressedTransformsClip,
                                                              compressedScalesClip,
                                                              (float*)outputBuffer.GetUnsafePtr(),
                                                              maskPtr,
                                                              blendFactor,
                                                              time,
                                                              (byte)keyframeInterpolationMode);
                }
            }
        }

        public static void SamplePoseMaskedBlendedAdd(void*                     compressedTransformsClip,
                                                      NativeArray<Qvvs>         outputBuffer,
                                                      ReadOnlySpan<ulong>       mask,
                                                      float blendFactor,
                                                      float time,
                                                      KeyframeInterpolationMode keyframeInterpolationMode)
        {
            CheckCompressedClipIsValid(compressedTransformsClip);

            var header = ClipHeader.Read(compressedTransformsClip);

            if (header.clipType == ClipHeader.ClipType.Scalars)
            {
                ThrowIfWrongType();
            }

            CheckOutputArrayIsSufficient(outputBuffer, header.trackCount);
            CheckMaskSpanIsSufficient(mask, header.trackCount);

            compressedTransformsClip   = (byte*)compressedTransformsClip + 16;
            void* compressedScalesClip = header.clipType ==
                                         ClipHeader.ClipType.SkeletonWithUniformScales ? (byte*)compressedTransformsClip + header.offsetToUniformScalesStartInBytes : null;

            fixed (ulong* maskPtr = &mask[0])
            {
                if (X86.Avx2.IsAvx2Supported)
                {
                    AVX.samplePoseMaskedBlendedAdd(compressedTransformsClip,
                                                   compressedScalesClip,
                                                   (float*)outputBuffer.GetUnsafePtr(),
                                                   maskPtr,
                                                   blendFactor,
                                                   time,
                                                   (byte)keyframeInterpolationMode);
                }
                else
                {
                    NoExtensions.samplePoseMaskedBlendedAdd(compressedTransformsClip,
                                                            compressedScalesClip,
                                                            (float*)outputBuffer.GetUnsafePtr(),
                                                            maskPtr,
                                                            blendFactor,
                                                            time,
                                                            (byte)keyframeInterpolationMode);
                }
            }
        }

        public static Qvvs SampleBone(void* compressedTransformsClip, int boneIndex, float time, KeyframeInterpolationMode keyframeInterpolationMode)
        {
            CheckCompressedClipIsValid(compressedTransformsClip);

            var header = ClipHeader.Read(compressedTransformsClip);

            if (header.clipType == ClipHeader.ClipType.Scalars)
            {
                ThrowIfWrongType();
            }

            CheckIndexIsValid(boneIndex, header.trackCount);

            compressedTransformsClip   = (byte*)compressedTransformsClip + 16;
            void* compressedScalesClip = header.clipType ==
                                         ClipHeader.ClipType.SkeletonWithUniformScales ? (byte*)compressedTransformsClip + header.offsetToUniformScalesStartInBytes : null;

            Qvvs qvv;

            if (X86.Avx2.IsAvx2Supported)
            {
                AVX.sampleBone(compressedTransformsClip, compressedScalesClip, (float*)(&qvv), boneIndex, time, (byte)keyframeInterpolationMode);
            }
            else
            {
                NoExtensions.sampleBone(compressedTransformsClip, compressedScalesClip, (float*)(&qvv), boneIndex, time, (byte)keyframeInterpolationMode);
            }

            return qvv;
        }

        public static void SampleFloats(void* compressedFloatsClip, NativeArray<float> outputBuffer, float time, KeyframeInterpolationMode keyframeInterpolationMode)
        {
            CheckCompressedClipIsValid(compressedFloatsClip);

            var header = ClipHeader.Read(compressedFloatsClip);

            if (header.clipType != ClipHeader.ClipType.Scalars)
            {
                ThrowIfWrongType();
            }

            CheckOutputArrayIsSufficient(outputBuffer, header.trackCount);

            compressedFloatsClip = (byte*)compressedFloatsClip + 16;

            if (X86.Avx2.IsAvx2Supported)
            {
                AVX.sampleFloats(compressedFloatsClip, (float*)outputBuffer.GetUnsafePtr(), time, (byte)keyframeInterpolationMode);
            }
            else
            {
                NoExtensions.sampleFloats(compressedFloatsClip, (float*)outputBuffer.GetUnsafePtr(), time, (byte)keyframeInterpolationMode);
            }
        }

        public static void SampleFloatsMasked(void*                     compressedFloatsClip,
                                              NativeArray<float>        outputBuffer,
                                              ReadOnlySpan<ulong>       mask,
                                              float time,
                                              KeyframeInterpolationMode keyframeInterpolationMode)
        {
            CheckCompressedClipIsValid(compressedFloatsClip);

            var header = ClipHeader.Read(compressedFloatsClip);

            if (header.clipType != ClipHeader.ClipType.Scalars)
            {
                ThrowIfWrongType();
            }

            CheckOutputArrayIsSufficient(outputBuffer, header.trackCount);
            CheckMaskSpanIsSufficient(mask, header.trackCount);

            compressedFloatsClip = (byte*)compressedFloatsClip + 16;

            fixed (ulong* maskPtr = &mask[0])
            {
                if (X86.Avx2.IsAvx2Supported)
                {
                    AVX.sampleFloatsMasked(compressedFloatsClip, (float*)outputBuffer.GetUnsafePtr(), maskPtr, time, (byte)keyframeInterpolationMode);
                }
                else
                {
                    NoExtensions.sampleFloatsMasked(compressedFloatsClip, (float*)outputBuffer.GetUnsafePtr(), maskPtr, time, (byte)keyframeInterpolationMode);
                }
            }
        }

        public static float SampleFloat(void* compressedFloatsClip, int trackIndex, float time, KeyframeInterpolationMode keyframeInterpolationMode)
        {
            CheckCompressedClipIsValid(compressedFloatsClip);

            var header = ClipHeader.Read(compressedFloatsClip);

            if (header.clipType != ClipHeader.ClipType.Scalars)
            {
                ThrowIfWrongType();
            }

            CheckIndexIsValid(trackIndex, header.trackCount);

            compressedFloatsClip = (byte*)compressedFloatsClip + 16;

            if (X86.Avx2.IsAvx2Supported)
            {
                return AVX.sampleFloat(compressedFloatsClip, trackIndex, time, (byte)keyframeInterpolationMode);
            }
            else
            {
                return NoExtensions.sampleFloat(compressedFloatsClip, trackIndex, time, (byte)keyframeInterpolationMode);
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        internal static void CheckCompressedClipIsValid(void* compressedClip)
        {
            if (compressedClip == null)
                throw new ArgumentNullException("compressedClip is null");
            if (!CollectionHelper.IsAligned(compressedClip, 16))
                throw new ArgumentException("compressedClip is not aligned to a 16 byte boundary");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void ThrowIfWrongType()
        {
            throw new ArgumentException("compressedClip is of the wrong type (skeleton vs scalar)");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void CheckOutputArrayIsSufficient(NativeArray<Qvvs> outputBuffer, short trackCount)
        {
            if (!outputBuffer.IsCreated || outputBuffer.Length == 0)
                throw new ArgumentException("outputBuffer is invalid");
            if (outputBuffer.Length < trackCount)
                throw new ArgumentException("outputBuffer does not contain enough elements");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void CheckOutputArrayIsSufficient(NativeArray<float> outputBuffer, short trackCount)
        {
            if (!outputBuffer.IsCreated || outputBuffer.Length == 0)
                throw new ArgumentException("outputBuffer is invalid");
            if (outputBuffer.Length < trackCount)
                throw new ArgumentException("outputBuffer does not contain enough elements");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void CheckMaskSpanIsSufficient(ReadOnlySpan<ulong> mask, short trackCount)
        {
            if (mask.IsEmpty)
                throw new ArgumentException("mask is invalid");
            if (mask.Length * 64 < trackCount)
                throw new ArgumentException("mask does not contain enough elements");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void CheckIndexIsValid(int index, short trackCount)
        {
            if (index < 0)
                throw new ArgumentOutOfRangeException("Bone or track index is negative");
            if (index >= trackCount)
                throw new ArgumentOutOfRangeException("Bone or track index exceeds bone or track count in clip");
        }

        static class NoExtensions
        {
            const string dllName = AclUnityCommon.dllName;

            [DllImport(dllName)]
            public static extern void samplePose(void*  compressedTransformTracks,
                                                 void*  compressedScaleTracks,
                                                 float* aosOutputBuffer,
                                                 float time,
                                                 byte keyframeInterpolationMode);

            [DllImport(dllName)]
            public static extern void samplePoseBlendedFirst(void*  compressedTransformTracks,
                                                             void*  compressedScaleTracks,
                                                             float* aosOutputBuffer,
                                                             float blendFactor,
                                                             float time,
                                                             byte keyframeInterpolationMode);

            [DllImport(dllName)]
            public static extern void samplePoseBlendedAdd(void*  compressedTransformTracks,
                                                           void*  compressedScaleTracks,
                                                           float* aosOutputBuffer,
                                                           float blendFactor,
                                                           float time,
                                                           byte keyframeInterpolationMode);

            [DllImport(dllName)]
            public static extern void samplePoseMasked(void*  compressedTransformTracks,
                                                       void*  compressedScaleTracks,
                                                       float* aosOutputBuffer,
                                                       ulong* mask,
                                                       float time,
                                                       byte keyframeInterpolationMode);

            [DllImport(dllName)]
            public static extern void samplePoseMaskedBlendedFirst(void*  compressedTransformTracks,
                                                                   void*  compressedScaleTracks,
                                                                   float* aosOutputBuffer,
                                                                   ulong* mask,
                                                                   float blendFactor,
                                                                   float time,
                                                                   byte keyframeInterpolationMode);

            [DllImport(dllName)]
            public static extern void samplePoseMaskedBlendedAdd(void*  compressedTransformTracks,
                                                                 void*  compressedScaleTracks,
                                                                 float* aosOutputBuffer,
                                                                 ulong* mask,
                                                                 float blendFactor,
                                                                 float time,
                                                                 byte keyframeInterpolationMode);

            [DllImport(dllName)]
            public static extern void sampleBone(void*  compressedTransformTracks,
                                                 void*  compressedScaleTracks,
                                                 float* boneQVV,
                                                 int boneIndex,
                                                 float time,
                                                 byte keyframeInterpolationMode);

            [DllImport(dllName)]
            public static extern void sampleFloats(void*  compressedFloatTracks,
                                                   float* floatOutputBuffer,
                                                   float time,
                                                   byte keyframeInterpolationMode);

            [DllImport(dllName)]
            public static extern void sampleFloatsBlendedFirst(void*  compressedFloatTracks,
                                                               float* floatOutputBuffer,
                                                               float blendFactor,
                                                               float time,
                                                               byte keyframeInterpolationMode);

            [DllImport(dllName)]
            public static extern void sampleFloatsBlendedAdd(void*  compressedFloatTracks,
                                                             float* floatOutputBuffer,
                                                             float blendFactor,
                                                             float time,
                                                             byte keyframeInterpolationMode);

            [DllImport(dllName)]
            public static extern void sampleFloatsMasked(void*  compressedFloatTracks,
                                                         float* floatOutputBuffer,
                                                         ulong* mask,
                                                         float time,
                                                         byte keyframeInterpolationMode);

            [DllImport(dllName)]
            public static extern void sampleFloatsMaskedBlendedFirst(void*  compressedFloatTracks,
                                                                     float* floatOutputBuffer,
                                                                     ulong* mask,
                                                                     float blendFactor,
                                                                     float time,
                                                                     byte keyframeInterpolationMode);

            [DllImport(dllName)]
            public static extern void sampleFloatsMaskedBlendedAdd(void*  compressedFloatTracks,
                                                                   float* floatOutputBuffer,
                                                                   ulong* mask,
                                                                   float blendFactor,
                                                                   float time,
                                                                   byte keyframeInterpolationMode);

            [DllImport(dllName)]
            public static extern float sampleFloat(void* compressedFloatTracks,
                                                   int trackIndex,
                                                   float time,
                                                   byte keyframeInterpolationMode);
        }

        static class AVX
        {
            const string dllName = AclUnityCommon.dllNameAVX;

            [DllImport(dllName)]
            public static extern void samplePose(void*  compressedTransformTracks,
                                                 void*  compressedScaleTracks,
                                                 float* aosOutputBuffer,
                                                 float time,
                                                 byte keyframeInterpolationMode);

            [DllImport(dllName)]
            public static extern void samplePoseBlendedFirst(void*  compressedTransformTracks,
                                                             void*  compressedScaleTracks,
                                                             float* aosOutputBuffer,
                                                             float blendFactor,
                                                             float time,
                                                             byte keyframeInterpolationMode);

            [DllImport(dllName)]
            public static extern void samplePoseBlendedAdd(void*  compressedTransformTracks,
                                                           void*  compressedScaleTracks,
                                                           float* aosOutputBuffer,
                                                           float blendFactor,
                                                           float time,
                                                           byte keyframeInterpolationMode);

            [DllImport(dllName)]
            public static extern void samplePoseMasked(void*  compressedTransformTracks,
                                                       void*  compressedScaleTracks,
                                                       float* aosOutputBuffer,
                                                       ulong* mask,
                                                       float time,
                                                       byte keyframeInterpolationMode);

            [DllImport(dllName)]
            public static extern void samplePoseMaskedBlendedFirst(void*  compressedTransformTracks,
                                                                   void*  compressedScaleTracks,
                                                                   float* aosOutputBuffer,
                                                                   ulong* mask,
                                                                   float blendFactor,
                                                                   float time,
                                                                   byte keyframeInterpolationMode);

            [DllImport(dllName)]
            public static extern void samplePoseMaskedBlendedAdd(void*  compressedTransformTracks,
                                                                 void*  compressedScaleTracks,
                                                                 float* aosOutputBuffer,
                                                                 ulong* mask,
                                                                 float blendFactor,
                                                                 float time,
                                                                 byte keyframeInterpolationMode);

            [DllImport(dllName)]
            public static extern void sampleBone(void*  compressedTransformTracks,
                                                 void*  compressedScaleTracks,
                                                 float* boneQVV,
                                                 int boneIndex,
                                                 float time,
                                                 byte keyframeInterpolationMode);

            [DllImport(dllName)]
            public static extern void sampleFloats(void*  compressedFloatTracks,
                                                   float* floatOutputBuffer,
                                                   float time,
                                                   byte keyframeInterpolationMode);

            [DllImport(dllName)]
            public static extern void sampleFloatsBlendedFirst(void*  compressedFloatTracks,
                                                               float* floatOutputBuffer,
                                                               float blendFactor,
                                                               float time,
                                                               byte keyframeInterpolationMode);

            [DllImport(dllName)]
            public static extern void sampleFloatsBlendedAdd(void*  compressedFloatTracks,
                                                             float* floatOutputBuffer,
                                                             float blendFactor,
                                                             float time,
                                                             byte keyframeInterpolationMode);

            [DllImport(dllName)]
            public static extern void sampleFloatsMasked(void*  compressedFloatTracks,
                                                         float* floatOutputBuffer,
                                                         ulong* mask,
                                                         float time,
                                                         byte keyframeInterpolationMode);

            [DllImport(dllName)]
            public static extern void sampleFloatsMaskedBlendedFirst(void*  compressedFloatTracks,
                                                                     float* floatOutputBuffer,
                                                                     ulong* mask,
                                                                     float blendFactor,
                                                                     float time,
                                                                     byte keyframeInterpolationMode);

            [DllImport(dllName)]
            public static extern void sampleFloatsMaskedBlendedAdd(void*  compressedFloatTracks,
                                                                   float* floatOutputBuffer,
                                                                   ulong* mask,
                                                                   float blendFactor,
                                                                   float time,
                                                                   byte keyframeInterpolationMode);

            [DllImport(dllName)]
            public static extern float sampleFloat(void* compressedFloatTracks,
                                                   int trackIndex,
                                                   float time,
                                                   byte keyframeInterpolationMode);
        }
    }
}

