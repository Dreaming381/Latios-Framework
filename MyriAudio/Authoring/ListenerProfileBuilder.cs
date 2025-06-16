using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Myri.Authoring
{
    /// <summary>
    /// Implement this interface to define a custom audio listener profile
    /// </summary>
    public interface IListenerProfileBuilder
    {
        void BuildProfile(ref ListenerProfileBuildContext context);
    }

    /// <summary>
    /// An asset type which is used to define a volume and frequency spatialization profile for an audio listener
    /// </summary>
    public abstract class ListenerProfileBuilder : ScriptableObject, IListenerProfileBuilder
    {
        /// <summary>
        /// Override this function to make several calls to AddChannel and AddFilterToChannel which defines a profile.
        /// </summary>
        public abstract void BuildProfile(ref ListenerProfileBuildContext context);
    }

    public struct ListenerProfileBuildContext
    {
        /// <summary>
        /// Adds a spatial channel to the profile. A spatial channel is a 3D radial slice where all audio sources coming from that direction
        /// are subject to the channels filters. Sources between channels will interpolate between the channels.
        /// </summary>
        /// <param name="minMaxHorizontalAngleInRadiansCounterClockwiseFromRight">
        /// The horizontal extremes of the slice in radians beginning from the left ear positively rotating towards forward.
        /// Values between -2pi and +2pi are allowed.</param>
        /// <param name="minMaxVerticalAngleInRadians">
        /// The vertical extremes of the slice in radians beginning from horizontal positively rotating upwards.
        /// Values between -2pi and +2pi are allowed.</param>
        /// <param name="volume">A volume multiplier for the channel. Useful for channels which don't have any filters but need
        /// to scale volume relative to other channels.</param>
        /// <param name="isRightEar">If true, this channel uses the right ear. Otherwise, it uses the left ear.</param>
        /// <returns>A handle which can be used to add filters to the channel</returns>
        public ChannelHandle AddSpatialChannel(float2 minMaxHorizontalAngleInRadiansCounterClockwiseFromRight,
                                               float2 minMaxVerticalAngleInRadians,
                                               float volume,
                                               bool isRightEar)
        {
            if (m_job.anglesPerLeftChannel.Length + m_job.anglesPerRightChannel.Length >= 127)
                throw new InvalidOperationException("An Listener Profile only supports up to 127 channels");

            if (isRightEar)
            {
                m_job.anglesPerRightChannel.Add(new float4(minMaxHorizontalAngleInRadiansCounterClockwiseFromRight, minMaxVerticalAngleInRadians));
                m_job.volumesPerRightChannel.Add(volume);
                return new ChannelHandle { channelIndex = m_job.anglesPerRightChannel.Length - 1, isRightChannel = true };
            }
            else
            {
                m_job.anglesPerLeftChannel.Add(new float4(minMaxHorizontalAngleInRadiansCounterClockwiseFromRight, minMaxVerticalAngleInRadians));
                m_job.volumesPerLeftChannel.Add(volume);
                return new ChannelHandle { channelIndex = m_job.anglesPerLeftChannel.Length - 1, isRightChannel = false };
            }
        }

        /// <summary>
        /// Adds a direct channel to the profile. A direct channel only receives audio sources which do not have any spatial information.
        /// Direct channels are still subject to the channels filters.
        /// </summary>
        /// <param name="volume">A volume multiplier for the channel. Useful for channels which don't have any filters but need
        /// to scale volume relative to other channels.</param>
        /// <param name="isRightEar">If true, this channel uses the right ear. Otherwise, it uses the left ear.</param>
        /// <returns>A handle which can be used to add filters to the channel</returns>
        public ChannelHandle AddDirectChannel(float volume, bool isRightEar)
        {
            if (m_job.anglesPerLeftChannel.Length + m_job.anglesPerRightChannel.Length >= 127)
                throw new InvalidOperationException("An Listener Profile only supports up to 127 channels");

            if (isRightEar)
            {
                m_job.anglesPerRightChannel.Add(float.NaN);
                m_job.volumesPerRightChannel.Add(volume);
                return new ChannelHandle { channelIndex = m_job.anglesPerRightChannel.Length - 1, isRightChannel = true };
            }
            else
            {
                m_job.anglesPerLeftChannel.Add(float.NaN);
                m_job.volumesPerLeftChannel.Add(volume);
                return new ChannelHandle { channelIndex = m_job.anglesPerLeftChannel.Length - 1, isRightChannel = false };
            }
        }

        /// <summary>
        /// Adds a filter to the channel. Filters are applied in the order they are added.
        /// </summary>
        /// <param name="filter">The filter to apply</param>
        /// <param name="channel">The channel handle returned from AddChannel</param>
        public void AddFilterToChannel(FrequencyFilter filter, ChannelHandle channel)
        {
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

        FinalizeBlobJob m_job;

        internal void Initialize()
        {
            m_job = new FinalizeBlobJob
            {
                filtersLeft            = new NativeList<FrequencyFilter>(Allocator.TempJob),
                filtersRight           = new NativeList<FrequencyFilter>(Allocator.TempJob),
                channelIndicesLeft     = new NativeList<int>(Allocator.TempJob),
                channelIndicesRight    = new NativeList<int>(Allocator.TempJob),
                anglesPerLeftChannel   = new NativeList<float4>(Allocator.TempJob),
                anglesPerRightChannel  = new NativeList<float4>(Allocator.TempJob),
                volumesPerLeftChannel  = new NativeList<float>(Allocator.TempJob),
                volumesPerRightChannel = new NativeList<float>(Allocator.TempJob),
                blobNativeReference    = new NativeReference<BlobAssetReference<ListenerProfileBlob> >(Allocator.TempJob)
            };
        }

        internal BlobAssetReference<ListenerProfileBlob> ComputeBlobAndDispose()
        {
            m_job.Run();
            var blob = m_job.blobNativeReference.Value;

            m_job.filtersLeft.Dispose();
            m_job.filtersRight.Dispose();
            m_job.channelIndicesLeft.Dispose();
            m_job.channelIndicesRight.Dispose();
            m_job.anglesPerLeftChannel.Dispose();
            m_job.anglesPerRightChannel.Dispose();
            m_job.volumesPerLeftChannel.Dispose();
            m_job.volumesPerRightChannel.Dispose();
            m_job.blobNativeReference.Dispose();

            return blob;
        }

        [BurstCompile]
        unsafe struct FinalizeBlobJob : IJob
        {
            public NativeList<FrequencyFilter> filtersLeft;
            public NativeList<int>             channelIndicesLeft;
            public NativeList<FrequencyFilter> filtersRight;
            public NativeList<int>             channelIndicesRight;

            public NativeList<float4> anglesPerLeftChannel;
            public NativeList<float4> anglesPerRightChannel;
            public NativeList<float>  volumesPerLeftChannel;
            public NativeList<float>  volumesPerRightChannel;

            public NativeReference<BlobAssetReference<ListenerProfileBlob> > blobNativeReference;

            public unsafe void Execute()
            {
                var     builder = new BlobBuilder(Allocator.Temp);
                ref var root    = ref builder.ConstructRoot<ListenerProfileBlob>();
                builder.ConstructFromNativeArray(ref root.anglesPerLeftChannel,  anglesPerLeftChannel.AsArray());
                builder.ConstructFromNativeArray(ref root.anglesPerRightChannel, anglesPerRightChannel.AsArray());
                var       dspsLeft                   = builder.Allocate(ref root.channelDspsLeft, volumesPerLeftChannel.Length);
                var       dspsRight                  = builder.Allocate(ref root.channelDspsRight, volumesPerRightChannel.Length);
                Span<int> leftFilterCountsByChannel  = stackalloc int[dspsLeft.Length];
                Span<int> rightFilterCountsByChannel = stackalloc int[dspsRight.Length];
                leftFilterCountsByChannel.Clear();
                rightFilterCountsByChannel.Clear();

                foreach (var c in channelIndicesLeft)
                    leftFilterCountsByChannel[c]++;
                foreach (var c in channelIndicesRight)
                    rightFilterCountsByChannel[c]++;

                Span<ChannelDspPtr> leftChannelFilters  = stackalloc ChannelDspPtr[dspsLeft.Length];
                Span<ChannelDspPtr> rightChannelFilters = stackalloc ChannelDspPtr[dspsRight.Length];
                leftChannelFilters.Clear();
                rightChannelFilters.Clear();

                for (int i = 0; i < dspsLeft.Length; i++)
                {
                    dspsLeft[i] = new ListenerProfileBlob.ChannelDsp
                    {
                        volume = volumesPerLeftChannel[i],
                    };
                    leftChannelFilters[i].ptr = (FrequencyFilter*)builder.Allocate(ref dspsLeft[i].filters, leftFilterCountsByChannel[i]).GetUnsafePtr();
                }
                for (int i = 0; i < dspsRight.Length; i++)
                {
                    dspsRight[i] = new ListenerProfileBlob.ChannelDsp
                    {
                        volume = volumesPerLeftChannel[i],
                    };
                    rightChannelFilters[i].ptr = (FrequencyFilter*)builder.Allocate(ref dspsRight[i].filters, rightFilterCountsByChannel[i]).GetUnsafePtr();
                }

                for (int i = 0; i < channelIndicesLeft.Length; i++)
                {
                    var channel = channelIndicesLeft[i];
                    var filter  = filtersLeft[i];

                    *leftChannelFilters[channel].ptr = filtersLeft[i];
                    leftChannelFilters[channel].ptr++;
                }
                for (int i = 0; i < channelIndicesRight.Length; i++)
                {
                    var channel = channelIndicesRight[i];
                    var filter  = filtersRight[i];

                    *rightChannelFilters[channel].ptr = filtersRight[i];
                    rightChannelFilters[channel].ptr++;
                }

                blobNativeReference.Value = builder.CreateBlobAssetReference<ListenerProfileBlob>(Allocator.Persistent);
            }

            struct ChannelDspPtr
            {
                public FrequencyFilter* ptr;
            }
        }
        #endregion
    }
}

