using System.Collections.Generic;
using System.Linq;
using Debug = UnityEngine.Debug;
using Unity.Entities;

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

        protected override void OnUpdate()
        {
            LatiosWorld lw = World as LatiosWorld;
            lw.FrameStart();
            for (int i = 0; i < Systems.Count; i++)
            {
                if (lw.paused)
                    break;
                SuperSystem.UpdateManagedSystem(Systems[i]);
            }
        }
    }

    [DisableAutoCreation]
    [UpdateInGroup(typeof(LatiosInitializationSystemGroup))]
    [UpdateAfter(typeof(Unity.Scenes.SceneSystemGroup))]
    public class LatiosWorldSyncGroup : ComponentSystemGroup
    {
        protected override void OnUpdate()
        {
            SuperSystem.UpdateAllManagedSystems(this);
        }
    }

    [DisableAutoCreation]
    [UpdateInGroup(typeof(LatiosInitializationSystemGroup), OrderFirst = true)]
    [UpdateBefore(typeof(BeginInitializationEntityCommandBufferSystem))]
    public class PreSyncPointGroup : ComponentSystemGroup
    {
        protected override void OnUpdate()
        {
            foreach (var sys in Systems)
            {
                SuperSystem.UpdateAllManagedSystems(this);
            }
        }
    }
}

