using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Latios.Systems;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Exposed;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

using Debug = UnityEngine.Debug;

namespace Latios
{
    /// <summary>
    /// Add this attribute to a system to prevent the system from being injected into the default group.
    /// Only works when using an injection method in BootstrapTools.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = true)]
    public class NoGroupInjectionAttribute : Attribute
    {
    }

    public static class BootstrapTools
    {
        #region SystemManipulation
        /// <summary>
        /// Injects all systems from the systems list in which contain namespaceSubstring within their namespaces.
        /// Automatically creates parent ComponentSystemGroups if necessary.
        /// Use this for libraries which use traditional injection to inject into the default world.
        /// </summary>
        /// <param name="systems">List of systems containing the namespaced systems to inject using world.GetOrCreateSystem</param>
        /// <param name="namespaceSubstring">The namespace substring to query the systems' namespace against</param>
        /// <param name="world">The world to inject the systems into</param>
        /// <param name="defaultGroup">If no UpdateInGroupAttributes exist on the type and this value is not null, the system is injected in this group</param>
        public static void InjectSystemsFromNamespace(NativeList<SystemTypeIndex> systems, string namespaceSubstring, World world, ComponentSystemGroup defaultGroup = null)
        {
            foreach (var system in systems)
            {
                var type = system.GetManagedType();
                if (type.Namespace == null)
                {
                    Debug.LogWarning("No namespace for " + type.ToString());
                    continue;
                }
                else if (!type.Namespace.Contains(namespaceSubstring))
                    continue;

                InjectSystem(system, world, defaultGroup);
            }
        }

        /// <summary>
        /// Injects all systems made by Unity (or systems that use "Unity" in their namespace or assembly) from the systems list.
        /// Automatically creates parent ComponentSystemGroups if necessary.
        /// Use this instead of InjectSystemsFromNamespace because Unity sometimes forgets to put namespaces on things.
        /// </summary>
        /// <param name="systems">List of systems containing the namespaced systems to inject using world.GetOrCreateSystem</param>
        /// <param name="world">The world to inject the systems into</param>
        /// <param name="defaultGroup">If no UpdateInGroupAttributes exist on the type and this value is not null, the system is injected in this group</param>
        /// <param name="silenceWarnings">If false, this method will print warnings about Unity systems that fail to specify a namespace.
        /// Use this to report bugs to Unity, as the presence of these systems prevent startup optimizations.</param>
        public static void InjectUnitySystems(NativeList<SystemTypeIndex> systems, World world, ComponentSystemGroup defaultGroup = null, bool silenceWarnings = true)
        {
            var sysList = new NativeList<SystemTypeIndex>(Allocator.Temp);
            foreach (var system in systems)
            {
                var type = system.GetManagedType();
                if (type.Namespace == null)
                {
                    if (type.Assembly.FullName.Contains("Unity") && !silenceWarnings)
                    {
                        Debug.LogWarning("Hey Unity Devs! You forget a namespace for " + type.ToString());
                    }
                    else
                        continue;
                }
                else if (!type.Namespace.Contains("Unity"))
                    continue;

                sysList.Add(system);
            }

            InjectSystems(sysList, world, defaultGroup);
        }

        /// <summary>
        /// Injects all systems not made by Unity (or systems that use "Unity" in their namespace or assembly)
        /// and not part of a module installer from the systems list. Automatically creates parent ComponentSystemGroups if necessary.
        /// It is recommended to use this after installing modules with module installers.
        /// </summary>
        /// <param name="systems">List of systems containing the namespaced systems to inject using world.GetOrCreateSystem</param>
        /// <param name="world">The world to inject the systems into</param>
        /// <param name="defaultGroup">If no UpdateInGroupAttributes exist on the type and this value is not null, the system is injected in this group</param>
        public static void InjectUserSystems(NativeList<SystemTypeIndex> systems, World world, ComponentSystemGroup defaultGroup)
        {
            var sysList = new NativeList<SystemTypeIndex>(Allocator.Temp);
            foreach (var system in systems)
            {
                var type = system.GetManagedType();
                if (type.Namespace == null)
                {
                    if (type.Assembly.FullName.Contains("Unity"))
                        continue;
                }
                else if (type.Namespace.Contains("Unity"))
                    continue;

                sysList.Add(system);
            }

            InjectSystems(sysList, world, defaultGroup);
        }

