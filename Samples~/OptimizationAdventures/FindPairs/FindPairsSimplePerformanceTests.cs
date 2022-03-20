using Latios.Psyshock;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.PerformanceTesting;

namespace OptimizationAdventures
{
    public class FindPairsSimplePerformanceTests
    {
        //This version biases towards smaller boxes.
        [BurstCompile]
        public struct GenerateRandomAabbs : IJob
        {
            public Random                            random;
            public NativeArray<AabbEntity>           aabbs;
            public NativeArray<AabbEntityRearranged> rearrangedAabbs;

            public void Execute()
            {
                float3 neg = new float3(-100000f, -100000f, -100000f);
                float3 pos = new float3(100000f, 100000f, 100000f);

                for (int i = 0; i < aabbs.Length; i++)
                {
                    float3 min  = random.NextFloat3(neg, pos);
                    float3 max  = random.NextFloat3(min, pos);
                    float3 diff = max - min;
                    float3 mult = random.NextFloat3();
                    mult        = mult * mult;
                    max         = min + diff * mult;

                    AabbEntity aabbEntity = new AabbEntity
                    {
                        entity = new Entity
                        {
                            Index   = i,
                            Version = 0
                        },
                        aabb = new Aabb
                        {
                            min = min,
                            max = max
                        }
                    };
                    aabbs[i] = aabbEntity;

                    var aabbEntityRearranged = new AabbEntityRearranged
                    {
                        minXmaxX   = new float2(min.x, max.x),
                        minYZmaxYZ = new float4(min.yz, max.yz),
                        entity     = aabbEntity.entity
                    };
                    rearrangedAabbs[i] = aabbEntityRearranged;
                }
            }
        }

        [BurstCompile]
        public struct ConvertToSoa : IJobFor
        {
            [ReadOnly] public NativeArray<AabbEntityRearranged> aabbs;
            public NativeArray<float>                           xmins;
            public NativeArray<float>                           xmaxs;
            public NativeArray<float4>                          minYZmaxYZs;
            public NativeArray<Entity>                          entities;

            public void Execute(int i)
            {
                xmins[i]       = aabbs[i].minXmaxX.x;
                xmaxs[i]       = aabbs[i].minXmaxX.y;
                minYZmaxYZs[i] = aabbs[i].minYZmaxYZ;
                entities[i]    = aabbs[i].entity;
            }
        }

