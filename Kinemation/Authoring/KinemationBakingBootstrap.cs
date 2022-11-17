using Latios.Authoring;
using Latios.Kinemation.Authoring.Systems;
using Unity.Entities;
using Unity.Rendering;

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
            context.filteredBakerTypes.Add(typeof(SkeletonBaker));
            context.filteredBakerTypes.Add(typeof(SkinnedMeshBaker));
            context.filteredBakerTypes.Remove(typeof(Unity.Rendering.SkinnedMeshRendererBaker));

            context.systemTypesToInject.Add(typeof(KinemationSmartBlobberBakingGroup));
            context.systemTypesToInject.Add(typeof(KinemationSmartBlobberResolverBakingGroup));
            context.systemTypesToInject.Add(typeof(AddMasksBakingSystem));
        }
    }
}

