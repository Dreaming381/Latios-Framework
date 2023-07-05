using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Kinemation
{
    internal static class MecanimInternalUtilities
    {
        internal static void AddLayerClipWeights(ref NativeList<TimedMecanimClipInfo> clipWeights,
                                                 ref MecanimControllerLayerBlob layer,
                                                 int currentStateIndex,
                                                 int lastStateIndex,
                                                 NativeArray<MecanimParameter>        parameters,
                                                 float timeInState,
                                                 float lastTransitionDuration,
                                                 float lastExitTime,
                                                 float layerWeight,
                                                 NativeList<float>                    floatCache = default)
        {
            MotionWeightCache weightCache;
            if (floatCache.IsCreated)
                weightCache = new MotionWeightCache(ref floatCache, layer.childMotions.Length);
            else
                weightCache = new MotionWeightCache(layer.childMotions.Length);

            if (lastStateIndex > -1)
            {
                ref var lastState = ref layer.states[lastStateIndex];

                if (lastTransitionDuration > timeInState)
                {
                    var lastStateTime  = timeInState + lastExitTime;
                    var normalizedTime = timeInState / lastTransitionDuration;

                    AddStateClipWeights(ref clipWeights,
                                        ref lastState,
                                        ref layer.childMotions,
                                        parameters,
                                        lastStateTime,
                                        layerWeight * (1 - normalizedTime),
                                        weightCache);
                    AddStateClipWeights(ref clipWeights,
                                        ref layer.states[currentStateIndex],
                                        ref layer.childMotions,
                                        parameters,
                                        timeInState,
                                        layerWeight * normalizedTime,
                                        weightCache);
                }
                else
                {
                    AddStateClipWeights(ref clipWeights,
                                        ref layer.states[currentStateIndex],
                                        ref layer.childMotions,
                                        parameters,
                                        timeInState,
                                        layerWeight,
                                        weightCache);
                }
            }
            else
            {
                AddStateClipWeights(ref clipWeights,
                                    ref layer.states[currentStateIndex],
                                    ref layer.childMotions,
                                    parameters,
                                    timeInState,
                                    layerWeight,
                                    weightCache);
            }
        }

        private static void AddStateClipWeights(ref NativeList<TimedMecanimClipInfo> clipWeights,
                                                ref MecanimStateBlob state,
                                                ref BlobArray<MecanimStateBlob>      childMotions,
                                                NativeArray<MecanimParameter>        parameters,
                                                float timeInState,
                                                float weightFactor,
                                                MotionWeightCache motionWeightCache)
        {
            if (state.isBlendTree)
            {
                switch (state.blendTreeType)
                {
                    case MecanimStateBlob.BlendTreeType.Direct:
                    {
                        //Each motion has their own parameter

                        for (int i = 0; i < state.childMotionIndices.Length; i++)
                        {
                            ref var childMotion               = ref childMotions[state.childMotionIndices[i]];
                            var     directBlendParameterValue = parameters[state.directBlendParameterIndices[i]].floatParam;
                            AddStateClipWeights(ref clipWeights,
                                                ref childMotion,
                                                ref childMotions,
                                                parameters,
                                                timeInState,
                                                weightFactor * directBlendParameterValue,
                                                motionWeightCache);
                        }

                        break;
                    }

                    case MecanimStateBlob.BlendTreeType.Simple1D:
                    {
                        if (state.childMotionIndices.Length < 2)
                        {
                            if (state.childMotionIndices.Length == 1)
                            {
                                ref var childMotion = ref childMotions[state.childMotionIndices[0]];
                                clipWeights.Add(new TimedMecanimClipInfo(ref childMotion, parameters, weightFactor,
                                                                         timeInState));
                            }

                            break;
                        }
                        var blendParameter = parameters[state.blendParameterIndex].floatParam;

                        //This will depend on the editor automatically reordering the child motions according to threshold
                        int stateChildMotionStartIndex = -1;
                        for (int i = 0; i < state.childMotionIndices.Length - 1; i++)
                        {
                            if (state.childMotionThresholds[i] < blendParameter &&
                                state.childMotionThresholds[i + 1] >= blendParameter)
                            {
                                //Blend these two
                                stateChildMotionStartIndex = i;
                                break;
                            }
                        }

                        if (stateChildMotionStartIndex > -1)
                        {
                            // Calculate the blend factor.
                            float blendFactor = (blendParameter - state.childMotionThresholds[stateChildMotionStartIndex]) /
                                                (state.childMotionThresholds[stateChildMotionStartIndex + 1] - state.childMotionThresholds[stateChildMotionStartIndex]);

                            ref var startChildMotion =
                                ref childMotions[state.childMotionIndices[stateChildMotionStartIndex]];
                            ref var endChildMotion =
                                ref childMotions[state.childMotionIndices[stateChildMotionStartIndex + 1]];

                            AddStateClipWeights(ref clipWeights,
                                                ref startChildMotion,
                                                ref childMotions,
                                                parameters,
                                                timeInState,
                                                weightFactor * (1f - blendFactor),
                                                motionWeightCache);
                            AddStateClipWeights(ref clipWeights,
                                                ref endChildMotion,
                                                ref childMotions,
                                                parameters,
                                                timeInState,
                                                weightFactor * blendFactor,
                                                motionWeightCache);
                        }
                        else
                        {
                            //Find the motion at the edge of the blend
                            var childMotionIndex = blendParameter < state.childMotionThresholds[0] ? 0 : state.childMotionIndices.Length - 1;
                            AddStateClipWeights(ref clipWeights,
                                                ref childMotions[state.childMotionIndices[childMotionIndex]],
                                                ref childMotions,
                                                parameters,
                                                timeInState,
                                                weightFactor,
                                                motionWeightCache);
                        }

                        break;
                    }
                    case MecanimStateBlob.BlendTreeType.SimpleDirectional2D:
                    {
                        var x              = parameters[state.blendParameterIndex].floatParam;
                        var y              = parameters[state.blendParameterYIndex].floatParam;
                        var blendParameter = new float2(x, y);

                        float              totalWeight        = 0;
                        NativeArray<float> childMotionWeights = motionWeightCache.GetCacheSubArray(state.childMotionIndices.Length);

                        for (int i = 0; i < state.childMotionIndices.Length; i++)
                        {
                            var motionPosition = state.childMotionPositions[i];

                            float dotProduct = math.dot(motionPosition, blendParameter);
                            // Use the dot product as the weight. This will be close to 1 for small angles and close to 0 for large angles.
                            float weight = math.clamp(dotProduct, 0f, 1f);  // Clamp the weight to [0, 1] to prevent negative weights for vectors pointing in opposite directions.

                            childMotionWeights[i] = weight;

                            totalWeight += weight;
                        }

                        //Get clip weights by their normalized weight value
                        for (int i = 0; i < state.childMotionIndices.Length; i++)
                        {
                            int childMotionIndex = state.childMotionIndices[i];
                            AddStateClipWeights(ref clipWeights,
                                                ref childMotions[childMotionIndex],
                                                ref childMotions,
                                                parameters,
                                                timeInState,
                                                weightFactor * childMotionWeights[i] / totalWeight,
                                                motionWeightCache);
                        }

                        break;
                    }
                    case MecanimStateBlob.BlendTreeType.FreeformCartesian2D:
                    {
                        // ref: https://runevision.com/thesis/rune_skovbo_johansen_thesis.pdf page 58 - Gradient Band Interpolation
                        // The interpolation specifies an influence function hi(p).  (hi(p) = min(1-(pip)*(pipj)/abs(pipj)^2))  The influence function hi(p) of a example i creates gradient bands between the
                        // example point pi and each of the other example points pj with j = 1...n, j 6= i. Note
                        // that the fraction inside the parenthesis is identical to a standard vector projection of
                        // pip onto pipj, except for a missing multiplication with the vector pipj. The fraction
                        // instead evaluates to a scalar that is 0 when the would-be projected vector would
                        // have zero length and is 1 when the would-be projected vector would be equal to
                        // pipj. Thus, after this fraction is subtracted from one, each gradient band decrease
                        // from the value of 1 at pi to 0 at pj
                        // The influence function evaluates to the minimum value of those gradient bands.
                        // These influence functions are normalized to get the weight functions wi(p) associated with each example i
                        // Due to the normalization the weight functions are not entirely identical to the influence functions:
                        // In some areas the influence functions may sum to more than one,
                        // and in those areas all the weights are divided by the sum (normalization). While
                        // the exact weights are altered by this, the overall shapes are quite similar and the
                        // extends of the weight functions (the areas in which they have non-zero weight) are not altered.
                        var x              = parameters[state.blendParameterIndex].floatParam;
                        var y              = parameters[state.blendParameterYIndex].floatParam;
                        var blendParameter = new float2(x, y);

                        var childMotionWeights = motionWeightCache.GetCacheSubArray(state.childMotionIndices.Length);
                        var totalWeight        = 0f;
                        for (int i = 0; i < state.childMotionIndices.Length; i++)
                        {
                            var motionPosition = state.childMotionPositions[i];
                            var minWeight      = float.PositiveInfinity;

                            float2 parametricVector = blendParameter - motionPosition;

                            for (int j = 0; j < state.childMotionIndices.Length; j++)
                            {
                                if (i == j)
                                {
                                    continue;
                                }

                                var    referenceMotionPosition = state.childMotionPositions[j];
                                float2 referenceVector         = referenceMotionPosition - motionPosition;
                                float  weight                  = math.max(1f - math.dot(parametricVector, referenceVector) / math.lengthsq(referenceVector), 0);
                                if (weight < minWeight)
                                {
                                    minWeight = weight;
                                }
                            }

                            childMotionWeights[i]  = minWeight;
                            totalWeight           += minWeight;
                        }

                        //Get clip weights by their normalized weight value
                        for (int i = 0; i < state.childMotionIndices.Length; i++)
                        {
                            int childMotionIndex = state.childMotionIndices[i];
                            AddStateClipWeights(ref clipWeights,
                                                ref childMotions[childMotionIndex],
                                                ref childMotions,
                                                parameters,
                                                timeInState,
                                                weightFactor * childMotionWeights[i] / totalWeight,
                                                motionWeightCache);
                        }

                        break;
                    }
                    case MecanimStateBlob.BlendTreeType.FreeformDirectional2D:
                    {
                        // ref: https://runevision.com/thesis/rune_skovbo_johansen_thesis.pdf page 59 - Gradient Bands in Polar Space
                        // Initially, the influence functions were defined in standard Cartesian space with the
                        //vectors pjpi and pjp specifying the differences in the x, y, and z components of the
                        //points.  The Polar gradient band interpolation method is based on the reasoning that in
                        // order to get a more desirable behavior for the weight functions of example points
                        // that represent velocities, the space in which the interpolation takes place should
                        // take on some of the properties of a polar coordinate system. This means that this
                        // interpolation method is far less general in that it is based on a defined origin in the
                        // interpolation space. However, in the specific case of handling velocities, this can be
                        // an advantage, since it allows for dealing with differences in direction and magnitude
                        // rather than differences in the Cartesian vector coordinate components. In this polar
                        // space, the gradient bands behaves somewhat differently:
                        // • Two example points placed equally far away from the origin should have a
                        // wedge shaped gradient band between them that extends from the origin and
                        // outwards (figure 6.12a). In this case the direction of the new point relative to
                        // the two example points determine the respective weights of the two examples.
                        // • Two example points placed successively further away from the origin in the
                        // same direction should have a circle shaped gradient band between them with
                        // its center in the origin (figure 6.12b). In this case the magnitude of the new
                        // point relative to the two example points determine the respective weights of
                        // the two examples.
                        // pipj = ((math.length(pj) - math.length(pi))/(math.length(pj) + math.length(pi))/2, math.angle(pj, pi))
                        // pip = ((math.length(p) - math.length(pi))/(math.length(pj) + math.length(pi))/2, math.angle(p, pi))
                        var x              = parameters[state.blendParameterIndex].floatParam;
                        var y              = parameters[state.blendParameterYIndex].floatParam;
                        var blendParameter = new float2(x, y);

                        var   childMotionWeights = motionWeightCache.GetCacheSubArray(state.childMotionIndices.Length);
                        float totalWeight        = 0;
                        for (int i = 0; i < state.childMotionIndices.Length; i++)
                        {
                            var motionPosition = state.childMotionPositions[i];
                            var minWeight      = float.PositiveInfinity;

                            for (int j = 0; j < state.childMotionIndices.Length; j++)
                            {
                                if (i == j)
                                {
                                    continue;
                                }
                                var referenceMotionPosition = state.childMotionPositions[j];

                                float2 parametricVector =
                                    new float2((math.length(blendParameter) - math.length(motionPosition)) /
                                               (math.length(referenceMotionPosition) + math.length(motionPosition)) / 2f, blendParameter.angleSigned(motionPosition));
                                float2 referenceVector =
                                    new float2((math.length(referenceMotionPosition) - math.length(motionPosition)) /
                                               (math.length(referenceMotionPosition) + math.length(motionPosition)) / 2f, referenceMotionPosition.angleSigned(motionPosition));
                                float dot    = math.dot(parametricVector, referenceVector);
                                float weight = math.abs(1f - dot);
                                if (weight < minWeight)
                                {
                                    minWeight = weight;
                                }
                            }

                            childMotionWeights[i]  = minWeight;
                            totalWeight           += minWeight;
                        }

                        //Get clip weights by their normalized weight value
                        for (int i = 0; i < state.childMotionIndices.Length; i++)
                        {
                            int childMotionIndex = state.childMotionIndices[i];
                            AddStateClipWeights(ref clipWeights,
                                                ref childMotions[childMotionIndex],
                                                ref childMotions,
                                                parameters,
                                                timeInState,
                                                weightFactor * childMotionWeights[i] / totalWeight,
                                                motionWeightCache);
                        }

                        break;
                    }
                }
            }
            else
            {
                clipWeights.Add(new TimedMecanimClipInfo(ref state, parameters, weightFactor, timeInState));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static float angleSigned(in this float2 from, in float2 to)
        {
            float unsignedAngle = angle(from, to);
            float sign          = math.sign(from.x * to.y - from.y * to.x);
            return unsignedAngle * sign;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static float angle(this float2 from, float2 to)
        {
            // sqrt(a) * sqrt(b) = sqrt(a * b) -- valid for real numbers
            float denominator = math.sqrt(math.lengthsq(from) * math.lengthsq(to));

            if (denominator < 1E-15F)
                return 0F;

            float dot = math.clamp(math.dot(from, to) / denominator, -1F, 1F);
            return math.radians(math.acos(dot));
        }

        // Always pass by value in recursive contexts
        struct MotionWeightCache
        {
            NativeArray<float> m_cache;
            public int         m_usedCount;

            public MotionWeightCache(int motionTotalCount)
            {
                m_cache     = new NativeArray<float>(motionTotalCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                m_usedCount = 0;
            }

            public MotionWeightCache(ref NativeList<float> sourceCache, int motionTotalCount)
            {
                sourceCache.ResizeUninitialized(motionTotalCount);
                m_cache     = sourceCache.AsArray();
                m_usedCount = 0;
            }

            public NativeArray<float> GetCacheSubArray(int motionCount)
            {
                var result   = m_cache.GetSubArray(m_usedCount, motionCount);
                m_usedCount += motionCount;
                return result;
            }
        }
    }
}

