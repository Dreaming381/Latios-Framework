using Debug = UnityEngine.Debug;
using Unity.Entities;
using UnityEngine.SceneManagement;

namespace Latios.Systems
{
    internal struct LatiosSceneChangeDummyTag : IComponentData { }

    /// <summary>
    /// This system is responsible for destroying entities on scene change. As of Entities 1.0 experimental,
    /// the usefulness of this system is limited to procedurally-generated entities at runtime.
    /// This system is not installed by default.
    /// </summary>
    [DisableAutoCreation]
    [UpdateInGroup(typeof(InitializationSystemGroup), OrderLast = true)]  //Doesn't matter, but good for visualization
    public partial class DestroyEntitiesOnSceneChangeSystem : SubSystem
    {
        EntityQuery m_destroyQuery;
        EntityQuery m_unitySubsceneLoadQuery;

        protected override void OnCreate()
        {
            m_destroyQuery = Fluent.With<LatiosSceneChangeDummyTag>().Without<WorldBlackboardTag>().Without<DontDestroyOnSceneChangeTag>().Without<RequestSceneLoaded>()
                             .IncludePrefabs().IncludeDisabledEntities().Build();

            m_unitySubsceneLoadQuery         = Fluent.With<Unity.Entities.RequestSceneLoaded>().Build();
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
        }
    }
}

