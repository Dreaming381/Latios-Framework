﻿using Latios.Psyshock;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.PerformanceTesting;
using Unity.Transforms;

using Latios.Systems;
using Unity.Burst.Intrinsics;

namespace OptimizationAdventures
{
    public partial class BuildingCollisionLayerPerformanceTests
    {
        [BurstCompile]
        partial struct GenerateJob : IJobChunk
        {
            public Random random;
            public Aabb   aabb;

            public ComponentTypeHandle<Translation> tHandle;
            public ComponentTypeHandle<Rotation>    rHandle;
            public ComponentTypeHandle<Collider>    cHandle;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var t = chunk.GetNativeArray(tHandle);
                var r = chunk.GetNativeArray(rHandle);
                var c = chunk.GetNativeArray(cHandle);

                for (int i = 0; i < chunk.Count; i++)
                {
                    t[i] = new Translation { Value = random.NextFloat3(aabb.min, aabb.max) };
                    r[i]                           = new Rotation { Value = random.NextQuaternionRotation() };
                    c[i]                           = new CapsuleCollider(random.NextFloat3(-10f, 10f), random.NextFloat3(-10f, 10f), random.NextFloat(0f, 10f));
                }
            }
        }

        private struct DestroyOnCollision : IFindPairsProcessor
        {
            public EntityCommandBuffer.ParallelWriter ecb;

            public void Execute(in FindPairsResult result)
            {
                if (Physics.DistanceBetween(result.bodyA.collider, result.bodyA.transform, result.bodyB.collider, result.bodyB.transform, 0f, out ColliderDistanceResult _))
                {
                    ecb.DestroyEntity(result.jobIndex, result.bodyA.entity);
                    ecb.DestroyEntity(result.jobIndex, result.bodyB.entity);
                }
            }
        }

        public void BuildLayerPerformanceTests(int count, uint seed, CollisionLayerSettings settings)
        {
            Random random = new Random(seed);
            random.InitState(seed);

            World world  = new World("Test World");
            var   system = world.CreateSystemManaged<FixedSimulationSystemGroup>();

            var eq        = world.EntityManager.CreateEntityQuery(typeof(Translation), typeof(Rotation), typeof(Collider), typeof(LocalToWorld));
            var archetype = world.EntityManager.CreateArchetype(typeof(Translation), typeof(Rotation), typeof(Collider), typeof(LocalToWorld));
            world.EntityManager.CreateEntity(archetype, count, Allocator.Temp);
            new GenerateJob
            {
                random  = new Random(seed),
                aabb    = settings.worldAabb,
                tHandle = world.EntityManager.GetComponentTypeHandle<Translation>(false),
                rHandle = world.EntityManager.GetComponentTypeHandle<Rotation>(false),
                cHandle = world.EntityManager.GetComponentTypeHandle<Collider>(false)
            }.Run(eq);

            var typeGroup    = BuildCollisionLayerP4.BuildLayerChunkTypeGroup(system);
            var layer        = new TestCollisionLayer(count, settings, Allocator.TempJob);
            var layerIndices = new NativeArray<int>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var aabbs        = new NativeArray<Aabb>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var transforms   = new NativeArray<RigidTransform>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            SampleUnit unit = count > 9999 ? SampleUnit.Millisecond : SampleUnit.Microsecond;

            Measure.Method(() => {
                var baseIndicesArray = eq.CalculateBaseEntityIndexArray(Allocator.TempJob);

                new BuildCollisionLayerP4.Part1FromQueryJob
                {
                    layer                     = layer,
                    aabbs                     = aabbs,
                    typeGroup                 = typeGroup,
                    layerIndices              = layerIndices,
                    rigidTransforms           = transforms,
                    firstEntityInChunkIndices = baseIndicesArray
                }.Run(eq);
            })
            .SampleGroup("Part1")
            .WarmupCount(0)
            .MeasurementCount(1)
            .Run();

            Measure.Method(() => { new BuildCollisionLayerP4.Part2Job
                                   {
                                       layer        = layer,
                                       layerIndices = layerIndices
                                   }.Run(); })
            .SampleGroup("Part2")
            .WarmupCount(0)
            .MeasurementCount(1)
            .Run();

            Measure.Method(() => {
                var baseIndicesArray = eq.CalculateBaseEntityIndexArray(Allocator.TempJob);

                new BuildCollisionLayerP4.Part3FromQueryJob
                {
                    layer                     = layer,
                    aabbs                     = aabbs,
                    layerIndices              = layerIndices,
                    rigidTranforms            = transforms,
                    typeGroup                 = typeGroup,
                    firstEntityInChunkIndices = baseIndicesArray
                }.Run(eq);
            })
            .SampleGroup("Part3")
            .WarmupCount(0)
            .MeasurementCount(1)
            .Run();

            var layerBetter = new TestCollisionLayer(layer, Allocator.TempJob);
            var layerNew    = new TestCollisionLayer(layer, Allocator.TempJob);

            Measure.Method(() =>
            {
                new BuildCollisionLayerP4.Part4Job
                {
                    layer = layer
                }.Run(layer.BucketCount);
                //}.Schedule(layer.BucketCount, 1).Complete();
            })
            .SampleGroup("Part4Original")
            .WarmupCount(0)
            .MeasurementCount(1)
            .Run();

            Measure.Method(() => {
                new BuildCollisionLayerP4.Part4JobBetter
                {
                    layer = layerBetter
                }.Run(layerBetter.BucketCount);
                //}.Schedule(layer.BucketCount, 1).Complete();
            })
            .SampleGroup("Part4Better")
            .WarmupCount(0)
            .MeasurementCount(1)
            .Run();

            Measure.Method(() => {
                new BuildCollisionLayerP4.Part4JobNew
                {
                    layer = layerBetter
                }.Run(layerBetter.BucketCount);
                //}.Schedule(layer.BucketCount, 1).Complete();
            })
            .SampleGroup("Part4New")
            .WarmupCount(0)
            .MeasurementCount(1)
            .Run();

            layerNew.Dispose();
            layerBetter.Dispose();
            layer.Dispose();
            world.Dispose();
        }

