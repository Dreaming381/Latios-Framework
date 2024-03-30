using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Myri.InternalSourceGen
{
    internal static unsafe class EffectOperations
    {
        // Rather than reconstruct the args on the stack for every call, we reuse an instance of this struct.
        // Todo: Would it be better to do dispatches in batches of the same type?
        public struct InitDestroyOpData
        {
            public DSP.EffectContext effectContext;
            public void*             parametersPtr;
            public void*             effectPtr;
        }

        public static void InitEffect(ref InitDestroyOpData data, FunctionPointer<StaticAPI.BurstDispatchEffectDelegate> functionPtr)
        {
        }

        public static void DestroyEffect(ref InitDestroyOpData data, FunctionPointer<StaticAPI.BurstDispatchEffectDelegate> functionPtr)
        {
        }

        public static void InitSpatialEffect(ref InitDestroyOpData data, FunctionPointer<StaticAPI.BurstDispatchSpatialEffectDelegate> functionPtr)
        {
        }

        public static void DestroySpatialEffect(ref InitDestroyOpData data, FunctionPointer<StaticAPI.BurstDispatchSpatialEffectDelegate> functionPtr)
        {
        }

        public struct CullUpdateOpData
        {
            public DSP.EffectContext         effectContext;
            public DSP.UpdateContext         updateContext;
            public DSP.SpatialCullingContext spatialCullingContext;
            public DSP.CullArray             cullArray;
            public DSP.SpatialUpdateContext  spatialUpdateContext;
            public DSP.SampleFrame*          sampleFramePtr;
            public void*                     parametersPtr;
            public void*                     effectPtr;
        }

        public static void UpdateEffect(ref CullUpdateOpData data, FunctionPointer<StaticAPI.BurstDispatchEffectDelegate > functionPtr)
        {
        }

        public static void CullSpatialEffect(ref CullUpdateOpData data, FunctionPointer<StaticAPI.BurstDispatchSpatialEffectDelegate> functionPtr)
        {
        }

        public static void UpdateSpatialEffect(ref CullUpdateOpData data, FunctionPointer<StaticAPI.BurstDispatchSpatialEffectDelegate > functionPtr)
        {
        }
    }
}

