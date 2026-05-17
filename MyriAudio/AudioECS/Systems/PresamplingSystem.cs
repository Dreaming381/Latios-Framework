using System;
using Latios.AuxEcs;
using Latios.Myri.DSP;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;

namespace Latios.Myri.AudioEcsBuiltin
{
    public static unsafe class PresamplingSystem
    {
        static readonly ProfilerMarker kMarker = new ProfilerMarker("PresamplingSystem");

        public static void OnUpdate(ref IAudioEcsSystemRunner.UpdateContext context)
        {
            using var profilerMarker = kMarker.Auto();

            bool starved = false;

            // Remove dead listeners
            {
                var          listenerEnumerator = context.auxWorld.AllWith<ListenerPresamplingState>();
                Span<Entity> deadListeners      = stackalloc Entity[listenerEnumerator.Count()];
                int          deadListenerCount  = 0;
                foreach ((var entity, _) in listenerEnumerator)
                {
                    if (!context.auxWorld.TryGetComponent<AudioListener>(entity, out _))
                    {
                        deadListeners[deadListenerCount] = entity;
                        deadListenerCount++;
                    }
                }
                deadListeners = deadListeners.Slice(0, deadListenerCount);
                foreach (var entity in deadListeners)
                    context.auxWorld.RemoveComponent<ListenerPresamplingState>(entity);
            }

            // Read updates
            for (int i = context.visualFrameUpdates.length - 1; i >= 0; i--)
            {
                var update = context.visualFrameUpdates[i];
                foreach (var message in update.pipeReader.Each<PresampledListenerMessage>())
                {
                    if (!TryGetOrCreateListenerPresamplingState(message.listenerEntity, ref context, out var state))
                        continue;

                    ref var updates = ref state.aux.updates;
                    bool    add     = updates.IsEmpty;
                    if (!add)
                    {
                        add             = true;
                        var preexisting = updates[updates.Length - 1];
                        if (preexisting.targetFrame == message.targetFrame)
                        {
                            add = false;
                            // If this new update targets the same frame as an old update, either replace it, or drop it if the old update already started.
                            if (updates[0].targetFrame >= context.feedbackID || updates[0].nextUpdateFrame <= preexisting.targetFrame)
                            {
                                // Replace
                                preexisting.profile              = message.profile;
                                preexisting.buffer               = message.buffer;
                                preexisting.nextUpdateFrame      = message.nextUpdateFrame;
                                preexisting.audioFramesInUpdate  = message.audioFramesInUpdate;
                                preexisting.targetFrame          = message.targetFrame;
                                preexisting.sampleRate           = message.sampleRate;
                                preexisting.samplesPerAudioFrame = message.samplesPerAudioFrame;
                                preexisting.startOffsetInBufferByChannel.Clear();
                                foreach (var offset in message.startOffsetInBufferByChannel)
                                    preexisting.startOffsetInBufferByChannel.Add(offset);
                                updates[updates.Length - 1] = preexisting;
                            }
                        }
                    }

                    if (add)
                    {
                        var startOffsets = new UnsafeList<int>(message.startOffsetInBufferByChannel.length, context.auxWorld.allocator);
                        foreach (var offset in message.startOffsetInBufferByChannel)
                            startOffsets.Add(offset);
                        state.aux.updates.Add(new PresampledUpdate
                        {
                            audioFramesInUpdate          = message.audioFramesInUpdate,
                            buffer                       = message.buffer,
                            nextUpdateFrame              = message.nextUpdateFrame,
                            profile                      = message.profile,
                            sampleRate                   = message.sampleRate,
                            samplesPerAudioFrame         = message.samplesPerAudioFrame,
                            startOffsetInBufferByChannel = startOffsets,
                            targetFrame                  = message.targetFrame,
                        });
                    }
                }
            }

            // Process each listener
            foreach ((var entity, var state, var output) in context.auxWorld.AllWith<ListenerPresamplingState, ListenerStereoMix>())
            {
                ref var updates = ref state.aux.updates;
                if (updates.IsEmpty || updates[0].targetFrame > context.feedbackID)
                    continue;

                // Dequeue old updates either when we advance to the next update, or when the config changed
                if (updates[0].nextUpdateFrame <= context.feedbackID ||
                    updates[0].sampleRate != context.finalOutputBuffer.sampleRate ||
                    updates[0].samplesPerAudioFrame != context.finalOutputBuffer.samplesPerChannel)
                {
                    // If we have a followup update queued that we can play, exhausted the current update, or encountered a config change,
                    // dequeue the first update, then repeat.
                    while ((updates.Length > 1 && updates[1].targetFrame <= context.feedbackID) ||
                           updates[0].targetFrame + updates[0].audioFramesInUpdate <= context.feedbackID ||
                           updates[0].sampleRate != context.finalOutputBuffer.sampleRate ||
                           updates[0].samplesPerAudioFrame != context.finalOutputBuffer.samplesPerChannel)
                    {
                        updates[0].Dispose();
                        updates.RemoveAt(0);
                        if (updates.IsEmpty)
                            break;
                    }
                }
                if (updates.IsEmpty || updates[0].targetFrame > context.feedbackID)
                {
                    // We starved.
                    starved = true;
                    continue;
                }

                var update = updates[0];

                // Handle blob changes
                float channelChangeDestepSampleLeft  = 0f;
                float channelChangeDestepSampleRight = 0f;
                if (update.profile != state.aux.previousBlob || state.aux.previousSampleRate != context.finalOutputBuffer.sampleRate)
                {
                    ref var newBlob  = ref update.profile.Value;
                    ref var channels = ref state.aux.channels;
                    if (!state.aux.previousBlob.IsCreated ||
                        newBlob.channelDspsLeft.Length != state.aux.previousBlob.Value.channelDspsLeft.Length ||
                        newBlob.channelDspsRight.Length != state.aux.previousBlob.Value.channelDspsRight.Length)
                    {
                        // Run each old channel's destep channel through the filter, and then dispose the channel.
                        int channelIndex = 0;
                        foreach (var channel in channels)
                        {
                            var sample = channel.destepSample;
                            foreach (var filter in channel.filters)
                            {
                                var c  = filter.channel;
                                sample = StateVariableFilter.ProcessSample(ref c, in filter.coefficients, sample);
                            }
                            sample *= channel.volume;
                            if (channelIndex < state.aux.previousBlob.Value.channelDspsLeft.Length)
                                channelChangeDestepSampleLeft += sample;
                            else
                                channelChangeDestepSampleRight += sample;
                            channel.Dispose();
                            channelIndex++;
                        }
                        channels.Clear();

                        // Create the new channels
                        for (int i = 0; i < newBlob.channelDspsLeft.Length; i++)
                        {
                            ref var                           dsp  = ref newBlob.channelDspsLeft[i];
                            UnsafeList<PresampledChannel.Svf> svfs = default;
                            if (dsp.filters.Length > 0)
                            {
                                svfs = new UnsafeList<PresampledChannel.Svf>(dsp.filters.Length, context.auxWorld.allocator);
                                for (int j = 0; j < dsp.filters.Length; j++)
                                {
                                    var filter = dsp.filters[j];
                                    svfs.Add(new PresampledChannel.Svf
                                    {
                                        channel      = default,
                                        coefficients =
                                            StateVariableFilter.CreateFilterCoefficients(filter.type, filter.cutoff, filter.q, filter.gainInDecibels,
                                                                                         context.finalOutputBuffer.sampleRate)
                                    });
                                }
                            }
                            channels.Add(new PresampledChannel
                            {
                                destepSample = 0f,
                                filters      = svfs,
                                volume       = dsp.volume,
                            });
                        }
                        for (int i = 0; i < newBlob.channelDspsRight.Length; i++)
                        {
                            ref var                           dsp  = ref newBlob.channelDspsRight[i];
                            UnsafeList<PresampledChannel.Svf> svfs = default;
                            if (dsp.filters.Length > 0)
                            {
                                svfs = new UnsafeList<PresampledChannel.Svf>(dsp.filters.Length, context.auxWorld.allocator);
                                for (int j = 0; j < dsp.filters.Length; j++)
                                {
                                    var filter = dsp.filters[j];
                                    svfs.Add(new PresampledChannel.Svf
                                    {
                                        channel      = default,
                                        coefficients =
                                            StateVariableFilter.CreateFilterCoefficients(filter.type, filter.cutoff, filter.q, filter.gainInDecibels,
                                                                                         context.finalOutputBuffer.sampleRate)
                                    });
                                }
                            }
                            channels.Add(new PresampledChannel
                            {
                                destepSample = 0f,
                                filters      = svfs,
                                volume       = dsp.volume,
                            });
                        }
                    }
                    else
                    {
                        // Either the sample rate changed, the volume changed, or the filters changed. Recreate the coefficients without changing anything else.
                        int channelIndex = 0;
                        for (int i = 0; i < newBlob.channelDspsLeft.Length; i++, channelIndex++)
                        {
                            ref var channel = ref channels.ElementAt(channelIndex);
                            ref var dsp     = ref newBlob.channelDspsLeft[i];
                            channel.filters.Resize(dsp.filters.Length, NativeArrayOptions.ClearMemory);
                            for (int j = 0; j < dsp.filters.Length; j++)
                            {
                                ref var channelFilter      = ref channel.filters.ElementAt(j);
                                var     filter             = dsp.filters[j];
                                channelFilter.coefficients = StateVariableFilter.CreateFilterCoefficients(filter.type,
                                                                                                          filter.cutoff,
                                                                                                          filter.q,
                                                                                                          filter.gainInDecibels,
                                                                                                          context.finalOutputBuffer.sampleRate);
                            }
                            channel.volume = dsp.volume;
                        }
                        for (int i = 0; i < newBlob.channelDspsRight.Length; i++, channelIndex++)
                        {
                            ref var channel = ref channels.ElementAt(channelIndex);
                            ref var dsp     = ref newBlob.channelDspsRight[i];
                            channel.filters.Resize(dsp.filters.Length, NativeArrayOptions.ClearMemory);
                            for (int j = 0; j < dsp.filters.Length; j++)
                            {
                                ref var channelFilter      = ref channel.filters.ElementAt(j);
                                var     filter             = dsp.filters[j];
                                channelFilter.coefficients = StateVariableFilter.CreateFilterCoefficients(filter.type,
                                                                                                          filter.cutoff,
                                                                                                          filter.q,
                                                                                                          filter.gainInDecibels,
                                                                                                          context.finalOutputBuffer.sampleRate);
                            }
                            channel.volume = dsp.volume;
                        }
                    }
                    state.aux.previousBlob       = update.profile;
                    state.aux.previousSampleRate = context.finalOutputBuffer.sampleRate;
                }

                // Check if we can skip writing to the output.
                {
                    bool allChannelsEmpty = update.buffer == null;
                    if (!allChannelsEmpty)
                    {
                        bool negative = true;
                        foreach (var offset in update.startOffsetInBufferByChannel)
                            negative     &= offset < 0;
                        allChannelsEmpty  = negative;
                    }
                    if (allChannelsEmpty && channelChangeDestepSampleLeft == 0f && channelChangeDestepSampleRight == 0f)
                    {
                        ref var channels   = ref state.aux.channels;
                        bool    zeroDestep = true;
                        for (int i = 0; i < channels.Length; i++)
                        {
                            zeroDestep &= channels.ElementAt(i).destepSample == 0f;
                        }
                        if (zeroDestep)
                            continue;
                    }
                }

                // Perform sampling.
                {
                    ref var channels = ref state.aux.channels;
                    output.aux.GetToAdd(out var left, out var right);
                    var smoothRatePerSample = math.rcp(context.finalOutputBuffer.sampleRate * 0.015f);  // Small 15 millisecond smoothing
                    var frameOffset         = (context.feedbackID - update.targetFrame) * context.finalOutputBuffer.samplesPerChannel;
                    for (int i = 0; i < channels.Length; i++)
                    {
                        ref var c = ref channels.ElementAt(i);
                        if (update.startOffsetInBufferByChannel[i] < 0 && math.abs(c.destepSample) <= 1e-5f)
                            continue;

                        ref var lr             = ref (i < update.profile.Value.channelDspsLeft.Length ? ref left : ref right);
                        float   smoothFactor   = 0f;
                        var     startOffset    = update.startOffsetInBufferByChannel[i];
                        bool    hasValidBuffer = startOffset >= 0;
                        var     delta          = c.destepSample - (hasValidBuffer ? update.buffer[startOffset + frameOffset] : 0f);
                        for (int j = 0; j < lr.Length; j++)
                        {
                            var sample   = hasValidBuffer ? update.buffer[startOffset + frameOffset + j] : 0f;
                            smoothFactor = math.min(j * smoothRatePerSample, 1f);
                            if (!hasValidBuffer && smoothFactor == 1f)
                                break;

                            sample += math.lerp(delta, 0f, smoothFactor);
                            for (int k = 0; k < c.filters.Length; k++)
                            {
                                ref var filter = ref c.filters.ElementAt(k);
                                sample         = StateVariableFilter.ProcessSample(ref filter.channel, in filter.coefficients, sample);
                            }
                            lr[j] += sample * c.volume;
                        }
                        c.destepSample = hasValidBuffer ? update.buffer[startOffset + frameOffset + lr.Length] : 0f;
                    }

                    // Apply blob configuration correction.
                    Destep(channelChangeDestepSampleLeft,  left,  context.finalOutputBuffer.sampleRate);
                    Destep(channelChangeDestepSampleRight, right, context.finalOutputBuffer.sampleRate);
                }
            }

            if (starved)
            {
                if (context.auxWorld.TryGetComponent<AudioSettings>(context.worldBlackboardEntity, out var settings))
                {
                    if (settings.aux.logWarningIfBuffersAreStarved)
                    {
                        UnityEngine.Debug.LogWarning($"Audio buffers starved for audio frame {context.feedbackID}.");
                    }
                }
            }
        }

