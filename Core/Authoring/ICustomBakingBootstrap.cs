using System.Collections.Generic;
using System.Reflection;
using Unity.Burst;
using Unity.Entities;
using Unity.Entities.Exposed;

namespace Latios.Authoring
{
    /// <summary>
    /// Implement this interface in a bootstrap to customize the baking world similar to runtime ICustomBootstrap.
    /// </summary>
    public interface ICustomBakingBootstrap
    {
        /// <summary>
        /// This function behaves similarly to ICustomBootstrap in that you can customize the conversion process
        /// at startup. However, unlike ICustomBootstrap, this is only invoked once per domain load.
        /// </summary>
        void InitializeBakingForAllWorlds(ref CustomBakingBootstrapContext context);
    }

    /// <summary>
    /// A context object used to customize the baking process.
    /// </summary>
    public struct CustomBakingBootstrapContext
    {
        /// <summary>
        /// Initially contains a list of Bakers Unity found. Bakers with [DisableAutoCreation] are not included in this list.
        /// Modify this list to add or remove Bakers. [DisableAutoCreation] is ignored after this point.
        /// </summary>
        public List<System.Type> filteredBakerTypes;
        /// <summary>
        /// Initially empty, specify a list of system types to disable while baking. Use cases for this are rare.
        /// </summary>
        public List<System.Type> bakingSystemTypesToDisable;
        /// <summary>
        /// Initially empty, specify a list of system types to disable while optimizing a subscene. Use cases for this are rare.
        /// </summary>
        public List<System.Type> optimizationSystemTypesToDisable;
        /// <summary>
        /// Initially empty, specify a list of system types to be auto-created and injected into the baking world.
        /// The new systems will ignore [DisableAutoCreation] and will be created after the original default list
        /// of systems are created.
        /// </summary>
        public List<System.Type> bakingSystemTypesToInject;
        /// <summary>
        /// Initially empty, specify a list of system types to be auto-created and injected into the baking world
        /// when scene optimizations are running (they run twice before writing the subscene to disk).
        /// The new systems will ignore [DisableAutoCreation] and will be created after the original default list
        /// of optimization systems are created.
        /// </summary>
        public List<System.Type> optimizationSystemTypesToInject;
    }

    internal class BakingOverride
    {
        OverrideBakers                       m_overrideBakers;
        public List<ICreateSmartBakerSystem> m_smartBakerSystemCreators;
        public List<System.Type>             m_bakingSystemTypesToDisable;
        public List<System.Type>             m_bakingSystemTypesToInject;
        public List<System.Type>             m_optimizationSystemTypesToDisable;
        public List<System.Type>             m_optimizationSystemTypesToInject;

        public BakingOverride()
        {
#if UNITY_EDITOR
            // Todo: We always do this regardless of if the bakers are used.
            // These bakers we create don't actually get added to the map.
            // And the systems don't do anything without the bakers.
            // Still, it would be nice to not have the systems created at all.
            m_smartBakerSystemCreators = new List<ICreateSmartBakerSystem>();
            foreach (var creatorType in UnityEditor.TypeCache.GetTypesDerivedFrom<ICreateSmartBakerSystem>())
            {
                if (creatorType.IsAbstract || creatorType.ContainsGenericParameters)
                    continue;

                m_smartBakerSystemCreators.Add(System.Activator.CreateInstance(creatorType) as ICreateSmartBakerSystem);
            }

            IEnumerable<System.Type> bootstrapTypes;

            bootstrapTypes = UnityEditor.TypeCache.GetTypesDerivedFrom(typeof(ICustomBakingBootstrap));

            System.Type selectedType = null;

            foreach (var bootType in bootstrapTypes)
            {
                if (bootType.IsAbstract || bootType.ContainsGenericParameters)
                    continue;

                if (selectedType == null)
                    selectedType = bootType;
                else if (selectedType.IsAssignableFrom(bootType))
                    selectedType = bootType;
                else if (!bootType.IsAssignableFrom(selectedType))
                    UnityEngine.Debug.LogError("Multiple custom ICustomConversionBootstrap exist in the project, ignoring " + bootType);
            }
            if (selectedType == null)
                return;

            ICustomBakingBootstrap bootstrap = System.Activator.CreateInstance(selectedType) as ICustomBakingBootstrap;

            var candidateBakers = OverrideBakers.GetDefaultBakerTypes();

            var context = new CustomBakingBootstrapContext
            {
                filteredBakerTypes               = candidateBakers,
                bakingSystemTypesToDisable       = new List<System.Type>(),
                bakingSystemTypesToInject        = new List<System.Type>(),
                optimizationSystemTypesToDisable = new List<System.Type>(),
                optimizationSystemTypesToInject  = new List<System.Type>(),
            };
            bootstrap.InitializeBakingForAllWorlds(ref context);

            m_overrideBakers = new OverrideBakers(true, context.filteredBakerTypes.ToArray());

            m_bakingSystemTypesToDisable       = context.bakingSystemTypesToDisable;
            m_bakingSystemTypesToInject        = context.bakingSystemTypesToInject;
            m_optimizationSystemTypesToDisable = context.optimizationSystemTypesToDisable;
            m_optimizationSystemTypesToInject  = context.optimizationSystemTypesToInject;
#endif
        }

