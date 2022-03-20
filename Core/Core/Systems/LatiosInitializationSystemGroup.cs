using System.Collections.Generic;
using System.Linq;
using Debug = UnityEngine.Debug;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Entities.Exposed.Dangerous;

namespace Latios.Systems
{
    public class LatiosInitializationSystemGroup : InitializationSystemGroup
    {
        private SyncPointPlaybackSystem              m_syncPlayback;
        private SceneManagerSystem                   m_sceneManager;
        private MergeBlackboardsSystem               m_mergeGlobals;
        private DestroyEntitiesOnSceneChangeSystem   m_destroySystem;
        private ManagedComponentsReactiveSystemGroup m_cleanupGroup;
        private LatiosWorldSyncGroup                 m_syncGroup;
        private PreSyncPointGroup                    m_preSyncGroup;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_syncPlayback  = World.CreateSystem<SyncPointPlaybackSystem>();
            m_sceneManager  = World.CreateSystem<SceneManagerSystem>();
            m_mergeGlobals  = World.CreateSystem<MergeBlackboardsSystem>();
            m_destroySystem = World.CreateSystem<DestroyEntitiesOnSceneChangeSystem>();
            m_cleanupGroup  = World.CreateSystem<ManagedComponentsReactiveSystemGroup>();
            m_syncGroup     = World.GetOrCreateSystem<LatiosWorldSyncGroup>();
            m_preSyncGroup  = World.GetOrCreateSystem<PreSyncPointGroup>();
            AddSystemToUpdateList(m_syncPlayback);
            AddSystemToUpdateList(m_sceneManager);
            AddSystemToUpdateList(m_destroySystem);
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

    [DisableAutoCreation]
    [UpdateInGroup(typeof(LatiosInitializationSystemGroup))]
    [UpdateAfter(typeof(Unity.Scenes.SceneSystemGroup))]
    [UpdateAfter(typeof(ConvertToEntitySystem))]
    public class LatiosWorldSyncGroup : ComponentSystemGroup
    {
        SystemSortingTracker m_tracker;

        protected override void OnUpdate()
        {
            SuperSystem.DoSuperSystemUpdate(this, ref m_tracker);
        }
    }

    [DisableAutoCreation]
    [UpdateInGroup(typeof(LatiosInitializationSystemGroup), OrderFirst = true)]
    [UpdateBefore(typeof(BeginInitializationEntityCommandBufferSystem))]
    public class PreSyncPointGroup : ComponentSystemGroup
    {
        SystemSortingTracker m_tracker;

        protected override void OnUpdate()
        {
            SuperSystem.DoSuperSystemUpdate(this, ref m_tracker);
        }
    }
}

