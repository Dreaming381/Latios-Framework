using Latios.Myri.Driver;
using Latios.Myri.DSP;
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

        private DSPNode              m_masterMixNode;
        private DSPConnection        m_masterMixToOutputConnection;
        private NativeList<int>      m_masterMixNodePortFreelist;
        private NativeReference<int> m_masterMixNodePortCount;

        private DSPNode                                     m_ildNode;
        private NativeReference<int>                        m_ildNodePortCount;
        private NativeReference<long>                       m_packedFrameCounterBufferId;  //MSB bufferId, LSB frame
        private NativeReference<int>                        m_audioFrame;
        private NativeReference<int>                        m_lastPlayedAudioFrame;
        private NativeReference<int>                        m_lastReadBufferId;
        private int                                         m_currentBufferId;
        private NativeList<OwnedIldBuffer>                  m_buffersInFlight;
        private NativeQueue<AudioFrameBufferHistoryElement> m_audioFrameHistory;
        private NativeList<ListenerGraphState>              m_listenerStatesToDisposeOnShutdown;

        private JobHandle   m_lastUpdateJobHandle;
        private EntityQuery m_aliveListenersQuery;
        private EntityQuery m_deadListenersQuery;
        private EntityQuery m_sourcesQuery;

        BlobRetainer m_blobRetainer;

        WorldTransformReadOnlyTypeHandle m_worldTransformHandle;

        LatiosWorldUnmanaged latiosWorld;

        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();

            m_initialized = false;

            latiosWorld.worldBlackboardEntity.AddComponentDataIfMissing(new AudioSettings
            {
                masterVolume                  = 1f,
                masterGain                    = 1f,
                masterLimiterDBRelaxPerSecond = BrickwallLimiter.kDefaultReleaseDBPerSample * 48000f,
                masterLimiterLookaheadTime    = 255.9f / 48000f,
                safetyAudioFrames             = 2,
                audioFramesPerUpdate          = 1,
                lookaheadAudioFrames          = 1,
                logWarningIfBuffersAreStarved = false
            });

            // Create queries
            m_aliveListenersQuery = state.Fluent().With<AudioListener>(true).Build();
            m_deadListenersQuery  = state.Fluent().Without<AudioListener>().With<ListenerGraphState>().Build();
            m_sourcesQuery        = state.Fluent().With<AudioSourceVolume>(true).With<AudioSourceClip>(false).Build();

            m_worldTransformHandle = new WorldTransformReadOnlyTypeHandle(ref state);
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
            m_masterMixNodePortFreelist         = new NativeList<int>(Allocator.Persistent);
            m_masterMixNodePortCount            = new NativeReference<int>(Allocator.Persistent);
            m_ildNodePortCount                  = new NativeReference<int>(Allocator.Persistent);
            m_packedFrameCounterBufferId        = new NativeReference<long>(Allocator.Persistent);
            m_audioFrame                        = new NativeReference<int>(Allocator.Persistent);
            m_lastPlayedAudioFrame              = new NativeReference<int>(Allocator.Persistent);
            m_lastReadBufferId                  = new NativeReference<int>(Allocator.Persistent);
            m_buffersInFlight                   = new NativeList<OwnedIldBuffer>(Allocator.Persistent);
            m_audioFrameHistory                 = new NativeQueue<AudioFrameBufferHistoryElement>(Allocator.Persistent);
            m_listenerStatesToDisposeOnShutdown = new NativeList<ListenerGraphState>(Allocator.Persistent);

            m_blobRetainer.Init();

            // Create graph and driver
            var format   = ChannelEnumConverter.GetSoundFormatFromSpeakerMode(UnityEngine.AudioSettings.speakerMode);
            var channels = ChannelEnumConverter.GetChannelCountFromSoundFormat(format);
            UnityEngine.AudioSettings.GetDSPBufferSize(out m_samplesPerFrame, out _);
            m_sampleRate = UnityEngine.AudioSettings.outputSampleRate;
            m_graph      = DSPGraph.Create(format, channels, m_samplesPerFrame, m_sampleRate);
            m_driverKey  = DriverManager.RegisterGraph(ref m_graph);

            var commandBlock = m_graph.CreateCommandBlock();
            m_masterMixNode  = commandBlock.CreateDSPNode<MasterMixNode.Parameters, MasterMixNode.SampleProviders, MasterMixNode>();
            commandBlock.AddOutletPort(m_masterMixNode, 2);
            m_masterMixToOutputConnection = commandBlock.Connect(m_masterMixNode, 0, m_graph.RootDSP, 0);
            m_ildNode                     = commandBlock.CreateDSPNode<ReadIldBuffersNode.Parameters, ReadIldBuffersNode.SampleProviders, ReadIldBuffersNode>();
            unsafe
            {
                commandBlock.UpdateAudioKernel<SetReadIldBuffersNodePackedFrameBufferId, ReadIldBuffersNode.Parameters, ReadIldBuffersNode.SampleProviders, ReadIldBuffersNode>(
                    new SetReadIldBuffersNodePackedFrameBufferId { ptr = (long*)m_packedFrameCounterBufferId.GetUnsafePtr() },
                    m_ildNode);
            }
            commandBlock.Complete();

            // Force initialization of Burst
            commandBlock  = m_graph.CreateCommandBlock();
            var dummyNode = commandBlock.CreateDSPNode<ListenerMixNode.Parameters, ListenerMixNode.SampleProviders, ListenerMixNode>();
            commandBlock.UpdateAudioKernel<ListenerMixNodeChannelUpdate, ListenerMixNode.Parameters, ListenerMixNode.SampleProviders, ListenerMixNode>(
                new ListenerMixNodeChannelUpdate { blob = default, sampleRate = 1024 },
                dummyNode);
            commandBlock.UpdateAudioKernel<MasterMixNodeUpdate, MasterMixNode.Parameters, MasterMixNode.SampleProviders, MasterMixNode>(new MasterMixNodeUpdate {
                settings = default
            },
                                                                                                                                        m_masterMixNode);
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

            var audioSettingsLookup      = GetComponentLookup<AudioSettings>(true);
            var listenerLookup           = GetComponentLookup<AudioListener>(true);
            var listenerGraphStateLookup = GetComponentLookup<ListenerGraphState>(false);

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
            var listenersChannelIDs      = new NativeList<AudioSourceChannelID>(aliveListenerEntities.Length, state.WorldUpdateAllocator);
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
                channelGuidHandle       = GetBufferTypeHandle<AudioListenerChannelID>(true),
                worldTransformHandle    = m_worldTransformHandle,
                listenersWithTransforms = listenersWithTransforms,
                listenersChannelIDs     = listenersChannelIDs,
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
                listenerEntities                  = aliveListenerEntities,
                destroyedListenerEntities         = deadListenerEntities,
                listenerLookup                    = listenerLookup,
                listenerGraphStateLookup          = listenerGraphStateLookup,
                ecb                               = ecb,
                listenerStatesToDisposeOnShutdown = m_listenerStatesToDisposeOnShutdown,
                audioSettingsLookup               = audioSettingsLookup,
                worldBlackboardEntity             = latiosWorld.worldBlackboardEntity,
                audioFrame                        = m_audioFrame,
                audioFrameHistory                 = m_audioFrameHistory,
                systemMasterMixNodePortFreelist   = m_masterMixNodePortFreelist,
                systemMasterMixNodePortCount      = m_masterMixNodePortCount,
                systemMasterMixNode               = m_masterMixNode,
                systemIldNodePortCount            = m_ildNodePortCount,
                systemIldNode                     = m_ildNode,
                commandBlock                      = dspCommandBlock,
                listenerBufferParameters          = listenerBufferParameters,
                outputSamplesMegaBuffer           = ildBuffer.buffer,
                outputSamplesMegaBufferChannels   = ildBuffer.channels,
                bufferId                          = m_currentBufferId,
                samplesPerFrame                   = m_samplesPerFrame,
                sampleRate                        = m_sampleRate,
            }.Schedule(JobHandle.CombineDependencies(captureListenersJH, captureFrameJH));

            var updateSourcesJH = new InitUpdateDestroy.UpdateClipAudioSourcesJob
            {
                audioFrame                 = m_audioFrame,
                lastPlayedAudioFrame       = m_lastPlayedAudioFrame,
                bufferId                   = m_currentBufferId,
                channelIDHandle            = GetComponentTypeHandle<AudioSourceChannelID>(true),
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
            if (sourcesChunkCount > 0 && aliveListenerEntities.Length > 0)
            {
                // Deferred Containers
                var allocateChunkChannelStreamsJH = NativeStream.ScheduleConstruct(out var chunkChannelStreams, chunkChannelCount, captureListenersJH, state.WorldUpdateAllocator);
                var allocateChannelStreamsJH      = NativeStream.ScheduleConstruct(out var channelStreams, channelCount, captureListenersJH, state.WorldUpdateAllocator);

                var cullingWeightingJH = new CullingAndWeighting.CullAndWeightJob
                {
                    capturedSources         = capturedSourcesStream.AsReader(),
                    channelCount            = channelCount,
                    chunkChannelStreams     = chunkChannelStreams.AsWriter(),
                    listenersWithTransforms = listenersWithTransforms.AsDeferredJobArray(),
                    listenersChannelIDs     = listenersChannelIDs.AsDeferredJobArray(),
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

            m_blobRetainer.Update(state.EntityManager, ildBuffer.bufferId, lastReadIldBufferFromMainThread);
        }

        public unsafe void OnDestroy(ref SystemState state)
        {
            if (!m_initialized)
                return;

            //UnityEngine.Debug.Log("AudioSystem.OnDestroy");
            m_lastUpdateJobHandle.Complete();
            state.CompleteDependency();
            var commandBlock = m_graph.CreateCommandBlock();
            foreach (var s in m_listenerStatesToDisposeOnShutdown)
            {
                if (!s.ildConnections.IsCreated)
                {
                    continue;
                }
                foreach (var c in s.ildConnections)
                    commandBlock.Disconnect(c);
                commandBlock.Disconnect(s.masterOutputConnection);
                commandBlock.ReleaseDSPNode(s.listenerMixNode);
                s.ildConnections.Dispose();
            }
            commandBlock.Disconnect(m_masterMixToOutputConnection);
            commandBlock.ReleaseDSPNode(m_ildNode);
            commandBlock.ReleaseDSPNode(m_masterMixNode);
            commandBlock.Complete();
            DriverManager.DeregisterAndDisposeGraph(m_driverKey);

            m_lastUpdateJobHandle.Complete();
            m_masterMixNodePortFreelist.Dispose();
            m_masterMixNodePortCount.Dispose();
            m_ildNodePortCount.Dispose();
            m_packedFrameCounterBufferId.Dispose();
            m_audioFrame.Dispose();
            m_lastPlayedAudioFrame.Dispose();
            m_lastReadBufferId.Dispose();
            m_audioFrameHistory.Dispose();
            m_listenerStatesToDisposeOnShutdown.Dispose();

            foreach (var buffer in m_buffersInFlight)
            {
                buffer.buffer.Dispose();
                buffer.channels.Dispose();
            }
            m_buffersInFlight.Dispose();

            m_blobRetainer.Dispose();
        }

        private struct OwnedIldBuffer
        {
            public NativeList<float>            buffer;
            public NativeList<IldBufferChannel> channels;
            public int                          bufferId;
        }
    }
}

