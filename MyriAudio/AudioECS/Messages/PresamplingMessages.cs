using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Myri.AudioEcsBuiltin
{
    public unsafe struct PresampledListenerMessage
    {
        public Entity                                  listenerEntity;
        public BlobAssetReference<ListenerProfileBlob> profile;
        public float*                                  buffer;
        public PipeSpan<int>                           startOffsetInBufferByChannel;  // -1 means no samples
        public int                                     audioFramesInUpdate;
        public int                                     targetFrame;
        public int                                     nextUpdateFrame;
        public int                                     sampleRate;
        public int                                     samplesPerAudioFrame;
    }
}

