using Latios.Psyshock;
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
    public partial class BuildCollisionLayerOldVsNewPerformanceTests
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
            var e         = world.EntityManager.CreateEntity(archetype, count, Allocator.Persistent);
            e.Dispose();
            new GenerateJob
            {
                random  = new Random(seed),
                aabb    = settings.worldAabb,
                tHandle = world.EntityManager.GetComponentTypeHandle<Translation>(false),
                rHandle = world.EntityManager.GetComponentTypeHandle<Rotation>(false),
                cHandle = world.EntityManager.GetComponentTypeHandle<Collider>(false)
            }.Run(eq);

            {
                var typeGroup    = BuildCollisionLayerP4.BuildLayerChunkTypeGroup(system);
                var layer        = new TestCollisionLayer(count, settings, Allocator.Persistent);
                var layerIndices = new NativeArray<int>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                var aabbs        = new NativeArray<Aabb>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                var transforms   = new NativeArray<RigidTransform>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

                SampleUnit unit = count > 9999 ? SampleUnit.Millisecond : SampleUnit.Microsecond;

                Measure.Method(() =>
                {
                    var baseIndicesArray  = eq.CalculateBaseEntityIndexArray(Allocator.TempJob);
                    var baseIndicesArray2 = new NativeArray<int>(baseIndicesArray, Allocator.TempJob);

                    new BuildCollisionLayerP4.Part1FromQueryJob
                    {
                        layer                     = layer,
                        aabbs                     = aabbs,
                        typeGroup                 = typeGroup,
                        layerIndices              = layerIndices,
                        rigidTransforms           = transforms,
                        firstEntityInChunkIndices = baseIndicesArray
                    }.Run(eq);

                    new BuildCollisionLayerP4.Part2Job
                    {
                        layer        = layer,
                        layerIndices = layerIndices
                    }.Run();

                    new BuildCollisionLayerP4.Part3FromQueryJob
                    {
                        layer                     = layer,
                        aabbs                     = aabbs,
                        layerIndices              = layerIndices,
                        rigidTranforms            = transforms,
                        typeGroup                 = typeGroup,
                        firstEntityInChunkIndices = baseIndicesArray2
                    }.Run(eq);

                    new BuildCollisionLayerP4.Part4Job
                    {
                        layer = layer
                    }.Run(layer.BucketCount);
                })
                .SampleGroup("OldVersion")
                .WarmupCount(0)
                .MeasurementCount(1)
                .Run();

                layer.Dispose();
            }

            {
                var typeGroup    = BuildCollisionLayerP4.BuildLayerChunkTypeGroup(system);
                var layer        = new TestCollisionLayer(count, settings, Allocator.Persistent);
                var layerIndices = new NativeArray<int>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                var aabbs        = new NativeArray<Aabb>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                var transforms   = new NativeArray<RigidTransform>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

                SampleUnit unit = count > 9999 ? SampleUnit.Millisecond : SampleUnit.Microsecond;

                Measure.Method(() =>
                {
                    var baseIndicesArray  = eq.CalculateBaseEntityIndexArray(Allocator.TempJob);
                    var baseIndicesArray2 = new NativeArray<int>(baseIndicesArray, Allocator.TempJob);

                    new BuildCollisionLayerP4.Part1FromQueryJob
                    {
                        layer                     = layer,
                        aabbs                     = aabbs,
                        typeGroup                 = typeGroup,
                        layerIndices              = layerIndices,
                        rigidTransforms           = transforms,
                        firstEntityInChunkIndices = baseIndicesArray
                    }.Run(eq);

                    new BuildCollisionLayerP4.Part2Job
                    {
                        layer        = layer,
                        layerIndices = layerIndices
                    }.Run();

                    new BuildCollisionLayerP4.Part3FromQueryJob
                    {
                        layer                     = layer,
                        aabbs                     = aabbs,
                        layerIndices              = layerIndices,
                        rigidTranforms            = transforms,
                        typeGroup                 = typeGroup,
                        firstEntityInChunkIndices = baseIndicesArray2
                    }.Run(eq);

                    new BuildCollisionLayerP4.Part4JobBetter
                    {
                        layer = layer
                    }.Run(layer.BucketCount);
                })
                .SampleGroup("OldVersionBetter")
                .WarmupCount(0)
                .MeasurementCount(1)
                .Run();

                layer.Dispose();
            }

            //NewVersion
            {
                var typeGroup     = BuildCollisionLayerOldVsNew.BuildLayerChunkTypeGroup(system);
                var layer         = new TestCollisionLayer(count, settings, Allocator.Persistent);
                var layerIndices  = new NativeArray<int>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                var remapSrcArray = new NativeArray<int>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                var xmins         = new NativeArray<float>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                var aos           = new NativeArray<BuildCollisionLayerOldVsNew.ColliderAoSData>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

                SampleUnit unit = count > 9999 ? SampleUnit.Millisecond : SampleUnit.Microsecond;

                Measure.Method(() =>
                {
                    var baseIndicesArray = eq.CalculateBaseEntityIndexArray(Allocator.TempJob);

                    new BuildCollisionLayerOldVsNew.Part1FromQueryJob
                    {
                        layer                     = layer,
                        typeGroup                 = typeGroup,
                        layerIndices              = layerIndices,
                        xmins                     = xmins,
                        colliderAoS               = aos,
                        firstEntityInChunkIndices = baseIndicesArray
                    }.Run(eq);

                    new BuildCollisionLayerOldVsNew.Part2Job
                    {
                        layer        = layer,
                        layerIndices = layerIndices
                    }.Run();

                    new BuildCollisionLayerOldVsNew.Part3Job
                    {
                        layerIndices       = layerIndices,
                        unsortedSrcIndices = remapSrcArray
                    }.Run(count);

                    new BuildCollisionLayerOldVsNew.Part4Job
                    {
                        unsortedSrcIndices   = remapSrcArray,
                        xmins                = xmins,
                        bucketStartAndCounts = layer.bucketStartsAndCounts
                    }.Run(layer.BucketCount);

                    new BuildCollisionLayerOldVsNew.Part5Job
                    {
                        layer           = layer,
                        colliderAoS     = aos,
                        remapSrcIndices = remapSrcArray
                    }.Run(count);
                })
                .SampleGroup("NewVersion")
                .WarmupCount(0)
                .MeasurementCount(1)
                .Run();

                remapSrcArray.Dispose();
                layer.Dispose();
            }

            {
                var typeGroup     = BuildCollisionLayerOldVsNew.BuildLayerChunkTypeGroup(system);
                var layer         = new TestCollisionLayer(count, settings, Allocator.Persistent);
                var layerIndices  = new NativeArray<int>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                var remapSrcArray = new NativeArray<int>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                var xmins         = new NativeArray<float>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                var aos           = new NativeArray<BuildCollisionLayerOldVsNew.ColliderAoSData>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

                SampleUnit unit = count > 9999 ? SampleUnit.Millisecond : SampleUnit.Microsecond;

                Measure.Method(() =>
                {
                    var baseIndicesArray = eq.CalculateBaseEntityIndexArray(Allocator.TempJob);

                    new BuildCollisionLayerOldVsNew.Part1FromQueryJob
                    {
                        layer                     = layer,
                        typeGroup                 = typeGroup,
                        layerIndices              = layerIndices,
                        xmins                     = xmins,
                        colliderAoS               = aos,
                        firstEntityInChunkIndices = baseIndicesArray
                    }.Run(eq);

                    new BuildCollisionLayerOldVsNew.Part2Job
                    {
                        layer        = layer,
                        layerIndices = layerIndices
                    }.Run();

                    new BuildCollisionLayerOldVsNew.Part3Job
                    {
                        layerIndices       = layerIndices,
                        unsortedSrcIndices = remapSrcArray
                    }.Run(count);

                    new BuildCollisionLayerOldVsNew.Part4JobUnity
                    {
                        unsortedSrcIndices   = remapSrcArray,
                        xmins                = xmins,
                        bucketStartAndCounts = layer.bucketStartsAndCounts
                    }.Run(layer.BucketCount);

                    new BuildCollisionLayerOldVsNew.Part5Job
                    {
                        layer           = layer,
                        colliderAoS     = aos,
                        remapSrcIndices = remapSrcArray
                    }.Run(count);
                })
                .SampleGroup("NewVersionUnity")
                .WarmupCount(0)
                .MeasurementCount(1)
                .Run();

                remapSrcArray.Dispose();
                layer.Dispose();
            }

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
            BuildLayerPerformanceTests(200000, 11234, new CollisionLayerSettings { worldAabb = new Aabb(-2000f, 2000f), worldSubdivisionsPerAxis = new int3(1, 4, 4) });
        }

        [Test, Performance]
        public void Build_500000()
        {
            BuildLayerPerformanceTests(500000, 11237, new CollisionLayerSettings { worldAabb = new Aabb(-2000f, 2000f), worldSubdivisionsPerAxis = new int3(1, 4, 4) });
        }
    }
}

