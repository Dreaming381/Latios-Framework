using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Myri.AudioEcsBuiltin
{
    public struct NewOrChangedListenerMessage
    {
        public Entity                           entity;
        public AudioListener                    audioListener;
        public PipeSpan<AudioListenerChannelID> channels;
        public bool                             hasChannels;
    }

    public struct RemovedListenersMessage
    {
        public PipeSpan<Entity> formerListenerEntities;
    }
}

