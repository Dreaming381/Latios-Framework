using Unity.Collections;
using Unity.Entities;
using UnityEngine.SceneManagement;

namespace Latios
{
    [AlwaysUpdateSystem]
    public class SceneManagerSystem : SubSystem
    {
        private EntityQuery m_rlsQuery;

        protected override void OnCreate()
        {
            CurrentScene curr = new CurrentScene
            {
                currentScene      = new NativeString128(),
                previousScene     = new NativeString128(),
                isSceneFirstFrame = false
            };
            worldGlobalEntity.AddOrSetComponentData(curr);

            m_rlsQuery = GetEntityQuery(typeof(RequestLoadScene));
        }

        //ScheduleSingle
        public struct CheckScenesJob : IJobForEach<RequestLoadScene>
        {
            public NativeArray<NativeString128> targetScene;
            public NativeArray<bool>            isInvalid;

            public void Execute(ref RequestLoadScene rls)
            {
                if (rls.newScene.LengthInBytes == 0)
                    return;
                if (targetScene[0].LengthInBytes == 0)
                    targetScene[0] = rls.newScene;
                else if (!rls.newScene.Equals(targetScene[0]))
                    isInvalid[0] = true;
            }
        }

        protected override void OnUpdate()
        {
            if (m_rlsQuery.CalculateChunkCount() > 0)
            {
                var checkScenesJob = new CheckScenesJob
                {
                    targetScene = new NativeArray<NativeString128>(1, Allocator.TempJob),
                    isInvalid   = new NativeArray<bool>(1, Allocator.TempJob)
                };

                checkScenesJob.Run(this);

                var targetScene = checkScenesJob.targetScene[0];
                var isInvalid   = checkScenesJob.isInvalid[0];
                checkScenesJob.isInvalid.Dispose();
                checkScenesJob.targetScene.Dispose();

                if (targetScene.LengthInBytes > 0)
                {
                    if (!isInvalid)
                    {
                        UnityEngine.Debug.LogError("Multiple scenes were requested to load during the previous frame.");
                    }
                    else
                    {
                        var curr           = worldGlobalEntity.GetComponentData<CurrentScene>();
                        curr.previousScene = curr.currentScene;
                        SceneManager.LoadScene(targetScene.ToString());
                        curr.currentScene      = targetScene;
                        curr.isSceneFirstFrame = true;
                        worldGlobalEntity.SetComponentData(curr);
                        return;
                    }
                }
            }

            //Handle case where initial scene loads or set firstFrame to false
            var currentScene = worldGlobalEntity.GetComponentData<CurrentScene>();
            if (currentScene.currentScene.LengthInBytes == 0)
            {
                currentScene.currentScene      = new NativeString128(SceneManager.GetActiveScene().name);
                currentScene.isSceneFirstFrame = true;
            }
            else
                currentScene.isSceneFirstFrame = false;
            worldGlobalEntity.SetComponentData(currentScene);
        }
    }
}

