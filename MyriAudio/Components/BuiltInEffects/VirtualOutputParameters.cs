using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Myri
{
    // Make public on release
    internal struct VirtualOutputParameters : IEffectParameters<DSP.VirtualOutputEffect, VirtualOutputParameters>
    {
        public float volume;
    }
}

