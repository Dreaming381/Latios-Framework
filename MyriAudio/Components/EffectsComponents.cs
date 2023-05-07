using Latios.Transforms;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Myri
{
    // Make these public on release
    internal interface IEffectParameters<TEffect, TParameters> : IComponentData
        where TEffect : unmanaged, DSP.IEffect<TEffect, TParameters>
        where TParameters : unmanaged, IEffectParameters<TEffect, TParameters>
    {
    }

    internal interface ISpatialEffectParamaters<TEffect, TParameters> : IComponentData
        where TEffect : unmanaged, DSP.ISpatialEffect<TEffect, TParameters>
        where TParameters : unmanaged, ISpatialEffectParamaters<TEffect, TParameters>
    {
    }

    internal interface IListenerProperty : IComponentData
    {
    }
}

