using Unity.Entities;

namespace Latios.Myri
{
    /// <summary>
    /// Configuration data for audio to be added to the worldBlackboardEntity
    /// </summary>
    public struct AudioSettings : IComponentData
    {
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

