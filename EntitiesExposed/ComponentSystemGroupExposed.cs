using System.Collections;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;

namespace Unity.Entities.Exposed
{
    public static class ComponentSystemGroupExposed
    {
        public static ComponentSystemGroupSystemEnumerator GetSystemEnumerator(this ComponentSystemGroup group) => new ComponentSystemGroupSystemEnumerator
        {
            group = group
        };

        public static NativeList<SystemTypeIndex> GetUpdateInGroupTargets(this SystemTypeIndex system, Allocator allocator = Allocator.Temp)
        {
            var attributes = TypeManager.GetSystemAttributes(system, TypeManager.SystemAttributeKind.UpdateInGroup, Allocator.Temp);
            var result     = new NativeList<SystemTypeIndex>(attributes.Length, allocator);
            foreach (var attribute in attributes)
            {
                result.Add(attribute.TargetSystemTypeIndex);
            }
            return result;
        }

        public static System.Type GetManagedType(this SystemTypeIndex system) => TypeManager.GetSystemType(system);
    }

    public struct ComponentSystemGroupSystemEnumerator
    {
        public ComponentSystemBase currentManaged { get; private set; }
        public SystemHandle current { get; private set; }
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
                currentManaged = group.m_managedSystemsToUpdate[index.Index];
                current        = currentManaged.SystemHandle;
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
            bool hasSystemsToRemove = group.m_managedSystemsToRemove.Count > 0 || !group.m_UnmanagedSystemsToRemove.IsEmpty;
            bool sortingTurnedOn    = lastEnableSortingSetting == false && enableSorting == true;

            if ((systemCount != lastSystemCount && enableSorting) || sortingTurnedOn || hasSystemsToRemove)
            {
                group.SortSystems();
            }

            lastSystemCount          = systemCount;
            lastEnableSortingSetting = enableSorting;
        }
    }
}

namespace Unity.Entities.Exposed.Dangerous
{
    public static class ComponentSystemGroupExposedDangerous
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS && !UNITY_DOTSRUNTIME
        public static void ClearSystemIds(this ComponentSystemGroup group) => JobsUtility.ClearSystemIds();
#endif
    }
}

// Add DisableAutoCreation to root groups to avoid self-injection issues since these systems are created anyways
namespace Unity.Entities
{
    [DisableAutoCreation]
    public partial class InitializationSystemGroup : ComponentSystemGroup
    {
    }

    [DisableAutoCreation]
    public partial class SimulationSystemGroup : ComponentSystemGroup
    {
    }

    [DisableAutoCreation]
    public partial class PresentationSystemGroup : ComponentSystemGroup
    {
    }
}

