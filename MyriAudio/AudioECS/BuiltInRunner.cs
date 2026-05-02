using Latios.Unsafe;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Myri.AudioEcsBuiltin
{
    public partial struct BuiltInRunner : IVInterface, IAudioEcsSystemRunner
    {
        public void OnInitialize(ref IAudioEcsSystemRunner.AudioFormatChangedContext context)
        {
            UpdateAudioSettingsSystem.OnInitialize(ref context);

            StereoOutputBrickwallLimiterSystem.OnInitialize(ref context);
            WriteStereoOutputSystem.OnInitialize(ref context);
        }

        public void OnAudioFormatChanged(ref IAudioEcsSystemRunner.AudioFormatChangedContext context)
        {
            WriteStereoOutputSystem.OnAudioFormatChanged(ref context);
        }

        public void OnUpdate(ref IAudioEcsSystemRunner.UpdateContext context)
        {
            UpdateAudioSettingsSystem.OnUpdate(ref context);
            UpdateListenersSystem.OnUpdate(ref context);

            ListenerBrickWallLimitersSystem.OnUpdate(ref context);
            MixListenersToStereoOutputSystem.OnUpdate(ref context);
            StereoOutputBrickwallLimiterSystem.OnUpdate(ref context);
            WriteStereoOutputSystem.OnUpdate(ref context);
        }

        public void OnShutdown(ref IAudioEcsSystemRunner.ShutdownContext context)
        {
        }
    }
}

