﻿using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Burst;
using Unity.Collections;
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
        /// Initially allocated but empty, specify a list of system types to disable while baking. Use cases for this are rare.
        /// </summary>
        public NativeList<SystemTypeIndex> bakingSystemTypesToDisable;
        /// <summary>
        /// Initially allocated but empty, specify a list of system types to disable while optimizing a subscene. Use cases for this are rare.
        /// </summary>
        public NativeList<SystemTypeIndex> optimizationSystemTypesToDisable;
        /// <summary>
        /// Initially allocated but empty, specify a list of system types to be auto-created and injected into the baking world.
        /// The new systems will ignore [DisableAutoCreation] and will be created after the original default list
        /// of systems are created.
        /// </summary>
        public NativeList<SystemTypeIndex> bakingSystemTypesToInject;
        /// <summary>
        /// Initially allocated but empty, specify a list of system types to be auto-created and injected into the baking world
        /// when scene optimizations are running (they run twice before writing the subscene to disk).
        /// The new systems will ignore [DisableAutoCreation] and will be created after the original default list
        /// of optimization systems are created.
        /// </summary>
        public NativeList<SystemTypeIndex> optimizationSystemTypesToInject;
    }

    internal class BakingOverride
    {
        OverrideBakers                       m_overrideBakers;
        public List<ICreateSmartBakerSystem> m_smartBakerSystemCreators;
        public NativeList<SystemTypeIndex>   m_bakingSystemTypesToDisable;
        public NativeList<SystemTypeIndex>   m_bakingSystemTypesToInject;
        public NativeList<SystemTypeIndex>   m_optimizationSystemTypesToDisable;
        public NativeList<SystemTypeIndex>   m_optimizationSystemTypesToInject;

        static bool s_appDomainUnloadRegistered;
        static bool s_initialized;

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
                bakingSystemTypesToDisable       = new NativeList<SystemTypeIndex>(Allocator.Temp),
                bakingSystemTypesToInject        = new NativeList<SystemTypeIndex>(Allocator.Temp),
                optimizationSystemTypesToDisable = new NativeList<SystemTypeIndex>(Allocator.Temp),
                optimizationSystemTypesToInject  = new NativeList<SystemTypeIndex>(Allocator.Temp),
            };
            bootstrap.InitializeBakingForAllWorlds(ref context);

            m_overrideBakers = new OverrideBakers(true, context.filteredBakerTypes.ToArray());

            m_bakingSystemTypesToDisable       = new NativeList<SystemTypeIndex>(context.bakingSystemTypesToDisable.Length, Allocator.Persistent);
            m_bakingSystemTypesToInject        = new NativeList<SystemTypeIndex>(context.bakingSystemTypesToInject.Length, Allocator.Persistent);
            m_optimizationSystemTypesToDisable = new NativeList<SystemTypeIndex>(context.optimizationSystemTypesToDisable.Length, Allocator.Persistent);
            m_optimizationSystemTypesToInject  = new NativeList<SystemTypeIndex>(context.optimizationSystemTypesToInject.Length, Allocator.Persistent);
            m_bakingSystemTypesToDisable.AddRange(context.bakingSystemTypesToDisable.AsArray());
            m_bakingSystemTypesToInject.AddRange(context.bakingSystemTypesToInject.AsArray());
            m_optimizationSystemTypesToDisable.AddRange(context.optimizationSystemTypesToDisable.AsArray());
            m_optimizationSystemTypesToInject.AddRange(context.optimizationSystemTypesToInject.AsArray());

            if (!s_appDomainUnloadRegistered)
            {
                // important: this will always be called from a special unload thread (main thread will be blocking on this)
                AppDomain.CurrentDomain.DomainUnload += (_, __) =>
                {
                    if (s_initialized)
                        Shutdown();
                };

                s_appDomainUnloadRegistered = true;
            }
            s_initialized = true;
#endif
        }

        public void Shutdown()
        {
            m_overrideBakers.Dispose();
            m_bakingSystemTypesToDisable.Dispose();
            m_bakingSystemTypesToInject.Dispose();
            m_optimizationSystemTypesToDisable.Dispose();
            m_optimizationSystemTypesToInject.Dispose();
        }
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

                if (s_bakingOverride.m_bakingSystemTypesToDisable.IsCreated)
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

                if (s_bakingOverride.m_bakingSystemTypesToInject.IsCreated)
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
                if (s_bakingOverride.m_optimizationSystemTypesToDisable.IsCreated)
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

                if (s_bakingOverride.m_optimizationSystemTypesToInject.IsCreated)
                    BootstrapTools.InjectSystems(s_bakingOverride.m_optimizationSystemTypesToInject, World, World.GetExistingSystemManaged<BakingSystemGroup>());

                m_initialized = true;
            }

            var result = !m_ranOnce;
            m_ranOnce  = false;
            return result;
        }
    }
}

