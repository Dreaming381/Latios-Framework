#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
using Latios.Authoring;
using Latios.Kinemation.Authoring.Systems;
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
        public static void InstallKinemationBakersAndSystems(ref CustomBakingBootstrapContext context)
        {
            RenderMeshUtilityReplacer.PatchRenderMeshUtility();

            context.filteredBakerTypes.Add(typeof(SkeletonBaker));
            context.filteredBakerTypes.Add(typeof(SkinnedMeshBaker));
            context.filteredBakerTypes.Remove(typeof(Unity.Rendering.SkinnedMeshRendererBaker));

            context.filteredBakerTypes.Add(typeof(DefaultMeshRendererBaker));
            context.filteredBakerTypes.Remove(typeof(Unity.Rendering.MeshRendererBaker));

            context.bakingSystemTypesToInject.Add(typeof(KinemationPreTransformsBakingGroup));
            context.bakingSystemTypesToInject.Add(typeof(KinemationSmartBlobberBakingGroup));
            context.bakingSystemTypesToInject.Add(typeof(KinemationSmartBlobberResolverBakingGroup));
            context.bakingSystemTypesToInject.Add(typeof(AddMasksBakingSystem));
            context.bakingSystemTypesToInject.Add(typeof(AddPostProcessMatrixSystem));

            context.optimizationSystemTypesToInject.Add(typeof(LatiosFrozenStaticRendererSystem));
            context.optimizationSystemTypesToDisable.Add(typeof(Unity.Rendering.FrozenStaticRendererSystem));
            context.optimizationSystemTypesToInject.Add(typeof(LatiosLODRequirementsUpdateSystem));
            context.optimizationSystemTypesToInject.Add(typeof(LatiosAddWorldAndChunkRenderBoundsSystem));
            context.optimizationSystemTypesToInject.Add(typeof(LatiosRenderBoundsUpdateSystem));
        }
    }
}
#endif

