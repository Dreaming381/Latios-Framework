using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

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

        private struct IMAState
        {
            public int previousSample;
            public int stepIndex;
        }

        /// <summary>
        /// Encodes PCM audio data to ADPCM format
        /// </summary>
        [BurstCompile]
        public static void Encode(in NativeArray<float> input, ref NativeArray<byte> output,
                                  int channels = 1)
        {
            int inputLength = input.Length;
            int outputLength = (inputLength + 1) / 2; // 4-bit per sample

            // Initialize encoder state per channel
            var encoderStates = new NativeArray<IMAState>(channels, Allocator.Temp);
            for (int c = 0; c < channels; c++)
            {
                encoderStates[c] = new IMAState
                {
                    previousSample = 0,
                    stepIndex = 0
                };
            }

            int outputIndex = 0;
            byte currentByte = 0;
            bool isUpperNibble = true;

            for (int i = 0; i < inputLength; i++)
            {
                int channel = i % channels;
                var state = encoderStates[channel];

                // Convert float to 16-bit PCM
                int sample = (int)math.clamp(input[i] * 32767f, -32768f, 32767f);

                // Encode sample
                byte adpcmSample = EncodeSampleIntToADPCM(sample, ref state);

                // Pack nibbles into bytes
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
                encoderStates[channel] = state;
            }

            // Handle remaining nibble
            if (!isUpperNibble && outputIndex < output.Length)
            {
                output[outputIndex] = currentByte;
            }

            encoderStates.Dispose();
        }

        [BurstCompile]
        private static byte EncodeSampleIntToADPCM(int sample, ref IMAState state)
        {
            int diff = sample - state.previousSample;
            int step = StepTable[state.stepIndex];

            byte code = 0;

            // Sign bit
            if (diff < 0)
            {
                code = 8;
                diff = -diff;
            }

            // Quantize difference
            int tempStepIndex = step;
            if (diff >= tempStepIndex)
            {
                code |= 4;
                diff -= tempStepIndex;
            }

            tempStepIndex >>= 1;
            if (diff >= tempStepIndex)
            {
                code |= 2;
                diff -= tempStepIndex;
            }

            tempStepIndex >>= 1;
            if (diff >= tempStepIndex)
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

        /// <summary>
        /// Decodes ADPCM data to PCM format with seek support
        /// </summary>
        [BurstCompile]
        public static void Decode(in NativeArray<byte> input, ref NativeArray<float> output,
                                  int channels = 1, int startSample = 0)
        {
            // Initialize decoder state per channel
            var decoderStates = new NativeArray<IMAState>(channels, Allocator.Temp);
            for (int c = 0; c < channels; c++)
            {
                decoderStates[c] = new IMAState
                {
                    previousSample = 0,
                    stepIndex = 0
                };
            }

            // Seek to start position if needed
            if (startSample > 0)
            {
                SeekToSample(input, ref decoderStates, startSample, channels);
            }

            int inputIndex = startSample / 2; // 2 samples per byte
            bool isUpperNibble = (startSample % 2) == 0;

            for (int i = 0; i < output.Length; i++)
            {
                int channel = i % channels;
                var state = decoderStates[channel];

                // Extract nibble
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

                // Decode sample
                int pcmSample = DecodeSample(adpcmSample, ref state);

                // Convert to float (-1.0 to 1.0)
                output[i] = math.clamp(pcmSample / 32768f, -1f, 1f);

                isUpperNibble = !isUpperNibble;
                decoderStates[channel] = state;
            }

            decoderStates.Dispose();
        }


        [BurstCompile]
        private static int DecodeSample(byte code, ref IMAState state)
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


        [BurstCompile]
        private static void SeekToSample(in NativeArray<byte> input, ref NativeArray<IMAState> states,
                                       int targetSample, int channels)
        {
            // Fast seek by decoding from beginning to target
            // For better performance, should implement block-based seeking
            var tempStates = new NativeArray<IMAState>(channels, Allocator.Temp);

            for (int c = 0; c < channels; c++)
            {
                tempStates[c] = new IMAState { previousSample = 0, stepIndex = 0 };
            }

            int inputIndex = 0;
            bool isUpperNibble = true;

            for (int sample = 0; sample < targetSample; sample++)
            {
                int channel = sample % channels;
                var state = tempStates[channel];

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

                DecodeSample(adpcmSample, ref state);
                isUpperNibble = !isUpperNibble;
                tempStates[channel] = state;
            }

            for (int c = 0; c < channels; c++)
            {
                states[c] = tempStates[c];
            }

            tempStates.Dispose();
        }
    }

}
