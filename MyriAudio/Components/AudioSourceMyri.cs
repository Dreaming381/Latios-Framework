using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Properties;

namespace Latios.Myri
{
    /// <summary>
    /// A clip for an audio source, which manages the playhead of the AudioClipBlob.
    /// Clips can be looping or played once.
    /// </summary>
    public struct AudioSourceClip : IComponentData
    {
        internal BlobAssetReference<AudioClipBlob> m_clip;
        internal ulong                             m_packed;

        /// <summary>
        /// Gets or sets the audio clip. Setting this will cause the playback state to reset.
        /// </summary>
        [CreateProperty]
        public BlobAssetReference<AudioClipBlob> clip
        {
            get => m_clip;
            set
            {
                m_clip = value;
                ResetPlaybackState();
            }
        }
        /// <summary>
        /// Gets or sets the looping state. Setting this will cause the playback state to reset.
        /// </summary>
        [CreateProperty]
        public bool looping
        {
            get => Bits.GetBit(m_packed, 63);
            set
            {
                Bits.SetBit(ref m_packed, 63, value);
                ResetPlaybackState();
            }
        }
        /// <summary>
        /// Gets or sets whether the looping clip starts playing from the beginning at spawn.
        /// Setting this will cause the playback state to reset if looping is true.
        /// Returns false or does nothing if looping is false.
        /// </summary>
        [CreateProperty]
        public bool offsetIsBasedOnSpawn
        {
            get => looping & Bits.GetBit(m_packed, 61);
            set
            {
                if (!looping)
                    return;
                Bits.SetBit(ref m_packed, 61, value);
                ResetPlaybackState();
            }
        }

        /// <summary>
        /// Resets the playback state as if this entity were just instantiated from a prefab.
        /// </summary>
        public void ResetPlaybackState()
        {
            m_initialized = false;
        }

        internal bool m_initialized
        {
            get => Bits.GetBit(m_packed, 62);
            set => Bits.SetBit(ref m_packed, 62, value);
        }

        // The spawned buffer ID to compare against the last consumed, used for oneshots as well as looping when offsetIsBasedOnSpawn is true
        internal int m_spawnedBufferId
        {
            // 12 days at 1000 buffers per second
            get => (int)Bits.GetBits(m_packed, 0, 31);
            set => Bits.SetBits(ref m_packed, 0, 31, (uint)value);
        }

        // One Shot
        internal int m_spawnedAudioFrame
        {
            // 33 days at 48kHz and 256 samples per frame
            get => (int)Bits.GetBits(m_packed, 31, 31);
            set => Bits.SetBits(ref m_packed, 31, 31, (uint)value);
        }

        // Looping
        internal bool m_offsetLocked
        {
            get => Bits.GetBit(m_packed, 60);
            set => Bits.SetBit(ref m_packed, 60, value);
        }

        // The sample offset the clip should start playing at when DSP time = 0.
        internal uint m_loopOffset
        {
            get => (uint)Bits.GetBits(m_packed, 31, 28);
            set => Bits.SetBits(ref m_packed, 31, 28, value);
        }

        internal void FixLoopingForBatching()
        {
            Bits.SetBits(ref m_packed, 60, 2, 0);
        }

        [CreateProperty(ReadOnly = true)]
        FixedString128Bytes clipName => m_clip.IsCreated ? m_clip.Value.name : default;

        [CreateProperty(ReadOnly = true)]
        double clipDuration => m_clip.IsCreated ? m_clip.Value.sampleCountPerChannel / (double)m_clip.Value.sampleRate : default;

        [CreateProperty(ReadOnly = true)]
        Codec codec => m_clip.IsCreated ? m_clip.Value.codec : default;

        [CreateProperty(ReadOnly = true)]
        float compressionRatio => m_clip.IsCreated ? CodecDispatch.GetCompressionRatio(m_clip.Value.codec, ref m_clip.Value.encodedSamples) : default;

        [CreateProperty(ReadOnly = true)]
        float compressionSignalToNoiseRatio => m_clip.IsCreated ? CodecDispatch.GetSignalToNoiseRatio(m_clip.Value.codec, ref m_clip.Value.encodedSamples) : default;
    }