        /// <summary>
        /// Injects all RootSuperSystems in the systems list into the world.
        /// Automatically creates parent ComponentSystemGroups if necessary.
        /// </summary>
        /// <param name="systems">List of systems containing the RootSuperSystems to inject using world.GetOrCreateSystem</param>
        /// <param name="world">The world to inject the systems into</param>
        /// <param name="defaultGroup">If no UpdateInGroupAttributes exist on the type and this value is not null, the system is injected in this group</param>
        public static void InjectRootSuperSystems(NativeList<SystemTypeIndex> systems, World world, ComponentSystemGroup defaultGroup = null)
        {
            foreach (var system in systems)
            {
                var type = system.GetManagedType();
                if (typeof(RootSuperSystem).IsAssignableFrom(type))
                    InjectSystem(system, world, defaultGroup);
            }
        }

        /// <summary>
        /// Finds all groups recursively included in the specified group via UpdateInGroup attributes and adds them to the hashset.
        /// This can then be used to check if non-group systems are a descendant of the group.
        /// </summary>
        /// <typeparam name="T">The group all other groups should be descendants of</typeparam>
        /// <param name="allSystems">A list of systems to consider</param>
        /// <param name="typesToAdd">The hashset where found groups should be added</param>
        public static void AddGroupAndAllNestedGroupsInsideToFilter<T>(NativeList<SystemTypeIndex>        allSystems,
                                                                       ref NativeHashSet<SystemTypeIndex> typesToAdd) where T : ComponentSystemGroup
        {
            var rootGroup = TypeManager.GetSystemTypeIndex<T>();
            typesToAdd.Add(rootGroup);
            var attrMap = new NativeHashMap<SystemTypeIndex, NativeList<SystemTypeIndex> >(allSystems.Length, Allocator.Temp);
            foreach (var s in allSystems)
            {
                if (!s.IsGroup)
                    continue;

                attrMap.Add(s, s.GetUpdateInGroupTargets());
            }

            var systemsToRemove = new NativeList<SystemTypeIndex>(Allocator.Temp);
            var locallyAddedSet = new NativeHashSet<SystemTypeIndex>(allSystems.Length, Allocator.Temp);
            locallyAddedSet.Add(rootGroup);
            bool emptyPass = false;
            while (!emptyPass)
            {
                emptyPass = true;
                systemsToRemove.Clear();
                foreach (var pair in attrMap)
                {
                    foreach (var parent in pair.Value)
                    {
                        if (locallyAddedSet.Contains(parent))
                        {
                            emptyPass = false;
                            systemsToRemove.Add(pair.Key);
                            locallyAddedSet.Add(pair.Key);
                            typesToAdd.Add(pair.Key);
                            break;
                        }
                    }
                }
                foreach (var s in systemsToRemove)
                    attrMap.Remove(s);
            }
        }

        public struct ComponentSystemBaseSystemHandleUnion
        {
            public ComponentSystemBase systemManaged;
            public SystemHandle        systemHandle;

            public static implicit operator ComponentSystemBase(ComponentSystemBaseSystemHandleUnion me) => me.systemManaged;
            public static implicit operator SystemHandle(ComponentSystemBaseSystemHandleUnion me) => me.systemHandle;
        }

