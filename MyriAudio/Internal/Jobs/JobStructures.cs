using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Audio;

namespace Latios.Myri
{
    internal struct CapturedSourceHeader
    {
        public enum Features : uint
        {
            None = 0x0,
            Clip = 0x1,
            SampleRateMultiplier = 0x2,
            ChannelID = 0x10000000,
            Transform = 0x20000000,
            DistanceFalloff = 0x40000000,
            Cone = 0x80000000,
            BatchingFeatures = Clip | SampleRateMultiplier,
        }

        public Features features;
        public float    volume;

        public bool HasFlag(Features flags) => (features & flags) != Features.None;
    }

    internal unsafe struct ChannelStreamSource
    {
        public CapturedSourceHeader sourceHeader;
        public byte*                sourceDataPtr;
        public int                  batchingByteCount;
        private uint                packed;

        public int itdIndex
        {
            get => (int)Bits.GetBits(packed, 0, 15);
            set => Bits.SetBits(ref packed, 0, 15, (uint)value);
        }
        public int itdCount
        {
            get => (int)Bits.GetBits(packed, 15, 16);
            set => Bits.SetBits(ref packed, 15, 16, (uint)value);
        }
        public bool isRightChannel
        {
            get => Bits.GetBit(packed, 31);
            set => Bits.SetBit(ref packed, 31, value);
        }
    }

    internal struct ListenerWithTransform
    {
        public AudioListener  listener;
        public RigidTransform transform;
        public int2           channelIDsRange;
    }

    internal struct ListenerWithPresampling
    {
        public Entity                                  listener;
        public BlobAssetReference<ListenerProfileBlob> profile;
    }

    internal struct AudioFrameBufferHistoryElement
    {
        public int bufferId;
        public int audioFrame;
        public int expectedNextUpdateFrame;
    }

    internal struct CapturedFrameState
    {
        public int           audioFrame;
        public int           lastPlayedAudioFrame;
        public int           lastConsumedBufferId;
        public AudioFormat   format;
        public AudioSettings audioSettings;
        public bool          requiresSourceReset;
    }
}

