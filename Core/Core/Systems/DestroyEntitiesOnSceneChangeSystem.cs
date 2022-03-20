using Debug = UnityEngine.Debug;
using Unity.Entities;
using UnityEngine.SceneManagement;

namespace Latios.Systems
{
    internal struct LatiosSceneChangeDummyTag : IComponentData { }

    [DisableAutoCreation]
    [AlwaysUpdateSystem]
    [UpdateInGroup(typeof(LatiosInitializationSystemGroup), OrderLast = true)]  //Doesn't matter, but good for visualization
    public partial class DestroyEntitiesOnSceneChangeSystem : SubSystem
    {
        private EntityQuery m_destroyQuery = default;

        protected override void OnCreate()
        {
            m_destroyQuery = Fluent.WithAll<LatiosSceneChangeDummyTag>().Without<WorldBlackboardTag>().Without<DontDestroyOnSceneChangeTag>().Without<RequestSceneLoaded>()
                             .IncludePrefabs().IncludeDisabled().Build();
            SceneManager.activeSceneChanged += RealUpdateOnSceneChange;
        }

        protected override void OnDestroy()
        {
            SceneManager.activeSceneChanged -= RealUpdateOnSceneChange;
        }

        protected override void OnUpdate()
        {
        }

        private void RealUpdateOnSceneChange(Scene unloaded, Scene loaded)
        {
            if (!Enabled)
                return;

            latiosWorld.ResumeNextFrame();
            EntityManager.AddComponent<LatiosSceneChangeDummyTag>(EntityManager.UniversalQuery);
            EntityManager.DestroyEntity(m_destroyQuery);
            EntityManager.RemoveComponent<LatiosSceneChangeDummyTag>(EntityManager.UniversalQuery);
            latiosWorld.CreateNewSceneBlackboardEntity();
        }
    }
}

