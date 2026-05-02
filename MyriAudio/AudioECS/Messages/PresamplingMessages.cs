using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Myri.AudioEcsBuiltin
{
    public struct PresampledBufferMessage
    {
        public PipeSpan<float> samples;
        public Entity          listenerEntity;
        public int             listenerChannel;
        public int             targetFrame;
        public int             nextUpdateFrame;
    }
}

