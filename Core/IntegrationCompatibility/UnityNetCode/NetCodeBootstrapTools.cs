#if NETCODE_PROJECT
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Mathematics;
using Unity.NetCode;

namespace Latios.Compatibility.UnityNetCode
{
    public static class NetCodeBootstrapTools
    {
        public static void InitializeNetCodeSingletonsOnTheWorldBlackboardEntity(LatiosWorld world)
        {
            //world.worldBlackboardEntity.AddComponent<RpcCollection>();
            if (world.IsClient() && !world.IsThinClient())
            {
                //world.worldBlackboardEntity.AddComponent<GhostPredictionSmoothing>();
            }
        }

        public static void EnableDynamicAssembliesList(WorldUnmanaged world)
        {
            using var query = world.EntityManager.CreateEntityQuery(new EntityQueryBuilder(
                                                                        Allocator.Temp).WithAllRW<RpcCollection>());
            query.GetSingletonRW<RpcCollection>().ValueRW.DynamicAssemblyList = true;
        }

        /// <summary>
        /// Injects all systems made by Unity (or systems that use "Unity" in their namespace or assembly) from the systems list.
        /// Also injects all systems containing "_Generated_" or ".Generated." in their full name as these are typically NetCode generated systems.
        /// Also injects all systems updating in the DefaultVariantSystemGroup since these have explicit creation order requirements.
        /// Automatically creates parent ComponentSystemGroups if necessary.
        /// </summary>
        /// <param name="systems">List of systems containing the namespaced systems to inject using world.GetOrCreateSystem</param>
        /// <param name="world">The world to inject the systems into</param>
        /// <param name="defaultGroup">If no UpdateInGroupAttributes exist on the type and this value is not null, the system is injected in this group</param>
        /// <param name="silenceWarnings">If false, this method will print warnings about Unity systems that fail to specify a namespace.
        /// Use this to report bugs to Unity, as the presence of these systems prevent startup optimizations.</param>
        public static void InjectUnityAndNetCodeGeneratedSystems(NativeList<SystemTypeIndex> systems,
                                                                 World world,
                                                                 ComponentSystemGroup defaultGroup    = null,
                                                                 bool silenceWarnings = true)
        {
            var sysList       = new NativeList<SystemTypeIndex>(Allocator.Temp);
            var variantGroups = new NativeHashSet<SystemTypeIndex>(systems.Length, Allocator.Temp);
            BootstrapTools.AddGroupAndAllNestedGroupsInsideToFilter<DefaultVariantSystemGroup>(systems, ref variantGroups);

            foreach (var system in systems)
            {
                if (variantGroups.Contains(system))
                {
                    sysList.Add(system);
                    continue;
                }
                bool found = false;
                foreach (var parent in system.GetUpdateInGroupTargets())
                {
                    if (variantGroups.Contains(parent))
                    {
                        found = true;
                        sysList.Add(system);
                        break;
                    }
                }
                if (found)
                    continue;

                var type = system.GetManagedType();
                if (type.Namespace == null)
                {
                    if (type.Assembly.FullName.Contains("Unity") && !silenceWarnings)
                    {
                        UnityEngine.Debug.LogWarning("Hey Unity Devs! You forget a namespace for " + type.ToString());
                    }
                    else if (!type.FullName.Contains(".Generated.") && !type.FullName.Contains("_Generated_"))
                        continue;
                }
                else if (!type.Namespace.Contains("Unity") && !type.FullName.Contains(".Generated.") && !type.FullName.Contains("_Generated_"))
                    continue;

                sysList.Add(system);
            }

            BootstrapTools.InjectSystems(sysList, world, defaultGroup);
        }
    }
}
#endif

