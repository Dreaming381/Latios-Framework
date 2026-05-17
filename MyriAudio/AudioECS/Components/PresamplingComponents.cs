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
    public unsafe struct PresampledUpdate : IDisposable
    {
        public BlobAssetReference<ListenerProfileBlob> profile;
        public float*                                  buffer;
        public UnsafeList<int>                         startOffsetInBufferByChannel;  // -1 means no samples
        public int                                     audioFramesInUpdate;
        public int                                     targetFrame;
        public int                                     nextUpdateFrame;
        public int                                     sampleRate;
        public int                                     samplesPerAudioFrame;

        public void Dispose() => startOffsetInBufferByChannel.Dispose();
    }

    public struct PresampledChannel : IDisposable
    {
        public struct Svf
        {
            public StateVariableFilter.Channel      channel;
            public StateVariableFilter.Coefficients coefficients;
        }

        public UnsafeList<Svf> filters;
        public float           volume;
        public float           destepSample;

        public void Dispose()
        {
            if (filters.IsCreated)
                filters.Dispose();
            this = default;
        }
    }

    public partial struct ListenerPresamplingState : IVInterface, IAuxDisposable
    {
        public UnsafeList<PresampledChannel>           channels;
        public UnsafeList<PresampledUpdate>            updates;
        public BlobAssetReference<ListenerProfileBlob> previousBlob;
        public int                                     nextUpdateFrame;
        public int                                     previousSampleRate;

        public void Dispose()
        {
            if (channels.IsCreated)
            {
                foreach (var channel in channels)
                    channel.Dispose();
                channels.Dispose();
            }
            if (updates.IsCreated)
            {
                foreach (var update in updates)
                    update.Dispose();
                updates.Dispose();
            }
            this = default;
        }
    }
}

