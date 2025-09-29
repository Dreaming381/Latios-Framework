using System;
using System.Text;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static UnityEngine.UI.Image;

namespace Latios.Myri
{
    [BurstCompile]
    public struct ADPCMCodec
    {
        // ADPCM step table
        private static readonly int[] StepTable = new int[]
        {
            7, 8, 9, 10, 11, 12, 13, 14, 16, 17,
            19, 21, 23, 25, 28, 31, 34, 37, 41, 45,
            50, 55, 60, 66, 73, 80, 88, 97, 107, 118,
            130, 143, 157, 173, 190, 209, 230, 253, 279, 307,
            337, 371, 408, 449, 494, 544, 598, 658, 724, 796,
            876, 963, 1060, 1166, 1282, 1411, 1552, 1707, 1878, 2066,
            2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871, 5358,
            5894, 6484, 7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899,
            15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767
        };

        // Index table for step adjustments
        private static readonly int[] IndexTable = new int[]
        {
            -1, -1, -1, -1, 2, 4, 6, 8,
            -1, -1, -1, -1, 2, 4, 6, 8
        };

        /// <summary>
        /// Encode a single mono channel
        /// </summary>
        public static void EncodeMonoChannel(in ReadOnlySpan<float> monoInput, ref BlobBuilderArray<byte> output, int sampleRate = 44100)
        {
            var encoderState = new IMAState
            {
                previousSample = 0,
                stepIndex = 0
            };

            int outputIndex = 0;
            byte currentByte = 0;
            bool isUpperNibble = true;

            for (int i = 0; i < monoInput.Length; i++)
            {
                // Convert float to 16-bit PCM
                int sample = (int)math.clamp(monoInput[i] * 32767f, -32768f, 32767f);

                byte adpcmSample = EncodeIntToADPCMSample(sample, ref encoderState, sampleRate);

                if (isUpperNibble)
                {
                    currentByte = (byte)(adpcmSample << 4);
                }
                else
                {
                    currentByte |= (byte)(adpcmSample & 0x0F);
                    if (outputIndex < output.Length)
                        output[outputIndex++] = currentByte;
                    currentByte = 0;
                }

                isUpperNibble = !isUpperNibble;
            }

            // Handle remaining nibble
            if (!isUpperNibble && outputIndex < output.Length)
            {
                output[outputIndex] = currentByte;
            }
        }

        /// <summary>
        /// Decode a single mono channel
        /// </summary>
        public static void DecodeMonoChannel(in ReadOnlySpan<byte> input, ref Span<float> output,
                                           int sampleRate = 44100, int startSample = 0)
        {
            var decoderState = new IMAState
            {
                previousSample = 0,
                stepIndex = 0
            };

            if (startSample > 0)
            {
                SeekToSample(input, ref decoderState, startSample, sampleRate);
            }

            int inputIndex = startSample / 2; // 2 samples per byte
            bool isUpperNibble = (startSample % 2) == 0;

            for (int i = 0; i < output.Length; i++)
            {
                byte adpcmSample;
                if (inputIndex < input.Length)
                {
                    if (isUpperNibble)
                    {
                        adpcmSample = (byte)((input[inputIndex] >> 4) & 0x0F);
                    }
                    else
                    {
                        adpcmSample = (byte)(input[inputIndex] & 0x0F);
                        inputIndex++;
                    }
                }
                else
                {
                    adpcmSample = 0;
                }

                int pcmSample = DecodeSample(adpcmSample, ref decoderState, sampleRate);
                output[i] = math.clamp(pcmSample / 32768f, -1f, 1f);

                isUpperNibble = !isUpperNibble;
            }
        }

        /// <summary>
        /// Encode stereo audio by processing each channel separately
        /// </summary>
        public static void EncodeStereoChannels(in ReadOnlySpan<float> leftChannel, in ReadOnlySpan<float> rightChannel,
                                              ref BlobBuilderArray<byte> leftOutput, ref BlobBuilderArray<byte> rightOutput,
                                              int sampleRate = 44100)
        {
            // Encode each channel independently
            EncodeMonoChannel(leftChannel, ref leftOutput, sampleRate);
            EncodeMonoChannel(rightChannel, ref rightOutput, sampleRate);
        }

        /// <summary>
        /// Decode stereo audio by processing each channel separately
        /// </summary>
        public static void DecodeStereoChannels(in NativeArray<byte> leftInput, in NativeArray<byte> rightInput,
                                              ref Span<float> leftOutput, ref Span<float> rightOutput,
                                              int sampleRate = 44100, int startSample = 0)
        {
            // Decode each channel independently
            DecodeMonoChannel(leftInput, ref leftOutput, sampleRate, startSample);
            DecodeMonoChannel(rightInput, ref rightOutput, sampleRate, startSample);
        }

        /// <summary>
        /// Split interleaved stereo samples into separate channel arrays
        /// </summary>
        [BurstCompile]
        public static void SplitStereoSamples(in NativeArray<float> interleavedSamples,
                                            ref NativeArray<float> leftChannel, ref NativeArray<float> rightChannel)
        {
            for (int i = 0; i < interleavedSamples.Length; i += 2)
            {
                int channelIndex = i / 2;
                if (channelIndex < leftChannel.Length)
                {
                    leftChannel[channelIndex] = interleavedSamples[i];
                    if (i + 1 < interleavedSamples.Length && channelIndex < rightChannel.Length)
                        rightChannel[channelIndex] = interleavedSamples[i + 1];
                }
            }
        }

