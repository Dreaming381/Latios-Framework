using Latios.AuxEcs;
using Latios.Unsafe;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Audio;

namespace Latios.Myri
{
    [BurstCompile(CompileSynchronously = true)]
    internal unsafe struct AudioEcsRootOutput : RootOutputInstance.IRealtime
    {
        TlsfAllocator*             m_tlsf;
        TlsfSecretary              m_tlsfSecretary;
        AuxWorld                   m_auxWorld;
        IAudioEcsSystemRunner.VPtr m_runner;
        bool                       m_initialized;
        bool                       m_needsRunnerInitialization;

        bool              m_hasAudioFormatUpdate;
        AudioFormat       m_audioFormat;
        UnsafeList<float> m_outputBuffer;
        BitField32        m_outputChannelsUsed;

        UnsafeList<VisualFrameUpdate> m_visualFrameUpdatesCache;
        FeedbackPipeManager           m_feedbackPipeManager;
        AudioEcsAtomicFeedbackIds     m_atomicIds;
        int                           m_maxReadCommandId;  // Read in Update(), used in Process().

        public AudioEcsRootOutput(TlsfAllocator* tlsfAllocator, IAudioEcsSystemRunner.VPtr runner, AudioEcsAtomicFeedbackIds atomicIds)
        {
            this        = default;
            m_tlsf      = tlsfAllocator;
            m_runner    = runner;
            m_atomicIds = atomicIds;
        }

        public void Configure(AudioFormat audioFormat)
        {
            m_audioFormat          = audioFormat;
            m_hasAudioFormatUpdate = true;
        }

        // Note: Update runs before EarlyProcessing, and is the only method that can actually read from Pipe.
        public void Update(ProcessorInstance.UpdatedDataContext context, ProcessorInstance.Pipe pipe)
        {
            if (!m_initialized && m_hasAudioFormatUpdate)
            {
                m_auxWorld                = new AuxWorld(m_tlsf->Handle);
                m_outputBuffer            = new UnsafeList<float>(m_audioFormat.channelCount * m_audioFormat.bufferFrameCount, m_tlsf->Handle);
                m_visualFrameUpdatesCache = new UnsafeList<VisualFrameUpdate>(128, m_tlsf->Handle);
                m_tlsfSecretary.Init(ref *m_tlsf);
                m_feedbackPipeManager.Init(m_tlsf->Handle);
                m_initialized               = true;
                m_needsRunnerInitialization = true;
                m_maxReadCommandId          = -1;
            }
            if (m_initialized)
            {
                // Read messages from visual (one message per visual frame)
                int maxRetiredFeedbackId = -1;
                m_visualFrameUpdatesCache.Clear();
                foreach (var wrapped in pipe.GetAvailableData(context))
                {
                    if (wrapped.TryGetData(out ControlToRealtimeMessage message))
                    {
                        m_maxReadCommandId   = math.max(m_maxReadCommandId, message.commandBufferId);
                        maxRetiredFeedbackId = math.max(maxRetiredFeedbackId, message.retiredFeedbackId);
                        m_visualFrameUpdatesCache.Add(new VisualFrameUpdate
                        {
                            commandId  = message.commandBufferId,
                            pipeReader = new CommandPipeReader
                            {
                                m_perThreadPipes = message.commandPipeList
                            }
                        });
                    }
                }
                // Update atomics for faster feedback
                m_atomicIds.Write(new AudioEcsAtomicFeedbackIds.Ids
                {
                    feedbackIdStarted    = m_feedbackPipeManager.feedbackID,
                    maxCommandIdConsumed = m_maxReadCommandId
                });

                // Retire feedback pipes now to free up memory before heavy processing
                m_feedbackPipeManager.OnRetiredFeedbackPipe(maxRetiredFeedbackId);
            }
        }

        public JobHandle EarlyProcessing(in RealtimeContext context, ProcessorInstance.Pipe pipe)
        {
            return default;
        }

