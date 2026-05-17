using Latios.Calci;
using Latios.Myri.AudioEcsBuiltin;
using Latios.Transforms;
using Latios.Transforms.Abstract;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Myri
{
    internal static class InitUpdateDestroy
    {
        [BurstCompile]
        public struct CaptureIldFrameJob : IJob
        {
            public NativeReference<CapturedFrameState> capturedFrameState;

            public NativeQueue<AudioFrameBufferHistoryElement>           audioFrameHistory;
            [ReadOnly] public ComponentLookup<AudioSettings>             audioSettingsLookup;
            [ReadOnly] public ComponentLookup<AudioEcsAtomicFeedbackIds> atomicLookup;
            [ReadOnly] public ComponentLookup<AudioEcsFormat>            formatLookup;
            public Entity                                                worldBlackboardEntity;

            public unsafe void Execute()
            {
                var ids      = atomicLookup[worldBlackboardEntity].Read();
                int frame    = ids.feedbackIdStarted;
                int bufferId = ids.maxCommandIdConsumed;
                var settings = audioSettingsLookup[worldBlackboardEntity];

                while (!audioFrameHistory.IsEmpty() && audioFrameHistory.Peek().bufferId < bufferId)
                {
                    audioFrameHistory.Dequeue();
                }
                int targetFrame = frame + 1 + math.max(settings.lookaheadAudioFrames, 0);
                if (!audioFrameHistory.IsEmpty() && audioFrameHistory.Peek().bufferId == bufferId)
                {
                    targetFrame = math.max(audioFrameHistory.Peek().expectedNextUpdateFrame, targetFrame);
                }
                var oldState             = capturedFrameState.Value;
                var format               = formatLookup[worldBlackboardEntity].audioFormat;
                var resetSources         = oldState.format.sampleRate != format.sampleRate || oldState.format.bufferFrameCount != format.bufferFrameCount;
                capturedFrameState.Value = new CapturedFrameState
                {
                    audioFrame           = targetFrame,
                    lastConsumedBufferId = bufferId,
                    lastPlayedAudioFrame = frame,
                    format               = format,
                    audioSettings        = settings,
                    requiresSourceReset  = resetSources,
                };
            }
        }

        // Single
        [BurstCompile]
        public struct CaptureListenersForSamplingJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle                         entityHandle;
            [ReadOnly] public ComponentTypeHandle<AudioListener>       listenerHandle;
            [ReadOnly] public WorldTransformReadOnlyAspect.TypeHandle  worldTransformHandle;
            [ReadOnly] public BufferTypeHandle<AudioListenerChannelID> channelGuidHandle;
            public NativeList<ListenerWithTransform>                   listenersWithTransforms;
            public NativeList<ListenerWithPresampling>                 listenersWithPresampling;
            public NativeList<AudioSourceChannelID>                    listenersChannelIDs;
            public NativeList<ListenerWithPresampling>                 culledListeners;
            public NativeArray<int>                                    channelCount;
            public NativeArray<int>                                    sourceChunkChannelCount;
            public int                                                 sourceChunkCount;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var entities        = chunk.GetEntityDataPtrRO(entityHandle);
                var listeners       = chunk.GetComponentDataPtrRO(ref listenerHandle);
                var worldTransforms = worldTransformHandle.Resolve(chunk);
                var channelsBuffers = chunk.GetBufferAccessor(ref channelGuidHandle);
                for (int i = 0; i < chunk.Count; i++)
                {
                    var l = listeners[i];
                    if (l.volume > 0f && sourceChunkCount > 0)
                    {
                        int2 range;
                        if (channelsBuffers.Length > 0)
                        {
                            var buffer = channelsBuffers[i];
                            range      = new int2(listenersChannelIDs.Length, buffer.Length);
                            listenersChannelIDs.AddRange(buffer.AsNativeArray().Reinterpret<AudioSourceChannelID>());
                        }
                        else
                        {
                            range = new int2(listenersChannelIDs.Length, 0);
                        }

                        var transform                                                    = new RigidTransform(worldTransforms[i].rotation, worldTransforms[i].position);
                        listenersWithTransforms.Add(new ListenerWithTransform { listener = l, transform = transform, channelIDsRange = range });

                        var channelsInListener      = l.ildProfile.Value.anglesPerLeftChannel.Length + l.ildProfile.Value.anglesPerRightChannel.Length;
                        channelCount[0]            += channelsInListener;
                        sourceChunkChannelCount[0] += channelsInListener * sourceChunkCount;

                        listenersWithPresampling.Add(new ListenerWithPresampling
                        {
                            listener = entities[i],
                            profile  = l.ildProfile,
                        });
                    }
                    else
                    {
                        culledListeners.Add(new ListenerWithPresampling
                        {
                            listener = entities[i],
                            profile  = l.ildProfile,
                        });
                    }
                }
            }
        }

        // Schedule single
        [BurstCompile]
        public struct UpdateChangedListenersJob : IJobChunk
        {
            [ReadOnly] public EntityTypeHandle                         entityHandle;
            [ReadOnly] public ComponentTypeHandle<AudioListener>       listenerHandle;
            [ReadOnly] public BufferTypeHandle<AudioListenerChannelID> channelGuidHandle;
            [ReadOnly] public AudioEcsCommandPipe                      commandPipe;
            public EntityCommandBuffer                                 ecb;
            public uint                                                lastSystemVersion;

            WorldTransformReadOnlyAspect.HasChecker worldTransformChecker;
            HasChecker<TrackedListener>             trackedListenerChecker;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                bool isAlive   = worldTransformChecker[chunk] && chunk.Has(ref listenerHandle);
                bool isTracked = trackedListenerChecker[chunk];

                if (isAlive)
                {
                    if (!isTracked || chunk.DidOrderChange(lastSystemVersion) ||
                        chunk.DidChange(ref listenerHandle, lastSystemVersion) || chunk.DidChange(ref channelGuidHandle, lastSystemVersion))
                    {
                        var entities         = chunk.GetNativeArray(entityHandle);
                        var listeners        = chunk.GetComponentDataPtrRO(ref listenerHandle);
                        var channelIDBuffers = chunk.GetBufferAccessorRO(ref channelGuidHandle);
                        for (int i = 0; i < chunk.Count; i++)
                        {
                            ref var message = ref commandPipe.pipe.CreateMessage<NewOrChangedListenerMessage>();
                            message         = new NewOrChangedListenerMessage
                            {
                                audioListener = listeners[i],
                                entity        = entities[i],
                                hasChannels   = channelIDBuffers.Length > 0,
                            };
                            if (channelIDBuffers.Length > 0)
                            {
                                var buffer = channelIDBuffers[i];
                                var span   = commandPipe.pipe.CreatePipeSpan<AudioListenerChannelID>(buffer.Length);
                                buffer.AsNativeArray().AsReadOnlySpan().CopyTo(span.AsSpan());
                                message.channels = span;
                            }
                        }

                        if (!isTracked)
                        {
                            var tracker = new TrackedListener { hasChannelIDs = channelIDBuffers.Length > 0 };
                            ecb.AddComponent(entities, tracker);
                        }
                    }
                }
                else if (isTracked)
                {
                    var     entities = chunk.GetNativeArray(entityHandle);
                    ref var message  = ref commandPipe.pipe.CreateMessage<RemovedListenersMessage>();
                    var     span     = commandPipe.pipe.CreatePipeSpan<Entity>(entities.Length);
                    entities.AsReadOnlySpan().CopyTo(span.AsSpan());
                    message.formerListenerEntities = span;
                    ecb.RemoveComponent<TrackedListener>(entities);
                }
            }
        }

        [BurstCompile]
        public struct UpdateClipAudioSourcesJob : IJobChunk
        {
            public NativeStream.Writer                                             stream;
            public ComponentTypeHandle<AudioSourceClip>                            clipHandle;
            public ComponentTypeHandle<AudioSourceDestroyOneShotWhenFinished>      expireHandle;
            [ReadOnly] public ComponentTypeHandle<AudioSourceVolume>               volumeHandle;
            [ReadOnly] public ComponentTypeHandle<AudioSourceSampleRateMultiplier> sampleRateMultiplierHandle;
            [ReadOnly] public ComponentTypeHandle<AudioSourceDistanceFalloff>      distanceFalloffHandle;
            [ReadOnly] public ComponentTypeHandle<AudioSourceEmitterCone>          emitterConeHandle;
            [ReadOnly] public ComponentTypeHandle<AudioSourceChannelID>            channelIDHandle;
            [ReadOnly] public WorldTransformReadOnlyAspect.TypeHandle              worldTransformHandle;
            [ReadOnly] public NativeReference<CapturedFrameState>                  capturedFrameState;
            [ReadOnly] public AudioEcsCommandPipe                                  commandPipe;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var audioFrame            = capturedFrameState.Value.audioFrame;
                var rng                   = new Rng.RngSequence(math.asuint(new int2(audioFrame, unfilteredChunkIndex)));
                var volumes               = (AudioSourceVolume*)chunk.GetRequiredComponentDataPtrRO(ref volumeHandle);
                var clips                 = (AudioSourceClip*)chunk.GetRequiredComponentDataPtrRW(ref clipHandle);
                var hasTransforms         = worldTransformHandle.Has(in chunk);
                var transforms            = worldTransformHandle.Resolve(chunk);
                var sampleRateMultipliers = chunk.GetComponentDataPtrRO(ref sampleRateMultiplierHandle);
                var falloffs              = chunk.GetComponentDataPtrRO(ref distanceFalloffHandle);
                var cones                 = chunk.GetComponentDataPtrRO(ref emitterConeHandle);
                var channelGuids          = chunk.GetComponentDataPtrRO(ref channelIDHandle);
                var expireMask            = chunk.GetEnabledMask(ref expireHandle);
                var bufferId              = commandPipe.commandId;
                var format                = capturedFrameState.Value.format;
                var sampleRate            = format.sampleRate;
                var samplesPerFrame       = format.bufferFrameCount;
                var reset                 = capturedFrameState.Value.requiresSourceReset;

                stream.BeginForEachIndex(unfilteredChunkIndex);
                for (int i = 0; i < chunk.Count; i++)
                {
                    // Clip
                    ref var clip = ref clips[i];

                    if (!clip.m_clip.IsCreated)
                        continue;

                    if (reset)
                        clip.ResetPlaybackState();

                    double sampleRateMultiplier = sampleRateMultipliers != null ? sampleRateMultipliers[i].multiplier : 1.0;
                    if (clip.looping)
                    {
                        if (sampleRateMultiplier <= 0.0)
                            continue;

                        if (!clip.m_initialized)
                        {
                            if (clip.offsetIsBasedOnSpawn)
                            {
                                ulong samplesPlayed = (ulong)samplesPerFrame * (ulong)audioFrame;
                                if (sampleRate == clip.m_clip.Value.sampleRate && sampleRateMultiplier == 1.0)
                                {
                                    int clipStart     = (int)(samplesPlayed % (ulong)clip.m_clip.Value.sampleCountPerChannel);
                                    clip.m_loopOffset = (uint)(clip.m_clip.Value.sampleCountPerChannel - clipStart);
                                }
                                else
                                {
                                    double clipSampleStride             = clip.m_clip.Value.sampleRate * sampleRateMultiplier / sampleRate;
                                    double samplesPlayedInSourceSamples = samplesPlayed * clipSampleStride;
                                    double clipStart                    = samplesPlayedInSourceSamples % clip.m_clip.Value.sampleCountPerChannel;
                                    // We can't get exact due to the mismatched rate, so we choose a rounded start point between
                                    // the last and first sample by chopping off the fractional part
                                    clip.m_loopOffset = (uint)(clip.m_clip.Value.sampleCountPerChannel - clipStart);
                                }
                                clip.m_spawnedBufferId = bufferId;
                            }
                            else
                            {
                                clip.m_loopOffset   = (uint)clip.m_clip.Value.loopedOffsets[rng.NextInt(0, clip.m_clip.Value.loopedOffsets.Length)];
                                clip.m_offsetLocked = true;
                            }
                            clip.m_initialized = true;
                        }
                        else if (!clip.m_offsetLocked && clip.offsetIsBasedOnSpawn)
                        {
                            if (capturedFrameState.Value.lastConsumedBufferId - clip.m_spawnedBufferId >= 0)
                            {
                                clip.m_offsetLocked = true;
                            }
                            else
                            {
                                // This check compares if the playhead loop advanced past the target start point in the loop
                                ulong  samplesPlayed                = (ulong)samplesPerFrame * (ulong)audioFrame;
                                double clipSampleStride             = clip.m_clip.Value.sampleRate * sampleRateMultiplier / sampleRate;
                                double samplesPlayedInSourceSamples = samplesPlayed * clipSampleStride;
                                double clipStart                    = (samplesPlayedInSourceSamples + clip.m_loopOffset) % clip.m_clip.Value.sampleCountPerChannel;
                                // We add a one sample tolerance in case we are regenerating the same audio frame, in which case the old values are fine.
                                if (clipStart < clip.m_clip.Value.sampleCountPerChannel / 2 && clipStart > 1)
                                {
                                    // We missed the buffer
                                    clipStart              = samplesPlayedInSourceSamples % clip.m_clip.Value.sampleCountPerChannel;
                                    clip.m_loopOffset      = (uint)(clip.m_clip.Value.sampleCountPerChannel - clipStart);
                                    clip.m_spawnedBufferId = bufferId;
                                }
                            }
                        }
                    }
                    else
                    {
                        // If the sample rate is invalid, prefer to just kill this oneshot.
                        if (sampleRateMultiplier <= 0.0)
                        {
                            if (expireMask.EnableBit.IsValid)
                                expireMask[i] = false;
                            continue;
                        }

                        var framesPlayed = capturedFrameState.Value.lastPlayedAudioFrame - clip.m_spawnedAudioFrame;
                        // There's a chance the one shot spawned last game frame but the dsp missed the audio frame.
                        // In such a case, we still want the one shot to start at the beginning rather than skip the first audio frame.
                        // This is more likely to happen in high framerate scenarios.
                        // This does not solve the problem where the audio frame ticks during DSP and again before the next AudioSystemUpdate.
                        if ((!clip.m_initialized) || (clip.m_spawnedBufferId - capturedFrameState.Value.lastConsumedBufferId > 0 && framesPlayed >= 0))
                        {
                            clip.m_spawnedBufferId   = bufferId;
                            clip.m_spawnedAudioFrame = audioFrame;
                            clip.m_initialized       = true;
                        }
                        else
                        {
                            double resampleRate = clip.m_clip.Value.sampleRate * sampleRateMultiplier / sampleRate;
                            if (clip.m_initialized && clip.m_clip.Value.sampleCountPerChannel < resampleRate * framesPlayed * samplesPerFrame)
                            {
                                if (expireMask.EnableBit.IsValid)
                                    expireMask[i] = false;
                                continue;
                            }
                        }
                    }

                    var batchableClip = clip;
                    if (clip.looping)
                        batchableClip.FixLoopingForBatching();

                    // Early culling
                    if (volumes[i].volume <= 0f)
                        continue;
                    if (falloffs != null && !hasTransforms)
                        continue;

                    // Write to stream
                    int byteCount  = 0;
                    var features   = CapturedSourceHeader.Features.Clip;
                    byteCount     += CollectionHelper.Align(UnsafeUtility.SizeOf<AudioSourceClip>(), 8);
                    if (sampleRateMultiplier != 1.0)
                    {
                        features  |= CapturedSourceHeader.Features.SampleRateMultiplier;
                        byteCount += 8;  // double is 8 bytes
                    }
                    if (channelGuids != null)
                    {
                        features  |= CapturedSourceHeader.Features.ChannelID;
                        byteCount += CollectionHelper.Align(UnsafeUtility.SizeOf<AudioSourceChannelID>(), 8);
                    }
                    if (falloffs != null)
                    {
                        features  |= CapturedSourceHeader.Features.Transform | CapturedSourceHeader.Features.DistanceFalloff;
                        byteCount += CollectionHelper.Align(UnsafeUtility.SizeOf<TransformQvvs>(), 8);
                        byteCount += CollectionHelper.Align(UnsafeUtility.SizeOf<AudioSourceDistanceFalloff>(), 8);

                        if (cones != null)
                        {
                            features  |= CapturedSourceHeader.Features.Cone;
                            byteCount += CollectionHelper.Align(UnsafeUtility.SizeOf<AudioSourceEmitterCone>(), 8);
                        }
                    }

                    stream.Write(new CapturedSourceHeader { features = features, volume = volumes[i].volume });

                    var ptr = stream.Allocate(byteCount);
                    UnsafeUtility.MemClear(ptr, byteCount);
                    UnsafeUtility.CopyStructureToPtr(ref batchableClip, ptr);
                    ptr += CollectionHelper.Align(UnsafeUtility.SizeOf<AudioSourceClip>(), 8);
                    if (sampleRateMultiplier != 1.0)
                    {
                        UnsafeUtility.CopyStructureToPtr(ref sampleRateMultiplier, ptr);
                        ptr += 8;
                    }
                    if (falloffs != null)
                    {
                        var transform = transforms[i].worldTransformQvvs;
                        UnsafeUtility.CopyStructureToPtr(ref transform,   ptr);
                        ptr += CollectionHelper.Align(UnsafeUtility.SizeOf<TransformQvvs>(), 8);

                        UnsafeUtility.CopyStructureToPtr(ref falloffs[i], ptr);
                        ptr += CollectionHelper.Align(UnsafeUtility.SizeOf<AudioSourceDistanceFalloff>(), 8);

                        if (cones != null)
                        {
                            UnsafeUtility.CopyStructureToPtr(ref cones[i], ptr);
                            ptr += CollectionHelper.Align(UnsafeUtility.SizeOf<AudioSourceEmitterCone>(), 8);
                        }
                    }
                    if (channelGuids != null)
                    {
                        UnsafeUtility.CopyStructureToPtr(ref channelGuids[i], ptr);
                        ptr += CollectionHelper.Align(UnsafeUtility.SizeOf<AudioSourceChannelID>(), 8);
                    }
                }
                stream.EndForEachIndex();
            }
        }
    }
}

