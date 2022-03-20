using System.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;

namespace Unity.Entities.Exposed
{
    public static class ComponentSystemGroupExposed
    {
        public static ComponentSystemGroupSystemEnumerator GetSystemEnumerator(this ComponentSystemGroup group) => new ComponentSystemGroupSystemEnumerator {
            group = group
        };
    }

    public struct ComponentSystemGroupSystemEnumerator
    {
        public ComponentSystemBase currentManaged { get; private set; }
        public SystemHandleUntyped current { get; private set; }
        public ComponentSystemGroup group { get; internal set; }
        public bool IsCurrentManaged => currentManaged != null;

        int m_masterOrderIndex;
        int m_cachedLength;

        public bool MoveNext()
        {
            if (group == null)
                return false;

            // Cache the update list length before updating; any new systems added mid-loop will change the length and
            // should not be processed until the subsequent group update, to give SortSystems() a chance to run.
            if (m_masterOrderIndex == 0)
                m_cachedLength = group.m_MasterUpdateList.Length;

            if (m_masterOrderIndex >= m_cachedLength)
            {
                current        = default;
                currentManaged = null;
                return false;
            }
            var index = group.m_MasterUpdateList[m_masterOrderIndex];
            if (index.IsManaged)
            {
                currentManaged = group.m_systemsToUpdate[index.Index];
                current        = currentManaged.SystemHandleUntyped;
            }
            else
            {
                current        = group.m_UnmanagedSystemsToUpdate[index.Index];
                currentManaged = null;
            }
            m_masterOrderIndex++;
            return true;
        }
    }

    public struct SystemSortingTracker
    {
        int  lastSystemCount;
        bool lastEnableSortingSetting;

        public void CheckAndSortSystems(ComponentSystemGroup group)
        {
            int  systemCount        = group.m_MasterUpdateList.Length;
            bool enableSorting      = group.EnableSystemSorting;
            bool hasSystemsToRemove = group.m_systemsToRemove.Count > 0 || !group.m_UnmanagedSystemsToRemove.IsEmpty;
            bool sortingTurnedOn    = lastEnableSortingSetting == false && enableSorting == true;

            if (systemCount != lastSystemCount || sortingTurnedOn || hasSystemsToRemove)
                group.SortSystems();

            lastSystemCount          = systemCount;
            lastEnableSortingSetting = enableSorting;
        }
    }
}

namespace Unity.Entities.Exposed.Dangerous
{
    public static class ComponentSystemGroupExposedDangerous
    {
        public static void SetExecutingSystem(this ComponentSystemGroup group, ref WorldUnmanaged world, SystemHandleUntyped system) => world.ExecutingSystem = system;
        public static void DestroyPendingSystemsInWorld(this ComponentSystemGroup group, ref WorldUnmanaged world) => world.DestroyPendingSystems();

#if ENABLE_UNITY_COLLECTIONS_CHECKS && !UNITY_DOTSRUNTIME
        public static void ClearSystemIds(this ComponentSystemGroup group) => JobsUtility.ClearSystemIds();
#endif
    }

    public static class SystemStateExposedDangerous
    {
        public static unsafe SystemRef<T> GetStronglyTypedUnmanagedSystem<T>(this ref SystemState state) where T : unmanaged, ISystem
        {
            return new SystemRef<T>(state.m_SystemPtr,
                                    new SystemHandle<T>(state.SystemHandleUntyped.m_Handle, state.SystemHandleUntyped.m_Version, state.SystemHandleUntyped.m_WorldSeqNo));
        }
    }
}

