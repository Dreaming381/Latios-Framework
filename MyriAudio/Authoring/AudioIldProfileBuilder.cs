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
    public abstract class AudioIldProfileBuilder : ScriptableObject
    {
        protected abstract void BuildProfile();

        protected ChannelHandle AddChannel(float2 minMaxHorizontalAngleInRadiansCounterClockwiseFromRight,
                                           float2 minMaxVerticalAngleInRadians,
                                           float passthroughFraction,
                                           float filterVolume,
                                           float passthroughVolume,
                                           bool isRightEar)
        {
            if (m_anglesPerLeftChannel.Length + m_anglesPerRightChannel.Length >= 127)
                throw new InvalidOperationException("An IldProfile only supports up to 127 channels");

            if (isRightEar)
            {
                m_anglesPerRightChannel.Add(new float4(minMaxHorizontalAngleInRadiansCounterClockwiseFromRight, minMaxVerticalAngleInRadians));
                m_passthroughFractionsPerRightChannel.Add(math.saturate(passthroughFraction));
                m_filterVolumesPerRightChannel.Add(math.saturate(filterVolume));
                m_passthroughVolumesPerRightChannel.Add(math.saturate(passthroughVolume));
                return new ChannelHandle { channelIndex = m_anglesPerRightChannel.Length - 1, isRightChannel = true };
            }
            else
            {
                m_anglesPerLeftChannel.Add(new float4(minMaxHorizontalAngleInRadiansCounterClockwiseFromRight, minMaxVerticalAngleInRadians));
                m_passthroughFractionsPerLeftChannel.Add(passthroughFraction);
                m_filterVolumesPerLeftChannel.Add(math.saturate(filterVolume));
                m_passthroughVolumesPerLeftChannel.Add(math.saturate(passthroughVolume));
                return new ChannelHandle { channelIndex = m_anglesPerLeftChannel.Length - 1, isRightChannel = false };
            }
        }

        protected void AddFilterToChannel(FrequencyFilter filter, ChannelHandle channel)
        {
            if (channel.isRightChannel)
            {
                m_filtersRight.Add(filter);
                m_channelIndicesRight.Add(channel.channelIndex);
            }
            else
            {
                m_filtersLeft.Add(filter);
                m_channelIndicesLeft.Add(channel.channelIndex);
            }
        }

        public struct ChannelHandle
        {
            internal int  channelIndex;
            internal bool isRightChannel;
        }

        #region Internals
        private NativeList<FrequencyFilter> m_filtersLeft;
        private NativeList<int>             m_channelIndicesLeft;
        private NativeList<FrequencyFilter> m_filtersRight;
        private NativeList<int>             m_channelIndicesRight;

        private NativeList<float4> m_anglesPerLeftChannel;
        private NativeList<float4> m_anglesPerRightChannel;
        private NativeList<float>  m_passthroughFractionsPerLeftChannel;
        private NativeList<float>  m_passthroughFractionsPerRightChannel;
        private NativeList<float>  m_filterVolumesPerLeftChannel;
        private NativeList<float>  m_filterVolumesPerRightChannel;
        private NativeList<float>  m_passthroughVolumesPerLeftChannel;
        private NativeList<float>  m_passthroughVolumesPerRightChannel;

        private bool                               m_computedHash = false;
        private Hash128                            m_hash;
        private BlobAssetReference<IldProfileBlob> m_blobProfile;

        internal Hash128 ComputeHash()
        {
            m_computedHash = false;
            m_blobProfile  = default;

            m_filtersLeft.Clear();
            m_filtersRight.Clear();
            m_channelIndicesLeft.Clear();
            m_channelIndicesRight.Clear();
            m_anglesPerLeftChannel.Clear();
            m_anglesPerRightChannel.Clear();
            m_passthroughFractionsPerLeftChannel.Clear();
            m_passthroughFractionsPerRightChannel.Clear();
            m_filterVolumesPerLeftChannel.Clear();
            m_filterVolumesPerRightChannel.Clear();
            m_passthroughVolumesPerLeftChannel.Clear();
            m_passthroughVolumesPerRightChannel.Clear();

            BuildProfile();

            var job = new ComputeHashJob
            {
                filtersLeft                         = m_filtersLeft,
                channelIndicesLeft                  = m_channelIndicesLeft,
                filtersRight                        = m_filtersRight,
                channelIndicesRight                 = m_channelIndicesRight,
                anglesPerLeftChannel                = m_anglesPerLeftChannel,
                anglesPerRightChannel               = m_anglesPerRightChannel,
                passthroughFractionsPerLeftChannel  = m_passthroughFractionsPerLeftChannel,
                passthroughFractionsPerRightChannel = m_passthroughFractionsPerRightChannel,
                filterVolumesPerLeftChannel         = m_filterVolumesPerLeftChannel,
                filterVolumesPerRightChannel        = m_filterVolumesPerRightChannel,
                passthroughVolumesPerLeftChannel    = m_passthroughVolumesPerLeftChannel,
                passthroughVolumesPerRightChannel   = m_passthroughVolumesPerRightChannel,
                result                              = new NativeReference<Hash128>(Allocator.TempJob)
            };
            job.Run();
            m_hash = job.result.Value;
            job.result.Dispose();
            m_computedHash = true;
            return m_hash;
        }

        internal BlobAssetReference<IldProfileBlob> ComputeBlob()
        {
            if (m_blobProfile.IsCreated)
                return m_blobProfile;

            if (!m_computedHash)
                ComputeHash();

            var     builder = new BlobBuilder(Allocator.Temp);
            ref var root    = ref builder.ConstructRoot<IldProfileBlob>();
            builder.ConstructFromNativeArray(ref root.filtersLeft,                         m_filtersLeft);
            builder.ConstructFromNativeArray(ref root.channelIndicesLeft,                  m_channelIndicesLeft);
            builder.ConstructFromNativeArray(ref root.filtersRight,                        m_filtersRight);
            builder.ConstructFromNativeArray(ref root.channelIndicesRight,                 m_channelIndicesRight);

            builder.ConstructFromNativeArray(ref root.anglesPerLeftChannel,                m_anglesPerLeftChannel);
            builder.ConstructFromNativeArray(ref root.anglesPerRightChannel,               m_anglesPerRightChannel);
            builder.ConstructFromNativeArray(ref root.passthroughFractionsPerLeftChannel,  m_passthroughFractionsPerLeftChannel);
            builder.ConstructFromNativeArray(ref root.passthroughFractionsPerRightChannel, m_passthroughFractionsPerRightChannel);
            builder.ConstructFromNativeArray(ref root.filterVolumesPerLeftChannel,         m_filterVolumesPerLeftChannel);
            builder.ConstructFromNativeArray(ref root.filterVolumesPerRightChannel,        m_filterVolumesPerRightChannel);
            builder.ConstructFromNativeArray(ref root.passthroughVolumesPerLeftChannel,    m_passthroughVolumesPerLeftChannel);
            builder.ConstructFromNativeArray(ref root.passthroughVolumesPerRightChannel,   m_passthroughVolumesPerRightChannel);

            m_blobProfile = builder.CreateBlobAssetReference<IldProfileBlob>(Allocator.Persistent);
            return m_blobProfile;
        }

        [BurstCompile]
        private struct ComputeHashJob : IJob
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

            public NativeReference<Hash128> result;

            public unsafe void Execute()
            {
                var bytes = new NativeList<byte>(Allocator.Temp);
                bytes.AddRange(filtersLeft.AsArray().Reinterpret<byte>(UnsafeUtility.SizeOf<FrequencyFilter>()));
                bytes.AddRange(filtersRight.AsArray().Reinterpret<byte>(UnsafeUtility.SizeOf<FrequencyFilter>()));
                bytes.AddRange(channelIndicesLeft.AsArray().Reinterpret<byte>(UnsafeUtility.SizeOf<int>()));
                bytes.AddRange(channelIndicesRight.AsArray().Reinterpret<byte>(UnsafeUtility.SizeOf<int>()));
                bytes.AddRange(anglesPerLeftChannel.AsArray().Reinterpret<byte>(UnsafeUtility.SizeOf<float4>()));
                bytes.AddRange(anglesPerRightChannel.AsArray().Reinterpret<byte>(UnsafeUtility.SizeOf<float4>()));
                bytes.AddRange(passthroughFractionsPerLeftChannel.AsArray().Reinterpret<byte>(UnsafeUtility.SizeOf<float>()));
                bytes.AddRange(passthroughFractionsPerRightChannel.AsArray().Reinterpret<byte>(UnsafeUtility.SizeOf<float>()));
                bytes.AddRange(filterVolumesPerLeftChannel.AsArray().Reinterpret<byte>(UnsafeUtility.SizeOf<float>()));
                bytes.AddRange(filterVolumesPerRightChannel.AsArray().Reinterpret<byte>(UnsafeUtility.SizeOf<float>()));
                bytes.AddRange(passthroughVolumesPerLeftChannel.AsArray().Reinterpret<byte>(UnsafeUtility.SizeOf<float>()));
                bytes.AddRange(passthroughVolumesPerRightChannel.AsArray().Reinterpret<byte>(UnsafeUtility.SizeOf<float>()));

                uint4 temp   = xxHash3.Hash128(bytes.GetUnsafePtr(), bytes.Length);
                result.Value = new Hash128(temp);
            }
        }

        private void OnEnable()
        {
            m_filtersLeft                         = new NativeList<FrequencyFilter>(Allocator.Persistent);
            m_filtersRight                        = new NativeList<FrequencyFilter>(Allocator.Persistent);
            m_channelIndicesLeft                  = new NativeList<int>(Allocator.Persistent);
            m_channelIndicesRight                 = new NativeList<int>(Allocator.Persistent);
            m_anglesPerLeftChannel                = new NativeList<float4>(Allocator.Persistent);
            m_anglesPerRightChannel               = new NativeList<float4>(Allocator.Persistent);
            m_passthroughFractionsPerLeftChannel  = new NativeList<float>(Allocator.Persistent);
            m_passthroughFractionsPerRightChannel = new NativeList<float>(Allocator.Persistent);
            m_filterVolumesPerLeftChannel         = new NativeList<float>(Allocator.Persistent);
            m_filterVolumesPerRightChannel        = new NativeList<float>(Allocator.Persistent);
            m_passthroughVolumesPerLeftChannel    = new NativeList<float>(Allocator.Persistent);
            m_passthroughVolumesPerRightChannel   = new NativeList<float>(Allocator.Persistent);
        }

        private void OnDisable()
        {
            {
                m_filtersLeft.Dispose();
                m_filtersRight.Dispose();
                m_channelIndicesLeft.Dispose();
                m_channelIndicesRight.Dispose();
                m_anglesPerLeftChannel.Dispose();
                m_anglesPerRightChannel.Dispose();
                m_passthroughFractionsPerLeftChannel.Dispose();
                m_passthroughFractionsPerRightChannel.Dispose();
                m_filterVolumesPerLeftChannel.Dispose();
                m_filterVolumesPerRightChannel.Dispose();
                m_passthroughVolumesPerLeftChannel.Dispose();
                m_passthroughVolumesPerRightChannel.Dispose();
            }
        }
        #endregion
    }
}

