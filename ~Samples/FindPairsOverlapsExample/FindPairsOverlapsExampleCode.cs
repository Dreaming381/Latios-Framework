using Latios;
using Latios.PhysicsEngine;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace Dragons
{
    public class FindPairsOverlapsExampleLoop : RootSuperSystem
    {
        protected override void CreateSystems()
        {
            GetOrCreateAndAddSystem<FindPairsOverlapsExampleSystem>();
        }
    }

    [AlwaysSynchronizeSystem]
    [AlwaysUpdateSystem]
    public class FindPairsOverlapsExampleSystem : JobSubSystem
    {
        public override bool ShouldUpdateSystem()
        {
            var currentScene = worldGlobalEntity.GetComponentData<CurrentScene>();
            return currentScene.current.Equals("FindPairsOverlapExample");
        }

        EntityQuery m_query;

        bool m_firstFrame = true;

        protected override JobHandle OnUpdate(JobHandle inputDeps)
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

            var                    typeGroup = Physics.BuildLayerChunkTypeGroup(this);
            CollisionLayerSettings settings  = new CollisionLayerSettings
            {
                layerType               = CollisionLayerType.Discrete,
                worldBucketCountPerAxis = new int3(1, 4, 4),
                worldAABB               = new Latios.PhysicsEngine.AABB(-1000f, 1000f),
            };
            Physics.BuildCollisionLayer(m_query, typeGroup, settings, Allocator.TempJob, default, out CollisionLayer layer).Complete();
            PhysicsDebug.DrawFindPairs(layer);
            layer.Dispose();

            return inputDeps;
        }
    }
}