        //Copied and pasted from Entities package and then modified as needed.
        /// <summary>
        /// Injects the system into the world. Automatically creates parent ComponentSystemGroups if necessary.
        /// </summary>
        /// <remarks>This function does nothing for unmanaged systems.</remarks>
        /// <param name="type">The type to inject. Uses world.GetOrCreateSystem</param>
        /// <param name="world">The world to inject the system into</param>
        /// <param name="defaultGroup">If no UpdateInGroupAttributes exist on the type and this value is not null, the system is injected in this group</param>
        /// <param name="groupRemap">If a type in an UpdateInGroupAttribute matches a key in this dictionary, it will be swapped with the value</param>
        public static ComponentSystemBaseSystemHandleUnion InjectSystem(SystemTypeIndex type,
                                                                        World world,
                                                                        ComponentSystemGroup defaultGroup                          = null,
                                                                        NativeHashMap<SystemTypeIndex, SystemTypeIndex> groupRemap = default)
        {
            bool isManaged = type.IsManaged;
            var  groups    = type.GetUpdateInGroupTargets();

            ComponentSystemBaseSystemHandleUnion newSystem = default;
            if (isManaged)
            {
                newSystem.systemManaged = world.GetOrCreateSystemManaged(type);
                newSystem.systemHandle  = newSystem.systemManaged.SystemHandle;
            }
            else
            {
                newSystem.systemHandle = world.GetOrCreateSystem(type);
            }
            if (groups.Length == 0 && defaultGroup != null)
            {
                if (isManaged)
                    defaultGroup.AddSystemToUpdateList(newSystem.systemManaged);
                else
                    defaultGroup.AddSystemToUpdateList(newSystem.systemHandle);
            }
            foreach (var g in groups)
            {
                if (TypeManager.GetSystemAttributes(newSystem.GetType(), typeof(NoGroupInjectionAttribute)).Length > 0)
                    break;

                var group = FindOrCreateGroup(world, type, g, defaultGroup, groupRemap);
                if (group != null)
                {
                    if (isManaged)
                        group.AddSystemToUpdateList(newSystem.systemManaged);
                    else
                        group.AddSystemToUpdateList(newSystem.systemHandle);
                }
            }
            return newSystem;
        }

        //Copied and pasted from Entities package and then modified as needed.
        /// <summary>
        /// Injects the systems into the world. Automatically creates parent ComponentSystemGroups if necessary.
        /// GetExistingSystem is valid in OnCreate for all systems within types as well as previously added systems.
        /// </summary>
        /// <remarks>This function does nothing for unmanaged systems.</remarks>
        /// <param name="types">The types to inject.</param>
        /// <param name="world">The world to inject the system into</param>
        /// <param name="defaultGroup">If no UpdateInGroupAttributes exist on the type and this value is not null, the system is injected in this group</param>
        /// <param name="groupRemap">If a type in an UpdateInGroupAttribute matches a key in this dictionary, it will be swapped with the value</param>
        public static ComponentSystemBaseSystemHandleUnion[] InjectSystems(NativeList<SystemTypeIndex> types,
                                                                           World world,
                                                                           ComponentSystemGroup defaultGroup                          = null,
                                                                           NativeHashMap<SystemTypeIndex, SystemTypeIndex> groupRemap = default)
        {
            var systems = world.GetOrCreateSystemsAndLogException(types, Allocator.Temp);

            // Add systems to their groups, based on the [UpdateInGroup] attribute.
            int typesIndex = -1;
            foreach (var system in systems)
            {
                typesIndex++;
                if (system == SystemHandle.Null)
                    continue;

                // Skip the built-in root-level system groups
                var type = types[typesIndex];

                if (type.IsManaged)
                {
                    var managedSystem             = world.AsManagedSystem(system);
                    var noUpdateInGroupAttributes = TypeManager.GetSystemAttributes(managedSystem.GetType(), typeof(NoGroupInjectionAttribute));
                    if (noUpdateInGroupAttributes.Length > 0)
                        continue;
                }

                var updateInGroupAttributes = type.GetUpdateInGroupTargets();
                if (updateInGroupAttributes.Length == 0)
                {
                    if (defaultGroup.SystemHandle != system)
                        defaultGroup.AddSystemToUpdateList(system);
                }

                foreach (var attr in updateInGroupAttributes)
                {
                    var group = FindOrCreateGroup(world, type, attr, defaultGroup, groupRemap);
                    if (group != null)
                    {
                        group.AddSystemToUpdateList(system);
                    }
                }
            }

            var result = new ComponentSystemBaseSystemHandleUnion[systems.Length];
            for (int i = 0; i < systems.Length; i++)
            {
                if (types[i].IsManaged)
                {
                    result[i] = new ComponentSystemBaseSystemHandleUnion
                    {
                        systemManaged = world.AsManagedSystem(systems[i]),
                        systemHandle  = systems[i]
                    };
                }
                else
                {
                    result[i] = new ComponentSystemBaseSystemHandleUnion
                    {
                        systemManaged = null,
                        systemHandle  = systems[i]
                    };
                }
            }

            return result;
        }

