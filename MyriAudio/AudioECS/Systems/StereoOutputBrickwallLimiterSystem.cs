using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;

namespace Latios.Myri.AudioEcsBuiltin
{
    public static class StereoOutputBrickwallLimiterSystem
    {
        static readonly ProfilerMarker kMarker = new ProfilerMarker("StereoOutputBrickwallLimiterSystem");

        public static void OnInitialize(ref IAudioEcsSystemRunner.AudioFormatChangedContext context)
        {
            if (!context.auxWorld.TryGetComponent<AudioSettings>(context.worldBlackboardEntity, out var settings))
                return;
            context.auxWorld.AddComponent(context.worldBlackboardEntity, new StereoOutputBrickwallLimiter
            {
                brickwallLimiter = new DSP.BrickwallLimiter(settings.aux.masterGain,
                                                            settings.aux.masterVolume,
                                                            settings.aux.masterLimiterDBRelaxPerSecond / context.newAudioFormat.sampleRate,
                                                            (int)math.ceil(settings.aux.masterLimiterLookaheadTime * context.newAudioFormat.sampleRate),
                                                            context.auxWorld.allocator)
            });
        }

        public static void OnUpdate(ref IAudioEcsSystemRunner.UpdateContext context)
        {
            using var profilerMarker = kMarker.Auto();

            // Get components
            if (!context.auxWorld.TryGetComponent<StereoOutputBuffers>(context.worldBlackboardEntity, out var outputBuffers))
                return;
            if (!context.auxWorld.TryGetComponent<AudioSettings>(context.worldBlackboardEntity, out var settings))
                return;
            if (!context.auxWorld.TryGetComponent<StereoOutputBrickwallLimiter>(context.worldBlackboardEntity, out var stereoOuputLimiter))
                return;

            // Apply any limiter updates or configuration changes
            var     sampleRate          = context.finalOutputBuffer.sampleRate;
            var     lookaheadSamples    = (int)math.ceil(settings.aux.masterLimiterLookaheadTime * sampleRate);
            ref var limiter             = ref stereoOuputLimiter.aux.brickwallLimiter;
            var     oldPreGain          = limiter.preGain;
            var     oldVolume           = limiter.volume;
            var     newPreGain          = settings.aux.masterGain;
            var     newVolume           = settings.aux.masterVolume;
            var     smoothRatePerSample = math.rcp(sampleRate * 0.015f);  // Small 15 millisecond smoothing
            limiter.releasePerSampleDB  = settings.aux.masterLimiterDBRelaxPerSecond * sampleRate;
            limiter.SetLookaheadSampleCount(lookaheadSamples);

            // Process buffer.
            var  left          = outputBuffers.aux.left;
            var  right         = outputBuffers.aux.right;
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

