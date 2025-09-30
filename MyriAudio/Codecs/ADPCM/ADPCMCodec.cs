using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Myri
{
    internal static class ADPCMCodec
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
        public static void EncodeChannel(in ReadOnlySpan<float> monoInput, ref BlobBuilderArray<byte> output)
        {
            var encoderState = new IMAState
            {
                previousSample = 0,
                stepIndex      = 0
            };

            int  outputIndex   = 0;
            byte currentByte   = 0;
            bool isUpperNibble = true;

            for (int i = 0; i < monoInput.Length; i++)
            {
                // Convert float to 16-bit PCM
                int sample = (int)math.clamp(monoInput[i] * 32767f, -32768f, 32767f);

                byte adpcmSample = EncodeIntToADPCMSample(sample, ref encoderState);

                if (isUpperNibble)
                {
                    currentByte = (byte)(adpcmSample << 4);
                }
                else
                {
                    currentByte |= (byte)(adpcmSample & 0x0F);
                    if (outputIndex < output.Length)
                        output[outputIndex++] = currentByte;
                    currentByte               = 0;
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
        public static void DecodeChannel(in ReadOnlySpan<byte> input, ref Span<float> output, int startSample, IMAState seekIMAState = default, int seekIMAStateIndex = default)
        {
            var decoderState = new IMAState
            {
                previousSample = 0,
                stepIndex      = 0
            };

            if (startSample > 0)
            {
                SeekToSample(input, ref decoderState, startSample, seekIMAState, seekIMAStateIndex);
                //SeekToSample(input, ref decoderState, startSample, default, default);
            }

            int  inputIndex    = startSample / 2;  // 2 samples per byte
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

                int pcmSample = DecodeSample(adpcmSample, ref decoderState);
                output[i]     = math.clamp(pcmSample / 32768f, -1f, 1f);

                isUpperNibble = !isUpperNibble;
            }
        }

        /// <summary>
        /// Decode a single mono channel entirely with IMA states (used for seek tables and SNR analysis)
        /// </summary>
        public static void DecodeChannelWithIMAStates(in ReadOnlySpan<byte> input, ref Span<float> output, ref Span<IMAState> ima)
        {
            var decoderState = new IMAState
            {
                previousSample = 0,
                stepIndex      = 0
            };

            int  inputIndex    = 0;
            bool isUpperNibble = true;

            for (int i = 0; i < ima.Length; i++)
            {
                ima[i] = decoderState;
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

                int pcmSample = DecodeSample(adpcmSample, ref decoderState);
                output[i]     = math.clamp(pcmSample / 32768f, -1f, 1f);

                isUpperNibble = !isUpperNibble;
            }
        }

        // Private helper methods (same as original implementation)
        private static byte EncodeIntToADPCMSample(int sample, ref IMAState state)
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
            if ((code & 4) != 0)
                deltaValue += step;
            if ((code & 2) != 0)
                deltaValue += step >> 1;
            if ((code & 1) != 0)
                deltaValue += step >> 2;
            deltaValue     += step >> 3;

            if ((code & 8) != 0)
                deltaValue = -deltaValue;

            state.previousSample = math.clamp(state.previousSample + deltaValue, -32768, 32767);
            state.stepIndex      = math.clamp(state.stepIndex + IndexTable[code], 0, StepTable.Length - 1);

            return code;
        }

        private static int DecodeSample(byte code, ref IMAState state)
        {
            int step       = StepTable[state.stepIndex];
            int deltaValue = 0;

            if ((code & 4) != 0)
                deltaValue += step;
            if ((code & 2) != 0)
                deltaValue += step >> 1;
            if ((code & 1) != 0)
                deltaValue += step >> 2;
            deltaValue     += step >> 3;

            if ((code & 8) != 0)
                deltaValue = -deltaValue;

            state.previousSample = math.clamp(state.previousSample + deltaValue, -32768, 32767);
            state.stepIndex      = math.clamp(state.stepIndex + IndexTable[code], 0, StepTable.Length - 1);

            return state.previousSample;
        }

        private static void SeekToSample(in ReadOnlySpan<byte> input, ref IMAState state, int targetSample, IMAState seekIMAState, int seekIMAStateIndex)
        {
            int  inputIndex    = seekIMAStateIndex / 2;
            bool isUpperNibble = true;

            state = seekIMAState;

            for (int sample = seekIMAStateIndex; sample < targetSample; sample++)
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

                DecodeSample(adpcmSample, ref state);
                isUpperNibble = !isUpperNibble;
            }
        }

        public struct IMAState
        {
            public int previousSample;
            public int stepIndex;
        }
    }
}

