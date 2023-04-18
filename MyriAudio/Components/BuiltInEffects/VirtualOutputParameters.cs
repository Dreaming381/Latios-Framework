using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Myri
{
    public struct VirtualOutputParameters : IEffectParameters<DSP.VirtualOutputEffect, VirtualOutputParameters>
    {
        public float volume;
    }
}

