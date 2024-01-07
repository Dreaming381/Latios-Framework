using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace AclUnity
{
    public static unsafe class Compression
    {
        public struct AclCompressedClipResult : IDisposable
        {
            internal ClipHeader        header;
            internal NativeArray<byte> compressedData;
            internal NativeArray<byte> extraCompressedData;

            public int sizeInBytes => compressedData.Length != 0 ? 16 + compressedData.Length + extraCompressedData.Length : 0;

            public void CopyTo(NativeArray<byte> arrayToCopyTo)
            {
                CheckArraySufficient(arrayToCopyTo);
                CopyTo((byte*)arrayToCopyTo.GetUnsafePtr());
            }

            public unsafe void CopyTo(byte* ptr)
            {
                var headerPtr = (ClipHeader*)ptr;
                *headerPtr    = header;
                var firstPtr  = (byte*)(headerPtr + 1);
                UnsafeUtility.MemCpy(firstPtr, compressedData.GetUnsafeReadOnlyPtr(), compressedData.Length);
                if (extraCompressedData.Length != 0)
                {
                    UnsafeUtility.MemCpy(firstPtr + header.offsetToUniformScalesStartInBytes, extraCompressedData.GetUnsafeReadOnlyPtr(), extraCompressedData.Length);
                }
            }

            public void Dispose()
            {
                DisposeCompressedTrack(this);
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            void CheckArraySufficient(NativeArray<byte> array)
            {
                if (!array.IsCreated)
                    throw new ArgumentException("The NativeArray is not valid");
                if (array.Length < sizeInBytes)
                    throw new ArgumentException("The NativeArray does not contain enough elements for the compressed clip");
            }
        }

        public struct SkeletonCompressionSettings
        {
            public short compressionLevel;
            public float maxDistanceError;
            public float sampledErrorDistanceFromBone;
            public float maxUniformScaleError;
        }

        public static readonly SkeletonCompressionSettings kDefaultSettings = new SkeletonCompressionSettings
        {
            compressionLevel             = 2,
            maxDistanceError             = 0.0001f,
            sampledErrorDistanceFromBone = 0.03f,
            maxUniformScaleError         = 0.00001f
        };

        public static AclCompressedClipResult CompressSkeletonClip(NativeArray<short>          parentIndices,
                                                                   NativeArray<Qvvs>           aosClipData,
                                                                   float sampleRate,
                                                                   SkeletonCompressionSettings settings
                                                                   )
        {
            CheckParentIndicesIsValid(parentIndices);
            CheckClipDataIsValid(aosClipData, parentIndices.Length);
            CheckSampleRateIsValid(sampleRate);
            CheckSkeletonSettingsIsValid(settings);

            var alignedClipData = (float*)aosClipData.GetUnsafeReadOnlyPtr();
            if (!CollectionHelper.IsAligned(alignedClipData, 16))
            {
                alignedClipData = (float*)UnsafeUtility.MallocTracked(UnsafeUtility.SizeOf<Qvvs>() * aosClipData.Length,
                                                                      math.max(UnsafeUtility.AlignOf<Qvvs>(), 16),
                                                                      Allocator.TempJob,
                                                                      0);
                UnsafeUtility.MemCpy(alignedClipData, aosClipData.GetUnsafeReadOnlyPtr(), UnsafeUtility.SizeOf<Qvvs>() * aosClipData.Length);
            }

            int               outCompressedSizeInBytes = 0;
            void*             compressedClipPtr;
            NativeArray<byte> compressedScales = default;
            float*            sampledScales    = null;

            // Todo: Maybe do this in AclUnity so that all heavy algorithms run native without the need for Burst?
            bool needsScales = false;
            foreach (var data in aosClipData)
            {
                needsScales |= data.stretchScale.w != 1f;
            }

            if (needsScales)
            {
                // Todo: This can be pretty heavy for Allocator.Temp. Use Malloc and Convert instead?
                var alignedScaleData = new NativeArray<float>(aosClipData.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                // Todo: Convert these loops into UnsafeUtility calls once the needsScales loop is native.
                for (int i = 0; i < aosClipData.Length; i++)
                    alignedScaleData[i] = aosClipData[i].stretchScale.w;
                var errors              = new NativeArray<float>(parentIndices.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                for (int i = 0; i < parentIndices.Length; i++)
                    errors[i]    = settings.maxUniformScaleError;
                var scalesResult = CompressScalarsClip(alignedScaleData, errors, sampleRate, settings.compressionLevel);
                compressedScales = scalesResult.compressedData;
                float timeStep   = math.rcp(sampleRate);
                float sampleTime = 0f;
                for (int i = 0; i < alignedScaleData.Length; i += parentIndices.Length)
                {
                    var scaleSampleSubArray = alignedScaleData.GetSubArray(i, parentIndices.Length);
                    Decompression.SampleFloats(compressedScales.GetUnsafeReadOnlyPtr(), scaleSampleSubArray, sampleTime, Decompression.KeyframeInterpolationMode.Nearest);
                    sampleTime += timeStep;
                }
                sampledScales = (float*)alignedScaleData.GetUnsafeReadOnlyPtr();
            }

            if (X86.Avx2.IsAvx2Supported)
            {
                compressedClipPtr = AVX.compressSkeletonClip((short*)parentIndices.GetUnsafeReadOnlyPtr(),
                                                             (short)parentIndices.Length,
                                                             settings.compressionLevel,
                                                             alignedClipData,
                                                             aosClipData.Length / parentIndices.Length,
                                                             sampleRate,
                                                             settings.maxDistanceError,
                                                             settings.sampledErrorDistanceFromBone,
                                                             &outCompressedSizeInBytes,
                                                             sampledScales
                                                             );
            }
            else
            {
                compressedClipPtr = NoExtensions.compressSkeletonClip((short*)parentIndices.GetUnsafeReadOnlyPtr(),
                                                                      (short)parentIndices.Length,
                                                                      settings.compressionLevel,
                                                                      alignedClipData,
                                                                      aosClipData.Length / parentIndices.Length,
                                                                      sampleRate,
                                                                      settings.maxDistanceError,
                                                                      settings.sampledErrorDistanceFromBone,
                                                                      &outCompressedSizeInBytes,
                                                                      sampledScales
                                                                      );
            }

            if (aosClipData.GetUnsafeReadOnlyPtr() != alignedClipData)
            {
                UnsafeUtility.Free(alignedClipData, Allocator.TempJob);
            }

            var resultArray = CollectionHelper.ConvertExistingDataToNativeArray<byte>(compressedClipPtr, outCompressedSizeInBytes, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var safety = CollectionHelper.CreateSafetyHandle(Allocator.Persistent);
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref resultArray, safety);
#endif

            return new AclCompressedClipResult
            {
                header = new ClipHeader
                {
                    clipType                          = needsScales ? ClipHeader.ClipType.SkeletonWithUniformScales : ClipHeader.ClipType.Skeleton,
                    duration                          = math.rcp(sampleRate) * ((aosClipData.Length / parentIndices.Length) - 1),
                    sampleRate                        = sampleRate,
                    trackCount                        = (short)parentIndices.Length,
                    offsetToUniformScalesStartInBytes = (uint)(needsScales ? CollectionHelper.Align(resultArray.Length, 16) : 0)
                },
                compressedData      = resultArray,
                extraCompressedData = compressedScales
            };
        }

        public static AclCompressedClipResult CompressScalarsClip(NativeArray<float> clipData,
                                                                  NativeArray<float> maxErrorsByTrack,
                                                                  float sampleRate,
                                                                  short compressionLevel
                                                                  )
        {
            CheckErrorsByTrackIsValid(maxErrorsByTrack);
            CheckClipDataIsValid(clipData, maxErrorsByTrack.Length);
            CheckSampleRateIsValid(sampleRate);
            CheckCompressionLevelIsValid(compressionLevel);

            int   outCompressedSizeInBytes = 0;
            void* compressedClipPtr;

            if (X86.Avx2.IsAvx2Supported)
            {
                compressedClipPtr = AVX.compressScalarsClip((short)maxErrorsByTrack.Length,
                                                            compressionLevel,
                                                            (float*)clipData.GetUnsafeReadOnlyPtr(),
                                                            clipData.Length / maxErrorsByTrack.Length,
                                                            sampleRate,
                                                            (float*)maxErrorsByTrack.GetUnsafeReadOnlyPtr(),
                                                            &outCompressedSizeInBytes);
            }
            else
            {
                compressedClipPtr = NoExtensions.compressScalarsClip((short)maxErrorsByTrack.Length,
                                                                     compressionLevel,
                                                                     (float*)clipData.GetUnsafeReadOnlyPtr(),
                                                                     clipData.Length / maxErrorsByTrack.Length,
                                                                     sampleRate,
                                                                     (float*)maxErrorsByTrack.GetUnsafeReadOnlyPtr(),
                                                                     &outCompressedSizeInBytes);
            }

            var resultArray = CollectionHelper.ConvertExistingDataToNativeArray<byte>(compressedClipPtr, outCompressedSizeInBytes, Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var safety = CollectionHelper.CreateSafetyHandle(Allocator.Persistent);
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref resultArray, safety);
#endif

            return new AclCompressedClipResult
            {
                header = new ClipHeader
                {
                    clipType                          = ClipHeader.ClipType.Scalars,
                    duration                          = math.rcp(sampleRate) * ((clipData.Length / maxErrorsByTrack.Length) - 1),
                    sampleRate                        = sampleRate,
                    trackCount                        = (short)maxErrorsByTrack.Length,
                    offsetToUniformScalesStartInBytes = 0
                },
                compressedData      = resultArray,
                extraCompressedData = default
            };
        }

        // Note: It shouldn't matter which DLL actually does the disposal since
        // this is a movable serializable type. So we don't have to worry about
        // Burst races in the Editor. As long as the DLLs link to the same allocator
        // it is fine.
        internal static void DisposeCompressedTrack(AclCompressedClipResult clip)
        {
            if (clip.compressedData.IsCreated)
            {
                if (X86.Avx2.IsAvx2Supported)
                    AVX.disposeCompressedTracksBuffer(clip.compressedData.GetUnsafePtr());
                else
                    NoExtensions.disposeCompressedTracksBuffer(clip.compressedData.GetUnsafePtr());

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                var handle = NativeArrayUnsafeUtility.GetAtomicSafetyHandle(clip.compressedData);
                CollectionHelper.DisposeSafetyHandle(ref handle);
#endif
            }
            if (clip.extraCompressedData.IsCreated)
            {
                if (X86.Avx2.IsAvx2Supported)
                    AVX.disposeCompressedTracksBuffer(clip.extraCompressedData.GetUnsafePtr());
                else
                    NoExtensions.disposeCompressedTracksBuffer(clip.extraCompressedData.GetUnsafePtr());

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                var handle = NativeArrayUnsafeUtility.GetAtomicSafetyHandle(clip.extraCompressedData);
                CollectionHelper.DisposeSafetyHandle(ref handle);
#endif
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void CheckParentIndicesIsValid(NativeArray<short> parentIndices)
        {
            if (!parentIndices.IsCreated || parentIndices.Length == 0)
                throw new ArgumentException("parentIndices is invalid");

            if (parentIndices.Length > short.MaxValue)
                throw new ArgumentException("parentIndices is too big");

            if (parentIndices[0] != 0)
                throw new ArgumentException("parentIndices has invalid root index");

            for (int i = 1; i < parentIndices.Length; i++)
            {
                if (math.abs(parentIndices[i]) > i)
                    throw new ArgumentException("parentIndices has invalid index");
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void CheckClipDataIsValid(NativeArray<Qvvs> aosClipData, int boneCount)
        {
            if (!aosClipData.IsCreated || aosClipData.Length == 0)
                throw new ArgumentException("aosClipData is invalid");
            if (aosClipData.Length % boneCount != 0 || aosClipData.Length < boneCount)
                throw new ArgumentException("aosClipData is not sized correctly relative to the bone count");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void CheckErrorsByTrackIsValid(NativeArray<float> maxErrorsByTrack)
        {
            if (!maxErrorsByTrack.IsCreated || maxErrorsByTrack.Length == 0)
                throw new ArgumentException("maxErrorsByTrack is invalid");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void CheckClipDataIsValid(NativeArray<float> clipData, int trackCount)
        {
            if (!clipData.IsCreated || clipData.Length == 0)
                throw new ArgumentException("clipData is invalid");
            if (clipData.Length % trackCount != 0 || clipData.Length < trackCount)
                throw new ArgumentException("clipData is not sized correctly relative to the track count");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void CheckSampleRateIsValid(float sampleRate)
        {
            if (sampleRate <= math.EPSILON)
                throw new ArgumentOutOfRangeException("sampleRate is negative or too small");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void CheckCompressionLevelIsValid(short compressionLevel)
        {
            if (compressionLevel == 100) // Automatic mode
                return;
            var clampedLevel = math.clamp(compressionLevel, 0, 4);
            if (compressionLevel != clampedLevel)
                throw new ArgumentOutOfRangeException(
                    "compressionLevel must be between 0 (lowest/fastest_to_compress) and 4 (highest/slowest_to_compress) or 100 for automatic mode");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void CheckSkeletonSettingsIsValid(SkeletonCompressionSettings settings)
        {
            CheckCompressionLevelIsValid(settings.compressionLevel);

            if (settings.maxDistanceError <= math.EPSILON)
                throw new ArgumentOutOfRangeException("maxDistanceError is negative or too small");
            if (settings.sampledErrorDistanceFromBone <= math.EPSILON)
                throw new ArgumentOutOfRangeException("sampledErrorDistanceFromBone is negative or too small");
        }

        static class NoExtensions
        {
            const string dllName = AclUnityCommon.dllName;

            [DllImport(dllName)]
            public static extern void* compressSkeletonClip(short* parentIndices,
                                                            short numBones,
                                                            short compressionLevel,
                                                            float* aosClipData,
                                                            int numSamples,
                                                            float sampleRate,
                                                            float maxDistanceError,
                                                            float sampledErrorDistanceFromBone,
                                                            int*   outCompressedSizeInBytes,
                                                            float* sampledScales);

            [DllImport(dllName)]
            public static extern void* compressScalarsClip(short numTracks,
                                                           short compressionLevel,
                                                           float* clipData,
                                                           int numSamples,
                                                           float sampleRate,
                                                           float* maxErrors,
                                                           int*   outCompressedSizeInBytes);

            [DllImport(dllName)]
            public static extern void* disposeCompressedTracksBuffer(void* compressedTracksBuffer);
        }

        static class AVX
        {
            const string dllName = AclUnityCommon.dllNameAVX;

            [DllImport(dllName)]
            public static extern void* compressSkeletonClip(short* parentIndices,
                                                            short numBones,
                                                            short compressionLevel,
                                                            float* aosClipData,
                                                            int numSamples,
                                                            float sampleRate,
                                                            float maxDistanceError,
                                                            float sampledErrorDistanceFromBone,
                                                            int*   outCompressedSizeInBytes,
                                                            float* sampledScales);

            [DllImport(dllName)]
            public static extern void* compressScalarsClip(short numTracks,
                                                           short compressionLevel,
                                                           float* clipData,
                                                           int numSamples,
                                                           float sampleRate,
                                                           float* maxErrors,
                                                           int*   outCompressedSizeInBytes);

            [DllImport(dllName)]
            public static extern void* disposeCompressedTracksBuffer(void* compressedTracksBuffer);
        }
    }
}

