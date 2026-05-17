using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;

namespace Latios.Myri.AudioEcsBuiltin
{
    public static class ListenerBrickWallLimitersSystem
    {
        static readonly ProfilerMarker kMarker = new ProfilerMarker("ListenerBrickWallLimitersSystem");

        public static void OnUpdate(ref IAudioEcsSystemRunner.UpdateContext context)
        {
            using var profilerMarker = kMarker.Auto();

            foreach ((var entity, var settings, var listenerLimiter, var mix) in context.auxWorld.AllWith<AudioListener, ListenerBrickwallLimiter, ListenerStereoMix>())
            {
                // Apply any limiter updates or configuration changes
                var     sampleRate          = context.finalOutputBuffer.sampleRate;
                var     lookaheadSamples    = (int)math.ceil(settings.aux.limiterLookaheadTime * sampleRate);
                ref var limiter             = ref listenerLimiter.aux.brickwallLimiter;
                var     oldPreGain          = limiter.preGain;
                var     oldVolume           = limiter.volume;
                var     newPreGain          = settings.aux.gain;
                var     newVolume           = settings.aux.volume;
                var     smoothRatePerSample = math.rcp(sampleRate * 0.015f);  // Small 15 millisecond smoothing
                limiter.releasePerSampleDB  = settings.aux.limiterDBRelaxPerSecond * sampleRate;
                limiter.SetLookaheadSampleCount(lookaheadSamples);

                // Try to early-out
                if (!mix.aux.hasSignal)
                {
                    if (!limiter.HasNonZeroValueInQueue())
                    {
                        limiter.preGain = newPreGain;
                        limiter.volume  = newVolume;
                        continue;
                    }
                }

                // Process buffer.
                mix.aux.GetToAdd(out var left, out var right);
                bool stopSmoothing = false;
                for (int i = 0; i < left.Length; i++)
                {
                    if (!stopSmoothing)
                    {
                        var smoothFactor = math.min(i * smoothRatePerSample, 1f);
                        limiter.preGain  = math.lerp(oldPreGain, newPreGain, smoothFactor);
                        limiter.volume   = math.lerp(oldVolume, newVolume, smoothFactor);
                        stopSmoothing    = smoothFactor >= 1f;
                    }

                    limiter.ProcessSample(left[i], right[i], out var leftOut, out var rightOut);
                    left[i]  = leftOut;
                    right[i] = rightOut;
                }
            }
        }
    }
}

