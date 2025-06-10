using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Myri
{
    /// <summary>
    /// A listener which captures audio in 3D space.
    /// </summary>
    public struct AudioListener : IComponentData
    {
        /// <summary>
        /// A volume multiplier for all audio picked up by this listener.
        /// For loud audio scenes, this only affects the relative loudness between other listeners.
        /// This value is not in decibels.
        /// </summary>
        public float volume;
        /// <summary>
        /// A gain value that is applied to the mixed audio signal before the listener limiter is applied.
        /// </summary>
        public float gain;
        /// <summary>
        ///  How quickly the volume should recover after an audio spike.
        /// </summary>
        public float limiterDBRelaxPerSecond;
        /// <summary>
        /// The amount of time in advance that the limiter should examine samples for spikes so
        /// that it can begin ramping down the volume. Larger values result in smoother transitions
        /// but add latency to the final output.
        /// </summary>
        public float limiterLookaheadTime;

        /// <summary>
        /// A multiplier that should be applied to all source spatial ranges.
        /// </summary>
        public half rangeMultiplier;

        internal ushort packed;
        internal int    unused;

        /// <summary>
        /// The resolution of time-based spatialization to apply between the range of 0 and 15.
        /// Higher values are more expensive but may provide a better sense of direction for the listener.
        /// </summary>
        public int itdResolution
        {
            get => Bits.GetBits(packed, 0, 4);
            set => Bits.SetBits(ref packed, 0, 4, (byte)math.clamp(value, 0, 15));
        }
        /// <summary>
        /// The profile which specifies volume and frequency-based filtering spatialization.
        /// </summary>
        public BlobAssetReference<ListenerProfileBlob> ildProfile;
    }

    /// <summary>
    /// A list of audio source channels represented as GUIDs that this listener can hear.
    /// A default entry allows it to hear sources without an AudioSourceChannelGuid.
    /// </summary>
    [InternalBufferCapacity(3)]  // Make this fill a full cache line, since there aren't many listeners to concern ourselves with chunk occupancy.
    public struct AudioListenerChannelID : IBufferElementData
    {
        public AudioSourceChannelID channel;
    }

    /// <summary>
    /// A volume and frequency-based filtering spatialization profile.
    /// A custom variant can be constructed by overriding AudioIldProfileBuilder.
    /// </summary>
    public struct ListenerProfileBlob
    {
        internal BlobArray<FrequencyFilter> filtersLeft;
        internal BlobArray<int>             channelIndicesLeft;
        internal BlobArray<FrequencyFilter> filtersRight;
        internal BlobArray<int>             channelIndicesRight;

        internal BlobArray<float4> anglesPerLeftChannel;
        internal BlobArray<float4> anglesPerRightChannel;
        internal BlobArray<float>  passthroughFractionsPerLeftChannel;
        internal BlobArray<float>  passthroughFractionsPerRightChannel;
        internal BlobArray<float>  filterVolumesPerLeftChannel;
        internal BlobArray<float>  filterVolumesPerRightChannel;
        internal BlobArray<float>  passthroughVolumesPerLeftChannel;
        internal BlobArray<float>  passthroughVolumesPerRightChannel;
    }

    // Todo: Rename this once the new design is ready.
    internal struct ListenerProfileBlobV2
    {
        internal struct ChannelDsp
        {
            public BlobArray<BlobArray<DSP.StateVariableFilter.Coefficients> > filterSequences;
            public BlobArray<float>                                            sequenceVolumes;
        }

        internal BlobArray<ChannelDsp> channelDspsLeft;
        internal BlobArray<ChannelDsp> channelDspsRight;
        internal BlobArray<float4>     anglesPerLeftChannel;
        internal BlobArray<float4>     anglesPerRightChannel;
        internal int                   rightChannelsOffset;
        internal int                   totalFiltersInLeftChannels;
    }

    /// <summary>
    /// A frequency-based filter configuration which can be applied to audio to achieve spatialization.
    /// </summary>
    public struct FrequencyFilter
    {
        /// <summary>
        /// The cutoff frequency for the filter.
        /// </summary>
        public float cutoff;
        /// <summary>
        /// The quality of the filter.
        /// </summary>
        public float q;
        /// <summary>
        /// The amplification or attenuation of the filter in decibels.
        /// </summary>
        public float gainInDecibels;
        /// <summary>
        /// The type of filter.
        /// </summary>
        public FrequencyFilterType type;
    }

    /// <summary>
    /// The type of frequency-based filtering to apply.
    /// </summary>
    public enum FrequencyFilterType
    {
        Lowpass,
        Highpass,
        Bandpass,
        Bell,
        Notch,
        Lowshelf,
        Highshelf
    }
}

