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

        public static void OnAudioFormatChanged(ref IAudioEcsSystemRunner.AudioFormatChangedContext context)
        {
            var          query    = context.auxWorld.AllWith<ListenerPresamplingState>();
            Span<Entity> entities = stackalloc Entity[query.Count()];
            int          count    = 0;
            foreach ((var entity, _) in query)
            {
                entities[count] = entity;
                count++;
            }
            foreach (var entity in entities)
            {
                context.auxWorld.RemoveComponent<ListenerPresamplingState>(entity);
            }
        }

        /*public static void OnUpdate(ref IAudioEcsSystemRunner.UpdateContext context)
           {
            using var profilerMarker = kMarker.Auto();

            // If a blob changed the number of channels, release all presampling.
            foreach ((var entity, var state, var listener) in context.auxWorld.AllWith<ListenerPresamplingState, AudioListener>())
            {
                if (state.aux.previousBlob != listener.aux.ildProfile)
                {
                    state.aux.Dispose();
                    ref var blob         = ref listener.aux.ildProfile.Value;
                    var     channelCount = blob.channelDspsLeft.Length + blob.channelDspsRight.Length;
                    state.aux            = new ListenerPresamplingState
                    {
                        channels     = new UnsafeList<PresampledChannel>(channelCount, context.auxWorld.allocator),
                        previousBlob = listener.aux.ildProfile
                    };
                }
            }

            // Read updates
            for (int i = context.visualFrameUpdates.length - 1; i >= 0; i--)
            {
                var update = context.visualFrameUpdates[i];
                foreach (var message in update.pipeReader.Each<PresampledBufferMessage>())
                {
                    if (!TryGetOrCreateListenerPresamplingState(message.listenerEntity, ref context, out var state))
                        continue;

                    ref var channel = ref GetOrCreateChannel(state, message.listenerEntity, message.listenerChannel, ref context);
                    // Todo:
                }
            }
           }

           static bool TryGetOrCreateListenerPresamplingState(Entity entity, ref IAudioEcsSystemRunner.UpdateContext context, out AuxRef<ListenerPresamplingState> state)
           {
            if (context.auxWorld.TryGetComponent(entity, out state))
                return true;

            if (!context.auxWorld.TryGetComponent<AudioListener>(entity, out var listener))
            {
                // This isn't a known listener.
                state = default;
                return false;
            }

            // New listener
            ref var blob         = ref listener.aux.ildProfile.Value;
            var     channelCount = blob.channelDspsLeft.Length + blob.channelDspsRight.Length;
            context.auxWorld.AddComponent(entity, new ListenerPresamplingState
            {
                channels     = new UnsafeList<PresampledChannel>(channelCount, context.auxWorld.allocator),
                previousBlob = listener.aux.ildProfile
            });
            context.auxWorld.TryGetComponent(entity, out state);
            state.aux.channels.AddReplicate(default, channelCount);
            return true;
           }

           static ref PresampledChannel GetOrCreateChannel(AuxRef<ListenerPresamplingState> state, Entity entity, int channelIndex, ref IAudioEcsSystemRunner.UpdateContext context)
           {
            ref var result = ref state.aux.channels.ElementAt(channelIndex);
            if (result.presampledFrames.IsCreated)
                return ref result;

            int capacity = 8;
            if (context.auxWorld.TryGetComponent<AudioSettings>(context.worldBlackboardEntity, out var settings))
            {
                capacity = settings.aux.audioFramesPerUpdate * 2 + settings.aux.lookaheadAudioFrames + settings.aux.safetyAudioFrames;
            }
            result.presampledFrames   = new UnsafeList<PresampledFrame>(capacity, context.auxWorld.allocator);
            ref var leftChannelsBlob  = ref state.aux.previousBlob.Value.channelDspsLeft;
            ref var rightChannelsBlob = ref state.aux.previousBlob.Value.channelDspsRight;
            ref var channel           = ref leftChannelsBlob[0];
            if (channelIndex < leftChannelsBlob.Length)
                channel = ref leftChannelsBlob[channelIndex];
            else
                channel = ref rightChannelsBlob[channelIndex - leftChannelsBlob.Length];
            if (channel.filters.Length > 0)
            {
                result.filters = new UnsafeList<PresampledChannel.Svf>(channel.filters.Length, context.auxWorld.allocator);
                foreach (var filter in channel.filters.AsSpan())
                {
                    result.filters.Add(new PresampledChannel.Svf
                    {
                        channel      = default,
                        coefficients =
                            StateVariableFilter.CreateFilterCoefficients(filter.type, filter.cutoff, filter.q, filter.gainInDecibels, context.finalOutputBuffer.sampleRate)
                    });
                }
            }
            return ref result;
           }

           static void InsertNewUpdates(ref PresampledChannel channel, PresampledBufferMessage message, int visualFrameId, ref IAudioEcsSystemRunner.UpdateContext context)
           {
            int frameCount = message.samples.length / context.finalOutputBuffer.samplesPerChannel;
            if (channel.presampledFrames.IsEmpty)
            {
                // Queue is empty. Dump everything in and return.

                for (int i = 0; i < frameCount; i++)
                {
                    if (message.samples.length == 0)
                    {  //
                    }

                    var frame = new PresampledFrame
                    {
                        allocator       = context.auxWorld.allocator,
                        extraSample     = message.samples[(i + 1) * context.finalOutputBuffer.samplesPerChannel],
                        nextUpdateFrame = message.nextUpdateFrame,
                        sampleCount     = context.finalOutputBuffer.samplesPerChannel,
                        samples         = AllocatorManager.Allocate<float>(context.auxWorld.allocator, context.finalOutputBuffer.samplesPerChannel),
                        targetFrame     = message.targetFrame + i,
                        visualFrameId   = visualFrameId,
                    };
                    var src = message.samples.AsSpan().Slice(i * context.finalOutputBuffer.samplesPerChannel, context.finalOutputBuffer.samplesPerChannel);
                    var dst = new Span<float>(frame.samples, frame.sampleCount);
                    src.CopyTo(dst);
                }
                return;
            }

            // If this update is late, discard it.
            if (channel.presampledFrames[0].nextUpdateFrame > message.targetFrame)
                return;

            int targetMessageFrame       = message.targetFrame;
            int targetMessageFrameOffset = 0;
            for (int i = 0; i < channel.presampledFrames.Length; i++)
            {
                if (channel.presampledFrames[i].targetFrame >= targetMessageFrame)
                {
                    if (message.samples.length == 0)
                    {
                        // Force silence.
                    }

                    ref var frameToOverwrite = ref channel.presampledFrames.ElementAt(i);
                    var     src              = message.samples.AsSpan().Slice(targetMessageFrameOffset * context.finalOutputBuffer.samplesPerChannel,
                                                                          context.finalOutputBuffer.samplesPerChannel);
                    var dst = new Span<float>(frameToOverwrite.samples, frameToOverwrite.sampleCount);
                    src.CopyTo(dst);
                    frameToOverwrite.targetFrame = targetMessageFrame;
                }
            }
            // Todo:
           }*/
    }
}

