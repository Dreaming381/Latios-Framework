using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;

namespace Latios.Myri.AudioEcsBuiltin
{
    public static class UpdateListenersSystem
    {
        static readonly ProfilerMarker kMarker = new ProfilerMarker("UpdateListenersSystem");

        public static void OnUpdate(ref IAudioEcsSystemRunner.UpdateContext context)
        {
            using var profilerMarker = kMarker.Auto();

            foreach (var visualFrame in context.visualFrameUpdates)
            {
                foreach (var message in visualFrame.pipeReader.Each<RemovedListenersMessage>())
                {
                    foreach (var entity in message.formerListenerEntities)
                    {
                        context.auxWorld.RemoveComponent<ListenerStereoMix>(          entity);
                        context.auxWorld.RemoveComponent<AudioListener>(              entity);
                        context.auxWorld.RemoveComponent<ListenerBrickwallLimiter>(   entity);
                        context.auxWorld.RemoveComponent<AudioListenerChannelIDsList>(entity);
                    }
                }

                foreach (var message in visualFrame.pipeReader.Each<NewOrChangedListenerMessage>())
                {
                    if (context.auxWorld.TryGetComponent<AudioListener>(message.entity, out var listener))
                    {
                        // Existing listener with update
                        context.auxWorld.TryGetComponent<AudioListenerChannelIDsList>(message.entity, out var list);
                        listener.aux = message.audioListener;
                        list.aux.channelIDs.Clear();
                        foreach (var channel in message.channels)
                            list.aux.channelIDs.Add(channel);
                    }
                    else
                    {
                        context.auxWorld.AddComponent(message.entity, message.audioListener);
                        context.auxWorld.AddComponent(message.entity, new ListenerStereoMix(context.finalOutputBuffer.m_samplesPerChannel, context.auxWorld.allocator));
                        context.auxWorld.AddComponent(message.entity, new ListenerBrickwallLimiter
                        {
                            brickwallLimiter = new DSP.BrickwallLimiter(message.audioListener.gain,
                                                                        message.audioListener.volume,
                                                                        message.audioListener.limiterDBRelaxPerSecond / context.finalOutputBuffer.sampleRate,
                                                                        (int)math.ceil(message.audioListener.limiterLookaheadTime * context.finalOutputBuffer.sampleRate),
                                                                        context.auxWorld.allocator)
                        });
                        context.auxWorld.AddComponent(message.entity, new AudioListenerChannelIDsList(message.channels, context.auxWorld.allocator));
                    }
                }
            }

            foreach (var mix in context.auxWorld.AllOf<ListenerStereoMix>())
            {
                mix.aux.StartNewMix();
            }
        }
    }
}

