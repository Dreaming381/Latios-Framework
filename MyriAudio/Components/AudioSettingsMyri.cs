using Unity.Entities;

namespace Latios.Myri
{
    public struct AudioSettings : IComponentData
    {
        public int  safetyAudioFrames;
        public int  audioFramesPerUpdate;
        public int  lookaheadAudioFrames;
        public bool logWarningIfBuffersAreStarved;
    }
}

