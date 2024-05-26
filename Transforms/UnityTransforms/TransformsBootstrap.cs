#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && LATIOS_TRANSFORMS_UNITY

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Transforms
{
    public static class TransformsBootstrap
    {
        public static void InstallGameObjectEntitySynchronization(LatiosWorld world)
        {
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<Systems.GameObjectEntityBindingSystem>(),           world);
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<Systems.CopyGameObjectTransformToEntitySystem>(),   world);
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<Systems.CopyGameObjectTransformFromEntitySystem>(), world);
        }
    }
}
#endif

