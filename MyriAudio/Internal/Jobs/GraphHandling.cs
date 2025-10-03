using System.Threading;
using Unity.Audio;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace Latios.Myri
{
    internal static class GraphHandling
    {
        [BurstCompile]
        public struct CaptureIldFrameJob : IJob
        {
            public NativeReference<long> packedFrameCounterBufferId;  //MSB bufferId, LSB frame
            public NativeReference<int>  audioFrame;
            public NativeReference<int>  lastPlayedAudioFrame;
            public NativeReference<int>  lastReadBufferId;

            public NativeQueue<AudioFrameBufferHistoryElement> audioFrameHistory;
            [ReadOnly] public ComponentLookup<AudioSettings>   audioSettingsLookup;
            public Entity                                      worldBlackboardEntity;

            public unsafe void Execute()
            {
                ref long packedRef = ref UnsafeUtility.AsRef<long>(packedFrameCounterBufferId.GetUnsafePtr());
                long     frameData = Interlocked.Read(ref packedRef);
                int      frame     = (int)(frameData & 0xffffffff);
                int      bufferId  = (int)(frameData >> 32);

                while (!audioFrameHistory.IsEmpty() && audioFrameHistory.Peek().bufferId < bufferId)
                {
                    audioFrameHistory.Dequeue();
                }
                int targetFrame = frame + 1 + math.max(audioSettingsLookup[worldBlackboardEntity].lookaheadAudioFrames, 0);
                if (!audioFrameHistory.IsEmpty() && audioFrameHistory.Peek().bufferId == bufferId)
                {
                    targetFrame = math.max(audioFrameHistory.Peek().expectedNextUpdateFrame, targetFrame);
                }
                audioFrame.Value           = targetFrame;
                lastReadBufferId.Value     = bufferId;
                lastPlayedAudioFrame.Value = frame;
            }
        }

        //Temporary workaround: DSPGraph updates can't be scheduled from Burst
        // [BurstCompile]
        public struct UpdateListenersGraphJob : IJob
        {
            [ReadOnly] public NativeArray<Entity>            listenerEntities;
            [ReadOnly] public NativeArray<Entity>            destroyedListenerEntities;
            [ReadOnly] public ComponentLookup<AudioListener> listenerLookup;
            public ComponentLookup<ListenerGraphState>       listenerGraphStateLookup;
            public EntityCommandBuffer                       ecb;

            public NativeList<ListenerGraphState> listenerStatesToDisposeOnShutdown;

            [ReadOnly] public ComponentLookup<AudioSettings> audioSettingsLookup;
            public Entity                                    worldBlackboardEntity;

            [ReadOnly] public NativeReference<int>             audioFrame;
            public NativeQueue<AudioFrameBufferHistoryElement> audioFrameHistory;

            public NativeList<int>      systemMasterMixNodePortFreelist;
            public NativeReference<int> systemMasterMixNodePortCount;
            public DSPNode              systemMasterMixNode;

            public NativeReference<int> systemIldNodePortCount;
            public DSPNode              systemIldNode;

            public DSPCommandBlock commandBlock;

            public NativeArray<ListenerBufferParameters> listenerBufferParameters;
            public NativeList<float>                     outputSamplesMegaBuffer;
            public NativeList<IldBufferChannel>          outputSamplesMegaBufferChannels;

            public int samplesPerFrame;
            public int sampleRate;
            public int bufferId;

            public unsafe void Execute()
            {
                bool dirty                  = false;
                var  existingEntities       = new NativeList<Entity>(Allocator.Temp);
                var  newEntities            = new NativeList<Entity>(Allocator.Temp);
                var  newListenerGraphStates = new NativeList<ListenerGraphState>(Allocator.Temp);
                int  megaBufferSampleCount  = 0;
                var  channelCounts          = new NativeArray<int>(listenerBufferParameters.Length, Allocator.Temp);

                var audioSettings                  = audioSettingsLookup[worldBlackboardEntity];
                audioSettings.audioFramesPerUpdate = math.max(audioSettings.audioFramesPerUpdate, 1);
                audioSettings.safetyAudioFrames    = math.max(audioSettings.safetyAudioFrames, 0);
                audioSettings.lookaheadAudioFrames = math.max(audioSettings.lookaheadAudioFrames, 0);

                // Destroy graph state and components of old entities
                for (int i = 0; i < destroyedListenerEntities.Length; i++)
                {
                    var entity             = destroyedListenerEntities[i];
                    dirty                  = true;
                    var listenerGraphState = listenerGraphStateLookup[entity];
                    commandBlock.Disconnect(listenerGraphState.masterOutputConnection);
                    systemMasterMixNodePortFreelist.Add(listenerGraphState.masterPortIndex);

                    for (int j = 0; j < listenerGraphState.ildConnections.Length; j++)
                    {
                        commandBlock.Disconnect(listenerGraphState.ildConnections[j]);
                    }
                    commandBlock.ReleaseDSPNode(listenerGraphState.listenerMixNode);

                    listenerGraphState.ildConnections.Dispose();
                    // Clear it in case shutdown happens before playback.
                    listenerGraphStateLookup[entity] = default;
                    ecb.RemoveComponent<ListenerGraphState>(entity);
                }

                // Process new and changed listeners
                for (int i = 0; i < listenerEntities.Length; i++)
                {
                    var entity = listenerEntities[i];

                    if (!listenerGraphStateLookup.HasComponent(entity))
                    {
                        // New listener
                        dirty        = true;
                        var listener = listenerLookup[entity];

                        // Create the output ListenerMixNode and tie it to the final mix
                        int masterPortIndex;
                        if (systemMasterMixNodePortFreelist.Length > 0)
                        {
                            masterPortIndex = systemMasterMixNodePortFreelist[systemMasterMixNodePortFreelist.Length - 1];
                            systemMasterMixNodePortFreelist.RemoveAt(systemMasterMixNodePortFreelist.Length - 1);
                        }
                        else
                        {
                            masterPortIndex = systemMasterMixNodePortCount.Value;
                            commandBlock.AddInletPort(systemMasterMixNode, 2);
                            systemMasterMixNodePortCount.Value++;
                        }
                        var listenerMixNode = commandBlock.CreateDSPNode<ListenerMixNode.Parameters, ListenerMixNode.SampleProviders, ListenerMixNode>();
                        commandBlock.AddOutletPort(listenerMixNode, 2);
                        commandBlock.UpdateAudioKernel<ListenerMixNodeVolumeUpdate, ListenerMixNode.Parameters,
                                                       ListenerMixNode.SampleProviders, ListenerMixNode>(new ListenerMixNodeVolumeUpdate
                        {
                            settings = new BrickwallLimiterSettings
                            {
                                volume               = listener.volume,
                                preGain              = listener.gain,
                                releasePerSampleDB   = listener.limiterDBRelaxPerSecond / sampleRate,
                                lookaheadSampleCount = (int)math.ceil(listener.limiterLookaheadTime * sampleRate)
                            }
                        }, listenerMixNode);
                        commandBlock.UpdateAudioKernel<ListenerMixNodeChannelUpdate, ListenerMixNode.Parameters,
                                                       ListenerMixNode.SampleProviders, ListenerMixNode>(new ListenerMixNodeChannelUpdate
                        {
                            blob       = listener.ildProfile,
                            sampleRate = sampleRate
                        }, listenerMixNode);

                        var listenerGraphState = new ListenerGraphState
                        {
                            listenerMixNode        = listenerMixNode,
                            masterPortIndex        = masterPortIndex,
                            masterOutputConnection = commandBlock.Connect(listenerMixNode, 0, systemMasterMixNode, masterPortIndex),
                            ildConnections         = new UnsafeList<DSPConnection>(8, Allocator.Persistent),
                            lastUsedProfile        = listener.ildProfile
                        };

                        ref var profile = ref listener.ildProfile.Value;

                        // Write out entity data
                        newEntities.Add(entity);
                        newListenerGraphStates.Add(listenerGraphState);

                        // Compute parameters and megabuffer allocation
                        int numChannels             = profile.channelDspsLeft.Length + profile.channelDspsRight.Length;
                        listenerBufferParameters[i] = new ListenerBufferParameters
                        {
                            bufferStart       = megaBufferSampleCount,
                            leftChannelsCount = profile.channelDspsLeft.Length,
                            samplesPerChannel = samplesPerFrame * (audioSettings.audioFramesPerUpdate + audioSettings.safetyAudioFrames) + 8  // 8 extra samples for anti-stepping
                        };
                        megaBufferSampleCount += listenerBufferParameters[i].samplesPerChannel * numChannels;
                        channelCounts[i]       = numChannels;
                    }
                    else
                    {
                        var     listener           = listenerLookup[entity];
                        ref var profile            = ref listener.ildProfile.Value;
                        var     listenerGraphState = listenerGraphStateLookup[entity];
                        if (listener.ildProfile != listenerGraphState.lastUsedProfile)
                        {
                            dirty = true;
                            commandBlock.UpdateAudioKernel<ListenerMixNodeChannelUpdate, ListenerMixNode.Parameters,
                                                           ListenerMixNode.SampleProviders, ListenerMixNode>(new ListenerMixNodeChannelUpdate
                            {
                                blob       = listener.ildProfile,
                                sampleRate = sampleRate
                            }, listenerGraphState.listenerMixNode);
                            listenerGraphState.lastUsedProfile = listener.ildProfile;
                            listenerGraphStateLookup[entity]   = listenerGraphState;
                        }
                        commandBlock.UpdateAudioKernel<ListenerMixNodeVolumeUpdate, ListenerMixNode.Parameters,
                                                       ListenerMixNode.SampleProviders, ListenerMixNode>(new ListenerMixNodeVolumeUpdate
                        {
                            settings = new BrickwallLimiterSettings
                            {
                                volume               = listener.volume,
                                preGain              = listener.gain,
                                releasePerSampleDB   = listener.limiterDBRelaxPerSecond / sampleRate,
                                lookaheadSampleCount = (int)math.ceil(listener.limiterLookaheadTime * sampleRate)
                            }
                        }, listenerGraphState.listenerMixNode);
                        existingEntities.Add(entity);

                        //Compute parameters and megabuffer allocation
                        int numChannels             = profile.channelDspsLeft.Length + profile.channelDspsRight.Length;
                        listenerBufferParameters[i] = new ListenerBufferParameters
                        {
                            bufferStart       = megaBufferSampleCount,
                            leftChannelsCount = profile.channelDspsLeft.Length,
                            samplesPerChannel = samplesPerFrame * (audioSettings.audioFramesPerUpdate + audioSettings.safetyAudioFrames) + 8  // 8 extra samples for anti-stepping
                        };
                        megaBufferSampleCount += listenerBufferParameters[i].samplesPerChannel * numChannels;
                        channelCounts[i]       = numChannels;
                    }
                }

                // Rebuild ildConnections
                if (dirty)
                {
                    // Reset connections for existing entities
                    foreach (var entity in existingEntities)
                    {
                        var listenerGraphState = listenerGraphStateLookup[entity];
                        foreach (var connection in listenerGraphState.ildConnections)
                            commandBlock.Disconnect(connection);
                    }
                    listenerStatesToDisposeOnShutdown.Clear();

                    int newEntityIndex  = 0;
                    int outputPortIndex = 0;
                    foreach (var entity in listenerEntities)
                    {
                        if (listenerGraphStateLookup.TryGetComponent(entity, out var listenerGraphState))
                        {
                            listenerGraphState.ildConnections.Clear();
                            ref var blob         = ref listenerGraphState.lastUsedProfile.Value;
                            var     channelCount = blob.channelDspsLeft.Length + blob.channelDspsRight.Length;
                            for (int i = listenerGraphState.inletPortCount; i < channelCount; i++)
                            {
                                commandBlock.AddInletPort(listenerGraphState.listenerMixNode, 2);
                                listenerGraphState.inletPortCount++;
                            }
                            for (int i = systemIldNodePortCount.Value; i < outputPortIndex + channelCount; i++)
                            {
                                commandBlock.AddOutletPort(systemIldNode, 2);
                                systemMasterMixNodePortCount.Value++;
                            }
                            for (int i = 0; i < channelCount; i++)
                            {
                                listenerGraphState.ildConnections.Add(commandBlock.Connect(systemIldNode, outputPortIndex, listenerGraphState.listenerMixNode, i));
                                outputPortIndex++;
                            }
                            listenerGraphStateLookup[entity] = listenerGraphState;
                            listenerStatesToDisposeOnShutdown.Add(listenerGraphState);
                        }
                        else
                        {
                            listenerGraphState   = newListenerGraphStates[newEntityIndex];
                            ref var blob         = ref listenerGraphState.lastUsedProfile.Value;
                            var     channelCount = blob.channelDspsLeft.Length + blob.channelDspsRight.Length;
                            for (int i = listenerGraphState.inletPortCount; i < channelCount; i++)
                            {
                                commandBlock.AddInletPort(listenerGraphState.listenerMixNode, 2);
                                listenerGraphState.inletPortCount++;
                            }
                            for (int i = systemIldNodePortCount.Value; i < outputPortIndex + channelCount; i++)
                            {
                                commandBlock.AddOutletPort(systemIldNode, 2);
                                systemMasterMixNodePortCount.Value++;
                            }
                            for (int i = 0; i < channelCount; i++)
                            {
                                listenerGraphState.ildConnections.Add(commandBlock.Connect(systemIldNode, outputPortIndex, listenerGraphState.listenerMixNode, i));
                                outputPortIndex++;
                            }
                            newListenerGraphStates[newEntityIndex] = listenerGraphState;
                            newEntityIndex++;
                            listenerStatesToDisposeOnShutdown.Add(listenerGraphState);
                        }
                    }
                }

                // Add components to new entities
                for (int i = 0; i < newEntities.Length; i++)
                {
                    ecb.AddComponent(newEntities[i], newListenerGraphStates[i]);
                }

                // Resize megaBuffer and populate offsets
                outputSamplesMegaBuffer.Resize(megaBufferSampleCount, NativeArrayOptions.ClearMemory);
                var megaBuffer = outputSamplesMegaBuffer.AsArray();
                for (int i = 0; i < listenerBufferParameters.Length; i++)
                {
                    var parameters = listenerBufferParameters[i];
                    for (int j = 0; j < channelCounts[i]; j++)
                    {
                        var subBuffer = megaBuffer.GetSubArray(parameters.bufferStart + parameters.samplesPerChannel * j, parameters.samplesPerChannel);
                        outputSamplesMegaBufferChannels.Add(new IldBufferChannel
                        {
                            buffer = (float*)subBuffer.GetUnsafePtr()
                        });
                    }
                }
                audioFrameHistory.Enqueue(new AudioFrameBufferHistoryElement
                {
                    audioFrame              = audioFrame.Value,
                    bufferId                = bufferId,
                    expectedNextUpdateFrame = audioFrame.Value + audioSettings.audioFramesPerUpdate
                });
                commandBlock.UpdateAudioKernel<ReadIldBuffersNodeUpdate, ReadIldBuffersNode.Parameters,
                                               ReadIldBuffersNode.SampleProviders, ReadIldBuffersNode>(new ReadIldBuffersNodeUpdate
                {
                    ildBuffer = new IldBuffer
                    {
                        bufferChannels  = outputSamplesMegaBufferChannels.GetUnsafePtr(),
                        bufferId        = bufferId,
                        framesInBuffer  = 1 + audioSettings.safetyAudioFrames,
                        framesPerUpdate = audioSettings.audioFramesPerUpdate,
                        channelCount    = outputSamplesMegaBufferChannels.Length,
                        frame           = audioFrame.Value,
                        warnIfStarved   = audioSettings.logWarningIfBuffersAreStarved
                    },
                },
                                                                                                       systemIldNode);

                // Update master limiter
                commandBlock.UpdateAudioKernel<MasterMixNodeUpdate, MasterMixNode.Parameters,
                                               MasterMixNode.SampleProviders, MasterMixNode>(new MasterMixNodeUpdate
                {
                    settings = new BrickwallLimiterSettings
                    {
                        volume               = math.saturate(audioSettings.masterVolume),
                        preGain              = audioSettings.masterGain,
                        releasePerSampleDB   = audioSettings.masterLimiterDBRelaxPerSecond / sampleRate,
                        lookaheadSampleCount = (int)math.ceil(audioSettings.masterLimiterLookaheadTime * sampleRate)
                    }
                },
                                                                                             systemMasterMixNode);
            }
        }

        [BurstCompile]
        public struct SubmitToDspGraphJob : IJob
        {
            public DSPCommandBlock commandBlock;

            public void Execute()
            {
                commandBlock.Complete();
            }
        }
    }
}

