using System.Diagnostics;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.SceneManagement;

namespace Latios.Systems
{
    /// <summary>
    /// This system is responsible for:
    /// 1) Changing scenes at the presence of RequestLoadScene
    /// 2) Keeping CurrentScene on the worldBlackboardEntity up-to-date
    /// 3) Resetting the sceneBlackboardEntity
    /// 4) Invoking OnNewScene() callbacks
    /// 5) Generating the pause frame
    /// 6) Forcing subscenes to load synchronously
    /// 7) Ensuring entities with DontDestroyOnSceneChangeTag are preserved when a subscene is unloaded on a scene change.
    /// This system is not installed by default.
    /// </summary>
    [DisableAutoCreation]
    [UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true)]
    [UpdateAfter(typeof(SyncPointPlaybackSystemDispatch))]
    public partial class SceneManagerSystem : SubSystem
    {
        private EntityQuery m_rlsQuery;
        private EntityQuery m_unitySubsceneLoadQuery;
        private EntityQuery m_dontDestroyOnSceneChangeQuery;
        private EntityQuery m_newSubsceneReferenceQuery;
        private bool        m_paused = false;

        private struct ObservedSubsceneReferenceTag : IComponentData { }

        protected override void OnCreate()
        {
            CurrentScene curr = new CurrentScene
            {
                currentScene      = new FixedString128Bytes(),
                previousScene     = new FixedString128Bytes(),
                isSceneFirstFrame = false
            };
            worldBlackboardEntity.AddComponentData(curr);

            m_unitySubsceneLoadQuery        = Fluent.With<Unity.Entities.RequestSceneLoaded>().Without<DisableSynchronousSubsceneLoadingTag>().Build();
            m_dontDestroyOnSceneChangeQuery = Fluent.With<Unity.Entities.SceneTag>().With<DontDestroyOnSceneChangeTag>().IncludeDisabledEntities().IncludePrefabs().Build();
            m_newSubsceneReferenceQuery     = Fluent.With<Unity.Scenes.SubScene>().Without<ObservedSubsceneReferenceTag>().Build();
        }

        protected override void OnUpdate()
        {
            if (!m_rlsQuery.IsEmptyIgnoreFilter)
            {
                FixedString128Bytes targetScene = new FixedString128Bytes();
                bool                isInvalid   = false;

                Entities.WithStoreEntityQueryInField(ref m_rlsQuery).ForEach((ref RequestLoadScene rls) =>
                {
                    if (rls.newScene.Length == 0)
                        return;
                    if (targetScene.Length == 0)
                        targetScene = rls.newScene;
                    else if (rls.newScene != targetScene)
                        isInvalid = true;
                }).Run();

                if (targetScene.Length > 0)
                {
                    if (isInvalid)
                    {
                        UnityEngine.Debug.LogError("Multiple scenes were requested to load during the previous frame.");
                        EntityManager.RemoveComponent<RequestLoadScene>(m_rlsQuery);
                    }
                    else
                    {
                        var curr           = worldBlackboardEntity.GetComponentData<CurrentScene>();
                        curr.previousScene = curr.currentScene;
                        EntityManager.RemoveComponent<Unity.Entities.SceneTag>(m_dontDestroyOnSceneChangeQuery);
                        UnityEngine.Debug.Log("Loading scene: " + targetScene);
                        SceneManager.LoadScene(targetScene.ToString());
                        latiosWorld.Pause();
                        m_paused               = true;
                        curr.currentScene      = targetScene;
                        curr.isSceneFirstFrame = true;
                        worldBlackboardEntity.SetComponentData(curr);
                        EntityManager.RemoveComponent<RequestLoadScene>(m_rlsQuery);
                        return;
                    }
                }
            }

            //Handle case where initial scene loads or set firstFrame to false
            var currentScene = worldBlackboardEntity.GetComponentData<CurrentScene>();
            if (currentScene.currentScene.Length == 0)
            {
                currentScene.currentScene      = SceneManager.GetActiveScene().name;
                currentScene.isSceneFirstFrame = true;
                worldBlackboardEntity.SetComponentData(currentScene);
            }
            else if (m_paused)
            {
                m_paused = false;
            }
            else
            {
                currentScene.isSceneFirstFrame = false;
            }
            worldBlackboardEntity.SetComponentData(currentScene);

            if (!m_newSubsceneReferenceQuery.IsEmptyIgnoreFilter)
            {
                var newSubsceneEntities = m_newSubsceneReferenceQuery.ToEntityArray(Allocator.Temp);
                foreach (var e in newSubsceneEntities)
                {
                    var subsceneRef = EntityManager.GetComponentObject<Unity.Scenes.SubScene>(e);
                    if (subsceneRef.TryGetComponent<SubsceneLoadOptions>(out var options))
                    {
                        if (options.loadOptions == SubsceneLoadOptions.LoadOptions.Asynchronous)
                        {
                            EntityManager.AddComponent<DisableSynchronousSubsceneLoadingTag>(e);
                            if (EntityManager.HasComponent<Unity.Scenes.ResolvedSectionEntity>(e))
                            {
                                foreach (var s in EntityManager.GetBuffer<Unity.Scenes.ResolvedSectionEntity>(e))
                                    EntityManager.AddComponent<DisableSynchronousSubsceneLoadingTag>(s.SectionEntity);
                            }
                        }
                    }
                }
                EntityManager.AddComponent<ObservedSubsceneReferenceTag>(m_newSubsceneReferenceQuery);
            }

            if (m_unitySubsceneLoadQuery.IsEmptyIgnoreFilter)
                return;

            var subsceneRequests = m_unitySubsceneLoadQuery.ToComponentDataArray<RequestSceneLoaded>(Allocator.Temp);
            var subsceneEntities = m_unitySubsceneLoadQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < subsceneEntities.Length; i++)
            {
                var subsceneEntity = subsceneEntities[i];
                var request        = subsceneRequests[i];
                if ((request.LoadFlags & SceneLoadFlags.DisableAutoLoad) == 0)
                {
                    request.LoadFlags |= SceneLoadFlags.BlockOnStreamIn;
                    if (EntityManager.HasComponent<Unity.Scenes.ResolvedSectionEntity>(subsceneEntity))
                    {
                        foreach (var s in EntityManager.GetBuffer<Unity.Scenes.ResolvedSectionEntity>(subsceneEntity))
                            EntityManager.AddComponentData(s.SectionEntity, request);
                    }
                    else if (EntityManager.HasComponent<Unity.Scenes.SceneEntityReference>(subsceneEntity))
                    {
                        var actualSubscene = EntityManager.GetComponentData<Unity.Scenes.SceneEntityReference>(subsceneEntity).SceneEntity;
                        if (EntityManager.HasComponent<DisableSynchronousSubsceneLoadingTag>(actualSubscene))
                        {
                            EntityManager.AddComponent<DisableSynchronousSubsceneLoadingTag>(subsceneEntity);
                            continue;
                        }
                    }
                    EntityManager.SetComponentData(subsceneEntity, request);
                }
            }
        }
    }
}

