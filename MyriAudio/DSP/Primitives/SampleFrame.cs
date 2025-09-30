using Unity.Collections;
using Unity.Mathematics;

namespace Latios.Myri.DSP
{
    // Make public on release
    internal struct SampleFrame
    {
        public NativeArray<float> left { get; internal set; }
        public NativeArray<float> right { get; internal set; }
        public int frameIndex { get; internal set; }
        public bool connected;

        public int length => left.Length;

        public ReadOnly readOnly => new ReadOnly
        {
            left       = left.AsReadOnly(),
            right      = right.AsReadOnly(),
            frameIndex = frameIndex,
            connected  = connected
        };

        public struct ReadOnly
        {
            public NativeArray<float>.ReadOnly left { get; internal set; }
            public NativeArray<float>.ReadOnly right { get; internal set; }
            public int frameIndex { get; internal set; }
            public bool connected { get; internal set; }
        }

        public void ClearToZero()
        {
            left.AsSpan().Clear();
            right.AsSpan().Clear();
        }
    }
}

