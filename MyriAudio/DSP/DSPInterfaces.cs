using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Myri.DSP
{
    // Make these public on release
    internal interface IEffect<TEffect, TParameters>
        where TEffect : unmanaged, IEffect<TEffect, TParameters>
        where TParameters : unmanaged, IEffectParameters<TEffect, TParameters>
    {
        public void OnAwake(in EffectContext context, in TParameters parameters);
        public void OnUpdate(in EffectContext effectContext, in UpdateContext updateContext, in TParameters parameters, ref SampleFrame frame);
        public void OnDestroy(in EffectContext context);

        public bool RequireUpdateWhenCulled => false;
        public bool RequireUpdateWhenInputFrameDisconnected => false;
    }

    internal interface ISpatialEffect<TEffect, TParameters>
        where TEffect : unmanaged, ISpatialEffect<TEffect, TParameters>
        where TParameters : unmanaged, ISpatialEffectParamaters<TEffect, TParameters>
    {
        public void OnAwake(in EffectContext context, in TParameters parameters);
        public void OnCull(in EffectContext effectContext, in SpatialCullingContext cullingContext, in TParameters parameters, ref CullArray cullArray);
        public void OnUpdate(in EffectContext effectContext, in SpatialUpdateContext updateContext, in TParameters parameters, ref SampleFrame frame);
        public void OnDestroy(in EffectContext context);

        public bool RequireUpdateWhenCulled => false;
        public bool RequireUpdateWhenInputFrameDisconnected => false;
    }
}

