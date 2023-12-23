using Latios.Transforms;
using Latios.Transforms.Systems;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Mimic.Mecanim.Systems
{
    /// <summary>
    /// This super system contains systems that manage the animator state machine and
    /// resulting layered bone transformations.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
    [UpdateBefore(typeof(TransformSuperSystem))]
#else
    [UpdateBefore(typeof(TransformSystemGroup))]
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

