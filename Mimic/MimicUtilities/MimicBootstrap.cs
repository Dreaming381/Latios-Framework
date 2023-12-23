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
        public static void InstallMecanim(World world)
        {
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<Mecanim.Systems.MecanimSuperSystem>(), world);
        }
    }
}

