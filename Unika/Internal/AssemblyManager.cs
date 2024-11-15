using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Unika
{
    internal static class AssemblyManager
    {
        static bool              initialized        = false;
        static HashSet<Assembly> loadedAssemblies   = new HashSet<Assembly>();
        static List<Type>        interfaceTypeCache = new List<Type>();
        static List<Type>        scriptTypeCache    = new List<Type>();

        delegate void InitializeDelegate();

        public static void Initialize()
        {
            if (initialized)
                return;

            ScriptTypeInfoManager.InitializeStatics();
            ScriptVTable.InitializeStatics();
            ScriptStructuralChangeInternal.InitializeStatics();

#if UNITY_EDITOR
            var interfaceTypes = UnityEditor.TypeCache.GetTypesDerivedFrom<IUnikaInterface>();

            foreach (var i in interfaceTypes)
            {
                if (i.IsInterface && i != typeof(IUnikaInterface))
                {
                    InitializeInterface(i);
                }
            }

            var scriptTypes = UnityEditor.TypeCache.GetTypesDerivedFrom<IUnikaScript>();

            foreach (var s in scriptTypes)
            {
                if (s.IsValueType)
                {
                    InitializeScriptType(s);
                }
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!BootstrapTools.IsAssemblyReferencingSubstring(assembly, "Unika"))
                    continue;

                loadedAssemblies.Add(assembly);
            }
#else
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                AddAssembly(assembly);
            }
#endif
            // important: this will always be called from a special unload thread (main thread will be blocking on this)
            AppDomain.CurrentDomain.DomainUnload += (_, __) => { Shutdown(); };

            // There is no domain unload in player builds, so we must be sure to shutdown when the process exits.
            AppDomain.CurrentDomain.ProcessExit += (_, __) => { Shutdown(); };

            initialized = true;
        }

        public static void Shutdown()
        {
            if (!initialized)
                return;

            ScriptVTable.DisposeStatics();
            ScriptTypeInfoManager.DisposeStatics();
            loadedAssemblies.Clear();
            initialized = false;
        }

        public static void AddAssembly(Assembly assembly)
        {
            if (!BootstrapTools.IsAssemblyReferencingSubstring(assembly, "Unika"))
                return;

            if (loadedAssemblies.Contains(assembly))
                return;

            loadedAssemblies.Add(assembly);

            interfaceTypeCache.Clear();
            scriptTypeCache.Clear();

            var interfaceType = typeof(IUnikaInterface);
            var scriptType    = typeof(IUnikaScript);

            try
            {
                var assemblyTypes = assembly.GetTypes();
                foreach (var t in assemblyTypes)
                {
                    if (t == interfaceType || t == scriptType)
                        continue;

                    if (t.IsInterface && interfaceType.IsAssignableFrom(t))
                        interfaceTypeCache.Add(t);
                    if (t.IsValueType && scriptType.IsAssignableFrom(t))
                        scriptTypeCache.Add(t);
                }
            }
            catch (ReflectionTypeLoadException e)
            {
                foreach (var t in e.Types)
                {
                    if (t != null && t.IsInterface && interfaceType.IsAssignableFrom(t))
                        interfaceTypeCache.Add(t);
                    if (t != null && t.IsValueType && scriptType.IsAssignableFrom(t))
                        scriptTypeCache.Add(t);
                }

                UnityEngine.Debug.LogWarning($"Unika AssemblyManager.cs failed loading assembly: {(assembly.IsDynamic ? assembly.ToString() : assembly.Location)}");
            }

            foreach (var i in interfaceTypeCache)
            {
                InitializeInterface(i);
            }

            foreach (var s in scriptTypeCache)
            {
                InitializeScriptType(s);
            }
        }

        static void InitializeInterface(Type interfaceType)
        {
            var method = interfaceType.GetMethod("__Initialize", BindingFlags.Static | BindingFlags.Public);
            if (method == null)
            {
                UnityEngine.Debug.LogError($"Unika failed to intialize {interfaceType}. Are you missing the `partial` keyword?");
                return;
            }
            var invokable = method.CreateDelegate(typeof(InitializeDelegate)) as InitializeDelegate;
            invokable();
        }

        static void InitializeScriptType(Type scriptType)
        {
            var method = scriptType.GetMethod("__Initialize", BindingFlags.Static | BindingFlags.Public);
            if (method == null)
            {
                UnityEngine.Debug.LogError($"Unika failed to intialize {scriptType}. Are you missing the `partial` keyword?");
                return;
            }
            var invokable = method.CreateDelegate(typeof(InitializeDelegate)) as InitializeDelegate;
            invokable();
        }
    }
}

