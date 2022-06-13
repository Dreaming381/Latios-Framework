using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

using Hash128 = Unity.Entities.Hash128;

namespace Latios.Myri.Authoring
{
    /// <summary>
    /// An asset type which is used to define a volume and frequency spatialization profile for an audio listener
    /// </summary>
    public abstract class ListenerProfileBuilder : ScriptableObject
    {
        /// <summary>
        /// Override this function to make several calls to AddChannel and AddFilterToChannel which defines a profile.
        /// </summary>
        protected abstract void BuildProfile();

        /// <summary>
        /// Adds a channel to the profile. A channel is a 3D radial slice where all audio sources coming from that direction
        /// are subject to the channels filters. Sources between channels will interpolate between the channels.
        /// </summary>
        /// <param name="minMaxHorizontalAngleInRadiansCounterClockwiseFromRight">
        /// The horizontal extremes of the slice in radians beginning from the left ear positively rotating towards forward.
        /// Values between -2pi and +2pi are allowed.</param>
        /// <param name="minMaxVerticalAngleInRadians">
        /// The vertical extremes of the slice in radians beginning from horizontal positively rotating upwards.
        /// Values between -2pi and +2pi are allowed.</param>
        /// <param name="passthroughFraction">The amount of signal which should bypass the filters. Must be between 0 and 1.</param>
        /// <param name="filterVolume">The raw attenuation or amplification to apply to the input of filtered signal. Not in decibels.</param>
        /// <param name="passthroughVolume">The raw attenuation of amplification to apply to the signal bypassing the filters. Not in decibels.</param>
        /// <param name="isRightEar">If true, this channel uses the right ear. Otherwise, it uses the left ear.</param>
        /// <returns>A handle which can be used to add filters to the channel</returns>
        protected ChannelHandle AddChannel(float2 minMaxHorizontalAngleInRadiansCounterClockwiseFromRight,
                                           float2 minMaxVerticalAngleInRadians,
                                           float passthroughFraction,
                                           float filterVolume,
                                           float passthroughVolume,
                                           bool isRightEar)
        {
            CheckAllowedToGenerate("AddChannel()");

            if (m_job.anglesPerLeftChannel.Length + m_job.anglesPerRightChannel.Length >= 127)
                throw new InvalidOperationException("An IldProfile only supports up to 127 channels");

            if (isRightEar)
            {
                m_job.anglesPerRightChannel.Add(new float4(minMaxHorizontalAngleInRadiansCounterClockwiseFromRight, minMaxVerticalAngleInRadians));
                m_job.passthroughFractionsPerRightChannel.Add(math.saturate(passthroughFraction));
                m_job.filterVolumesPerRightChannel.Add(math.saturate(filterVolume));
                m_job.passthroughVolumesPerRightChannel.Add(math.saturate(passthroughVolume));
                return new ChannelHandle { channelIndex = m_job.anglesPerRightChannel.Length - 1, isRightChannel = true };
            }
            else
            {
                m_job.anglesPerLeftChannel.Add(new float4(minMaxHorizontalAngleInRadiansCounterClockwiseFromRight, minMaxVerticalAngleInRadians));
                m_job.passthroughFractionsPerLeftChannel.Add(passthroughFraction);
                m_job.filterVolumesPerLeftChannel.Add(math.saturate(filterVolume));
                m_job.passthroughVolumesPerLeftChannel.Add(math.saturate(passthroughVolume));
                return new ChannelHandle { channelIndex = m_job.anglesPerLeftChannel.Length - 1, isRightChannel = false };
            }
        }

        /// <summary>
        /// Adds a filter to the channel. Filters are applied in the order they are added.
        /// </summary>
        /// <param name="filter">The filter to apply</param>
        /// <param name="channel">The channel handle returned from AddChannel</param>
        protected void AddFilterToChannel(FrequencyFilter filter, ChannelHandle channel)
        {
            CheckAllowedToGenerate("AddFilterToChannel()");

            if (channel.isRightChannel)
            {
                m_job.filtersRight.Add(filter);
                m_job.channelIndicesRight.Add(channel.channelIndex);
            }
            else
            {
                m_job.filtersLeft.Add(filter);
                m_job.channelIndicesLeft.Add(channel.channelIndex);
            }
        }

        /// <summary>
        /// A handle representing a channel added to the profile
        /// </summary>
        public struct ChannelHandle
        {
            internal int  channelIndex;
            internal bool isRightChannel;
        }

        #region Internals

        FinalizeBlobJob m_job           = default;
        bool            m_allowGenerate = false;

        void CheckAllowedToGenerate(string function)
        {
            if (!m_allowGenerate)
                throw new InvalidOperationException($"The context is not valid. Please only call {function} from within BuildProfile() and do not call BuildProfile() yourself.");
        }

