using System.Collections;
using Latios.Myri.DSP;
using Unity.Entities;
using UnityEngine;

namespace Latios.Myri.Authoring
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Latios/Myri/Audio Settings (Myri)")]
    public class AudioSettingsAuthoring : MonoBehaviour
    {
        [Header("Master Output")]
        [Tooltip("The final output volume for everything in Myri, clamped to the range [0, 1]")]
        public float volume = 1f;
        [Tooltip("A gain value that is applied to the mixed audio signal before the final limiter is applied")]
        public float gain = 1f;
        [Tooltip("How quickly the volume should recover after an audio spike, in decibels per second")]
        [InspectorName("Limiter dB Relax Rate")]
        public float limiterDBRelaxPerSecond = BrickwallLimiter.kDefaultReleaseDBPerSample * 48000f;
        [Tooltip(
             "The amount of time in advance in seconds that the final limiter should examine samples for spikes so that it can begin ramping down the volume. Larger values result in smoother transitions but add latency to the final output.")
        ]
        public float limiterLookaheadTime = 255.9f / 48000f;

        [Header("Buffering")]
        [Tooltip("The number of additional audio frames to generate in case the main thread stalls")]
        public int safetyAudioFrames = 2;
        [Tooltip("Set this to the max number of audio updates which can happen in a normal visual frame")]
        public int audioFramesPerUpdate = 1;
        [Tooltip("If the beginning of clips are getting chopped off due to large amounts of sources, increase this value by a multiple of Audio Frames Per Update")]
        public int lookaheadAudioFrames = 1;
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
                masterVolume                  = authoring.volume,
                masterGain                    = authoring.gain,
                masterLimiterDBRelaxPerSecond = authoring.limiterDBRelaxPerSecond,
                masterLimiterLookaheadTime    = authoring.limiterLookaheadTime,
                safetyAudioFrames             = authoring.safetyAudioFrames,
                audioFramesPerUpdate          = authoring.audioFramesPerUpdate,
                lookaheadAudioFrames          = authoring.lookaheadAudioFrames,
                logWarningIfBuffersAreStarved = authoring.logWarningIfBuffersAreStarved
            });
        }
    }
}

