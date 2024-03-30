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

    internal interface IResourceComponent : IComponentData
    {
    }

    internal interface IResourceBuffer : IBufferElementData
    {
    }

    internal interface IFeedbackComponent : IComponentData, IEnableableComponent
    {
    }

    internal interface IFeedbackBuffer : IBufferElementData, IEnableableComponent
    {
    }

    /// <summary>
    /// Provides more granular control of sending to the DSP thread than change filters
    /// </summary>
    internal struct DspSubmitFlag : IComponentData, IEnableableComponent { }

    internal struct EffectStackElement : IBufferElementData, IEnableableComponent
    {
        public Entity effectEntity;
    }
}

