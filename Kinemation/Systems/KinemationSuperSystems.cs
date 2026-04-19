using Latios.Transforms.Systems;
using Unity.Entities;
using Unity.Rendering;
#if LATIOS_TRANSFORMS_UNCACHED_QVVS || LATIOS_TRANSFORMS_UNITY
using Unity.Transforms;
#endif

namespace Latios.Kinemation.Systems
{
    #region User-target group SuperSystems
    /// <summary>
    /// This round-robin super system is intended for systems that don't require info from deforming meshes.
    /// This is the ideal location for round-robin systems that schedule single-threaded jobs updating material properties.
    /// WorldRenderBounds are not up-to-date during the custom graphics phase.
    /// </summary>
    [DisableAutoCreation]
    public partial class DispatchRoundRobinEarlyExtensionsSuperSystem : SuperSystem
    {
        protected override void CreateSystems()
        {
            EnableSystemSorting = true;
        }
    }

    /// <summary>
    /// This round-robin super system updates after all the deformation systems, and before material properties are uploaded.
    /// It is intended for systems that require valid graphics buffer contents from deforming meshes.
    /// WorldRenderBounds are not up-to-date during the custom graphics phase.
    /// </summary>
    [DisableAutoCreation]
    public partial class DispatchRoundRobinLateExtensionsSuperSystem : SuperSystem
    {
        protected override void CreateSystems()
        {
            EnableSystemSorting = true;
        }
    }

    /// <summary>
    /// This super system is the first to execute within the custom graphics phase.
    /// Use it to enable entities for custom graphics processing.
    /// WorldRenderBounds are not up-to-date when this super system updates.
    /// </summary>
    [DisableAutoCreation]
    public partial class KinemationCustomGraphicsSetupSuperSystem : SuperSystem
    {
        protected override void CreateSystems()
        {
            EnableSystemSorting = true;
        }
    }
    #endregion

    #region Update SuperSystems
    /// <summary>
    /// This super system updates after the second presentation sync point (the one that always happens).
    /// Jobs scheduled from here and afterwards may run during engine and editor updates.
    /// </summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(LatiosEntitiesGraphicsSystem))]
    [DisableAutoCreation]
    public partial class KinemationPostRenderSuperSystem : SuperSystem
    {
        protected override void CreateSystems()
        {
            EnableSystemSorting = false;

            worldBlackboardEntity.AddComponent<EnableUpdatingInCustomGraphics>();

            GetOrCreateAndAddUnmanagedSystem<ClearPerFrameCullingMasksSystem>();
            GetOrCreateAndAddManagedSystem<KinemationCustomGraphicsSetupSuperSystem>();
            GetOrCreateAndAddUnmanagedSystem<InitializeAndClassifyPerFrameDeformMetadataSystem>();
            GetOrCreateAndAddUnmanagedSystem<AllocateDeformMaterialPropertiesSystem>();
            GetOrCreateAndAddUnmanagedSystem<CopyDeformCustomSystem>();
            GetOrCreateAndAddUnmanagedSystem<AllocateUniqueMeshesSystem>();
            GetOrCreateAndAddManagedSystem<KinemationPostRenderCollectSuperSystem>();

            GetOrCreateAndAddUnmanagedSystem<BeginPerFrameDeformMeshBuffersUploadSystem>();
            GetOrCreateAndAddUnmanagedSystem<ApplyMipMapStreamingLevelsSystem>();
            GetOrCreateAndAddManagedSystem<KinemationPostRenderWriteSuperSystem>();

            GetOrCreateAndAddUnmanagedSystem<UpdateDeformedMeshBoundsSystem>();
            GetOrCreateAndAddUnmanagedSystem<UpdateSkeletonBoundsSystem>();
            GetOrCreateAndAddUnmanagedSystem<LatiosRenderBoundsUpdateSystem>();
            GetOrCreateAndAddUnmanagedSystem<LatiosLightProbeUpdateSystem>();
            GetOrCreateAndAddUnmanagedSystem<ApplyDispatchMasksToFrameMasksSystem>();
            GetOrCreateAndAddUnmanagedSystem<SetRenderVisibilityFeedbackFlagsSystem>();
            GetOrCreateAndAddUnmanagedSystem<EndPerFrameMeshDeformBuffersUploadSystem>();
            GetOrCreateAndAddManagedSystem<KinemationPostRenderDispatchSuperSystem>();

#if UNITY_EDITOR
            GetOrCreateAndAddManagedSystem<KinemationCullingPassSystemExposerSuperSystem>();
#endif
        }
    }

    [DisableAutoCreation]
    public partial class KinemationPostRenderCollectSuperSystem : SuperSystem
    {
        CustomGraphicsRoundRobinDispatchSuperSystem dispatchSuperSystem;

        protected override void CreateSystems()
        {
            EnableSystemSorting = false;
            dispatchSuperSystem = GetOrCreateAndAddManagedSystem<CustomGraphicsRoundRobinDispatchSuperSystem>();
        }

        protected override void OnUpdate()
        {
            dispatchSuperSystem.nextState = CullingComputeDispatchState.Collect;
            base.OnUpdate();
        }
    }

