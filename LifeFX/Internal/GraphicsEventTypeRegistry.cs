using System;
using System.Collections.Generic;
using System.Reflection;  // Preserve for builds
using Latios.Kinemation;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.LifeFX
{
    internal static class GraphicsEventTypeRegistry
    {
        struct SharedKey { }

        public struct TypeToIndex<T> where T : unmanaged
        {
            public static readonly SharedStatic<int> typeToIndexOffBy1 = SharedStatic<int>.GetOrCreate<SharedKey, T>();
            public static int typeToIndex
            {
                get
                {
                    if (typeToIndexOffBy1.Data == 0)
                        throw new System.InvalidOperationException($"The graphics event type for this {new TypeToIndex<T>()} has not been registered.");
                    return typeToIndexOffBy1.Data - 1;
                }
            }
        }

        public struct EventMetadata
        {
            public GraphicsBufferBroker.StaticID brokerId;
            public short                         size;
            public short                         alignment;
        }

        public static readonly SharedStatic<UnsafeList<EventMetadata> > s_eventMetadataList = SharedStatic<UnsafeList<EventMetadata> >.GetOrCreate<EventMetadata>();

        public struct EventHashManager
        {
            UnsafeHashMap<UnityObjectRef<GraphicsEventTunnelBase>, UnityObjectRef<GraphicsEventTunnelBase> > deduplicateTargetMap;
            UnsafeHashMap<Unity.Entities.Hash128, UnityObjectRef<GraphicsEventTunnelBase> >                  firstRegisteredMap;
            bool                                                                                             lockForReading;

            public void Init()
            {
                deduplicateTargetMap = new UnsafeHashMap<UnityObjectRef<GraphicsEventTunnelBase>, UnityObjectRef<GraphicsEventTunnelBase> >(32, Allocator.Persistent);
                firstRegisteredMap   = new UnsafeHashMap<Unity.Entities.Hash128, UnityObjectRef<GraphicsEventTunnelBase> >(64, Allocator.Persistent);
                lockForReading       = false;
            }

            public void Dispose()
            {
                deduplicateTargetMap.Dispose();
                firstRegisteredMap.Dispose();
            }

            // Should only be called from main thread
            public void Add(UnityObjectRef<GraphicsEventTunnelBase> instance, Unity.Entities.Hash128 hash)
            {
                if (lockForReading)
                    throw new System.InvalidOperationException("Cannot add a new GraphicsEventTunnel while a system is using it.");

                if (firstRegisteredMap.TryGetValue(hash, out var target))
                    deduplicateTargetMap.TryAdd(instance, target);
                else
                {
                    firstRegisteredMap.Add(hash, instance);
                    deduplicateTargetMap.Add(instance, instance);
                }
            }

            public UnityObjectRef<GraphicsEventTunnelBase> this[UnityObjectRef<GraphicsEventTunnelBase> key] => deduplicateTargetMap[key];

            public void SetLock(bool lockForReading) => this.lockForReading = lockForReading;
        }

        public static readonly SharedStatic<EventHashManager> s_eventHashManager = SharedStatic<EventHashManager>.GetOrCreate<EventHashManager>();

        static bool s_initialized    = false;
        static bool s_isInitializing = false;

        public static bool IsInitializing() => s_isInitializing;

        public static void Init()
        {
            if (s_initialized || s_isInitializing)
                return;

            s_isInitializing = true;

            s_eventHashManager.Data.Init();

            s_eventMetadataList.Data = new UnsafeList<EventMetadata>(16, Allocator.Persistent);

#if UNITY_EDITOR
            var eventSOs = UnityEditor.TypeCache.GetTypesDerivedFrom<GraphicsEventTunnelBase>();
#else
            var eventSOs = new List<Type>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!BootstrapTools.IsAssemblyReferencingSubstring(assembly, "LifeFX"))
                    continue;

                var targetType = typeof(GraphicsEventTunnelBase);
                try
                {
                    var assemblyTypes = assembly.GetTypes();
                    foreach (var t in assemblyTypes)
                    {
                        if (t == targetType)
                            continue;

                        if (targetType.IsAssignableFrom(t))
                            eventSOs.Add(t);
                    }
                }
                catch (ReflectionTypeLoadException e)
                {
                    foreach (var t in e.Types)
                    {
                        if (t != null && t != targetType && targetType.IsAssignableFrom(t))
                            eventSOs.Add(t);
                    }

                    UnityEngine.Debug.LogWarning($"LifeFX GraphicsEventTypeRegistry.cs failed loading assembly: {(assembly.IsDynamic ? assembly.ToString() : assembly.Location)}");
                }
            }
#endif
            var eventTypesAlreadyFound = new HashSet<Type>();
            var sharedKeyType          = typeof(SharedKey);
            foreach (var eventSO in eventSOs)
            {
                if (eventSO.IsAbstract)
                    continue;
                var so        = ScriptableObject.CreateInstance(eventSO) as GraphicsEventTunnelBase;
                var eventType = so.GetEventType();
                if (!eventTypesAlreadyFound.Contains(eventType.type))
                {
                    eventTypesAlreadyFound.Add(eventType.type);
                    s_eventMetadataList.Data.Add(new EventMetadata
                    {
                        brokerId  = GraphicsBufferBroker.ReserveUploadPool(),
                        size      = (short)eventType.size,
                        alignment = (short)eventType.alignment,
                    });
                    SharedStatic<int>.GetOrCreate(sharedKeyType, eventType.type).Data = s_eventMetadataList.Data.Length;
                }
                so.DestroySafelyFromAnywhere();
            }

            // important: this will always be called from a special unload thread (main thread will be blocking on this)
            AppDomain.CurrentDomain.DomainUnload += (_, __) => { Shutdown(); };

            // There is no domain unload in player builds, so we must be sure to shutdown when the process exits.
            AppDomain.CurrentDomain.ProcessExit += (_, __) => { Shutdown(); };

            s_initialized    = true;
            s_isInitializing = false;
        }

        static void Shutdown()
        {
            s_eventMetadataList.Data.Dispose();
            s_eventHashManager.Data.Dispose();
        }
    }
}

