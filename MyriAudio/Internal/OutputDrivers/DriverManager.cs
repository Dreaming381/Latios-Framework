using System.Collections.Generic;
using Unity.Audio;

namespace Latios.Myri.Driver
{
    internal static class DriverManager
    {
        struct Index
        {
            public int  index;
            public bool isManaged;
        }

        static Dictionary<int, Index>  registeredGraphs      = new Dictionary<int, Index>();
        static List<AudioOutputHandle> burstDrivers          = new List<AudioOutputHandle>();
        static Stack<int>              burstDriversFreeList  = new Stack<int>();
        static List<DSPGraph>          managedGraphs         = new List<DSPGraph>();
        static Stack<int>              managedGraphsFreeList = new Stack<int>();
        static int                     incrementingKey       = 1;

        static MyriManagedDriver       currentManagedDriver;
        static List<MyriManagedDriver> managedDriversListCache = new List<MyriManagedDriver>();

        public static int RegisterGraph(ref DSPGraph graph)
        {
            if (!DriverSelection.useManagedDriver)
            {
                var burstDriver = new MyriBurstDriver { Graph = graph };

                if (!burstDriversFreeList.TryPop(out var freeIndex))
                {
                    freeIndex = burstDrivers.Count;
                    burstDrivers.Add(default);
                }
                registeredGraphs.Add(incrementingKey, new Index
                {
                    index     = freeIndex,
                    isManaged = false
                });
                var result = incrementingKey;
                incrementingKey++;

                burstDrivers[freeIndex] = burstDriver.AttachToDefaultOutput();
                return result;
            }
            else
            {
                lock(managedGraphs)
                {
                    if (!managedGraphsFreeList.TryPop(out var freeIndex))
                    {
                        freeIndex = managedGraphs.Count;
                        managedGraphs.Add(default);
                    }
                    registeredGraphs.Add(incrementingKey, new Index
                    {
                        index     = freeIndex,
                        isManaged = true
                    });
                    var result = incrementingKey;
                    incrementingKey++;

                    managedGraphs[freeIndex] = graph;
                    return result;
                }
            }
        }

        public static void DeregisterAndDisposeGraph(int registeredKey)
        {
            if (!registeredGraphs.TryGetValue(registeredKey, out var index))
            {
                UnityEngine.Debug.LogError($"Myri's DriverManager is corrupted. The key does not exist.");
                return;
            }
            if (!index.isManaged)
            {
                burstDrivers[index.index].Dispose();
                burstDrivers[index.index] = default;
                burstDriversFreeList.Push(index.index);
            }
            else
            {
                lock(managedGraphs)
                {
                    managedGraphs[index.index].Dispose();
                    managedGraphs[index.index] = default;
                    managedGraphsFreeList.Push(index.index);
                }
            }
            registeredGraphs.Remove(registeredKey);
        }

        public static void Update()
        {
            if (!DriverSelection.useManagedDriver)
                return;

            // Todo: Support creating a managed driver when there is no listener.
            // The biggest challenge with this is that if a listener were to exist later,
            // then we can't detect it, and Unity will complain about multiple listeners.
            if (currentManagedDriver == null)
                currentManagedDriver = null;
            else
                return;

            UnityEngine.AudioListener currentListener = null;
            // Check the main camera for a listener
            var mainCamera = UnityEngine.Camera.main;
            if (mainCamera != null)
            {
                if (!mainCamera.TryGetComponent(out currentListener))
                    currentListener = null;
            }
            if (currentListener == null)
            {
                // Is it on a player character?
                currentListener = UnityEngine.Object.FindAnyObjectByType<UnityEngine.AudioListener>();
            }

            if (currentListener == null)
                return;

            currentManagedDriver = currentListener.gameObject.AddComponent<MyriManagedDriver>();
        }

        public static List<DSPGraph> GetLockableManagedGraphs() => managedGraphs;
    }
}

