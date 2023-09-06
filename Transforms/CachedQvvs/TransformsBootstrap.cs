#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Transforms
{
    public static class TransformsBootstrap
    {
        public static void InstallTransforms(LatiosWorld world, ComponentSystemGroup defaultComponentSystemGroup, bool extreme = false)
        {
            foreach (var system in world.Systems)
            {
                var type = system.GetType();
                if (type.Namespace != null && type.Namespace.StartsWith("Unity.Transforms"))
                {
                    system.Enabled = false;
                }
                if (type.Name.StartsWith("Companion") && type.Namespace != null && type.Namespace.StartsWith("Unity.Entities"))
                {
                    system.Enabled = false;
                }
            }

            if (extreme)
                world.worldBlackboardEntity.AddComponentData(new RuntimeFeatureFlags { flags = RuntimeFeatureFlags.Flags.ExtremeTransforms });
            else
                world.worldBlackboardEntity.AddComponent<RuntimeFeatureFlags>();

            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<Systems.TransformSuperSystem>(),                 world, defaultComponentSystemGroup);
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<Systems.HybridTransformsSyncPointSuperSystem>(), world);
        }
    }
}
#endif

