using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Kinemation
{
    /// <summary>
    /// A struct containing local-space translation, rotation, and scale.
    /// </summary>
    public struct BoneTransform
    {
        private quaternion m_rotation;
        private float4     m_translation;
        private float4     m_scale;

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

        // Todo: SamplePose functions.
        // The reason these aren't originally supported is that they need a buffer to write to.
        // Since users will likely want to blend and stuff, that requires lots of temporary buffers
        // which will overflow Allocator.Temp.
        // The solution is a custom allocator that is rewindable per threadIndex.
        // But this was out of scope for the initial release of 0.5.
    }
}

