using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Mimic
{
    public static class MimicBootstrap
    {
        /// <summary>
        /// Installs the Mecanim state machine runtime systems. This should only be installed in the runtime world.
        /// </summary>
        /// <param name="world"></param>
        public static void InstallMecanimAddon(World world)
        {
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<Addons.Mecanim.Systems.MecanimSuperSystem>(), world);
        }
    }
}

