using System.Collections.Generic;
using Latios;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace Latios
{
    /// <summary>
    /// Implement this interface to customize or replace the Editor World.
    /// </summary>
    public interface ICustomEditorBootstrap
    {
        /// <summary>
        /// Modify the existing Editor World, or replace it by creating and returning a new one.
        /// </summary>
        /// <param name="defaultEditorWorld">The default Editor World</param>
        /// <returns>A new World such as a LatiosWorld if one was created,
        /// otherwise either defaultEditorWorld or null to keep the existing Editor World</returns>
        World InitializeOrModify(World defaultEditorWorld);
    }

#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoad]
#endif
    internal static class EditorBootstrapUtilities
    {
        static EditorBootstrapUtilities()
        {
            RegisterEditorWorldAction();
        }

        static bool m_isRegistered = false;

        internal static void RegisterEditorWorldAction()
        {
            if (!m_isRegistered)
            {
                m_isRegistered                                                         = true;
                Unity.Entities.Exposed.WorldExposedExtensions.DefaultWorldInitialized += InitializeEditorWorld;
            }
        }

        internal static void InitializeEditorWorld(World defaultEditorWorld)
        {
            if (World.DefaultGameObjectInjectionWorld != defaultEditorWorld || !defaultEditorWorld.Flags.HasFlag(WorldFlags.Editor))
                return;

            IEnumerable<System.Type> bootstrapTypes;
#if UNITY_EDITOR
            bootstrapTypes = UnityEditor.TypeCache.GetTypesDerivedFrom(typeof(ICustomEditorBootstrap));
#else

            var types = new List<System.Type>();
            var type  = typeof(ICustomEditorBootstrap);
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

                    UnityEngine.Debug.LogWarning($"EditorWorldBootstrap failed loading assembly: {(assembly.IsDynamic ? assembly.ToString() : assembly.Location)}");
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
                    UnityEngine.Debug.LogError("Multiple custom ICustomEditorBootstrap exist in the project, ignoring " + bootType);
            }
            if (selectedType == null)
                return;

            ICustomEditorBootstrap bootstrap = System.Activator.CreateInstance(selectedType) as ICustomEditorBootstrap;

            var newWorld = bootstrap.InitializeOrModify(defaultEditorWorld);
            if (newWorld != defaultEditorWorld && newWorld != null)
            {
                if (defaultEditorWorld.IsCreated)
                {
                    ScriptBehaviourUpdateOrder.RemoveWorldFromCurrentPlayerLoop(defaultEditorWorld);
                    defaultEditorWorld.Dispose();
                }
                ScriptBehaviourUpdateOrder.AppendWorldToCurrentPlayerLoop(newWorld);
                World.DefaultGameObjectInjectionWorld = newWorld;
            }
        }
    }

#if UNITY_EDITOR
    public static class UnityEditorTool
    {
        [UnityEditor.MenuItem("Edit/Restart Editor World")]
        public static void RestartEditorWorld()
        {
            var previousEditorWorld = World.DefaultGameObjectInjectionWorld;
            World.DefaultGameObjectInjectionWorld = null;

            if (previousEditorWorld == null)
            {
                DefaultWorldInitialization.DefaultLazyEditModeInitialize();
                previousEditorWorld = World.DefaultGameObjectInjectionWorld;
            }
            EditorBootstrapUtilities.InitializeEditorWorld(previousEditorWorld);

            if (World.DefaultGameObjectInjectionWorld == null)
            {
                if (World.DefaultGameObjectInjectionWorld == previousEditorWorld && previousEditorWorld != null)
                {
                    if (previousEditorWorld.IsCreated)
                    {
                        ScriptBehaviourUpdateOrder.RemoveWorldFromCurrentPlayerLoop(previousEditorWorld);
                        previousEditorWorld.Dispose();
                    }
                }

                var world = new World("EditorWorld", WorldFlags.Editor);

                World.DefaultGameObjectInjectionWorld = world;

                var systemList = DefaultWorldInitialization.GetAllSystems(WorldSystemFilterFlags.Default, true);
                DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(world, systemList);

                ScriptBehaviourUpdateOrder.AppendWorldToCurrentPlayerLoop(world);

                var subscenes = new List<Unity.Scenes.SubScene>();
                foreach (var subscene in Unity.Scenes.SubScene.AllSubScenes)
                {
                    if (subscene.enabled)
                    {
                        subscenes.Add(subscene);
                    }
                }

                foreach (var subscene in subscenes)
                    subscene.enabled = false;

                foreach (var subscene in subscenes)
                    subscene.enabled = true;
            }
        }
    }
#endif
}

