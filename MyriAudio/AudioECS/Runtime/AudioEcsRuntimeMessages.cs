using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Myri
{
    internal struct ControlToRealtimeMessage
    {
        public int                  commandBufferId;
        public int                  retiredFeedbackId;
        public UnsafeList<MegaPipe> commandPipeList;
    }

    internal struct RealtimeToControlMessage
    {
        public int      feedbackBufferId;
        public int      retiredCommandId;
        public MegaPipe feedbackPipe;
    }

    internal struct ShutdownControlMessage
    {
        public int dummy;
    }
}

