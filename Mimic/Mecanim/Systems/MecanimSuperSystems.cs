using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Mimic.Addons.Mecanim.Systems
{
    /// <summary>
    /// This super system contains systems that manage the animator state machine and
    /// resulting layered bone transformations.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
    [UpdateBefore(typeof(Latios.Transforms.Systems.TransformSuperSystem))]
#else
    [UpdateBefore(typeof(Unity.Transforms.TransformSystemGroup))]
#endif
    [DisableAutoCreation]
    public partial class MecanimSuperSystem : SuperSystem  // Todo: Make name more specific?
    {
        protected override void CreateSystems()
        {
            EnableSystemSorting = false;

            GetOrCreateAndAddUnmanagedSystem<MecanimStateMachineUpdateSystem>();
            GetOrCreateAndAddUnmanagedSystem<ApplyMecanimLayersToExposedBonesSystem>();
            GetOrCreateAndAddUnmanagedSystem<ApplyMecanimLayersToOptimizedSkeletonsSystem>();
        }
    }
}

