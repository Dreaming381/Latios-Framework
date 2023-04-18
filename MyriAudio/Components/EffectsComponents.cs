using Latios.Transforms;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Myri
{
    public interface IEffectParameters<TEffect, TParameters> : IComponentData
        where TEffect : unmanaged, DSP.IEffect<TEffect, TParameters>
        where TParameters : unmanaged, IEffectParameters<TEffect, TParameters>
    {
    }

    public interface ISpatialEffectParamaters<TEffect, TParameters> : IComponentData
        where TEffect : unmanaged, DSP.ISpatialEffect<TEffect, TParameters>
        where TParameters : unmanaged, ISpatialEffectParamaters<TEffect, TParameters>
    {
    }

    public interface IListenerProperty : IComponentData
    {
    }
}

