#if NETCODE_PROJECT

using Latios.Systems;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Entities.Exposed.Dangerous;
using Unity.NetCode;

namespace Latios.Compatibility.UnityNetCode
{
    [DisableAutoCreation, NoGroupInjection, AlwaysUpdateSystem]
    public class LatiosServerInitializationSystemGroup : ServerInitializationSystemGroup
    {
        private SyncPointPlaybackSystem m_syncPlayback;
        private MergeBlackboardsSystem m_mergeGlobals;
        private ManagedComponentsReactiveSystemGroup m_cleanupGroup;
        private LatiosWorldSyncGroup m_syncGroup;
        private PreSyncPointGroup m_preSyncGroup;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_syncPlayback = World.CreateSystem<SyncPointPlaybackSystem>();
            m_mergeGlobals = World.CreateSystem<MergeBlackboardsSystem>();
            m_cleanupGroup = World.CreateSystem<ManagedComponentsReactiveSystemGroup>();
            m_syncGroup    = World.GetOrCreateSystem<LatiosWorldSyncGroup>();
            m_preSyncGroup = World.GetOrCreateSystem<PreSyncPointGroup>();
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

    [DisableAutoCreation, NoGroupInjection, AlwaysUpdateSystem]
    public class LatiosClientInitializationSystemGroup : ClientInitializationSystemGroup
    {
        private SyncPointPlaybackSystem m_syncPlayback;
        private MergeBlackboardsSystem m_mergeGlobals;
        private ManagedComponentsReactiveSystemGroup m_cleanupGroup;
        private LatiosWorldSyncGroup m_syncGroup;
        private PreSyncPointGroup m_preSyncGroup;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_syncPlayback = World.CreateSystem<SyncPointPlaybackSystem>();
            m_mergeGlobals = World.CreateSystem<MergeBlackboardsSystem>();
            m_cleanupGroup = World.CreateSystem<ManagedComponentsReactiveSystemGroup>();
            m_syncGroup    = World.GetOrCreateSystem<LatiosWorldSyncGroup>();
            m_preSyncGroup = World.GetOrCreateSystem<PreSyncPointGroup>();
            AddSystemToUpdateList(m_syncPlayback);
            AddSystemToUpdateList(m_syncGroup);
            AddSystemToUpdateList(m_preSyncGroup);

            m_syncGroup.AddSystemToUpdateList(m_cleanupGroup);
            m_syncGroup.AddSystemToUpdateList(m_mergeGlobals);
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

    [DisableAutoCreation, NoGroupInjection]
    public class LatiosServerSimulationSystemGroup : ServerSimulationSystemGroup
    {
        InsideOutRateManager m_insideOutHook = new InsideOutRateManager();

        protected override void OnUpdate()
        {
            // This conditional check shouldn't be necessary, but serves as a cheap self-cleansing.
            m_insideOutHook.m_realRateManager = RateManager != m_insideOutHook ? RateManager : null;
            RateManager                       = m_insideOutHook;
            base.OnUpdate();
            RateManager = m_insideOutHook.m_realRateManager;
        }
    }

    [DisableAutoCreation, NoGroupInjection]
    public class LatiosClientSimulationSystemGroup : ClientSimulationSystemGroup
    {
        InsideOutRateManager m_insideOutHook = new InsideOutRateManager();

        protected override void OnUpdate()
        {
            // This conditional check shouldn't be necessary, but serves as a cheap self-cleansing.
            m_insideOutHook.m_realRateManager = RateManager != m_insideOutHook ? RateManager : null;
            RateManager                       = m_insideOutHook;
            base.OnUpdate();
            RateManager = m_insideOutHook.m_realRateManager;
        }
    }

    [DisableAutoCreation, NoGroupInjection]
    public class LatiosClientPresentationSystemGroup : ClientPresentationSystemGroup
    {
        SystemSortingTracker m_tracker;

        protected override void OnUpdate()
        {
            if (HasSingleton<ThinClientComponent>())
                return;

            SuperSystem.DoSuperSystemUpdate(this, ref m_tracker);
        }
    }

    internal class InsideOutRateManager : IRateManager
    {
        SystemSortingTracker m_tracker;
        internal IRateManager m_realRateManager;

        public float Timestep { get; set; }

        public bool ShouldGroupUpdate(ComponentSystemGroup group)
        {
            group.RateManager = m_realRateManager;
            SuperSystem.DoSuperSystemUpdate(group, ref m_tracker);

            // If the group's rate manager changed, capture it.
            m_realRateManager = group.RateManager;

            group.RateManager = this;
            return false;
        }
    }
}
#endif

