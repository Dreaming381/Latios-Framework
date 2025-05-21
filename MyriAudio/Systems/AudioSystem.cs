using Latios.Myri.Driver;
using Latios.Transforms.Abstract;
using Unity.Audio;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

using static Unity.Entities.SystemAPI;

namespace Latios.Myri.Systems
{
    [DisableAutoCreation]
    [UpdateInGroup(typeof(Latios.Systems.PreSyncPointGroup))]
    [BurstCompile]
    public partial struct AudioSystem : ISystem, ISystemShouldUpdate
    {
        bool m_initialized;

        private DSPGraph m_graph;
        private int      m_driverKey;
        private int      m_sampleRate;
        private int      m_samplesPerFrame;

        private DSPNode              m_mixNode;
        private DSPConnection        m_mixToLimiterMasterConnection;
        private NativeList<int>      m_mixNodePortFreelist;
        private NativeReference<int> m_mixNodePortCount;

        private DSPNode       m_limiterMasterNode;
        private DSPConnection m_limiterMasterToOutputConnection;

        private DSPNode                                     m_ildNode;
        private NativeReference<int>                        m_ildNodePortCount;
        private NativeReference<long>                       m_packedFrameCounterBufferId;  //MSB bufferId, LSB frame
        private NativeReference<int>                        m_audioFrame;
        private NativeReference<int>                        m_lastPlayedAudioFrame;
        private NativeReference<int>                        m_lastReadBufferId;
        private int                                         m_currentBufferId;
        private NativeList<OwnedIldBuffer>                  m_buffersInFlight;
        private NativeQueue<AudioFrameBufferHistoryElement> m_audioFrameHistory;
        private NativeList<ListenerGraphState>              m_listenerGraphStatesToDispose;

        private JobHandle   m_lastUpdateJobHandle;
        private EntityQuery m_aliveListenersQuery;
        private EntityQuery m_deadListenersQuery;
        private EntityQuery m_sourcesQuery;

        WorldTransformReadOnlyAspect.TypeHandle m_worldTransformHandle;

        LatiosWorldUnmanaged latiosWorld;

        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();

            m_initialized = false;

            latiosWorld.worldBlackboardEntity.AddComponentDataIfMissing(new AudioSettings
            {
                safetyAudioFrames             = 2,
                audioFramesPerUpdate          = 1,
                lookaheadAudioFrames          = 0,
                logWarningIfBuffersAreStarved = false
            });

            // Create queries
            m_aliveListenersQuery = state.Fluent().With<AudioListener>(true).Build();
            m_deadListenersQuery  = state.Fluent().Without<AudioListener>().With<ListenerGraphState>().Build();
            m_sourcesQuery        = state.Fluent().With<AudioSourceVolume, AudioSourceDistanceFalloff>(true).WithWorldTransformReadOnly().With<AudioSourceClip>(false).Build();

            m_worldTransformHandle = new WorldTransformReadOnlyAspect.TypeHandle(ref state);
        }

