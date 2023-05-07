using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Myri
{
    // Make public on release
    internal struct BrickwallLimiterSettings
    {
        public float preGain;
        public float limitDB;
        public float releaseDBPerSample;
        public int   lookaheadSampleCount;
    }
}

