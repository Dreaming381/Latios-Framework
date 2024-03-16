using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace Latios.Myri
{
    internal static class CullingAndWeighting
    {
        public const int kBatchSize = 128;

        //Parallel
        //The weighting algorithm is fairly pricey
        [BurstCompile]
        public struct OneshotsJob : IJobParallelForBatch
        {
            [ReadOnly] public NativeList<ListenerWithTransform>              listenersWithTransforms;
            [ReadOnly] public NativeArray<OneshotEmitter>                    emitters;
            [NativeDisableParallelForRestriction] public NativeStream.Writer weights;
            [NativeDisableParallelForRestriction] public NativeStream.Writer listenerEmitterPairs;  //int2: listener, emitter

            public void Execute(int startIndex, int count)
            {
                var scratchCache = new NativeList<float4>(Allocator.Temp);

                var baseWeights = new NativeArray<Weights>(listenersWithTransforms.Length, Allocator.Temp, NativeArrayOptions.ClearMemory);
                for (int i = 0; i < listenersWithTransforms.Length; i++)
                {
                    int c = listenersWithTransforms[i].listener.ildProfile.Value.anglesPerLeftChannel.Length +
                            listenersWithTransforms[i].listener.ildProfile.Value.anglesPerRightChannel.Length;
                    Weights w = default;
                    for (int j = 0; j < c; j++)
                    {
                        w.channelWeights.Add(0f);
                    }
                    c = listenersWithTransforms[i].listener.itdResolution;
                    c = 2 * c + 1;
                    for (int j = 0; j < c; j++)
                    {
                        w.itdWeights.Add(0f);
                    }
                    baseWeights[i] = w;
                }

                listenerEmitterPairs.BeginForEachIndex(startIndex / kBatchSize);
                weights.BeginForEachIndex(startIndex / kBatchSize);

                for (int i = startIndex; i < startIndex + count; i++)
                {
                    var emitter = emitters[i];
                    for (int j = 0; j < listenersWithTransforms.Length; j++)
                    {
                        if (math.distancesq(emitter.transform.pos,
                                            listenersWithTransforms[j].transform.pos) < emitter.source.outerRange * emitter.source.outerRange && emitter.source.clip.IsCreated)
                        {
                            var w = baseWeights[j];

                            EmitterParameters e = new EmitterParameters
                            {
                                cone            = emitter.cone,
                                innerRange      = emitter.source.innerRange,
                                outerRange      = emitter.source.outerRange,
                                rangeFadeMargin = emitter.source.rangeFadeMargin,
                                transform       = emitter.transform,
                                useCone         = emitter.useCone,
                                volume          = emitter.source.volume
                            };
                            //ComputeWeights(ref w, e, in listenersWithTransforms.ElementAt(j), scratchCache);
                            ComputeWeights(ref w, e, listenersWithTransforms[j], scratchCache);

                            weights.Write(w);
                            listenerEmitterPairs.Write(new int2(j, i));
                        }
                    }
                }

                listenerEmitterPairs.EndForEachIndex();
                weights.EndForEachIndex();
            }
        }

        //Parallel
        //The weighting algorithm is fairly pricey
        [BurstCompile]
        public struct LoopedJob : IJobParallelForBatch
        {
            [ReadOnly] public NativeList<ListenerWithTransform>              listenersWithTransforms;
            [ReadOnly] public NativeArray<LoopedEmitter>                     emitters;
            [NativeDisableParallelForRestriction] public NativeStream.Writer weights;
            [NativeDisableParallelForRestriction] public NativeStream.Writer listenerEmitterPairs;  //int2: listener, emitter

            public void Execute(int startIndex, int count)
            {
                var scratchCache = new NativeList<float4>(Allocator.Temp);

                var baseWeights = new NativeArray<Weights>(listenersWithTransforms.Length, Allocator.Temp, NativeArrayOptions.ClearMemory);
                for (int i = 0; i < listenersWithTransforms.Length; i++)
                {
                    int c = listenersWithTransforms[i].listener.ildProfile.Value.anglesPerLeftChannel.Length +
                            listenersWithTransforms[i].listener.ildProfile.Value.anglesPerRightChannel.Length;
                    Weights w = default;
                    for (int j = 0; j < c; j++)
                    {
                        w.channelWeights.Add(0f);
                    }
                    c = listenersWithTransforms[i].listener.itdResolution;
                    c = 2 * c + 1;
                    for (int j = 0; j < c; j++)
                    {
                        w.itdWeights.Add(0f);
                    }
                    baseWeights[i] = w;
                }

                listenerEmitterPairs.BeginForEachIndex(startIndex / kBatchSize);
                weights.BeginForEachIndex(startIndex / kBatchSize);

                for (int i = startIndex; i < startIndex + count; i++)
                {
                    var emitter = emitters[i];
                    for (int j = 0; j < listenersWithTransforms.Length; j++)
                    {
                        if (math.distancesq(emitter.transform.pos,
                                            listenersWithTransforms[j].transform.pos) < emitter.source.outerRange * emitter.source.outerRange && emitter.source.clip.IsCreated)
                        {
                            var w = baseWeights[j];

                            EmitterParameters e = new EmitterParameters
                            {
                                cone            = emitter.cone,
                                innerRange      = emitter.source.innerRange,
                                outerRange      = emitter.source.outerRange,
                                rangeFadeMargin = emitter.source.rangeFadeMargin,
                                transform       = emitter.transform,
                                useCone         = emitter.useCone,
                                volume          = emitter.source.volume
                            };
                            ComputeWeights(ref w, e, listenersWithTransforms[j], scratchCache);

                            weights.Write(w);
                            listenerEmitterPairs.Write(new int2(j, i));
                        }
                    }
                }

                listenerEmitterPairs.EndForEachIndex();
                weights.EndForEachIndex();
            }
        }

        private struct EmitterParameters
        {
            public float volume;
            public float innerRange;
            public float outerRange;
            public float rangeFadeMargin;

            public RigidTransform         transform;
            public AudioSourceEmitterCone cone;
            public bool                   useCone;
        }

        private static void ComputeWeights(ref Weights weights, EmitterParameters emitter, ListenerWithTransform listener, NativeList<float4> scratchCache)
        {
            float volume = emitter.volume * listener.listener.volume;

            var emitterInListenerSpace    = math.mul(math.inverse(listener.transform), emitter.transform);
            var emitterPositionNormalized = math.normalizesafe(emitterInListenerSpace.pos, float3.zero);

            //attenuation
            {
                float d     = math.length(emitterInListenerSpace.pos);
                float atten = 1f;
                if (d > emitter.innerRange)
                {
                    if (emitter.innerRange <= 0f)
                    {
                        //The offset is the distance from the innerRange minus 1 unit clamped between the innerRange and the margin.
                        //The minus one offset ensures the falloff is always 1 or larger, making the transition betweem the innerRange
                        //and the falloff region continuous (by calculus terminology).
                        float falloff = math.min(d, emitter.outerRange - emitter.rangeFadeMargin) - (emitter.innerRange - 1f);
                        atten         = math.saturate(math.rcp(falloff * falloff));
                    }
                    else
                    {
                        float falloff = math.min(d, emitter.outerRange - emitter.rangeFadeMargin) / emitter.innerRange;
                        atten         = math.saturate(math.rcp(falloff * falloff));
                    }
                }
                if (d > emitter.outerRange - emitter.rangeFadeMargin)
                {
                    float factor = (d - (emitter.outerRange - emitter.rangeFadeMargin)) / emitter.rangeFadeMargin;
                    factor       = math.saturate(factor);
                    atten        = math.lerp(atten, 0f, factor);
                }

                if (emitter.useCone)
                {
                    float cosine = math.dot(math.forward(emitterInListenerSpace.rot), -emitterPositionNormalized);
                    if (cosine <= emitter.cone.cosOuterAngle)
                    {
                        atten *= emitter.cone.outerAngleAttenuation;
                    }
                    else if (cosine < emitter.cone.cosInnerAngle)
                    {
                        float factor  = math.unlerp(emitter.cone.cosOuterAngle, emitter.cone.cosInnerAngle, cosine);
                        atten        *= math.lerp(emitter.cone.outerAngleAttenuation, 1f, factor);
                    }
                }
                volume *= atten;
            }

            //ITD
            {
                float itd = (math.dot(emitterPositionNormalized, math.right()) * 0.5f + 0.5f) * weights.itdWeights.Length;
                //float frac                    = math.modf(itd, out float integer);
                //int   indexLow                = math.clamp((int)integer, 0, weights.itdWeights.Length - 1);
                //int   indexHigh               = math.clamp(indexLow + 1, 0, weights.itdWeights.Length - 1);
                //weights.itdWeights[indexLow]  = volume * frac;
                //weights.itdWeights[indexHigh] = volume * (1f - frac);
                int index                 = math.clamp((int)math.round(itd), 0, weights.itdWeights.Length - 1);
                weights.itdWeights[index] = volume;
            }

            //ILD
            {
                ref var profile = ref listener.listener.ildProfile.Value;

                float2 xz     = math.normalizesafe(emitterPositionNormalized.xz, new float2(0f, 1f));
                float2 angles = default;
                angles.x      = math.atan2(xz.y, xz.x);
                float2 yz     = math.normalizesafe(emitterPositionNormalized.yz, new float2(1f, 0f));
                angles.y      = math.atan2(yz.y, yz.x);

                //Left
                //First, find if there is a perfect match
                bool perfectMatch = false;
                for (int i = 0; i < profile.anglesPerLeftChannel.Length; i++)
                {
                    perfectMatch = math.all(((angles >= profile.anglesPerLeftChannel[i].xz) &
                                             (angles <= profile.anglesPerLeftChannel[i].yw)) |
                                            ((angles + 2f * math.PI >= profile.anglesPerLeftChannel[i].xz) &
                                             (angles + 2f * math.PI <= profile.anglesPerLeftChannel[i].yw)));
                    if (perfectMatch)
                    {
                        weights.channelWeights[i] = 1f;
                        perfectMatch              = true;
                        break;
                    }
                }

                if (!perfectMatch)
                {
                    //No perfect match.
                    int4                     bestMinMaxXYIndices = default;  //This should always be overwritten
                    float4                   bestAngleDeltas     = new float4(2f * math.PI, -2f * math.PI, 2f * math.PI, -2f * math.PI);
                    FixedList128Bytes<int>   candidateChannels   = default;
                    FixedList128Bytes<float> candidateDistances  = default;

                    //Find our limits
                    scratchCache.Clear();
                    scratchCache.AddRangeFromBlob(ref profile.anglesPerLeftChannel);
                    var                      leftChannelDeltas  = scratchCache.AsArray();
                    FixedList512Bytes<bool2> leftChannelInsides = default;

                    for (int i = 0; i < leftChannelDeltas.Length; i++)
                    {
                        var delta  = leftChannelDeltas[i] - angles.xxyy;
                        var temp   = delta;
                        delta     += math.select(0f, new float4(2f * math.PI, -2f * math.PI, 2f * math.PI, -2f * math.PI), delta * new float4(1f, -1f, 1f, -1f) < 0f);
                        delta     -= math.select(0f,
                                                 new float4(2f * math.PI, -2f * math.PI, 2f * math.PI, -2f * math.PI),
                                                 delta * new float4(1f, -1f, 1f, -1f) >= 2f * math.PI);
                        temp                 -= math.select(0f, 2f * math.PI, temp.xxzz > 0f);
                        bool2 inside          = temp.yw >= 0f;
                        leftChannelDeltas[i]  = delta;
                        leftChannelInsides.Add(inside);
                    }
                    //By this point, any delta should be (positive, negative, positive, negative)

                    //Find our search region
                    for (int i = 0; i < leftChannelDeltas.Length; i++)
                    {
                        bool2 inside = leftChannelInsides[i];
                        var   delta  = leftChannelDeltas[i];
                        if (inside.x)
                        {
                            //above
                            if (delta.z <= bestAngleDeltas.z)
                            {
                                bestAngleDeltas.z     = delta.z;
                                bestMinMaxXYIndices.z = i;
                            }
                            //below
                            if (delta.w >= bestAngleDeltas.w)
                            {
                                bestAngleDeltas.w     = delta.w;
                                bestMinMaxXYIndices.w = i;
                            }
                        }
                        if (inside.y)
                        {
                            //right
                            if (delta.x <= bestAngleDeltas.x)
                            {
                                bestAngleDeltas.x     = delta.x;
                                bestMinMaxXYIndices.x = i;
                            }
                            //left
                            if (delta.y >= bestAngleDeltas.y)
                            {
                                bestAngleDeltas.y     = delta.y;
                                bestMinMaxXYIndices.y = i;
                            }
                        }
                    }

                    //Add our constraining indices to the pot
                    var bestAngleDistances = math.abs(bestAngleDeltas);
                    candidateChannels.Add(bestMinMaxXYIndices.x);
                    candidateDistances.Add(bestAngleDistances.x);
                    if (bestMinMaxXYIndices.x != bestMinMaxXYIndices.y)
                    {
                        candidateChannels.Add(bestMinMaxXYIndices.y);
                        candidateDistances.Add(bestAngleDistances.y);
                    }
                    else
                        candidateDistances[0] = math.min(candidateDistances[0], bestAngleDistances.y);

                    if (math.all(bestMinMaxXYIndices.xy != bestMinMaxXYIndices.z))
                    {
                        candidateChannels.Add(bestMinMaxXYIndices.z);
                        candidateDistances.Add(bestAngleDistances.z);
                    }
                    else if (bestMinMaxXYIndices.x == bestMinMaxXYIndices.z)
                        candidateDistances[0] = math.min(candidateDistances[0], bestAngleDistances.z);
                    else
                        candidateDistances[1] = math.min(candidateDistances[1], bestAngleDistances.z);

                    if (math.all(bestMinMaxXYIndices.xyz != bestMinMaxXYIndices.w))
                    {
                        candidateChannels.Add(bestMinMaxXYIndices.w);
                        candidateDistances.Add(bestAngleDistances.w);
                    }
                    else if (bestMinMaxXYIndices.x == bestMinMaxXYIndices.w)
                        candidateDistances[0] = math.min(candidateDistances[0], bestAngleDistances.w);
                    else if (bestMinMaxXYIndices.y == bestMinMaxXYIndices.w)
                        candidateDistances[1] = math.min(candidateDistances[1], bestAngleDistances.w);
                    else
                        candidateDistances[candidateDistances.Length - 1] = math.min(candidateDistances[candidateDistances.Length - 1], bestAngleDistances.w);

                    //Add additional candidates
                    for (int i = 0; i < leftChannelDeltas.Length; i++)
                    {
                        if (math.any(i == bestMinMaxXYIndices))
                            continue;

                        float4 delta = leftChannelDeltas[i];
                        bool   added = false;
                        int    c     = candidateDistances.Length;
                        if (math.all(delta.xz < bestAngleDeltas.xz))
                        {
                            candidateChannels.Add(i);
                            candidateDistances.Add(math.length(delta.xz));
                            added = true;
                        }
                        if (delta.y > bestAngleDeltas.y && delta.z < bestAngleDeltas.z)
                        {
                            if (added)
                            {
                                candidateDistances[c] = math.min(candidateDistances[c], math.length(delta.yz));
                            }
                            else
                            {
                                candidateChannels.Add(i);
                                candidateDistances.Add(math.length(delta.yz));
                                added = true;
                            }
                        }
                        if (delta.x < bestAngleDeltas.x && delta.w < bestAngleDeltas.w)
                        {
                            if (added)
                            {
                                candidateDistances[c] = math.min(candidateDistances[c], math.length(delta.xw));
                            }
                            else
                            {
                                candidateChannels.Add(i);
                                candidateDistances.Add(math.length(delta.xw));
                                added = true;
                            }
                        }
                        if (math.all(delta.yw > bestAngleDeltas.yw))
                        {
                            if (added)
                            {
                                candidateDistances[c] = math.min(candidateDistances[c], math.length(delta.yw));
                            }
                            else
                            {
                                candidateChannels.Add(i);
                                candidateDistances.Add(math.length(delta.yw));
                            }
                        }
                    }

                    //Compute weights
                    float sum = 0f;
                    for (int i = 0; i < candidateDistances.Length; i++)
                    {
                        candidateDistances[i]  = 1f / candidateDistances[i];
                        sum                   += candidateDistances[i];
                    }
                    for (int i = 0; i < candidateDistances.Length; i++)
                    {
                        weights.channelWeights[candidateChannels[i]] = candidateDistances[i] / sum;
                    }
                }

                //Right
                //First, find if there is a perfect match
                perfectMatch = false;
                for (int i = 0; i < profile.anglesPerRightChannel.Length; i++)
                {
                    perfectMatch = math.all(((angles >= profile.anglesPerRightChannel[i].xz) &
                                             (angles <= profile.anglesPerRightChannel[i].yw)) |
                                            ((angles + 2f * math.PI >= profile.anglesPerRightChannel[i].xz) &
                                             (angles + 2f * math.PI <= profile.anglesPerRightChannel[i].yw)));
                    if (perfectMatch)
                    {
                        weights.channelWeights[i + profile.anglesPerLeftChannel.Length] = 1f;
                        perfectMatch                                                    = true;
                        break;
                    }
                }

                if (!perfectMatch)
                {
                    //No perfect match.
                    int4                     bestMinMaxXYIndices = default;  //This should always be overwritten
                    float4                   bestAngleDeltas     = new float4(2f * math.PI, -2f * math.PI, 2f * math.PI, -2f * math.PI);
                    FixedList128Bytes<int>   candidateChannels   = default;
                    FixedList128Bytes<float> candidateDistances  = default;

                    //Find our limits
                    scratchCache.Clear();
                    scratchCache.AddRangeFromBlob(ref profile.anglesPerRightChannel);
                    var                      rightChannelDeltas  = scratchCache.AsArray();
                    FixedList512Bytes<bool2> rightChannelInsides = default;

                    for (int i = 0; i < rightChannelDeltas.Length; i++)
                    {
                        var delta  = rightChannelDeltas[i] - angles.xxyy;
                        var temp   = delta;
                        delta     += math.select(0f, new float4(2f * math.PI, -2f * math.PI, 2f * math.PI, -2f * math.PI), delta * new float4(1f, -1f, 1f, -1f) < 0f);
                        delta     -= math.select(0f,
                                                 new float4(2f * math.PI, -2f * math.PI, 2f * math.PI, -2f * math.PI),
                                                 delta * new float4(1f, -1f, 1f, -1f) >= 2f * math.PI);
                        temp                  -= math.select(0f, 2f * math.PI, temp.xxzz > 0f);
                        bool2 inside           = temp.yw >= 0f;
                        rightChannelDeltas[i]  = delta;
                        rightChannelInsides.Add(inside);
                    }
                    //By this point, any delta should be (positive, negative, positive, negative)

                    //Find our search region
                    for (int i = 0; i < rightChannelDeltas.Length; i++)
                    {
                        bool2 inside = rightChannelInsides[i];
                        var   delta  = rightChannelDeltas[i];
                        if (inside.x)
                        {
                            //above
                            if (delta.z <= bestAngleDeltas.z)
                            {
                                bestAngleDeltas.z     = delta.z;
                                bestMinMaxXYIndices.z = i;
                            }
                            //below
                            if (delta.w >= bestAngleDeltas.w)
                            {
                                bestAngleDeltas.w     = delta.w;
                                bestMinMaxXYIndices.w = i;
                            }
                        }
                        if (inside.y)
                        {
                            //right
                            if (delta.x <= bestAngleDeltas.x)
                            {
                                bestAngleDeltas.x     = delta.x;
                                bestMinMaxXYIndices.x = i;
                            }
                            //left
                            if (delta.y >= bestAngleDeltas.y)
                            {
                                bestAngleDeltas.y     = delta.y;
                                bestMinMaxXYIndices.y = i;
                            }
                        }
                    }

                    //Add our constraining indices to the pot
                    var bestAngleDistances = math.abs(bestAngleDeltas);
                    candidateChannels.Add(bestMinMaxXYIndices.x);
                    candidateDistances.Add(bestAngleDistances.x);
                    if (bestMinMaxXYIndices.x != bestMinMaxXYIndices.y)
                    {
                        candidateChannels.Add(bestMinMaxXYIndices.y);
                        candidateDistances.Add(bestAngleDistances.y);
                    }
                    else
                        candidateDistances[0] = math.min(candidateDistances[0], bestAngleDistances.y);

                    if (math.all(bestMinMaxXYIndices.xy != bestMinMaxXYIndices.z))
                    {
                        candidateChannels.Add(bestMinMaxXYIndices.z);
                        candidateDistances.Add(bestAngleDistances.z);
                    }
                    else if (bestMinMaxXYIndices.x == bestMinMaxXYIndices.z)
                        candidateDistances[0] = math.min(candidateDistances[0], bestAngleDistances.z);
                    else
                        candidateDistances[1] = math.min(candidateDistances[1], bestAngleDistances.z);

                    if (math.all(bestMinMaxXYIndices.xyz != bestMinMaxXYIndices.w))
                    {
                        candidateChannels.Add(bestMinMaxXYIndices.w);
                        candidateDistances.Add(bestAngleDistances.w);
                    }
                    else if (bestMinMaxXYIndices.x == bestMinMaxXYIndices.w)
                        candidateDistances[0] = math.min(candidateDistances[0], bestAngleDistances.w);
                    else if (bestMinMaxXYIndices.y == bestMinMaxXYIndices.w)
                        candidateDistances[1] = math.min(candidateDistances[1], bestAngleDistances.w);
                    else
                        candidateDistances[candidateDistances.Length - 1] = math.min(candidateDistances[candidateDistances.Length - 1], bestAngleDistances.w);

                    //Add additional candidates
                    for (int i = 0; i < rightChannelDeltas.Length; i++)
                    {
                        if (math.any(i == bestMinMaxXYIndices))
                            continue;

                        float4 delta = rightChannelDeltas[i];
                        bool   added = false;
                        int    c     = candidateDistances.Length;
                        if (math.all(delta.xz < bestAngleDeltas.xz))
                        {
                            candidateChannels.Add(i);
                            candidateDistances.Add(math.length(delta.xz));
                            added = true;
                        }
                        if (delta.y > bestAngleDeltas.y && delta.z < bestAngleDeltas.z)
                        {
                            if (added)
                            {
                                candidateDistances[c] = math.min(candidateDistances[c], math.length(delta.yz));
                            }
                            else
                            {
                                candidateChannels.Add(i);
                                candidateDistances.Add(math.length(delta.yz));
                                added = true;
                            }
                        }
                        if (delta.x < bestAngleDeltas.x && delta.w < bestAngleDeltas.w)
                        {
                            if (added)
                            {
                                candidateDistances[c] = math.min(candidateDistances[c], math.length(delta.xw));
                            }
                            else
                            {
                                candidateChannels.Add(i);
                                candidateDistances.Add(math.length(delta.xw));
                                added = true;
                            }
                        }
                        if (math.all(delta.yw > bestAngleDeltas.yw))
                        {
                            if (added)
                            {
                                candidateDistances[c] = math.min(candidateDistances[c], math.length(delta.yw));
                            }
                            else
                            {
                                candidateChannels.Add(i);
                                candidateDistances.Add(math.length(delta.yw));
                            }
                        }
                    }

                    //Compute weights
                    float sum = 0f;
                    for (int i = 0; i < candidateDistances.Length; i++)
                    {
                        candidateDistances[i]  = 1f / candidateDistances[i];
                        sum                   += candidateDistances[i];
                    }
                    for (int i = 0; i < candidateDistances.Length; i++)
                    {
                        weights.channelWeights[candidateChannels[i] + profile.anglesPerLeftChannel.Length] = candidateDistances[i] / sum;
                    }
                }

                // Filter out zero-weights
                // Todo: Pre-compute and cache
                for (int i = 0; i < profile.anglesPerLeftChannel.Length; i++)
                {
                    if (profile.filterVolumesPerLeftChannel[i] * (1f - profile.passthroughFractionsPerLeftChannel[i]) +
                        profile.passthroughVolumesPerLeftChannel[i] * profile.passthroughFractionsPerLeftChannel[i] <= 0f)
                        weights.channelWeights[i] = 0f;
                }
                for (int i = 0; i < profile.anglesPerRightChannel.Length; i++)
                {
                    if (profile.filterVolumesPerRightChannel[i] * (1f - profile.passthroughFractionsPerRightChannel[i]) +
                        profile.passthroughVolumesPerRightChannel[i] * profile.passthroughFractionsPerRightChannel[i] <= 0f)
                        weights.channelWeights[i + profile.anglesPerLeftChannel.Length] = 0f;
                }
            }
        }
    }
}

