using System.Collections;
using Unity.Entities;
using UnityEngine;

namespace Latios.Myri.Authoring
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Latios/Audio (Myri)/Audio Settings")]
    public class AudioSettingsAuthoring : MonoBehaviour, IConvertGameObjectToEntity
    {
        public int  audioFramesPerUpdate          = 3;
        public int  audioSubframesPerFrame        = 1;
        public bool logWarningIfBuffersAreStarved = false;

        public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
        {
            dstManager.AddComponentData(entity, new AudioSettings
            {
                audioFramesPerUpdate          = audioFramesPerUpdate,
                audioSubframesPerFrame        = audioSubframesPerFrame,
                logWarningIfBuffersAreStarved = logWarningIfBuffersAreStarved
            });
        }
    }
}

