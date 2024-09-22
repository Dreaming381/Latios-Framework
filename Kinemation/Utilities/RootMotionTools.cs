using Latios.Transforms;
using Unity.Burst.CompilerServices;
using Unity.Mathematics;

namespace Latios.Kinemation
{
    /// <summary>
    /// A struct which can accumulate a root motion delta transform during sampling and blending, properly accounting for looping clips.
    /// This must be initialized to default before use.
    /// </summary>
    public struct RootMotionDeltaAccumulator  // Note: Must be initialized to default to work.
    {
        private TransformQvvs delta;

        /// <summary>
        /// Accumulate the weighted root transform after sampling a clip into a BufferPoseBlender.
        /// </summary>
        /// <param name="blender">A BufferPoseBlender which should contain the sampled root transform at index 0. Index 0 is cleared after reading.</param>
        /// <param name="clip">The clip that was sampled, and from which more samples can be taken.</param>
        /// <param name="previousClipTime">The previous clip time, obtained via LoopToClipTime(), prior to the current sample in index 0.</param>
        /// <param name="loopCycleTransitions">The signed number of times the clip "wrapped" between the previous time and the current time.
        /// Backwards playback will result in negative values. Use SkeletonClip.CountLoopCycleTransitions() to obtain this value.</param>
        /// <param name="keyframeInterpolationMode">The interpolation mode used when sampling the clip.</param>
        public void Accumulate(ref BufferPoseBlender blender,
                               ref SkeletonClip clip,
                               float previousClipTime,
                               float loopCycleTransitions,
                               KeyframeInterpolationMode keyframeInterpolationMode = KeyframeInterpolationMode.Interpolate)
        {
            var sampledRoot           = blender.bufferAsQvvs.Reinterpret<TransformQvvs>()[0];
            blender.bufferAsQvvs[0]   = default;
            var normalizedSampledRoot = sampledRoot;
            normalizedSampledRoot.NormalizeBone();
            Accumulate(in normalizedSampledRoot, math.asfloat(sampledRoot.worldIndex), ref clip, previousClipTime, loopCycleTransitions, keyframeInterpolationMode);
        }

        /// <summary>
        /// Accumulate the weighted root transform after sampling a clip into an optimized skeleton.
        /// </summary>
        /// <param name="skeleton">An optimized skeleton which should contain the sampled root transform at bone index 0. The bone is cleared after reading.</param>
        /// <param name="clip">The clip that was sampled, and from which more samples can be taken.</param>
        /// <param name="previousClipTime">The previous clip time, obtained via LoopToClipTime(), prior to the current sample in index 0.</param>
        /// <param name="loopCycleTransitions">The signed number of times the clip "wrapped" between the previous time and the current time.
        /// Backwards playback will result in negative values. Use SkeletonClip.CountLoopCycleTransitions() to obtain this value.</param>
        /// <param name="keyframeInterpolationMode">The interpolation mode used when sampling the clip.</param>
        public void Accumulate(ref OptimizedSkeletonAspect skeleton,
                               ref SkeletonClip clip,
                               float previousClipTime,
                               float loopCycleTransitions,
                               KeyframeInterpolationMode keyframeInterpolationMode = KeyframeInterpolationMode.Interpolate)
        {
            var array                 = skeleton.rawLocalTransformsRW;
            var sampledRoot           = array[0];
            array[0]                  = default;
            var normalizedSampledRoot = sampledRoot;
            normalizedSampledRoot.NormalizeBone();
            Accumulate(in normalizedSampledRoot, math.asfloat(sampledRoot.worldIndex), ref clip, previousClipTime, loopCycleTransitions, keyframeInterpolationMode);
        }

        /// <summary>
        /// Sample and accumulate the root transform using the specified clip.
        /// </summary>
        /// <param name="clip">The clip to sample</param>
        /// <param name="currentLoopingTime">The current time in the clip, including loop multiples of the clip's duration</param>
        /// <param name="previousLoopingTime">The previous time in the clip, including loop multiples of the clip's duration</param>
        /// <param name="weight">The weight factor that should be applied for this sample when blending the root delta</param>
        /// <param name="keyframeInterpolationMode">The interpolation mode to use when sampling the clip</param>
        public void SampleAccumulate(ref SkeletonClip clip,
                                     float currentLoopingTime,
                                     float previousLoopingTime,
                                     float weight,
                                     KeyframeInterpolationMode keyframeInterpolationMode = KeyframeInterpolationMode.Interpolate)
        {
            var loopCycleTransitions = clip.CountLoopCycleTransitions(currentLoopingTime, previousLoopingTime);
            var currentClipTime      = clip.LoopToClipTime(currentLoopingTime);
            var previousClipTime     = clip.LoopToClipTime(previousLoopingTime);
            var current              = clip.SampleBone(0, currentClipTime);
            Accumulate(in current, weight, ref clip, previousClipTime, loopCycleTransitions, keyframeInterpolationMode);
        }

