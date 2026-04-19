using System;
using Latios.Transforms;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Myri
{
    internal unsafe struct IldBufferChannel
    {
        public float* buffer;
    }

    internal unsafe struct IldBuffer
    {
        [NativeDisableUnsafePtrRestriction]
        public IldBufferChannel* bufferChannels;
        public int               channelCount;
        public int               frame;
        public int               bufferId;
        public int               framesInBuffer;
        public int               framesPerUpdate;
        public bool              warnIfStarved;
    }
}

namespace Latios.Myri.Interop
{
    internal unsafe struct DspUpdateBuffer
    {
        public int bufferId;

        public PresampledAndTimingUpdateBuffer presampledAndTimingUpdateBuffer;

        public BrickwallLimiterSettings masterLimiterSettings;
    }

    #region Pre-sampled
    internal unsafe struct PresampledAndTimingUpdateBuffer
    {
        public UnsafeList<PresampledBufferForListener> presampledBuffersForListeners;
        public int                                     frame;
        public int                                     framesInBuffer;
        public int                                     framesPerUpdate;
        public bool                                    warnIfStarved;
    }

    internal unsafe struct PresampledBufferForListener : IComparable<PresampledBufferForListener>
    {
        public float* samples;
        public int    listenerId;
        public int    channelIndex;

        public int CompareTo(PresampledBufferForListener other)
        {
            var result = listenerId.CompareTo(other.listenerId);
            if (result == 0)
                return channelIndex.CompareTo(other.channelIndex);
            return result;
        }
    }
    #endregion
}

