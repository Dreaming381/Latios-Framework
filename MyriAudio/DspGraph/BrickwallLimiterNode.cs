using Unity.Audio;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

// This is a brickwall limiter with lookahead. The way such filters work is they add a delay to the signal in order to "see ahead".
// From this, they can begin gradually attenuating the signal prior to the sample that would have otherwise had a magnitude greater
// than 1.0f. The gradual attenuation prevents clicking noises that would otherwise occur from sudden attenuation spikes.

// This initial implementation is a naive brute-force implementation which searches through all lookahead samples for the extreme
// slope for every sample. This costs 1.2 ms on my system. A better solution would be to keep a queue of slope segments. However,
// this implementation uses fixed memory allocated in the node object since the AudioKernal allocator is poorly documented.

namespace Latios.Myri
{
    [BurstCompile(CompileSynchronously = true)]
    public unsafe struct BrickwallLimiterNode : IAudioKernel<BrickwallLimiterNode.Parameters, BrickwallLimiterNode.SampleProviders>
    {
        public enum Parameters
        {
            Unused
        }
        public enum SampleProviders
        {
            Unused
        }

        float      m_currentAttenuationDb;
        float      m_maxVolumeDb;
        float      m_releasePerSampleDb;
        DelayQueue m_delayQueueL;
        DelayQueue m_delayQueueR;
        DelayQueue m_delayAmplitudeDb;

        public void Initialize()
        {
            m_currentAttenuationDb        = 0f;
            m_maxVolumeDb                 = 0f;
            m_releasePerSampleDb          = 10f / 60f / 1024f;  // Dependent on sample rate so should be overwritten
            m_delayQueueL.maxSamples      = 256;
            m_delayQueueR.maxSamples      = 256;
            m_delayAmplitudeDb.maxSamples = 256;
        }

        public void Execute(ref ExecuteContext<Parameters, SampleProviders> context)
        {
            // Assume stereo in, stereo out
            if (context.Outputs.Count <= 0)
                return;
            var outputBuffer = context.Outputs.GetSampleBuffer(0);
            if (outputBuffer.Channels <= 1)
            {
                ZeroSampleBuffer(outputBuffer);
                return;
            }
            if (context.Inputs.Count <= 0)
            {
                ZeroSampleBuffer(outputBuffer);
                return;
            }
            var inputBuffer = context.Inputs.GetSampleBuffer(0);
            if (inputBuffer.Channels <= 1)
            {
                ZeroSampleBuffer(outputBuffer);
                return;
            }

            // Temporary: Update the release rate here until the limiter parameters are exposed.
            m_releasePerSampleDb = 10f / 60f / context.DSPBufferSize;

            // Real start
            var inputL  = inputBuffer.GetBuffer(0);
            var inputR  = inputBuffer.GetBuffer(1);
            var outputL = outputBuffer.GetBuffer(0);
            var outputR = outputBuffer.GetBuffer(1);
            int length  = outputL.Length;

            int src = 0;
            int dst = 0;

            // Fill the queues if they haven't filled up yet and write zeros to the output
            while (dst < length && m_delayQueueL.count < m_delayQueueL.maxSamples)
            {
                m_delayQueueL.Enqueue(inputL[src]);
                m_delayQueueR.Enqueue(inputR[src]);
                float max = math.max(math.abs(inputL[src]), math.abs(inputR[src]));
                m_delayAmplitudeDb.Enqueue(20f * math.log10(max));
                src++;
                outputL[dst] = 0f;
                outputR[dst] = 0f;
                dst++;
            }

            while (dst < length)
            {
                var leftSample        = m_delayQueueL.Dequeue();
                var rightSample       = m_delayQueueR.Dequeue();
                var amplitudeSampleDb = m_delayAmplitudeDb.Dequeue();

                // Clamp the attenuation to whatever the sample needs
                m_currentAttenuationDb = math.min(m_currentAttenuationDb, -amplitudeSampleDb);

                // Attenuate the sample
                var currentAttenuation = math.pow(10f, m_currentAttenuationDb / 20f);
                outputL[dst]           = leftSample * currentAttenuation;
                outputR[dst]           = rightSample * currentAttenuation;
                dst++;

                // Fill the gap in the queue
                m_delayQueueL.Enqueue(inputL[src]);
                m_delayQueueR.Enqueue(inputR[src]);
                float max = math.max(math.abs(inputL[src]), math.abs(inputR[src]));
                m_delayAmplitudeDb.Enqueue(20f * math.log10(max));
                src++;

                // Find the maximally decreasing attenuation slope in the lookahead queue
                float slope = float.MaxValue;
                for (int i = 0; i < m_delayAmplitudeDb.count; i++)
                {
                    float newSlope = (-m_delayAmplitudeDb[i] - m_currentAttenuationDb) / (i + 1);
                    slope          = math.min(slope, newSlope);
                }

                // Update the attenuation for the next sample
                m_currentAttenuationDb += math.select(slope, m_releasePerSampleDb, slope > 0f);
                m_currentAttenuationDb  = math.min(m_currentAttenuationDb, m_maxVolumeDb);
            }
        }

        void ZeroSampleBuffer(SampleBuffer sb)
        {
            for (int c = 0; c < sb.Channels; c++)
            {
                var b = sb.GetBuffer(c);
                for (int i = 0; i < b.Length; i++)
                {
                    b[i] = 0f;
                }
            }
        }

        public void Dispose()
        {
        }

        unsafe struct DelayQueue
        {
            fixed float m_buffer[2048];
            int         m_nextEnqueueIndex;
            int         m_nextDequeueIndex;
            int         m_count;
            int         m_maxSamples;

            public int count => m_count;

            public int maxSamples
            {
                get => m_maxSamples;
                set
                {
                    //dequeue all into new buffer with new max and then copy buffer
                    if (value != m_maxSamples)
                    {
                        DelayQueue other   = default;
                        other.m_maxSamples = value;
                        while (m_count > value)
                        {
                            Dequeue();
                        }
                        while (m_count > 0)
                        {
                            other.Enqueue(Dequeue());
                        }
                        this = other;
                    }
                }
            }

            public void Enqueue(float newValue)
            {
                m_buffer[m_nextEnqueueIndex] = newValue;
                m_nextEnqueueIndex++;
                m_nextEnqueueIndex = math.select(m_nextEnqueueIndex, 0, m_nextEnqueueIndex >= m_maxSamples);
                m_count++;
            }

            public float Dequeue()
            {
                float result = m_buffer[m_nextDequeueIndex];
                m_nextDequeueIndex++;
                m_nextDequeueIndex = math.select(m_nextDequeueIndex, 0, m_nextDequeueIndex >= m_maxSamples);
                m_count--;
                return result;
            }

            public float this[int index]
            {
                get
                {
                    int targetIndex = index + m_nextDequeueIndex;
                    targetIndex     = math.select(targetIndex, targetIndex - m_maxSamples, targetIndex >= m_maxSamples);
                    return m_buffer[targetIndex];
                }
            }
        }
    }
}

