using System.Threading;
using Unity.Audio;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

//Todo: Need to factor in the panFilterRatio
namespace Latios.Myri
{
    [BurstCompile(CompileSynchronously = true)]
    internal unsafe struct ReadIldBuffersNode : IAudioKernel<ReadIldBuffersNode.Parameters, ReadIldBuffersNode.SampleProviders>
    {
        public enum Parameters
        {
            Unused
        }
        public enum SampleProviders
        {
            Unused
        }

        int       m_currentFrame;
        int       m_nextUpdateFrame;
        int       m_lastPlayedBufferID;
        IldBuffer m_ildBuffer;

        internal FixedList4096<IldBuffer> m_queuedIldBuffers;

        [NativeDisableUnsafePtrRestriction]
        internal long* m_packedFrameCounterBufferId;

        public void Initialize()
        {
            //We start on frame 1 so that a buffer ID and frame of both 0 means uninitialized.
            //The audio components use this at the time of writing this comment.
            m_currentFrame       = 1;
            m_nextUpdateFrame    = 0;
            m_lastPlayedBufferID = -1;
            m_ildBuffer          = default;
            m_queuedIldBuffers   = default;
        }

        public void Execute(ref ExecuteContext<Parameters, SampleProviders> context)
        {
            bool bufferStarved = false;

            m_currentFrame++;
            if (m_currentFrame >= m_nextUpdateFrame && !m_queuedIldBuffers.IsEmpty)
            {
                int bestIndex = -1;
                for (int i = 0; i < m_queuedIldBuffers.Length; i++)
                {
                    if (m_queuedIldBuffers[i].frame <= m_currentFrame)
                    {
                        bestIndex = i;
                    }
                }
                for (int i = 0; i < bestIndex; i++)
                {
                    m_queuedIldBuffers.RemoveAt(0);
                }
                if (m_queuedIldBuffers[0].frame <= m_currentFrame)
                {
                    m_ildBuffer          = m_queuedIldBuffers[0];
                    m_lastPlayedBufferID = m_ildBuffer.bufferId;  //we need to report the buffer we just consumed, the audio system knows to keep that one around yet
                    m_nextUpdateFrame    = m_ildBuffer.frame + m_ildBuffer.framesPerUpdate;
                }
            }

            for (int outputChannelIndex = 0; outputChannelIndex < context.Outputs.Count; outputChannelIndex++)
            {
                var channelOutput = context.Outputs.GetSampleBuffer(outputChannelIndex);
                if (channelOutput.Channels <= 0)
                    continue;
                var outputBuffer = channelOutput.GetBuffer(0);
                if (m_ildBuffer.channelCount <= outputChannelIndex)
                {
                    for (int i = 0; i < outputBuffer.Length; i++)
                    {
                        outputBuffer[i] = 0f;
                    }
                }
                else if (m_currentFrame - m_ildBuffer.frame >= m_ildBuffer.framesInBuffer)
                {
                    for (int i = 0; i < outputBuffer.Length; i++)
                    {
                        outputBuffer[i] = 0f;
                    }
                    bufferStarved = true;
                }
                else
                {
                    var ildBufferChannel = m_ildBuffer.bufferChannels[outputChannelIndex];

                    int bufferOffset  = m_currentFrame - m_ildBuffer.frame;
                    bufferOffset     *= outputBuffer.Length;
                    for (int i = 0; i < outputBuffer.Length; i++)
                    {
                        outputBuffer[i] = ildBufferChannel.buffer[bufferOffset + i];
                    }
                }
            }

            if (bufferStarved && m_ildBuffer.warnIfStarved)
            {
                UnityEngine.Debug.LogWarning(
                    $"Dsp buffer starved. Kernel frame: {m_currentFrame}, IldBuffer frame: {m_ildBuffer.frame}, ildBuffer Id: {m_ildBuffer.bufferId}, frames in buffer: {m_ildBuffer.framesInBuffer}, next update frame: {m_nextUpdateFrame}, frames per update: {m_ildBuffer.framesPerUpdate}");
            }

            long     packed   = m_currentFrame + (((long)m_lastPlayedBufferID) << 32);
            ref long location = ref UnsafeUtility.AsRef<long>(m_packedFrameCounterBufferId);
            Interlocked.Exchange(ref location, packed);
        }

        public void Dispose()
        {
        }
    }

    [BurstCompile(CompileSynchronously = true)]
    internal unsafe struct ReadIldBuffersNodeUpdate : IAudioKernelUpdate<ReadIldBuffersNode.Parameters, ReadIldBuffersNode.SampleProviders,
                                                                         ReadIldBuffersNode>
    {
        public IldBuffer ildBuffer;

        public void Update(ref ReadIldBuffersNode audioKernel)
        {
            if (audioKernel.m_queuedIldBuffers.IsEmpty)
            {
                audioKernel.m_queuedIldBuffers.Add(ildBuffer);
                return;
            }
            else
            {
                for (int i = 0; i < audioKernel.m_queuedIldBuffers.Length; i++)
                {
                    if (ildBuffer.frame <= audioKernel.m_queuedIldBuffers[i].frame)
                    {
                        audioKernel.m_queuedIldBuffers.Length = i;
                        break;
                    }
                }
                if (audioKernel.m_queuedIldBuffers.Length != audioKernel.m_queuedIldBuffers.Capacity)
                {
                    audioKernel.m_queuedIldBuffers.Add(ildBuffer);
                }
            }
        }
    }

    [BurstCompile(CompileSynchronously = true)]
    internal unsafe struct SetReadIldBuffersNodePackedFrameBufferId : IAudioKernelUpdate<ReadIldBuffersNode.Parameters, ReadIldBuffersNode.SampleProviders, ReadIldBuffersNode>
    {
        [NativeDisableUnsafePtrRestriction]
        public long* ptr;

        public void Update(ref ReadIldBuffersNode audioKernel)
        {
            audioKernel.m_packedFrameCounterBufferId = ptr;
        }
    }
}

