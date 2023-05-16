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
            DisallowNetCode("Scene Manager");

            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<SceneManagerSystem>(),                 world);
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<DestroyEntitiesOnSceneChangeSystem>(), world);
        }

        [System.Diagnostics.Conditional("NETCODE_PROJECT")]
        private static void DisallowNetCode(string feature)
        {
            throw new System.InvalidOperationException($"{feature} cannot be used in a Unity NetCode project.");
        }
    }
}