        public bool ShouldUpdateSystem(ref SystemState state)
        {
            if (m_initialized)
            {
                DriverManager.Update();
                return true;
            }

            if (m_aliveListenersQuery.IsEmptyIgnoreFilter && m_deadListenersQuery.IsEmptyIgnoreFilter && m_sourcesQuery.IsEmptyIgnoreFilter)
                return false;

            m_initialized = true;

            // Initialize containers first
            m_mixNodePortFreelist          = new NativeList<int>(Allocator.Persistent);
            m_mixNodePortCount             = new NativeReference<int>(Allocator.Persistent);
            m_ildNodePortCount             = new NativeReference<int>(Allocator.Persistent);
            m_packedFrameCounterBufferId   = new NativeReference<long>(Allocator.Persistent);
            m_audioFrame                   = new NativeReference<int>(Allocator.Persistent);
            m_lastPlayedAudioFrame         = new NativeReference<int>(Allocator.Persistent);
            m_lastReadBufferId             = new NativeReference<int>(Allocator.Persistent);
            m_buffersInFlight              = new NativeList<OwnedIldBuffer>(Allocator.Persistent);
            m_audioFrameHistory            = new NativeQueue<AudioFrameBufferHistoryElement>(Allocator.Persistent);
            m_listenerGraphStatesToDispose = new NativeList<ListenerGraphState>(Allocator.Persistent);

            // Create graph and driver
            var format   = ChannelEnumConverter.GetSoundFormatFromSpeakerMode(UnityEngine.AudioSettings.speakerMode);
            var channels = ChannelEnumConverter.GetChannelCountFromSoundFormat(format);
            UnityEngine.AudioSettings.GetDSPBufferSize(out m_samplesPerFrame, out _);
            m_sampleRate = UnityEngine.AudioSettings.outputSampleRate;
            m_graph      = DSPGraph.Create(format, channels, m_samplesPerFrame, m_sampleRate);
            m_driverKey  = DriverManager.RegisterGraph(ref m_graph);

            var commandBlock = m_graph.CreateCommandBlock();
            m_mixNode        = commandBlock.CreateDSPNode<MixStereoPortsNode.Parameters, MixStereoPortsNode.SampleProviders, MixStereoPortsNode>();
            commandBlock.AddOutletPort(m_mixNode, 2);
            m_limiterMasterNode = commandBlock.CreateDSPNode<BrickwallLimiterNode.Parameters, BrickwallLimiterNode.SampleProviders, BrickwallLimiterNode>();
            commandBlock.AddInletPort(m_limiterMasterNode, 2);
            commandBlock.AddOutletPort(m_limiterMasterNode, 2);
            m_mixToLimiterMasterConnection    = commandBlock.Connect(m_mixNode, 0, m_limiterMasterNode, 0);
            m_limiterMasterToOutputConnection = commandBlock.Connect(m_limiterMasterNode, 0, m_graph.RootDSP, 0);
            m_ildNode                         = commandBlock.CreateDSPNode<ReadIldBuffersNode.Parameters, ReadIldBuffersNode.SampleProviders, ReadIldBuffersNode>();
            unsafe
            {
                commandBlock.UpdateAudioKernel<SetReadIldBuffersNodePackedFrameBufferId, ReadIldBuffersNode.Parameters, ReadIldBuffersNode.SampleProviders, ReadIldBuffersNode>(
                    new SetReadIldBuffersNodePackedFrameBufferId { ptr = (long*)m_packedFrameCounterBufferId.GetUnsafePtr() },
                    m_ildNode);
            }
            commandBlock.Complete();

            // Force initialization of Burst
            commandBlock  = m_graph.CreateCommandBlock();
            var dummyNode = commandBlock.CreateDSPNode<MixPortsToStereoNode.Parameters, MixPortsToStereoNode.SampleProviders, MixPortsToStereoNode>();
            StateVariableFilterNode.Create(commandBlock, StateVariableFilterNode.FilterType.Bandpass, 0f, 0f, 0f, 1);
            commandBlock.UpdateAudioKernel<MixPortsToStereoNodeUpdate, MixPortsToStereoNode.Parameters, MixPortsToStereoNode.SampleProviders, MixPortsToStereoNode>(
                new MixPortsToStereoNodeUpdate { leftChannelCount = 0 },
                dummyNode);
            commandBlock.UpdateAudioKernel<ReadIldBuffersNodeUpdate, ReadIldBuffersNode.Parameters, ReadIldBuffersNode.SampleProviders, ReadIldBuffersNode>(new ReadIldBuffersNodeUpdate
            {
                ildBuffer = new IldBuffer(),
            },
                                                                                                                                                            m_ildNode);
            commandBlock.Cancel();

            DriverManager.Update();
            return true;
        }