    [DisableAutoCreation]
    public partial class KinemationPostRenderWriteSuperSystem : SuperSystem
    {
        CustomGraphicsRoundRobinDispatchSuperSystem dispatchSuperSystem;

        protected override void CreateSystems()
        {
            EnableSystemSorting = false;
            dispatchSuperSystem = GetOrCreateAndAddManagedSystem<CustomGraphicsRoundRobinDispatchSuperSystem>();
        }

        protected override void OnUpdate()
        {
            dispatchSuperSystem.nextState = CullingComputeDispatchState.Write;
            base.OnUpdate();
        }
    }

    [DisableAutoCreation]
    public partial class KinemationPostRenderDispatchSuperSystem : SuperSystem
    {
        CustomGraphicsRoundRobinDispatchSuperSystem dispatchSuperSystem;

        protected override void CreateSystems()
        {
            EnableSystemSorting = false;
            dispatchSuperSystem = GetOrCreateAndAddManagedSystem<CustomGraphicsRoundRobinDispatchSuperSystem>();
        }

        protected override void OnUpdate()
        {
            dispatchSuperSystem.nextState = CullingComputeDispatchState.Dispatch;
            base.OnUpdate();
        }
    }

    /// <summary>
    /// This super system executes special dispatch custom graphics systems in round-robin fashion.
    /// This is because dispatch systems typically require two separate sync points each to
    /// interact with the graphics API. By executing these phases in round-robin, the worker
    /// threads are able to stay busy during a single system's sync point, as each system
    /// only needs to sync with its own jobs.
    /// </summary>
    [DisableAutoCreation]
    public partial class CustomGraphicsRoundRobinDispatchSuperSystem : SuperSystem
    {
        internal CullingComputeDispatchState nextState = CullingComputeDispatchState.Collect;

        protected override void CreateSystems()
        {
            EnableSystemSorting = false;

            GetOrCreateAndAddManagedSystem<DispatchRoundRobinEarlyExtensionsSuperSystem>();
            GetOrCreateAndAddUnmanagedSystem<UploadUniqueMeshesSystem>();
            GetOrCreateAndAddUnmanagedSystem<UploadDynamicMeshesSystem>();
            GetOrCreateAndAddUnmanagedSystem<BlendShapesDispatchSystem>();
            GetOrCreateAndAddUnmanagedSystem<SkinningDispatchSystem>();
            GetOrCreateAndAddManagedSystem<DispatchRoundRobinLateExtensionsSuperSystem>();
            GetOrCreateAndAddUnmanagedSystem<UploadMaterialPropertiesSystem>();

            worldBlackboardEntity.AddComponent<CullingComputeDispatchActiveState>();
        }

        protected override void OnUpdate()
        {
            worldBlackboardEntity.SetComponentData(new CullingComputeDispatchActiveState { state = nextState });
            base.OnUpdate();
        }
    }

    /// <summary>
    /// This super system runs during the sync point stage of PresentationSystemGroup.
    /// Often, none of the systems in this stage do anything, so there may not actually
    /// be a real sync point here.
    /// </summary>
    [UpdateInGroup(typeof(StructuralChangePresentationSystemGroup))]
    [DisableAutoCreation]
    public partial class KinemationRenderSyncPointSuperSystem : SuperSystem
    {
        protected override void CreateSystems()
        {
            EnableSystemSorting = false;

            GetOrCreateAndAddUnmanagedSystem<LatiosUpdateEntitiesGraphicsChunkStructureSystem>();
            GetOrCreateAndAddUnmanagedSystem<LatiosAddWorldAndChunkRenderBoundsSystem>();
            GetOrCreateAndAddUnmanagedSystem<KinemationBindingReactiveSystem>();
        }
    }

    /// <summary>
    /// This super system runs during the Latios Framework sync point stage in InitializationSystemGroup.
    /// This helps prevent sync points from happening in PresentationSystemGroup.
    /// </summary>
    [UpdateInGroup(typeof(Latios.Systems.LatiosWorldSyncGroup), OrderLast = true)]
    [DisableAutoCreation]
    public partial class KinemationFrameSyncPointSuperSystem : SuperSystem
    {
        protected override void CreateSystems()
        {
            EnableSystemSorting = false;

            GetOrCreateAndAddUnmanagedSystem<LatiosUpdateEntitiesGraphicsChunkStructureSystem>();
            GetOrCreateAndAddUnmanagedSystem<LatiosAddWorldAndChunkRenderBoundsSystem>();
            GetOrCreateAndAddUnmanagedSystem<KinemationBindingReactiveSystem>();
        }
    }
    #endregion

    #region Live Baking SuperSystems
    [UpdateInGroup(typeof(Latios.Systems.AfterLiveBakingSuperSystem))]
    [DisableAutoCreation]
    public partial class KinemationAfterLiveBakingSuperSystem : SuperSystem
    {
        protected override void CreateSystems()
        {
            EnableSystemSorting = false;

            GetOrCreateAndAddUnmanagedSystem<LiveBakingCheckForReinitsSystem>();
            GetOrCreateAndAddUnmanagedSystem<LiveBakingEnableChangedUniqueMeshesSystem>();
        }
    }
    #endregion

