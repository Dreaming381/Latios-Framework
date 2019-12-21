using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Debug = UnityEngine.Debug;
using Unity.Entities;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace Latios
{
    public static class BootstrapTools
    {
        #region SystemManipulation
        /// <summary>
        /// Injects all systems in which contain 'namespaceSubstring within their namespaces.
        /// Automatically creates parent ComponentSystemGroups if necessary.
        /// Use this for libraries which use traditional injection to inject into the default world.
        /// </summary>
        /// <param name="systems">List of systems containing the namespaced systems to inject using world.GetOrCreateSystem</param>
        /// <param name="namespaceSubstring">The namespace substring to query the systems' namespace against</param>
        /// <param name="world">The world to inject the systems into</param>
        /// <param name="defaultGroup">If no UpdateInGroupAttributes exist on the type and this value is not null, the system is injected in this group</param>
        public static void InjectSystemsFromNamespace(List<Type> systems, string namespaceSubstring, World world, ComponentSystemGroup defaultGroup = null)
        {
            foreach (var type in systems)
            {
                if (type.Namespace == null)
                {
                    Debug.LogWarning("No namespace for " + type.ToString());
                    continue;
                }
                else if (!type.Namespace.Contains(namespaceSubstring))
                    continue;

                InjectSystem(type, world, defaultGroup);
            }
        }

        /// <summary>
        /// Injects all RootSuperSystems in the systems list into the world.
        /// Automatically creates parent ComponentSystemGroups if necessary.
        /// </summary>
        /// <param name="systems">List of systems containing the RootSuperSystems to inject using world.GetOrCreateSystem</param>
        /// <param name="world">The world to inject the systems into</param>
        /// <param name="defaultGroup">If no UpdateInGroupAttributes exist on the type and this value is not null, the system is injected in this group</param>
        public static void InjectRootSuperSystems(List<Type> systems, World world, ComponentSystemGroup defaultGroup = null)
        {
            foreach (var type in systems)
            {
                if (typeof(RootSuperSystem).IsAssignableFrom(type))
                    InjectSystem(type, world, defaultGroup);
            }
        }

        //Copied and pasted from Entities package and then modified as needed.
        /// <summary>
        /// Injects the system into the world. Automatically creates parent ComponentSystemGroups if necessary.
        /// </summary>
        /// <param name="type">The type to inject. Uses world.GetOrCreateSystem</param>
        /// <param name="world">The world to inject the system into</param>
        /// <param name="defaultGroup">If no UpdateInGroupAttributes exist on the type and this value is not null, the system is injected in this group</param>
        public static ComponentSystemBase InjectSystem(Type type, World world, ComponentSystemGroup defaultGroup = null)
        {
            var                 groups = type.GetCustomAttributes(typeof(UpdateInGroupAttribute), true);
            ComponentSystemBase result = null;
            if (groups.Length == 0 && defaultGroup != null)
            {
                result = world.GetOrCreateSystem(type);
                defaultGroup.AddSystemToUpdateList(result);
                return result;
            }

            foreach (var g in groups)
            {
                if (!(g is UpdateInGroupAttribute group))
                    continue;

                if (!(typeof(ComponentSystemGroup)).IsAssignableFrom(group.GroupType))
                {
                    Debug.LogError($"Invalid [UpdateInGroup] attribute for {type}: {group.GroupType} must be derived from ComponentSystemGroup.");
                    continue;
                }

                var groupMgr = world.GetExistingSystem(group.GroupType);
                if (groupMgr == null)
                {
                    groupMgr = InjectSystem(group.GroupType, world, defaultGroup);
                }
                var groupTarget = groupMgr as ComponentSystemGroup;
                result          = world.GetOrCreateSystem(type);
                groupTarget.AddSystemToUpdateList(result);
            }
            return result;
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
                groupList.Add((system, world.CreateSystem(system) as ComponentSystemGroup));
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
                    group.Item2.SortSystemUpdateList();
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
        //Mostly copied from ScriptBehaviourUpdateOrder

        /// <summary>
        /// Update the player loop with a world's root-level systems including FixedUpdate
        /// </summary>
        /// <param name="world">World with root-level systems that need insertion into the player loop</param>
        /// <param name="existingPlayerLoop">Optional parameter to preserve existing player loops (e.g. ScriptBehaviourUpdateOrder.CurrentPlayerLoop)</param>
        public static void UpdatePlayerLoopWithFixedUpdate(World world, PlayerLoopSystem? existingPlayerLoop = null)
        {
            //Use reflection to snag the private delegate needed for this
            var insertMethodInfo = typeof(ScriptBehaviourUpdateOrder).GetMethod("InsertManagerIntoSubsystemList",
                                                                                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var simMethod   = insertMethodInfo.MakeGenericMethod(typeof(SimulationSystemGroup));
            var presMethod  = insertMethodInfo.MakeGenericMethod(typeof(PresentationSystemGroup));
            var initMethod  = insertMethodInfo.MakeGenericMethod(typeof(InitializationSystemGroup));
            var fixedMethod = insertMethodInfo.MakeGenericMethod(typeof(FixedSimulationSystemGroup));

            var playerLoop = existingPlayerLoop ?? PlayerLoop.GetDefaultPlayerLoop();

            if (world != null)
            {
                // Insert the root-level systems into the appropriate PlayerLoopSystem subsystems:
                for (var i = 0; i < playerLoop.subSystemList.Length; ++i)
                {
                    int subsystemListLength = playerLoop.subSystemList[i].subSystemList.Length;
                    if (playerLoop.subSystemList[i].type == typeof(Update))
                    {
                        var newSubsystemList = new PlayerLoopSystem[subsystemListLength + 1];
                        for (var j = 0; j < subsystemListLength; ++j)
                            newSubsystemList[j] = playerLoop.subSystemList[i].subSystemList[j];
                        simMethod.Invoke(null, new object[] {newSubsystemList,
                                                             subsystemListLength + 0, world.GetOrCreateSystem<SimulationSystemGroup>() });
                        playerLoop.subSystemList[i].subSystemList = newSubsystemList;
                    }
                    else if (playerLoop.subSystemList[i].type == typeof(PreLateUpdate))
                    {
                        var newSubsystemList = new PlayerLoopSystem[subsystemListLength + 1];
                        for (var j = 0; j < subsystemListLength; ++j)
                            newSubsystemList[j] = playerLoop.subSystemList[i].subSystemList[j];
                        presMethod.Invoke(null, new object[] {newSubsystemList,
                                                              subsystemListLength + 0, world.GetOrCreateSystem<PresentationSystemGroup>() });
                        playerLoop.subSystemList[i].subSystemList = newSubsystemList;
                    }
                    else if (playerLoop.subSystemList[i].type == typeof(Initialization))
                    {
                        var newSubsystemList = new PlayerLoopSystem[subsystemListLength + 1];
                        for (var j = 0; j < subsystemListLength; ++j)
                            newSubsystemList[j] = playerLoop.subSystemList[i].subSystemList[j];
                        initMethod.Invoke(null, new object[] {newSubsystemList,
                                                              subsystemListLength + 0, world.GetOrCreateSystem<InitializationSystemGroup>() });
                        playerLoop.subSystemList[i].subSystemList = newSubsystemList;
                    }
                    else if (playerLoop.subSystemList[i].type == typeof(FixedUpdate))
                    {
                        var newSubsystemList = new PlayerLoopSystem[subsystemListLength + 1];
                        for (var j = 0; j < subsystemListLength; ++j)
                            newSubsystemList[j] = playerLoop.subSystemList[i].subSystemList[j];
                        fixedMethod.Invoke(null, new object[] {newSubsystemList,
                                                               subsystemListLength + 0, world.GetOrCreateSystem<FixedSimulationSystemGroup>() });
                        playerLoop.subSystemList[i].subSystemList = newSubsystemList;
                    }
                }
            }

            ScriptBehaviourUpdateOrder.SetPlayerLoop(playerLoop);
        }
        #endregion

        #region TypeManager
        private delegate void TmAddTypeInfoToTables(Type type, TypeManager.TypeInfo typeInfo);
        private delegate TypeManager.TypeInfo TmBuildComponentType(Type type);

        //Todo: Replace with codegen
        public static void PopulateTypeManagerWithGenerics(Type genericWrapperIcdType, Type interfaceType)
        {
            if (!genericWrapperIcdType.IsValueType || !typeof(IComponentData).IsAssignableFrom(genericWrapperIcdType))
                throw new ArgumentException($"{genericWrapperIcdType} is not a valid struct IComponentData");

            //Snag methods from TypeManager
            var tmAddTypeInfoToTables = GetStaticMethod("AddTypeInfoToTables", 2).CreateDelegate(typeof(TmAddTypeInfoToTables)) as TmAddTypeInfoToTables;
            var tmBuildComponentType  = GetStaticMethod("BuildComponentType", 1).CreateDelegate(typeof(TmBuildComponentType)) as TmBuildComponentType;

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                if (!IsAssemblyReferencingLatios(assembly))
                    continue;

                foreach (var type in assembly.GetTypes())
                {
                    if (type.GetCustomAttribute(typeof(DisableAutoTypeRegistration)) != null)
                        continue;

                    if (type == interfaceType)
                        continue;

                    if (interfaceType.IsAssignableFrom(type))
                    {
                        if (!type.IsValueType)
                            throw new InvalidOperationException($"{type} implements {interfaceType} but is not a struct type");

                        var concrete = genericWrapperIcdType.MakeGenericType(type);
                        var info     = tmBuildComponentType(concrete);
                        tmAddTypeInfoToTables(concrete, info);
                    }
                }
            }
        }

        private static MethodInfo GetMethod(string methodName, int numOfArgs)
        {
            var methods = typeof(TypeManager).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (var method in methods)
            {
                if (method.Name == methodName && method.GetParameters().Length == numOfArgs)
                    return method;
            }
            return null;
        }

        private static MethodInfo GetStaticMethod(string methodName, int numOfArgs)
        {
            var methods = typeof(TypeManager).GetMethods(BindingFlags.Static | BindingFlags.NonPublic);
            foreach (var method in methods)
            {
                if (method.Name == methodName && method.GetParameters().Length == numOfArgs)
                    return method;
            }
            return null;
        }

        public static bool IsAssemblyReferencingLatios(Assembly assembly)
        {
            const string entitiesAssemblyName = "Latios";
            if (assembly.GetName().Name.Contains(entitiesAssemblyName))
                return true;

            var referencedAssemblies = assembly.GetReferencedAssemblies();
            foreach (var referenced in referencedAssemblies)
                if (referenced.Name.Contains(entitiesAssemblyName))
                    return true;
            return false;
        }
        #endregion
    }
}

