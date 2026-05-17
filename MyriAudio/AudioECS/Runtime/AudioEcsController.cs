using Latios.Unsafe;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine.Audio;

namespace Latios.Myri
{
    [BurstCompile]
    internal unsafe struct AudioEcsController : RootOutputInstance.IControl<AudioEcsRootOutput>
    {
        TlsfAllocator*                       m_tlsf;
        IAudioEcsSystemRunner.VPtr           m_runner;
        AudioEcsAtomicFeedbackIds            m_atomicIds;
        int                                  m_runnerSize;
        int                                  m_runnerAlignment;
        AudioFormat                          m_audioFormat;
        LatiosWorldUnmanaged                 m_latiosWorld;
        UnsafeList<ControlToRealtimeMessage> m_sentPipes;
        int                                  m_maxReadFeedbackId;
        bool                                 m_receivedShutdown;

        public AudioEcsController(IAudioEcsBootstrap bootstrap, LatiosWorldUnmanaged latiosWorld)
        {
            // Evaluate bootstrap
            IAudioEcsBootstrap.Configurator configurator = default;
            bootstrap.OnStart(ref configurator);
            if (!configurator.configured)
            {
                throw new System.InvalidOperationException("IAudioEcsBootstrap.Configurator.Configure() was not called.");
            }

            // Instantiate realtime info.
#if UNITY_EDITOR
            bool warn = true;
#else
            bool warn = false;
#endif
            m_tlsf  = AllocatorManager.Allocate<TlsfAllocator>(Allocator.Persistent);
            *m_tlsf = new TlsfAllocator(Allocator.Persistent, configurator.tlsfPoolSize, warn);
            AllocatorManager.Register(ref *m_tlsf);
            m_tlsf->AllocatePool(configurator.tlsfPoolSize);

            m_runner                      = configurator.runnerPtr;
            m_atomicIds.m_atomicPackedIds = AllocatorManager.Allocate<long>(Allocator.Persistent);
            m_atomicIds.Write(new AudioEcsAtomicFeedbackIds.Ids
            {
                feedbackIdStarted    = -1,
                maxCommandIdConsumed = -1,
            });
            m_runnerSize      = configurator.runnerSizeInBytes;
            m_runnerAlignment = configurator.runnerAlignmentInBytes;

            // Instantiate message resources
            m_audioFormat       = default;
            m_sentPipes         = new UnsafeList<ControlToRealtimeMessage>(32, Allocator.Persistent);
            m_latiosWorld       = latiosWorld;
            m_maxReadFeedbackId = -1;

            var writePipes = new UnsafeList<MegaPipe>(JobsUtility.ThreadIndexCount, Allocator.Persistent);
            writePipes.AddReplicate(default, JobsUtility.ThreadIndexCount);
            m_latiosWorld.worldBlackboardEntity.AddOrSetCollectionComponentAndDisposeOld(new AudioEcsCommandPipe
            {
                m_pipe = new CommandPipeWriter
                {
                    m_allocator      = Allocator.Persistent,
                    m_perThreadPipes = new NativeReference<UnsafeList<MegaPipe> >(writePipes, Allocator.Persistent),
                },
                m_commandBufferId = new NativeReference<int>(0, Allocator.Persistent),
            });
            m_latiosWorld.worldBlackboardEntity.AddOrSetCollectionComponentAndDisposeOld(new AudioEcsFeedbackPipe
            {
                m_pipes       = new NativeList<MegaPipe>(Allocator.Persistent),
                m_feedbackIds = new NativeList<int>(Allocator.Persistent),
            });
            m_latiosWorld.worldBlackboardEntity.AddComponentData(m_atomicIds);

            m_receivedShutdown = false;
        }

        public AudioEcsRootOutput CreateRealtime()
        {
            return new AudioEcsRootOutput(m_tlsf, m_runner, m_atomicIds, m_latiosWorld.worldBlackboardEntity);
        }

        public JobHandle Configure(ControlContext context, ref AudioEcsRootOutput realtime, in AudioFormat format)
        {
            if (format.speakerMode != m_audioFormat.speakerMode || format.bufferFrameCount != m_audioFormat.bufferFrameCount || format.sampleRate != m_audioFormat.sampleRate)
            {
                m_audioFormat = format;
                realtime.Configure(format);
                // Prevent sync on later configurations.
                if (m_latiosWorld.worldBlackboardEntity.HasComponent<AudioEcsFormat>())
                    m_latiosWorld.worldBlackboardEntity.SetComponentData(new AudioEcsFormat { audioFormat = format });
                else
                    m_latiosWorld.worldBlackboardEntity.AddComponentData(new AudioEcsFormat { audioFormat = format });
            }
            return default;
        }

