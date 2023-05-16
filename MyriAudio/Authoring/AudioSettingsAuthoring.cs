using System.Collections;
using Unity.Entities;
using UnityEngine;

namespace Latios.Myri.Authoring
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Latios/Audio (Myri)/Audio Settings")]
    public class AudioSettingsAuthoring : MonoBehaviour
    {
        [Tooltip("The number of additional audio frames to generate in case the main thread stalls")]
        public int safetyAudioFrames = 2;
        [Tooltip("Set this to the max number of audio updates which can happen in a normal visual frame")]
        public int audioFramesPerUpdate = 1;
        [Tooltip("If the beginning of clips are getting chopped off due to large amounts of sources, increase this value by 1")]
        public int lookaheadAudioFrames = 0;
        [Tooltip("If enabled, the audio thread will log when it runs out of samples. It is normal for it to log during initialization.")]
        public bool logWarningIfBuffersAreStarved = false;
    }

    public class AudioSettingsBaker : Baker<AudioSettingsAuthoring>
    {
        public override void Bake(AudioSettingsAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new AudioSettings
            {
                safetyAudioFrames             = authoring.safetyAudioFrames,
                audioFramesPerUpdate          = authoring.audioFramesPerUpdate,
                lookaheadAudioFrames          = authoring.lookaheadAudioFrames,
                logWarningIfBuffersAreStarved = authoring.logWarningIfBuffersAreStarved
            });
        }
    }
}