    /// <summary>
    /// The volume of an audio source
    /// </summary>
    public struct AudioSourceVolume : IComponentData
    {
        /// <summary>
        /// The raw volume multiplier for the source. This is not in decibels.
        /// </summary>
        public float volume;
    }

    /// <summary>
    /// A distance-based falloff for an audio source. When present, spatialization occurs.
    /// </summary>
    public struct AudioSourceDistanceFalloff : IComponentData
    {
        /// <summary>
        /// When the listener is within this distance, no distance-based attenuation is applied to this audio source.
        /// </summary>
        public half innerRange;
        /// <summary>
        /// When the listener is outside of this distance, the audio source is not heard.
        /// </summary>
        public half outerRange;
        /// <summary>
        /// As the listener nears the outerRange, additional damping is applied to help the perceived volume smoothly transition to zero.
        /// This value dictates the width of the region where this damping takes affect.
        /// </summary>
        public half rangeFadeMargin;
    }

    /// <summary>
    /// An attenuation cone for an audio source. When present on an entity with an audio source, directional attenuation is applied.
    /// The cone's center axis is aligned to the audio source's transform's forward direction.
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
    /// An identifier which listeners can use to select only a subset of sources to listen to
    /// </summary>
    public struct AudioSourceChannelID : IComponentData, IEquatable<AudioSourceChannelID>
    {
        [CreateProperty(ReadOnly = true)]
        internal Hash128 guid;

        public bool Equals(AudioSourceChannelID other) => guid.Equals(other.guid);
        public static bool operator ==(AudioSourceChannelID a, AudioSourceChannelID b) => a.guid == b.guid;
        public static bool operator !=(AudioSourceChannelID a, AudioSourceChannelID b) => a.guid != b.guid;
        public override int GetHashCode() => guid.GetHashCode();

        public override bool Equals(object obj) => obj is AudioSourceChannelID a && Equals(a);
    }

    /// <summary>
    /// If present on an entity with an AudioSourceOneshot, the entity will be destroyed when the playhead passes the last sample.
    /// This is conservatively computed based on values from the audio thread, so the entity may not be destroyed until multiple frames later.
    /// </summary>
    public struct AudioSourceDestroyOneShotWhenFinished : IComponentData, IAutoDestroyExpirable { }

    /// <summary>
    /// A modifier component that can scale the sample rate. This is a cheap trick to add variation in pitch and duration of audio clips.
    /// However, it is not designed to be animated, and should be left untouched once the clip starts playing.
    /// Randomizing the value of this component will very likely prevent voice combining.
    /// </summary>
    public struct AudioSourceSampleRateMultiplier : IComponentData
    {
        public float multiplier;
    }

    /// <summary>
    /// An audio clip representation accessible in Burst jobs.
    /// </summary>
    public struct AudioClipBlob
    {
        /// <summary>
        /// The samples as stored in their possibly compressed format
        /// </summary>
        public BlobPtr<byte> encodedSamples;
        /// <summary>
        /// These are offsets for the different voices used in looping audio. The number of offsets is equal to the number of voices.
        /// </summary>
        public BlobArray<int> loopedOffsets;
        /// <summary>
        /// The name of the audio clip asset that created this blob asset.
        /// </summary>
        [CreateProperty]
        public FixedString128Bytes name;
        /// <summary>
        /// The sample rate of the audio clip. A value of 48000 would mean 48000 float samples are required for 1 second of audio.
        /// </summary>
        public int sampleRate;

        /// <summary>
        /// The number of samples in the clip for a single channel. This divided by the sampleRate produces the realtime length
        /// of the clip in seconds.
        /// </summary>
        public int sampleCountPerChannel;

        /// <summary>
        /// If true, the audio clip is a stereo clip. Otherwise it is a mono clip. Surround is not supported.
        /// </summary>
        public bool isStereo;

        /// <summary>
        /// The codec used for the encodedSamples
        /// </summary>
        public Codec codec;
    }
}

