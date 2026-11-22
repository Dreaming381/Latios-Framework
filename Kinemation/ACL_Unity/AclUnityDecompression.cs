#if !LATIOS_DISABLE_ACL
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

            SampleFloatsFromRaw(compressedFloatsClip, outputBuffer, time, keyframeInterpolationMode);
        }

        internal static void SampleFloatsFromRaw(void* compressedFloatByteArray, NativeArray<float> outputBuffer, float time, KeyframeInterpolationMode keyframeInterpolationMode)
        {
            if (X86.Avx2.IsAvx2Supported)
            {
                AVX.sampleFloats(compressedFloatByteArray, (float*)outputBuffer.GetUnsafePtr(), time, (byte)keyframeInterpolationMode);
            }
            else
            {
                NoExtensions.sampleFloats(compressedFloatByteArray, (float*)outputBuffer.GetUnsafePtr(), time, (byte)keyframeInterpolationMode);
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

        #region Deterministic
        // The Deterministic variants produce bit-identical results on every platform and CPU feature level for bit-identical inputs,
        // regardless of the caller's floating-point mode. They are slower than their regular counterparts, and their results are not
        // bit-identical to them. Denormal outputs are flushed to zero, because Burst's FloatMode.Deterministic treats denormal inputs
        // differently on x86 and ARM.

        /// <summary>
        /// Deterministic variant of SamplePose(). Outputs are bit-identical on all platforms, and denormals are flushed to zero.
        /// </summary>
        public static void SamplePoseDeterministic(void* compressedTransformsClip, NativeArray<Qvvs> outputBuffer, float time, KeyframeInterpolationMode keyframeInterpolationMode)
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

            var outputPtr = (float*)outputBuffer.GetUnsafePtr();
            if (X86.Avx2.IsAvx2Supported)
                AVX.samplePoseDeterministic(compressedTransformsClip, compressedScalesClip, outputPtr, time, (byte)keyframeInterpolationMode);
            else
                NoExtensions.samplePoseDeterministic(compressedTransformsClip, compressedScalesClip, outputPtr, time, (byte)keyframeInterpolationMode);
            FlushDenormalsToZero(outputPtr, header.trackCount * 12);
        }

        /// <summary>
        /// Deterministic variant of SamplePoseBlendedFirst(). Outputs are bit-identical on all platforms, and denormals are flushed to zero.
        /// </summary>
        public static void SamplePoseBlendedFirstDeterministic(void*                     compressedTransformsClip,
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

            var outputPtr = (float*)outputBuffer.GetUnsafePtr();
            if (X86.Avx2.IsAvx2Supported)
                AVX.samplePoseBlendedFirstDeterministic(compressedTransformsClip, compressedScalesClip, outputPtr, blendFactor, time, (byte)keyframeInterpolationMode);
            else
                NoExtensions.samplePoseBlendedFirstDeterministic(compressedTransformsClip, compressedScalesClip, outputPtr, blendFactor, time, (byte)keyframeInterpolationMode);
            FlushDenormalsToZero(outputPtr, header.trackCount * 12);
        }

        /// <summary>
        /// Deterministic variant of SamplePoseBlendedAdd(). Outputs are bit-identical on all platforms, and denormals are flushed to zero.
        /// </summary>
        public static void SamplePoseBlendedAddDeterministic(void*                     compressedTransformsClip,
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

            var outputPtr = (float*)outputBuffer.GetUnsafePtr();
            if (X86.Avx2.IsAvx2Supported)
                AVX.samplePoseBlendedAddDeterministic(compressedTransformsClip, compressedScalesClip, outputPtr, blendFactor, time, (byte)keyframeInterpolationMode);
            else
                NoExtensions.samplePoseBlendedAddDeterministic(compressedTransformsClip, compressedScalesClip, outputPtr, blendFactor, time, (byte)keyframeInterpolationMode);
            FlushDenormalsToZero(outputPtr, header.trackCount * 12);
        }

        /// <summary>
        /// Deterministic variant of SamplePoseMasked(). Outputs are bit-identical on all platforms, and denormals are flushed to zero.
        /// </summary>
        public static void SamplePoseMaskedDeterministic(void*                     compressedTransformsClip,
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

            var outputPtr = (float*)outputBuffer.GetUnsafePtr();
            fixed (ulong* maskPtr = &mask[0])
            {
                if (X86.Avx2.IsAvx2Supported)
                    AVX.samplePoseMaskedDeterministic(compressedTransformsClip, compressedScalesClip, outputPtr, maskPtr, time, (byte)keyframeInterpolationMode);
                else
                    NoExtensions.samplePoseMaskedDeterministic(compressedTransformsClip, compressedScalesClip, outputPtr, maskPtr, time, (byte)keyframeInterpolationMode);
                FlushDenormalsToZeroMasked(outputPtr, 12, maskPtr, header.trackCount);
            }
        }

        /// <summary>
        /// Deterministic variant of SamplePoseMaskedBlendedFirst(). Outputs are bit-identical on all platforms, and denormals are flushed to zero.
        /// </summary>
        public static void SamplePoseMaskedBlendedFirstDeterministic(void*                     compressedTransformsClip,
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

            var outputPtr = (float*)outputBuffer.GetUnsafePtr();
            fixed (ulong* maskPtr = &mask[0])
            {
                if (X86.Avx2.IsAvx2Supported)
                    AVX.samplePoseMaskedBlendedFirstDeterministic(compressedTransformsClip,
                                                                  compressedScalesClip,
                                                                  outputPtr,
                                                                  maskPtr,
                                                                  blendFactor,
                                                                  time,
                                                                  (byte)keyframeInterpolationMode);
                else
                    NoExtensions.samplePoseMaskedBlendedFirstDeterministic(compressedTransformsClip,
                                                                           compressedScalesClip,
                                                                           outputPtr,
                                                                           maskPtr,
                                                                           blendFactor,
                                                                           time,
                                                                           (byte)keyframeInterpolationMode);
                FlushDenormalsToZeroMasked(outputPtr, 12, maskPtr, header.trackCount);
            }
        }

        /// <summary>
        /// Deterministic variant of SamplePoseMaskedBlendedAdd(). Outputs are bit-identical on all platforms, and denormals are flushed to zero.
        /// </summary>
        public static void SamplePoseMaskedBlendedAddDeterministic(void*                     compressedTransformsClip,
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

            var outputPtr = (float*)outputBuffer.GetUnsafePtr();
            fixed (ulong* maskPtr = &mask[0])
            {
                if (X86.Avx2.IsAvx2Supported)
                    AVX.samplePoseMaskedBlendedAddDeterministic(compressedTransformsClip,
                                                                compressedScalesClip,
                                                                outputPtr,
                                                                maskPtr,
                                                                blendFactor,
                                                                time,
                                                                (byte)keyframeInterpolationMode);
                else
                    NoExtensions.samplePoseMaskedBlendedAddDeterministic(compressedTransformsClip,
                                                                         compressedScalesClip,
                                                                         outputPtr,
                                                                         maskPtr,
                                                                         blendFactor,
                                                                         time,
                                                                         (byte)keyframeInterpolationMode);
                FlushDenormalsToZeroMasked(outputPtr, 12, maskPtr, header.trackCount);
            }
        }

        /// <summary>
        /// Deterministic variant of SampleBone(). Outputs are bit-identical on all platforms, and denormals are flushed to zero.
        /// </summary>
        public static Qvvs SampleBoneDeterministic(void* compressedTransformsClip, int boneIndex, float time, KeyframeInterpolationMode keyframeInterpolationMode)
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
                AVX.sampleBoneDeterministic(compressedTransformsClip, compressedScalesClip, (float*)(&qvv), boneIndex, time, (byte)keyframeInterpolationMode);
            else
                NoExtensions.sampleBoneDeterministic(compressedTransformsClip, compressedScalesClip, (float*)(&qvv), boneIndex, time, (byte)keyframeInterpolationMode);
            FlushDenormalsToZero((float*)(&qvv), 12);

            return qvv;
        }

        /// <summary>
        /// Deterministic variant of SampleFloats(). Outputs are bit-identical on all platforms, and denormals are flushed to zero.
        /// </summary>
        public static void SampleFloatsDeterministic(void* compressedFloatsClip, NativeArray<float> outputBuffer, float time, KeyframeInterpolationMode keyframeInterpolationMode)
        {
            CheckCompressedClipIsValid(compressedFloatsClip);

            var header = ClipHeader.Read(compressedFloatsClip);

            if (header.clipType != ClipHeader.ClipType.Scalars)
            {
                ThrowIfWrongType();
            }

            CheckOutputArrayIsSufficient(outputBuffer, header.trackCount);

            compressedFloatsClip = (byte*)compressedFloatsClip + 16;

            var outputPtr = (float*)outputBuffer.GetUnsafePtr();
            if (X86.Avx2.IsAvx2Supported)
                AVX.sampleFloatsDeterministic(compressedFloatsClip, outputPtr, time, (byte)keyframeInterpolationMode);
            else
                NoExtensions.sampleFloatsDeterministic(compressedFloatsClip, outputPtr, time, (byte)keyframeInterpolationMode);
            FlushDenormalsToZero(outputPtr, header.trackCount);
        }

        /// <summary>
        /// Deterministic variant of SampleFloatsMasked(). Outputs are bit-identical on all platforms, and denormals are flushed to zero.
        /// </summary>
        public static void SampleFloatsMaskedDeterministic(void*                     compressedFloatsClip,
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

            var outputPtr = (float*)outputBuffer.GetUnsafePtr();
            fixed (ulong* maskPtr = &mask[0])
            {
                if (X86.Avx2.IsAvx2Supported)
                    AVX.sampleFloatsMaskedDeterministic(compressedFloatsClip, outputPtr, maskPtr, time, (byte)keyframeInterpolationMode);
                else
                    NoExtensions.sampleFloatsMaskedDeterministic(compressedFloatsClip, outputPtr, maskPtr, time, (byte)keyframeInterpolationMode);
                FlushDenormalsToZeroMasked(outputPtr, 1, maskPtr, header.trackCount);
            }
        }

        /// <summary>
        /// Deterministic variant of SampleFloat(). The output is bit-identical on all platforms, and a denormal is flushed to zero.
        /// </summary>
        public static float SampleFloatDeterministic(void* compressedFloatsClip, int trackIndex, float time, KeyframeInterpolationMode keyframeInterpolationMode)
        {
            CheckCompressedClipIsValid(compressedFloatsClip);

            var header = ClipHeader.Read(compressedFloatsClip);

            if (header.clipType != ClipHeader.ClipType.Scalars)
            {
                ThrowIfWrongType();
            }

            CheckIndexIsValid(trackIndex, header.trackCount);

            compressedFloatsClip = (byte*)compressedFloatsClip + 16;

            float result;
            if (X86.Avx2.IsAvx2Supported)
                result = AVX.sampleFloatDeterministic(compressedFloatsClip, trackIndex, time, (byte)keyframeInterpolationMode);
            else
                result = NoExtensions.sampleFloatDeterministic(compressedFloatsClip, trackIndex, time, (byte)keyframeInterpolationMode);
            FlushDenormalsToZero(&result, 1);
            return result;
        }

        // Integer operations, so that the result does not depend on the floating-point mode.
        static void FlushDenormalsToZero(float* values, int count)
        {
            var bits = (uint*)values;
            for (int i = 0; i < count; i++)
            {
                uint value = bits[i];
                bits[i]    = math.select(value, value & 0x80000000u, (value & 0x7f800000u) == 0u);
            }
        }

        static void FlushDenormalsToZeroMasked(float* values, int stride, ulong* mask, int trackCount)
        {
            for (int track = 0; track < trackCount; track++)
            {
                if ((mask[track >> 6] & (1ul << (track & 0x3f))) != 0)
                    FlushDenormalsToZero(values + track * stride, stride);
            }
        }
        #endregion

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        internal static void CheckCompressedClipIsValid(void* compressedClip)
        {
            if (compressedClip == null)
                throw new ArgumentNullException("compressedClip is null");
            if (!CollectionHelper.IsAligned(compressedClip, 16))
                throw new ArgumentException("compressedClip is not aligned to a 16 byte boundary");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        static void ThrowIfWrongType()
        {
            throw new ArgumentException("compressedClip is of the wrong type (skeleton vs scalar)");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        static void CheckOutputArrayIsSufficient(NativeArray<Qvvs> outputBuffer, short trackCount)
        {
            if (!outputBuffer.IsCreated || outputBuffer.Length == 0)
                throw new ArgumentException("outputBuffer is invalid");
            if (outputBuffer.Length < trackCount)
                throw new ArgumentException("outputBuffer does not contain enough elements");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        static void CheckOutputArrayIsSufficient(NativeArray<float> outputBuffer, short trackCount)
        {
            if (!outputBuffer.IsCreated || outputBuffer.Length == 0)
                throw new ArgumentException("outputBuffer is invalid");
            if (outputBuffer.Length < trackCount)
                throw new ArgumentException("outputBuffer does not contain enough elements");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        static void CheckMaskSpanIsSufficient(ReadOnlySpan<ulong> mask, short trackCount)
        {
            if (mask.IsEmpty)
                throw new ArgumentException("mask is invalid");
            if (mask.Length * 64 < trackCount)
                throw new ArgumentException("mask does not contain enough elements");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
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

            [DllImport(dllName)]
            public static extern void samplePoseDeterministic(void*  compressedTransformTracks,
                                                              void*  compressedScaleTracks,
                                                              float* aosOutputBuffer,
                                                              float time,
                                                              byte keyframeInterpolationMode);

            [DllImport(dllName)]
            public static extern void samplePoseBlendedFirstDeterministic(void*  compressedTransformTracks,
                                                                          void*  compressedScaleTracks,
                                                                          float* aosOutputBuffer,
                                                                          float blendFactor,
                                                                          float time,
                                                                          byte keyframeInterpolationMode);

            [DllImport(dllName)]
            public static extern void samplePoseBlendedAddDeterministic(void*  compressedTransformTracks,
                                                                        void*  compressedScaleTracks,
                                                                        float* aosOutputBuffer,
                                                                        float blendFactor,
                                                                        float time,
                                                                        byte keyframeInterpolationMode);

            [DllImport(dllName)]
            public static extern void samplePoseMaskedDeterministic(void*  compressedTransformTracks,
                                                                    void*  compressedScaleTracks,
                                                                    float* aosOutputBuffer,
                                                                    ulong* mask,
                                                                    float time,
                                                                    byte keyframeInterpolationMode);

            [DllImport(dllName)]
            public static extern void samplePoseMaskedBlendedFirstDeterministic(void*  compressedTransformTracks,
                                                                                void*  compressedScaleTracks,
                                                                                float* aosOutputBuffer,
                                                                                ulong* mask,
                                                                                float blendFactor,
                                                                                float time,
                                                                                byte keyframeInterpolationMode);

            [DllImport(dllName)]
            public static extern void samplePoseMaskedBlendedAddDeterministic(void*  compressedTransformTracks,
                                                                              void*  compressedScaleTracks,
                                                                              float* aosOutputBuffer,
                                                                              ulong* mask,
                                                                              float blendFactor,
                                                                              float time,
                                                                              byte keyframeInterpolationMode);

            [DllImport(dllName)]
            public static extern void sampleBoneDeterministic(void*  compressedTransformTracks,
                                                              void*  compressedScaleTracks,
                                                              float* boneQVV,
                                                              int boneIndex,
                                                              float time,
                                                              byte keyframeInterpolationMode);

            [DllImport(dllName)]
            public static extern void sampleFloatsDeterministic(void*  compressedFloatTracks,
                                                                float* floatOutputBuffer,
                                                                float time,
                                                                byte keyframeInterpolationMode);

            [DllImport(dllName)]
            public static extern void sampleFloatsMaskedDeterministic(void*  compressedFloatTracks,
                                                                      float* floatOutputBuffer,
                                                                      ulong* mask,
                                                                      float time,
                                                                      byte keyframeInterpolationMode);

            [DllImport(dllName)]
            public static extern float sampleFloatDeterministic(void* compressedFloatTracks,
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

            [DllImport(dllName)]
            public static extern void samplePoseDeterministic(void*  compressedTransformTracks,
                                                              void*  compressedScaleTracks,
                                                              float* aosOutputBuffer,
                                                              float time,
                                                              byte keyframeInterpolationMode);

            [DllImport(dllName)]
            public static extern void samplePoseBlendedFirstDeterministic(void*  compressedTransformTracks,
                                                                          void*  compressedScaleTracks,
                                                                          float* aosOutputBuffer,
                                                                          float blendFactor,
                                                                          float time,
                                                                          byte keyframeInterpolationMode);

            [DllImport(dllName)]
            public static extern void samplePoseBlendedAddDeterministic(void*  compressedTransformTracks,
                                                                        void*  compressedScaleTracks,
                                                                        float* aosOutputBuffer,
                                                                        float blendFactor,
                                                                        float time,
                                                                        byte keyframeInterpolationMode);

            [DllImport(dllName)]
            public static extern void samplePoseMaskedDeterministic(void*  compressedTransformTracks,
                                                                    void*  compressedScaleTracks,
                                                                    float* aosOutputBuffer,
                                                                    ulong* mask,
                                                                    float time,
                                                                    byte keyframeInterpolationMode);

            [DllImport(dllName)]
            public static extern void samplePoseMaskedBlendedFirstDeterministic(void*  compressedTransformTracks,
                                                                                void*  compressedScaleTracks,
                                                                                float* aosOutputBuffer,
                                                                                ulong* mask,
                                                                                float blendFactor,
                                                                                float time,
                                                                                byte keyframeInterpolationMode);

            [DllImport(dllName)]
            public static extern void samplePoseMaskedBlendedAddDeterministic(void*  compressedTransformTracks,
                                                                              void*  compressedScaleTracks,
                                                                              float* aosOutputBuffer,
                                                                              ulong* mask,
                                                                              float blendFactor,
                                                                              float time,
                                                                              byte keyframeInterpolationMode);

            [DllImport(dllName)]
            public static extern void sampleBoneDeterministic(void*  compressedTransformTracks,
                                                              void*  compressedScaleTracks,
                                                              float* boneQVV,
                                                              int boneIndex,
                                                              float time,
                                                              byte keyframeInterpolationMode);

            [DllImport(dllName)]
            public static extern void sampleFloatsDeterministic(void*  compressedFloatTracks,
                                                                float* floatOutputBuffer,
                                                                float time,
                                                                byte keyframeInterpolationMode);

            [DllImport(dllName)]
            public static extern void sampleFloatsMaskedDeterministic(void*  compressedFloatTracks,
                                                                      float* floatOutputBuffer,
                                                                      ulong* mask,
                                                                      float time,
                                                                      byte keyframeInterpolationMode);

            [DllImport(dllName)]
            public static extern float sampleFloatDeterministic(void* compressedFloatTracks,
                                                                int trackIndex,
                                                                float time,
                                                                byte keyframeInterpolationMode);
        }
    }
}
#endif

