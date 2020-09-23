using System.Diagnostics;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.SceneManagement;

namespace Latios.Systems
{
    [AlwaysUpdateSystem]
    public class SceneManagerSystem : SubSystem
    {
        private EntityQuery m_rlsQuery;
        private bool        m_paused = false;

        protected override void OnCreate()
        {
            CurrentScene curr = new CurrentScene
            {
                currentScene      = new FixedString128(),
                previousScene     = new FixedString128(),
                isSceneFirstFrame = false
            };
            worldGlobalEntity.AddOrSetComponentData(curr);
        }

        protected override void OnUpdate()
        {
            if (m_rlsQuery.CalculateChunkCount() > 0)
            {
                FixedString128 targetScene = new FixedString128();
                bool           isInvalid   = false;

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
                        var curr           = worldGlobalEntity.GetComponentData<CurrentScene>();
                        curr.previousScene = curr.currentScene;
                        UnityEngine.Debug.Log("Loading scene: " + targetScene);
                        SceneManager.LoadScene(targetScene.ToString());
                        latiosWorld.Pause();
                        m_paused               = true;
                        curr.currentScene      = targetScene;
                        curr.isSceneFirstFrame = true;
                        worldGlobalEntity.SetComponentData(curr);
                        EntityManager.RemoveComponent<RequestLoadScene>(m_rlsQuery);
                        return;
                    }
                }
            }

            //Handle case where initial scene loads or set firstFrame to false
            var currentScene = worldGlobalEntity.GetComponentData<CurrentScene>();
            if (currentScene.currentScene.Length == 0)
            {
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
            worldGlobalEntity.SetComponentData(currentScene);
        }
    }
}

