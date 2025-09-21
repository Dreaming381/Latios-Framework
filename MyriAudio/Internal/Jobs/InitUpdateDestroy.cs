using Latios.Calci;
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
        // Single
        [BurstCompile]
        public struct UpdateListenersJob : IJobChunk
        {
            [ReadOnly] public ComponentTypeHandle<AudioListener>       listenerHandle;
            [ReadOnly] public WorldTransformReadOnlyTypeHandle         worldTransformHandle;
            [ReadOnly] public BufferTypeHandle<AudioListenerChannelID> channelGuidHandle;
            public NativeList<ListenerWithTransform>                   listenersWithTransforms;
            public NativeList<AudioSourceChannelID>                    listenersChannelIDs;
            public NativeArray<int>                                    channelCount;
            public NativeArray<int>                                    sourceChunkChannelCount;
            public int                                                 sourceChunkCount;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var listeners       = chunk.GetNativeArray(ref listenerHandle);
                var worldTransforms = worldTransformHandle.Resolve(chunk);
                var channelsBuffers = chunk.GetBufferAccessor(ref channelGuidHandle);
                for (int i = 0; i < chunk.Count; i++)
                {
                    var l = listeners[i];
                    //This culling desyncs the listener indices from the graph handling logic.
                    //Todo: Figure out how to bring this optimization back.
                    //if (l.volume > 0f)
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
                    }
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
            [ReadOnly] public WorldTransformReadOnlyTypeHandle                     worldTransformHandle;
            [ReadOnly] public NativeReference<int>                                 audioFrame;
            [ReadOnly] public NativeReference<int>                                 lastPlayedAudioFrame;
            [ReadOnly] public NativeReference<int>                                 lastConsumedBufferId;
            public int                                                             bufferId;
            public int                                                             sampleRate;
            public int                                                             samplesPerFrame;

            public unsafe void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var rng                   = new Rng.RngSequence(math.asuint(new int2(audioFrame.Value, unfilteredChunkIndex)));
                var volumes               = (AudioSourceVolume*)chunk.GetRequiredComponentDataPtrRO(ref volumeHandle);
                var clips                 = (AudioSourceClip*)chunk.GetRequiredComponentDataPtrRW(ref clipHandle);
                var hasTransforms         = worldTransformHandle.Has(in chunk);
                var transforms            = worldTransformHandle.Resolve(chunk);
                var sampleRateMultipliers = chunk.GetComponentDataPtrRO(ref sampleRateMultiplierHandle);
                var falloffs              = chunk.GetComponentDataPtrRO(ref distanceFalloffHandle);
                var cones                 = chunk.GetComponentDataPtrRO(ref emitterConeHandle);
                var channelGuids          = chunk.GetComponentDataPtrRO(ref channelIDHandle);
                var expireMask            = chunk.GetEnabledMask(ref expireHandle);

                stream.BeginForEachIndex(unfilteredChunkIndex);
                for (int i = 0; i < chunk.Count; i++)
                {
                    // Clip
                    ref var clip = ref clips[i];

                    if (!clip.m_clip.IsCreated)
                        continue;

                    double sampleRateMultiplier = sampleRateMultipliers != null ? sampleRateMultipliers[i].multiplier : 1.0;
                    if (clip.looping)
                    {
                        if (sampleRateMultiplier <= 0.0)
                            continue;

                        if (!clip.m_initialized)
                        {
                            if (clip.offsetIsBasedOnSpawn)
                            {
                                ulong samplesPlayed = (ulong)samplesPerFrame * (ulong)audioFrame.Value;
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
                            if (lastConsumedBufferId.Value - clip.m_spawnedBufferId >= 0)
                            {
                                clip.m_offsetLocked = true;
                            }
                            else
                            {
                                // This check compares if the playhead loop advanced past the target start point in the loop
                                ulong  samplesPlayed                = (ulong)samplesPerFrame * (ulong)audioFrame.Value;
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

                        var framesPlayed = lastPlayedAudioFrame.Value - clip.m_spawnedAudioFrame;
                        // There's a chance the one shot spawned last game frame but the dsp missed the audio frame.
                        // In such a case, we still want the one shot to start at the beginning rather than skip the first audio frame.
                        // This is more likely to happen in high framerate scenarios.
                        // This does not solve the problem where the audio frame ticks during DSP and again before the next AudioSystemUpdate.
                        if ((!clip.m_initialized) || (clip.m_spawnedBufferId - lastConsumedBufferId.Value > 0 && framesPlayed >= 0))
                        {
                            clip.m_spawnedBufferId   = bufferId;
                            clip.m_spawnedAudioFrame = audioFrame.Value;
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

