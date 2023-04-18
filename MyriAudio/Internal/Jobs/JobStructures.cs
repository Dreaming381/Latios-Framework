using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Myri
{
    internal struct ClipFrameLookup : IEquatable<ClipFrameLookup>
    {
        public BlobAssetReference<AudioClipBlob> clip;
        public int                               spawnFrameOrOffset;

        public unsafe bool Equals(ClipFrameLookup other)
        {
            return ((ulong)clip.GetUnsafePtr()).Equals((ulong)other.clip.GetUnsafePtr()) && spawnFrameOrOffset == other.spawnFrameOrOffset;
        }

        public unsafe override int GetHashCode()
        {
            return new int2((int)((ulong)clip.GetUnsafePtr() >> 4), spawnFrameOrOffset).GetHashCode();
        }
    }

    internal struct Weights
    {
        public FixedList512Bytes<float> channelWeights;
        public FixedList128Bytes<float> itdWeights;

        public static Weights operator + (Weights a, Weights b)
        {
            Weights result = a;
            for (int i = 0; i < a.channelWeights.Length; i++)
            {
                result.channelWeights[i] += b.channelWeights[i];
            }
            for (int i = 0; i < a.itdWeights.Length; i++)
            {
                result.itdWeights[i] += b.itdWeights[i];
            }
            return result;
        }
    }

    internal struct ListenerBufferParameters
    {
        public int bufferStart;
        public int leftChannelsCount;
        public int samplesPerChannel;
    }

    internal struct ListenerWithTransform
    {
        public AudioListener  listener;
        public RigidTransform transform;
    }

    internal struct OneshotEmitter
    {
        public AudioSourceOneShot     source;
        public RigidTransform         transform;
        public AudioSourceEmitterCone cone;
        public bool                   useCone;
    }

    internal struct LoopedEmitter
    {
        public AudioSourceLooped      source;
        public RigidTransform         transform;
        public AudioSourceEmitterCone cone;
        public bool                   useCone;
    }

    internal struct AudioFrameBufferHistoryElement
    {
        public int bufferId;
        public int audioFrame;
        public int expectedNextUpdateFrame;
    }
}

