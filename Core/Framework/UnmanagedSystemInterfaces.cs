using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Jobs;

namespace Latios
{
    /// <summary>
    /// Implement this interface to get a callback whenever a new sceneBlackboardEntity is created
    /// </summary>
    public interface ISystemNewScene
    {
        public void OnNewScene(ref SystemState state);
    }

    /// <summary>
    /// Implement this interface to get a callback for determining if this system should run
    /// </summary>
    public interface ISystemShouldUpdate
    {
        public bool ShouldUpdateSystem(ref SystemState state);
    }

    /// <summary>
    /// Allows a system to update multiple times without synchronizing the JobHandle from the system's previous update in a frame.
    /// This can be useful for fixed update loops where packing the job chain can increase throughput.
    /// Warning: Dependency may not always account for the jobs from the previous update, as it only relies upon ECS dependency
    /// managedment as if the second run belonged to a separate system. However, when the max number of updates is reached, or during
    /// the first update of the new frame, the JobHandles of the previous unsynced updates are forced to complete.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
    public class DontSyncPreviousUpdatesThisFrameAttribute : Attribute
    {
        public int maxUpdatesWithoutSync;
        /// <summary>
        /// Commands a system to not complete its previous jobs if this is not the first time this system has updated in a frame.
        /// </summary>
        /// <param name="maxUpdatesWithoutSync">The maximum number of updates this system can go without syncing.</param>
        public DontSyncPreviousUpdatesThisFrameAttribute(int maxUpdatesWithoutSync)
        {
            this.maxUpdatesWithoutSync = maxUpdatesWithoutSync;
        }
    }

    internal struct SystemChainUpdatesManager : IDisposable
    {
        struct SyncState
        {
            public int       frameCounter;
            public int       maxUpdatesPerFrame;
            public int       updateCountThisFrame;
            public JobHandle accumulatedJobHandle;
        }

        UnsafeHashMap<int, SyncState> metaIdToSyncStateMap;

        public SystemChainUpdatesManager(bool dummy)
        {
            // The following line is bugged and does not include systems with [DisableAutoCreation].
            //var systems         = DefaultWorldInitialization.GetAllSystems(WorldSystemFilterFlags.All);

            // Use reflection instead.
            var systems = typeof(TypeManager).GetField("s_SystemTypes", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null) as List<Type>;

            metaIdToSyncStateMap = new UnsafeHashMap<int, SyncState>(systems.Count, Allocator.Persistent);
            foreach (var system in systems)
            {
                // For some reason, Unity includes a null system at the start of this list sometimes.
                if (system == null)
                    continue;

                DontSyncPreviousUpdatesThisFrameAttribute syncAttribute = null;
                try
                {
                    syncAttribute = system.GetCustomAttribute<DontSyncPreviousUpdatesThisFrameAttribute>();
                }
                catch (Exception)
                {
                    //UnityEngine.Debug.LogException(ex);
                }
                if (syncAttribute != null)
                {
                    int metaId = -1;
                    try
                    {
                        metaId = WorldExposedExtensions.GetMetaIdForType(system);
                    }
                    catch (ArgumentException e)
                    {
                        UnityEngine.Debug.LogWarning(
                            $"A meta ID was not found for system of type {system.Name}. Unity.Entities Error: {e.Message}");
                        continue;
                    }
                    metaIdToSyncStateMap.Add(metaId, new SyncState { maxUpdatesPerFrame = syncAttribute.maxUpdatesWithoutSync });
                }
            }
        }

        public unsafe void BeforeOnUpdate(ref SystemState state, int frameCounter)
        {
            if (!metaIdToSyncStateMap.TryGetValue(state.UnmanagedMetaIndex, out var syncState))
                return;
            if (frameCounter != syncState.frameCounter || syncState.updateCountThisFrame >= syncState.maxUpdatesPerFrame)
            {
                if (!syncState.accumulatedJobHandle.Equals(default))
                    syncState.accumulatedJobHandle.Complete();
                syncState.accumulatedJobHandle = default;
                syncState.updateCountThisFrame = 1;
            }
            else
            {
                syncState.accumulatedJobHandle = JobHandle.CombineDependencies(syncState.accumulatedJobHandle, state.GetLastScheduledJobHandle());
                state.Dependency               = default;
                syncState.updateCountThisFrame++;
            }
            syncState.frameCounter                         = frameCounter;
            metaIdToSyncStateMap[state.UnmanagedMetaIndex] = syncState;
        }

        public void Dispose()
        {
            foreach (var pair in metaIdToSyncStateMap)
            {
                pair.Value.accumulatedJobHandle.Complete();
            }
            metaIdToSyncStateMap.Dispose();
        }
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
            // The following line is bugged and does not include systems with [DisableAutoCreation].
            //var systems         = DefaultWorldInitialization.GetAllSystems(WorldSystemFilterFlags.All);

            // Use reflection instead.
            var systems = typeof(TypeManager).GetField("s_SystemTypes", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null) as List<Type>;

            var filteredIndices = new NativeList<int>(Allocator.Temp);
            for (int i = 0; i < systems.Count; i++)
            {
                var type = systems[i];
                if (type == null)
                    continue;

                if (typeof(ISystem).IsAssignableFrom(type))
                {
                    if (typeof(ISystemNewScene).IsAssignableFrom(type) || typeof(ISystemShouldUpdate).IsAssignableFrom(type))
                    {
                        filteredIndices.Add(i);
                    }
                }
            }

            m_dispatches             = new List<DispatchBase>(filteredIndices.Length);
            m_metaId2DispatchMapping = new UnsafeHashMap<int, int>(filteredIndices.Length + 1, Allocator.Persistent);

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
                state.WorldUnmanaged.GetUnsafeSystemRef<T>(state.SystemHandle).OnNewScene(ref state);
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
                return state.WorldUnmanaged.GetUnsafeSystemRef<T>(state.SystemHandle).ShouldUpdateSystem(ref state);
            }
        }

        public class DispatchNewSceneShouldUpdate<T> : DispatchBase where T : unmanaged, ISystem, ISystemNewScene, ISystemShouldUpdate
        {
            public override void OnNewScene(ref SystemState state)
            {
                state.WorldUnmanaged.GetUnsafeSystemRef<T>(state.SystemHandle).OnNewScene(ref state);
            }

            public override bool ShouldUpdateSystem(ref SystemState state)
            {
                return state.WorldUnmanaged.GetUnsafeSystemRef<T>(state.SystemHandle).ShouldUpdateSystem(ref state);
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

