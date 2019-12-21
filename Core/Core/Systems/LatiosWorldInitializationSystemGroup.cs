using System.Collections.Generic;
using Unity.Entities;

namespace Latios
{
    public class MyInitializationSystemGroup : InitializationSystemGroup
    {
        private SceneManagerSystem                     m_sceneManager;
        private MergeGlobalsSystem                     m_mergeGlobals;
        private DestroyEntitiesOnSceneChangeSystem     m_destroySystem;
        private ManagedStructStorageCleanupSystemGroup m_cleanupGroup;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_sceneManager  = World.CreateSystem<SceneManagerSystem>();
            m_mergeGlobals  = World.CreateSystem<MergeGlobalsSystem>();
            m_destroySystem = World.CreateSystem<DestroyEntitiesOnSceneChangeSystem>();
            m_cleanupGroup  = World.CreateSystem<ManagedStructStorageCleanupSystemGroup>();
        }

        public override void SortSystemUpdateList()
        {
            //m_destroySystem does not have a normal update, so don't add it to the list.
            m_systemsToUpdate.Remove(m_sceneManager);
            m_systemsToUpdate.Remove(m_mergeGlobals);
            m_systemsToUpdate.Remove(m_cleanupGroup);
            base.SortSystemUpdateList();
            // Re-insert built-in systems to construct the final list
            var finalSystemList = new List<ComponentSystemBase>(2 + m_systemsToUpdate.Count);
            finalSystemList.Add(m_mergeGlobals);
            finalSystemList.Add(m_sceneManager);
            finalSystemList.Add(m_cleanupGroup);
            foreach (var s in m_systemsToUpdate)
                finalSystemList.Add(s);
            m_systemsToUpdate = finalSystemList;
        }
    }
}

