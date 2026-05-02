using System;
using Latios.AuxEcs;
using Latios.Myri.DSP;
using Latios.Unsafe;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Myri.AudioEcsBuiltin
{
    public unsafe struct PresampledFrame : IDisposable
    {
        //public float*                           samples;
        //public AllocatorManager.AllocatorHandle allocator;
        //public float                            extraSample;
        //public int                              sampleCount;
        //public int                              targetFrame;
        //public int                              nextUpdateFrame;
        //public int                              visualFrameId;

        public void Dispose()
        {
            //if (samples != null)
            //    AllocatorManager.Free(allocator, samples, sampleCount);
            this = default;
        }
    }

    public struct PresampledChannel : IDisposable
    {
        public struct Svf
        {
            public StateVariableFilter.Channel      channel;
            public StateVariableFilter.Coefficients coefficients;
        }

        public UnsafeList<PresampledFrame> presampledFrames;
        public UnsafeList<Svf>             filters;

        public void Dispose()
        {
            if (presampledFrames.IsCreated)
            {
                foreach (var update in presampledFrames)
                    update.Dispose();
                presampledFrames.Dispose();
            }
            if (filters.IsCreated)
                filters.Dispose();
            this = default;
        }
    }

    public partial struct ListenerPresamplingState : IVInterface, IAuxDisposable
    {
        public UnsafeList<PresampledChannel>           channels;
        public BlobAssetReference<ListenerProfileBlob> previousBlob;

        public void Dispose()
        {
            if (channels.IsCreated)
            {
                foreach (var channel in channels)
                    channel.Dispose();
                channels.Dispose();
            }
            this = default;
        }
    }
}

