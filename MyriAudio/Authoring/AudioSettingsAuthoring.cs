using System.Collections;
using Unity.Entities;
using UnityEngine;

namespace Latios.Myri.Authoring
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Latios/Audio (Myri)/Audio Settings")]
    public class AudioSettingsAuthoring : MonoBehaviour, IConvertGameObjectToEntity
    {
        public int  safetyAudioFrames             = 2;
        public int  audioFramesPerUpdate          = 1;
        public int  lookaheadAudioFrames          = 0;
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

