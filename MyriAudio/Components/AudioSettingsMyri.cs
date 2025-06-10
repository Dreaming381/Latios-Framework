using Unity.Entities;

namespace Latios.Myri
{
    /// <summary>
    /// Configuration data for audio to be added to the worldBlackboardEntity
    /// </summary>
    public struct AudioSettings : IComponentData
    {
        /// <summary>
        /// The final output volume for everything in Myri, clamped to the range [0, 1].
        /// </summary>
        public float masterVolume;
        /// <summary>
        /// A gain value that is applied to the mixed audio signal before the final limiter is applied.
        /// </summary>
        public float masterGain;
        /// <summary>
        ///  How quickly the volume should recover after an audio spike.
        /// </summary>
        public float masterLimiterDBRelaxPerSecond;
        /// <summary>
        /// The amount of time in advance that the final limiter should examine samples for spikes so
        /// that it can begin ramping down the volume. Larger values result in smoother transitions
        /// but add latency to the final output.
        /// </summary>
        public float masterLimiterLookaheadTime;
        /// <summary>
        /// The number of additional audio frames to generate in case the main thread stalls
        /// </summary>
        public int safetyAudioFrames;
        /// <summary>
        /// The number of audio frames expected per update. Increase this if the audio framerate is higher than the visual framerate.
        /// </summary>
        public int audioFramesPerUpdate;
        /// <summary>
        /// The number of audio frames ahead of the DSP clock that the jobs should start generating data for.
        /// Increase this when the amount of sampling is heavy and the beginning of clips get cut off.
        /// Increasing this value adds a delay to the audio.
        /// </summary>
        public int lookaheadAudioFrames;
        /// <summary>
        /// If enabled, warnings will be logged when the audio thread runs out of samples to process.
        /// </summary>
        public bool logWarningIfBuffersAreStarved;
    }
}

