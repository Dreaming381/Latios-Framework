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
        public static void InstallMecanimBakersAndSystems(ref CustomBakingBootstrapContext context)
        {
#if UNITY_EDITOR
            context.filteredBakerTypes.Add(typeof(Mecanim.Authoring.MecanimSmartBaker));
            context.bakingSystemTypesToInject.Add(TypeManager.GetSystemTypeIndex<Mecanim.Authoring.Systems.MecanimAnimatorControllerSmartBlobberSystem>());
#endif
        }
    }
}

