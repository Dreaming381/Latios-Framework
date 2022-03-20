using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Entities.Exposed.Dangerous;
using Unity.Jobs;

namespace Latios
{
    public interface ISystemNewScene
    {
        public void OnNewScene(ref SystemState state);
    }

    public interface ISystemShouldUpdate
    {
        public bool ShouldUpdateSystem(ref SystemState state);
    }

    internal class UnmanagedExtraInterfacesDispatcher : IDisposable
    {
        List<DispatchBase> m_dispatches;

        UnsafeHashMap<int, int> m_metaId2DispatchMapping;

        public DispatchBase GetDispatch(ref SystemState state)
        {
            if (BurstLookupUtility.BurstLookup(in m_metaId2DispatchMapping, state.UnmanagedMetaIndex, out int dispatchIndex))
            {
                return m_dispatches[dispatchIndex];
            }
            return null;
        }

        public void Dispose()
        {
            m_metaId2DispatchMapping.Dispose();
        }

        public UnmanagedExtraInterfacesDispatcher()
        {
            var systems         = DefaultWorldInitialization.GetAllSystems(WorldSystemFilterFlags.All);
            var filteredIndices = new NativeList<int>(Allocator.Temp);
            for (int i = 0; i < systems.Count; i++)
            {
                var type = systems[i];
                if (typeof(ISystem).IsAssignableFrom(type))
                {
                    if (typeof(ISystemNewScene).IsAssignableFrom(type) || typeof(ISystemShouldUpdate).IsAssignableFrom(type))
                    {
                        filteredIndices.Add(i);
                    }
                }
            }

            m_dispatches             = new List<DispatchBase>(filteredIndices.Length);
            m_metaId2DispatchMapping = new UnsafeHashMap<int, int>(filteredIndices.Length, Allocator.Persistent);

            var NewSceneType             = typeof(DispatchNewScene<>);
            var ShouldUpdateType         = typeof(DispatchShouldUpdate<>);
            var NewSceneShouldUpdateType = typeof(DispatchNewSceneShouldUpdate<>);

            for (int i = 0; i < filteredIndices.Length; i++)
            {
                var  systemType     = systems[filteredIndices[i]];
                bool isNewScene     = typeof(ISystemNewScene).IsAssignableFrom(systemType);
                bool isShouldUpdate = typeof(ISystemShouldUpdate).IsAssignableFrom(systemType);

                int metaId = -1;

                try
                {
                    metaId = WorldExposedExtensions.GetMetaIdForType(systemType);
                }
                catch (ArgumentException e)
                {
                    UnityEngine.Debug.LogWarning(
                        $"A meta ID was not found for system of type {systemType.Name}. OnNewScene and ShouldUpdateSystem may not work correctly. Unity.Entities Error: {e.Message}");
                    continue;
                }

                DispatchBase dispatcher;
                if (isNewScene && isShouldUpdate)
                    dispatcher = Activator.CreateInstance(NewSceneShouldUpdateType.MakeGenericType(systemType)) as DispatchBase;
                else if (isNewScene)
                    dispatcher = Activator.CreateInstance(NewSceneType.MakeGenericType(systemType)) as DispatchBase;
                else if (isShouldUpdate)
                    dispatcher = Activator.CreateInstance(ShouldUpdateType.MakeGenericType(systemType)) as DispatchBase;
                else
                    continue;

                m_metaId2DispatchMapping.Add(metaId, m_dispatches.Count);
                m_dispatches.Add(dispatcher);
            }
        }

        public abstract class DispatchBase
        {
            public abstract void OnNewScene(ref SystemState state);
            public abstract bool ShouldUpdateSystem(ref SystemState state);
        }

        public class DispatchNewScene<T> : DispatchBase where T : unmanaged, ISystem, ISystemNewScene
        {
            public override void OnNewScene(ref SystemState state)
            {
                state.GetStronglyTypedUnmanagedSystem<T>().Struct.OnNewScene(ref state);
            }

            public override bool ShouldUpdateSystem(ref SystemState state)
            {
                return state.Enabled;
            }
        }

        public class DispatchShouldUpdate<T> : DispatchBase where T : unmanaged, ISystem, ISystemShouldUpdate
        {
            public override void OnNewScene(ref SystemState state)
            {
            }

            public override bool ShouldUpdateSystem(ref SystemState state)
            {
                return state.GetStronglyTypedUnmanagedSystem<T>().Struct.ShouldUpdateSystem(ref state);
            }
        }

        public class DispatchNewSceneShouldUpdate<T> : DispatchBase where T : unmanaged, ISystem, ISystemNewScene, ISystemShouldUpdate
        {
            public override void OnNewScene(ref SystemState state)
            {
                state.GetStronglyTypedUnmanagedSystem<T>().Struct.OnNewScene(ref state);
            }

            public override bool ShouldUpdateSystem(ref SystemState state)
            {
                return state.GetStronglyTypedUnmanagedSystem<T>().Struct.ShouldUpdateSystem(ref state);
            }
        }
    }

    [BurstCompile]
    internal static class BurstLookupUtility
    {
        [BurstCompile]
        public static bool BurstLookup(in UnsafeHashMap<int, int> map, int key, out int value)
        {
            return map.TryGetValue(key, out value);
        }
    }
}

