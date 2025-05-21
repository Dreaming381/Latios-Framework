#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
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
    /// This group updates at the end of InitializationSystemGroup right after an early transform system update.
    /// It is responsible for initializing any default-initialized motion history components.
    /// You can use [UpdateInGroup(typeof(MotionHistoryInitializeSuperSystem))] to add systems that should be updated
    /// at this time.
    /// </summary>
    [DisableAutoCreation]
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    public partial class MotionHistoryInitializeSuperSystem : SuperSystem
    {
        protected override void CreateSystems()
        {
            EnableSystemSorting = true;

            GetOrCreateAndAddUnmanagedSystem<MotionHistoryInitializeSystem>();
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
    /// You can use [UpdateInGroup(typeof(PreTransformSuperSystem))] to add systems that should be updated
    /// right before the main transform hierarchy synchronization step. This is useful for any entities which
    /// need to copy a local or world transform from a GameObject or another entity. Note that this group
    /// updates at least twice per frame.
    /// </summary>
    [DisableAutoCreation]
    public partial class PreTransformSuperSystem : SuperSystem
    {
        protected override void CreateSystems()
        {
            EnableSystemSorting = true;

            GetOrCreateAndAddUnmanagedSystem<CopyGameObjectTransformToEntitySystem>();
        }
    }

    /// <summary>
    /// You can use [UpdateInGroup(typeof(PostTransformSuperSystem))] to add systems that should be updated
    /// right after the main transform hierarchy synchronization step. This is useful for any entities which
    /// need to copy a local or world transform to a GameObject or another entity. Note that this group
    /// updates at least twice per frame.
    /// </summary>
    [DisableAutoCreation]
    public partial class PostTransformSuperSystem : SuperSystem
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
            //GetOrCreateAndAddManagedSystem<CompanionGameObjectUpdateSystem>();
        }
    }

    /// <summary>
    /// This group is updated inside the PostSyncPointGroup and performs an early transform system update
    /// to sync up any new or deleted hierarchies, followed by motion history initialization to ensure
    /// valid history for rendering.
    /// </summary>
    [DisableAutoCreation]
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateInGroup(typeof(PostSyncPointGroup))]
    public partial class TransformInitializeSuperSystem : SuperSystem
    {
        protected override void CreateSystems()
        {
            EnableSystemSorting = false;

            GetOrCreateAndAddManagedSystem<TransformSuperSystem>();
            GetOrCreateAndAddManagedSystem<MotionHistoryInitializeSuperSystem>();
        }
    }

    /// <summary>
    /// The Transform System Group of Latios Transforms which performs hierarchy synchronization.
    /// Do not add systems to this group directly, but instead add systems to [Pre/Post]TransformSuperSystem.
    /// </summary>
    [DisableAutoCreation]
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    public partial class TransformSuperSystem : SuperSystem
    {
        protected override void CreateSystems()
        {
            EnableSystemSorting = false;

            var flags = worldBlackboardEntity.GetComponentData<RuntimeFeatureFlags>();

            GetOrCreateAndAddManagedSystem<PreTransformSuperSystem>();
            GetOrCreateAndAddUnmanagedSystem<ParentChangeSystem>();
            if ((flags.flags & RuntimeFeatureFlags.Flags.ExtremeTransforms) != RuntimeFeatureFlags.Flags.None)
            {
                GetOrCreateAndAddUnmanagedSystem<ExtremeChildDepthsSystem>();
                GetOrCreateAndAddUnmanagedSystem<ExtremeTransformHierarchyUpdateSystem>();
            }
            else
            {
                GetOrCreateAndAddUnmanagedSystem<TransformHierarchyUpdateSystem>();
            }
            GetOrCreateAndAddManagedSystem<PostTransformSuperSystem>();
        }
    }
    #endregion
}
#endif

