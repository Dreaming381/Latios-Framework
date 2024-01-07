using Latios.Authoring;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Mimic.Authoring
{
    public static class MimicBakingBootstrap
    {
        /// <summary>
        /// Adds Mecanim bakers and baking systems into baking world
        /// </summary>
        public static void InstallMecanimAddon(ref CustomBakingBootstrapContext context)
        {
#if UNITY_EDITOR
            context.filteredBakerTypes.Add(typeof(Addons.Mecanim.Authoring.MecanimSmartBaker));
            context.bakingSystemTypesToInject.Add(TypeManager.GetSystemTypeIndex<Addons.Mecanim.Authoring.Systems.MecanimAnimatorControllerSmartBlobberSystem>());
#endif
        }
    }
}

