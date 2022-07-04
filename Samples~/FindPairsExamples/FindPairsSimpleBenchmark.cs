using Latios;
using Latios.Psyshock;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace Dragons
{
    [AlwaysUpdateSystem, DisableAutoCreation]
    public partial class FindPairsSimpleBenchmark : SubSystem
    {
        private struct DestroyOnCollisionProcessor : IFindPairsProcessor
        {
            public DestroyCommandBuffer.ParallelWriter dcb;

            public void Execute(FindPairsResult result)
            {
                if (Physics.DistanceBetween(result.bodyA.collider, result.bodyA.transform, result.bodyB.collider, result.bodyB.transform, 0f, out ColliderDistanceResult _))
                {
                    dcb.Add(result.entityA, result.jobIndex);
                    dcb.Add(result.entityB, result.jobIndex);
                }
            }
        }

        private EntityQuery m_query;
        private int         m_frameCount = 0;
        protected override void OnCreate()
        {
            m_query = Fluent.PatchQueryForBuildingCollisionLayer().Build();
        }

        public override bool ShouldUpdateSystem()
        {
            if (!worldBlackboardEntity.HasComponent<CurrentScene>())
                return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name.Equals("FindPairsSimpleBenchmark");
            var currentScene = worldBlackboardEntity.GetComponentData<CurrentScene>();
            return currentScene.current.Equals("FindPairsSimpleBenchmark");
        }

        protected override void OnUpdate()
        {
            if (m_frameCount <= 1)  //Due to a missing line below, I was actually profiling 200000 entities instead of 100000. This reproduces that behavior.
            {
                var archetype = EntityManager.CreateArchetype(typeof(Translation), typeof(Rotation), typeof(Collider), typeof(LocalToWorld));
                EntityManager.CreateEntity(archetype, 100000, Allocator.Temp);
                var random = new Random(1123);
                Entities.ForEach((ref Translation trans, ref Rotation rot, ref Collider col) =>
                {
                    trans.Value = random.NextFloat3(new float3(-2000f), new float3(2000f));
                    rot.Value   = random.NextQuaternionRotation();
                    col         = new CapsuleCollider(random.NextFloat3(-10f, 10f), random.NextFloat3(-10f, 10f), random.NextFloat(0f, 10f));
                }).Run();
            }

            CollisionLayerSettings settings = new CollisionLayerSettings
            {
                worldSubdivisionsPerAxis = new int3(1, 4, 4),
                worldAABB                = new Aabb(-2000f, 2000f),
            };

            for (int i = 0; i < 3; i++)
            {
                var dcb       = new DestroyCommandBuffer(Allocator.TempJob);
                var processor = new DestroyOnCollisionProcessor { dcb = dcb.AsParallelWriter() };

                UnityEngine.Profiling.Profiler.BeginSample("BuildLayer");
                Physics.BuildCollisionLayer(m_query, this).WithSettings(settings).ScheduleParallel(out CollisionLayer layer, Allocator.TempJob).Complete();
                UnityEngine.Profiling.Profiler.EndSample();

                UnityEngine.Profiling.Profiler.BeginSample("FindPairs");
                Physics.FindPairs(layer, processor).ScheduleParallel().Complete();
                UnityEngine.Profiling.Profiler.EndSample();

                dcb.Dispose();
                layer.Dispose();
            }
            m_frameCount++;  //This line was missing which caused me to measure 200,000 colliders instead of 100,000
        }
    }
}

