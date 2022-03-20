using System.Collections.Generic;
using Unity.Audio;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace Latios.Myri.Systems
{
    [UpdateInGroup(typeof(Latios.Systems.PreSyncPointGroup))]
    public partial class AudioSystem : SubSystem
    {
        private DSPGraph             m_graph;
        private LatiosDSPGraphDriver m_driver;
        private AudioOutputHandle    m_outputHandle;
        private int                  m_sampleRate;
        private int                  m_samplesPerFrame;

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
        private List<ManagedIldBuffer>                      m_buffersInFlight;
        private NativeQueue<AudioFrameBufferHistoryElement> m_audioFrameHistory;

        private JobHandle   m_lastUpdateJobHandle;
        private EntityQuery m_aliveListenersQuery;
        private EntityQuery m_deadListenersQuery;
        private EntityQuery m_oneshotsToDestroyWhenFinishedQuery;
        private EntityQuery m_oneshotsQuery;
        private EntityQuery m_loopedQuery;

        protected override void OnCreate()
        {
            //Initialize containers first
            m_mixNodePortFreelist        = new NativeList<int>(Allocator.Persistent);
            m_mixNodePortCount           = new NativeReference<int>(Allocator.Persistent);
            m_ildNodePortCount           = new NativeReference<int>(Allocator.Persistent);
            m_packedFrameCounterBufferId = new NativeReference<long>(Allocator.Persistent);
            m_audioFrame                 = new NativeReference<int>(Allocator.Persistent);
            m_lastPlayedAudioFrame       = new NativeReference<int>(Allocator.Persistent);
            m_lastReadBufferId           = new NativeReference<int>(Allocator.Persistent);
            m_buffersInFlight            = new List<ManagedIldBuffer>();
            m_audioFrameHistory          = new NativeQueue<AudioFrameBufferHistoryElement>(Allocator.Persistent);

            worldBlackboardEntity.AddComponentDataIfMissing(new AudioSettings
            {
                safetyAudioFrames             = 2,
                audioFramesPerUpdate          = 1,
                lookaheadAudioFrames          = 0,
                logWarningIfBuffersAreStarved = false
            });

            //Create graph and driver
            var format   = ChannelEnumConverter.GetSoundFormatFromSpeakerMode(UnityEngine.AudioSettings.speakerMode);
            var channels = ChannelEnumConverter.GetChannelCountFromSoundFormat(format);
            UnityEngine.AudioSettings.GetDSPBufferSize(out m_samplesPerFrame, out _);
            m_sampleRate   = UnityEngine.AudioSettings.outputSampleRate;
            m_graph        = DSPGraph.Create(format, channels, m_samplesPerFrame, m_sampleRate);
            m_driver       = new LatiosDSPGraphDriver { Graph = m_graph };
            m_outputHandle = m_driver.AttachToDefaultOutput();

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

            //Create queries
            m_aliveListenersQuery                = Fluent.WithAll<AudioListener>(true).Build();
            m_deadListenersQuery                 = Fluent.Without<AudioListener>().WithAll<ListenerGraphState>().Build();
            m_oneshotsToDestroyWhenFinishedQuery = Fluent.WithAll<AudioSourceOneShot>().WithAll<AudioSourceDestroyOneShotWhenFinished>(true).Build();
            m_oneshotsQuery                      = Fluent.WithAll<AudioSourceOneShot>().Build();
            m_loopedQuery                        = Fluent.WithAll<AudioSourceLooped>().Build();

            //Force initialization of Burst
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
        }

        protected override void OnUpdate()
        {
            var ecsJH = Dependency;

            //Query arrays
            var aliveListenerEntities = m_aliveListenersQuery.ToEntityArrayAsync(Allocator.TempJob, out JobHandle aliveListenerEntitiesJH);
            var deadListenerEntities  = m_deadListenersQuery.ToEntityArrayAsync(Allocator.TempJob, out JobHandle deadListenerEntitiesJH);
            var listenerEntitiesJH    = JobHandle.CombineDependencies(aliveListenerEntitiesJH, deadListenerEntitiesJH);

            //Type handles
            var entityHandle      = GetEntityTypeHandle();
            var listenerHandle    = GetComponentTypeHandle<AudioListener>(true);
            var oneshotHandle     = GetComponentTypeHandle<AudioSourceOneShot>(false);
            var loopedHandle      = GetComponentTypeHandle<AudioSourceLooped>(false);
            var coneHandle        = GetComponentTypeHandle<AudioSourceEmitterCone>(true);
            var translationHandle = GetComponentTypeHandle<Translation>(true);
            var rotationHandle    = GetComponentTypeHandle<Rotation>(true);
            var ltwHandle         = GetComponentTypeHandle<LocalToWorld>(true);
            var parentHandle      = GetComponentTypeHandle<Parent>(true);

            var audioSettingsCdfe          = GetComponentDataFromEntity<AudioSettings>(true);
            var listenerCdfe               = GetComponentDataFromEntity<AudioListener>(true);
            var listenerGraphStateCdfe     = GetComponentDataFromEntity<ListenerGraphState>(false);
            var entityOutputGraphStateCdfe = GetComponentDataFromEntity<EntityOutputGraphState>(false);

            //Buffer
            m_currentBufferId++;
            var ildBuffer = new ManagedIldBuffer
            {
                buffer   = new NativeList<float>(Allocator.Persistent),
                channels = new NativeList<IldBufferChannel>(Allocator.Persistent),
                bufferId = m_currentBufferId
            };

            //Containers
            var destroyCommandBuffer     = latiosWorld.syncPoint.CreateDestroyCommandBuffer().AsParallelWriter();
            var entityCommandBuffer      = latiosWorld.syncPoint.CreateEntityCommandBuffer();
            var dspCommandBlock          = m_graph.CreateCommandBlock();
            var listenersWithTransforms  = new NativeList<ListenerWithTransform>(aliveListenerEntities.Length, Allocator.TempJob);
            var listenerBufferParameters = new NativeArray<ListenerBufferParameters>(aliveListenerEntities.Length,
                                                                                     Allocator.TempJob,
                                                                                     NativeArrayOptions.UninitializedMemory);
            var forIndexToListenerAndChannelIndices = new NativeList<int2>(Allocator.TempJob);
            var oneshotEmitters                     = new NativeArray<OneshotEmitter>(m_oneshotsQuery.CalculateEntityCount(),
                                                                                      Allocator.TempJob,
                                                                                      NativeArrayOptions.UninitializedMemory);
            var loopedEmitters                    = new NativeArray<LoopedEmitter>(m_loopedQuery.CalculateEntityCount(), Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var oneshotWeightsStream              = new NativeStream(oneshotEmitters.Length / CullingAndWeighting.BATCH_SIZE + 1, Allocator.TempJob);
            var loopedWeightsStream               = new NativeStream(loopedEmitters.Length / CullingAndWeighting.BATCH_SIZE + 1, Allocator.TempJob);
            var oneshotListenerEmitterPairsStream = new NativeStream(oneshotEmitters.Length / CullingAndWeighting.BATCH_SIZE + 1, Allocator.TempJob);
            var loopedListenerEmitterPairsStream  = new NativeStream(loopedEmitters.Length / CullingAndWeighting.BATCH_SIZE + 1, Allocator.TempJob);
            var oneshotClipFrameLookups           = new NativeList<ClipFrameLookup>(Allocator.TempJob);
            var loopedClipFrameLookups            = new NativeList<ClipFrameLookup>(Allocator.TempJob);
            var oneshotBatchedWeights             = new NativeList<Weights>(Allocator.TempJob);
            var loopedBatchedWeights              = new NativeList<Weights>(Allocator.TempJob);
            var oneshotTargetListenerIndices      = new NativeList<int>(Allocator.TempJob);
            var loopedTargetListenerIndices       = new NativeList<int>(Allocator.TempJob);

            //Jobs
            m_lastUpdateJobHandle.Complete();

            //This may lag behind what the job threads will see.
            //That's fine, as this is only used for disposing memory.
            int lastReadIldBufferFromMainThread = m_lastReadBufferId.Value;

            var captureListenersJH = new InitUpdateDestroy.UpdateListenersJob
            {
                listenerHandle          = listenerHandle,
                translationHandle       = translationHandle,
                rotationHandle          = rotationHandle,
                ltwHandle               = ltwHandle,
                listenersWithTransforms = listenersWithTransforms
            }.Schedule(m_aliveListenersQuery, ecsJH);

            var captureFrameJH = new GraphHandling.CaptureIldFrameJob
            {
                packedFrameCounterBufferId = m_packedFrameCounterBufferId,
                audioFrame                 = m_audioFrame,
                lastPlayedAudioFrame       = m_lastPlayedAudioFrame,
                lastReadBufferId           = m_lastReadBufferId,
                audioFrameHistory          = m_audioFrameHistory,
                audioSettingsCdfe          = audioSettingsCdfe,
                worldBlackboardEntity      = worldBlackboardEntity
            }.Schedule();

            var ecsCaptureFrameJH = JobHandle.CombineDependencies(ecsJH, captureFrameJH);

            var updateListenersGraphJH = new GraphHandling.UpdateListenersGraphJob
            {
                listenerEntities                    = aliveListenerEntities,
                destroyedListenerEntities           = deadListenerEntities,
                listenerCdfe                        = listenerCdfe,
                listenerGraphStateCdfe              = listenerGraphStateCdfe,
                listenerOutputGraphStateCdfe        = entityOutputGraphStateCdfe,
                ecb                                 = entityCommandBuffer,
                audioSettingsCdfe                   = audioSettingsCdfe,
                worldBlackboardEntity               = worldBlackboardEntity,
                audioFrame                          = m_audioFrame,
                audioFrameHistory                   = m_audioFrameHistory,
                systemMixNodePortFreelist           = m_mixNodePortFreelist,
                systemMixNodePortCount              = m_mixNodePortCount,
                systemMixNode                       = m_mixNode,
                systemIldNodePortCount              = m_ildNodePortCount,
                systemIldNode                       = m_ildNode,
                commandBlock                        = dspCommandBlock,
                listenerBufferParameters            = listenerBufferParameters,
                forIndexToListenerAndChannelIndices = forIndexToListenerAndChannelIndices,
                outputSamplesMegaBuffer             = ildBuffer.buffer,
                outputSamplesMegaBufferChannels     = ildBuffer.channels,
                bufferId                            = m_currentBufferId,
                samplesPerFrame                     = m_samplesPerFrame
            }.Schedule(JobHandle.CombineDependencies(captureListenersJH, captureFrameJH, listenerEntitiesJH));

            var destroyOneshotsJH = new InitUpdateDestroy.DestroyOneshotsWhenFinishedJob
            {
                dcb                   = destroyCommandBuffer,
                entityHandle          = entityHandle,
                oneshotHandle         = oneshotHandle,
                audioFrame            = m_audioFrame,
                lastPlayedAudioFrame  = m_lastPlayedAudioFrame,
                sampleRate            = m_sampleRate,
                settingsCdfe          = audioSettingsCdfe,
                samplesPerFrame       = m_samplesPerFrame,
                worldBlackboardEntity = worldBlackboardEntity
            }.ScheduleParallel(m_oneshotsToDestroyWhenFinishedQuery, ecsCaptureFrameJH);

            var updateOneshotsJH = new InitUpdateDestroy.UpdateOneshotsJob
            {
                oneshotHandle        = oneshotHandle,
                ltwHandle            = ltwHandle,
                translationHandle    = translationHandle,
                rotationHandle       = rotationHandle,
                parentHandle         = parentHandle,
                coneHandle           = coneHandle,
                audioFrame           = m_audioFrame,
                lastPlayedAudioFrame = m_lastPlayedAudioFrame,
                lastConsumedBufferId = m_lastReadBufferId,
                bufferId             = m_currentBufferId,
                emitters             = oneshotEmitters
            }.ScheduleParallel(m_oneshotsQuery, destroyOneshotsJH);

            var updateLoopedJH = new InitUpdateDestroy.UpdateLoopedsJob
            {
                loopedHandle         = loopedHandle,
                ltwHandle            = ltwHandle,
                translationHandle    = translationHandle,
                rotationHandle       = rotationHandle,
                parentHandle         = parentHandle,
                coneHandle           = coneHandle,
                audioFrame           = m_audioFrame,
                lastConsumedBufferId = m_lastReadBufferId,
                bufferId             = m_currentBufferId,
                sampleRate           = m_sampleRate,
                samplesPerFrame      = m_samplesPerFrame,
                emitters             = loopedEmitters
            }.ScheduleParallel(m_loopedQuery, ecsCaptureFrameJH);

            //No more ECS
            var oneshotsCullingWeightingJH = new CullingAndWeighting.OneshotsJob
            {
                emitters                = oneshotEmitters,
                listenersWithTransforms = listenersWithTransforms,
                weights                 = oneshotWeightsStream.AsWriter(),
                listenerEmitterPairs    = oneshotListenerEmitterPairsStream.AsWriter()
            }.ScheduleBatch(oneshotEmitters.Length, CullingAndWeighting.BATCH_SIZE, JobHandle.CombineDependencies(captureListenersJH, updateOneshotsJH));

            var loopedCullingWeightingJH = new CullingAndWeighting.LoopedJob
            {
                emitters                = loopedEmitters,
                listenersWithTransforms = listenersWithTransforms,
                weights                 = loopedWeightsStream.AsWriter(),
                listenerEmitterPairs    = loopedListenerEmitterPairsStream.AsWriter()
            }.ScheduleBatch(loopedEmitters.Length, CullingAndWeighting.BATCH_SIZE, JobHandle.CombineDependencies(captureListenersJH, updateLoopedJH));

            var oneshotsBatchingJH = new Batching.BatchOneshotsJob
            {
                emitters              = oneshotEmitters,
                pairWeights           = oneshotWeightsStream.AsReader(),
                listenerEmitterPairs  = oneshotListenerEmitterPairsStream.AsReader(),
                clipFrameLookups      = oneshotClipFrameLookups,
                batchedWeights        = oneshotBatchedWeights,
                targetListenerIndices = oneshotTargetListenerIndices
            }.Schedule(oneshotsCullingWeightingJH);

            var loopedBatchingJH = new Batching.BatchLoopedJob
            {
                emitters              = loopedEmitters,
                pairWeights           = loopedWeightsStream.AsReader(),
                listenerEmitterPairs  = loopedListenerEmitterPairsStream.AsReader(),
                clipFrameLookups      = loopedClipFrameLookups,
                batchedWeights        = loopedBatchedWeights,
                targetListenerIndices = loopedTargetListenerIndices
            }.Schedule(loopedCullingWeightingJH);

            var oneshotSamplingJH = new Sampling.SampleOneshotClipsJob
            {
                clipFrameLookups                    = oneshotClipFrameLookups.AsDeferredJobArray(),
                weights                             = oneshotBatchedWeights.AsDeferredJobArray(),
                targetListenerIndices               = oneshotTargetListenerIndices.AsDeferredJobArray(),
                listenerBufferParameters            = listenerBufferParameters,
                forIndexToListenerAndChannelIndices = forIndexToListenerAndChannelIndices.AsDeferredJobArray(),
                outputSamplesMegaBuffer             = ildBuffer.buffer.AsDeferredJobArray(),
                sampleRate                          = m_sampleRate,
                samplesPerFrame                     = m_samplesPerFrame,
                audioFrame                          = m_audioFrame
            }.Schedule(forIndexToListenerAndChannelIndices, 1, JobHandle.CombineDependencies(updateListenersGraphJH, oneshotsBatchingJH));

            var loopedSamplingJH = new Sampling.SampleLoopedClipsJob
            {
                clipFrameLookups                    = loopedClipFrameLookups.AsDeferredJobArray(),
                weights                             = loopedBatchedWeights.AsDeferredJobArray(),
                targetListenerIndices               = loopedTargetListenerIndices.AsDeferredJobArray(),
                listenerBufferParameters            = listenerBufferParameters,
                forIndexToListenerAndChannelIndices = forIndexToListenerAndChannelIndices.AsDeferredJobArray(),
                outputSamplesMegaBuffer             = ildBuffer.buffer.AsDeferredJobArray(),
                sampleRate                          = m_sampleRate,
                samplesPerFrame                     = m_samplesPerFrame,
                audioFrame                          = m_audioFrame
            }.Schedule(forIndexToListenerAndChannelIndices, 1, JobHandle.CombineDependencies(oneshotSamplingJH, loopedBatchingJH));

            var shipItJH = new GraphHandling.SubmitToDspGraphJob
            {
                commandBlock = dspCommandBlock
            }.Schedule(loopedSamplingJH);

            Dependency = JobHandle.CombineDependencies(updateListenersGraphJH,  //handles captureListener and captureFrame
                                                       updateOneshotsJH,  //handles destroyOneshots
                                                       updateLoopedJH
                                                       );

            var disposeJobHandles = new NativeList<JobHandle>(Allocator.TempJob);
            disposeJobHandles.Add(aliveListenerEntities.Dispose(updateListenersGraphJH));
            disposeJobHandles.Add(deadListenerEntities.Dispose(updateListenersGraphJH));
            disposeJobHandles.Add(listenersWithTransforms.Dispose(JobHandle.CombineDependencies(oneshotsCullingWeightingJH, loopedCullingWeightingJH)));
            disposeJobHandles.Add(listenerBufferParameters.Dispose(loopedSamplingJH));
            disposeJobHandles.Add(forIndexToListenerAndChannelIndices.Dispose(loopedSamplingJH));
            disposeJobHandles.Add(oneshotEmitters.Dispose(oneshotsBatchingJH));
            disposeJobHandles.Add(loopedEmitters.Dispose(loopedBatchingJH));
            disposeJobHandles.Add(oneshotWeightsStream.Dispose(oneshotsBatchingJH));
            disposeJobHandles.Add(loopedWeightsStream.Dispose(loopedBatchingJH));
            disposeJobHandles.Add(oneshotListenerEmitterPairsStream.Dispose(oneshotsBatchingJH));
            disposeJobHandles.Add(loopedListenerEmitterPairsStream.Dispose(loopedBatchingJH));
            disposeJobHandles.Add(oneshotClipFrameLookups.Dispose(oneshotSamplingJH));
            disposeJobHandles.Add(loopedClipFrameLookups.Dispose(loopedSamplingJH));
            disposeJobHandles.Add(oneshotBatchedWeights.Dispose(oneshotSamplingJH));
            disposeJobHandles.Add(loopedBatchedWeights.Dispose(loopedSamplingJH));
            disposeJobHandles.Add(oneshotTargetListenerIndices.Dispose(oneshotSamplingJH));
            disposeJobHandles.Add(loopedTargetListenerIndices.Dispose(loopedSamplingJH));
            disposeJobHandles.Add(shipItJH);

            for (int i = 0; i < m_buffersInFlight.Count; i++)
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

            m_lastUpdateJobHandle = JobHandle.CombineDependencies(disposeJobHandles);
            disposeJobHandles.Dispose();

            m_buffersInFlight.Add(ildBuffer);
        }

        protected override void OnDestroy()
        {
            //UnityEngine.Debug.Log("AudioSystem.OnDestroy");
            var commandBlock = m_graph.CreateCommandBlock();
            commandBlock.Disconnect(m_mixToLimiterMasterConnection);
            commandBlock.Disconnect(m_limiterMasterToOutputConnection);
            commandBlock.ReleaseDSPNode(m_ildNode);
            commandBlock.ReleaseDSPNode(m_mixNode);
            commandBlock.ReleaseDSPNode(m_limiterMasterNode);
            commandBlock.Complete();
            AudioOutputExtensions.DisposeOutputHook(ref m_outputHandle);
            m_driver.Dispose();

            m_lastUpdateJobHandle.Complete();
            m_mixNodePortFreelist.Dispose();
            m_mixNodePortCount.Dispose();
            m_ildNodePortCount.Dispose();
            m_packedFrameCounterBufferId.Dispose();
            m_audioFrame.Dispose();
            m_lastPlayedAudioFrame.Dispose();
            m_lastReadBufferId.Dispose();
            m_audioFrameHistory.Dispose();

            foreach (var buffer in m_buffersInFlight)
            {
                buffer.buffer.Dispose();
                buffer.channels.Dispose();
            }
        }

        private struct ManagedIldBuffer
        {
            public NativeList<float>            buffer;
            public NativeList<IldBufferChannel> channels;
            public int                          bufferId;
        }
    }
}

