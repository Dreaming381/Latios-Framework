using Latios;
using Latios.Psyshock;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Dragons
{
    [DisableAutoCreation]
    public partial class FindPairsBuildLayerExample : SubSystem
    {
        private EntityQuery m_query;
        protected override void OnCreate()
        {
            m_query = Fluent.PatchQueryForBuildingCollisionLayer().Build();
        }

        public override bool ShouldUpdateSystem()
        {
            if (!worldBlackboardEntity.HasComponent<CurrentScene>())
                return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name.Equals("FindPairsBuildLayerExample");
            var currentScene = worldBlackboardEntity.GetComponentData<CurrentScene>();
            return currentScene.current.Equals("FindPairsBuildLayerExample");
        }

        protected override void OnUpdate()
        {
            CollisionLayerSettings settings = new CollisionLayerSettings
            {
                worldSubdivisionsPerAxis = new int3(2, 2, 2),
                worldAabb                = new Aabb { min = new float3(-50f, -50f, -50f), max = new float3(50f, 50f, 50f) },
            };
            Dependency = Physics.BuildCollisionLayer(m_query, this).WithSettings(settings).ScheduleParallel(out CollisionLayer layer, Allocator.TempJob, Dependency);
            Dependency = PhysicsDebug.DrawLayer(layer).ScheduleParallel(Dependency);
            Dependency = layer.Dispose(Dependency);
        }
    }
}

