using Unity.Entities;
using Unity.Rendering;

namespace Latios.Kinemation.Systems
{
    /// <summary>
    /// Subclass this class and add it to the world prior to installing Kinemation
    /// to customize the culling loop.
    /// </summary>
    [DisableAutoCreation]
    public class KinemationCullingSuperSystem : SuperSystem
    {
        protected override void CreateSystems()
        {
            EnableSystemSorting = false;

            GetOrCreateAndAddUnmanagedSystem<InitializeAndFilterPerCameraSystem>();
            GetOrCreateAndAddUnmanagedSystem<FrustumCullExposedSkeletonsSystem>();
            GetOrCreateAndAddUnmanagedSystem<FrustumCullOptimizedSkeletonsSystem>();
            GetOrCreateAndAddUnmanagedSystem<UpdateLODsSystem>();
            GetOrCreateAndAddUnmanagedSystem<FrustumCullSkinnedEntitiesSystem>();
            GetOrCreateAndAddUnmanagedSystem<AllocateDeformedMeshesSystem>();
            GetOrCreateAndAddUnmanagedSystem<AllocateLinearBlendMatricesSystem>();
            GetOrCreateAndAddManagedSystem<SkinningDispatchSystem>();
            GetOrCreateAndAddUnmanagedSystem<FrustumCullUnskinnedEntitiesSystem>();
            GetOrCreateAndAddUnmanagedSystem<CopySkinWithCullingSystem>();
            GetOrCreateAndAddManagedSystem<UploadMaterialPropertiesSystem>();
            GetOrCreateAndAddUnmanagedSystem<GenerateBrgDrawCommandsSystem>();
        }
    }

    [UpdateInGroup(typeof(UpdatePresentationSystemGroup))]
    [UpdateAfter(typeof(RenderBoundsUpdateSystem))]
    [DisableAutoCreation]
    public class KinemationRenderUpdateSuperSystem : RootSuperSystem
    {
        protected override void CreateSystems()
        {
            EnableSystemSorting = false;

            GetOrCreateAndAddUnmanagedSystem<UpdateSkeletonBoundsSystem>();
            GetOrCreateAndAddUnmanagedSystem<UpdateSkinnedMeshChunkBoundsSystem>();
            GetOrCreateAndAddManagedSystem<BeginPerFrameMeshSkinningBuffersUploadSystem>();
        }
    }

    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(LatiosEntitiesGraphicsSystem))]
    [DisableAutoCreation]
    public class KinemationPostRenderSuperSystem : SuperSystem
    {
        protected override void CreateSystems()
        {
            EnableSystemSorting = false;

            GetOrCreateAndAddManagedSystem<EndPerFrameMeshSkinningBuffersUploadSystem>();
            GetOrCreateAndAddUnmanagedSystem<UpdateMatrixPreviousSystem>();
            GetOrCreateAndAddUnmanagedSystem<CombineExposedBonesSystem>();
            GetOrCreateAndAddUnmanagedSystem<ClearPerFrameCullingMasksSystem>();
            //GetOrCreateAndAddUnmanagedSystem<UpdateChunkComputeDeformMetadataSystem>();
            //GetOrCreateAndAddUnmanagedSystem<UpdateChunkLinearBlendMetadataSystem>();
            GetOrCreateAndAddUnmanagedSystem<ResetPerFrameSkinningMetadataJob>();
        }
    }

    [UpdateInGroup(typeof(StructuralChangePresentationSystemGroup))]
    [DisableAutoCreation]
    public class KinemationRenderSyncPointSuperSystem : SuperSystem
    {
        protected override void CreateSystems()
        {
            EnableSystemSorting = false;

            GetOrCreateAndAddUnmanagedSystem<AddMissingMatrixCacheSystem>();
            GetOrCreateAndAddUnmanagedSystem<AddMissingMasksSystem>();
            GetOrCreateAndAddUnmanagedSystem<SkeletonMeshBindingReactiveSystem>();
        }
    }

    [UpdateInGroup(typeof(Latios.Systems.LatiosWorldSyncGroup))]
    [DisableAutoCreation]
    public class KinemationFrameSyncPointSuperSystem : SuperSystem
    {
        protected override void CreateSystems()
        {
            EnableSystemSorting = false;

            GetOrCreateAndAddUnmanagedSystem<AddMissingMatrixCacheSystem>();
            GetOrCreateAndAddUnmanagedSystem<AddMissingMasksSystem>();
            GetOrCreateAndAddUnmanagedSystem<SkeletonMeshBindingReactiveSystem>();
        }
    }
}

