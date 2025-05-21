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

                    if (clip.looping)
                    {
                        if (blob.sampleRate == sampleRate && sampleRateMultiplier == 1.0)
                        {
                            int clipStart = (int)((samplesPlayed + (ulong)clip.m_loopOffset) % (ulong)blob.samplesLeftOrMono.Length) + (int)math.round(itdOffset);
                            SampleMatchedRateLooped(ref blob, clipStart, streamSource.isRightChannel, streamSource.sourceHeader.volume, outputSamples);
                        }
                        else
                        {
                            double samplesPlayedInSourceSamples = samplesPlayed * clipSampleStride;
                            double clipStart                    = (samplesPlayedInSourceSamples + clip.m_loopOffset + itdOffset) % blob.samplesLeftOrMono.Length;
                            SampleMismatchedRateLooped(ref blob, clipStart, clipSampleStride, streamSource.isRightChannel, streamSource.sourceHeader.volume, outputSamples);
                        }
                    }
                    else
                    {
                        int jumpFrames = audioFrame.Value - clip.m_spawnedAudioFrame;

                        if (blob.sampleRate == sampleRate && sampleRateMultiplier == 1.0)
                        {
                            int clipStart = jumpFrames * samplesPerFrame + (int)math.round(itdOffset);
                            SampleMatchedRateOneshot(ref blob, clipStart, streamSource.isRightChannel, streamSource.sourceHeader.volume, outputSamples);
                        }
                        else
                        {
                            double clipStart = jumpFrames * clipSamplesPerFrame + itdOffset;
                            SampleMismatchedRateOneshot(ref blob, clipStart, clipSampleStride, streamSource.isRightChannel, streamSource.sourceHeader.volume, outputSamples);
                        }
                    }
                }
                channelStreams.EndForEachIndex();
            }

            void SampleMatchedRateLooped(ref AudioClipBlob clip, int clipStart, bool isRightChannel, float weight, NativeArray<float> output)
            {
                while (clipStart < 0)
                    clipStart += clip.samplesLeftOrMono.Length;

                if (isRightChannel && clip.isStereo)
                {
                    for (int i = 0; i < output.Length; i++)
                    {
                        int index  = (clipStart + i) % clip.samplesRight.Length;
                        output[i] += clip.samplesRight[index] * weight;
                    }
                }
                else
                {
                    for (int i = 0; i < output.Length; i++)
                    {
                        int index  = (clipStart + i) % clip.samplesLeftOrMono.Length;
                        output[i] += clip.samplesLeftOrMono[index] * weight;
                    }
                }
            }

            void SampleMismatchedRateLooped(ref AudioClipBlob clip, double clipStart, double clipSampleStride, bool isRightChannel, float weight, NativeArray<float> output)
            {
                ref var clipSamples = ref (isRightChannel && clip.isStereo ? ref clip.samplesRight : ref clip.samplesLeftOrMono);

                for (int i = 0; i < output.Length; i++)
                {
                    double pos          = clipStart + clipSampleStride * i;
                    int    posLeft      = (int)pos % clipSamples.Length;
                    int    posRight     = (posLeft + 1) % clipSamples.Length;  //This handles wraparound between last sample and first sample
                    float  leftSample   = clipSamples[posLeft];
                    float  rightSample  = clipSamples[posRight];
                    output[i]          += math.lerp(leftSample, rightSample, (float)math.frac(pos)) * weight;
                }
            }

            void SampleMatchedRateOneshot(ref AudioClipBlob clip, int clipStart, bool isRightChannel, float weight, NativeArray<float> output)
            {
                int outputStartIndex     = math.max(-clipStart, 0);
                int remainingClipSamples = clip.samplesLeftOrMono.Length - clipStart;
                int remainingSamples     = math.min(output.Length, math.select(0, remainingClipSamples, remainingClipSamples > 0));

                if (isRightChannel && clip.isStereo)
                {
                    for (int i = outputStartIndex; i < remainingSamples; i++)
                    {
                        output[i] += clip.samplesRight[clipStart + i] * weight;
                    }
                }
                else
                {
                    for (int i = outputStartIndex; i < remainingSamples; i++)
                    {
                        output[i] += clip.samplesLeftOrMono[clipStart + i] * weight;
                    }
                }
            }

            void SampleMismatchedRateOneshot(ref AudioClipBlob clip, double clipStart, double clipSampleStride, bool isRightChannel, float weight, NativeArray<float> output)
            {
                ref var clipSamples = ref (isRightChannel && clip.isStereo ? ref clip.samplesRight : ref clip.samplesLeftOrMono);

                for (int i = 0; i < output.Length; i++)
                {
                    double pos          = clipStart + clipSampleStride * i;
                    int    posLeft      = (int)pos;
                    int    posRight     = posLeft + 1;
                    int    safeLeft     = math.clamp(posLeft, 0, clipSamples.Length - 1);
                    int    safeRight    = math.clamp(posRight, 0, clipSamples.Length - 1);
                    float  leftSample   = math.select(0f, clipSamples[safeLeft], posLeft == safeLeft);
                    float  rightSample  = math.select(0f, clipSamples[safeRight], posRight == safeRight);
                    output[i]          += math.lerp(leftSample, rightSample, (float)math.frac(pos)) * weight;
                }
            }
        }
    }
}

