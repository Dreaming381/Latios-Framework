using Latios.Transforms;
using Latios.Transforms.Abstract;
using static Unity.Entities.SystemAPI;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Audio;

namespace Latios.Myri.Systems
{
    [DisableAutoCreation]
    [UpdateInGroup(typeof(Latios.Systems.PreSyncPointGroup))]
    [BurstCompile]
    public partial struct AudioSystem : ISystem, ISystemShouldUpdate
    {
        LatiosWorldUnmanaged latiosWorld;

        bool               m_initialized;
        RootOutputInstance m_rootOutputInstance;
        BlobRetainer       m_blobRetainer;

        NativeQueue<AudioFrameBufferHistoryElement> m_audioFrameHistory;
        NativeReference<CapturedFrameState>         m_capturedFrameState;
        NativeList<OwnedSampleMegaBuffer>           m_ownedMegaBuffers;

        EntityQuery m_sourcesQuery;
        EntityQuery m_changedListenersQuery;
        EntityQuery m_aliveListenersQuery;

        WorldTransformReadOnlyAspect.TypeHandle m_worldTransformHandle;

        int m_previousConsumedCommandId;
        int m_twoAgoConsumedCommandId;

        public void OnCreate(ref SystemState state)
        {
            latiosWorld = state.GetLatiosWorldUnmanaged();

            m_sourcesQuery          = state.Fluent().With<AudioSourceVolume>(true).With<AudioSourceClip>(false).Build();
            m_changedListenersQuery = state.Fluent().WithAnyEnabled<AudioListener, TrackedListener>(true).Build();
            m_aliveListenersQuery   = state.Fluent().With<AudioListener>(true).WithWorldTransformReadOnly().Build();

            m_worldTransformHandle = new WorldTransformReadOnlyAspect.TypeHandle(ref state);

            latiosWorld.worldBlackboardEntity.AddComponentDataIfMissing(AudioSettings.kDefault);

            var carrier = latiosWorld.worldBlackboardEntity.GetManagedStructComponent<AudioEcsBootstrapCarrier>();
            if (!carrier.bootstrap.ShouldWaitForMyriSourceOrListenerBeforeStarting())
            {
                Initialize(ref state, carrier.bootstrap);
            }

            m_previousConsumedCommandId = -1;
            m_twoAgoConsumedCommandId   = -1;
        }

        public bool ShouldUpdateSystem(ref SystemState state)
        {
            if (m_initialized)
                return true;

            if (m_sourcesQuery.IsEmptyIgnoreFilter)
                return false;

            var carrier = latiosWorld.worldBlackboardEntity.GetManagedStructComponent<AudioEcsBootstrapCarrier>();
            Initialize(ref state, carrier.bootstrap);
            return true;
        }

        void Initialize(ref SystemState state, IAudioEcsBootstrap bootstrap)
        {
            m_rootOutputInstance = MyriBootstrap.CreateCustomAudioEcsRuntime(bootstrap, latiosWorld, ControlContext.builtIn);
            m_blobRetainer       = new BlobRetainer();
            m_blobRetainer.Init();

            m_audioFrameHistory  = new NativeQueue<AudioFrameBufferHistoryElement>(Allocator.Persistent);
            m_capturedFrameState = new NativeReference<CapturedFrameState>(Allocator.Persistent);
            m_ownedMegaBuffers   = new NativeList<OwnedSampleMegaBuffer>(Allocator.Persistent);

            m_initialized = true;
        }

        public void OnDestroy(ref SystemState state)
        {
            if (m_initialized)
            {
                MyriBootstrap.ShutdownCustomAudioEcsRuntime(m_rootOutputInstance, ControlContext.builtIn);
                m_blobRetainer.Dispose();

                m_audioFrameHistory.Dispose();
                m_capturedFrameState.Dispose();
                foreach (var buffer in m_ownedMegaBuffers)
                {
                    buffer.buffer.Dispose();
                    buffer.releaseFrame.Dispose();
                }
                m_ownedMegaBuffers.Dispose();

                m_initialized = false;
            }
        }