        private static ComponentSystemGroup FindOrCreateGroup(World world,
                                                              SystemTypeIndex systemType,
                                                              SystemTypeIndex targetGroup,
                                                              ComponentSystemGroup defaultGroup,
                                                              NativeHashMap<SystemTypeIndex,
                                                                            SystemTypeIndex> remap)
        {
            if (targetGroup == SystemTypeIndex.Null)
                return null;

            if (remap.IsCreated && remap.TryGetValue(targetGroup, out var remapType))
                targetGroup = remapType;
            if (targetGroup == SystemTypeIndex.Null)
                return null;

            if (!targetGroup.IsGroup)
            {
                throw new InvalidOperationException($"Invalid [UpdateInGroup] attribute for {systemType}: {targetGroup} must be derived from ComponentSystemGroup.");
            }

            var groupSys = world.GetExistingSystemManaged(targetGroup);
            if (groupSys == null)
            {
                groupSys = InjectSystem(targetGroup, world, defaultGroup, remap);
            }

            return groupSys as ComponentSystemGroup;
        }

        /// <summary>
        /// Builds a ComponentSystemGroup and auto-injects children systems in the list.
        /// Systems without an UpdateInGroupAttribute are not added.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="systems">Systems that are allowed to be added to the Group hierarchy</param>
        /// <param name="world">The world in which the systems are built</param>
        /// <returns></returns>
        public static T BuildSystemGroup<T>(List<Type> systems, World world) where T : ComponentSystemGroup
        {
            var groupsToCreate = new HashSet<Type>();
            var allGroups      = new List<Type>();
            foreach (var system in systems)
            {
                if (IsGroup(system))
                    allGroups.Add(system);
            }
            AddChildrenGroupsToHashsetRecursively(typeof(T), allGroups, groupsToCreate);

            var groupList = new List<(Type, ComponentSystemGroup)>();
            foreach (var system in groupsToCreate)
            {
                groupList.Add((system, world.CreateSystemManaged(system) as ComponentSystemGroup));
            }

            foreach (var system in groupList)
            {
                foreach (var targetGroup in groupList)
                {
                    if (IsInGroup(system.Item1, targetGroup.Item1))
                        targetGroup.Item2.AddSystemToUpdateList(system.Item2);
                }
            }

            foreach (var system in systems)
            {
                if (IsGroup(system))
                    continue;
                if (!typeof(ComponentSystemBase).IsAssignableFrom(system))
                    continue;
                foreach (var targetGroup in groupList)
                {
                    if (IsInGroup(system, targetGroup.Item1))
                        targetGroup.Item2.AddSystemToUpdateList(world.GetOrCreateSystem(system));
                }
            }

            foreach (var group in groupList)
            {
                if (group.Item1 == typeof(T))
                {
                    group.Item2.SortSystems();
                    return group.Item2 as T;
                }
            }

            return null;
        }

        /// <summary>
        /// Is the system a type of ComponentSystemGroup?
        /// </summary>
        public static bool IsGroup(Type systemType)
        {
            return typeof(ComponentSystemGroup).IsAssignableFrom(systemType);
        }

        /// <summary>
        /// Does the system want to be injected in the group?
        /// </summary>
        /// <param name="systemType">The type of system to be injected</param>
        /// <param name="groupType">The type of group that would be specified in the UpdateInGroupAttribute</param>
        /// <returns></returns>
        public static bool IsInGroup(Type systemType, Type groupType)
        {
            var atts = systemType.GetCustomAttributes(typeof(UpdateInGroupAttribute), true);
            foreach (var att in atts)
            {
                if (!(att is UpdateInGroupAttribute uig))
                    continue;
                if (uig.GroupType.IsAssignableFrom(groupType))
                {
                    return true;
                }
            }
            return false;
        }

        private static void AddChildrenGroupsToHashsetRecursively(Type startType, List<Type> componentSystemGroups, HashSet<Type> foundGroups)
        {
            if (!foundGroups.Contains(startType))
            {
                foundGroups.Add(startType);
            }
            foreach (var system in componentSystemGroups)
            {
                if (!foundGroups.Contains(system) && IsInGroup(system, startType))
                    AddChildrenGroupsToHashsetRecursively(system, componentSystemGroups, foundGroups);
            }
        }
        #endregion

        #region PlayerLoop

