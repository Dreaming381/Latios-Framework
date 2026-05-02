using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;

namespace Latios.Myri.AudioEcsBuiltin
{
    public static class MixListenersToStereoOutputSystem
    {
        static readonly ProfilerMarker kMarker = new ProfilerMarker("MixListenersToStereoOutputSystem");

        public static void OnUpdate(ref IAudioEcsSystemRunner.UpdateContext context)
        {
            using var profilerMarker = kMarker.Auto();

            if (!context.auxWorld.TryGetComponent<StereoOutputBuffers>(context.worldBlackboardEntity, out var outputBuffers))
                return;

            outputBuffers.aux.Clear();
            var outLeft  = outputBuffers.aux.left;
            var outRight = outputBuffers.aux.right;

            foreach (var listenerMix in context.auxWorld.AllOf<ListenerStereoMix>())
            {
                if (!listenerMix.aux.hasSignal)
                    continue;
                listenerMix.aux.GetToRead(out var inLeft, out var inRight);

                for (int i = 0; i < outLeft.Length; i++)
                    outLeft[i] += inLeft[i];
                for (int i = 0; i < outRight.Length; i++)
                    outRight[i] += inRight[i];
            }
        }
    }
}