        public void SweepPerformanceTests(int count, uint seed, int preallocate = 1)
        {
            Random random = new Random(seed);
            random.InitState(seed);
            NativeArray<AabbEntity>           randomAabbs           = new NativeArray<AabbEntity>(count, Allocator.TempJob);
            NativeArray<AabbEntityRearranged> randomAabbsRearranged = new NativeArray<AabbEntityRearranged>(count, Allocator.TempJob);
            NativeArray<float>                xmins                 = new NativeArray<float>(count, Allocator.TempJob);
            NativeArray<float>                xmaxs                 = new NativeArray<float>(count, Allocator.TempJob);
            NativeArray<float4>               minYZmaxYZs           = new NativeArray<float4>(count, Allocator.TempJob);
            NativeArray<Entity>               entities              = new NativeArray<Entity>(count, Allocator.TempJob);
            var                               jh                    =
                new GenerateRandomAabbs { random                    = random, aabbs = randomAabbs, rearrangedAabbs = randomAabbsRearranged }.Schedule();
            jh                                                      = randomAabbs.Sort(jh);
            jh                                                      = randomAabbsRearranged.Sort(jh);
            jh                                                      = new ConvertToSoa {
                aabbs                                               = randomAabbsRearranged, xmins = xmins, xmaxs = xmaxs, minYZmaxYZs = minYZmaxYZs, entities = entities
            }.Schedule(xmins.Length, jh);
            jh.Complete();

            NativeList<EntityPair> pairsNaive      = new NativeList<EntityPair>(preallocate, Allocator.TempJob);
            NativeList<EntityPair> pairsBool4      = new NativeList<EntityPair>(preallocate, Allocator.TempJob);
            NativeList<EntityPair> pairsLessNaive  = new NativeList<EntityPair>(preallocate, Allocator.TempJob);
            NativeList<EntityPair> pairsFunny      = new NativeList<EntityPair>(preallocate, Allocator.TempJob);
            NativeList<EntityPair> pairsBetter     = new NativeList<EntityPair>(preallocate, Allocator.TempJob);
            NativeList<EntityPair> pairsSimd       = new NativeList<EntityPair>(preallocate, Allocator.TempJob);
            NativeList<EntityPair> pairsRearrange  = new NativeList<EntityPair>(preallocate, Allocator.TempJob);
            NativeList<EntityPair> pairsSoa        = new NativeList<EntityPair>(preallocate, Allocator.TempJob);
            NativeList<EntityPair> pairsSoaShuffle = new NativeList<EntityPair>(preallocate, Allocator.TempJob);

            SampleUnit unit = count > 999 ? SampleUnit.Millisecond : SampleUnit.Microsecond;

            Measure.Method(() => { new NaiveSweep { aabbs = randomAabbs, overlaps = pairsNaive }.Run(); })
            .SampleGroup("NaiveSweep")
            .WarmupCount(0)
            .MeasurementCount(1)
            .Run();

            Measure.Method(() => { new Bool4Sweep { aabbs = randomAabbs, overlaps = pairsBool4 }.Run(); })
            .SampleGroup("Bool4Sweep")
            .WarmupCount(0)
            .MeasurementCount(1)
            .Run();

            Measure.Method(() => { new LessNaiveSweep { aabbs = randomAabbs, overlaps = pairsLessNaive }.Run(); })
            .SampleGroup("LessNaiveSweep")
            .WarmupCount(0)
            .MeasurementCount(1)
            .Run();

            Measure.Method(() => { new FunnySweep { aabbs = randomAabbs, overlaps = pairsFunny }.Run(); })
            .SampleGroup("FunnySweep")
            .WarmupCount(0)
            .MeasurementCount(1)
            .Run();

            Measure.Method(() => { new BetterSweep { aabbs = randomAabbs, overlaps = pairsBetter }.Run(); })
            .SampleGroup("BetterSweep")
            .WarmupCount(0)
            .MeasurementCount(1)
            .Run();

            Measure.Method(() => { new SimdSweep { aabbs = randomAabbs, overlaps = pairsSimd }.Run(); })
            .SampleGroup("SimdSweep")
            .WarmupCount(0)
            .MeasurementCount(1)
            .Run();

            Measure.Method(() => { new RearrangedSweep { aabbs = randomAabbsRearranged, overlaps = pairsRearrange }.Run(); })
            .SampleGroup("RearrangedSweep")
            .WarmupCount(0)
            .MeasurementCount(1)
            .Run();

            Measure.Method(() => { new SoaSweep { xmins = xmins, xmaxs = xmaxs, minYZmaxYZs = minYZmaxYZs, entities = entities, overlaps = pairsSoa }.Run(); })
            .SampleGroup("SoaSweep")
            .WarmupCount(0)
            .MeasurementCount(1)
            .Run();

            Measure.Method(() => { new SoaShuffleSweep { xmins = xmins, xmaxs = xmaxs, minYZmaxYZs = minYZmaxYZs, entities = entities, overlaps = pairsSoaShuffle }.Run(); })
            .SampleGroup("SoaShuffleSweep")
            .WarmupCount(0)
            .MeasurementCount(1)
            .Run();

            UnityEngine.Debug.Log("Pairs: " + pairsNaive.Length);
            UnityEngine.Debug.Log("Pairs: " + pairsBetter.Length);
            UnityEngine.Debug.Log("Pairs: " + pairsSimd.Length);

            randomAabbs.Dispose();
            randomAabbsRearranged.Dispose();
            xmins.Dispose();
            xmaxs.Dispose();
            minYZmaxYZs.Dispose();
            entities.Dispose();
            pairsNaive.Dispose();
            pairsBool4.Dispose();
            pairsLessNaive.Dispose();
            pairsFunny.Dispose();
            pairsBetter.Dispose();
            pairsSimd.Dispose();
            pairsRearrange.Dispose();
            pairsSoa.Dispose();
            pairsSoaShuffle.Dispose();
        }

        [Test, Performance]
        public void Sweep_10()
        {
            SweepPerformanceTests(10, 1);
        }

        [Test, Performance]
        public void Sweep_100()
        {
            SweepPerformanceTests(100, 56);
        }

        [Test, Performance]
        public void Sweep_1000()
        {
            SweepPerformanceTests(1000, 76389);
        }

        [Test, Performance]
        public void Sweep_10000()
        {
            SweepPerformanceTests(10000, 2348976);
        }

        [Test, Performance]
        public void Sweep_20()
        {
            SweepPerformanceTests(20, 1);
        }

        [Test, Performance]
        public void Sweep_200()
        {
            SweepPerformanceTests(200, 76);
        }

        [Test, Performance]
        public void Sweep_2000()
        {
            SweepPerformanceTests(2000, 56324);
        }

        [Test, Performance]
        public void Sweep_20000()
        {
            SweepPerformanceTests(20000, 2980457);
        }

        [Test, Performance]
        public void Sweep_50()
        {
            SweepPerformanceTests(50, 1);
        }

        [Test, Performance]
        public void Sweep_500()
        {
            SweepPerformanceTests(500, 23);
        }

        [Test, Performance]
        public void Sweep_5000()
        {
            SweepPerformanceTests(5000, 47893);
        }

        [Test, Performance]
        public void Sweep_50000()
        {
            SweepPerformanceTests(50000, 237648);
        }

        //[Test, Performance]
        //public void Sweep_100000()
        //{
        //    SweepPerformanceTests(100000, 234896);
        //}
    }
}

