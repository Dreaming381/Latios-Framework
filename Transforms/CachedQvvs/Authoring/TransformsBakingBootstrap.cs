#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
using Latios.Authoring;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Mathematics;

namespace Latios.Transforms.Authoring
{
    public static class TransformsBakingBootstrap
    {
        public static void InstallLatiosTransformsBakers(ref CustomBakingBootstrapContext context)
        {
            var systems = TypeManager.GetSystemTypeIndices(WorldSystemFilterFlags.BakingSystem);
            foreach (var system in systems)
            {
                if (system.IsManaged)
                    continue;
                var name = TypeManager.GetSystemName(system);
                if (!name.Contains(new FixedString64Bytes("TransformBakingSystem")))
                    continue;

                var type = system.GetManagedType();
                // Unity forgot to put this system in its namespace.
                if (type.Name == "TransformBakingSystem" && (type.Namespace == null || type.Namespace.StartsWith("Unity")))
                {
                    context.bakingSystemTypesToDisable.Add(system);
                    break;
                }
            }
            context.bakingSystemTypesToInject.Add(TypeManager.GetSystemTypeIndex<Systems.HierarchyUpdateModeFlagsBakingSystem>());
            context.bakingSystemTypesToInject.Add(TypeManager.GetSystemTypeIndex<Systems.ExtraTransformComponentsBakingSystem>());
            context.bakingSystemTypesToInject.Add(TypeManager.GetSystemTypeIndex<Systems.TransformBakingSystem>());
            context.bakingSystemTypesToInject.Add(TypeManager.GetSystemTypeIndex<Systems.TransformHierarchySyncBakingSystem>());
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

