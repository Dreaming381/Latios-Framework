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

            context.bakingSystemTypesToInject.Add(TypeManager.GetSystemTypeIndex<KinemationPreTransformsBakingGroup>());
            context.bakingSystemTypesToInject.Add(TypeManager.GetSystemTypeIndex<KinemationSmartBlobberBakingGroup>());
            context.bakingSystemTypesToInject.Add(TypeManager.GetSystemTypeIndex<KinemationSmartBlobberResolverBakingGroup>());
            context.bakingSystemTypesToInject.Add(TypeManager.GetSystemTypeIndex<AddMasksBakingSystem>());
            context.bakingSystemTypesToInject.Add(TypeManager.GetSystemTypeIndex<AddPostProcessMatrixSystem>());

            context.optimizationSystemTypesToInject.Add(TypeManager.GetSystemTypeIndex<LatiosFrozenStaticRendererSystem>());
            context.optimizationSystemTypesToDisable.Add(TypeManager.GetSystemTypeIndex<Unity.Rendering.FrozenStaticRendererSystem>());
            context.optimizationSystemTypesToInject.Add(TypeManager.GetSystemTypeIndex<LatiosLODRequirementsUpdateSystem>());
            context.optimizationSystemTypesToInject.Add(TypeManager.GetSystemTypeIndex<LatiosAddWorldAndChunkRenderBoundsSystem>());
            context.optimizationSystemTypesToInject.Add(TypeManager.GetSystemTypeIndex<LatiosRenderBoundsUpdateSystem>());
        }

        /// <summary>
        /// Adds Mecanim bakers and baking systems into baking world
        /// </summary>
        public static void InstallMecanimBakersAndSystems(ref CustomBakingBootstrapContext context)
        {
            context.filteredBakerTypes.Add(typeof(MecanimSmartBaker));
            context.bakingSystemTypesToInject.Add(TypeManager.GetSystemTypeIndex<MecanimAnimatorControllerSmartBlobberSystem>());
        }
    }
}

