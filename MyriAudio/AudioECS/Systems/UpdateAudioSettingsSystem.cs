using Unity.Mathematics;
using Unity.Profiling;

namespace Latios.Myri.AudioEcsBuiltin
{
    public static class UpdateAudioSettingsSystem
    {
        static readonly ProfilerMarker kMarker = new ProfilerMarker("UpdateAudioSettingsSystem");

        public static void OnInitialize(ref IAudioEcsSystemRunner.AudioFormatChangedContext context)
        {
            context.auxWorld.AddComponent(context.worldBlackboardEntity, AudioSettings.kDefault);
        }

        public static void OnUpdate(ref IAudioEcsSystemRunner.UpdateContext context)
        {
            using var profilerMarker = kMarker.Auto();

            if (!context.auxWorld.TryGetComponent<AudioSettings>(context.worldBlackboardEntity, out var settings))
                return;
            foreach (var update in context.visualFrameUpdates)
            {
                foreach (var message in update.pipeReader.Each<AudioSettings>())
                    settings.aux = message;
            }
        }
    }
}