        /// <summary>
        /// Update the player loop with a world's root-level systems including FixedUpdate
        /// </summary>
        /// <param name="world">World with root-level systems that need insertion into the player loop</param>
        public static void AddWorldToCurrentPlayerLoopWithFixedUpdate(World world)
        {
            var playerLoop = PlayerLoop.GetCurrentPlayerLoop();

            if (world != null)
            {
                ScriptBehaviourUpdateOrder.AppendSystemToPlayerLoop(world.GetExistingSystemManaged<InitializationSystemGroup>(),  ref playerLoop, typeof(Initialization));
                ScriptBehaviourUpdateOrder.AppendSystemToPlayerLoop(world.GetExistingSystemManaged<SimulationSystemGroup>(),      ref playerLoop, typeof(PostLateUpdate));
                ScriptBehaviourUpdateOrder.AppendSystemToPlayerLoop(world.GetExistingSystemManaged<PresentationSystemGroup>(),    ref playerLoop, typeof(PreLateUpdate));
                ScriptBehaviourUpdateOrder.AppendSystemToPlayerLoop(world.GetExistingSystemManaged<FixedSimulationSystemGroup>(), ref playerLoop, typeof(FixedUpdate));
            }
            PlayerLoop.SetPlayerLoop(playerLoop);
        }

        /// <summary>
        /// Update the PlayerLoop to run simulation after rendering.
        /// </summary>
        /// <param name="world">World with root-level systems that need insertion into the player loop</param>
        public static void AddWorldToCurrentPlayerLoopWithDelayedSimulation(World world)
        {
            var playerLoop = PlayerLoop.GetCurrentPlayerLoop();

            if (world != null)
            {
                InjectSystem(TypeManager.GetSystemTypeIndex<DeferredSimulationEndFrameControllerSystem>(), world);

                ScriptBehaviourUpdateOrder.AppendSystemToPlayerLoop(world.GetExistingSystemManaged<InitializationSystemGroup>(), ref playerLoop,
                                                                    typeof(Initialization));
                // We add it here for visibility in tools. But really we don't update until EndOfFrame
                WorldExposedExtensions.AddDummyRootLevelSystemToPlayerLoop(world.GetExistingSystemManaged<SimulationSystemGroup>(), ref playerLoop);
                ScriptBehaviourUpdateOrder.AppendSystemToPlayerLoop(world.GetExistingSystemManaged<PresentationSystemGroup>(), ref playerLoop,
                                                                    typeof(PreLateUpdate));
            }
            PlayerLoop.SetPlayerLoop(playerLoop);
        }
        #endregion

        #region TypeManager
        public static bool IsAssemblyReferencingLatios(Assembly assembly)
        {
            return IsAssemblyReferencingSubstring(assembly, "Latios");
        }

        public static bool IsAssemblyReferencingSubstring(Assembly assembly, string nameSubstring)
        {
            if (assembly.GetName().Name.Contains(nameSubstring))
                return true;

            var referencedAssemblies = assembly.GetReferencedAssemblies();
            foreach (var referenced in referencedAssemblies)
                if (referenced.Name.Contains(nameSubstring))
                    return true;
            return false;
        }

        internal static T TryCreateCustomBootstrap<T>()
        {
            IEnumerable<System.Type> bootstrapTypes;
#if UNITY_EDITOR
            bootstrapTypes = UnityEditor.TypeCache.GetTypesDerivedFrom(typeof(T));
#else

            var types = new List<System.Type>();
            var type  = typeof(T);
            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!BootstrapTools.IsAssemblyReferencingLatios(assembly))
                    continue;

                try
                {
                    var assemblyTypes = assembly.GetTypes();
                    foreach (var t in assemblyTypes)
                    {
                        if (type.IsAssignableFrom(t))
                            types.Add(t);
                    }
                }
                catch (System.Reflection.ReflectionTypeLoadException e)
                {
                    foreach (var t in e.Types)
                    {
                        if (t != null && type.IsAssignableFrom(t))
                            types.Add(t);
                    }

                    UnityEngine.Debug.LogWarning($"{nameof(T)} failed loading assembly: {(assembly.IsDynamic ? assembly.ToString() : assembly.Location)}");
                }
            }

            bootstrapTypes = types;
#endif

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
                    UnityEngine.Debug.LogError($"Multiple custom {nameof(T)} exist in the project, ignoring {bootType}");
            }
            if (selectedType == null)
                return default;

            return (T)System.Activator.CreateInstance(selectedType);
        }
        #endregion
    }
}