        public void Process(in RealtimeContext context, ProcessorInstance.Pipe pipe, JobHandle inputJh)
        {
            if (!m_initialized)
                return;

            // Handle delayed initialization, since Update() doesn't provide the RealtimeContext
            if (m_needsRunnerInitialization)
            {
                var initContext = new IAudioEcsSystemRunner.AudioFormatChangedContext
                {
                    auxWorld                  = m_auxWorld,
                    newAudioFormat            = m_audioFormat,
                    unityAudioRealtimeContext = context,
                    pipeWriter                = new FeedbackPipeWriter { m_megaPipe = m_feedbackPipeManager.activePipe },
                    feedbackID                = m_feedbackPipeManager.feedbackID
                };
                m_runner.OnInitialize(ref initContext);
                m_hasAudioFormatUpdate      = false;
                m_needsRunnerInitialization = false;
            }

            // Schedule the begin-frame TLSf update after initialization since there won't be anything to do before that point
            m_tlsfSecretary.Update(ref *m_tlsf);

            // Handle reconfiguration (rare)
            if (m_hasAudioFormatUpdate)
            {
                m_outputBuffer.Resize(m_audioFormat.channelCount * m_audioFormat.bufferFrameCount);
                var changeContext = new IAudioEcsSystemRunner.AudioFormatChangedContext
                {
                    auxWorld                  = m_auxWorld,
                    newAudioFormat            = m_audioFormat,
                    unityAudioRealtimeContext = context,
                    pipeWriter                = new FeedbackPipeWriter { m_megaPipe = m_feedbackPipeManager.activePipe },
                    feedbackID                = m_feedbackPipeManager.feedbackID
                };
                m_runner.OnAudioFormatChanged(ref changeContext);
                m_hasAudioFormatUpdate = false;
            }

            // Execute the runner for this mix cycle
            var updateContext = new IAudioEcsSystemRunner.UpdateContext
            {
                allRootOutputsEarlyProcessingJobHandle = inputJh,
                auxWorld                               = m_auxWorld,
                feedbackID                             = m_feedbackPipeManager.feedbackID,
                finalOutputBuffer                      = new FinalOutputBuffer
                {
                    m_buffer             = m_outputBuffer,
                    m_channelCount       = m_audioFormat.channelCount,
                    m_sampleRate         = m_audioFormat.sampleRate,
                    m_samplesPerChannel  = m_audioFormat.bufferFrameCount,
                    m_channelInitialized = default
                },
                pipeWriter = new FeedbackPipeWriter
                {
                    m_megaPipe = m_feedbackPipeManager.activePipe,
                },
                unityAudioRealtimeContext = context,
                visualFrameUpdates        = new AllVisualFrameUpdates
                {
                    updates = m_visualFrameUpdatesCache,
                }
            };
            m_runner.OnUpdate(ref updateContext);
            m_outputChannelsUsed = updateContext.finalOutputBuffer.m_channelInitialized;

            // Send feedback to simulation
            pipe.SendData(context, new RealtimeToControlMessage
            {
                feedbackBufferId = m_feedbackPipeManager.feedbackID,
                retiredCommandId = m_maxReadCommandId,
                feedbackPipe     = *m_feedbackPipeManager.activePipe
            });
            m_feedbackPipeManager.OnSentActivePipe();

            // Perform the end-frame TLSF update
            m_tlsfSecretary.Update(ref *m_tlsf);
        }

        public void EndProcessing(in RealtimeContext context, ProcessorInstance.Pipe pipe, ChannelBuffer output)
        {
            if (m_outputChannelsUsed.Value == 0)
            {
                output.Clear();
                return;
            }

            int i = 0;
            for (int c = 0; c < output.channelCount; c++)
            {
                if (!m_outputChannelsUsed.IsSet(c))
                {
                    for (int s = 0; s < output.frameCount; s++)
                    {
                        output[c, s] = 0f;
                        i++;
                    }
                }
                else
                {
                    for (int s = 0; s < output.frameCount; s++)
                    {
                        output[c, s] = m_outputBuffer[i];
                        i++;
                    }
                }
            }
        }

        public void RemovedFromProcessing()
        {
            if (!m_initialized)
                return;

            var shutdownContext = new IAudioEcsSystemRunner.ShutdownContext
            {
                auxWorld = m_auxWorld,
            };
            m_runner.OnShutdown(ref shutdownContext);

            m_outputBuffer.Dispose();
            m_visualFrameUpdatesCache.Dispose();
            m_tlsfSecretary.Shutdown(ref *m_tlsf);
            m_feedbackPipeManager.Shutdown();
        }

