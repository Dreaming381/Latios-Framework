using Latios.Authoring;
using Latios.Kinemation.Authoring.Systems;
using Unity.Entities;
using Unity.Rendering;

namespace Latios.Kinemation.Authoring
{
    /// <summary>
    /// Static class containing installers for optional authoring time features in the Kinemation module
    /// </summary>
    public static class KinemationConversionBootstrap
    {
        /// <summary>
        /// Adds Kinemation conversion systems into conversion world and disables the Hybrid Renderer's SkinnedMeshRenderer conversion
        /// </summary>
        /// <param name="world">The conversion world in which to install the Kinemation conversion systems</param>
        public static void InstallKinemationConversion(World world)
        {
            if (!world.Flags.HasFlag(WorldFlags.Conversion))
                throw new System.InvalidOperationException("Kinemation Conversion must be installed in a conversion world.");

            {
                var builtinConversionSystem = world.GetExistingSystem<SkinnedMeshRendererConversion>();
                if (builtinConversionSystem != null)
                    builtinConversionSystem.Enabled = false;
            }

            BootstrapTools.InjectSystem(typeof(DiscoverSkeletonsConversionSystem),            world);
            BootstrapTools.InjectSystem(typeof(DiscoverUnboundSkinnedMeshesConversionSystem), world);
            BootstrapTools.InjectSystem(typeof(SkeletonPathsSmartBlobberSystem),              world);
            BootstrapTools.InjectSystem(typeof(SkeletonHierarchySmartBlobberSystem),          world);
            BootstrapTools.InjectSystem(typeof(MeshSkinningSmartBlobberSystem),               world);
            BootstrapTools.InjectSystem(typeof(MeshPathsSmartBlobberSystem),                  world);
            BootstrapTools.InjectSystem(typeof(SkeletonClipSetSmartBlobberSystem),            world);
            BootstrapTools.InjectSystem(typeof(KinemationCleanupConversionSystem),            world);
            var system = BootstrapTools.InjectSystem(typeof(AddMasksConversionSystem),                     world);

            var cs                                                                           = system.systemManaged as GameObjectConversionSystem;
            cs.GetSettings().OnPostCreateConversionWorldWrapper.OnPostCreateConversionWorld += (w, s) =>
            {
                var builtinConversionSystem = world.GetExistingSystem<SkinnedMeshRendererConversion>();
                if (builtinConversionSystem != null)
                    builtinConversionSystem.Enabled = false;
            };
        }
    }
}

