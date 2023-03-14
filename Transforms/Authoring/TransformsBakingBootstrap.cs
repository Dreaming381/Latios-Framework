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
            context.bakingSystemTypesToInject.Add(typeof(Systems.TransformBakingSystem));
            context.bakingSystemTypesToInject.Add(typeof(Systems.ExtraTransformComponentsBakingSystem));
        }
    }
}

