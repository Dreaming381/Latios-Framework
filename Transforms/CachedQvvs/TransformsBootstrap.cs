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
            var transformGroup = world.GetExistingSystemManaged<Unity.Transforms.TransformSystemGroup>();
            if (transformGroup != null)
                transformGroup.Enabled = false;

            var companionTransformSystem = world.Unmanaged.GetExistingUnmanagedSystem<Unity.Entities.CompanionGameObjectUpdateTransformSystem>();
            if (world.Unmanaged.IsSystemValid(companionTransformSystem))
                world.Unmanaged.ResolveSystemStateRef(companionTransformSystem).Enabled = false;

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

