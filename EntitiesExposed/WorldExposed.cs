using System;
using System.Collections.Generic;
using BurstRuntime = Unity.Burst.BurstRuntime;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.LowLevel;

namespace Unity.Entities.Exposed
{
    public static class WorldExposedExtensions
    {
        /// <summary>
        /// Returns the managed executing system type if one is executing.
        /// </summary>
        /// <param name="world"></param>
        /// <returns></returns>
        public static Type ExecutingSystemType(this World world)
        {
            return world.Unmanaged.GetTypeOfSystem(world.Unmanaged.ExecutingSystem);
        }

        public static SystemHandle GetCurrentlyExecutingSystem(this ref WorldUnmanaged world)
        {
            return world.ExecutingSystem;
        }

        public static unsafe ComponentSystemBase AsManagedSystem(this World world, SystemHandle system)
        {
            return world.Unmanaged.ResolveSystemState(system)->ManagedSystem;
        }

        public static unsafe ComponentSystemBase AsManagedSystem(this ref WorldUnmanaged world, SystemHandle system)
        {
            return world.ResolveSystemState(system)->ManagedSystem;
        }

        public static unsafe int GetMetaIdForType(Type t)
        {
            return SystemBaseRegistry.GetSystemTypeMetaIndex(BurstRuntime.GetHashCode64(t));
        }

        public static event Action<World> DefaultWorldInitialized
        {
            add
            {
                DefaultWorldInitialization.DefaultWorldInitialized += value;
            }
            remove
            {
                DefaultWorldInitialization.DefaultWorldInitialized -= value;
            }
        }

        public static event Action<World, ComponentSystemBase> OnSystemCreated
        {
            add
            {
                World.SystemCreated += value;
            }
            remove
            {
                World.SystemCreated -= value;
            }
        }

        public static void AddDummyRootLevelSystemToPlayerLoop(ComponentSystemBase sys, ref PlayerLoopSystem loop)
        {
            var oldSystems     = loop.subSystemList;
            loop.subSystemList = new PlayerLoopSystem[oldSystems.Length + 1];
            for (int i = 0; i < oldSystems.Length; i++)
                loop.subSystemList[i]             = oldSystems[i];
            loop.subSystemList[oldSystems.Length] = new PlayerLoopSystem
            {
                type           = sys.GetType(),
                updateDelegate = new NullDummyDelegateWrapper(sys).TriggerEmptyUpdate
            };
        }
    }

    class NullDummyDelegateWrapper : ScriptBehaviourUpdateOrder.DummyDelegateWrapper
    {
        public NullDummyDelegateWrapper(ComponentSystemBase sys) : base(sys)
        {
        }

        public void TriggerEmptyUpdate()
        {
        }
    }
}

