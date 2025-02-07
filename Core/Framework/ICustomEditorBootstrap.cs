using System;
using System.Collections.Generic;
using Latios;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Exposed;
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
        /// <param name="defaultEditorWorldName">The name for the new default Editor World</param>
        /// <returns>A new World such as a LatiosWorld if one was created,
        /// otherwise null to use a Unity-created default editor world</returns>
        World Initialize(string defaultEditorWorldName);
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

        static bool             m_isRegistered    = false;
        static ICustomBootstrap m_editorBootstrap = null;

        internal static void RegisterEditorWorldAction()
        {
            if (!m_isRegistered)
            {
                m_isRegistered                                       = true;
                EditorWorldInitializationOverride.s_overrideDelegate = CreateBootstrapOverride;
            }
        }

        internal static ICustomBootstrap CreateBootstrapOverride(bool isEditorWorld)
        {
            if (isEditorWorld)
            {
                if (m_editorBootstrap == null)
                {
                    var editorBootstrapType = typeof(GenericEditorBootstrapWrapper<>).MakeGenericType(typeof(int));
                    m_editorBootstrap       = Activator.CreateInstance(editorBootstrapType) as ICustomBootstrap;
                }
                return m_editorBootstrap;
            }
            else
                return BootstrapTools.TryCreateCustomBootstrap<ICustomBootstrap>();
        }

        internal class GenericEditorBootstrapWrapper<T> : ICustomBootstrap
        {
            public bool Initialize(string defaultWorldName)
            {
                var bootstrap = BootstrapTools.TryCreateCustomBootstrap<ICustomEditorBootstrap>();
                if (bootstrap == null)
                    return false;

                var world = bootstrap.Initialize(defaultWorldName);
                if (world != null)
                {
                    ScriptBehaviourUpdateOrder.AppendWorldToCurrentPlayerLoop(world);
                    World.DefaultGameObjectInjectionWorld = world;
                    return true;
                }
                return false;
            }
        }
    }

#if UNITY_EDITOR
    public static class UnityEditorTool
    {
        [UnityEditor.MenuItem("Edit/Latios/Restart Editor World")]
        public static void RestartEditorWorld()
        {
            var previousEditorWorld = World.DefaultGameObjectInjectionWorld;
            World.DefaultGameObjectInjectionWorld = null;
            if (previousEditorWorld != null)
                previousEditorWorld.Dispose();

            DefaultWorldInitialization.DefaultLazyEditModeInitialize();

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
#endif
}

