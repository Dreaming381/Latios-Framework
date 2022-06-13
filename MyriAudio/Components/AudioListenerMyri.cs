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
        /// The resolution of time-based spatialization to apply between the range of 0 and 15.
        /// Higher values are more expensive but may provide a better sense of direction for the listener.
        /// </summary>
        public int itdResolution;
        /// <summary>
        /// The profile which specifies volume and frequency-based filtering spatialization.
        /// </summary>
        public BlobAssetReference<ListenerProfileBlob> ildProfile;
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

