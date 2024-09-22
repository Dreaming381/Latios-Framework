using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Latios.Transforms;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Kinemation
{
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
    public unsafe struct SkeletonClip
    {
        internal BlobArray<byte> compressedClipDataAligned16;

        /// <summary>
        /// The duration of the clip in seconds
        /// </summary>
        public float duration => AclUnity.ClipHeader.Read(compressedClipDataAligned16.GetUnsafePtr()).duration;

        /// <summary>
        /// The internal sample rate of the clip
        /// </summary>
        public float sampleRate => AclUnity.ClipHeader.Read(compressedClipDataAligned16.GetUnsafePtr()).sampleRate;

        /// <summary>
        /// The number of bones in the clip
        /// </summary>
        public short boneCount => AclUnity.ClipHeader.Read(compressedClipDataAligned16.GetUnsafePtr()).trackCount;

        /// <summary>
        /// Events associated with the clip
        /// </summary>
        public ClipEvents events;

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
        /// Computes the number of wraps through the clip between the previous time and the current time.
        /// Every time the clip wraps from the end to the beginning, 1 wrap is added. Every time the clip
        /// wraps backwards from the beginning to the end, 1 wrap is subtracted, meaning the final result
        /// can be negative. The final result is a whole number, but for performance reasons is returned
        /// as a floating point value.
        /// </summary>
        /// <param name="currentTime">The current unbounded time</param>
        /// <param name="previousTime">The previous unbounded time</param>
        /// <returns>A signed integral number of wraps that occurred</returns>
        public float CountLoopCycleTransitions(float currentTime, float previousTime)
        {
            float2 packedTimes = new float2(currentTime, previousTime);
            math.modf(packedTimes / duration, out var cycles);
            cycles -= math.select(0f, 1f, packedTimes < 0f);
            return cycles.x - cycles.y;
        }

        /// <summary>
        /// Samples the animation clip for the given bone index at the given time
        /// </summary>
        /// <param name="boneIndex">The bone index to sample. This value is automatically clamped to a valid value.</param>
        /// <param name="time">
        /// The time value to sample the the clip in seconds.
        /// This value is automatically clamped to a value between 0f and the clip's duration.</param>
        /// <param name="keyframeInterpolationMode">The mechanism used to sample a time value between two keyframes</param>
        /// <returns>A bone transform sampled from the clip in local space of the bone, with its weight set to 1f</returns>
        public unsafe TransformQvvs SampleBone(int boneIndex, float time, KeyframeInterpolationMode keyframeInterpolationMode = KeyframeInterpolationMode.Interpolate)
        {
            var mode = (AclUnity.Decompression.KeyframeInterpolationMode)keyframeInterpolationMode;

            // Note: ACL clamps time, so we don't need to worry about it.
            // ACLUnity also clamps the boneIndex, so we don't need to worry about that either.
            var qvv = AclUnity.Decompression.SampleBone(compressedClipDataAligned16.GetUnsafePtr(), boneIndex, time, mode);
            return new TransformQvvs
            {
                rotation   = qvv.rotation,
                position   = qvv.translation.xyz,
                worldIndex = math.asint(1f),
                stretch    = qvv.stretchScale.xyz,
                scale      = qvv.stretchScale.w
            };
        }

        /// <summary>
        /// Samples the animation clip for the entire skeleton at the given time weighted by the blendWeight.
        /// This method uses a special fast-path.
        /// </summary>
        /// <param name="optimizedSkeletonAspect">The skeleton aspect on which to apply the animation</param>
        /// <param name="time">The time value to sample the the clip in seconds.
        /// <param name="blendWeight">A weight factor to use for blending in the range of (0f, 1f]</param>
        /// This value is automatically clamped to a value between 0f and the clip's duration.</param>
        /// <param name="keyframeInterpolationMode">The mechanism used to sample a time value between two keyframes</param>
        public unsafe void SamplePose(ref OptimizedSkeletonAspect optimizedSkeletonAspect,
                                      float time,
                                      float blendWeight,
                                      KeyframeInterpolationMode keyframeInterpolationMode = KeyframeInterpolationMode.Interpolate)
        {
            CheckSkeletonIsBigEnoughForClip(in optimizedSkeletonAspect, boneCount);

            var mode = (AclUnity.Decompression.KeyframeInterpolationMode)keyframeInterpolationMode;

            if (optimizedSkeletonAspect.BeginSampleTrueIfAdditive(out var buffer))
                AclUnity.Decompression.SamplePoseBlendedAdd(compressedClipDataAligned16.GetUnsafePtr(), buffer, blendWeight, time, mode);
            else
                AclUnity.Decompression.SamplePoseBlendedFirst(compressedClipDataAligned16.GetUnsafePtr(), buffer, blendWeight, time, mode);
        }

        /// <summary>
        /// Samples the animation clip for parts of the skeleton specified by the mask at the given time weighted by the blendWeight.
        /// This method uses a special fast-path.
        /// </summary>
        /// <param name="optimizedSkeletonAspect">The skeleton aspect on which to apply the animation</param>
        /// <param name="mask">A bit array where each bit specifies if the bone at that index should be sampled</param>
        /// <param name="time">The time value to sample the the clip in seconds.
        /// <param name="blendWeight">A weight factor to use for blending in the range of (0f, 1f]</param>
        /// This value is automatically clamped to a value between 0f and the clip's duration.</param>
        /// <param name="keyframeInterpolationMode">The mechanism used to sample a time value between two keyframes</param>
        public unsafe void SamplePose(ref OptimizedSkeletonAspect optimizedSkeletonAspect,
                                      ReadOnlySpan<ulong>         mask,
                                      float time,
                                      float blendWeight,
                                      KeyframeInterpolationMode keyframeInterpolationMode = KeyframeInterpolationMode.Interpolate)
        {
            CheckSkeletonIsBigEnoughForClip(in optimizedSkeletonAspect, boneCount);

            var mode = (AclUnity.Decompression.KeyframeInterpolationMode)keyframeInterpolationMode;

            if (optimizedSkeletonAspect.BeginSampleTrueIfAdditive(out var buffer))
                AclUnity.Decompression.SamplePoseMaskedBlendedAdd(compressedClipDataAligned16.GetUnsafePtr(), buffer, mask, blendWeight, time, mode);
            else
                AclUnity.Decompression.SamplePoseMaskedBlendedFirst(compressedClipDataAligned16.GetUnsafePtr(), buffer, mask, blendWeight, time, mode);
        }

        /// <summary>
        /// Samples the animation clip for the entire set of transforms at the given time weighted by the blendWeight.
        /// This method uses a special fast-path.
        /// </summary>
        /// <param name="blender">The blender which contains context information about previous sampling operations</param>
        /// <param name="time">The time value to sample the the clip in seconds.
        /// <param name="blendWeight">A weight factor to use for blending in the range of (0f, 1f]</param>
        /// This value is automatically clamped to a value between 0f and the clip's duration.</param>
        /// <param name="keyframeInterpolationMode">The mechanism used to sample a time value between two keyframes</param>
        public unsafe void SamplePose(ref BufferPoseBlender blender,
                                      float time,
                                      float blendWeight               = 1f,
                                      KeyframeInterpolationMode keyframeInterpolationMode = KeyframeInterpolationMode.Interpolate)
        {
            CheckBlenderIsBigEnoughForClip(in blender, boneCount);

            var mode = (AclUnity.Decompression.KeyframeInterpolationMode)keyframeInterpolationMode;

            if (blender.sampledFirst)
                AclUnity.Decompression.SamplePoseBlendedAdd(compressedClipDataAligned16.GetUnsafePtr(), blender.bufferAsQvvs, blendWeight, time, mode);
            else
            {
                AclUnity.Decompression.SamplePoseBlendedFirst(compressedClipDataAligned16.GetUnsafePtr(), blender.bufferAsQvvs, blendWeight, time, mode);
                blender.sampledFirst = true;
            }
        }

        /// <summary>
        /// Samples the animation clip for the transforms selected by the mask at the given time weighted by the blendWeight.
        /// This method uses a special fast-path.
        /// </summary>
        /// <param name="blender">The blender which contains context information about previous sampling operations</param>
        /// <param name="mask">A bit array where each bit specifies if the bone at that index should be sampled</param>
        /// <param name="time">The time value to sample the the clip in seconds.
        /// <param name="blendWeight">A weight factor to use for blending in the range of (0f, 1f]</param>
        /// This value is automatically clamped to a value between 0f and the clip's duration.</param>
        /// <param name="keyframeInterpolationMode">The mechanism used to sample a time value between two keyframes</param>
        public unsafe void SamplePose(ref BufferPoseBlender blender,
                                      ReadOnlySpan<ulong>       mask,
                                      float time,
                                      float blendWeight               = 1f,
                                      KeyframeInterpolationMode keyframeInterpolationMode = KeyframeInterpolationMode.Interpolate)
        {
            CheckBlenderIsBigEnoughForClip(in blender, boneCount);

            var mode = (AclUnity.Decompression.KeyframeInterpolationMode)keyframeInterpolationMode;

            if (blender.sampledFirst)
                AclUnity.Decompression.SamplePoseMaskedBlendedAdd(compressedClipDataAligned16.GetUnsafePtr(), blender.bufferAsQvvs, mask, blendWeight, time, mode);
            else
            {
                AclUnity.Decompression.SamplePoseMaskedBlendedFirst(compressedClipDataAligned16.GetUnsafePtr(), blender.bufferAsQvvs, mask, blendWeight, time, mode);
                blender.sampledFirst = true;
            }
        }

        /// <summary>
        /// Samples the animation clip for the entire set of transforms at the given time raw into a buffer without any blending.
        /// worldIndex values are undefined. This method uses a special fast-path.
        /// </summary>
        /// <param name="localTransforms">The raw local transforms array to overwrite with the sampled data.</param>
        /// <param name="time">The time value to sample the the clip in seconds.</param>
        /// <param name="keyframeInterpolationMode">The mechanism used to sample a time value between two keyframes</param>
        public unsafe void SamplePose(ref NativeArray<TransformQvvs> localTransforms,
                                      float time,
                                      KeyframeInterpolationMode keyframeInterpolationMode = KeyframeInterpolationMode.Interpolate)
        {
            var mode = (AclUnity.Decompression.KeyframeInterpolationMode)keyframeInterpolationMode;
            AclUnity.Decompression.SamplePose(compressedClipDataAligned16.GetUnsafePtr(), localTransforms.Reinterpret<AclUnity.Qvvs>(), time, mode);
        }

        /// <summary>
        /// Samples the animation clip for the entire set of transforms at the given time raw into a buffer with blending.
        /// New transforms can be specified to either overwrite existing values or be added via weighting. This method uses a special fast-path.
        /// </summary>
        /// <param name="localTransforms">The raw local transforms array to overwrite with the sampled data.</param>
        /// <param name="time">The time value to sample the the clip in seconds.</param>
        /// <param name="blendWeight">A weight factor to use for blending in the range of (0f, 1f]</param>
        /// <param name="overwrite">If true, the existing transforms and accumulated weights are overwritten.
        /// If false, the new transform values are added to the existing values.</param>
        /// <param name="keyframeInterpolationMode">The mechanism used to sample a time value between two keyframes</param>
        public unsafe void SamplePose(ref NativeArray<TransformQvvs> localTransforms,
                                      float time,
                                      float blendWeight,
                                      bool overwrite,
                                      KeyframeInterpolationMode keyframeInterpolationMode = KeyframeInterpolationMode.Interpolate)
        {
            var mode = (AclUnity.Decompression.KeyframeInterpolationMode)keyframeInterpolationMode;
            if (overwrite)
                AclUnity.Decompression.SamplePoseBlendedFirst(compressedClipDataAligned16.GetUnsafePtr(), localTransforms.Reinterpret<AclUnity.Qvvs>(), blendWeight, time, mode);
            else
                AclUnity.Decompression.SamplePoseBlendedAdd(compressedClipDataAligned16.GetUnsafePtr(), localTransforms.Reinterpret<AclUnity.Qvvs>(), blendWeight, time, mode);
        }

        /// <summary>
        /// Samples the animation clip for the transforms selected by the mask at the given time raw into a buffer with blending.
        /// New transforms can be specified to either overwrite existing values or be added via weighting. This method uses a special fast-path.
        /// </summary>
        /// <param name="localTransforms">The raw local transforms array to overwrite with the sampled data.</param>
        /// <param name="mask">A bit array where each bit specifies if the bone at that index should be sampled</param>
        /// <param name="time">The time value to sample the the clip in seconds.</param>
        /// <param name="blendWeight">A weight factor to use for blending in the range of (0f, 1f]</param>
        /// <param name="overwrite">If true, the existing transforms and accumulated weights are overwritten.
        /// If false, the new transform values are added to the existing values.</param>
        /// <param name="keyframeInterpolationMode">The mechanism used to sample a time value between two keyframes</param>
        public unsafe void SamplePose(ref NativeArray<TransformQvvs> localTransforms,
                                      ReadOnlySpan<ulong>            mask,
                                      float time,
                                      float blendWeight,
                                      bool overwrite,
                                      KeyframeInterpolationMode keyframeInterpolationMode = KeyframeInterpolationMode.Interpolate)
        {
            var mode = (AclUnity.Decompression.KeyframeInterpolationMode)keyframeInterpolationMode;
            if (overwrite)
                AclUnity.Decompression.SamplePoseMaskedBlendedFirst(compressedClipDataAligned16.GetUnsafePtr(),
                                                                    localTransforms.Reinterpret<AclUnity.Qvvs>(),
                                                                    mask,
                                                                    blendWeight,
                                                                    time,
                                                                    mode);
            else
                AclUnity.Decompression.SamplePoseMaskedBlendedAdd(compressedClipDataAligned16.GetUnsafePtr(),
                                                                  localTransforms.Reinterpret<AclUnity.Qvvs>(),
                                                                  mask,
                                                                  blendWeight,
                                                                  time,
                                                                  mode);
        }

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
            if (blender.bufferAsQvvs.Length < boneCount)
                throw new ArgumentException("The blender does not contain enough elements to store the sampled pose.");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckSkeletonIsBigEnoughForClip(in OptimizedSkeletonAspect osa, short boneCount)
        {
            if (osa.boneCount < boneCount)
                throw new ArgumentException("The Optimized Skeleton does not contain enough bones for the animation clip.");
        }
    }

    /// <summary>
    /// Blob data containing a collection of general purpose parameter animation clips
    /// </summary>
    public struct ParameterClipSetBlob
    {
        public short                    parameterCount;
        public BlobArray<ParameterClip> clips;
        /// <summary>
        /// Equivalent to the FixedString128Bytes.GetHashCode() for each parameter name
        /// </summary>
        public BlobArray<int>                 parameterNameHashes;
        public BlobArray<FixedString128Bytes> parameterNames;
    }

    /// <summary>
    /// Partial blob data containing a single clip for a collection of parameters
    /// </summary>
    public unsafe struct ParameterClip
    {
        internal BlobArray<byte> compressedClipDataAligned16;

        /// <summary>
        /// The duration of the clip in seconds
        /// </summary>
        public float duration => AclUnity.ClipHeader.Read(compressedClipDataAligned16.GetUnsafePtr()).duration;

        /// <summary>
        /// The internal sample rate of the clip
        /// </summary>
        public float sampleRate => AclUnity.ClipHeader.Read(compressedClipDataAligned16.GetUnsafePtr()).sampleRate;

        /// <summary>
        /// The number of parameters in the clip
        /// </summary>
        public short parameterCount => AclUnity.ClipHeader.Read(compressedClipDataAligned16.GetUnsafePtr()).trackCount;

        /// <summary>
        /// Events associated with the clip
        /// </summary>
        public ClipEvents events;

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
        /// Computes the number of wraps through the clip between the previous time and the current time.
        /// Every time the clip wraps from the end to the beginning, 1 wrap is added. Every time the clip
        /// wraps backwards from the beginning to the end, 1 wrap is subtracted, meaning the final result
        /// can be negative. The final result is a whole number, but for performance reasons is returned
        /// as a floating point value.
        /// </summary>
        /// <param name="currentTime">The current unbounded time</param>
        /// <param name="previousTime">The previous unbounded time</param>
        /// <returns>A signed integral number of wraps that occurred</returns>
        public float CountLoopCycleTransitions(float currentTime, float previousTime)
        {
            float2 packedTimes = new float2(currentTime, previousTime);
            math.modf(packedTimes / duration, out var cycles);
            cycles -= math.select(0f, 1f, packedTimes < 0f);
            return cycles.x - cycles.y;
        }

        /// <summary>
        /// Samples the animation clip for the given parameter index at the given time
        /// </summary>
        /// <param name="parameterIndex">The parameter index to sample. This value is automatically clamped to a valid value.</param>
        /// <param name="time">
        /// The time value to sample the the clip in seconds.
        /// This value is automatically clamped to a value between 0f and the clip's duration.</param>
        /// <param name="keyframeInterpolationMode">The mechanism used to sample a time value between two keyframes</param>
        /// <returns>A parameter sampled from the clip in local space of the bone</returns>
        public unsafe float SampleParameter(int parameterIndex, float time, KeyframeInterpolationMode keyframeInterpolationMode = KeyframeInterpolationMode.Interpolate)
        {
            var mode = (AclUnity.Decompression.KeyframeInterpolationMode)keyframeInterpolationMode;

            // Note: ACL clamps time, so we don't need to worry about it.
            // ACLUnity also clamps the parameterIndex, so we don't need to worry about that either.
            return AclUnity.Decompression.SampleFloat(compressedClipDataAligned16.GetUnsafePtr(), parameterIndex, time, mode);
        }

        /// <summary>
        /// Samples the animation clip for all parameters at the given time at once. This method uses a special fast-path.
        /// </summary>
        /// <param name="destination">The array of floats where the parameters should be stored. If the array is not large enough, a safety exception is thrown.</param>
        /// <param name="time">
        /// The time value to sample the the clip in seconds.
        /// This value is automatically clamped to a value between 0f and the clip's duration.</param>
        /// <param name="keyframeInterpolationMode">The mechanism used to sample a time value between two keyframes</param>
        public unsafe void SampleAllParameters(NativeArray<float>        destination,
                                               float time,
                                               KeyframeInterpolationMode keyframeInterpolationMode = KeyframeInterpolationMode.Interpolate)
        {
            CheckBufferIsBigEnoughForClip(destination, parameterCount);
            var mode = (AclUnity.Decompression.KeyframeInterpolationMode)keyframeInterpolationMode;
            AclUnity.Decompression.SampleFloats(compressedClipDataAligned16.GetUnsafePtr(), destination, time, mode);
        }

        /// <summary>
        /// Samples the animation clip for the selected set of parameters specified by the mask at the given time at once.
        /// This method uses a special fast-path.
        /// </summary>
        /// <param name="destination">The array of floats where the parameters should be stored. If the array is not large enough, a safety exception is thrown.</param>
        /// <param name="mask">A bit array where each bit specifies if the parameter at that index should be sampled</param>
        /// <param name="time">
        /// The time value to sample the the clip in seconds.
        /// This value is automatically clamped to a value between 0f and the clip's duration.</param>
        /// <param name="keyframeInterpolationMode">The mechanism used to sample a time value between two keyframes</param>
        public unsafe void SampleSelectedParameters(NativeArray<float>        destination,
                                                    ReadOnlySpan<ulong>       mask,
                                                    float time,
                                                    KeyframeInterpolationMode keyframeInterpolationMode = KeyframeInterpolationMode.Interpolate)
        {
            CheckBufferIsBigEnoughForClip(destination, parameterCount);
            var mode = (AclUnity.Decompression.KeyframeInterpolationMode)keyframeInterpolationMode;
            AclUnity.Decompression.SampleFloatsMasked(compressedClipDataAligned16.GetUnsafePtr(), destination, mask, time, mode);
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        void CheckBufferIsBigEnoughForClip(NativeArray<float> buffer, short parameterCount)
        {
            if (buffer.Length < parameterCount)
                throw new ArgumentException("The Native Array does not contain enough elements to store all the parameters.");
        }
    }

    /// <summary>
    /// A time-sorted SOA of events where each event has a name and an extra int parameter
    /// </summary>
    public struct ClipEvents
    {
        public BlobArray<float>              times;
        public BlobArray<int>                nameHashes;
        public BlobArray<int>                parameters;
        public BlobArray<FixedString64Bytes> names;

        /// <summary>
        /// Finds all events within the time range (previousTime, currentTime] and returns the first index and the count.
        /// If currentTime is less than previousTime, if iterating i = firstEventIndex while i < firstEventIndex + count,
        /// events should be indexed as [i % times.length] to account for looping behavior.
        /// </summary>
        /// <param name="previousTime">The previous time to start searching for events. This time value is exclusive.</param>
        /// <param name="currentTime">The current time to end searching for events. This time value is inclusive.</param>
        /// <param name="firstEventIndex">The index of the first event found. -1 if no events are found.</param>
        /// <param name="eventCount">The number of events found</param>
        /// <returns>True if events were found, false otherwise</returns>
        public bool TryGetEventsRange(float previousTime, float currentTime, out int firstEventIndex, out int eventCount)
        {
            if (previousTime == currentTime || times.Length == 0)
            {
                firstEventIndex = -1;
                eventCount      = 0;
                return false;
            }

            // Todo: Vectorize and optimize
            firstEventIndex = -1;
            for (int i = 0; i < times.Length; i++)
            {
                if (times[i] > previousTime)
                {
                    firstEventIndex = i;
                    break;
                }
            }
            int onePastLastEventIndex = -1;
            for (int i = 0; i < times.Length; i++)
            {
                if (times[i] > currentTime)
                {
                    onePastLastEventIndex = i;
                    break;
                }
            }

            if (previousTime < currentTime)
            {
                if (firstEventIndex == -1)
                {
                    eventCount = 0;
                    return false;
                }

                if (onePastLastEventIndex == -1)
                {
                    // The time is beyond the last event.
                    eventCount = times.Length - firstEventIndex;
                    return true;
                }

                eventCount = onePastLastEventIndex - firstEventIndex;
                return true;
            }

            // We wrapped around
            if (onePastLastEventIndex <= 0 && firstEventIndex == -1)
            {
                // We start past the last event and the current time is before the first event
                eventCount = 0;
                return false;
            }

            if (firstEventIndex == -1)
            {
                firstEventIndex = 0;
                eventCount      = onePastLastEventIndex;
                return true;
            }

            eventCount = times.Length - firstEventIndex + onePastLastEventIndex;
            return true;
        }
    }
}