        [BurstCompile]
        public unsafe void OnUpdate(ref SystemState state)
        {
            var ecsJH = state.Dependency;

            // Query arrays
            var aliveListenerEntities = m_aliveListenersQuery.ToEntityArray(state.WorldUpdateAllocator);
            var deadListenerEntities  = m_deadListenersQuery.ToEntityArray(state.WorldUpdateAllocator);

            // Type handles
            m_worldTransformHandle.Update(ref state);

            var audioSettingsLookup          = GetComponentLookup<AudioSettings>(true);
            var listenerLookup               = GetComponentLookup<AudioListener>(true);
            var listenerGraphStateLookup     = GetComponentLookup<ListenerGraphState>(false);
            var entityOutputGraphStateLookup = GetComponentLookup<EntityOutputGraphState>(false);

            // Buffer
            m_currentBufferId++;
            var ildBuffer = new OwnedIldBuffer
            {
                buffer   = new NativeList<float>(Allocator.Persistent),
                channels = new NativeList<IldBufferChannel>(Allocator.Persistent),
                bufferId = m_currentBufferId
            };

            // Containers
            var ecb                      = latiosWorld.syncPoint.CreateEntityCommandBuffer();
            var dspCommandBlock          = m_graph.CreateCommandBlock();
            var listenersWithTransforms  = new NativeList<ListenerWithTransform>(aliveListenerEntities.Length, state.WorldUpdateAllocator);
            var listenerBufferParameters = CollectionHelper.CreateNativeArray<ListenerBufferParameters>(aliveListenerEntities.Length,
                                                                                                        state.WorldUpdateAllocator,
                                                                                                        NativeArrayOptions.UninitializedMemory);
            var sourcesChunkCount     = m_sourcesQuery.CalculateChunkCountWithoutFiltering();
            var capturedSourcesStream = new NativeStream(sourcesChunkCount, state.WorldUpdateAllocator);
            var channelCount          = CollectionHelper.CreateNativeArray<int>(1, state.WorldUpdateAllocator, NativeArrayOptions.ClearMemory);
            var chunkChannelCount     = CollectionHelper.CreateNativeArray<int>(1, state.WorldUpdateAllocator, NativeArrayOptions.ClearMemory);
            var channelCountPtr       = (int*)channelCount.GetUnsafeReadOnlyPtr();

            // Jobs
            m_lastUpdateJobHandle.Complete();

            // This may lag behind what the job threads will see.
            // That's fine, as this is only used for disposing memory.
            int lastReadIldBufferFromMainThread = m_lastReadBufferId.Value;

            var captureListenersJH = new InitUpdateDestroy.UpdateListenersJob
            {
                listenerHandle          = GetComponentTypeHandle<AudioListener>(true),
                worldTransformHandle    = m_worldTransformHandle,
                listenersWithTransforms = listenersWithTransforms,
                channelCount            = channelCount,
                sourceChunkChannelCount = chunkChannelCount,
                sourceChunkCount        = sourcesChunkCount
            }.Schedule(m_aliveListenersQuery, ecsJH);

            var captureFrameJH = new GraphHandling.CaptureIldFrameJob
            {
                packedFrameCounterBufferId = m_packedFrameCounterBufferId,
                audioFrame                 = m_audioFrame,
                lastPlayedAudioFrame       = m_lastPlayedAudioFrame,
                lastReadBufferId           = m_lastReadBufferId,
                audioFrameHistory          = m_audioFrameHistory,
                audioSettingsLookup        = audioSettingsLookup,
                worldBlackboardEntity      = latiosWorld.worldBlackboardEntity
            }.Schedule();

            var updateListenersGraphJH = new GraphHandling.UpdateListenersGraphJob
            {
                listenerEntities                = aliveListenerEntities,
                destroyedListenerEntities       = deadListenerEntities,
                listenerLookup                  = listenerLookup,
                listenerGraphStateLookup        = listenerGraphStateLookup,
                listenerOutputGraphStateLookup  = entityOutputGraphStateLookup,
                ecb                             = ecb,
                statesToDisposeThisFrame        = m_listenerGraphStatesToDispose,
                audioSettingsLookup             = audioSettingsLookup,
                worldBlackboardEntity           = latiosWorld.worldBlackboardEntity,
                audioFrame                      = m_audioFrame,
                audioFrameHistory               = m_audioFrameHistory,
                systemMixNodePortFreelist       = m_mixNodePortFreelist,
                systemMixNodePortCount          = m_mixNodePortCount,
                systemMixNode                   = m_mixNode,
                systemIldNodePortCount          = m_ildNodePortCount,
                systemIldNode                   = m_ildNode,
                commandBlock                    = dspCommandBlock,
                listenerBufferParameters        = listenerBufferParameters,
                outputSamplesMegaBuffer         = ildBuffer.buffer,
                outputSamplesMegaBufferChannels = ildBuffer.channels,
                bufferId                        = m_currentBufferId,
                samplesPerFrame                 = m_samplesPerFrame
            }.Schedule(JobHandle.CombineDependencies(captureListenersJH, captureFrameJH));

            var updateSourcesJH = new InitUpdateDestroy.UpdateClipAudioSourcesJob
            {
                audioFrame                 = m_audioFrame,
                lastPlayedAudioFrame       = m_lastPlayedAudioFrame,
                bufferId                   = m_currentBufferId,
                clipHandle                 = GetComponentTypeHandle<AudioSourceClip>(false),
                distanceFalloffHandle      = GetComponentTypeHandle<AudioSourceDistanceFalloff>(true),
                emitterConeHandle          = GetComponentTypeHandle<AudioSourceEmitterCone>(true),
                expireHandle               = GetComponentTypeHandle<AudioSourceDestroyOneShotWhenFinished>(false),
                lastConsumedBufferId       = m_lastReadBufferId,
                sampleRate                 = m_sampleRate,
                sampleRateMultiplierHandle = GetComponentTypeHandle<AudioSourceSampleRateMultiplier>(true),
                samplesPerFrame            = m_samplesPerFrame,
                stream                     = capturedSourcesStream.AsWriter(),
                volumeHandle               = GetComponentTypeHandle<AudioSourceVolume>(true),
                worldTransformHandle       = m_worldTransformHandle
            }.ScheduleParallel(m_sourcesQuery, JobHandle.CombineDependencies(captureFrameJH, ecsJH));

            state.Dependency = JobHandle.CombineDependencies(updateListenersGraphJH, updateSourcesJH);  // updateListenersGraphJH includes captureListener and captureFrame jobs

            // No more ECS

            // If there are no sources, then we should early out. Otherwise NativeStream has a bad time.
            JobHandle shipItJH = state.Dependency;
            if (sourcesChunkCount > 0)
            {
                // Deferred Containers
                var allocateChunkChannelStreamsJH = NativeStream.ScheduleConstruct(out var chunkChannelStreams, chunkChannelCount, captureListenersJH, state.WorldUpdateAllocator);
                var allocateChannelStreamsJH      = NativeStream.ScheduleConstruct(out var channelStreams, channelCount, captureListenersJH, state.WorldUpdateAllocator);

                var cullingWeightingJH = new CullingAndWeighting.CullAndWeightJob
                {
                    capturedSources         = capturedSourcesStream.AsReader(),
                    channelCount            = channelCount,
                    chunkChannelStreams     = chunkChannelStreams.AsWriter(),
                    listenersWithTransforms = listenersWithTransforms.AsDeferredJobArray()
                }.ScheduleParallel(sourcesChunkCount, 1, JobHandle.CombineDependencies(allocateChunkChannelStreamsJH, updateSourcesJH));

                var batchingJH = new Batching.BatchJob
                {
                    capturedSources     = capturedSourcesStream.AsReader(),
                    channelStream       = channelStreams.AsWriter(),
                    chunkChannelStreams = chunkChannelStreams.AsReader()
                }.Schedule(channelCountPtr, 1, JobHandle.CombineDependencies(allocateChannelStreamsJH, cullingWeightingJH));

                var samplingJH = new Sampling.SampleJob
                {
                    audioFrame              = m_audioFrame,
                    capturedSources         = capturedSourcesStream.AsReader(),
                    channelStreams          = channelStreams.AsReader(),
                    outputSamplesMegaBuffer = ildBuffer.buffer.AsDeferredJobArray(),
                    sampleRate              = m_sampleRate,
                    samplesPerFrame         = m_samplesPerFrame,
                }.Schedule(channelCountPtr, 1, JobHandle.CombineDependencies(updateListenersGraphJH, batchingJH));

                shipItJH = samplingJH;
            }

            shipItJH = new GraphHandling.SubmitToDspGraphJob
            {
                commandBlock = dspCommandBlock
            }.Schedule(shipItJH);

            var disposeJobHandles = new NativeList<JobHandle>(Allocator.Temp);
            disposeJobHandles.Add(shipItJH);

            for (int i = 0; i < m_buffersInFlight.Length; i++)
            {
                var buffer = m_buffersInFlight[i];
                if (buffer.bufferId - lastReadIldBufferFromMainThread < 0)
                {
                    disposeJobHandles.Add(buffer.buffer.Dispose(ecsJH));
                    disposeJobHandles.Add(buffer.channels.Dispose(ecsJH));
                    m_buffersInFlight.RemoveAtSwapBack(i);
                    i--;
                }
            }

            m_lastUpdateJobHandle = JobHandle.CombineDependencies(disposeJobHandles.AsArray());

            m_buffersInFlight.Add(ildBuffer);
        }

