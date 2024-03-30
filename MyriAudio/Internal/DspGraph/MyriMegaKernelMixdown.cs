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
                                                            listenerMeta.limiterSettings.limitDB,
                                                            listenerMeta.limiterSettings.releaseDBPerSample,
                                                            listenerMeta.limiterSettings.lookaheadSampleCount,
                                                            Allocator.AudioKernel);
                }
                else
                {
                    listener.limiter.preGain            = listenerMeta.limiterSettings.preGain;
                    listener.limiter.limitDB            = listenerMeta.limiterSettings.limitDB;
                    listener.limiter.releasePerSampleDB = listenerMeta.limiterSettings.releaseDBPerSample;
                    listener.limiter.SetLookaheadSampleCount(listenerMeta.limiterSettings.lookaheadSampleCount);
                }

                listener.limiter.ProcessFrame(ref listener.sampleFrame, true);
                for (int i = 0; i < finalFrame.length; i++)
                {
                    var left   = finalFrame.left;
                    var right  = finalFrame.right;
                    left[i]   += listener.sampleFrame.left[i];
                    right[i]  += listener.sampleFrame.right[i];
                }
            }

            m_masterLimiter.ProcessFrame(ref finalFrame, true);
            var buffer = context.Outputs.GetSampleBuffer(0);
            buffer.GetBuffer(0).CopyFrom(finalFrame.left);
            buffer.GetBuffer(1).CopyFrom(finalFrame.right);

            m_profilingMixdown.End();
        }
    }
}

