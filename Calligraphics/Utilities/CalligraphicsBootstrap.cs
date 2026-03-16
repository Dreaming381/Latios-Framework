using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Calligraphics
{
    /// <summary>
    /// Static class containing installers for optional runtime features in the Calligraphics module
    /// </summary>
    public static class CalligraphicsBootstrap
    {
        /// <summary>
        /// Installs Calligraphics into the World in the PresentationSystemGroup.
        /// Install this in both the Editor and Runtime Worlds.
        /// Kinemation must be installed first.
        /// </summary>
        /// <param name="world">The world in which Calligraphics should be installed</param>
        public static void InstallCalligraphics(World world)
        {
            if (world.Flags.HasFlag(WorldFlags.Conversion))
                throw new System.InvalidOperationException("Cannot install Calligraphics runtime in a conversion world.");

            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<Systems.CalligraphicsFrameSyncPointSuperSystem>(),  world);
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<Systems.CalligraphicsRenderSyncPointSuperSystem>(), world);
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<Systems.CalligraphicsPresentationSuperSystem>(),    world);
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<Systems.DispatchGlyphsSystem>(),                    world);
        }
    }
}

