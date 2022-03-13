using System.Collections;
using Unity.Entities;
using UnityEngine;

namespace Latios.Myri.Authoring
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Latios/Audio (Myri)/Audio Settings")]
    public class AudioSettingsAuthoring : MonoBehaviour, IConvertGameObjectToEntity
    {
        [Tooltip("The number of additional audio frames to generate in case the main thread stalls")]
        public int safetyAudioFrames = 2;
        [Tooltip("Set this to the max number of audio updates which can happen in a normal visual frame")]
        public int audioFramesPerUpdate = 1;
        [Tooltip("If the beginning of clips are getting chopped off due to large amounts of sources, increase this value by 1")]
        public int lookaheadAudioFrames = 0;
        [Tooltip("If enabled, the audio thread will log when it runs out of samples. It is normal for it to log during initialization.")]
        public bool logWarningIfBuffersAreStarved = false;

        public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
        {
            dstManager.AddComponentData(entity, new AudioSettings
            {
                safetyAudioFrames             = safetyAudioFrames,
                audioFramesPerUpdate          = audioFramesPerUpdate,
                lookaheadAudioFrames          = lookaheadAudioFrames,
                logWarningIfBuffersAreStarved = logWarningIfBuffersAreStarved
            });
        }
    }
}

