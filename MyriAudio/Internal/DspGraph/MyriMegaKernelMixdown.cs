using Unity.Audio;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Myri.DSP
{
    internal unsafe partial struct MyriMegaKernel
    {
        public void Mixdown(ref ExecuteContext<Parameters, SampleProviders> context)
        {
            m_profilingMixdown.Begin();

            var finalFrame        = m_framePool.Acquire(m_frameSize);
            finalFrame.connected  = true;
            finalFrame.frameIndex = m_currentFrame;
            finalFrame.ClearToZero();
            for (int listenerIndex = 0; listenerIndex < m_listenerStackIdToStateMap.length; listenerIndex++)
            {
                ref var listener = ref m_listenerStackIdToStateMap[listenerIndex];
                if (listener.listenerMetadataPtr == null)
                    continue;

                ref var listenerMeta = ref *listener.listenerMetadataPtr;
                if (!listenerMeta.listenerEnabled || listenerMeta.limiterSettings.preGain == 0f)
                {
                    if (listener.limiter.isCreated)
                    {
                        listener.limiter.ResetAttenuation();
                        listener.limiter.ClearLookahead();
                    }
                    continue;
                }

                if (!listener.limiter.isCreated)
                {
                    if (!listener.sampleFrame.connected)
                        continue;

                    listener.limiter = new BrickwallLimiter(listenerMeta.limiterSettings.preGain,
                                                            listenerMeta.limiterSettings.volume,
                                                            listenerMeta.limiterSettings.releasePerSampleDB,
                                                            listenerMeta.limiterSettings.lookaheadSampleCount,
                                                            Allocator.AudioKernel);
                }
                else
                {
                    listener.limiter.preGain            = listenerMeta.limiterSettings.preGain;
                    listener.limiter.volume             = listenerMeta.limiterSettings.volume;
                    listener.limiter.releasePerSampleDB = listenerMeta.limiterSettings.releasePerSampleDB;
                    listener.limiter.SetLookaheadSampleCount(listenerMeta.limiterSettings.lookaheadSampleCount);
                }

                //listener.limiter.ProcessFrame(ref listener.sampleFrame, true);
                for (int i = 0; i < finalFrame.length; i++)
                {
                    var leftIn  = listener.sampleFrame.left[i];
                    var rightIn = listener.sampleFrame.right[i];
                    listener.limiter.ProcessSample(leftIn, rightIn, out var leftOut, out var rightOut);
                    var left   = finalFrame.left;
                    var right  = finalFrame.right;
                    left[i]   += leftOut;
                    right[i]  += rightOut;
                }
            }

            var buffer      = context.Outputs.GetSampleBuffer(0);
            var bufferLeft  = buffer.GetBuffer(0);
            var bufferRight = buffer.GetBuffer(1);
            for (int i = 0; i < finalFrame.length; i++)
            {
                var leftIn  = finalFrame.left[i];
                var rightIn = finalFrame.right[i];
                m_masterLimiter.ProcessSample(leftIn, rightIn, out var leftOut, out var rightOut);
                bufferLeft[i]  = leftOut;
                bufferRight[i] = rightOut;
            }

            m_profilingMixdown.End();
        }
    }
}

