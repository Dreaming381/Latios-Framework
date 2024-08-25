using Latios.Authoring;
using Latios.Kinemation.Authoring.Systems;
using Latios.Kinemation.Systems;
using Unity.Entities;

namespace Latios.Kinemation.Authoring
{
    /// <summary>
    /// Static class containing installers for optional authoring time features in the Kinemation module
    /// </summary>
    public static class KinemationBakingBootstrap
    {
        /// <summary>
        /// Adds Kinemation bakers and baking systems into baking world and disables the Entities.Graphics's SkinnedMeshRenderer bakers
        /// </summary>
        /// <param name="world">The conversion world in which to install the Kinemation conversion systems</param>
        public static void InstallKinemation(ref CustomBakingBootstrapContext context)
        {
            RenderMeshUtilityReplacer.PatchRenderMeshUtility();

            context.filteredBakerTypes.Add(typeof(SkeletonBaker));
            context.filteredBakerTypes.Add(typeof(SkinnedMeshBaker));
            context.filteredBakerTypes.Remove(typeof(Unity.Rendering.SkinnedMeshRendererBaker));

            context.filteredBakerTypes.Add(typeof(DefaultMeshRendererBaker));
            context.filteredBakerTypes.Remove(typeof(Unity.Rendering.MeshRendererBaker));

            context.bakingSystemTypesToInject.Add(TypeManager.GetSystemTypeIndex<KinemationPreTransformsBakingGroup>());
            context.bakingSystemTypesToInject.Add(TypeManager.GetSystemTypeIndex<KinemationSmartBlobberBakingGroup>());
            context.bakingSystemTypesToInject.Add(TypeManager.GetSystemTypeIndex<KinemationSmartBlobberResolverBakingGroup>());
            context.bakingSystemTypesToInject.Add(TypeManager.GetSystemTypeIndex<AddMasksBakingSystem>());
            context.bakingSystemTypesToInject.Add(TypeManager.GetSystemTypeIndex<AddPostProcessMatrixSystem>());

            context.bakingSystemTypesToDisable.Add(TypeManager.GetSystemTypeIndex<Unity.Rendering.AdditionalMeshRendererFilterBakingSystem>());
            context.bakingSystemTypesToDisable.Add(TypeManager.GetSystemTypeIndex<Unity.Rendering.MeshRendererBaking>());
            context.bakingSystemTypesToDisable.Add(TypeManager.GetSystemTypeIndex<Unity.Rendering.RenderMeshPostProcessSystem>());
            context.bakingSystemTypesToInject.Add(TypeManager.GetSystemTypeIndex<RendererBakingSystem>());

            context.optimizationSystemTypesToInject.Add(TypeManager.GetSystemTypeIndex<LatiosFrozenStaticRendererSystem>());
            context.optimizationSystemTypesToDisable.Add(TypeManager.GetSystemTypeIndex<Unity.Rendering.FrozenStaticRendererSystem>());
            context.optimizationSystemTypesToInject.Add(TypeManager.GetSystemTypeIndex<LatiosAddWorldAndChunkRenderBoundsSystem>());
            context.optimizationSystemTypesToDisable.Add(TypeManager.GetSystemTypeIndex<Unity.Rendering.RenderBoundsUpdateSystem>());
            context.optimizationSystemTypesToInject.Add(TypeManager.GetSystemTypeIndex<LatiosRenderBoundsUpdateSystem>());
            context.optimizationSystemTypesToDisable.Add(TypeManager.GetSystemTypeIndex<Unity.Rendering.UpdateSceneBoundingVolumeFromRendererBounds>());
            context.optimizationSystemTypesToInject.Add(TypeManager.GetSystemTypeIndex<LatiosUpdateSceneBoundingVolumeFromRendererBounds>());
        }
    }
}

