using Unity.Entities;
using UnityEngine.SceneManagement;

namespace Latios
{
    internal struct LatiosSceneChangeDummyTag : IComponentData { }

    [AlwaysUpdateSystem]
    public class DestroyEntitiesOnSceneChangeSystem : SubSystem
    {
        private EntityQuery destroyQuery = null;

        protected override void OnCreate()
        {
            EntityQueryDesc desc = new EntityQueryDesc
            {
                All = new ComponentType[]
                {
                    typeof(LatiosSceneChangeDummyTag)
                },
                None = new ComponentType[]
                {
                    typeof(WorldGlobalTag),
                    typeof(DontDestroyOnSceneChangeTag)
                }
            };
            destroyQuery                = GetEntityQuery(desc);
            SceneManager.sceneUnloaded += RealUpdateOnSceneChange;
        }

        protected override void OnUpdate()
        {
        }

        private void RealUpdateOnSceneChange(Scene unloaded)
        {
            if (unloaded.isSubScene)
                return;

            //Why are add and remove inconsistent?
            EntityManager.AddComponent(EntityManager.UniversalQuery, typeof(LatiosSceneChangeDummyTag));
            EntityManager.DestroyEntity(destroyQuery);
            EntityManager.RemoveComponent<LatiosSceneChangeDummyTag>(EntityManager.UniversalQuery);
            latiosWorld.CreateNewSceneGlobalEntity();
        }
    }
}