        struct TlsfSecretary
        {
            bool                              m_hasPendingAllocationJob;
            JobHandle                         m_jobHandle;
            NativeReference<AllocationResult> m_allocationResult;
            public void Init(ref TlsfAllocator allocator)
            {
                m_allocationResult        = new NativeReference<AllocationResult>(allocator.Handle);
                m_hasPendingAllocationJob = false;
                m_jobHandle               = default;
            }

            public void Update(ref TlsfAllocator allocator)
            {
                if (m_hasPendingAllocationJob)
                {
                    if (!m_jobHandle.IsCompleted)
                        return;

                    m_jobHandle.Complete();
                    var result = m_allocationResult.Value;
                    allocator.GetStats(out _, out _, out _, out var usedAllocator);
                    allocator.AddMemoryToPool(result.data, result.elementSize, result.numElements, usedAllocator);
                    m_hasPendingAllocationJob = false;
                }

                allocator.GetStats(out var bytesUsed, out var bytesTotal, out var standardPoolSize, out var backingAllocator);
                var bytesRemaining = bytesTotal - bytesUsed;
                if ((long)bytesRemaining < standardPoolSize / 2)
                {
                    m_jobHandle = new AllocateJob
                    {
                        allocator       = backingAllocator,
                        bytesToAllocate = standardPoolSize,
                    }.Schedule();
                    m_hasPendingAllocationJob = true;
                }
            }

            public void Shutdown(ref TlsfAllocator allocator)
            {
                if (m_hasPendingAllocationJob)
                {
                    m_jobHandle.Complete();
                    var result = m_allocationResult.Value;
                    allocator.GetStats(out _, out _, out _, out var usedAllocator);
                    allocator.AddMemoryToPool(result.data, result.elementSize, result.numElements, usedAllocator);
                    m_hasPendingAllocationJob = false;
                }
                m_allocationResult.Dispose();
            }

            struct AllocationResult
            {
                public byte* data;
                public int   elementSize;
                public int   numElements;
            }

            [BurstCompile]
            struct AllocateJob : IJob
            {
                public AllocatorManager.AllocatorHandle allocator;
                public long                             bytesToAllocate;
                NativeReference<AllocationResult>       result;

                public void Execute()
                {
                    TlsfAllocator.GetRequiredAllocationParameters(bytesToAllocate, out var elementSize, out var alignment, out var numElements);
                    result.Value = new AllocationResult
                    {
                        data        = (byte*)AllocatorManager.Allocate(allocator, elementSize, alignment, numElements),
                        elementSize = elementSize,
                        numElements = numElements
                    };
                }
            }
        }

        struct FeedbackPipeManager
        {
            public MegaPipe*                 activePipe;
            public int                       feedbackID;
            UnsafeList<SentPipe>             sentPipes;
            AllocatorManager.AllocatorHandle allocator;

            struct SentPipe
            {
                public MegaPipe pipe;
                public int      feedbackID;
            }

            public void Init(AllocatorManager.AllocatorHandle allocator)
            {
                this.allocator = allocator;
                activePipe     = AllocatorManager.Allocate<MegaPipe>(allocator);
                *activePipe    = new MegaPipe(allocator);
                sentPipes      = new UnsafeList<SentPipe>(32, allocator);
                feedbackID     = 0;
            }

            public void OnSentActivePipe()
            {
                sentPipes.Add(new SentPipe { pipe = *activePipe, feedbackID = feedbackID });
                *activePipe                                                 = new MegaPipe(allocator);
                feedbackID++;
            }

            public void OnRetiredFeedbackPipe(int retiredId)
            {
                int dst = 0;
                for (int i = 0; i < sentPipes.Length; i++)
                {
                    var sentPipe = sentPipes[i];
                    if (feedbackID <= retiredId)
                    {
                        sentPipe.pipe.Dispose();
                    }
                    else
                    {
                        sentPipes[dst] = sentPipe;
                        dst++;
                    }
                }
                sentPipes.Length = dst;
            }

            public void Shutdown()
            {
                activePipe->Dispose();
                AllocatorManager.Free(allocator, activePipe);
                foreach (var sentPipe in sentPipes)
                    sentPipe.pipe.Dispose();
                sentPipes.Dispose();
            }
        }
    }
}

