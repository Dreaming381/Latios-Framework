using System;
using Latios.Transforms;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Myri
{
    internal static class CullingAndWeighting
    {
        // Parallel
        [BurstCompile]
        public struct CullAndWeightJob : IJobFor
        {
            [ReadOnly] public NativeArray<ListenerWithTransform>             listenersWithTransforms;
            [ReadOnly] public NativeArray<AudioSourceChannelID>              listenersChannelIDs;
            [ReadOnly] public NativeStream.Reader                            capturedSources;
            [ReadOnly] public NativeArray<int>                               channelCount;
            [NativeDisableParallelForRestriction] public NativeStream.Writer chunkChannelStreams;

            UnsafeList<float4>           scratchCache;
            UnsafeList<ExportableSource> sourcesToExport;

            public unsafe void Execute(int chunkIndex)
            {
                if (!scratchCache.IsCreated)
                {
                    scratchCache    = new UnsafeList<float4>(16, Allocator.Temp);
                    sourcesToExport = new UnsafeList<ExportableSource>(128 * channelCount[0], Allocator.Temp);
                }

                sourcesToExport.Clear();
                int sourceCount = capturedSources.BeginForEachIndex(chunkIndex) / 2;
                for (int sourceIndex = 0; sourceIndex < sourceCount; sourceIndex++)
                {
                    var sourceHeader = capturedSources.Read<CapturedSourceHeader>();

                    // Compute required offsets and total size of source
                    int sourceSize = 0;
                    if (sourceHeader.HasFlag(CapturedSourceHeader.Features.Clip))
                        sourceSize += CollectionHelper.Align(UnsafeUtility.SizeOf<AudioSourceClip>(), 8);
                    if (sourceHeader.HasFlag(CapturedSourceHeader.Features.SampleRateMultiplier))
                        sourceSize              += 8;
                    var sourceBatchingByteCount  = sourceSize;

                    var channelIDOffset = sourceSize;
                    if (sourceHeader.HasFlag(CapturedSourceHeader.Features.ChannelID))
                        sourceSize      += CollectionHelper.Align(UnsafeUtility.SizeOf<AudioSourceChannelID>(), 8);
                    var transformOffset  = sourceSize;
                    if (sourceHeader.HasFlag(CapturedSourceHeader.Features.Transform))
                        sourceSize            += CollectionHelper.Align(UnsafeUtility.SizeOf<TransformQvvs>(), 8);
                    var distanceFalloffOffset  = sourceSize;
                    if (sourceHeader.HasFlag(CapturedSourceHeader.Features.DistanceFalloff))
                        sourceSize += CollectionHelper.Align(UnsafeUtility.SizeOf<AudioSourceDistanceFalloff>(), 8);
                    var coneOffset  = sourceSize;
                    if (sourceHeader.HasFlag(CapturedSourceHeader.Features.Cone))
                        sourceSize += CollectionHelper.Align(UnsafeUtility.SizeOf<AudioSourceEmitterCone>(), 8);

                    var sourceDataPtr = capturedSources.ReadUnsafePtr(sourceSize);

                    // Set up emitter
                    EmitterParameters e = default;
                    e.volume            = sourceHeader.volume;
                    if (sourceHeader.HasFlag(CapturedSourceHeader.Features.Transform))
                    {
                        var transform = *(TransformQvvs*)(sourceDataPtr + transformOffset);
                        e.transform   = new RigidTransform(transform.rotation, transform.position);
                    }
                    bool isSpatial = sourceHeader.HasFlag(CapturedSourceHeader.Features.DistanceFalloff);
                    if (isSpatial)
                    {
                        var distanceFalloff = *(AudioSourceDistanceFalloff*)(sourceDataPtr + distanceFalloffOffset);
                        e.innerRange        = distanceFalloff.innerRange;
                        e.outerRange        = distanceFalloff.outerRange;
                        e.rangeFadeMargin   = distanceFalloff.rangeFadeMargin;
                    }
                    if (sourceHeader.HasFlag(CapturedSourceHeader.Features.Cone))
                    {
                        e.useCone = true;
                        e.cone    = *(AudioSourceEmitterCone*)(sourceDataPtr + coneOffset);
                    }

                    var channelID = sourceHeader.HasFlag(CapturedSourceHeader.Features.ChannelID) ? *(AudioSourceChannelID*)(sourceDataPtr + channelIDOffset) : default;

                    // Iterate through listeners, and track channels incrementally
                    int listenerFirstChannelIndex = 0;
                    int listenerChannelCount      = 1;
                    for (int listenerIndex = 0; listenerIndex < listenersWithTransforms.Length; listenerIndex++, listenerFirstChannelIndex += listenerChannelCount)
                    {
                        var listener = listenersWithTransforms[listenerIndex];
                        if (listener.channelIDsRange.y == 0 || listener.listener.volume <= 0f)
                            continue;

                        ref var blob         = ref listener.listener.ildProfile.Value;
                        listenerChannelCount = blob.anglesPerLeftChannel.Length + blob.anglesPerRightChannel.Length;

                        var rangeSq = math.square(e.outerRange * listener.listener.rangeMultiplier);
                        if (isSpatial && math.distancesq(e.transform.pos, listener.transform.pos) >= rangeSq)
                            continue;

                        var listenerChannelIDs = listenersChannelIDs.GetSubArray(listener.channelIDsRange.x, listener.channelIDsRange.y);
                        if (!listenerChannelIDs.Contains(channelID))
                            continue;

                        // Todo: Make Weights be based on Spans and indices to optimize the processing below.
                        Weights w               = default;
                        w.channelWeights.Length = listenerChannelCount;
                        w.itdWeights.Length     = 2 * listener.listener.itdResolution + 1;

                        if (isSpatial)
                            ComputeWeights(ref w, in e, in listener, ref scratchCache);
                        else
                        {
                            w.itdWeights[w.itdWeights.Length / 2] = e.volume;
                            for (int i = 0; i < blob.anglesPerLeftChannel.Length; i++)
                            {
                                if (math.all(math.isnan(blob.anglesPerLeftChannel[i])))
                                    w.channelWeights[i] = 1f;
                            }
                            for (int i = 0; i < blob.anglesPerRightChannel.Length; i++)
                            {
                                if (math.all(math.isnan(blob.anglesPerRightChannel[i])))
                                    w.channelWeights[i + blob.anglesPerLeftChannel.Length] = 1f;
                            }
                        }

                        int   itdIndex  = -1;
                        float itdVolume = 0f;
                        for (int i = 0; i < w.itdWeights.Length; i++)
                        {
                            if (w.itdWeights[i] > 0f)
                            {
                                itdIndex  = i;
                                itdVolume = w.itdWeights[i];
                                break;
                            }
                        }

                        if (itdIndex == -1)
                            continue;

                        sourceHeader.volume = itdVolume;
                        var channelSource   = new ChannelStreamSource
                        {
                            sourceHeader      = sourceHeader,
                            sourceDataPtr     = sourceDataPtr,
                            batchingByteCount = sourceBatchingByteCount,
                            itdIndex          = itdIndex,
                            itdCount          = w.itdWeights.Length,
                            isRightChannel    = false,
                        };
                        for (int i = 0; i < w.channelWeights.Length; i++)
                        {
                            if (w.channelWeights[i] > 0f)
                            {
                                var toAdd = new ExportableSource
                                {
                                    source      = channelSource,
                                    channel     = i,
                                    stableIndex = sourceIndex
                                };
                                toAdd.source.sourceHeader.volume *= w.channelWeights[i];
                                toAdd.source.isRightChannel       = i >= blob.anglesPerLeftChannel.Length;
                                sourcesToExport.AddNoResize(toAdd);
                            }
                        }
                    }
                }

                // Export to NativeSteam. We can only write to one index at a time, which is why we cached
                // our results to a list. But we still need to reorder them by listener.
                sourcesToExport.Sort();
                int previousChannel = -1;
                foreach (var export in sourcesToExport)
                {
                    if (export.channel != previousChannel)
                    {
                        if (previousChannel >= 0)
                            chunkChannelStreams.EndForEachIndex();
                        chunkChannelStreams.BeginForEachIndex(chunkIndex * channelCount[0] + export.channel);
                        previousChannel = export.channel;
                    }
                    chunkChannelStreams.Write(export.source);
                }
                if (previousChannel >= 0)
                    chunkChannelStreams.EndForEachIndex();

                capturedSources.EndForEachIndex();
            }

            struct ExportableSource : IComparable<ExportableSource>
            {
                public ChannelStreamSource source;
                public int                 channel;
                public int                 stableIndex;

                public int CompareTo(ExportableSource other)
                {
                    var result = channel.CompareTo(other.channel);
                    if (result == 0)
                        return stableIndex.CompareTo(other.stableIndex);
                    return result;
                }
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

        internal struct Weights
        {
            public FixedList512Bytes<float> channelWeights;
            public FixedList128Bytes<float> itdWeights;

            public static Weights operator +(Weights a, Weights b)
            {
                Weights result = a;
                for (int i = 0; i < a.channelWeights.Length; i++)
                {
                    result.channelWeights[i] += b.channelWeights[i];
                }
                for (int i = 0; i < a.itdWeights.Length; i++)
                {
                    result.itdWeights[i] += b.itdWeights[i];
                }
                return result;
            }
        }

        private static void ComputeWeights(ref Weights weights, in EmitterParameters emitter, in ListenerWithTransform listener, ref UnsafeList<float4> scratchCache)
        {
            float volume = emitter.volume;

            var emitterInListenerSpace    = math.mul(math.inverse(listener.transform), emitter.transform);
            var emitterPositionNormalized = math.normalizesafe(emitterInListenerSpace.pos, float3.zero);

            //attenuation
            {
                float d               = math.length(emitterInListenerSpace.pos);
                float atten           = 1f;
                var   innerRange      = emitter.innerRange * listener.listener.rangeMultiplier;
                var   outerRange      = emitter.outerRange * listener.listener.rangeMultiplier;
                var   rangeFadeMargin = emitter.rangeFadeMargin * listener.listener.rangeMultiplier;
                if (d > innerRange)
                {
                    if (innerRange <= 0f)
                    {
                        //The offset is the distance from the innerRange minus 1 unit clamped between the innerRange and the margin.
                        //The minus one offset ensures the falloff is always 1 or larger, making the transition betweem the innerRange
                        //and the falloff region continuous (by calculus terminology).
                        float falloff = math.min(d, outerRange - rangeFadeMargin) - (innerRange - 1f);
                        atten         = math.saturate(math.rcp(falloff * falloff));
                    }
                    else
                    {
                        float falloff = math.min(d, outerRange - rangeFadeMargin) / innerRange;
                        atten         = math.saturate(math.rcp(falloff * falloff));
                    }
                }
                if (d > outerRange - rangeFadeMargin)
                {
                    float factor = (d - (outerRange - rangeFadeMargin)) / rangeFadeMargin;
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
                    // No perfect match.
                    int4                     bestMinMaxXYIndices = default;  //This should always be overwritten
                    float4                   bestAngleDeltas     = new float4(2f * math.PI, -2f * math.PI, 2f * math.PI, -2f * math.PI);
                    FixedList128Bytes<int>   candidateChannels   = default;
                    FixedList128Bytes<float> candidateDistances  = default;

                    // Find our limits
                    scratchCache.Clear();
                    scratchCache.AddRangeFromBlob(ref profile.anglesPerLeftChannel);
                    var                      leftChannelDeltas  = scratchCache;
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
                    // By this point, any delta should be (positive, negative, positive, negative)

                    // Find our search region
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

                    // Add our constraining indices to the pot
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

                    // Add additional candidates
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

                    // Compute weights
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

                // Right
                // First, find if there is a perfect match
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
                    // No perfect match.
                    int4                     bestMinMaxXYIndices = default;  //This should always be overwritten
                    float4                   bestAngleDeltas     = new float4(2f * math.PI, -2f * math.PI, 2f * math.PI, -2f * math.PI);
                    FixedList128Bytes<int>   candidateChannels   = default;
                    FixedList128Bytes<float> candidateDistances  = default;

                    // Find our limits
                    scratchCache.Clear();
                    scratchCache.AddRangeFromBlob(ref profile.anglesPerRightChannel);
                    var                      rightChannelDeltas  = scratchCache;
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
                    // By this point, any delta should be (positive, negative, positive, negative)

                    // Find our search region
                    for (int i = 0; i < rightChannelDeltas.Length; i++)
                    {
                        bool2 inside = rightChannelInsides[i];
                        var   delta  = rightChannelDeltas[i];
                        if (inside.x)
                        {
                            // above
                            if (delta.z <= bestAngleDeltas.z)
                            {
                                bestAngleDeltas.z     = delta.z;
                                bestMinMaxXYIndices.z = i;
                            }
                            // below
                            if (delta.w >= bestAngleDeltas.w)
                            {
                                bestAngleDeltas.w     = delta.w;
                                bestMinMaxXYIndices.w = i;
                            }
                        }
                        if (inside.y)
                        {
                            // right
                            if (delta.x <= bestAngleDeltas.x)
                            {
                                bestAngleDeltas.x     = delta.x;
                                bestMinMaxXYIndices.x = i;
                            }
                            // left
                            if (delta.y >= bestAngleDeltas.y)
                            {
                                bestAngleDeltas.y     = delta.y;
                                bestMinMaxXYIndices.y = i;
                            }
                        }
                    }

                    // Add our constraining indices to the pot
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

                    // Add additional candidates
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

                    // Compute weights
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

                for (int i = 0; i < profile.anglesPerLeftChannel.Length; i++)
                {
                    if (profile.channelDspsLeft[i].volume <= 0f)
                        weights.channelWeights[i] = 0f;
                }
                for (int i = 0; i < profile.anglesPerRightChannel.Length; i++)
                {
                    if (profile.channelDspsRight[i].volume <= 0f)
                        weights.channelWeights[i + profile.anglesPerLeftChannel.Length] = 0f;
                }
            }
        }
    }
}

