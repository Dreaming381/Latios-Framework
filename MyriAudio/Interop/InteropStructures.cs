using System;
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

