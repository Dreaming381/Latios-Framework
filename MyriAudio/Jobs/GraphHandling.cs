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

            public NativeQueue<AudioFrameBufferHistoryElement>       audioFrameHistory;
            [ReadOnly] public ComponentDataFromEntity<AudioSettings> audioSettingsCdfe;
            public Entity                                            worldBlackboardEntity;

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
                int targetFrame = frame + 1 + math.max(audioSettingsCdfe[worldBlackboardEntity].lookaheadAudioFrames, 0);
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
        //[BurstCompile]
        public struct UpdateListenersGraphJob : IJob
        {
            [ReadOnly] public NativeArray<Entity>                    listenerEntities;
            [ReadOnly] public NativeArray<Entity>                    destroyedListenerEntities;
            [ReadOnly] public ComponentDataFromEntity<AudioListener> listenerCdfe;
            public ComponentDataFromEntity<ListenerGraphState>       listenerGraphStateCdfe;
            public ComponentDataFromEntity<EntityOutputGraphState>   listenerOutputGraphStateCdfe;
            public EntityCommandBuffer                               ecb;

            [ReadOnly] public ComponentDataFromEntity<AudioSettings> audioSettingsCdfe;
            public Entity                                            worldBlackboardEntity;

            [ReadOnly] public NativeReference<int>             audioFrame;
            public NativeQueue<AudioFrameBufferHistoryElement> audioFrameHistory;

            public NativeList<int>      systemMixNodePortFreelist;
            public NativeReference<int> systemMixNodePortCount;
            public DSPNode              systemMixNode;

            public NativeReference<int> systemIldNodePortCount;
            public DSPNode              systemIldNode;

            public DSPCommandBlock commandBlock;

            public NativeArray<ListenerBufferParameters> listenerBufferParameters;
            public NativeList<int2>                      forIndexToListenerAndChannelIndices;
            public NativeList<float>                     outputSamplesMegaBuffer;
            public NativeList<IldBufferChannel>          outputSamplesMegaBufferChannels;

            public int samplesPerFrame;
            public int bufferId;

            public unsafe void Execute()
            {
                bool dirty                  = false;
                var  existingEntities       = new NativeList<Entity>(Allocator.Temp);
                var  newEntities            = new NativeList<Entity>(Allocator.Temp);
                var  newOutputGraphStates   = new NativeList<EntityOutputGraphState>(Allocator.Temp);
                var  newListenerGraphStates = new NativeList<ListenerGraphState>(Allocator.Temp);
                int  megaBufferSampleCount  = 0;
                var  channelCounts          = new NativeArray<int>(listenerBufferParameters.Length, Allocator.Temp);

                var audioSettings                  = audioSettingsCdfe[worldBlackboardEntity];
                audioSettings.audioFramesPerUpdate = math.max(audioSettings.audioFramesPerUpdate, 1);
                audioSettings.safetyAudioFrames    = math.max(audioSettings.safetyAudioFrames, 0);
                audioSettings.lookaheadAudioFrames = math.max(audioSettings.lookaheadAudioFrames, 0);

                //Destroy graph state and components of old entities
                for (int i = 0; i < destroyedListenerEntities.Length; i++)
                {
                    var entity                   = destroyedListenerEntities[i];
                    dirty                        = true;
                    var listenerOutputGraphState = listenerOutputGraphStateCdfe[entity];
                    commandBlock.Disconnect(listenerOutputGraphState.connection);
                    systemMixNodePortFreelist.Add(listenerOutputGraphState.portIndex);

                    var listenerGraphState = listenerGraphStateCdfe[entity];
                    for (int j = 0; j < listenerGraphState.connections.Length; j++)
                    {
                        commandBlock.Disconnect(listenerGraphState.connections[j]);
                    }
                    for (int j = 0; j < listenerGraphState.ildConnections.Length; j++)
                    {
                        commandBlock.Disconnect(listenerGraphState.ildConnections[j].connection);
                    }
                    for (int j = 0; j < listenerGraphState.nodes.Length; j++)
                    {
                        commandBlock.ReleaseDSPNode(listenerGraphState.nodes[j]);
                    }

                    listenerGraphState.connections.Dispose();
                    listenerGraphState.nodes.Dispose();
                    listenerGraphState.ildConnections.Dispose();
                    ecb.RemoveComponent<ListenerGraphState>(    entity);
                    ecb.RemoveComponent<EntityOutputGraphState>(entity);
                }

                //Process new and changed listeners
                for (int i = 0; i < listenerEntities.Length; i++)
                {
                    var entity = listenerEntities[i];

                    if (!listenerGraphStateCdfe.HasComponent(entity))
                    {
                        dirty                  = true;
                        var listener           = listenerCdfe[entity];
                        var listenerGraphState = new ListenerGraphState
                        {
                            connections     = new UnsafeList<DSPConnection>(8, Allocator.Persistent),
                            nodes           = new UnsafeList<DSPNode>(8, Allocator.Persistent),
                            ildConnections  = new UnsafeList<IldOutputConnection>(8, Allocator.Persistent),
                            lastUsedProfile = listener.ildProfile
                        };

                        //Create the output MixPortsToStereoNode and tie it to the final mix
                        int mixNodePortIndex;
                        if (systemMixNodePortFreelist.Length > 0)
                        {
                            mixNodePortIndex = systemMixNodePortFreelist[systemMixNodePortFreelist.Length - 1];
                            systemMixNodePortFreelist.RemoveAt(systemMixNodePortFreelist.Length - 1);
                        }
                        else
                        {
                            mixNodePortIndex = systemMixNodePortCount.Value;
                            commandBlock.AddInletPort(systemMixNode, 2);
                            systemMixNodePortCount.Value++;
                        }
                        var listenerMixNode = commandBlock.CreateDSPNode<MixPortsToStereoNode.Parameters, MixPortsToStereoNode.SampleProviders, MixPortsToStereoNode>();
                        commandBlock.AddOutletPort(listenerMixNode, 2);

                        listenerGraphState.nodes.Add(listenerMixNode);
                        var listenerOutputGraphState = new EntityOutputGraphState
                        {
                            connection = commandBlock.Connect(listenerMixNode, 0, systemMixNode, mixNodePortIndex),
                            portIndex  = mixNodePortIndex
                        };

                        ref var profile = ref listener.ildProfile.Value;

                        BuildChannelGraph(ref profile, commandBlock, listenerMixNode, ref listenerGraphState);

                        //Write out entity data
                        newEntities.Add(entity);
                        newOutputGraphStates.Add(listenerOutputGraphState);
                        newListenerGraphStates.Add(listenerGraphState);

                        //Compute parameters and megabuffer allocation
                        int numChannels             = profile.passthroughFractionsPerLeftChannel.Length + profile.passthroughFractionsPerRightChannel.Length;
                        listenerBufferParameters[i] = new ListenerBufferParameters
                        {
                            bufferStart       = megaBufferSampleCount,
                            leftChannelsCount = profile.passthroughFractionsPerLeftChannel.Length,
                            samplesPerChannel = samplesPerFrame * (audioSettings.audioFramesPerUpdate + audioSettings.safetyAudioFrames)
                        };
                        for (int j = 0; j < numChannels; j++)
                        {
                            forIndexToListenerAndChannelIndices.Add(new int2(i, j));
                        }
                        megaBufferSampleCount += listenerBufferParameters[i].samplesPerChannel * numChannels;
                        channelCounts[i]       = numChannels;
                    }
                    else
                    {
                        var     listener           = listenerCdfe[entity];
                        ref var profile            = ref listener.ildProfile.Value;
                        var     listenerGraphState = listenerGraphStateCdfe[entity];
                        if (listener.ildProfile != listenerGraphState.lastUsedProfile)
                        {
                            dirty = true;
                            //Swap the old MixPortsToStereoNode with a new one
                            var listenerOutputGraphState = listenerOutputGraphStateCdfe[entity];
                            var listenerMixNode          =
                                commandBlock.CreateDSPNode<MixPortsToStereoNode.Parameters, MixPortsToStereoNode.SampleProviders, MixPortsToStereoNode>();
                            commandBlock.AddOutletPort(listenerMixNode, 2);
                            commandBlock.Disconnect(listenerOutputGraphState.connection);
                            listenerOutputGraphState.connection = commandBlock.Connect(listenerMixNode, 0, systemMixNode, listenerOutputGraphState.portIndex);

                            //Destroy the old graph
                            for (int j = 0; j < listenerGraphState.connections.Length; j++)
                            {
                                commandBlock.Disconnect(listenerGraphState.connections[j]);
                            }
                            for (int j = 0; j < listenerGraphState.ildConnections.Length; j++)
                            {
                                commandBlock.Disconnect(listenerGraphState.ildConnections[j].connection);
                            }
                            for (int j = 0; j < listenerGraphState.nodes.Length; j++)
                            {
                                commandBlock.ReleaseDSPNode(listenerGraphState.nodes[j]);
                            }
                            listenerGraphState.connections.Clear();
                            listenerGraphState.nodes.Clear();
                            listenerGraphState.ildConnections.Clear();

                            //Set up the new graph
                            listenerGraphState.lastUsedProfile = listener.ildProfile;
                            listenerGraphState.nodes.Add(listenerMixNode);

                            BuildChannelGraph(ref profile, commandBlock, listenerMixNode, ref listenerGraphState);

                            //Write out entity data
                            listenerGraphStateCdfe[entity]       = listenerGraphState;
                            listenerOutputGraphStateCdfe[entity] = listenerOutputGraphState;
                        }
                        existingEntities.Add(entity);

                        //Compute parameters and megabuffer allocation
                        int numChannels             = profile.passthroughFractionsPerLeftChannel.Length + profile.passthroughFractionsPerRightChannel.Length;
                        listenerBufferParameters[i] = new ListenerBufferParameters
                        {
                            bufferStart       = megaBufferSampleCount,
                            leftChannelsCount = profile.passthroughFractionsPerLeftChannel.Length,
                            samplesPerChannel = samplesPerFrame * (audioSettings.audioFramesPerUpdate + audioSettings.safetyAudioFrames)
                        };
                        for (int j = 0; j < numChannels; j++)
                        {
                            forIndexToListenerAndChannelIndices.Add(new int2(i, j));
                        }
                        megaBufferSampleCount += listenerBufferParameters[i].samplesPerChannel * numChannels;
                        channelCounts[i]       = numChannels;
                    }
                }

                //Rebuild ildConnections
                if (dirty)
                {
                    //Reset connections for existing entities
                    for (int i = 0; i < existingEntities.Length; i++)
                    {
                        var entity             = existingEntities[i];
                        var listenerGraphState = listenerGraphStateCdfe[entity];
                        for (int j = 0; j < listenerGraphState.ildConnections.Length; j++)
                        {
                            var ildConnection = listenerGraphState.ildConnections[j];
                            if (ildConnection.ildOutputPort >= 0)
                            {
                                commandBlock.Disconnect(ildConnection.connection);
                                ildConnection.ildOutputPort          = -ildConnection.ildOutputPort - 1;
                                listenerGraphState.ildConnections[j] = ildConnection;
                            }
                        }
                        listenerGraphStateCdfe[entity] = listenerGraphState;
                    }

                    var fakePortRealPortHashmap = new NativeHashMap<int, int>(8, Allocator.Temp);
                    int ildPortsUsed            = 0;
                    //Build connections for existing entities
                    for (int i = 0; i < existingEntities.Length; i++)
                    {
                        var entity             = existingEntities[i];
                        var listenerGraphState = listenerGraphStateCdfe[entity];
                        fakePortRealPortHashmap.Clear();
                        for (int j = 0; j < listenerGraphState.ildConnections.Length; j++)
                        {
                            var ildConnection = listenerGraphState.ildConnections[j];
                            if (fakePortRealPortHashmap.TryGetValue(ildConnection.ildOutputPort, out int realPort))
                            {
                                ildConnection.ildOutputPort = realPort;
                            }
                            else
                            {
                                if (ildPortsUsed >= systemIldNodePortCount.Value)
                                {
                                    commandBlock.AddOutletPort(systemIldNode, 1);
                                    systemIldNodePortCount.Value++;
                                }
                                fakePortRealPortHashmap.Add(ildConnection.ildOutputPort, ildPortsUsed);
                                ildConnection.ildOutputPort = ildPortsUsed;
                                ildPortsUsed++;
                            }
                            ildConnection.connection = commandBlock.Connect(systemIldNode, ildConnection.ildOutputPort, ildConnection.node, ildConnection.nodeInputPort);
                            if (ildConnection.attenuation != 1f)
                                commandBlock.SetAttenuation(ildConnection.connection, ildConnection.attenuation);
                            listenerGraphState.ildConnections[j] = ildConnection;
                        }
                        listenerGraphStateCdfe[entity] = listenerGraphState;
                    }
                    //Build connections fo new entities
                    for (int i = 0; i < newEntities.Length; i++)
                    {
                        var listenerGraphState = newListenerGraphStates[i];
                        fakePortRealPortHashmap.Clear();
                        for (int j = 0; j < listenerGraphState.ildConnections.Length; j++)
                        {
                            var ildConnection = listenerGraphState.ildConnections[j];
                            if (fakePortRealPortHashmap.TryGetValue(ildConnection.ildOutputPort, out int realPort))
                            {
                                ildConnection.ildOutputPort = realPort;
                            }
                            else
                            {
                                if (ildPortsUsed >= systemIldNodePortCount.Value)
                                {
                                    commandBlock.AddOutletPort(systemIldNode, 1);
                                    systemIldNodePortCount.Value++;
                                }
                                fakePortRealPortHashmap.Add(ildConnection.ildOutputPort, ildPortsUsed);
                                ildConnection.ildOutputPort = ildPortsUsed;
                                ildPortsUsed++;
                            }
                            ildConnection.connection = commandBlock.Connect(systemIldNode, ildConnection.ildOutputPort, ildConnection.node, ildConnection.nodeInputPort);
                            if (ildConnection.attenuation != 1f)
                                commandBlock.SetAttenuation(ildConnection.connection, ildConnection.attenuation);
                            listenerGraphState.ildConnections[j] = ildConnection;
                        }
                        newListenerGraphStates[i] = listenerGraphState;
                    }
                }

                //Add components to new entities
                for (int i = 0; i < newEntities.Length; i++)
                {
                    ecb.AddComponent(newEntities[i], newListenerGraphStates[i]);
                    ecb.AddComponent(newEntities[i], newOutputGraphStates[i]);
                }

                //Resize megaBuffer and populate offsets
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
                commandBlock.UpdateAudioKernel<ReadIldBuffersNodeUpdate, ReadIldBuffersNode.Parameters, ReadIldBuffersNode.SampleProviders, ReadIldBuffersNode>(new ReadIldBuffersNodeUpdate
                {
                    ildBuffer = new IldBuffer
                    {
                        bufferChannels  = (IldBufferChannel*)outputSamplesMegaBufferChannels.GetUnsafePtr(),
                        bufferId        = bufferId,
                        framesInBuffer  = 1 + audioSettings.safetyAudioFrames,
                        framesPerUpdate = audioSettings.audioFramesPerUpdate,
                        channelCount    = outputSamplesMegaBufferChannels.Length,
                        frame           = audioFrame.Value,
                        warnIfStarved   = audioSettings.logWarningIfBuffersAreStarved
                    },
                },
                                                                                                                                                                systemIldNode);
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

        public static StateVariableFilterNode.FilterType ToDspFilterType(this FrequencyFilterType type)
        {
            switch(type)
            {
                case FrequencyFilterType.Lowpass: return StateVariableFilterNode.FilterType.Lowpass;
                case FrequencyFilterType.Highpass: return StateVariableFilterNode.FilterType.Highpass;
                case FrequencyFilterType.Bandpass: return StateVariableFilterNode.FilterType.Bandpass;
                case FrequencyFilterType.Bell: return StateVariableFilterNode.FilterType.Bell;
                case FrequencyFilterType.Notch: return StateVariableFilterNode.FilterType.Notch;
                case FrequencyFilterType.Lowshelf: return StateVariableFilterNode.FilterType.Lowshelf;
                case FrequencyFilterType.Highshelf: return StateVariableFilterNode.FilterType.Highshelf;
            }
            return StateVariableFilterNode.FilterType.Lowpass;
        }

        public static void BuildChannelGraph(ref IldProfileBlob profile, DSPCommandBlock commandBlock, DSPNode listenerMixNode, ref ListenerGraphState listenerGraphState)
        {
            //Create the channel nodes and connections but leave the ild outputs disconnected
            int listenerMixPortCount = 0;
            for (int i = 0; i < profile.passthroughFractionsPerLeftChannel.Length; i++)
            {
                bool    filterAdded        = false;
                DSPNode previousFilterNode = listenerMixNode;
                int     previousFilterPort = listenerMixPortCount;
                float   filterVolume       = profile.filterVolumesPerLeftChannel[i] * (1f - profile.passthroughFractionsPerLeftChannel[i]);
                float   passthroughVolume  = profile.passthroughVolumesPerLeftChannel[i] * profile.passthroughFractionsPerLeftChannel[i];
                commandBlock.AddInletPort(listenerMixNode, 1);
                listenerMixPortCount++;

                if (filterVolume > 0f)
                {
                    //We have to walk backwards to build the filter connections correctly since the ild outputs must remain disconnected
                    for (int j = profile.channelIndicesLeft.Length - 1; j >= 0; j--)
                    {
                        if (i == profile.channelIndicesLeft[j])
                        {
                            var filter     = profile.filtersLeft[j];
                            var filterNode = StateVariableFilterNode.Create(commandBlock,
                                                                            filter.type.ToDspFilterType(),
                                                                            filter.cutoff,
                                                                            filter.q,
                                                                            filter.gainInDecibels,
                                                                            1);
                            listenerGraphState.nodes.Add(filterNode);
                            var filterConnection = commandBlock.Connect(filterNode, 0, previousFilterNode, previousFilterPort);
                            listenerGraphState.connections.Add(filterConnection);
                            previousFilterNode = filterNode;
                            previousFilterPort = 0;
                            if (!filterAdded && filterVolume < 1f)
                                commandBlock.SetAttenuation(filterConnection, filterVolume);
                            filterAdded = true;
                        }
                    }
                    if (filterAdded)
                    {
                        listenerGraphState.ildConnections.Add(new IldOutputConnection
                        {
                            ildOutputPort = -i - 1,  //The negative value stores the intended channel in a disconnected state
                            nodeInputPort = previousFilterPort,
                            node          = previousFilterNode,
                            attenuation   = 1f
                        });
                    }
                }
                if (passthroughVolume > 0f)
                {
                    if (filterAdded)
                    {
                        previousFilterPort = listenerMixPortCount;
                        commandBlock.AddInletPort(listenerMixNode, 1);
                        listenerMixPortCount++;
                    }
                    listenerGraphState.ildConnections.Add(new IldOutputConnection
                    {
                        ildOutputPort = -i - 1,
                        nodeInputPort = previousFilterPort,
                        node          = listenerMixNode,
                        attenuation   = passthroughVolume
                    });
                }
            }
            commandBlock.UpdateAudioKernel<MixPortsToStereoNodeUpdate, MixPortsToStereoNode.Parameters, MixPortsToStereoNode.SampleProviders, MixPortsToStereoNode>(
                new MixPortsToStereoNodeUpdate { leftChannelCount = listenerMixPortCount },
                listenerMixNode);
            for (int i = 0; i < profile.passthroughFractionsPerRightChannel.Length; i++)
            {
                bool    filterAdded        = false;
                DSPNode previousFilterNode = listenerMixNode;
                int     previousFilterPort = listenerMixPortCount;
                float   filterVolume       = profile.filterVolumesPerRightChannel[i] * (1f - profile.passthroughFractionsPerRightChannel[i]);
                float   passthroughVolume  = profile.passthroughVolumesPerRightChannel[i] * profile.passthroughFractionsPerRightChannel[i];
                commandBlock.AddInletPort(listenerMixNode, 1);
                listenerMixPortCount++;

                if (filterVolume > 0f)
                {
                    //We have to walk backwards to build the filter connections correctly since the ild outputs must remain disconnected
                    for (int j = profile.channelIndicesRight.Length - 1; j >= 0; j--)
                    {
                        if (i == profile.channelIndicesRight[j])
                        {
                            var filter     = profile.filtersRight[j];
                            var filterNode = StateVariableFilterNode.Create(commandBlock,
                                                                            filter.type.ToDspFilterType(),
                                                                            filter.cutoff,
                                                                            filter.q,
                                                                            filter.gainInDecibels,
                                                                            1);
                            listenerGraphState.nodes.Add(filterNode);
                            var filterConnection = commandBlock.Connect(filterNode, 0, previousFilterNode, previousFilterPort);
                            listenerGraphState.connections.Add(filterConnection);
                            previousFilterNode = filterNode;
                            previousFilterPort = 0;
                            if (!filterAdded && filterVolume < 1f)
                                commandBlock.SetAttenuation(filterConnection, filterVolume);
                            filterAdded = true;
                        }
                    }
                    if (filterAdded)
                    {
                        listenerGraphState.ildConnections.Add(new IldOutputConnection
                        {
                            ildOutputPort = -i - 1 - profile.passthroughFractionsPerLeftChannel.Length,  //The negative value stores the intended channel in a disconnected state
                            nodeInputPort = previousFilterPort,
                            node          = previousFilterNode,
                            attenuation   = 1f
                        });
                    }
                }
                if (passthroughVolume > 0f)
                {
                    if (filterAdded)
                    {
                        previousFilterPort = listenerMixPortCount;
                        commandBlock.AddInletPort(listenerMixNode, 1);
                        listenerMixPortCount++;
                    }
                    listenerGraphState.ildConnections.Add(new IldOutputConnection
                    {
                        ildOutputPort = -i - 1 - profile.passthroughFractionsPerLeftChannel.Length,
                        nodeInputPort = previousFilterPort,
                        node          = listenerMixNode,
                        attenuation   = passthroughVolume
                    });
                }
            }
        }
    }
}

