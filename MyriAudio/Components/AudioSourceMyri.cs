using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Myri
{
    /// <summary>
    /// An audio source which plays in a continuous loop synchronized with the global DSP Time.
    /// </summary>
    public struct AudioSourceLooped : IComponentData
    {
        //Internal until we can detect changes. Will likely require a separate component.
        internal BlobAssetReference<AudioClipBlob> m_clip;
        internal int                               m_loopOffset;
        /// <summary>
        /// The raw volume multiplier for the source. This is not in decibels.
        /// </summary>
        public float volume;
        /// <summary>
        /// When the listener is within this distance, no distance-based attenuation is applied to this audio source.
        /// </summary>
        public float innerRange;
        /// <summary>
        /// When the listener is outside of this distance, the audio source is not heard.
        /// </summary>
        public float outerRange;
        /// <summary>
        /// As the listener nears the outerRange, additional damping is applied to help the perceived volume smoothly transition to zero.
        /// This value dictates the width of the region where this damping takes affect.
        /// </summary>
        public float    rangeFadeMargin;
        internal short  m_spawnBufferLow16;
        internal ushort m_flags;

        /// <summary>
        /// The audio clip to play on loop. Setting this value with a new clip resets the playhead.
        /// </summary>
        public BlobAssetReference<AudioClipBlob> clip
        {
            get => m_clip;
            set
            {
                if (m_clip != value)
                {
                    initialized  = false;
                    offsetLocked = false;
                    m_loopOffset = 0;
                    m_clip       = value;
                }
            }
        }

        /// <summary>
        /// Resets the clip's playhead.
        /// </summary>
        public void ResetPlaybackState()
        {
            var c = clip;
            clip  = default;
            clip  = c;
        }

        /// <summary>
        /// If true, the clip plays from the beginning at the time of spawn. Otherwise it plays using a random offset based on the number of voices.
        /// </summary>
        public bool offsetIsBasedOnSpawn
        {
            get => (m_flags & 0x4) != 0;
            set
            {
                ResetPlaybackState();
                m_flags = (ushort)math.select(m_flags & ~0x4, m_flags | 0x4, value);
            }
        }

        internal bool initialized
        {
            get => (m_flags & 0x1) != 0;
            set => m_flags = (ushort)math.select(m_flags & ~0x1, m_flags | 0x1, value);
        }

        internal bool offsetLocked
        {
            get => (m_flags & 0x2) != 0;
            set => m_flags = (ushort)math.select(m_flags & ~0x2, m_flags | 0x2, value);
        }
    }

    /// <summary>
    /// An audio source which plays only once, starting from the beginning when spawned.
    /// </summary>
    public struct AudioSourceOneShot : IComponentData
    {
        internal BlobAssetReference<AudioClipBlob> m_clip;
        internal int                               m_spawnedAudioFrame;
        internal int                               m_spawnedBufferId;

        /// <summary>
        /// The raw volume multiplier for the source. This is not in decibels.
        /// </summary>
        public float volume;
        /// <summary>
        /// When the listener is within this distance, no distance-based attenuation is applied to this audio source.
        /// </summary>
        public float innerRange;
        /// <summary>
        /// When the listener is outside of this distance, the audio source is not heard.
        /// </summary>
        public float outerRange;
        /// <summary>
        /// As the listener nears the outerRange, additional damping is applied to help the perceived volume smoothly transition to zero.
        /// This value dictates the width of the region where this damping takes affect.
        /// </summary>
        public float rangeFadeMargin;

        /// <summary>
        /// The audio clip to play. Setting this value with a new clip resets the playhead back to the beginning.
        /// </summary>
        public BlobAssetReference<AudioClipBlob> clip
        {
            get => m_clip;
            set
            {
                if (m_clip != value)
                {
                    m_spawnedAudioFrame = 0;
                    m_spawnedBufferId   = 0;
                    m_clip              = value;
                }
            }
        }

        /// <summary>
        /// Resets the clips playhead back to the beginning.
        /// </summary>
        public void ResetPlaybackState()
        {
            var c = clip;
            clip  = default;
            clip  = c;
        }

        internal bool isInitialized => (m_spawnedBufferId != 0) | (m_spawnedBufferId != m_spawnedAudioFrame);
    }

    /// <summary>
    /// When present on an entity with an audio source, directional attenuation is applied.
    /// The cone's center ray uses the audio source's transform's forward direction.
    /// </summary>
    public struct AudioSourceEmitterCone : IComponentData
    {
        /// <summary>
        /// The cosine of the inner angle within which no attenuation is applied.
        /// </summary>
        public float cosInnerAngle;
        /// <summary>
        /// The cosine of the outer angle outside of which the full attenuation is applied.
        /// </summary>
        public float cosOuterAngle;
        /// <summary>
        /// The amount of attenuation to apply at or outside the outer angle. The value should be between 0 and 1.
        /// </summary>
        public float outerAngleAttenuation;
    }

    /// <summary>
    /// If present on an entity with an AudioSourceOneshot, the entity will be destroyed when the playhead passes the last sample.
    /// This is conservatively computed based on values from the audio thread, so the entity may not be destroyed until multiple frames later.
    /// </summary>
    public struct AudioSourceDestroyOneShotWhenFinished : IComponentData, IAutoDestroyExpirable { }

    /// <summary>
    /// An audio clip representation accessible in Burst jobs.
    /// </summary>
    public struct AudioClipBlob
    {
        /// <summary>
        /// The samples for either the left channel, or the mono channel if this is not a stereo clip.
        /// </summary>
        public BlobArray<float> samplesLeftOrMono;
        /// <summary>
        /// The samples for the right channel. It is length 0 if this is not a stereo clip.
        /// </summary>
        public BlobArray<float> samplesRight;
        /// <summary>
        /// These are offsets for the different voices used in looping audio. The number of offsets is equal to the number of voices.
        /// </summary>
        public BlobArray<int> loopedOffsets;
        /// <summary>
        /// The name of the audio clip asset that created this blob asset.
        /// </summary>
        public FixedString128Bytes name;
        /// <summary>
        /// The sample rate of the audio clip. A value of 48000 would mean 48000 float samples are required for 1 second of audio.
        /// </summary>
        public int sampleRate;

        /// <summary>
        /// If true, the audio clip is a stereo clip. Otherwise it is a mono clip. Surround is not supported.
        /// </summary>
        public bool isStereo => samplesRight.Length == samplesLeftOrMono.Length;
    }
}

