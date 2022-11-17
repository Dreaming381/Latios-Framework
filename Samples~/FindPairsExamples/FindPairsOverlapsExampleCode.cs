using Latios;
using Latios.Psyshock;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace Dragons
{
    [DisableAutoCreation]
    public partial class FindPairsOverlapsExampleSystem : SubSystem
    {
        public override bool ShouldUpdateSystem()
        {
            if (!worldBlackboardEntity.HasComponent<CurrentScene>())
                return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name.Equals("FindPairsOverlapExample");
            var currentScene = worldBlackboardEntity.GetComponentData<CurrentScene>();
            return currentScene.current.Equals("FindPairsOverlapExample");
        }

        EntityQuery m_query;

        bool m_firstFrame = true;

        protected override void OnUpdate()
        {
            if (m_firstFrame)
            {
                var archetype = EntityManager.CreateArchetype(typeof(Translation), typeof(Rotation), typeof(Collider), typeof(LocalToWorld));
                EntityManager.CreateEntity(archetype, 50, Allocator.Temp);
                var random = new Random(1123);
                Entities.WithStoreEntityQueryInField(ref m_query).WithAll<LocalToWorld>().ForEach((ref Translation c0, ref Rotation c1, ref Collider c2) =>
                {
                    c0.Value = random.NextFloat3(new float3(-18f), new float3(18f));
                    c1.Value = random.NextQuaternionRotation();
                    c2       = new CapsuleCollider(float3.zero, new float3(2f), 0.6f);
                }).Run();
                m_firstFrame = false;
            }

            CollisionLayerSettings settings = new CollisionLayerSettings
            {
                worldSubdivisionsPerAxis = new int3(1, 4, 4),
                worldAabb                = new Aabb(-1000f, 1000f),
            };
            Dependency = Physics.BuildCollisionLayer(m_query, this).WithSettings(settings).ScheduleParallel(out CollisionLayer layer, Allocator.TempJob, Dependency);
            Dependency = PhysicsDebug.DrawFindPairs(layer).ScheduleParallel(Dependency);
            Dependency = layer.Dispose(Dependency);
        }
    }
}

