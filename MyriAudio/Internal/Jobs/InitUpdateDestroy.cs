using Latios.Transforms.Abstract;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Myri
{
    internal static class InitUpdateDestroy
    {
        //Parallel
        [BurstCompile]
        public struct DestroyOneshotsWhenFinishedJob : IJobChunk
        {
            public ComponentTypeHandle<AudioSourceDestroyOneShotWhenFinished> expireHandle;
            [ReadOnly] public ComponentTypeHandle<AudioSourceOneShot>         oneshotHandle;
            [ReadOnly] public NativeReference<int>                            audioFrame;
            [ReadOnly] public NativeReference<int>                            lastPlayedAudioFrame;
            [ReadOnly] public ComponentLookup<AudioSettings>                  settingsLookup;
            public Entity                                                     worldBlackboardEntity;
            public int                                                        sampleRate;
            public int                                                        samplesPerFrame;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var oneshots = chunk.GetNativeArray(ref oneshotHandle);
                var mask     = chunk.GetEnabledMask(ref expireHandle);
                for (int i = 0; i < oneshots.Length; i++)
                {
                    var os = oneshots[i];
                    if (!os.m_clip.IsCreated)
                    {
                        mask[i] = false;
                        continue;
                    }
                    int    playedFrames = lastPlayedAudioFrame.Value - os.m_spawnedAudioFrame;
                    double resampleRate = os.clip.Value.sampleRate / (double)sampleRate;
                    if (os.isInitialized && os.clip.Value.samplesLeftOrMono.Length < resampleRate * playedFrames * samplesPerFrame)
                    {
                        mask[i] = false;
                    }
                }
            }
        }

        //Single
        [BurstCompile]
        public struct UpdateListenersJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<AudioListener>      listenerHandle;
            [ReadOnly] public WorldTransformReadOnlyAspect.TypeHandle worldTransformHandle;
            public NativeList<ListenerWithTransform>                  listenersWithTransforms;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var listeners       = chunk.GetNativeArray(ref listenerHandle);
                var worldTransforms = worldTransformHandle.Resolve(chunk);
                for (int i = 0; i < chunk.Count; i++)
                {
                    var l = listeners[i];
                    //This culling desyncs the listener indices from the graph handling logic.
                    //Todo: Figure out how to bring this optimization back.
                    //if (l.volume > 0f)
                    {
                        l.itdResolution                                                  = math.clamp(l.itdResolution, 0, 15);
                        var transform                                                    = new RigidTransform(worldTransforms[i].rotation, worldTransforms[i].position);
                        listenersWithTransforms.Add(new ListenerWithTransform { listener = l, transform = transform });
                    }
                }
            }
        }

        //Parallel
        //Todo: It might be worth it to cull here rather than write to the emitters array.
        [BurstCompile]
        public struct UpdateOneshotsJob : IJobChunk
        {
            public ComponentTypeHandle<AudioSourceOneShot>                           oneshotHandle;
            [ReadOnly] public ComponentTypeHandle<AudioSourceEmitterCone>            coneHandle;
            [ReadOnly] public WorldTransformReadOnlyAspect.TypeHandle                worldTransformHandle;
            [NativeDisableParallelForRestriction] public NativeArray<OneshotEmitter> emitters;
            [ReadOnly] public NativeReference<int>                                   audioFrame;
            [ReadOnly] public NativeReference<int>                                   lastPlayedAudioFrame;
            [ReadOnly] public NativeReference<int>                                   lastConsumedBufferId;
            public int                                                               bufferId;

            [ReadOnly, DeallocateOnJobCompletion] public NativeArray<int> firstEntityInChunkIndices;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var firstEntityIndex = firstEntityInChunkIndices[unfilteredChunkIndex];
                var oneshots         = chunk.GetNativeArray(ref oneshotHandle);
                for (int i = 0; i < chunk.Count; i++)
                {
                    var oneshot = oneshots[i];
                    //There's a chance the one shot spawned last game frame but the dsp missed the audio frame.
                    //In such a case, we still want the one shot to start at the beginning rather than skip the first audio frame.
                    //This is more likely to happen in high framerate scenarios.
                    //This does not solve the problem where the audio frame ticks during DSP and again before the next AudioSystemUpdate.
                    if ((!oneshot.isInitialized) || (oneshot.m_spawnedBufferId - lastConsumedBufferId.Value > 0 && (lastPlayedAudioFrame.Value - oneshot.m_spawnedAudioFrame >= 0)))
                    {
                        oneshot.m_spawnedBufferId   = bufferId;
                        oneshot.m_spawnedAudioFrame = audioFrame.Value;
                        oneshots[i]                 = oneshot;
                    }
                }

                if (chunk.Has(ref coneHandle))
                {
                    var worldTransforms = worldTransformHandle.Resolve(chunk);
                    var cones           = chunk.GetNativeArray(ref coneHandle);
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        emitters[firstEntityIndex + i] = new OneshotEmitter
                        {
                            source    = oneshots[i],
                            transform = new RigidTransform(worldTransforms[i].rotation, worldTransforms[i].position),
                            cone      = cones[i],
                            useCone   = true
                        };
                    }
                }
                else
                {
                    var worldTransforms = worldTransformHandle.Resolve(chunk);
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        emitters[firstEntityIndex + i] = new OneshotEmitter
                        {
                            source    = oneshots[i],
                            transform = new RigidTransform(worldTransforms[i].rotation, worldTransforms[i].position),
                            cone      = default,
                            useCone   = false
                        };
                    }
                }
            }
        }

        //Parallel
        //Todo: It might be worth it to cull here rather than write to the emitters array.
        [BurstCompile]
        public struct UpdateLoopedJob : IJobChunk
        {
            public ComponentTypeHandle<AudioSourceLooped>                           loopedHandle;
            [ReadOnly] public ComponentTypeHandle<AudioSourceEmitterCone>           coneHandle;
            [ReadOnly] public WorldTransformReadOnlyAspect.TypeHandle               worldTransformHandle;
            [NativeDisableParallelForRestriction] public NativeArray<LoopedEmitter> emitters;
            [ReadOnly] public NativeReference<int>                                  audioFrame;
            [ReadOnly] public NativeReference<int>                                  lastConsumedBufferId;
            public int                                                              bufferId;
            public int                                                              sampleRate;
            public int                                                              samplesPerFrame;

            [ReadOnly, DeallocateOnJobCompletion] public NativeArray<int> firstEntityInChunkIndices;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var firstEntityIndex = firstEntityInChunkIndices[unfilteredChunkIndex];
                var rng              = new Rng.RngSequence(math.asuint(new int2(audioFrame.Value, unfilteredChunkIndex)));
                var looped           = chunk.GetNativeArray(ref loopedHandle);
                for (int i = 0; i < chunk.Count; i++)
                {
                    var l = looped[i];

                    if (!l.initialized)
                    {
                        if (l.offsetIsBasedOnSpawn)
                        {
                            ulong samplesPlayed = (ulong)samplesPerFrame * (ulong)audioFrame.Value;
                            if (sampleRate == l.clip.Value.sampleRate)
                            {
                                int clipStart  = (int)(samplesPlayed % (ulong)l.clip.Value.samplesLeftOrMono.Length);
                                l.m_loopOffset = l.clip.Value.samplesLeftOrMono.Length - clipStart;
                            }
                            else
                            {
                                double clipSampleStride             = l.clip.Value.sampleRate / (double)sampleRate;
                                double samplesPlayedInSourceSamples = samplesPlayed * clipSampleStride;
                                double clipStart                    = samplesPlayedInSourceSamples % l.clip.Value.samplesLeftOrMono.Length;
                                // We can't get exact due to the mismatched rate, so we choose a rounded start point between
                                // the last and first sample by chopping off the fractional part
                                l.m_loopOffset = (int)(l.clip.Value.samplesLeftOrMono.Length - clipStart);
                            }
                            l.m_spawnBufferLow16 = (short)bufferId;
                        }
                        else
                        {
                            l.m_loopOffset = l.m_clip.Value.loopedOffsets[rng.NextInt(0, l.m_clip.Value.loopedOffsets.Length)];
                            l.offsetLocked = true;
                        }
                        l.initialized = true;
                        looped[i]     = l;
                    }
                    else if (!l.offsetLocked && l.offsetIsBasedOnSpawn)
                    {
                        if ((short)lastConsumedBufferId.Value - l.m_spawnBufferLow16 >= 0)
                        {
                            l.offsetLocked = true;
                        }
                        else
                        {
                            // This check compares if the playhead loop advanced past the target start point in the loop
                            ulong  samplesPlayed                = (ulong)samplesPerFrame * (ulong)audioFrame.Value;
                            double clipSampleStride             = l.clip.Value.sampleRate / (double)sampleRate;
                            double samplesPlayedInSourceSamples = samplesPlayed * clipSampleStride;
                            double clipStart                    = (samplesPlayedInSourceSamples + l.m_loopOffset) % l.clip.Value.samplesLeftOrMono.Length;
                            // We add a one sample tolerance in case we are regenerating the same audio frame, in which case the old values are fine.
                            if (clipStart < l.clip.Value.samplesLeftOrMono.Length / 2 && clipStart > 1)
                            {
                                // We missed the buffer
                                clipStart            = samplesPlayedInSourceSamples % l.clip.Value.samplesLeftOrMono.Length;
                                l.m_loopOffset       = (int)(l.clip.Value.samplesLeftOrMono.Length - clipStart);
                                l.m_spawnBufferLow16 = (short)bufferId;
                            }
                        }
                        looped[i] = l;
                    }
                }

                if (chunk.Has(ref coneHandle))
                {
                    var worldTransforms = worldTransformHandle.Resolve(chunk);
                    var cones           = chunk.GetNativeArray(ref coneHandle);
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        emitters[firstEntityIndex + i] = new LoopedEmitter
                        {
                            source    = looped[i],
                            transform = new RigidTransform(worldTransforms[i].rotation, worldTransforms[i].position),
                            cone      = cones[i],
                            useCone   = true
                        };
                    }
                }
                else
                {
                    var worldTransforms = worldTransformHandle.Resolve(chunk);
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        emitters[firstEntityIndex + i] = new LoopedEmitter
                        {
                            source    = looped[i],
                            transform = new RigidTransform(worldTransforms[i].rotation, worldTransforms[i].position),
                            cone      = default,
                            useCone   = false
                        };
                    }
                }
            }
        }
    }
}

