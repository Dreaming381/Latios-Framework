using System.Diagnostics;
using Latios.PhysicsEngine;
using TMPro;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace Latios
{
    [AlwaysUpdateSystem]
    public class FindPairsSimpleBenchmark : SubSystem
    {
        private struct DestroyOnCollision : IFindPairsProcessor
        {
            public EntityCommandBuffer.ParallelWriter                     ecb;
            [NativeDisableParallelForRestriction] public NativeArray<int> collisionCount;

            public void Execute(FindPairsResult result)
            {
                if (Physics.DistanceBetween(result.bodyA.collider, result.bodyA.transform, result.bodyB.collider, result.bodyB.transform, 0f, out ColliderDistanceResult _))
                {
                    ecb.DestroyEntity(result.jobIndex, result.bodyA.entity);
                    ecb.DestroyEntity(result.jobIndex, result.bodyB.entity);
                    collisionCount[result.jobIndex * 16]++;
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
            var currentScene = worldGlobalEntity.GetComponentData<CurrentScene>();
            return currentScene.current.Equals("FindPairsSimpleBenchmark");
        }

        protected override void OnUpdate()
        {
            Entities.ForEach((TextMeshPro tmp) =>
            {
                //if (m_frameCount == 0)
                if (m_frameCount <= 1)  //Due to a missing line below, I was actually profiling 200000 entities instead of 100000. This reproduces that behavior.
                {
                    var archetype = EntityManager.CreateArchetype(typeof(Translation), typeof(Rotation), typeof(Collider), typeof(LocalToWorld));
                    //EntityManager.CreateEntity(archetype, 10000, Allocator.Temp);
                    EntityManager.CreateEntity(archetype, 100000, Allocator.Temp);
                    var random = new Random(1123);
                    Entities.ForEach((ref Translation trans, ref Rotation rot, ref Collider col) =>
                    {
                        //trans.Value = random.NextFloat3(new float3(-1000f), new float3(1000f));
                        trans.Value = random.NextFloat3(new float3(-2000f), new float3(2000f));
                        rot.Value   = random.NextQuaternionRotation();
                        col         = new CapsuleCollider(random.NextFloat3(-10f, 10f), random.NextFloat3(-10f, 10f), random.NextFloat(0f, 10f));
                    }).Run();
                }

                var ecbSystem       = World.GetExistingSystem<BeginInitializationEntityCommandBufferSystem>();
                var ecbs            = ecbSystem.CreateCommandBuffer();
                ecbs.ShouldPlayback = false;
                var ecb             = ecbs.AsParallelWriter();

                var                    processor                   = new DestroyOnCollision { ecb = ecb };
                CollisionLayerSettings settings                                                   = new CollisionLayerSettings
                {
                    worldSubdivisionsPerAxis = new int3(1, 4, 4),
                    //worldAABB                = new AABB(-1000f, 1000f),
                    worldAABB = new Aabb(-2000f, 2000f),
                };

                tmp.text            = "FindPairsSimpleBenchmark\n\n\n";
                Stopwatch stopwatch = new Stopwatch();
                for (int i = 0; i < 3; i++)
                {
                    UnityEngine.Profiling.Profiler.BeginSample("BuildLayer");
                    stopwatch.Start();
                    //Physics.BuildCollisionLayer(m_query, this).WithSettings(settings).Run(out CollisionLayer layer, Allocator.TempJob);
                    Physics.BuildCollisionLayer(m_query, this).WithSettings(settings).ScheduleParallel(out CollisionLayer layer, Allocator.TempJob).Complete();
                    stopwatch.Stop();
                    UnityEngine.Profiling.Profiler.EndSample();
                    tmp.text += "Build Layer:\t" + stopwatch.Elapsed.Ticks + "\n\n";
                    stopwatch.Reset();

                    var counts               = new NativeArray<int>(layer.BucketCount * 32, Allocator.TempJob);
                    processor.collisionCount = counts;

                    UnityEngine.Profiling.Profiler.BeginSample("FindPairs");
                    stopwatch.Start();
                    //Physics.FindPairs(layer, processor).Run();
                    Physics.FindPairs(layer, processor).ScheduleParallel().Complete();
                    stopwatch.Stop();
                    UnityEngine.Profiling.Profiler.EndSample();
                    int sum = 0;
                    foreach (int c in counts)
                    {
                        sum += c;
                    }
                    tmp.text += "Find Pairs:\t" + stopwatch.Elapsed.Ticks + "\nCount:\t" + sum + "\n\n\n";
                    counts.Dispose();
                    stopwatch.Reset();
                    //PhysicsDebug.DrawFindPairs(layer);

                    layer.Dispose();
                }
            }).WithStructuralChanges().Run();
            m_frameCount++;  //This line was missing which caused me to measure 200,000 colliders instead of 100,000
        }
    }
}