        [BurstCompile]
        public unsafe void OnUpdate(ref SystemState state)
        {
            var atomicIds   = latiosWorld.worldBlackboardEntity.GetComponentData<AudioEcsAtomicFeedbackIds>();
            var commandPipe = latiosWorld.GetCollectionComponent<AudioEcsCommandPipe>(latiosWorld.worldBlackboardEntity, out var commandPipeJh, true);
            // Complete writes to this handle, just in case a user declares the dependency wrong.
            commandPipeJh.Complete();

            var ecsJh = state.Dependency;

            m_worldTransformHandle.Update(ref state);

            var listenerCount            = m_aliveListenersQuery.CalculateEntityCountWithoutFiltering();
            var listenersWithTransforms  = new NativeList<ListenerWithTransform>(listenerCount, state.WorldUpdateAllocator);
            var listenersChannelIDs      = new NativeList<AudioSourceChannelID>(listenerCount, state.WorldUpdateAllocator);
            var culledListeners          = new NativeList<ListenerWithPresampling>(listenerCount, state.WorldUpdateAllocator);
            var listenersWithPresampling = new NativeList<ListenerWithPresampling>(listenerCount, state.WorldUpdateAllocator);
            var sourcesChunkCount        = m_sourcesQuery.CalculateChunkCountWithoutFiltering();
            var capturedSourcesStream    = new NativeStream(sourcesChunkCount, state.WorldUpdateAllocator);
            var channelCount             = CollectionHelper.CreateNativeArray<int>(1, state.WorldUpdateAllocator, NativeArrayOptions.ClearMemory);
            var chunkChannelCount        = CollectionHelper.CreateNativeArray<int>(1, state.WorldUpdateAllocator, NativeArrayOptions.ClearMemory);
            var channelCountPtr          = (int*)channelCount.GetUnsafeReadOnlyPtr();

            var changedListenersJh = new InitUpdateDestroy.UpdateChangedListenersJob
            {
                channelGuidHandle = GetBufferTypeHandle<AudioListenerChannelID>(true),
                commandPipe       = commandPipe,
                ecb               = latiosWorld.syncPoint.CreateEntityCommandBuffer(),
                entityHandle      = GetEntityTypeHandle(),
                lastSystemVersion = state.LastSystemVersion,
                listenerHandle    = GetComponentTypeHandle<AudioListener>(true),
            }.Schedule(m_changedListenersQuery, ecsJh);

            var captureListenersJh = new InitUpdateDestroy.CaptureListenersForSamplingJob
            {
                channelCount             = channelCount,
                channelGuidHandle        = GetBufferTypeHandle<AudioListenerChannelID>(true),
                culledListeners          = culledListeners,
                entityHandle             = GetEntityTypeHandle(),
                listenerHandle           = GetComponentTypeHandle<AudioListener>(true),
                listenersChannelIDs      = listenersChannelIDs,
                listenersWithPresampling = listenersWithPresampling,
                listenersWithTransforms  = listenersWithTransforms,
                sourceChunkChannelCount  = chunkChannelCount,
                sourceChunkCount         = sourcesChunkCount,
                worldTransformHandle     = m_worldTransformHandle,
            }.Schedule(m_aliveListenersQuery, ecsJh);

            var captureFrameJh = new InitUpdateDestroy.CaptureIldFrameJob
            {
                audioFrameHistory     = m_audioFrameHistory,
                audioSettingsLookup   = GetComponentLookup<AudioSettings>(true),
                atomicLookup          = GetComponentLookup<AudioEcsAtomicFeedbackIds>(true),
                capturedFrameState    = m_capturedFrameState,
                formatLookup          = GetComponentLookup<AudioEcsFormat>(true),
                worldBlackboardEntity = latiosWorld.worldBlackboardEntity
            }.Schedule(ecsJh);

            var sourcesInputJh  = JobHandle.CombineDependencies(captureFrameJh, captureListenersJh);
            var updateSourcesJh = new InitUpdateDestroy.UpdateClipAudioSourcesJob
            {
                capturedFrameState         = m_capturedFrameState,
                channelIDHandle            = GetComponentTypeHandle<AudioSourceChannelID>(true),
                clipHandle                 = GetComponentTypeHandle<AudioSourceClip>(false),
                commandPipe                = commandPipe,
                distanceFalloffHandle      = GetComponentTypeHandle<AudioSourceDistanceFalloff>(true),
                emitterConeHandle          = GetComponentTypeHandle<AudioSourceEmitterCone>(true),
                expireHandle               = GetComponentTypeHandle<AudioSourceDestroyOneShotWhenFinished>(false),
                sampleRateMultiplierHandle = GetComponentTypeHandle<AudioSourceSampleRateMultiplier>(true),
                stream                     = capturedSourcesStream.AsWriter(),
                volumeHandle               = GetComponentTypeHandle<AudioSourceVolume>(true),
                worldTransformHandle       = m_worldTransformHandle,
            }.ScheduleParallel(m_sourcesQuery, sourcesInputJh);

            // Clean up buffers now before we optionally add a new one.
            {
                var ids              = atomicIds.Read();
                var lastStartedFrame = ids.feedbackIdStarted;
                if (ids.maxCommandIdConsumed != m_previousConsumedCommandId)
                {
                    m_twoAgoConsumedCommandId   = m_previousConsumedCommandId;
                    m_previousConsumedCommandId = ids.maxCommandIdConsumed;
                }

                var dst = 0;
                for (int src = 0; src < m_ownedMegaBuffers.Length; src++)
                {
                    if (m_ownedMegaBuffers[src].releaseFrame.Value - lastStartedFrame < 0)
                    {
                        m_ownedMegaBuffers[src].releaseFrame.Dispose();
                        m_ownedMegaBuffers[src].buffer.Dispose();
                    }
                    else
                    {
                        m_ownedMegaBuffers[dst] = m_ownedMegaBuffers[src];
                        dst++;
                    }
                }
                m_ownedMegaBuffers.Length = dst;

                m_blobRetainer.Update(state.EntityManager, commandPipe.commandId, m_twoAgoConsumedCommandId);
            }

            // End ECS processing
            state.Dependency = JobHandle.CombineDependencies(updateSourcesJh, changedListenersJh);

            var backgroundJh = state.Dependency;
            // We check for greater than 0 for both due to an old NativeStream issue which may or may not be fixed now.
            if (sourcesChunkCount > 0 && listenerCount > 0)
            {
                // Deferred Containers
                var allocateChunkChannelStreamsJh = NativeStream.ScheduleConstruct(out var chunkChannelStreams, chunkChannelCount, captureListenersJh, state.WorldUpdateAllocator);
                var allocateChannelStreamsJh      = NativeStream.ScheduleConstruct(out var channelStreams, channelCount, captureListenersJh, state.WorldUpdateAllocator);
                var outputRangesByChannel         = new NativeList<int2>(state.WorldUpdateAllocator);
                var samplesBuffer                 = new NativeList<float>(Allocator.Persistent);
                var releaseFrame                  = new NativeReference<int>(Allocator.Persistent);

                var cullingWeightingJh = new CullingAndWeighting.CullAndWeightJob
                {
                    capturedSources         = capturedSourcesStream.AsReader(),
                    channelCount            = channelCount,
                    chunkChannelStreams     = chunkChannelStreams.AsWriter(),
                    listenersWithTransforms = listenersWithTransforms.AsDeferredJobArray(),
                    listenersChannelIDs     = listenersChannelIDs.AsDeferredJobArray(),
                }.ScheduleParallel(sourcesChunkCount, 1, JobHandle.CombineDependencies(allocateChunkChannelStreamsJh, updateSourcesJh));

                var batchingInputJh = JobHandle.CombineDependencies(allocateChannelStreamsJh, cullingWeightingJh);
                var batchingJh      = new Batching.BatchJob
                {
                    capturedSources     = capturedSourcesStream.AsReader(),
                    channelStream       = channelStreams.AsWriter(),
                    chunkChannelStreams = chunkChannelStreams.AsReader()
                }.Schedule(channelCountPtr, 1, batchingInputJh);

                var allocateJh = new Batching.AllocateChannelsJob
                {
                    capturedFrameState       = m_capturedFrameState,
                    channelCount             = channelCount,
                    chunkChannelStreams      = chunkChannelStreams.AsReader(),
                    commandPipe              = commandPipe,
                    culledListeners          = culledListeners.AsDeferredJobArray(),
                    listenersWithPresampling = listenersWithPresampling.AsDeferredJobArray(),
                    outputRangesByChannel    = outputRangesByChannel,
                    outputSamplesMegaBuffer  = samplesBuffer,
                    releaseFrame             = releaseFrame,
                }.Schedule(batchingInputJh);

                var samplingJh = new Sampling.SampleJob
                {
                    capturedSources         = capturedSourcesStream.AsReader(),
                    channelStreams          = channelStreams.AsReader(),
                    frameState              = m_capturedFrameState,
                    outputRangesByChannel   = outputRangesByChannel.AsDeferredJobArray(),
                    outputSamplesMegaBuffer = samplesBuffer.AsDeferredJobArray(),
                }.Schedule(channelCountPtr, 1, JobHandle.CombineDependencies(allocateJh, batchingJh));

                m_ownedMegaBuffers.Add(new OwnedSampleMegaBuffer
                {
                    buffer       = samplesBuffer,
                    releaseFrame = releaseFrame
                });

                backgroundJh = samplingJh;
            }
            else
            {
                var allocateJh = new Batching.AllocateSilenceJob
                {
                    capturedFrameState = m_capturedFrameState,
                    commandPipe        = commandPipe,
                    culledListeners    = culledListeners.AsDeferredJobArray(),
                }.Schedule(sourcesInputJh);
                backgroundJh = JobHandle.CombineDependencies(backgroundJh, allocateJh);
            }

            latiosWorld.UpdateCollectionComponentDependency<AudioEcsCommandPipe>(latiosWorld.worldBlackboardEntity, backgroundJh, true);
        }

        struct OwnedSampleMegaBuffer
        {
            public NativeList<float>    buffer;
            public NativeReference<int> releaseFrame;
        }
    }
}

