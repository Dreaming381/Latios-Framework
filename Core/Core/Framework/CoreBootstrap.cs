using Latios.Systems;
using Unity.Entities;
using Unity.Transforms;

namespace Latios
{
    /// <summary>
    /// Static class containing installers for optional runtime features in the Core module
    /// </summary>
    public static unsafe class CoreBootstrap
    {
        /// <summary>
        /// Installs the Scene Management features into the World
        /// </summary>
        /// <param name="world">The World where systems should be installed.</param>
        public static void InstallSceneManager(World world)
        {
            if (world.Flags.HasFlag(WorldFlags.Conversion))
                throw new System.InvalidOperationException("Cannot install Scene Manager in a conversion world.");

            DisallowNetCode("Scene Manager");

            BootstrapTools.InjectSystem(typeof(SceneManagerSystem),                 world);
            BootstrapTools.InjectSystem(typeof(DestroyEntitiesOnSceneChangeSystem), world);
        }

        /// <summary>
        /// Installs Improved Transforms systems into the world and disables some existing transform systems.
        /// Improved Transforms have been tested to be superior to Unity's transform systems in every way.
        /// More code runs in Burst. More entities are culled with change filters. And fewer bugs exist.
        /// Performance is always better, measured to a minimum improvement of 4%, often much higher.
        /// </summary>
        /// <param name="world">The World where systems should be installed.</param>
        public static void InstallImprovedTransforms(World world)
        {
            if (world.Flags.HasFlag(WorldFlags.Conversion))
                throw new System.InvalidOperationException("Cannot install Improved Transforms in a conversion world.");

            if (world.GetExistingSystem<ExtremeLocalToParentSystem>() != null)
            {
                throw new System.InvalidOperationException("Cannot install Improved Transforms when Extreme Transforms are already installed.");
            }

            var unmanaged = world.Unmanaged;

            try
            {
                unmanaged.ResolveSystemState(unmanaged.GetExistingUnmanagedSystem<LocalToParentSystem>().Handle)->Enabled = false;
            }
            catch (System.InvalidOperationException)
            {
                // Failed to find the unmanaged system
            }
            try
            {
                unmanaged.ResolveSystemState(unmanaged.GetExistingUnmanagedSystem<ParentSystem>().Handle)->Enabled = false;
            }
            catch (System.InvalidOperationException)
            {
                // Failed to find the unmanaged system
            }

            BootstrapTools.InjectSystem(typeof(ImprovedParentSystem),        world);
            BootstrapTools.InjectSystem(typeof(ImprovedLocalToParentSystem), world);
        }

        /// <summary>
        /// Installs Extreme Transforms systems into the world and disables some existing transform systems.
        /// Extreme Transforms is designed to make better use of L2 and L3 caches at extremely high entity counts.
        /// It has a significant main thread cost and changes the archetypes of entities (though those changes are
        /// batched with normal hierarchy archetype changes that Unity would normally make).
        /// In extreme circumstances, it performs significantly better than Improved Transforms. But especially suffers
        /// when the majority of entities undergo structural changes between updates.
        /// </summary>
        /// <param name="world"></param>
        public static void InstallExtremeTransforms(World world)
        {
            if (world.Flags.HasFlag(WorldFlags.Conversion))
                throw new System.InvalidOperationException("Cannot install Extreme Transforms in a conversion world.");

            var unmanaged = world.Unmanaged;

            bool caughtException = false;

            try
            {
                unmanaged.GetExistingUnmanagedSystem<ImprovedParentSystem>();
            }
            catch (System.InvalidOperationException)
            {
                // Failed to find the unmanaged system
                caughtException = true;
            }

            if (!caughtException)
                throw new System.InvalidOperationException("Cannot install Extreme Transforms when Improved Transforms are already installed");

            try
            {
                unmanaged.ResolveSystemState(unmanaged.GetExistingUnmanagedSystem<LocalToParentSystem>().Handle)->Enabled = false;
            }
            catch (System.InvalidOperationException)
            {
                // Failed to find the unmanaged system
            }
            try
            {
                unmanaged.ResolveSystemState(unmanaged.GetExistingUnmanagedSystem<ParentSystem>().Handle)->Enabled = false;
            }
            catch (System.InvalidOperationException)
            {
                // Failed to find the unmanaged system
            }

            BootstrapTools.InjectSystem(typeof(ExtremeParentSystem),        world);
            BootstrapTools.InjectSystem(typeof(ExtremeChildDepthsSystem),   world);
            BootstrapTools.InjectSystem(typeof(ExtremeLocalToParentSystem), world);
        }

        [System.Diagnostics.Conditional("NETCODE_PROJECT")]
        private static void DisallowNetCode(string feature)
        {
            throw new System.InvalidOperationException($"{feature} cannot be used in a Unity NetCode project.");
        }
    }
}

