using System.Collections.Generic;
using System.Linq;
using Debug = UnityEngine.Debug;
using Unity.Entities;

namespace Latios.Systems
{
    public class LatiosInitializationSystemGroup : InitializationSystemGroup
    {
        private SceneManagerSystem                   m_sceneManager;
        private MergeGlobalsSystem                   m_mergeGlobals;
        private DestroyEntitiesOnSceneChangeSystem   m_destroySystem;
        private ManagedComponentsReactiveSystemGroup m_cleanupGroup;
        private LatiosSyncPointGroup                 m_syncGroup;

        private BeginInitializationEntityCommandBufferSystem m_beginECB;
        private EndInitializationEntityCommandBufferSystem   m_endECB;

        //This is not a perfect fix, but there isn't a great workaround atm.
        private bool                      m_initialized     = false;
        private List<ComponentSystemBase> m_systemsToUpdate = new List<ComponentSystemBase>();

        protected override void OnCreate()
        {
            base.OnCreate();
            m_sceneManager  = World.CreateSystem<SceneManagerSystem>();
            m_mergeGlobals  = World.CreateSystem<MergeGlobalsSystem>();
            m_destroySystem = World.CreateSystem<DestroyEntitiesOnSceneChangeSystem>();
            m_cleanupGroup  = World.CreateSystem<ManagedComponentsReactiveSystemGroup>();
            m_syncGroup     = World.GetOrCreateSystem<LatiosSyncPointGroup>();
        }

        public void SortSystemUpdateList()
        {
            //m_destroySystem does not have a normal update, so don't add it to the list.

            //Remove from list to add back later.
            m_beginECB = World.GetExistingSystem<BeginInitializationEntityCommandBufferSystem>();
            m_endECB   = World.GetExistingSystem<EndInitializationEntityCommandBufferSystem>();
            AddSystemToUpdateList(m_sceneManager);
            AddSystemToUpdateList(m_mergeGlobals);
            AddSystemToUpdateList(m_cleanupGroup);
            SortSystems();
            var systems = Systems.ToList();
            systems.Remove(m_beginECB);
            systems.Remove(m_endECB);
            systems.Remove(m_sceneManager);
            systems.Remove(m_mergeGlobals);
            systems.Remove(m_cleanupGroup);
            systems.Remove(m_syncGroup);
            systems.Remove(m_destroySystem);
            // Re-insert built-in systems to construct the final list
            var finalSystemList = new List<ComponentSystemBase>(5 + systems.Count);
            finalSystemList.Add(m_beginECB);
            finalSystemList.Add(m_sceneManager);

            //Todo: MergeGlobals and CleanupGroup need to happen after scene loads and patches but before user code.
            //However, there has to be a cleaner way to do this that doesn't make so many assumptions
            int index;
            for (index = systems.Count - 1; index >= 0; index--)
            {
                if (systems[index].GetType().Namespace.Contains("Unity"))
                    break;
            }

            foreach (var s in systems)
            {
                finalSystemList.Add(s);

                if (index == 0)
                {
                    finalSystemList.Add(m_mergeGlobals);
                    finalSystemList.Add(m_cleanupGroup);
                    finalSystemList.Add(m_syncGroup);
                }
                index--;
            }
            finalSystemList.Add(m_endECB);
            var systemsToUpdate = m_systemsToUpdate;
            systemsToUpdate.Clear();
            /*foreach (var s in Systems)
               {
                RemoveSystemFromUpdateList(s);
               }
               base.SortSystemUpdateList();*/
            SortSystems();
            var unitySystems = Systems as List<ComponentSystemBase>;
            unitySystems.Clear();
            foreach (var s in finalSystemList)
            {
                m_systemsToUpdate.Add(s);
                unitySystems.Add(s);
            }

            m_initialized = true;
        }

        protected override void OnUpdate()
        {
            if (!m_initialized)
                SortSystemUpdateList();

            LatiosWorld lw = World as LatiosWorld;
            lw.FrameStart();
            foreach (var sys in m_systemsToUpdate)
            {
                if (lw.paused)
                    break;
                sys.Update();
            }
        }

        public override IReadOnlyList<ComponentSystemBase> Systems
        {
            get
            {
                if (m_initialized)
                    return m_systemsToUpdate;
                else
                    return base.Systems;
            }
        }
    }

    [UpdateInGroup(typeof(LatiosInitializationSystemGroup))]
    public class LatiosSyncPointGroup : ComponentSystemGroup
    {
    }
}

