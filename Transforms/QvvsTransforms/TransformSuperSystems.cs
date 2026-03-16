#if !LATIOS_TRANSFORMS_UNITY
using Latios.Systems;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Transforms.Systems
{
    #region Injectable
    /// <summary>
    /// This group is updated inside the PostSyncPointGroup and performs and initializes motion history after new
    /// hierarchies have spawned, modifying any default-initialized motion history components.
    /// You can use [UpdateInGroup(typeof(MotionHistoryInitializeSuperSystem))] to add systems that should be updated
    /// at this time.
    /// </summary>
    [DisableAutoCreation]
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(PostSyncPointGroup))]
    public partial class MotionHistoryInitializeSuperSystem : SuperSystem
    {
        protected override void CreateSystems()
        {
            EnableSystemSorting = true;

            GetOrCreateAndAddUnmanagedSystem<MotionHistoryInitializeSystem>();
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            GetOrCreateAndAddUnmanagedSystem<ValidateRootReferencesSystem>();
#endif
        }
    }

    /// <summary>
    /// This group updates at the beginning of simulation, and is when any logic that keeps track of a history
    /// should rotate that history.
    /// You can use [UpdateInGroup(typeof(MotionHistoryUpdateSuperSystem))] to add systems that should be updated
    /// at this time.
    /// </summary>
    [DisableAutoCreation]
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    [UpdateAfter(typeof(BeginSimulationEntityCommandBufferSystem))]
    [UpdateBefore(typeof(FixedStepSimulationSystemGroup))]
    [UpdateBefore(typeof(VariableRateSimulationSystemGroup))]
    public partial class MotionHistoryUpdateSuperSystem : SuperSystem
    {
        protected override void CreateSystems()
        {
            EnableSystemSorting = true;

            GetOrCreateAndAddUnmanagedSystem<MotionHistoryUpdateSystem>();
        }
    }

    /// <summary>
    /// This group updates at the end of simulation, and is when any logic that exports entity data back to GameObject
    /// data should occur.
    /// You can use [UpdateInGroup(typeof(PostTransformSuperSystem))] to add systems that should be updated
    /// at this time.
    /// </summary>
    /// [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(PostSyncPointGroup), OrderLast = true)]
    [DisableAutoCreation]
    public partial class ExportToGameObjectTransformsEndInitializationSuperSystem : SuperSystem
    {
        protected override void CreateSystems()
        {
            EnableSystemSorting = true;

#if !UNITY_DISABLE_MANAGED_COMPONENTS
            GetOrCreateAndAddUnmanagedSystem<CompanionGameObjectUpdateTransformSystem>();
#endif
            GetOrCreateAndAddUnmanagedSystem<CopyGameObjectTransformFromEntitySystem>();
        }
    }

    /// <summary>
    /// This group updates at the end of simulation, and is when any logic that exports entity data back to GameObject
    /// data should occur.
    /// You can use [UpdateInGroup(typeof(PostTransformSuperSystem))] to add systems that should be updated
    /// at this time.
    /// </summary>
    /// [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    [UpdateBefore(typeof(EndSimulationEntityCommandBufferSystem))]
    [UpdateAfter(typeof(LateSimulationSystemGroup))]
    [DisableAutoCreation]
    public partial class ExportToGameObjectTransformsEndSimulationSuperSystem : SuperSystem
    {
        protected override void CreateSystems()
        {
            EnableSystemSorting = true;

#if !UNITY_DISABLE_MANAGED_COMPONENTS
            GetOrCreateAndAddUnmanagedSystem<CompanionGameObjectUpdateTransformSystem>();
#endif
            GetOrCreateAndAddUnmanagedSystem<CopyGameObjectTransformFromEntitySystem>();
        }
    }
    #endregion

    #region Entry Points
    /// <summary>
    /// This group is updated inside the LatiosWorldSyncGroup and handles registration of GameObjectEntity bindings.
    /// If you create GameObjectEntities at runtime, make sure such systems update before this system.
    /// This system also updates Disabled statuses for Unity's built-in Companion GameObjects.
    /// </summary>
    [UpdateInGroup(typeof(LatiosWorldSyncGroup), OrderLast = true)]
    [DisableAutoCreation]
    public partial class HybridTransformsSyncPointSuperSystem : SuperSystem
    {
        protected override void CreateSystems()
        {
            EnableSystemSorting = true;

            GetOrCreateAndAddManagedSystem<GameObjectEntityBindingSystem>();
        }
    }
    #endregion
}
#endif

