using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Myri
{
    /// <summary>
    /// Utility class for working with Unity AudioClip
    /// </summary>
    public static class AudioCodecUtility
    {

        /// <summary>
        /// Encodes <see cref="UnityEngine.AudioClip"/> to ADPCM format
        /// </summary>
        public static NativeArray<byte> EncodeAudioClip(UnityEngine.AudioClip clip)
        {
            var inputArray = AudioClipUtility.GetAudioClipData(clip);

            ADPCMEncodeContext encoder = new ADPCMEncodeContext(inputArray, clip.channels, clip.frequency);
            encoder.Encode().Schedule().Complete();

            // Copy to managed array for return
            var result = encoder.EncodedClipData.ToArray();
            encoder.Dispose();
            inputArray.Dispose();

            return new NativeArray<byte>(result, Allocator.Persistent);
        }

        /// <summary>
        /// Decodes ADPCM data to <see cref="UnityEngine.AudioClip"/> compatible format
        /// </summary>
        public static Span<float> DecodeToFloat(NativeArray<byte> encodedData, int sampleCount,
                                          int channels = 1, int startSample = 0)
        {
            ADPCMDecodeContext decoder = new ADPCMDecodeContext(encodedData, channels, sampleCount);
            decoder.Decode(startSample).Schedule().Complete();

            var result = decoder.DecodedClipData.ToArray();
            encodedData.Dispose();
            decoder.Dispose();

            return result;
        }

        /// <summary>
        /// Decodes ADPCM data from seek position to <see cref="UnityEngine.AudioClip"/> compatible format
        /// </summary>
        public static Span<float> DecodeToFloatFromSeekPos(NativeArray<byte> encodedData, int sampleCount, int seekPos,
                                          int channels = 1, int startSample = 0)
        {
            int remainingSamples = sampleCount - seekPos;
            return DecodeToFloat(encodedData, remainingSamples, channels, startSample);
        }
        /// <summary>
        /// Get time position from seek sample
        /// </summary>
        public static float GetTimePositionFromSeek(int seekSample, int sampleRate, int channels) => (float)seekSample / sampleRate / channels;

        /// <summary>
        /// Calculate Signal-to-Noise Ratio between original and decoded audio
        /// </summary>
        public static float CalculateSignalToNoiseRatio(float[] original, float[] decoded)
        {
            if (original.Length != decoded.Length)
            {
                UnityEngine.Debug.LogWarning("Array lengths don't match for SNR calculation");
                return 0f;
            }

            double signalPower = 0;
            double noisePower = 0;

            for (int i = 0; i < original.Length; i++)
            {
                signalPower += original[i] * original[i];
                double error = original[i] - decoded[i];
                noisePower += error * error;
            }

            if (noisePower == 0) return float.PositiveInfinity; // Perfect match

            double snr = 10 * math.log10(signalPower / noisePower);
            return (float)snr;
        }

        /// <summary>
        /// Compare audio segments for accuracy measurement
        /// </summary>
        public static float CompareAudioSegments(float[] original, float[] decoded, int offset)
        {
            int compareLength = math.min(decoded.Length, original.Length - offset);
            if (compareLength <= 0) return 0f;

            double totalError = 0;
            double totalSignal = 0;

            for (int i = 0; i < compareLength; i++)
            {
                double error = math.abs(original[offset + i] - decoded[i]);
                totalError += error;
                totalSignal += math.abs(original[offset + i]);
            }

            if (totalSignal == 0) return 100f;

            double accuracy = (1.0 - (totalError / totalSignal)) * 100.0;
            return (float)math.max(0, accuracy);
        }
    }
}
