#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
using Unity.Entities;
using Unity.Rendering;

namespace Latios.Kinemation.Systems
{
    /// <summary>
    /// Subclass this class and add it to the world prior to installing Kinemation
    /// to customize the culling loop.
    /// </summary>
    [DisableAutoCreation]
    public partial class KinemationCullingSuperSystem : SuperSystem
    {
        protected override void CreateSystems()
        {
            EnableSystemSorting = false;

            GetOrCreateAndAddUnmanagedSystem<InitializeAndFilterPerCameraSystem>();
            GetOrCreateAndAddUnmanagedSystem<FrustumCullExposedSkeletonsSystem>();
            GetOrCreateAndAddUnmanagedSystem<FrustumCullOptimizedSkeletonsSystem>();
            GetOrCreateAndAddUnmanagedSystem<UpdateLODsSystem>();
            GetOrCreateAndAddUnmanagedSystem<RenderQuickToggleEnableFlagCullingSystem>();
            GetOrCreateAndAddUnmanagedSystem<FrustumCullSkinnedEntitiesSystem>();
            GetOrCreateAndAddUnmanagedSystem<FrustumCullSkinnedPostProcessEntitiesSystem>();
            GetOrCreateAndAddUnmanagedSystem<FrustumCullUnskinnedEntitiesSystem>();
            GetOrCreateAndAddUnmanagedSystem<AllocateDeformMaterialPropertiesSystem>();
            GetOrCreateAndAddUnmanagedSystem<CopyDeformWithCullingSystem>();

            GetOrCreateAndAddManagedSystem<CullingRoundRobinDispatchSuperSystem>();

            GetOrCreateAndAddUnmanagedSystem<GenerateBrgDrawCommandsSystem>();
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

            GetOrCreateAndAddManagedSystem<UploadDynamicMeshesSystem>();
            GetOrCreateAndAddManagedSystem<BlendShapesDispatchSystem>();
            GetOrCreateAndAddManagedSystem<SkinningDispatchSystem>();
            GetOrCreateAndAddManagedSystem<UploadMaterialPropertiesSystem>();

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

    [UpdateInGroup(typeof(UpdatePresentationSystemGroup))]
    [UpdateBefore(typeof(RenderBoundsUpdateSystem))]
    [DisableAutoCreation]
    public partial class KinemationRenderUpdateSuperSystem : RootSuperSystem
    {
        protected override void CreateSystems()
        {
            EnableSystemSorting = false;

            GetOrCreateAndAddUnmanagedSystem<UpdateDeformedMeshBoundsSystem>();
            GetOrCreateAndAddUnmanagedSystem<UpdateSkeletonBoundsSystem>();
            GetOrCreateAndAddUnmanagedSystem<LatiosRenderBoundsUpdateSystem>();
            GetOrCreateAndAddUnmanagedSystem<UpdateBrgBoundsSystem>();
            GetOrCreateAndAddUnmanagedSystem<LatiosLODRequirementsUpdateSystem>();
            GetOrCreateAndAddManagedSystem<BeginPerFrameDeformMeshBuffersUploadSystem>();
        }
    }

    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(LatiosEntitiesGraphicsSystem))]
    [DisableAutoCreation]
    public partial class KinemationPostRenderSuperSystem : SuperSystem
    {
        protected override void CreateSystems()
        {
            EnableSystemSorting = false;

            GetOrCreateAndAddManagedSystem<EndPerFrameMeshDeformBuffersUploadSystem>();
            GetOrCreateAndAddUnmanagedSystem<LatiosLightProbeUpdateSystem>();
            GetOrCreateAndAddUnmanagedSystem<CombineExposedBonesSystem>();
            GetOrCreateAndAddUnmanagedSystem<UpdateSkinnedPostProcessMatrixBoundsSystem>();
            GetOrCreateAndAddUnmanagedSystem<ClearPerFrameCullingMasksSystem>();
            GetOrCreateAndAddUnmanagedSystem<InitializeAndClassifyPerFrameDeformMetadataSystem>();
        }
    }

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

    [UpdateInGroup(typeof(Latios.Systems.LatiosWorldSyncGroup))]
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
}
#endif

