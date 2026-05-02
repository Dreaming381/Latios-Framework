using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;

namespace Latios.Myri.AudioEcsBuiltin
{
    public static class WriteStereoOutputSystem
    {
        static readonly ProfilerMarker kMarker = new ProfilerMarker("WriteStereoOutputSystem");

        public static void OnInitialize(ref IAudioEcsSystemRunner.AudioFormatChangedContext context)
        {
            OnAudioFormatChanged(ref context);
        }

        public static void OnAudioFormatChanged(ref IAudioEcsSystemRunner.AudioFormatChangedContext context)
        {
            if (context.newAudioFormat.channelCount != 2)
            {
                UnityEngine.Debug.LogError($"WriteStereoOutputSystem does not support the audio format with {context.newAudioFormat.channelCount} channels.");
            }
            // Todo: Move below to MixListenersToStereoSystem
            if (context.auxWorld.TryGetComponent<StereoOutputBuffers>(context.worldBlackboardEntity, out var outputBuffers))
            {
                if (outputBuffers.aux.left.Length != context.newAudioFormat.bufferFrameCount)
                {
                    outputBuffers.aux.Dispose();
                    outputBuffers.aux = new StereoOutputBuffers(context.newAudioFormat.bufferFrameCount, context.auxWorld.allocator);
                }
            }
            else
            {
                context.auxWorld.AddComponent(context.worldBlackboardEntity, new StereoOutputBuffers(context.newAudioFormat.bufferFrameCount, context.auxWorld.allocator));
            }
        }

        public static void OnUpdate(ref IAudioEcsSystemRunner.UpdateContext context)
        {
            using var profilerMarker = kMarker.Auto();

            if (context.finalOutputBuffer.channelCount != 2)
                return;

            if (!context.auxWorld.TryGetComponent<StereoOutputBuffers>(context.worldBlackboardEntity, out var outputBuffers))
                return;

            outputBuffers.aux.left.CopyTo(context.finalOutputBuffer.GetChannel(0));
            outputBuffers.aux.right.CopyTo(context.finalOutputBuffer.GetChannel(1));
        }
    }
}

