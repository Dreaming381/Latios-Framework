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
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<SceneManagerSystem>(),                 world);
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<DestroyEntitiesOnSceneChangeSystem>(), world);
        }

#if NETCODE_PROJECT
        /// <summary>
        /// When installed in the Editor World, this removes the Disabled component from prespawned ghosts, allowing you to see them.
        /// </summary>
        /// <param name="world">The World where systems should be installed.</param>
        public static void InstallNetCodePreSpawnEnableInEditorSystem(World world)
        {
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<Latios.Compatibility.UnityNetCode.Systems.EnablePreSpawnedGhostsInEditorSystem>(), world);
        }
#endif
    }
}