        static bool TryGetOrCreateListenerPresamplingState(Entity entity, ref IAudioEcsSystemRunner.UpdateContext context, out AuxRef<ListenerPresamplingState> state)
        {
            if (context.auxWorld.TryGetComponent(entity, out state))
                return true;

            if (!context.auxWorld.TryGetComponent<AudioListener>(entity, out var listener))
            {
                // This isn't a known listener. The update might be from a listener that was since destroyed.
                state = default;
                return false;
            }

            // New listener
            ref var blob         = ref listener.aux.ildProfile.Value;
            var     channelCount = blob.channelDspsLeft.Length + blob.channelDspsRight.Length;
            context.auxWorld.AddComponent(entity, new ListenerPresamplingState
            {
                channels           = new UnsafeList<PresampledChannel>(channelCount, context.auxWorld.allocator),
                previousBlob       = default,
                updates            = new UnsafeList<PresampledUpdate>(16, context.auxWorld.allocator),
                nextUpdateFrame    = -1,
                previousSampleRate = 0,
            });
            context.auxWorld.TryGetComponent(entity, out state);
            return true;
        }

        static void Destep(float delta, Span<float> samples, int sampleRate)
        {
            if (delta == 0f)
                return;
            var   smoothRatePerSample = math.rcp(sampleRate * 0.015f);  // Small 15 millisecond smoothing
            float smoothFactor        = 0f;
            for (int i = 0; i < samples.Length && smoothFactor < 1f; i++)
            {
                smoothFactor  = math.min(i * smoothRatePerSample, 1f);
                samples[i]   += math.lerp(delta, 0f, smoothFactor);
            }
        }
    }
}

