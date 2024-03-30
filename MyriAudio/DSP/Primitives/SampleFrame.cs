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

    // General reminder: Adding dB is like multiplying raw, and subtracting dB is like dividing.
    public static class SampleUtilities
    {
        public static float ConvertToDB(float rawSample)
        {
            var result = 20f * math.log10(math.abs(rawSample));
            return math.select(-144f, result, math.isfinite(result));
        }

        public static float ConvertDBToRawAttenuation(float dB)
        {
            return math.pow(10f, dB / 20f);
        }
    }
}

