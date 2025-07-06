using System;
using Unity.Collections;
using Unity.Mathematics;

namespace Latios.Myri.DSP
{
    // This is a brickwall limiter with lookahead. The way such filters work is they add a delay to the signal in order to "see ahead".
    // From this, they can begin gradually attenuating the signal prior to the sample that would have otherwise had a magnitude greater
    // than 1.0f. The gradual attenuation prevents clicking noises that would otherwise occur from sudden attenuation spikes.

    // This initial implementation is a naive brute-force implementation which searches through all lookahead samples for the extreme
    // slope for every sample. This costs 1.2 ms on my system. There is definitely room for optimization here.

    // Make public on release
    internal struct BrickwallLimiter : IDisposable
    {
        /// <summary>
        /// At 48000 Hz, this works out to 7.8 DB per second.
        /// This value was the hardcoded release value in previous versions of Myri.
        /// </summary>
        public const float kDefaultReleaseDBPerSample   = 10f / 60f / 1024f;
        public const float kDefaultPreGain              = 1f;
        public const float kDefaultVolume               = 1f;
        public const int   kDefaultLookaheadSampleCount = 256;

        SampleQueue                      m_delayQueueL;
        SampleQueue                      m_delayQueueR;
        SampleQueue                      m_delayAmplitudeDB;
        AllocatorManager.AllocatorHandle m_allocator;
        float                            m_preGain;
        float                            m_preGainDB;
        float                            m_volume;
        float                            m_releasePerSampleDB;
        float                            m_currentAttenuationDB;

        public BrickwallLimiter(float initialPreGain, float initialVolume, float initialReleaseDBPerSample, int lookaheadSampleCount, AllocatorManager.AllocatorHandle allocator)
        {
            m_allocator            = allocator;
            m_preGain              = initialPreGain;
            m_preGainDB            = SampleUtilities.ConvertToDB(initialPreGain);
            m_volume               = initialVolume;
            m_releasePerSampleDB   = initialReleaseDBPerSample;
            m_currentAttenuationDB = 0f;
            m_delayQueueL          = new SampleQueue(lookaheadSampleCount, allocator);
            m_delayQueueR          = new SampleQueue(lookaheadSampleCount, allocator);
            m_delayAmplitudeDB     = new SampleQueue(lookaheadSampleCount, allocator);
        }

        public void Dispose()
        {
            m_delayQueueL.Dispose();
            m_delayQueueR.Dispose();
            m_delayAmplitudeDB.Dispose();
        }

        public bool isCreated => m_delayQueueL.isCreated;

        public float preGain
        {
            get => m_preGain;
            set
            {
                m_preGain   = value;
                m_preGainDB = SampleUtilities.ConvertToDB(m_preGain);
            }
        }

        public float volume
        {
            get => m_volume;
            set => m_volume = value;
        }

        public float releasePerSampleDB
        {
            get => m_releasePerSampleDB;
            set { m_releasePerSampleDB = value; }
        }

        public int lookaheadSampleCount => m_delayAmplitudeDB.capacity;

        public void ProcessSample(float leftIn, float rightIn, out float leftOut, out float rightOut)
        {
            if (m_delayQueueL.count < m_delayQueueL.capacity)
            {
                m_delayQueueL.Enqueue(leftIn);
                m_delayQueueR.Enqueue(rightIn);
                var max = math.max(math.abs(leftIn), math.abs(rightIn));
                m_delayAmplitudeDB.Enqueue(SampleUtilities.ConvertToDB(max));
                leftOut  = 0f;
                rightOut = 0f;
                return;
            }

            var leftSample        = m_delayQueueL.Dequeue();
            var rightSample       = m_delayQueueR.Dequeue();
            var amplitudeSampleDB = m_delayAmplitudeDB.Dequeue();

            // Clamp the attenuation to whatever the sample needs
            m_currentAttenuationDB = math.min(m_currentAttenuationDB, -(amplitudeSampleDB + m_preGainDB));

            var factor = m_preGain * m_volume * SampleUtilities.ConvertDBToRawAttenuation(m_currentAttenuationDB);
            leftOut    = leftSample * factor;
            rightOut   = rightSample * factor;

            // Add back to the queue
            {
                m_delayQueueL.Enqueue(leftIn);
                m_delayQueueR.Enqueue(rightIn);
                var max = math.max(math.abs(leftIn), math.abs(rightIn));
                m_delayAmplitudeDB.Enqueue(SampleUtilities.ConvertToDB(max));
            }

            // Find the maximally decreasing attenuation slope in the lookahead queue
            float slope = float.MaxValue;
            for (int i = 0; i < m_delayAmplitudeDB.count; i++)
            {
                float newSlope = (-(m_preGainDB + m_delayAmplitudeDB[i]) - m_currentAttenuationDB) / (i + 1);
                slope          = math.min(slope, newSlope);
            }

            // Update the attenuation for the next sample
            m_currentAttenuationDB += math.select(slope, m_releasePerSampleDB, slope > 0f);
            m_currentAttenuationDB  = math.min(m_currentAttenuationDB, 0f);
        }

        public void ResetAttenuation() => m_currentAttenuationDB = 0f;

        public void ClearLookahead()
        {
            m_delayQueueL.Clear();
            m_delayQueueR.Clear();
            m_delayAmplitudeDB.Clear();
        }

        public void SetLookaheadSampleCount(int lookaheadSampleCount)
        {
            if (lookaheadSampleCount == m_delayAmplitudeDB.capacity)
                return;

            var oldLeft  = m_delayQueueL;
            var oldRight = m_delayQueueR;
            var oldDb    = m_delayAmplitudeDB;

            m_delayQueueL      = new SampleQueue(lookaheadSampleCount, m_allocator);
            m_delayQueueR      = new SampleQueue(lookaheadSampleCount, m_allocator);
            m_delayAmplitudeDB = new SampleQueue(lookaheadSampleCount, m_allocator);

            int countToTransfer = m_delayQueueL.count;
            for (int i = 0; i < countToTransfer; i++)
            {
                m_delayQueueL.Enqueue(oldLeft.Dequeue());
                m_delayQueueR.Enqueue(oldRight.Dequeue());
                m_delayAmplitudeDB.Enqueue(oldDb.Dequeue());
            }

            oldLeft.Dispose();
            oldRight.Dispose();
            oldDb.Dispose();
        }
    }
}