        public void Shutdown() => m_overrideBakers.Dispose();
    }

    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    [UpdateInGroup(typeof(PreBakingSystemGroup))]
    internal partial class BakingBootstrapSystem : SystemBase, IRateManager
    {
        static BakingOverride s_bakingOverride;

        public float Timestep { get; set; }

        protected override void OnCreate()
        {
            if (s_bakingOverride == null)
            {
                s_bakingOverride = new BakingOverride();
            }

            World.GetExistingSystemManaged<PreBakingSystemGroup>().RateManager = this;
        }

        bool m_initialized = false;
        bool m_ranOnce     = false;
        protected override void OnUpdate()
        {
            m_ranOnce = true;
        }

        public bool ShouldGroupUpdate(ComponentSystemGroup group)
        {
            if (!m_initialized)
            {
                var smartBakingSystemGroup = World.GetOrCreateSystemManaged<Systems.SmartBakerBakingGroup>();
                foreach (var creator in s_bakingOverride.m_smartBakerSystemCreators)
                    creator.Create(World, smartBakingSystemGroup);

                if (s_bakingOverride.m_bakingSystemTypesToDisable != null)
                {
                    foreach (var disableType in s_bakingOverride.m_bakingSystemTypesToDisable)
                    {
                        var handle = World.GetExistingSystem(disableType);
                        if (handle != SystemHandle.Null)
                        {
                            World.Unmanaged.ResolveSystemStateRef(handle).Enabled = false;
                        }
                    }
                }

                if (s_bakingOverride.m_bakingSystemTypesToInject != null)
                    BootstrapTools.InjectSystems(s_bakingOverride.m_bakingSystemTypesToInject, World, World.GetExistingSystemManaged<BakingSystemGroup>());

                m_initialized = true;
            }

            var result = !m_ranOnce;
            m_ranOnce  = false;
            return result;
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.EntitySceneOptimizations)]
    internal partial class BakingOptimizationBootstrapSystem : SystemBase, IRateManager
    {
        static BakingOverride s_bakingOverride;

        public float Timestep { get; set; }

        ComponentSystemGroup m_group;

        protected override void OnCreate()
        {
            if (s_bakingOverride == null)
            {
                s_bakingOverride = new BakingOverride();
            }

            System.Type targetGroupType = null;
#if UNITY_EDITOR
            foreach (var groupType in UnityEditor.TypeCache.GetTypesDerivedFrom<ComponentSystemGroup>())
            {
                if (groupType.Name == "OptimizationGroup" && groupType.Namespace != null && groupType.Namespace.StartsWith("Unity.Entities.Streaming"))
                {
                    targetGroupType = groupType;
                    break;
                }
            }
#endif
            m_group             = World.GetExistingSystemManaged(targetGroupType) as ComponentSystemGroup;
            m_group.RateManager = this;
        }

        bool m_initialized = false;
        bool m_ranOnce     = false;
        protected override void OnUpdate()
        {
            m_ranOnce = true;
        }

        public bool ShouldGroupUpdate(ComponentSystemGroup group)
        {
            if (!m_initialized)
            {
                if (s_bakingOverride.m_optimizationSystemTypesToDisable != null)
                {
                    foreach (var disableType in s_bakingOverride.m_optimizationSystemTypesToDisable)
                    {
                        var handle = World.GetExistingSystem(disableType);
                        if (handle != SystemHandle.Null)
                        {
                            World.Unmanaged.ResolveSystemStateRef(handle).Enabled = false;
                        }
                    }
                }

                if (s_bakingOverride.m_optimizationSystemTypesToInject != null)
                    BootstrapTools.InjectSystems(s_bakingOverride.m_optimizationSystemTypesToInject, World, World.GetExistingSystemManaged<BakingSystemGroup>());

                m_initialized = true;
            }

            var result = !m_ranOnce;
            m_ranOnce  = false;
            return result;
        }
    }
}

