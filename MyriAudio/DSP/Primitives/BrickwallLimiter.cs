using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Myri.DSP
{
    // Make public on release
    internal struct BrickwallLimiter : IDisposable
    {
        /// <summary>
        /// At 48000 Hz, this works out to 7.8 DB per second.
        /// This value was the hardcoded release value in previous versions of Myri.
        /// </summary>
        public const float kDefaultReleaseDBPerSample   = 10f / 60f / 1024f;
        public const float kDefaultPreGain              = 1f;
        public const float kDefaultLimitDB              = 0f;
        public const int   kDefaultLookaheadSampleCount = 256;

        SampleQueue                      m_delayQueueL;
        SampleQueue                      m_delayQueueR;
        SampleQueue                      m_delayAmplitudeDB;
        AllocatorManager.AllocatorHandle m_allocator;
        float                            m_preGain;
        float                            m_limitDB;
        float                            m_releasePerSampleDB;
        float                            m_currentAttenuationDB;

        public BrickwallLimiter(float preGain, float limitDB, float releaseDBPerSample, int lookaheadSampleCount, AllocatorManager.AllocatorHandle allocator)
        {
            m_allocator            = allocator;
            m_preGain              = preGain;
            m_limitDB              = limitDB;
            m_releasePerSampleDB   = releaseDBPerSample;
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

        public void ProcessFrame(ref SampleFrame frame, bool outputConnected)
        {
            var left               = frame.left;
            var right              = frame.right;
            int currentSampleIndex = 0;

            while (m_delayQueueL.count < m_delayQueueL.capacity && currentSampleIndex < left.Length)
            {
                m_delayQueueL.Enqueue(math.select(0f, left[currentSampleIndex] * m_preGain, frame.connected));
                m_delayQueueR.Enqueue(math.select(0f, right[currentSampleIndex] * m_preGain, frame.connected));
                float max = math.max(math.abs(left[currentSampleIndex]), math.abs(right[currentSampleIndex])) * m_preGain;
                m_delayAmplitudeDB.Enqueue(SampleUtilities.ConvertToDB(math.select(0f, max, frame.connected)));
                if (outputConnected)
                {
                    left[currentSampleIndex]  = 0f;
                    right[currentSampleIndex] = 0f;
                }
                currentSampleIndex++;
            }

            if (currentSampleIndex >= frame.length)
            {
                frame.connected = false;
                return;
            }

            while (currentSampleIndex < frame.length)
            {
                var leftSample        = m_delayQueueL.Dequeue();
                var rightSample       = m_delayQueueR.Dequeue();
                var amplitudeSampleDB = m_delayAmplitudeDB.Dequeue();

                // Clamp the attenuation to whatever the sample needs
                m_currentAttenuationDB = math.min(m_currentAttenuationDB, m_limitDB - amplitudeSampleDB);

                if (outputConnected)
                {
                    var currentAttenuation    = SampleUtilities.ConvertDBToRawAttenuation(m_currentAttenuationDB);
                    left[currentSampleIndex]  = leftSample * currentAttenuation;
                    right[currentSampleIndex] = rightSample * currentAttenuation;
                }

                // Add back to the queue
                {
                    m_delayQueueL.Enqueue(math.select(0f, left[currentSampleIndex] * m_preGain, frame.connected));
                    m_delayQueueR.Enqueue(math.select(0f, right[currentSampleIndex] * m_preGain, frame.connected));
                    float max = math.max(math.abs(left[currentSampleIndex]), math.abs(right[currentSampleIndex])) * m_preGain;
                    m_delayAmplitudeDB.Enqueue(SampleUtilities.ConvertToDB(math.select(0f, max, frame.connected)));
                }

                // Find the maximally decreasing attenuation slope in the lookahead queue
                float slope = float.MaxValue;
                for (int i = 0; i < m_delayAmplitudeDB.count; i++)
                {
                    float newSlope = (m_limitDB - m_delayAmplitudeDB[i] - m_currentAttenuationDB) / (i + 1);
                    slope          = math.min(slope, newSlope);
                }

                // Update the attenuation for the next sample
                m_currentAttenuationDB += math.select(slope, m_releasePerSampleDB, slope > 0f);
                m_currentAttenuationDB  = math.min(m_currentAttenuationDB, 0f);
            }

            frame.connected = true;
        }

        public float preGain
        {
            get => m_preGain;
            set => m_preGain = value;
        }

        public float limitDB
        {
            get => m_limitDB;
            set => m_limitDB = value;
        }

        public float releasePerSampleDB
        {
            get => m_releasePerSampleDB;
            set { m_releasePerSampleDB = value; }
        }

        public int lookaheadSampleCount => m_delayAmplitudeDB.capacity;

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

        public void ResetAttenuation() => m_currentAttenuationDB = 0f;

        public void ClearLookahead()
        {
            m_delayQueueL.Clear();
            m_delayQueueR.Clear();
            m_delayAmplitudeDB.Clear();
        }
    }
}