        public void OnDestroy(ref SystemState state)
        {
            if (!m_initialized)
                return;

            //UnityEngine.Debug.Log("AudioSystem.OnDestroy");
            m_lastUpdateJobHandle.Complete();
            state.CompleteDependency();
            var commandBlock = m_graph.CreateCommandBlock();
            foreach (var s in m_listenerGraphStatesToDispose)
            {
                foreach (var c in s.ildConnections)
                    commandBlock.Disconnect(c.connection);
                foreach (var c in s.connections)
                    commandBlock.Disconnect(c);
                foreach (var n in s.nodes)
                    commandBlock.ReleaseDSPNode(n);
                s.nodes.Dispose();
                s.ildConnections.Dispose();
                s.connections.Dispose();
            }
            commandBlock.Disconnect(m_mixToLimiterMasterConnection);
            commandBlock.Disconnect(m_limiterMasterToOutputConnection);
            commandBlock.ReleaseDSPNode(m_ildNode);
            commandBlock.ReleaseDSPNode(m_mixNode);
            commandBlock.ReleaseDSPNode(m_limiterMasterNode);
            commandBlock.Complete();
            DriverManager.DeregisterAndDisposeGraph(m_driverKey);

            m_lastUpdateJobHandle.Complete();
            m_mixNodePortFreelist.Dispose();
            m_mixNodePortCount.Dispose();
            m_ildNodePortCount.Dispose();
            m_packedFrameCounterBufferId.Dispose();
            m_audioFrame.Dispose();
            m_lastPlayedAudioFrame.Dispose();
            m_lastReadBufferId.Dispose();
            m_audioFrameHistory.Dispose();
            m_listenerGraphStatesToDispose.Dispose();

            foreach (var buffer in m_buffersInFlight)
            {
                buffer.buffer.Dispose();
                buffer.channels.Dispose();
            }
            m_buffersInFlight.Dispose();
        }

        private struct OwnedIldBuffer
        {
            public NativeList<float>            buffer;
            public NativeList<IldBufferChannel> channels;
            public int                          bufferId;
        }
    }
}

