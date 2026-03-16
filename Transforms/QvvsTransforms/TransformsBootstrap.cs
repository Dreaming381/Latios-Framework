#if !LATIOS_TRANSFORMS_UNITY

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Transforms
{
    public static class TransformsBootstrap
    {
        public static void InstallTransforms(LatiosWorld world)
        {
            var transformGroup = world.GetExistingSystemManaged<Unity.Transforms.TransformSystemGroup>();
            if (transformGroup != null)
                transformGroup.Enabled = false;

            var companionTransformSystem = world.Unmanaged.GetExistingUnmanagedSystem<Unity.Entities.CompanionGameObjectUpdateTransformSystem>();
            if (world.Unmanaged.IsSystemValid(companionTransformSystem))
                world.Unmanaged.ResolveSystemStateRef(companionTransformSystem).Enabled = false;

            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<Systems.HierarchyCleanupSystem>(),                                   world);
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<Systems.AddCleanupForRootsOfSurvivingChildExpirablesSystem>(),       world);
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<Systems.MotionHistoryInitializeSuperSystem>(),                       world);
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<Systems.MotionHistoryUpdateSuperSystem>(),                           world);
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<Systems.ExportToGameObjectTransformsEndInitializationSuperSystem>(), world);
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<Systems.ExportToGameObjectTransformsEndSimulationSuperSystem>(),     world);
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<Systems.HybridTransformsSyncPointSuperSystem>(),                     world);

#if UNITY_EDITOR
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<Systems.LiveBakingTransformsRecordSystem>(),                         world);
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<Systems.LiveBakingTransformsFixupSystem>(),                          world);
#endif
        }
    }
}
#endif

