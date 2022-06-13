using System.Diagnostics;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.SceneManagement;

namespace Latios.Systems
{
    [DisableAutoCreation]
    [AlwaysUpdateSystem]
    [UpdateInGroup(typeof(LatiosInitializationSystemGroup), OrderFirst = true)]
    [UpdateAfter(typeof(SyncPointPlaybackSystem))]
    public partial class SceneManagerSystem : SubSystem
    {
        private EntityQuery m_rlsQuery;
        private bool        m_paused = false;

        protected override void OnCreate()
        {
            CurrentScene curr = new CurrentScene
            {
                currentScene      = new FixedString128Bytes(),
                previousScene     = new FixedString128Bytes(),
                isSceneFirstFrame = false
            };
            worldBlackboardEntity.AddComponentData(curr);

            latiosWorld.autoGenerateSceneBlackboardEntity = false;
        }

        protected override void OnUpdate()
        {
            if (m_rlsQuery.CalculateChunkCount() > 0)
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
                latiosWorld.CreateNewSceneBlackboardEntity();
                currentScene.currentScene      = SceneManager.GetActiveScene().name;
                currentScene.isSceneFirstFrame = true;
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
        }
    }
}