        [Test, Performance]
        public void Build_10()
        {
            BuildLayerPerformanceTests(10, 1, new CollisionLayerSettings { worldAabb = new Aabb(-1f, 1f), worldSubdivisionsPerAxis = new int3(1, 1, 1) });
        }

        [Test, Performance]
        public void Build_20()
        {
            BuildLayerPerformanceTests(20, 12, new CollisionLayerSettings { worldAabb = new Aabb(-1f, 1f), worldSubdivisionsPerAxis = new int3(1, 1, 1) });
        }

        [Test, Performance]
        public void Build_50()
        {
            BuildLayerPerformanceTests(50, 16, new CollisionLayerSettings { worldAabb = new Aabb(-1f, 1f), worldSubdivisionsPerAxis = new int3(1, 1, 1) });
        }

        [Test, Performance]
        public void Build_100()
        {
            BuildLayerPerformanceTests(100, 28, new CollisionLayerSettings { worldAabb = new Aabb(-10f, 10f), worldSubdivisionsPerAxis = new int3(1, 2, 2) });
        }

        [Test, Performance]
        public void Build_200()
        {
            BuildLayerPerformanceTests(200, 283, new CollisionLayerSettings { worldAabb = new Aabb(-10f, 10f), worldSubdivisionsPerAxis = new int3(1, 2, 2) });
        }

        [Test, Performance]
        public void Build_500()
        {
            BuildLayerPerformanceTests(500, 286, new CollisionLayerSettings { worldAabb = new Aabb(-10f, 10f), worldSubdivisionsPerAxis = new int3(1, 2, 2) });
        }

        [Test, Performance]
        public void Build_1000()
        {
            BuildLayerPerformanceTests(1000, 654, new CollisionLayerSettings { worldAabb = new Aabb(-100f, 100f), worldSubdivisionsPerAxis = new int3(1, 2, 2) });
        }

        [Test, Performance]
        public void Build_2000()
        {
            BuildLayerPerformanceTests(2000, 6542, new CollisionLayerSettings { worldAabb = new Aabb(-100f, 100f), worldSubdivisionsPerAxis = new int3(1, 2, 2) });
        }

        [Test, Performance]
        public void Build_5000()
        {
            BuildLayerPerformanceTests(5000, 6574, new CollisionLayerSettings { worldAabb = new Aabb(-100f, 100f), worldSubdivisionsPerAxis = new int3(1, 2, 2) });
        }

        [Test, Performance]
        public void Build_10000()
        {
            BuildLayerPerformanceTests(10000, 8873, new CollisionLayerSettings { worldAabb = new Aabb(-1000f, 1000f), worldSubdivisionsPerAxis = new int3(1, 4, 4) });
        }

        [Test, Performance]
        public void Build_20000()
        {
            BuildLayerPerformanceTests(20000, 88735, new CollisionLayerSettings { worldAabb = new Aabb(-1000f, 1000f), worldSubdivisionsPerAxis = new int3(1, 4, 4) });
        }

        [Test, Performance]
        public void Build_50000()
        {
            BuildLayerPerformanceTests(50000, 88732, new CollisionLayerSettings { worldAabb = new Aabb(-1000f, 1000f), worldSubdivisionsPerAxis = new int3(1, 4, 4) });
        }

        [Test, Performance]
        public void Build_100000()
        {
            BuildLayerPerformanceTests(100000, 1123, new CollisionLayerSettings { worldAabb = new Aabb(-2000f, 2000f), worldSubdivisionsPerAxis = new int3(1, 4, 4) });
        }

        [Test, Performance]
        public void Build_200000()
        {
            BuildLayerPerformanceTests(200000, 1123, new CollisionLayerSettings { worldAabb = new Aabb(-2000f, 2000f), worldSubdivisionsPerAxis = new int3(1, 4, 4) });
        }

        [Test, Performance]
        public void Build_500000()
        {
            BuildLayerPerformanceTests(500000, 11237, new CollisionLayerSettings { worldAabb = new Aabb(-2000f, 2000f), worldSubdivisionsPerAxis = new int3(1, 4, 4) });
        }
    }
}