        internal BlobAssetReference<ListenerProfileBlob> ComputeBlob()
        {
            m_job = new FinalizeBlobJob
            {
                filtersLeft                         = new NativeList<FrequencyFilter>(Allocator.TempJob),
                filtersRight                        = new NativeList<FrequencyFilter>(Allocator.TempJob),
                channelIndicesLeft                  = new NativeList<int>(Allocator.TempJob),
                channelIndicesRight                 = new NativeList<int>(Allocator.TempJob),
                anglesPerLeftChannel                = new NativeList<float4>(Allocator.TempJob),
                anglesPerRightChannel               = new NativeList<float4>(Allocator.TempJob),
                passthroughFractionsPerLeftChannel  = new NativeList<float>(Allocator.TempJob),
                passthroughFractionsPerRightChannel = new NativeList<float>(Allocator.TempJob),
                filterVolumesPerLeftChannel         = new NativeList<float>(Allocator.TempJob),
                filterVolumesPerRightChannel        = new NativeList<float>(Allocator.TempJob),
                passthroughVolumesPerLeftChannel    = new NativeList<float>(Allocator.TempJob),
                passthroughVolumesPerRightChannel   = new NativeList<float>(Allocator.TempJob),
                blob                                = new NativeReference<BlobAssetReference<ListenerProfileBlob> >(Allocator.TempJob)
            };

            m_allowGenerate = true;
            BuildProfile();
            m_allowGenerate = false;

            m_job.Run();
            var blob = m_job.blob.Value;

            m_job.filtersLeft.Dispose();
            m_job.filtersRight.Dispose();
            m_job.channelIndicesLeft.Dispose();
            m_job.channelIndicesRight.Dispose();
            m_job.anglesPerLeftChannel.Dispose();
            m_job.anglesPerRightChannel.Dispose();
            m_job.passthroughFractionsPerLeftChannel.Dispose();
            m_job.passthroughFractionsPerRightChannel.Dispose();
            m_job.filterVolumesPerLeftChannel.Dispose();
            m_job.filterVolumesPerRightChannel.Dispose();
            m_job.passthroughVolumesPerLeftChannel.Dispose();
            m_job.passthroughVolumesPerRightChannel.Dispose();
            m_job.blob.Dispose();

            return blob;
        }

        [BurstCompile]
        struct FinalizeBlobJob : IJob
        {
            public NativeList<FrequencyFilter> filtersLeft;
            public NativeList<int>             channelIndicesLeft;
            public NativeList<FrequencyFilter> filtersRight;
            public NativeList<int>             channelIndicesRight;

            public NativeList<float4> anglesPerLeftChannel;
            public NativeList<float4> anglesPerRightChannel;
            public NativeList<float>  passthroughFractionsPerLeftChannel;
            public NativeList<float>  passthroughFractionsPerRightChannel;
            public NativeList<float>  filterVolumesPerLeftChannel;
            public NativeList<float>  filterVolumesPerRightChannel;
            public NativeList<float>  passthroughVolumesPerLeftChannel;
            public NativeList<float>  passthroughVolumesPerRightChannel;

            public NativeReference<BlobAssetReference<ListenerProfileBlob> > blob;

            public void Execute()
            {
                var     builder = new BlobBuilder(Allocator.Temp);
                ref var root    = ref builder.ConstructRoot<ListenerProfileBlob>();
                builder.ConstructFromNativeArray(ref root.filtersLeft,                         filtersLeft);
                builder.ConstructFromNativeArray(ref root.channelIndicesLeft,                  channelIndicesLeft);
                builder.ConstructFromNativeArray(ref root.filtersRight,                        filtersRight);
                builder.ConstructFromNativeArray(ref root.channelIndicesRight,                 channelIndicesRight);

                builder.ConstructFromNativeArray(ref root.anglesPerLeftChannel,                anglesPerLeftChannel);
                builder.ConstructFromNativeArray(ref root.anglesPerRightChannel,               anglesPerRightChannel);
                builder.ConstructFromNativeArray(ref root.passthroughFractionsPerLeftChannel,  passthroughFractionsPerLeftChannel);
                builder.ConstructFromNativeArray(ref root.passthroughFractionsPerRightChannel, passthroughFractionsPerRightChannel);
                builder.ConstructFromNativeArray(ref root.filterVolumesPerLeftChannel,         filterVolumesPerLeftChannel);
                builder.ConstructFromNativeArray(ref root.filterVolumesPerRightChannel,        filterVolumesPerRightChannel);
                builder.ConstructFromNativeArray(ref root.passthroughVolumesPerLeftChannel,    passthroughVolumesPerLeftChannel);
                builder.ConstructFromNativeArray(ref root.passthroughVolumesPerRightChannel,   passthroughVolumesPerRightChannel);

                blob.Value = builder.CreateBlobAssetReference<ListenerProfileBlob>(Allocator.Persistent);
            }
        }
        #endregion
    }
}