    #region Culling and Dispatch SuperSystems
    /// <summary>
    /// This super system executes for each culling pass callback from Unity and typically
    /// runs multiple times per frame. If you need a new hook point into this culling loop
    /// for your own custom systems, please make a request via available social channels.
    /// </summary>
    [DisableAutoCreation]
    public partial class KinemationCullingSuperSystem : SuperSystem
    {
        protected override void CreateSystems()
        {
            EnableSystemSorting = false;

            GetOrCreateAndAddUnmanagedSystem<InitializeAndFilterPerCameraSystem>();
            GetOrCreateAndAddUnmanagedSystem<CullLodsSystem>();
            GetOrCreateAndAddUnmanagedSystem<FrustumCullSystem>();
            GetOrCreateAndAddUnmanagedSystem<SelectMmiRangeLodsSystem>();
            GetOrCreateAndAddUnmanagedSystem<GenerateBrgDrawCommandsSystem>();

            SetRateManagerCreateAllocator(null);
        }

        protected override unsafe void OnUpdate()
        {
            var old = World.CurrentGroupAllocators;
            World.SetGroupAllocator(RateGroupAllocators);
            base.OnUpdate();
            World.RestoreGroupAllocator(old);
        }
    }

    /// <summary>
    /// This super system executes for each dispatch pass callback from Unity and may
    /// run multiple times per frame. If you need a new hook point into this culling loop
    /// for your own custom systems, please make a request via available social channels.
    /// </summary>
    [DisableAutoCreation]
    public partial class KinemationCullingDispatchSuperSystem : SuperSystem
    {
        protected override void CreateSystems()
        {
            EnableSystemSorting = false;

            GetOrCreateAndAddUnmanagedSystem<AllocateDeformMaterialPropertiesSystem>();
            GetOrCreateAndAddUnmanagedSystem<CopyDeformMaterialsSystem>();

            GetOrCreateAndAddManagedSystem<CullingRoundRobinDispatchSuperSystem>();

            GetOrCreateAndAddUnmanagedSystem<ApplyDispatchMasksToFrameMasksSystem>();
            GetOrCreateAndAddUnmanagedSystem<SetRenderVisibilityFeedbackFlagsSystem>();
            GetOrCreateAndAddUnmanagedSystem<EvaluateMipMapStreamingLevelsSystem>();

            SetRateManagerCreateAllocator(null);
        }

        protected override unsafe void OnUpdate()
        {
            var old = World.CurrentGroupAllocators;
            World.SetGroupAllocator(RateGroupAllocators);
            base.OnUpdate();
            World.RestoreGroupAllocator(old);
        }
    }

    /// <summary>
    /// This super system executes special dispatch culling systems in round-robin fashion.
    /// This is because dispatch systems typically require two separate sync points each to
    /// interact with the graphics API. By executing these phases in round-robin, the worker
    /// threads are able to stay busy during a single system's sync point, as each system
    /// only needs to sync with its own jobs.
    /// </summary>
    [DisableAutoCreation]
    public partial class CullingRoundRobinDispatchSuperSystem : SuperSystem
    {
        protected override void CreateSystems()
        {
            EnableSystemSorting = false;

            GetOrCreateAndAddManagedSystem<DispatchRoundRobinEarlyExtensionsSuperSystem>();
            GetOrCreateAndAddUnmanagedSystem<UploadDynamicMeshesSystem>();
            GetOrCreateAndAddUnmanagedSystem<BlendShapesDispatchSystem>();
            GetOrCreateAndAddUnmanagedSystem<SkinningDispatchSystem>();
            GetOrCreateAndAddManagedSystem<DispatchRoundRobinLateExtensionsSuperSystem>();
            GetOrCreateAndAddUnmanagedSystem<UploadMaterialPropertiesSystem>();

            worldBlackboardEntity.AddComponent<CullingComputeDispatchActiveState>();
        }

        protected override void OnUpdate()
        {
            worldBlackboardEntity.SetComponentData(new CullingComputeDispatchActiveState { state = CullingComputeDispatchState.Collect });
            base.OnUpdate();
            worldBlackboardEntity.SetComponentData(new CullingComputeDispatchActiveState { state = CullingComputeDispatchState.Write });
            base.OnUpdate();
            worldBlackboardEntity.SetComponentData(new CullingComputeDispatchActiveState { state = CullingComputeDispatchState.Dispatch });
            base.OnUpdate();
        }
    }
    #endregion

    [DisableAutoCreation]
    public partial class KinemationCullingPassSystemExposerSuperSystem : SuperSystem
    {
        protected override void CreateSystems()
        {
            GetOrCreateAndAddManagedSystem<KinemationCullingSuperSystem>();
            GetOrCreateAndAddManagedSystem<KinemationCullingDispatchSuperSystem>();
        }

        // This is just for showing in the Editor.
        protected override void OnUpdate()
        {
        }
    }
}

