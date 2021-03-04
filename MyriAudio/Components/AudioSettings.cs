using Unity.Entities;

namespace Latios.Myri
{
    public struct AudioSettings : IComponentData
    {
        public int  audioFramesPerUpdate;
        public int  audioSubframesPerFrame;
        public bool logWarningIfBuffersAreStarved;
    }
}

