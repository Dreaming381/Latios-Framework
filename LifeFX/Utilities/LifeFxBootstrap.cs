using Latios.LifeFX.Systems;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.LifeFX
{
    /// <summary>
    /// Static class containing installers for optional runtime features in the LifeFX module
    /// </summary>
    public static class LifeFXBootstrap
    {
        /// <summary>
        /// Installs LifeFX into the World in the PresentationSystemGroup and enables Kinemation custom graphics.
        /// Install this in both the Runtime World and optionally the editor world if you intend to animate entities in the editor.
        /// Kinemation must be installed first.
        /// </summary>
        /// <param name="world">The world in which LifeFX should be installed</param>
        public static void InstallLifeFX(LatiosWorld world)
        {
            world.worldBlackboardEntity.AddComponent<Kinemation.EnableCustomGraphicsTag>();
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<GraphicsEventUploadSystem>(),           world);
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<GraphicsGlobalBufferBroadcastSystem>(), world);
        }
    }
}

