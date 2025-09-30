using System;
using Unity.Collections;
using Unity.Mathematics;

namespace Latios.Myri.DSP
{
    /// <summary>
    /// A brickwall limiter with lookahead. This filter delays the signal in order to "see ahead". From this, it begins gradually
    /// attenuating the signal prior to a sample that would have otherwise had a magnitude greater than 1.0f. The gradual attenuation
    /// prevents clicking noises and popping that would otherwise occur from sudden volume spikes being clamped or attenuated.
    ///
    /// The current implementation is a naive brute-force implementation which searches through all lookahead samples for the extreme
    /// slope. It does this for every sample. With a 1024 samples per frame and a 256 sample lookahead, this process can cost roughly
    /// 0.5 to 1.5 milliseconds, depending on hardware. There is definitely room for optimization here.
    /// </summary>
    public struct BrickwallLimiter : IDisposable
    {
        /// <summary>
        /// At 48000 Hz, this works out to 7.8 DB per second.
        /// This value was the hardcoded release value in previous versions of Myri.
        /// </summary>
        public const float kDefaultReleaseDBPerSample = 10f / 60f / 1024f;
        /// <summary>
        /// The default pregain, in which the signal is passed as-is
        /// </summary>
        public const float kDefaultPreGain = 1f;
        /// <summary>
        /// The default volume, in which the signal retains its source volume unless
        /// that volume exceeds playback limits
        /// </summary>
        public const float kDefaultVolume = 1f;
        /// <summary>
        /// The default lookahead sample count. This was the default hardcoded value in previous version of Myri.
        /// </summary>
        public const int kDefaultLookaheadSampleCount = 256;

        SampleQueue                      m_delayQueueL;
        SampleQueue                      m_delayQueueR;
        SampleQueue                      m_delayAmplitudeDB;
        AllocatorManager.AllocatorHandle m_allocator;
        float                            m_preGain;
        float                            m_preGainDB;
        float                            m_volume;
        float                            m_releasePerSampleDB;
        float                            m_currentAttenuationDB;

        /// <summary>
        /// Constructs a brickwall limiter instance
        /// </summary>
        /// <param name="initialPreGain">A volume control applied before limiting, as a raw multiplier</param>
        /// <param name="initialVolume">The max output volume, as a raw multiplier</param>
        /// <param name="initialReleaseDBPerSample">The number of decibels to raise the limiter volume per sample when the input signal is quiet</param>
        /// <param name="lookaheadSampleCount">The number of samples to delay the signal by so that the limiter can start smoothing the signal in advance</param>
        /// <param name="allocator">The allocator to use for this limiter's internal allocations, which should remain valid for the lifetime of this limiter</param>
        public BrickwallLimiter(float initialPreGain, float initialVolume, float initialReleaseDBPerSample, int lookaheadSampleCount, AllocatorManager.AllocatorHandle allocator)
        {
            m_allocator            = allocator;
            m_preGain              = initialPreGain;
            m_preGainDB            = DspTools.ConvertToDB(initialPreGain);
            m_volume               = initialVolume;
            m_releasePerSampleDB   = initialReleaseDBPerSample;
            m_currentAttenuationDB = 0f;
            m_delayQueueL          = new SampleQueue(lookaheadSampleCount, allocator);
            m_delayQueueR          = new SampleQueue(lookaheadSampleCount, allocator);
            m_delayAmplitudeDB     = new SampleQueue(lookaheadSampleCount, allocator);
        }

        /// <summary>
        /// Disposes the limiter and frees all internal allocations
        /// </summary>
        public void Dispose()
        {
            m_delayQueueL.Dispose();
            m_delayQueueR.Dispose();
            m_delayAmplitudeDB.Dispose();
        }

        /// <summary>
        /// True if this instance has allocated memory, false otherwise
        /// </summary>
        public bool isCreated => m_delayQueueL.isCreated;

        /// <summary>
        /// A volume control applied before limiting, as a raw multiplier
        /// </summary>
        public float preGain
        {
            get => m_preGain;
            set
            {
                m_preGain   = value;
                m_preGainDB = DspTools.ConvertToDB(m_preGain);
            }
        }

        /// <summary>
        /// The max output volume, as a raw multiplier
        /// </summary>
        public float volume
        {
            get => m_volume;
            set => m_volume = value;
        }

        /// <summary>
        /// The number of decibels to raise the limiter volume per sample when the input signal is quiet
        /// </summary>
        public float releasePerSampleDB
        {
            get => m_releasePerSampleDB;
            set { m_releasePerSampleDB = value; }
        }

        /// <summary>
        /// The number of samples to delay the signal by so that the limiter can start smoothing the signal in advance
        /// </summary>
        public int lookaheadSampleCount => m_delayAmplitudeDB.capacity;

        /// <summary>
        /// Adds a new sample into the limiter delay queue, and gets back the limited sample
        /// </summary>
        /// <param name="leftIn">The left input sample</param>
        /// <param name="rightIn">The right input sample</param>
        /// <param name="leftOut">The filtered left output sample</param>
        /// <param name="rightOut">The filtered right output sample</param>
        public void ProcessSample(float leftIn, float rightIn, out float leftOut, out float rightOut)
        {
            if (m_delayQueueL.count < m_delayQueueL.capacity)
            {
                m_delayQueueL.Enqueue(leftIn);
                m_delayQueueR.Enqueue(rightIn);
                var max = math.max(math.abs(leftIn), math.abs(rightIn));
                m_delayAmplitudeDB.Enqueue(DspTools.ConvertToDB(max));
                leftOut  = 0f;
                rightOut = 0f;
                return;
            }

            var leftSample        = m_delayQueueL.Dequeue();
            var rightSample       = m_delayQueueR.Dequeue();
            var amplitudeSampleDB = m_delayAmplitudeDB.Dequeue();

            // Clamp the attenuation to whatever the sample needs
            m_currentAttenuationDB = math.min(m_currentAttenuationDB, -(amplitudeSampleDB + m_preGainDB));

            var factor = m_preGain * m_volume * DspTools.ConvertDBToRawAttenuation(m_currentAttenuationDB);
            leftOut    = leftSample * factor;
            rightOut   = rightSample * factor;

            // Add back to the queue
            {
                m_delayQueueL.Enqueue(leftIn);
                m_delayQueueR.Enqueue(rightIn);
                var max = math.max(math.abs(leftIn), math.abs(rightIn));
                m_delayAmplitudeDB.Enqueue(DspTools.ConvertToDB(max));
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

        /// <summary>
        /// Resets the attenuation of the limiter, causing it to recover from a loud spike immediately
        /// </summary>
        public void ResetAttenuation() => m_currentAttenuationDB = 0f;

        /// <summary>
        /// Drops all samples from the delay queue
        /// </summary>
        public void ClearLookahead()
        {
            m_delayQueueL.Clear();
            m_delayQueueR.Clear();
            m_delayAmplitudeDB.Clear();
        }

        /// <summary>
        /// Sets the number of samples to delay by. This causes internal reallocations of buffers.
        /// </summary>
        /// <param name="lookaheadSampleCount">The new number of samples to keep in the delay queue</param>
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