        public void Dispose(ControlContext context, ref AudioEcsRootOutput realtime)
        {
            m_tlsf->GetStats(out var bytesUsed, out var bytesTotal);
            if (bytesUsed > 0)
            {
                UnityEngine.Debug.LogWarning($"Audio ECS allocator has detected a leak of {bytesUsed} bytes out of {bytesTotal} bytes reserved.");
            }
            AllocatorManager.Free(Allocator.Persistent, m_runner.ptr.ptr, m_runnerSize, m_runnerAlignment);
            AllocatorManager.Free(Allocator.Persistent, m_atomicIds.m_atomicPackedIds);
            AllocatorManager.UnmanagedUnregister(ref *m_tlsf);
            m_tlsf->Dispose();
            AllocatorManager.Free(Allocator.Persistent, m_tlsf);
            foreach (var pipe in m_sentPipes)
            {
                foreach (var threadPipe in pipe.commandPipeList)
                {
                    if (threadPipe.isCreated)
                        threadPipe.Dispose();
                }
                pipe.commandPipeList.Dispose();
            }
            m_sentPipes.Dispose();
        }

        public ProcessorInstance.Response OnMessage(ControlContext context, ProcessorInstance.Pipe pipe, ProcessorInstance.Message message)
        {
            if (message.Is<ShutdownControlMessage>())
            {
                m_latiosWorld.GetCollectionComponent<AudioEcsFeedbackPipe>(m_latiosWorld.worldBlackboardEntity, out var jh, false);
                jh.Complete();
                m_latiosWorld.GetCollectionComponent<AudioEcsCommandPipe>(m_latiosWorld.worldBlackboardEntity, out jh, false);
                jh.Complete();
                m_latiosWorld.worldBlackboardEntity.RemoveCollectionComponentAndDispose<AudioEcsFeedbackPipe>();
                m_latiosWorld.worldBlackboardEntity.RemoveCollectionComponentAndDispose<AudioEcsCommandPipe>();
                m_receivedShutdown = true;
                return ProcessorInstance.Response.Handled;
            }
            return ProcessorInstance.Response.Unhandled;
        }

        public void Update(ControlContext context, ProcessorInstance.Pipe pipe)
        {
            // For some dumb reason, we still get an update even after requesting shutdown.
            if (m_receivedShutdown)
                return;

            // Gather old feedback pipes and compute last read feedbackID
            var feedbackPipe = m_latiosWorld.GetCollectionComponent<AudioEcsFeedbackPipe>(m_latiosWorld.worldBlackboardEntity, out var jh, false);
            jh.Complete();
            foreach (var id in feedbackPipe.m_feedbackIds)
                m_maxReadFeedbackId = math.max(m_maxReadFeedbackId, id);

            // Collect new feedback pipes
            feedbackPipe.m_pipes.Clear();
            feedbackPipe.m_feedbackIds.Clear();
            int maxRetiredCommandId = -1;
            foreach (var pipeMessage in pipe.GetAvailableData(context))
            {
                if (pipeMessage.TryGetData(out RealtimeToControlMessage r2cMessage))
                {
                    maxRetiredCommandId = math.max(maxRetiredCommandId, r2cMessage.retiredCommandId);
                    feedbackPipe.m_pipes.Add(r2cMessage.feedbackPipe);
                    feedbackPipe.m_feedbackIds.Add(r2cMessage.feedbackBufferId);
                }
            }

            // Dispose old sent command pipes that have been retired.
            int dst = 0;
            for (int i = 0; i < m_sentPipes.Length; i++)
            {
                var sentPipe = m_sentPipes[i];
                if (sentPipe.commandBufferId <= maxRetiredCommandId)
                {
                    foreach (var threadedPipe in sentPipe.commandPipeList)
                    {
                        if (threadedPipe.isCreated)
                            threadedPipe.Dispose();
                    }
                    sentPipe.commandPipeList.Dispose();
                }
                else
                {
                    m_sentPipes[dst] = sentPipe;
                    dst++;
                }
            }
            m_sentPipes.Length = dst;

            // Collect command pipe
            var commandPipe = m_latiosWorld.GetCollectionComponent<AudioEcsCommandPipe>(m_latiosWorld.worldBlackboardEntity, out jh, false);
            jh.Complete();

            // Send it
            var messageToSend = new ControlToRealtimeMessage
            {
                commandBufferId   = commandPipe.commandId,
                commandPipeList   = commandPipe.m_pipe.m_perThreadPipes.Value,
                retiredFeedbackId = m_maxReadFeedbackId
            };
            pipe.SendData(context, in messageToSend);
            m_sentPipes.Add(messageToSend);

            // Allocate new command pipe
            var writePipes = new UnsafeList<MegaPipe>(JobsUtility.ThreadIndexCount, Allocator.Persistent);
            writePipes.AddReplicate(default, JobsUtility.ThreadIndexCount);
            commandPipe.m_pipe.m_perThreadPipes.Value = writePipes;
            commandPipe.m_commandBufferId.Value++;

            // Release the collection components
            m_latiosWorld.UpdateCollectionComponentMainThreadAccess<AudioEcsFeedbackPipe>(m_latiosWorld.worldBlackboardEntity, false);
            m_latiosWorld.UpdateCollectionComponentMainThreadAccess<AudioEcsCommandPipe>( m_latiosWorld.worldBlackboardEntity, false);
        }
    }
}

