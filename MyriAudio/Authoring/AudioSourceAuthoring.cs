using System.Collections.Generic;
using Latios.Authoring;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Myri.Authoring
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Latios/Myri/Audio Source (Myri)")]
    public class AudioSourceAuthoring : MonoBehaviour
    {
        [Header("Playback")]
        [Tooltip("An audio clip which will be converted into a DOTS representation and played by this source")]
        public AudioClip clip;
        [Tooltip("The compression codec to use for the clip, on top of subscene serialization compression")]
        public Codec codec;
        [Tooltip("The raw volume applied to the audio source, before spatial falloff is applied")]
        public float volume = 1f;
        [Tooltip("Whether or not the source should play the clip in a loop")]
        public bool looping;
        [Tooltip(
             "If true, the audio source begins playing from the beginning when it spawns. This option only affects looping sources. Do not use this for large amounts of looped sources.")
        ]
        public bool playFromBeginningAtSpawn;
        [Tooltip("The number of unique voices entities instantiated from this converted Game Object may use. This option only affects looping sources.")]
        public int voices;
        [Tooltip("If enabled, the entity will automatically be destroyed once the clip is finished playing. This option is ignored for looping sources.")]
        public bool autoDestroyOnFinish;

        [Header("Channels")]
        [Tooltip("Optionally specify the channel asset that listeners can reference to listen to this source.")]
        public AudioChannelAsset audioChannel;

        [Header("Falloff")]
        public bool useFalloff = true;
        [Tooltip("When the listener is within this distance to the source, no falloff attenuation is applied")]
        public float innerRange = 5f;
        [Tooltip("When the listener is outside this distance to the source, no audio is heard")]
        public float outerRange = 25f;
        [Tooltip("A distance from the outerRange is which the falloff attenuation is dampened towards 0 where it otherwise wouldn't")]
        public float rangeFadeMargin = 1f;

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

    [TemporaryBakingType]
    public struct AudioSourceBakerWorker : ISmartBakeItem<AudioSourceAuthoring>
    {
        SmartBlobberHandle<AudioClipBlob> m_handle;

        public bool Bake(AudioSourceAuthoring authoring, IBaker baker)
        {
            var entity = baker.GetEntity(TransformUsageFlags.Renderable);

            bool useRuntimeProceduralClip = false;
            var  customClip               = baker.GetComponent<IAudioClipOverride>();
            if (customClip != null)
            {
                m_handle = customClip.BakeClip(baker, entity);
                if (!m_handle.IsValid)
                {
                    useRuntimeProceduralClip = true;
                }
            }
            else
                m_handle = baker.RequestCreateBlobAsset(authoring.clip, authoring.codec, authoring.voices);

            if (!useRuntimeProceduralClip)
            {
                baker.AddComponent(entity, new AudioSourceClip
                {
                    looping              = authoring.looping,
                    offsetIsBasedOnSpawn = authoring.playFromBeginningAtSpawn
                });
            }

            baker.AddComponent(entity, new AudioSourceVolume
            {
                volume = authoring.volume,
            });

            if (!authoring.looping && authoring.autoDestroyOnFinish)
            {
                baker.AddComponent<AudioSourceDestroyOneShotWhenFinished>(entity);
            }

            if (authoring.audioChannel != null)
            {
                baker.AddComponent(entity, authoring.audioChannel.GetChannelID(baker));
            }

            if (authoring.useFalloff)
            {
                baker.AddComponent(entity, new AudioSourceDistanceFalloff
                {
                    innerRange      = (half)authoring.innerRange,
                    outerRange      = (half)authoring.outerRange,
                    rangeFadeMargin = (half)authoring.rangeFadeMargin,
                });

                if (authoring.useCone)
                {
                    baker.AddComponent(entity, new AudioSourceEmitterCone
                    {
                        cosInnerAngle         = math.cos(math.radians(authoring.innerAngle)),
                        cosOuterAngle         = math.cos(math.radians(authoring.outerAngle)),
                        outerAngleAttenuation = authoring.outerAngleVolume
                    });
                }
            }

            return m_handle.IsValid;
        }

        public void PostProcessBlobRequests(EntityManager entityManager, Entity entity)
        {
            var source    = entityManager.GetComponentData<AudioSourceClip>(entity);
            source.m_clip = m_handle.Resolve(entityManager);
            entityManager.SetComponentData(entity, source);
        }
    }

    public class AudioSourceBaker : SmartBaker<AudioSourceAuthoring, AudioSourceBakerWorker>
    {
    }
}

