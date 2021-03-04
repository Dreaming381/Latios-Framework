using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Myri
{
    public struct AudioListener : IComponentData
    {
        public float volume;

        public int                                itdResolution;
        public BlobAssetReference<IldProfileBlob> ildProfile;
    }

    public struct IldProfileBlob
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

    public struct FrequencyFilter
    {
        public float               cutoff;
        public float               q;
        public float               gainInDecibels;
        public FrequencyFilterType type;
    }

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

