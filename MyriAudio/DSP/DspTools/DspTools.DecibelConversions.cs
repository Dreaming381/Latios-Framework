using Unity.Mathematics;

// General reminder: Adding dB is like multiplying raw, and subtracting dB is like dividing.

namespace Latios.Myri.DSP
{
    public static partial class DspTools
    {
        /// <summary>
        /// Converts a raw sample value to decibels, where -1 or 1 maps to 0 dB
        /// </summary>
        public static float ConvertToDB(float rawSample)
        {
            var result = 20f * math.log10(math.abs(rawSample));
            return math.select(-144f, result, math.isfinite(result));
        }

        /// <summary>
        /// Converts the decibel value into a raw multiplier to apply to samples
        /// </summary>
        public static float ConvertDBToRawAttenuation(float dB)
        {
            return math.pow(10f, dB / 20f);
        }
    }
}

