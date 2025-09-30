using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Myri.DSP
{
    public static partial class DspTools
    {
        /// <summary>
        /// Accumulate signal and noise powers
        /// </summary>
        public static (double signal, double noise) AccumulateSignalNoisePower(ReadOnlySpan<float> original, ReadOnlySpan<float> decoded)
        {
            if (original.Length != decoded.Length)
            {
                UnityEngine.Debug.LogWarning($"Array lengths don't match: {original.Length} vs {decoded.Length}");
                return (0, 0);
            }

            double signalPower = 0;
            double noisePower  = 0;

            for (int i = 0; i < original.Length; i++)
            {
                signalPower  += original[i] * original[i];
                double error  = original[i] - decoded[i];
                noisePower   += error * error;
            }

            return (signalPower, noisePower);
        }

        /// <summary>
        /// Calculate Signal-to-Noise Ratio between powers
        /// </summary>
        public static float CalculateSignalToNoiseRatio(double signalPower, double noisePower)
        {
            if (signalPower == 0)
            {
                UnityEngine.Debug.LogWarning($"No signal");
                return 0;
            }

            if (noisePower == 0)
                return float.PositiveInfinity; // Perfect match

            double snr = 10 * math.log10(signalPower / noisePower);
            return (float)snr;
        }

        /// <summary>
        /// Calculate Signal-to-Noise Ratio between original and decoded audio
        /// </summary>
        public static float CalculateSignalToNoiseRatio(ReadOnlySpan<float> original, ReadOnlySpan<float> decoded)
        {
            var   powers = AccumulateSignalNoisePower(original, decoded);
            float snr    = CalculateSignalToNoiseRatio(powers.signal, powers.noise);
            return snr;
        }

        /// <summary>
        /// Calculate Signal-to-Noise Ratio between original and decoded audio
        /// </summary>
        public static float CalculateSignalToNoiseRatio(ReadOnlySpan<float> originalLeft,
                                                        ReadOnlySpan<float> originalRight,
                                                        ReadOnlySpan<float> decodedLeft,
                                                        ReadOnlySpan<float> decodedRight)
        {
            var   powersLeft  = AccumulateSignalNoisePower(originalLeft, decodedLeft);
            var   powersRight = AccumulateSignalNoisePower(originalRight, decodedRight);
            var   signal      = powersLeft.signal + powersRight.signal;
            var   noise       = powersLeft.noise + powersRight.noise;
            float snr         = CalculateSignalToNoiseRatio(signal, noise);
            return snr;
        }
    }
}

