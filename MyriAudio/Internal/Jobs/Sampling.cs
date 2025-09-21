using System;
using Latios.Unsafe;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Myri
{
    internal static class Sampling
    {
        const double ITD_TIME = 0.0007;

        [BurstCompile]
        public struct SampleJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeStream.Reader  capturedSources;
            [ReadOnly] public NativeStream.Reader  channelStreams;
            [ReadOnly] public NativeReference<int> audioFrame;

            [NativeDisableParallelForRestriction] public NativeArray<float> outputSamplesMegaBuffer;

            public int sampleRate;
            public int samplesPerFrame;

            public unsafe void Execute(int channelIndex)
            {
                ulong samplesPlayed     = (ulong)samplesPerFrame * (ulong)audioFrame.Value;
                var   samplesPerChannel = outputSamplesMegaBuffer.Length / channelStreams.ForEachCount;
                var   outputSamples     = outputSamplesMegaBuffer.GetSubArray(channelIndex * samplesPerChannel, samplesPerChannel);

                using var tsa = ThreadStackAllocator.GetAllocator();

                // Sanity check because we are using pointers into this container.
                capturedSources.BeginForEachIndex(0);

                var sourceCount = channelStreams.BeginForEachIndex(channelIndex);
                for (int sourceIndex = 0; sourceIndex < sourceCount; sourceIndex++)
                {
                    var     streamSource         = channelStreams.Read<ChannelStreamSource>();
                    var     clip                 = *(AudioSourceClip*)streamSource.sourceDataPtr;
                    ref var blob                 = ref clip.m_clip.Value;
                    double  sampleRateMultiplier = 1.0;
                    if (streamSource.sourceHeader.HasFlag(CapturedSourceHeader.Features.SampleRateMultiplier))
                    {
                        var offset           = streamSource.sourceDataPtr + CollectionHelper.Align(UnsafeUtility.SizeOf<AudioSourceClip>(), 8);
                        sampleRateMultiplier = *(double*)offset;
                    }

                    double itdMaxOffset        = blob.sampleRate * sampleRateMultiplier * ITD_TIME;
                    double clipSampleStride    = blob.sampleRate * sampleRateMultiplier / sampleRate;
                    double clipSamplesPerFrame = clipSampleStride * samplesPerFrame;

                    double itdOffset = math.lerp(0, -itdMaxOffset, streamSource.itdIndex / (double)(streamSource.itdCount - 1));
                    itdOffset        =
                        math.select(itdOffset, math.lerp(-itdMaxOffset, 0, streamSource.itdIndex / (double)(streamSource.itdCount - 1)), streamSource.isRightChannel);
                    itdOffset = math.select(itdOffset, 0, streamSource.itdCount == 1);

                    bool useRightChannel = streamSource.isRightChannel && blob.isStereo;

                    if (clip.looping)
                    {
                        if (blob.sampleRate == sampleRate && sampleRateMultiplier == 1.0)
                        {
                            int clipStart = (int)((samplesPlayed + (ulong)clip.m_loopOffset) % (ulong)blob.sampleCountPerChannel) + (int)math.round(itdOffset);
                            SampleMatchedRateLooped(tsa, ref blob, clipStart, useRightChannel, streamSource.sourceHeader.volume, outputSamples.AsSpan());
                        }
                        else
                        {
                            double samplesPlayedInSourceSamples = samplesPlayed * clipSampleStride;
                            double clipStart                    = (samplesPlayedInSourceSamples + clip.m_loopOffset + itdOffset) % blob.sampleCountPerChannel;
                            SampleMismatchedRateLooped(tsa,
                                                       ref blob,
                                                       clipStart,
                                                       clipSampleStride,
                                                       useRightChannel,
                                                       streamSource.sourceHeader.volume,
                                                       outputSamples.AsSpan());
                        }
                    }
                    else
                    {
                        int jumpFrames = audioFrame.Value - clip.m_spawnedAudioFrame;

                        if (blob.sampleRate == sampleRate && sampleRateMultiplier == 1.0)
                        {
                            int clipStart = jumpFrames * samplesPerFrame + (int)math.round(itdOffset);
                            SampleMatchedRateOneshot(tsa, ref blob, clipStart, useRightChannel, streamSource.sourceHeader.volume, outputSamples.AsSpan());
                        }
                        else
                        {
                            double clipStart = jumpFrames * clipSamplesPerFrame + itdOffset;
                            SampleMismatchedRateOneshot(tsa,
                                                        ref blob,
                                                        clipStart,
                                                        clipSampleStride,
                                                        useRightChannel,
                                                        streamSource.sourceHeader.volume,
                                                        outputSamples.AsSpan());
                        }
                    }
                }
                channelStreams.EndForEachIndex();
            }

            void SampleMatchedRateLooped(ThreadStackAllocator tsa, ref AudioClipBlob clip, int clipStart, bool isRightChannel, float weight, Span<float> output)
            {
                while (clipStart < 0)
                    clipStart += clip.sampleCountPerChannel;
                clipStart     %= clip.sampleCountPerChannel;

                if (clipStart + output.Length >= clip.sampleCountPerChannel)
                {
                    if (output.Length * 2 >= clip.sampleCountPerChannel)
                    {
                        // Very short clip. Just grab the whole thing.
                        var context = new CodecContext
                        {
                            sampleRate           = sampleRate,
                            threadStackAllocator = tsa.CreateChildAllocator()
                        };
                        var input = CodecDispatch.DecodeChannel(clip.codec, ref clip.encodedSamples, isRightChannel, 0, clip.sampleCountPerChannel, ref context);
                        for (int i = 0; i < output.Length; i++)
                        {
                            int index = (clipStart + i) % clip.sampleCountPerChannel;
                            output[i] = input[index] * weight;
                        }
                        context.threadStackAllocator.Dispose();
                        return;
                    }

                    var firstHalfSampleCount = clip.sampleCountPerChannel - clipStart;
                    var firstHalfOutput      = output.Slice(0, firstHalfSampleCount);
                    SampleMatchedRateOneshot(tsa, ref clip, clipStart, isRightChannel, weight, firstHalfOutput);
                    var secondHalfSampleCount = output.Length - firstHalfSampleCount;
                    var secondHalfOutput      = output.Slice(firstHalfSampleCount, secondHalfSampleCount);
                    SampleMatchedRateOneshot(tsa, ref clip, 0,         isRightChannel, weight, secondHalfOutput);
                }
                else
                {
                    SampleMatchedRateOneshot(tsa, ref clip, clipStart, isRightChannel, weight, output);
                }
            }

            void SampleMismatchedRateLooped(ThreadStackAllocator tsa, ref AudioClipBlob clip, double clipStart, double clipSampleStride, bool isRightChannel, float weight,
                                            Span<float> output)
            {
                var clipLengthInOutputSamples = clip.sampleCountPerChannel * clipSampleStride;
                while (clipStart < 0.0)
                {
                    clipStart += clipLengthInOutputSamples;
                }
                clipStart %= clipLengthInOutputSamples;
                if (clipStart + output.Length * clipSampleStride + 1 < clipLengthInOutputSamples)
                {
                    // No wrapping.
                    SampleMismatchedRateOneshot(tsa, ref clip, clipStart, clipSampleStride, isRightChannel, weight, output);
                    return;
                }

                // Wrapping is tricky. Creating a temporary buffer to fill with matched rate sampling, so that we have a contiguous array of samples to interpolate.
                var       firstInputSample = (int)clipStart;
                var       lastInputSample  = (int)math.ceil(clipStart + output.Length * clipSampleStride);
                var       inputSampleCount = lastInputSample - firstInputSample + 2;  // Give us a little padding
                using var allocator        = tsa.CreateChildAllocator();
                var       unwrappedBuffer  = allocator.AllocateAsSpan<float>(inputSampleCount);
                SampleMatchedRateLooped(allocator, ref clip, firstInputSample, isRightChannel, weight, unwrappedBuffer);

                clipStart -= firstInputSample;

                for (int i = 0; i < output.Length; i++)
                {
                    double pos          = clipStart + clipSampleStride * i;
                    int    posLeft      = (int)pos;
                    int    posRight     = posLeft + 1;
                    float  leftSample   = unwrappedBuffer[posLeft];
                    float  rightSample  = unwrappedBuffer[posRight];
                    output[i]          += math.lerp(leftSample, rightSample, (float)math.frac(pos));
                }
            }

            void SampleMatchedRateOneshot(ThreadStackAllocator tsa, ref AudioClipBlob clip, int clipStart, bool isRightChannel, float weight, Span<float> output)
            {
                int outputStartIndex       = math.max(-clipStart, 0);
                int remainingClipSamples   = clip.sampleCountPerChannel - clipStart;
                int remainingOutputSamples = math.min(output.Length, math.select(0, remainingClipSamples, remainingClipSamples > 0));
                int inputSampleCount       = remainingOutputSamples - outputStartIndex;
                int inputSampleStart       = math.max(clipStart, 0);
                var context                = new CodecContext
                {
                    sampleRate           = sampleRate,
                    threadStackAllocator = tsa.CreateChildAllocator()
                };

                var input = CodecDispatch.DecodeChannel(clip.codec, ref clip.encodedSamples, isRightChannel, inputSampleStart, inputSampleCount, ref context);
                for (int i = 0; i < inputSampleCount; i++)
                {
                    output[i + outputStartIndex] += input[i] * weight;
                }
                context.threadStackAllocator.Dispose();
            }

            void SampleMismatchedRateOneshot(ThreadStackAllocator tsa,
                                             ref AudioClipBlob clip,
                                             double clipStart,
                                             double clipSampleStride,
                                             bool isRightChannel,
                                             float weight,
                                             Span<float>          output)
            {
                int outputStartIndex = 0;
                while (clipStart < 0.0 && outputStartIndex < output.Length)
                {
                    clipStart += clipSampleStride;
                    outputStartIndex++;
                }
                if (outputStartIndex >= output.Length || clipStart >= clip.sampleCountPerChannel)
                {
                    return;
                }
                // We can't afford to go past the last sample.
                var inputSamplesRemaining  = (clip.sampleCountPerChannel - 1 - clipStart) / clipSampleStride;
                var outputSamplesRemaining = output.Length - outputStartIndex;
                var sampleCount            = math.min(outputSamplesRemaining, (int)inputSamplesRemaining);
                var inputSampleStart       = (int)clipStart;
                var inputSampleEnd         = (int)math.ceil(clipStart + clipSampleStride * (sampleCount - 1));
                if (inputSampleEnd >= clip.sampleCountPerChannel)
                {
                    // Floating point precision wasn't quite clean. Remove a sample to get us back in range.
                    inputSampleEnd--;
                    sampleCount--;
                }
                var inputSampleCount = inputSampleEnd - inputSampleStart + 1;
                var context          = new CodecContext
                {
                    sampleRate           = sampleRate,
                    threadStackAllocator = tsa.CreateChildAllocator()
                };
                var input  = CodecDispatch.DecodeChannel(clip.codec, ref clip.encodedSamples, isRightChannel, inputSampleStart, inputSampleCount, ref context);
                clipStart -= inputSampleStart;
                for (int i = 0; i < sampleCount; i++)
                {
                    double pos                    = clipStart + clipSampleStride * i;
                    var    posLeft                = (int)pos;
                    var    posRight               = posLeft + 1;
                    var    leftSample             = input[posLeft];
                    var    rightSample            = input[posRight];
                    output[outputStartIndex + i] += math.lerp(leftSample, rightSample, (float)math.frac(pos)) * weight;
                }
                context.threadStackAllocator.Dispose();
            }
        }
    }
}

