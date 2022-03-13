using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Myri.Authoring
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Latios/Audio (Myri)/Audio Source")]
    public class AudioSourceAuthoring : MonoBehaviour
    {
        [Tooltip("An audio clip which will be converted into a DOTS representation and played by this source")]
        public AudioClip clip;
        [Tooltip("Whether or not the source should play the clip in a loop")]
        public bool looping;
        [Tooltip("If enabled, the entity will automatically be destroyed once the clip is finished playing. This option is ignored for looping sources.")]
        public bool autoDestroyOnFinish;
        [Tooltip("The raw volume applied to the audio source, before spatial falloff is applied")]
        public float volume = 1f;
        [Tooltip("When the listener is within this distance to the source, no falloff attenuation is applied")]
        public float innerRange = 5f;
        [Tooltip("When the listener is outside this distance to the source, no audio is heard")]
        public float outerRange = 25f;
        [Tooltip("A distance from the outerRange is which the falloff attenuation is dampened towards 0 where it otherwise wouldn't")]
        public float rangeFadeMargin = 1f;
        [Tooltip(
             "If true, the audio source begins playing from the beginning when it spawns. This option only affects looping sources. Do not use this for large amounts of looped sources.")
        ]
        public bool playFromBeginningAtSpawn;
        [Tooltip("The number of unique voices entities instantiated from this converted Game Object may use. This option only affects looping sources.")]
        public int voices;

        [Header("Cone")]
        [Tooltip("If enabled, directional attenuation is applied")]
        public bool useCone;
        [Tooltip("The inner angle from the entity's forward direction in which no attenuation is applied")]
        public float innerAngle = 30f;
        [Tooltip("The outer angle from the entity's forward direction in which full attenuation is applied")]
        public float outerAngle = 60f;
        [Tooltip("The attenuation to apply at the outer angle. A value of 0 makes the source inaudible outside of the outer angle.")]
        [Range(0f, 1f)]
        public float outerAngleVolume = 0f;
    }
}

