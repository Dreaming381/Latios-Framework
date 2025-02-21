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
    /// This round-robin super system updates after all the deformation systems, and during culling, right before material properties.
    /// It is intended for systems that require valid graphics buffer contents from deforming meshes.
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
    /// This super system is the first to execute within KinemationCustomGraphicsSuperSystem.
    /// Use it to enable entities for custom graphics processing.
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
    /// This is the main super system that processes render bounds.
    /// </summary>
    [UpdateInGroup(typeof(UpdatePresentationSystemGroup))]
    [UpdateBefore(typeof(RenderBoundsUpdateSystem))]
    [DisableAutoCreation]
    public partial class KinemationRenderUpdateSuperSystem : SuperSystem
    {
        protected override void CreateSystems()
        {
            EnableSystemSorting = false;

            GetOrCreateAndAddUnmanagedSystem<UpdateDeformedMeshBoundsSystem>();
            GetOrCreateAndAddUnmanagedSystem<UpdateSkeletonBoundsSystem>();
            GetOrCreateAndAddUnmanagedSystem<LatiosRenderBoundsUpdateSystem>();
            GetOrCreateAndAddUnmanagedSystem<UpdateBrgBoundsSystem>();
            GetOrCreateAndAddUnmanagedSystem<BeginPerFrameDeformMeshBuffersUploadSystem>();
        }
    }

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

            GetOrCreateAndAddUnmanagedSystem<EndPerFrameMeshDeformBuffersUploadSystem>();
            GetOrCreateAndAddUnmanagedSystem<ClearPerFrameCullingMasksSystem>();
            GetOrCreateAndAddUnmanagedSystem<InitializeAndClassifyPerFrameDeformMetadataSystem>();
            GetOrCreateAndAddUnmanagedSystem<PrepareLODsSystem>();
            GetOrCreateAndAddUnmanagedSystem<LatiosLightProbeUpdateSystem>();
            GetOrCreateAndAddUnmanagedSystem<CombineExposedBonesSystem>();
            GetOrCreateAndAddUnmanagedSystem<UpdateSkinnedPostProcessMatrixBoundsSystem>();
            GetOrCreateAndAddUnmanagedSystem<AllocateUniqueMeshesSystem>();
            GetOrCreateAndAddManagedSystem<KinemationCustomGraphicsSuperSystem>();
#if UNITY_EDITOR
            GetOrCreateAndAddManagedSystem<KinemationCullingPassSystemExposerSuperSystem>();
#endif
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

            if ((World.Flags & WorldFlags.Editor) == WorldFlags.Editor)
                GetOrCreateAndAddUnmanagedSystem<LiveBakingCheckForReinitsSystem>();

            GetOrCreateAndAddUnmanagedSystem<LatiosUpdateEntitiesGraphicsChunkStructureSystem>();
            GetOrCreateAndAddUnmanagedSystem<LatiosAddWorldAndChunkRenderBoundsSystem>();
            GetOrCreateAndAddUnmanagedSystem<KinemationBindingReactiveSystem>();
        }
    }
    #endregion

    #region Custom Graphics SuperSystems
    /// <summary>
    /// This super system optionally updates based on the existence of EnableCustomGraphicsTag
    /// on the worldBlackboardEntity. It is responsible for making ECS data accessible to VFX Graph
    /// or other operations which must happen before culling.
    /// </summary>
    [DisableAutoCreation]
    public partial class KinemationCustomGraphicsSuperSystem : SuperSystem
    {
        protected override void CreateSystems()
        {
            EnableSystemSorting = false;

            GetOrCreateAndAddManagedSystem<KinemationCustomGraphicsSetupSuperSystem>();

            GetOrCreateAndAddUnmanagedSystem<AllocateDeformMaterialPropertiesSystem>();
            GetOrCreateAndAddUnmanagedSystem<CopyDeformCustomSystem>();

            GetOrCreateAndAddManagedSystem<CustomGraphicsRoundRobinDispatchSuperSystem>();

            GetOrCreateAndAddUnmanagedSystem<ApplyDispatchMasksToFrameMasksSystem>();
            GetOrCreateAndAddUnmanagedSystem<SetRenderVisibilityFeedbackFlagsSystem>();
        }

        public override bool ShouldUpdateSystem()
        {
            return worldBlackboardEntity.HasComponent<EnableCustomGraphicsTag>();
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
        protected override void CreateSystems()
        {
            EnableSystemSorting = false;

            GetOrCreateAndAddManagedSystem<DispatchRoundRobinEarlyExtensionsSuperSystem>();
            GetOrCreateAndAddUnmanagedSystem<UploadDynamicMeshesSystem>();
            GetOrCreateAndAddUnmanagedSystem<BlendShapesDispatchSystem>();
            GetOrCreateAndAddUnmanagedSystem<SkinningDispatchSystem>();
            GetOrCreateAndAddManagedSystem<DispatchRoundRobinLateExtensionsSuperSystem>();

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
            GetOrCreateAndAddUnmanagedSystem<CullInvalidUniqueMeshesSystem>();
            GetOrCreateAndAddUnmanagedSystem<FrustumCullExposedSkeletonsSystem>();
            GetOrCreateAndAddUnmanagedSystem<FrustumCullOptimizedSkeletonsSystem>();
            GetOrCreateAndAddUnmanagedSystem<CullLodsSystem>();
            GetOrCreateAndAddUnmanagedSystem<FrustumCullSkinnedEntitiesSystem>();
            GetOrCreateAndAddUnmanagedSystem<FrustumCullSkinnedPostProcessEntitiesSystem>();
            GetOrCreateAndAddUnmanagedSystem<FrustumCullUnskinnedEntitiesSystem>();
            GetOrCreateAndAddUnmanagedSystem<CopyDeformCullingSystem>();
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