        private void Accumulate(in TransformQvvs current,
                                float weight,
                                ref SkeletonClip clip,
                                float previousClipTime,
                                float loopCycleTransitions,
                                KeyframeInterpolationMode keyframeInterpolationMode)
        {
            var previousRoot = clip.SampleBone(0, previousClipTime, keyframeInterpolationMode);
            if (Hint.Likely(math.abs(loopCycleTransitions) < 0.5f))
            {
                var newDelta = RootMotionTools.DeltaBetween(current, previousRoot);
                newDelta     = RootMotionTools.ApplyWeight(newDelta, weight);
                delta        = RootMotionTools.AddDeltas(delta, newDelta);
            }
            else
            {
                var           beginRoot = clip.SampleBone(0, 0f);
                var           endRoot   = clip.SampleBone(0, clip.duration);
                TransformQvvs newDelta;
                if (Hint.Likely(loopCycleTransitions > 0f))
                {
                    var h = RootMotionTools.DeltaBetween(endRoot, previousRoot);
                    var t = RootMotionTools.DeltaBetween(current, beginRoot);
                    if (Hint.Unlikely(loopCycleTransitions > 1.5f))
                    {
                        var middleDelta = RootMotionTools.DeltaBetween(endRoot, beginRoot);
                        var toAdd       = middleDelta;
                        for (float i = 2.5f; i < loopCycleTransitions; i += 1f)
                            middleDelta = RootMotionTools.ConcatenateDeltas(middleDelta, toAdd);
                        newDelta        = RootMotionTools.ConcatenateDeltas(RootMotionTools.ConcatenateDeltas(h, middleDelta), t);
                    }
                    else
                        newDelta = RootMotionTools.ConcatenateDeltas(h, t);
                }
                else
                {
                    var h = RootMotionTools.DeltaBetween(endRoot, current);
                    var t = RootMotionTools.DeltaBetween(previousRoot, beginRoot);
                    if (Hint.Unlikely(loopCycleTransitions < -1.5f))
                    {
                        var middleDelta = RootMotionTools.DeltaBetween(beginRoot, endRoot);
                        var toAdd       = middleDelta;
                        for (float i = -2.5f; i < loopCycleTransitions; i -= 1f)
                            middleDelta = RootMotionTools.ConcatenateDeltas(middleDelta, toAdd);
                        newDelta        = RootMotionTools.ConcatenateDeltas(RootMotionTools.ConcatenateDeltas(h, middleDelta), t);
                    }
                    else
                        newDelta = RootMotionTools.ConcatenateDeltas(h, t);
                }
                newDelta = RootMotionTools.ApplyWeight(newDelta, weight);
                delta    = RootMotionTools.AddDeltas(delta, newDelta);
            }
        }

        /// <summary>
        /// Gets the raw accumulated root motion transform delta
        /// </summary>
        public TransformQvvs rawDelta => delta;
        /// <summary>
        /// Gets the normalized accumulated root motion transform delta
        /// </summary>
        public TransformQvvs normalizedDelta
        {
            get
            {
                var r = rawDelta;
                r.NormalizeBone();
                return r;
            }
        }
    }

    /// <summary>
    /// Contains methods to apply "mathematical expressions" between bone transforms for working with transform deltas
    /// </summary>
    public static class RootMotionTools
    {
        /// <summary>
        /// Gets a transform delta from the previous transform to the current transform. Only valid for normalized transforms.
        /// </summary>
        /// <param name="current">The current transform</param>
        /// <param name="previous">The previous transform</param>
        /// <returns>Effectively result = current - previous, using appropriate "difference" metrics for each attribute of the transform</returns>
        public static TransformQvvs DeltaBetween(in TransformQvvs current, in TransformQvvs previous)
        {
            return new TransformQvvs
            {
                position   = current.position - previous.position,
                rotation   = math.mul(current.rotation, math.inverse(previous.rotation)),
                worldIndex = current.worldIndex,
                scale      = current.scale / previous.scale,
                stretch    = current.stretch / previous.stretch
            };
        }

        /// <summary>
        /// Applies a weight to all attributes of the transform
        /// </summary>
        /// <param name="bone">The transform to apply weights to</param>
        /// <param name="weight">The weight to scale everything by</param>
        /// <returns>The weighted transform</returns>
        public static TransformQvvs ApplyWeight(TransformQvvs bone, float weight)
        {
            bone.position       *= weight;
            bone.rotation.value *= weight;
            bone.scale          *= weight;
            bone.stretch        *= weight;
            bone.worldIndex      = math.asint(math.asfloat(bone.worldIndex) * weight);
            return bone;
        }

        /// <summary>
        /// Adds two weighted delta transforms together. Use this for combining deltas between different clips.
        /// This operation is commutative.
        /// </summary>
        /// <param name="deltaA">The first delta</param>
        /// <param name="deltaB">The second delta</param>
        /// <returns>A combined delta that is effectively the sum of its parts</returns>
        public static TransformQvvs AddDeltas(in TransformQvvs deltaA, in TransformQvvs deltaB)
        {
            return new TransformQvvs
            {
                position   = deltaA.position + deltaB.position,
                rotation   = deltaA.rotation.value + deltaB.rotation.value,
                scale      = deltaA.scale + deltaB.scale,
                stretch    = deltaA.stretch + deltaB.stretch,
                worldIndex = math.asint(math.asfloat(deltaA.worldIndex) + math.asfloat(deltaB.worldIndex)),
            };
        }

        /// <summary>
        /// Sequences two delta transforms together. Use this for dealing with discontinuities when sampling deltas in a single clip such as wrapping,
        /// or when the time spans multiple adjacent clips played in sequence. Order matters, and the transforms must be normalized.
        /// </summary>
        /// <param name="deltaFirst">The first delta acquired in the sequence</param>
        /// <param name="deltaSecond">The second delta acquired in the sequence</param>
        /// <returns>An overall delta that would result from playing the first delta, and then the second</returns>
        public static TransformQvvs ConcatenateDeltas(in TransformQvvs deltaFirst, in TransformQvvs deltaSecond)
        {
            return new TransformQvvs
            {
                position   = deltaFirst.position + deltaSecond.position,
                rotation   = math.mul(deltaSecond.rotation, deltaFirst.rotation),
                scale      = deltaFirst.scale * deltaSecond.scale,
                stretch    = deltaFirst.stretch * deltaSecond.stretch,
                worldIndex = deltaFirst.worldIndex,
            };
        }
    }
}

