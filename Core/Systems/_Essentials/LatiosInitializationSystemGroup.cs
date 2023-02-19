using System.Collections.Generic;
using System.Linq;
using Debug = UnityEngine.Debug;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Entities.Exposed.Dangerous;

namespace Latios.Systems
{
    /// <summary>
    /// A specialized version of InitializationSystemGroup for LatiosWorld.
    /// </summary>
    [DisableAutoCreation, NoGroupInjection]
    public class LatiosInitializationSystemGroup : InitializationSystemGroup
    {
        private SyncPointPlaybackSystemDispatch      m_syncPlayback;
        private MergeBlackboardsSystem               m_mergeGlobals;
        private ManagedComponentsReactiveSystemGroup m_cleanupGroup;
        private LatiosWorldSyncGroup                 m_syncGroup;
        private PreSyncPointGroup                    m_preSyncGroup;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_syncPlayback = World.CreateSystemManaged<SyncPointPlaybackSystemDispatch>();
            m_mergeGlobals = World.CreateSystemManaged<MergeBlackboardsSystem>();
            m_cleanupGroup = World.CreateSystemManaged<ManagedComponentsReactiveSystemGroup>();
            m_syncGroup    = World.GetOrCreateSystemManaged<LatiosWorldSyncGroup>();
            m_preSyncGroup = World.GetOrCreateSystemManaged<PreSyncPointGroup>();
            AddSystemToUpdateList(m_syncPlayback);
            AddSystemToUpdateList(m_syncGroup);
            AddSystemToUpdateList(m_preSyncGroup);
            m_syncGroup.AddSystemToUpdateList(m_mergeGlobals);
            m_syncGroup.AddSystemToUpdateList(m_cleanupGroup);
        }

        SystemSortingTracker m_tracker;

        protected override void OnUpdate()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS && !UNITY_DOTSRUNTIME
            this.ClearSystemIds();
#endif

            LatiosWorld lw = World as LatiosWorld;
            lw.FrameStart();
            SuperSystem.DoSuperSystemUpdate(this, ref m_tracker);
        }
    }

    /// <summary>
    /// A group that runs after blackboard entities, collection components, and managed struct components are processed.
    /// Add systems to this group that perform structural changes to avoid synchronizing jobs, as jobs are usually synchronized at this point.
    /// </summary>
    [DisableAutoCreation]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(Unity.Scenes.SceneSystemGroup))]
    public class LatiosWorldSyncGroup : ComponentSystemGroup
    {
        SystemSortingTracker m_tracker;

        protected override void OnUpdate()
        {
            SuperSystem.DoSuperSystemUpdate(this, ref m_tracker);
        }
    }

    /// <summary>
    /// A group that runs before syncPoint and the following structural-change systems.
    /// Systems that have heavy jobs which do not depend on ECS structures can be scheduled here.
    /// The jobs may run alongside the sync point, utilizing worker threads which would normally be idle.
    /// </summary>
    [DisableAutoCreation]
    [UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true)]
    [UpdateBefore(typeof(BeginInitializationEntityCommandBufferSystem))]
    [UpdateBefore(typeof(SyncPointPlaybackSystemDispatch))]
    public class PreSyncPointGroup : ComponentSystemGroup
    {
        SystemSortingTracker m_tracker;

        protected override void OnUpdate()
        {
            SuperSystem.DoSuperSystemUpdate(this, ref m_tracker);
        }
    }
}

