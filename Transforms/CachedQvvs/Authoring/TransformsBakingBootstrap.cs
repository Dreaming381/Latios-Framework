#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
using Latios.Authoring;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Transforms.Authoring
{
    public static class TransformsBakingBootstrap
    {
        public static void InstallLatiosTransformsBakers(ref CustomBakingBootstrapContext context)
        {
            var systems = TypeManager.GetSystems(WorldSystemFilterFlags.BakingSystem);
            foreach (var system in systems)
            {
                // Unity forgot to put this system in its namespace.
                if (system.Name == "TransformBakingSystem" && (system.Namespace == null || system.Namespace.StartsWith("Unity")))
                {
                    context.bakingSystemTypesToDisable.Add(system);
                    break;
                }
            }
            context.bakingSystemTypesToInject.Add(typeof(Systems.ExtraTransformComponentsBakingSystem));
            context.bakingSystemTypesToInject.Add(typeof(Systems.TransformBakingSystem));
            context.bakingSystemTypesToInject.Add(typeof(Systems.TransformHierarchySyncBakingSystem));
        }
    }
}

namespace Latios.Transforms.Authoring.Systems
{
    /// <summary>
    /// Updates before all other Transform Systems.
    /// This provides an opportune time for baking systems to request extra transforms components
    /// or manually set up hierarchies that will have the WorldTransform automatically synced afterwards.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    [UpdateInGroup(typeof(TransformBakingSystemGroup))]
    public partial class UserPreTransformsBakingSystemGroup : ComponentSystemGroup
    {
    }
}
#endif

