using Unity.Collections;
using UnityEngine;

namespace Latios.Myri
{
    /// <summary>
    /// Utility class for working with Unity AudioClip
    /// </summary>
    public static class AudioClipUtility
    {
        /// <summary>
        /// Create AudioClip from sample array
        /// </summary>
        public static AudioClip CreateAudioClipFromSamples(float[] samples, int frequency, int channels, string name)
        {
            int sampleCount = samples.Length / channels;
            AudioClip clip = AudioClip.Create(name, sampleCount, channels, frequency, false);
            clip.SetData(samples, 0);
            return clip;
        }
        /// <summary>
        /// Get <see cref="UnityEngine.AudioClip"/> data collection
        /// </summary>
        public static NativeArray<float> GetAudioClipData(UnityEngine.AudioClip clip, Allocator allocator = Allocator.TempJob)
        {
            float[] samples = new float[clip.samples * clip.channels];
            clip.GetData(samples, 0);

            return new NativeArray<float>(samples, allocator);
        }
    }
}
