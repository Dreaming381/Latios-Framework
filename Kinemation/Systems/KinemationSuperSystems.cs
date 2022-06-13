using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine.Rendering;

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

            GetOrCreateAndAddSystem<FrustumCullExposedSkeletonsSystem>();
            GetOrCreateAndAddSystem<FrustumCullOptimizedSkeletonsSystem>();
            GetOrCreateAndAddSystem<UpdateLODsSystem>();
            GetOrCreateAndAddSystem<FrustumCullSkinnedEntitiesSystem>();
            GetOrCreateAndAddSystem<AllocateDeformedMeshesSystem>();
            GetOrCreateAndAddSystem<AllocateLinearBlendMatricesSystem>();
            GetOrCreateAndAddSystem<SkinningDispatchSystem>();
            GetOrCreateAndAddSystem<FrustumCullUnskinnedEntitiesSystem>();
            GetOrCreateAndAddSystem<CopySkinWithCullingSystem>();
            GetOrCreateAndAddSystem<UploadMaterialPropertiesSystem>();
            GetOrCreateAndAddSystem<UpdateVisibilitiesSystem>();
        }
    }

    [UpdateInGroup(typeof(UpdatePresentationSystemGroup))]
    [UpdateAfter(typeof(RenderBoundsUpdateSystem))]
    [DisableAutoCreation]
    public class KinemationRenderUpdateSuperSystem : RootSuperSystem
    {
        protected override void CreateSystems()
        {
            GetOrCreateAndAddSystem<UpdateSkeletonBoundsSystem>();
            GetOrCreateAndAddSystem<UpdateSkinnedMeshChunkBoundsSystem>();
            GetOrCreateAndAddSystem<BeginPerFrameMeshSkinningBuffersUploadSystem>();
        }
    }

    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(LatiosHybridRendererSystem))]
    [DisableAutoCreation]
    public class KinemationPostRenderSuperSystem : SuperSystem
    {
        protected override void CreateSystems()
        {
            GetOrCreateAndAddSystem<EndPerFrameMeshSkinningBuffersUploadSystem>();
            GetOrCreateAndAddSystem<UpdateMatrixPreviousSystem>();
            GetOrCreateAndAddSystem<CombineExposedBonesSystem>();
            GetOrCreateAndAddSystem<ClearPerFrameCullingMasksSystem>();
            GetOrCreateAndAddSystem<UpdateChunkComputeDeformMetadataSystem>();
            GetOrCreateAndAddSystem<UpdateChunkLinearBlendMetadataSystem>();
            GetOrCreateAndAddSystem<ResetPerFrameSkinningMetadataJob>();
        }
    }

    [UpdateInGroup(typeof(StructuralChangePresentationSystemGroup))]
    [DisableAutoCreation]
    public class KinemationRenderSyncPointSuperSystem : SuperSystem
    {
        protected override void CreateSystems()
        {
            GetOrCreateAndAddSystem<AddMissingMatrixCacheSystem>();
            GetOrCreateAndAddSystem<AddMissingMasksSystem>();
            GetOrCreateAndAddSystem<SkeletonMeshBindingReactiveSystem>();
        }
    }

    [UpdateInGroup(typeof(Latios.Systems.LatiosWorldSyncGroup))]
    [DisableAutoCreation]
    public class KinemationFrameSyncPointSuperSystem : SuperSystem
    {
        protected override void CreateSystems()
        {
            GetOrCreateAndAddSystem<AddMissingMatrixCacheSystem>();
            GetOrCreateAndAddSystem<AddMissingMasksSystem>();
            GetOrCreateAndAddSystem<SkeletonMeshBindingReactiveSystem>();
        }
    }
}

