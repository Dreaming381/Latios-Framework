using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Transforms
{
    public static class TransformsBootstrap
    {
        public static void InstallTransforms(World world, ComponentSystemGroup defaultComponentSystemGroup)
        {
            foreach (var system in world.Systems)
            {
                var type = system.GetType();
                if (type.Namespace != null && type.Namespace.StartsWith("Unity.Transforms"))
                {
                    system.Enabled = false;
                }
            }

            BootstrapTools.InjectSystem(typeof(Systems.TransformSuperSystem), world, defaultComponentSystemGroup);
        }
    }
}

