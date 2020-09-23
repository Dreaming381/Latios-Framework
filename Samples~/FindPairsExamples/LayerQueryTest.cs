using Latios.PhysicsEngine;
using TMPro;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

//Not ready for release yet.
/*
   namespace Latios
   {
    [AlwaysUpdateSystem]
    public class LayerQueryTest : SubSystem
    {
        [BurstCompile]
        struct GenerateJob : IJobForEach<Translation, Rotation, Collider>
        {
            public Random random;
            public void Execute(ref Translation c0, ref Rotation c1, ref Collider c2)
            {
                c0.Value = random.NextFloat3(new float3(-1000f), new float3(1000f));
                c1.Value = random.NextQuaternionRotation();
                c2       = new BoxCollider(float3.zero, random.NextFloat3(float3.zero, new float3(100f, 100f, 100f)));
            }
        }

        [BurstCompile]
        private struct CountProcessor : IFindPairsProcessor
        {
            public NativeArray<int> count;

            public void Execute(FindPairsResult result)
            {
                count[0]++;
            }
        }

        [BurstCompile]
        struct HardCountJob : IJob
        {
            public CollisionLayer layer;
            public CountProcessor processor;

            public void Execute()
            {
                Physics.FindPairs(layer, processor).RunImmediate();
            }
        }

        private EntityQuery m_query;
        private bool        m_firstFrame = true;
        protected override void OnCreate()
        {
            m_query = Fluent.PatchQueryForBuildingCollisionLayer().Build();
        }

        public override bool ShouldUpdateSystem()
        {
            var currentScene = worldGlobalEntity.GetComponentData<CurrentScene>();
            return currentScene.isFirstFrame && currentScene.current.Equals("LayerQueryTest");
        }

        protected override void OnUpdate()
        {
            if (m_firstFrame)
            {
                var                 archetype = EntityManager.CreateArchetype(typeof(Translation), typeof(Rotation), typeof(Collider), typeof(LocalToWorld));
                NativeArray<Entity> entities  = new NativeArray<Entity>(50, Allocator.Persistent);
                EntityManager.CreateEntity(archetype, entities);
                new GenerateJob { random = new Random(1123) }.Run(this);
                entities.Dispose();
                m_firstFrame = false;
            }

            CountProcessor         processor = new CountProcessor { count = new NativeArray<int>(1, Allocator.TempJob) };
            CollisionLayerSettings settings                               = new CollisionLayerSettings
            {
                worldSubdivisionsPerAxis = new int3(1, 4, 4),
                worldAABB                = new Aabb(-1000f, 1000f),
            };
            var jh = Physics.BuildCollisionLayer(m_query, this).WithSettings(settings).ScheduleParallel(out CollisionLayer layer, Allocator.TempJob);
            jh     = new HardCountJob { layer = layer, processor = processor }.Schedule(jh);
            jh.Complete();
            //new ILayerQueryExtensions.LayerSelfQueryJobPart1<CountProcessor> { layer = layer, processor = processor }.Run(layer.BucketCount);
            //UnityEngine.Debug.Log("Count Part 1 = " + processor.count[0]);
            //new ILayerQueryExtensions.LayerSelfQueryJobPart2<CountProcessor> { layer = layer, processor = processor }.Run();
            //UnityEngine.Debug.Log("Count Part 2 = " + processor.count[0]);
            //PhysicsDebug.DrawLayer(layer);
            PhysicsDebug.DrawFindPairs(layer);
            layer.Dispose();
            processor.count.Dispose();
        }
    }
   }
 */