        /// <summary>
        /// Combine separate channel arrays into interleaved stereo samples
        /// </summary>
        [BurstCompile]
        public static void CombineStereoSamples(in NativeArray<float> leftChannel, in NativeArray<float> rightChannel,
                                              ref NativeArray<float> interleavedSamples)
        {
            for (int i = 0; i < leftChannel.Length && i < rightChannel.Length; i++)
            {
                int interleavedIndex = i * 2;
                if (interleavedIndex + 1 < interleavedSamples.Length)
                {
                    interleavedSamples[interleavedIndex] = leftChannel[i];
                    interleavedSamples[interleavedIndex + 1] = rightChannel[i];
                }
            }
        }


        // Private helper methods (same as original implementation)
        [BurstCompile]
        private static byte EncodeIntToADPCMSample(int sample, ref IMAState state, int sampleRate)
        {
            int diff = sample - state.previousSample;
            int step = StepTable[state.stepIndex];

            byte code = 0;

            if (diff < 0)
            {
                code = 8;
                diff = -diff;
            }

            int tempStep = step;
            if (diff >= tempStep)
            {
                code |= 4;
                diff -= tempStep;
            }

            tempStep >>= 1;
            if (diff >= tempStep)
            {
                code |= 2;
                diff -= tempStep;
            }

            tempStep >>= 1;
            if (diff >= tempStep)
            {
                code |= 1;
            }

            // Update state
            int deltaValue = 0;
            if ((code & 4) != 0) deltaValue += step;
            if ((code & 2) != 0) deltaValue += step >> 1;
            if ((code & 1) != 0) deltaValue += step >> 2;
            deltaValue += step >> 3;

            if ((code & 8) != 0)
                deltaValue = -deltaValue;

            state.previousSample = math.clamp(state.previousSample + deltaValue, -32768, 32767);
            state.stepIndex = math.clamp(state.stepIndex + IndexTable[code], 0, StepTable.Length - 1);

            return code;
        }

        [BurstCompile]
        private static int DecodeSample(byte code, ref IMAState state, int sampleRate)
        {
            int step = StepTable[state.stepIndex];
            int deltaValue = 0;

            if ((code & 4) != 0) deltaValue += step;
            if ((code & 2) != 0) deltaValue += step >> 1;
            if ((code & 1) != 0) deltaValue += step >> 2;
            deltaValue += step >> 3;

            if ((code & 8) != 0)
                deltaValue = -deltaValue;

            state.previousSample = math.clamp(state.previousSample + deltaValue, -32768, 32767);
            state.stepIndex = math.clamp(state.stepIndex + IndexTable[code], 0, StepTable.Length - 1);

            return state.previousSample;
        }

        private static void SeekToSample(in ReadOnlySpan<byte> input, ref IMAState state,
                                           int targetSample, int sampleRate)
        {
            int inputIndex = 0;
            bool isUpperNibble = true;

            for (int sample = 0; sample < targetSample; sample++)
            {
                byte adpcmSample;
                if (inputIndex < input.Length)
                {
                    if (isUpperNibble)
                    {
                        adpcmSample = (byte)((input[inputIndex] >> 4) & 0x0F);
                    }
                    else
                    {
                        adpcmSample = (byte)(input[inputIndex] & 0x0F);
                        inputIndex++;
                    }
                }
                else
                {
                    adpcmSample = 0;
                }

                DecodeSample(adpcmSample, ref state, sampleRate);
                isUpperNibble = !isUpperNibble;
            }
        }

        [BurstCompile]
        private static int GetOptimalInitialStepIndex(int sampleRate)
        {
            if (sampleRate >= 88200) return 2;
            if (sampleRate >= 48000) return 1;
            if (sampleRate >= 44100) return 0;
            if (sampleRate >= 22050) return 0;
            if (sampleRate >= 11025) return 1;
            return 2;
        }

        [BurstCompile]
        private static int ApplySampleRateAdaptation(int originalIndexChange, int sampleRate)
        {
            if (sampleRate >= 96000)
            {
                if (originalIndexChange > 0) return originalIndexChange + 1;
                return originalIndexChange;
            }
            else if (sampleRate <= 8000)
            {
                if (originalIndexChange > 2) return originalIndexChange - 1;
                if (originalIndexChange < -2) return originalIndexChange + 1;
                return originalIndexChange;
            }

            return originalIndexChange;
        }


        /// <summary>
        /// Accumulate signal and noise powers
        /// </summary>
        public static (double signal, double noise) AccumulateSignalNoisePower(ReadOnlySpan<float> original, ReadOnlySpan<float> decoded)
        {
            if (original.Length != decoded.Length)
            {
                Debug.LogWarning($"Array lengths don't match: {original.Length} vs {decoded.Length}");
                return (0, 0);
            }

            double signalPower = 0;
            double noisePower = 0;

            for (int i = 0; i < original.Length; i++)
            {
                signalPower += original[i] * original[i];
                double error = original[i] - decoded[i];
                noisePower += error * error;
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
                Debug.LogWarning($"No signal");
                return 0;
            }

            if (noisePower == 0) return float.PositiveInfinity; // Perfect match

            double snr = 10 * math.log10(signalPower / noisePower);
            return (float)snr;
        }

        /// <summary>
        /// Calculate Signal-to-Noise Ratio between original and decoded audio
        /// </summary>
        public static float CalculateSignalToNoiseRatio(ReadOnlySpan<float> original, ReadOnlySpan<float> decoded)
        {
            var powers = AccumulateSignalNoisePower(original, decoded);
            float snr = CalculateSignalToNoiseRatio(powers.signal, powers.noise);
            return snr;
        }
    }


    [System.Serializable]
    public struct IMAState
    {
        public int previousSample;
        public int stepIndex;
    }
}
