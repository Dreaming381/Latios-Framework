using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Kinemation
{
    /// <summary>
    /// A struct containing local-space translation, rotation, and scale.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 48)]
    public struct BoneTransform
    {
        [FieldOffset(0)] private quaternion m_rotation;
        [FieldOffset(16)] private float4    m_translation;
        [FieldOffset(32)] private float4    m_scale;

        public quaternion rotation { get => m_rotation; set => m_rotation              = value; }
        public float3 translation { get => m_translation.xyz; set => m_translation.xyz = value; }
        public float3 scale { get => m_scale.xyz; set => m_scale.xyz                   = value; }

        public BoneTransform(quaternion rotation, float3 translation, float3 scale)
        {
            m_rotation    = rotation;
            m_translation = new float4(translation, 0f);
            m_scale       = new float4(scale, 1f);
        }

        internal unsafe BoneTransform(AclUnity.Qvv qvv)
        {
            m_rotation    = qvv.rotation;
            m_translation = qvv.translation;
            m_scale       = qvv.scale;
        }
    }

    /// <summary>
    /// The mechanism used to sample an animation when the time value lies between two keyframes.
    /// Keyframes are evenly distributed based on the authored clip's sample rate.
    /// </summary>
    public enum KeyframeInterpolationMode : byte
    {
        Interpolate = 0,
        Floor = 1,
        Ceil = 2,
        Nearest = 3
    }

    /// <summary>
    /// Blob data containing a collection of skeleton animation clips
    /// </summary>
    public struct SkeletonClipSetBlob
    {
        public short                   boneCount;
        public BlobArray<SkeletonClip> clips;
    }

    /// <summary>
    /// Partial blob data containing a single skeleton animation clip
    /// </summary>
    public struct SkeletonClip
    {
        internal BlobArray<byte> compressedClipDataAligned16;

        /// <summary>
        /// The duration of the clip in seconds
        /// </summary>
        public float duration;

        /// <summary>
        /// The internal sample rate of the clip
        /// </summary>
        public float sampleRate;

        /// <summary>
        /// The number of bones in the clip
        /// </summary>
        public short boneCount;

        /// <summary>
        /// The name of the original authoring clip
        /// </summary>
        public FixedString128Bytes name;

        /// <summary>
        /// Computes a wrapped time value from an unbounded time as if the clip looped infinitely
        /// </summary>
        /// <param name="time">The time value which may exceed the duration of the clip</param>
        /// <returns>A clip time between 0 and the clip's duration</returns>
        /// <remarks>If the clip is 5 seconds long, then a value of 7 would return 2, and a value of -2 would return 3.</remarks>
        public float LoopToClipTime(float time)
        {
            float wrappedTime  = math.fmod(time, duration);
            wrappedTime       += math.select(0f, duration, wrappedTime < 0f);
            return wrappedTime;
        }

        /// <summary>
        /// Samples the animation clip for the given bone index at the given time
        /// </summary>
        /// <param name="boneIndex">The bone index to sample. This value is automatically clamped to a valid value.</param>
        /// <param name="time">
        /// The time value to sample the the clip in seconds.
        /// This value is automatically clamped to a value between 0f and the clip's duration.</param>
        /// <param name="keyframeInterpolationMode">The mechanism used to sample a time value between two keyframes</param>
        /// <returns>A bone transform sampled from the clip in local space of the bone</returns>
        public unsafe BoneTransform SampleBone(int boneIndex, float time, KeyframeInterpolationMode keyframeInterpolationMode = KeyframeInterpolationMode.Interpolate)
        {
            var mode = (AclUnity.Decompression.KeyframeInterpolationMode)keyframeInterpolationMode;

            // Note: ACL clamps time, so we don't need to worry about it.
            // ACLUnity also clamps the boneIndex, so we don't need to worry about that either.
            var qvv = AclUnity.Decompression.SampleBone(compressedClipDataAligned16.GetUnsafePtr(), boneIndex, time, mode);
            return new BoneTransform(qvv);
        }

        public unsafe void SamplePose(ref BufferPoseBlender blender,
                                      float blendWeight,
                                      float time,
                                      KeyframeInterpolationMode keyframeInterpolationMode = KeyframeInterpolationMode.Interpolate)
        {
            CheckBlenderIsBigEnoughForClip(in blender, boneCount);

            var mode = (AclUnity.Decompression.KeyframeInterpolationMode)keyframeInterpolationMode;

            if (blender.sampledFirst)
                AclUnity.Decompression.SamplePoseAosBlendedAdd(compressedClipDataAligned16.GetUnsafePtr(), blender.bufferAsQvv, blendWeight, time, mode);
            else if (blendWeight == 1f)
            {
                AclUnity.Decompression.SamplePoseAos(compressedClipDataAligned16.GetUnsafePtr(), blender.bufferAsQvv, time, mode);
                blender.sampledFirst = true;
            }
            else
            {
                AclUnity.Decompression.SamplePoseAosBlendedFirst(compressedClipDataAligned16.GetUnsafePtr(), blender.bufferAsQvv, blendWeight, time, mode);
                blender.sampledFirst = true;
            }
        }

        /// <summary>
        /// Samples the animation clip for the entire skeleton at the given time and computes the entire OptimizedBoneToRoot buffer.
        /// This method uses a special fast-path.
        /// </summary>
        /// <param name="destination">The buffer to write the results to</param>
        /// <param name="hierarchy">The hierarchy info for the skeleton</param>
        /// <param name="time">The time value to sample the the clip in seconds.
        /// This value is automatically clamped to a value between 0f and the clip's duration.</param>
        /// <param name="keyframeInterpolationMode">The mechanism used to sample a time value between two keyframes</param>
        public unsafe void SamplePose(DynamicBuffer<OptimizedBoneToRoot>                 destination,
                                      BlobAssetReference<OptimizedSkeletonHierarchyBlob> hierarchy,
                                      float time,
                                      KeyframeInterpolationMode keyframeInterpolationMode = KeyframeInterpolationMode.Interpolate)
        {
            CheckBufferIsBigEnoughForClip(destination, boneCount);
            CheckHierarchyIsBigEnoughForClip(hierarchy, boneCount);

            var mode = (AclUnity.Decompression.KeyframeInterpolationMode)keyframeInterpolationMode;

            var destinationAsArray = destination.Reinterpret<float4x4>().AsNativeArray();
            // Mask out the section of the array that we intend to mutate
            if (destinationAsArray.Length > boneCount)
                destinationAsArray  = destinationAsArray.GetSubArray(0, boneCount);
            var destinationAsFloat4 = destinationAsArray.Reinterpret<float4>(64);
            // Qvv is 12 floats whereas a buffer element is 16.
            // Therefore we stuff the data at the back three quarters of the buffer.
            var destinationQvvSubArray = destinationAsFloat4.GetSubArray(destinationAsFloat4.Length - 3 * boneCount, 3 * boneCount);
            var destinationAsQvv       = destinationQvvSubArray.Reinterpret<AclUnity.Qvv>(16);
            AclUnity.Decompression.SamplePoseAos(compressedClipDataAligned16.GetUnsafePtr(), destinationAsQvv, time, mode);

            if (!hierarchy.Value.hasAnyParentScaleInverseBone)
            {
                // Fast path.
                destinationAsArray[0] = float4x4.identity;

                for (int i = 1; i < boneCount; i++)
                {
                    var qvv = destinationAsQvv[i];
                    var mat = float4x4.TRS(qvv.translation.xyz, qvv.rotation, qvv.scale.xyz);
                    Hint.Assume(hierarchy.Value.parentIndices[i] < i);
                    mat                   = math.mul(destinationAsArray[hierarchy.Value.parentIndices[i]], mat);
                    destinationAsArray[i] = mat;
                }
            }
            else
            {
                // Slower path because we pack inverse scale into the fourth row of each matrix.

                // We need to explicitly check for parentScaleInverse for index 0.
                if (hierarchy.Value.hasChildWithParentScaleInverseBitmask[0].IsSet(0))
                {
                    var inverseScale      = math.rcp(destinationAsQvv[0].scale);
                    var mat               = float4x4.identity;
                    mat.c0.w              = inverseScale.x;
                    mat.c1.w              = inverseScale.y;
                    mat.c2.w              = inverseScale.z;
                    destinationAsArray[0] = mat;
                }

                for (int i = 1; i < boneCount; i++)
                {
                    var qvv = destinationAsQvv[i];
                    var mat = float4x4.TRS(qvv.translation.xyz, qvv.rotation, qvv.scale.xyz);
                    Hint.Assume(hierarchy.Value.parentIndices[i] < i);

                    var  parentMat             = destinationAsArray[hierarchy.Value.parentIndices[i]];
                    bool hasParentScaleInverse = hierarchy.Value.hasParentScaleInverseBitmask[i / 64].IsSet(i % 64);
                    var  psi                   = float4x4.Scale(math.select(1f, new float3(parentMat.c0.w, parentMat.c1.w, parentMat.c2.w), hasParentScaleInverse));
                    parentMat.c0.w             = 0f;
                    parentMat.c1.w             = 0f;
                    parentMat.c2.w             = 0f;
                    mat                        = math.mul(psi, mat);
                    mat                        = math.mul(parentMat, mat);

                    bool needsInverseScale = hierarchy.Value.hasChildWithParentScaleInverseBitmask[i / 64].IsSet(i % 64);
                    var  inverseScale      = math.select(0f, math.rcp(qvv.scale), needsInverseScale);
                    mat.c0.w               = inverseScale.x;
                    mat.c1.w               = inverseScale.y;
                    mat.c2.w               = inverseScale.z;
                    destinationAsArray[i]  = mat;
                }

                // Now we need to clean up the inverse scales. We wrote zeros where we didn't need them.
                // So we can do a tzcnt walk.
                for (int maskId = 0; maskId * 64 < boneCount; maskId++)
                {
                    var mask = hierarchy.Value.hasChildWithParentScaleInverseBitmask[maskId];
                    for (int i = mask.CountTrailingZeros(); i < 64 && maskId * 64 + i < boneCount; mask.SetBits(i, false), i = mask.CountTrailingZeros())
                    {
                        var mat                             = destinationAsArray[maskId * 64 + i];
                        mat.c0.w                            = 0f;
                        mat.c1.w                            = 0f;
                        mat.c2.w                            = 0f;
                        destinationAsArray[maskId * 64 + i] = mat;
                    }
                }
            }
        }

        // Todo: SamplePose functions.
        // The reason these aren't originally supported is that they need a buffer to write to.
        // Since users will likely want to blend and stuff, that requires lots of temporary buffers
        // which will overflow Allocator.Temp.
        // The solution is a custom allocator that is rewindable per threadIndex.
        // But this was out of scope for the initial release of 0.5.

        /// <summary>
        /// The size the clip would be if uncompressed, ignoring padding bytes
        /// </summary>
        public int sizeUncompressed => ((int)math.round(sampleRate * duration) + 1) * 40;
        /// <summary>
        /// The size of the clip in its compressed form.
        /// </summary>
        public int sizeCompressed => compressedClipDataAligned16.Length;

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckBlenderIsBigEnoughForClip(in BufferPoseBlender blender, short boneCount)
        {
            if (blender.bufferAs4x4.Length < boneCount)
                throw new ArgumentException("The blender does not contain enough elements to store the sampled pose.");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckBufferIsBigEnoughForClip(DynamicBuffer<OptimizedBoneToRoot> buffer, short boneCount)
        {
            if (buffer.Length < boneCount)
                throw new ArgumentException("The dynamic buffer does not contain enough elements to store the sampled pose.");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckHierarchyIsBigEnoughForClip(BlobAssetReference<OptimizedSkeletonHierarchyBlob> hierarchy, short boneCount)
        {
            if (!hierarchy.IsCreated)
                throw new ArgumentNullException("The hierarchy blob asset is null.");

            if (hierarchy.Value.parentIndices.Length < boneCount)
                throw new ArgumentException("The hierarchy blob asset represents a skeleton with less bones than the clip.");
        }
    }
}

