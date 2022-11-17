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
            public NativeArray<AabbEntityMorton>     mortonAabbs;

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

                    var aabbEntityMorton = new AabbEntityMorton
                    {
                        aabb   = new Aabb(min, max),
                        entity = aabbEntity.entity,
                        min    = ConvertToMorton(min),
                        max    = ConvertToMorton(max)
                    };
                    mortonAabbs[i] = aabbEntityMorton;

                    if (aabbEntityMorton.min.z > aabbEntityMorton.max.z)
                    {
                        uint3 minUint;
                        {
                            uint3 key  = math.asuint(aabbEntityMorton.aabb.min);
                            uint3 mask = math.select(0x80000000, 0xffffffff, (key & 0x80000000) > 0);
                            key        = mask ^ key;
                            minUint    = key;
                        }
                        uint3 maxUint;
                        {
                            uint3 key  = math.asuint(aabbEntityMorton.aabb.max);
                            uint3 mask = math.select(0x80000000, 0xffffffff, (key & 0x80000000) > 0);
                            key        = mask ^ key;
                            maxUint    = key;
                        }

                        UnityEngine.Debug.Log($"min as key: {minUint}, max as key: {maxUint}");
                    }
                }
            }

            uint3 ConvertToMorton(float3 floatValue)
            {
                uint3 key       = math.asuint(floatValue);
                uint3 mask      = math.select(0x80000000, 0xffffffff, (key & 0x80000000) > 0);
                key             = mask ^ key;
                var   keyLower  = key & 0xffff;
                ulong resLower  = PartBits(keyLower.x) | (PartBits(keyLower.y) << 1) | (PartBits(keyLower.z) << 2);
                var   keyHigher = (key >> 16) & 0xffff;
                ulong resHigher = PartBits(keyHigher.x) | (PartBits(keyHigher.y) << 1) | (PartBits(keyHigher.z) << 2);
                uint3 result;
                result.x = (uint)(resLower & 0xffffffff);
                result.y = (uint)(((resLower >> 32) & 0xffff) | ((resHigher << 16) & 0xffff0000));
                result.z = (uint)((resHigher >> 16) & 0xffffffff);
                return result;
            }

            ulong PartBits(uint src)
            {
                ulong result = src;
                result       = (result ^ (result << 32)) & 0x1f00000000ffff;
                result       = (result ^ (result << 16)) & 0x1f0000ff0000ff;
                result       = (result ^ (result << 8)) & 0x100f00f00f00f00f;
                result       = (result ^ (result << 4)) & 0x10c30c30c30c30c3;
                result       = (result ^ (result << 2)) & 0x1249249249249249;
                return result;
            }
        }

        [BurstCompile]
        public struct ConvertToSoa : IJobFor
        {
            [ReadOnly] public NativeArray<AabbEntityRearranged> aabbs;
            public NativeArray<float>                           xmins;
            public NativeArray<float>                           xmaxs;
            public NativeArray<float4>                          minYZmaxYZs;
            public NativeArray<float4>                          minYZmaxYZsFlipped;
            public NativeArray<Entity>                          entities;

            public void Execute(int i)
            {
                xmins[i]              = aabbs[i].minXmaxX.x;
                xmaxs[i]              = aabbs[i].minXmaxX.y;
                minYZmaxYZs[i]        = aabbs[i].minYZmaxYZ;
                minYZmaxYZsFlipped[i] = minYZmaxYZs[i] * new float4(1f, 1f, -1f, -1f);
                entities[i]           = aabbs[i].entity;
            }
        }

        [BurstCompile]
        public struct BuildBatches : IJob
        {
            [ReadOnly] public NativeArray<float4> minYZmaxYZs;
            public NativeArray<float4>            batchMinYZmaxYZs;

            public void Execute()
            {
                for (int i = 0; i < minYZmaxYZs.Length; i += 4)
                {
                    var batch = minYZmaxYZs[i];
                    for (int j = 1; j < 4 && i + j < minYZmaxYZs.Length; j++)
                    {
                        batch.xy = math.min(batch.xy, minYZmaxYZs[i + j].xy);
                        batch.zw = math.max(batch.zw, minYZmaxYZs[i + j].zw);
                    }
                    batchMinYZmaxYZs[i >> 2] = batch;
                }
            }
        }

        [BurstCompile]
        public struct BuildDualAxes : IJob
        {
            [ReadOnly] public NativeArray<AabbEntityRearranged> aabbs;
            public NativeArray<uint>                            xs;
            public NativeArray<uint>                            zs;

            public void Execute()
            {
                var xSort = new NativeArray<Sortable>(aabbs.Length * 2, Allocator.Temp);
                var zSort = new NativeArray<Sortable>(aabbs.Length * 2, Allocator.Temp);

                for (int i = 0; i < aabbs.Length; i++)
                {
                    var aabb         = aabbs[i];
                    xSort[2 * i]     = new Sortable { f = aabb.minXmaxX.x, index = (uint)i };
                    xSort[2 * i + 1] = new Sortable { f = aabb.minXmaxX.y, index = (uint)i + (uint)aabbs.Length };

                    zSort[2 * i]     = new Sortable { f = aabb.minYZmaxYZ.y, index = (uint)i };
                    zSort[2 * i + 1]                                               = new Sortable { f = aabb.minYZmaxYZ.w, index = (uint)i + (uint)aabbs.Length };
                }

                xSort.Sort();
                zSort.Sort();

                for (int i = 0; i < xSort.Length; i++)
                {
                    xs[i] = xSort[i].index;
                    zs[i] = zSort[i].index;
                }
            }

            struct Sortable : System.IComparable<Sortable>
            {
                public float f;
                public uint  index;

                public int CompareTo(Sortable other)
                {
                    var result = f.CompareTo(other.f);
                    if (result == 0)
                        return index.CompareTo(other.index);
                    return result;
                }
            }
        }

        public void SweepPerformanceTests(int count, uint seed, int preallocate = 1)
        {
            Random random = new Random(seed);
            random.InitState(seed);
            NativeArray<AabbEntity>           randomAabbs            = new NativeArray<AabbEntity>(count, Allocator.TempJob);
            NativeArray<AabbEntityRearranged> randomAabbsRearranged  = new NativeArray<AabbEntityRearranged>(count, Allocator.TempJob);
            NativeArray<AabbEntityMorton>     randomAabbsMorton      = new NativeArray<AabbEntityMorton>(count, Allocator.TempJob);
            NativeArray<float>                xminsFull              = new NativeArray<float>(count + 1, Allocator.TempJob);
            NativeArray<float>                xmaxsFull              = new NativeArray<float>(count + 1, Allocator.TempJob);
            NativeArray<float4>               minYZmaxYZsFull        = new NativeArray<float4>(count + 1, Allocator.TempJob);
            NativeArray<float4>               minYZmaxYZsFlippedFull = new NativeArray<float4>(count + 1, Allocator.TempJob);
            NativeArray<float4>               batchMinYZmaxYZ        = new NativeArray<float4>(count / 4 + 1, Allocator.TempJob);
            NativeArray<uint>                 zToXMinsMaxes          = new NativeArray<uint>(count * 2, Allocator.TempJob);
            NativeArray<uint>                 xs                     = new NativeArray<uint>(count * 2, Allocator.TempJob);
            NativeArray<Entity>               entities               = new NativeArray<Entity>(count, Allocator.TempJob);

            var jh = new GenerateRandomAabbs
            {
                random          = random,
                aabbs           = randomAabbs,
                rearrangedAabbs = randomAabbsRearranged,
                mortonAabbs     = randomAabbsMorton
            }.Schedule();

            jh                     = randomAabbs.SortJob().Schedule(jh);
            jh                     = randomAabbsRearranged.SortJob().Schedule(jh);
            jh                     = randomAabbsMorton.SortJob().Schedule(jh);
            var xmins              = xminsFull.GetSubArray(0, count);
            var xmaxs              = xmaxsFull.GetSubArray(0, count);
            var minYZmaxYZs        = minYZmaxYZsFull.GetSubArray(0, count);
            var minYZmaxYZsFlipped = minYZmaxYZsFlippedFull.GetSubArray(0, count);
            jh                     = new ConvertToSoa
            {
                aabbs              = randomAabbsRearranged,
                xmins              = xmins,
                xmaxs              = xmaxs,
                minYZmaxYZs        = minYZmaxYZs,
                minYZmaxYZsFlipped = minYZmaxYZsFlipped,
                entities           = entities
            }.Schedule(xmins.Length, jh);
            jh.Complete();
            new BuildBatches { minYZmaxYZs = minYZmaxYZs, batchMinYZmaxYZs = batchMinYZmaxYZ }.Run();
            new BuildDualAxes { aabbs                                      = randomAabbsRearranged, zs = zToXMinsMaxes, xs = xs }.Run();

            float extremeNegative               = float.MinValue;
            float extremePositive               = float.MaxValue;
            xmins[xmins.Length - 1]             = extremePositive;
            xmaxs[xmaxs.Length - 1]             = extremeNegative;
            minYZmaxYZs[minYZmaxYZs.Length - 1] = new float4(extremePositive, extremePositive, extremeNegative, extremeNegative);
            minYZmaxYZs[minYZmaxYZs.Length - 1] = new float4(extremePositive);

            NativeList<EntityPair> pairsNaive          = new NativeList<EntityPair>(preallocate, Allocator.TempJob);
            NativeList<EntityPair> pairsBool4          = new NativeList<EntityPair>(preallocate, Allocator.TempJob);
            NativeList<EntityPair> pairsLessNaive      = new NativeList<EntityPair>(preallocate, Allocator.TempJob);
            NativeList<EntityPair> pairsFunny          = new NativeList<EntityPair>(preallocate, Allocator.TempJob);
            NativeList<EntityPair> pairsBetter         = new NativeList<EntityPair>(preallocate, Allocator.TempJob);
            NativeList<EntityPair> pairsSimd           = new NativeList<EntityPair>(preallocate, Allocator.TempJob);
            NativeList<EntityPair> pairsRearrange      = new NativeList<EntityPair>(preallocate, Allocator.TempJob);
            NativeList<EntityPair> pairsSoa            = new NativeList<EntityPair>(preallocate, Allocator.TempJob);
            NativeList<EntityPair> pairsSoaShuffle     = new NativeList<EntityPair>(preallocate, Allocator.TempJob);
            NativeList<EntityPair> pairsOptimalOrder   = new NativeList<EntityPair>(preallocate, Allocator.TempJob);
            NativeList<EntityPair> pairsFlipped        = new NativeList<EntityPair>(preallocate, Allocator.TempJob);
            NativeList<EntityPair> pairsSentinel       = new NativeList<EntityPair>(preallocate, Allocator.TempJob);
            NativeList<EntityPair> pairsMortonNaive    = new NativeList<EntityPair>(preallocate, Allocator.TempJob);
            NativeList<EntityPair> pairsBatch          = new NativeList<EntityPair>(preallocate, Allocator.TempJob);
            NativeList<EntityPair> pairsDual           = new NativeList<EntityPair>(preallocate, Allocator.TempJob);
            NativeList<EntityPair> pairsDualCondensed  = new NativeList<EntityPair>(preallocate, Allocator.TempJob);
            NativeList<EntityPair> pairsDualOptimized  = new NativeList<EntityPair>(preallocate, Allocator.TempJob);
            NativeList<EntityPair> pairsUnrolledPoor   = new NativeList<EntityPair>(preallocate, Allocator.TempJob);
            NativeList<EntityPair> pairsUnrolled       = new NativeList<EntityPair>(preallocate, Allocator.TempJob);
            NativeList<EntityPair> pairsUnrolled2      = new NativeList<EntityPair>(preallocate, Allocator.TempJob);
            NativeList<EntityPair> pairsUnrolled3      = new NativeList<EntityPair>(preallocate, Allocator.TempJob);
            NativeList<EntityPair> pairsUnrolled4      = new NativeList<EntityPair>(preallocate, Allocator.TempJob);
            NativeList<EntityPair> pairsDualBranchless = new NativeList<EntityPair>(preallocate, Allocator.TempJob);

            SampleUnit unit = count > 2000 ? SampleUnit.Millisecond : SampleUnit.Microsecond;

            Measure.Method(() => { new NaiveSweep { aabbs = randomAabbs, overlaps = pairsNaive }.Run(); })
            .SampleGroup(new SampleGroup("NaiveSweep", unit))
            .WarmupCount(0)
            .MeasurementCount(1)
            .Run();

            Measure.Method(() => { new Bool4Sweep { aabbs = randomAabbs, overlaps = pairsBool4 }.Run(); })
            .SampleGroup(new SampleGroup("Bool4Sweep", unit))
            .WarmupCount(0)
            .MeasurementCount(1)
            .Run();

            Measure.Method(() => { new LessNaiveSweep { aabbs = randomAabbs, overlaps = pairsLessNaive }.Run(); })
            .SampleGroup(new SampleGroup("LessNaiveSweep", unit))
            .WarmupCount(0)
            .MeasurementCount(1)
            .Run();

            Measure.Method(() => { new FunnySweep { aabbs = randomAabbs, overlaps = pairsFunny }.Run(); })
            .SampleGroup(new SampleGroup("FunnySweep", unit))
            .WarmupCount(0)
            .MeasurementCount(1)
            .Run();

            Measure.Method(() => { new BetterSweep { aabbs = randomAabbs, overlaps = pairsBetter }.Run(); })
            .SampleGroup(new SampleGroup("BetterSweep", unit))
            .WarmupCount(0)
            .MeasurementCount(1)
            .Run();

            Measure.Method(() => { new SimdSweep { aabbs = randomAabbs, overlaps = pairsSimd }.Run(); })
            .SampleGroup(new SampleGroup("SimdSweep", unit))
            .WarmupCount(0)
            .MeasurementCount(1)
            .Run();

            Measure.Method(() => { new RearrangedSweep { aabbs = randomAabbsRearranged, overlaps = pairsRearrange }.Run(); })
            .SampleGroup(new SampleGroup("RearrangedSweep", unit))
            .WarmupCount(0)
            .MeasurementCount(1)
            .Run();

            Measure.Method(() => { new SoaSweep { xmins = xmins, xmaxs = xmaxs, minYZmaxYZs = minYZmaxYZs, entities = entities, overlaps = pairsSoa }.Run(); })
            .SampleGroup(new SampleGroup("SoaSweep", unit))
            .WarmupCount(0)
            .MeasurementCount(1)
            .Run();

            Measure.Method(() => { new SoaShuffleSweep { xmins = xmins, xmaxs = xmaxs, minYZmaxYZs = minYZmaxYZs, entities = entities, overlaps = pairsSoaShuffle }.Run(); })
            .SampleGroup(new SampleGroup("SoaShuffleSweep", unit))
            .WarmupCount(0)
            .MeasurementCount(1)
            .Run();

            Measure.Method(() => {
                pairsOptimalOrder.Clear();
                new OptimalOrderSweep { xmins = xmins, xmaxs = xmaxs, minYZmaxYZs = minYZmaxYZs, entities = entities, overlaps = pairsOptimalOrder }.Run();
            })
            .SampleGroup(new SampleGroup("OptimalOrderSweep", unit))
            .WarmupCount(0)
            .MeasurementCount(1)
            .Run();

            Measure.Method(() => { new FlippedSweep {
                                       xmins = xmins, xmaxs = xmaxs, minYZmaxYZsFlipped = minYZmaxYZsFlipped, entities = entities, overlaps = pairsFlipped
                                   }.Run(); })
            .SampleGroup(new SampleGroup("FlippedSweep", unit))
            .WarmupCount(0)
            .MeasurementCount(1)
            .Run();

            Measure.Method(() => { new SentinelSweep {
                                       xmins = xminsFull, xmaxs = xmaxsFull, minYZmaxYZsFlipped = minYZmaxYZsFlippedFull, entities = entities, overlaps = pairsSentinel
                                   }.Run(); })
            .SampleGroup(new SampleGroup("SentinelSweep", unit))
            .WarmupCount(0)
            .MeasurementCount(1)
            .Run();

            Measure.Method(() => { new MortonNaiveSweep { aabbs = randomAabbsMorton, overlaps = pairsMortonNaive }.Run(); })
            .SampleGroup(new SampleGroup("MortonNaiveSweep", unit))
            .WarmupCount(0)
            .MeasurementCount(1)
            .Run();

            Measure.Method(() => { new BatchSweep {
                                       xmins = xmins, xmaxs = xmaxs, minYZmaxYZs = minYZmaxYZs, batchMinYZmaxYZs = batchMinYZmaxYZ, entities = entities, overlaps = pairsBatch
                                   }.Run(); })
            .SampleGroup(new SampleGroup("BatchSweep", unit))
            .WarmupCount(0)
            .MeasurementCount(1)
            .Run();

            Measure.Method(() => { new DualSweep { zToXMinsMaxes = zToXMinsMaxes, xs = xs, minYZmaxYZs = minYZmaxYZs, entities = entities, overlaps = pairsDual }.Run(); })
            .SampleGroup(new SampleGroup("DualSweep", unit))
            .WarmupCount(0)
            .MeasurementCount(1)
            .Run();

            Measure.Method(() => {
                //pairsDualCondensed.Clear();
                new DualSweepCondensed {
                    zToXMinsMaxes = zToXMinsMaxes, xs = xs, minYZmaxYZs = minYZmaxYZs, entities = entities, overlaps = pairsDualCondensed
                }.Run();
            })
            .SampleGroup(new SampleGroup("DualSweepCondensed", unit))
            .WarmupCount(0)
            .MeasurementCount(1)
            .Run();

            Measure.Method(() => {
                //pairsDualOptimized.Clear();
                new DualSweepOptimized
                {
                    zToXMinsMaxes = zToXMinsMaxes,
                    xs            = xs,
                    minYZmaxYZs   = minYZmaxYZs,
                    entities      = entities,
                    overlaps      = pairsDualOptimized
                }.Run();
            })
            .SampleGroup(new SampleGroup("DualSweepOptimized", unit))
            .WarmupCount(0)
            .MeasurementCount(1)
            //.MeasurementCount(count)
            .Run();

            Measure.Method(() => {
                //pairsUnrolled.Clear();
                new UnrolledSweepPoor
                {
                    xmins              = xmins,
                    xmaxs              = xmaxs,
                    minYZmaxYZsFlipped = minYZmaxYZsFlipped,
                    entities           = entities,
                    overlaps           = pairsUnrolledPoor
                }.Run();
            })
            .SampleGroup(new SampleGroup("UnrolledSweepPoor", unit))
            .WarmupCount(0)
            .MeasurementCount(1)
            //.MeasurementCount(count)
            .Run();

            Measure.Method(() => {
                //pairsUnrolled.Clear();
                new UnrolledSweep
                {
                    xmins              = xmins,
                    xmaxs              = xmaxs,
                    minYZmaxYZsFlipped = minYZmaxYZsFlipped,
                    entities           = entities,
                    overlaps           = pairsUnrolled
                }.Run();
            })
            .SampleGroup(new SampleGroup("UnrolledSweep", unit))
            .WarmupCount(0)
            .MeasurementCount(1)
            //.MeasurementCount(count)
            .Run();

            Measure.Method(() => {
                //pairsUnrolled2.Clear();
                new UnrolledSweep2
                {
                    xmins              = xmins,
                    xmaxs              = xmaxs,
                    minYZmaxYZsFlipped = minYZmaxYZsFlipped,
                    entities           = entities,
                    overlaps           = pairsUnrolled2
                }.Run();
            })
            .SampleGroup(new SampleGroup("UnrolledSweep2", unit))
            .WarmupCount(0)
            .MeasurementCount(1)
            //.MeasurementCount(count)
            .Run();

            Measure.Method(() => {
                //pairsUnrolled3.Clear();
                new UnrolledSweep3
                {
                    xmins              = xmins,
                    xmaxs              = xmaxs,
                    minYZmaxYZsFlipped = minYZmaxYZsFlipped,
                    entities           = entities,
                    overlaps           = pairsUnrolled3
                }.Run();
            })
            .SampleGroup(new SampleGroup("UnrolledSweep3", unit))
            .WarmupCount(0)
            .MeasurementCount(1)
            //.MeasurementCount(count)
            .Run();

            Measure.Method(() => {
                //pairsUnrolled4.Clear();
                new UnrolledSweep4
                {
                    xmins              = xmins,
                    xmaxs              = xmaxs,
                    minYZmaxYZsFlipped = minYZmaxYZsFlipped,
                    entities           = entities,
                    overlaps           = pairsUnrolled4
                }.Run();
            })
            .SampleGroup(new SampleGroup("UnrolledSweep4", unit))
            .WarmupCount(0)
            .MeasurementCount(1)
            //.MeasurementCount(count)
            .Run();

            Measure.Method(() => {
                //pairsDualBranchless.Clear();
                new DualSweepBranchless
                {
                    zToXMinsMaxes      = zToXMinsMaxes,
                    xs                 = xs,
                    minYZmaxYZsFlipped = minYZmaxYZsFlipped,
                    entities           = entities,
                    overlaps           = pairsDualBranchless
                }.Run();
            })
            .SampleGroup(new SampleGroup("DualSweepBranchless", unit))
            .WarmupCount(0)
            .MeasurementCount(1)
            //.MeasurementCount(count)
            .Run();

            //UnityEngine.Debug.Log("Pairs: " + pairsNaive.Length);
            //UnityEngine.Debug.Log("Pairs: " + pairsBetter.Length);
            //UnityEngine.Debug.Log("Pairs: " + pairsSimd.Length);
            //UnityEngine.Debug.Log("Pairs: " + pairsOptimalOrder.Length);
            //UnityEngine.Debug.Log("Pairs: " + pairsNew.Length);
            //UnityEngine.Debug.Log("Pairs: " + pairsDualBranchless.Length);
            //UnityEngine.Debug.Log("Pairs: " + pairsDualOptimized.Length);
            //UnityEngine.Debug.Log("Pairs: " + pairsUnrolled.Length);

            randomAabbs.Dispose();
            randomAabbsRearranged.Dispose();
            randomAabbsMorton.Dispose();
            xminsFull.Dispose();
            xmaxsFull.Dispose();
            minYZmaxYZsFull.Dispose();
            batchMinYZmaxYZ.Dispose();
            zToXMinsMaxes.Dispose();
            xs.Dispose();
            minYZmaxYZsFlippedFull.Dispose();
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
            pairsOptimalOrder.Dispose();
            pairsFlipped.Dispose();
            pairsSentinel.Dispose();
            pairsMortonNaive.Dispose();
            pairsBatch.Dispose();
            pairsDual.Dispose();
            pairsDualCondensed.Dispose();
            pairsDualOptimized.Dispose();
            pairsUnrolledPoor.Dispose();
            pairsUnrolled.Dispose();
            pairsUnrolled2.Dispose();
            pairsUnrolled3.Dispose();
            pairsUnrolled4.Dispose();
            pairsDualBranchless.Dispose();
        }

        [Test, Performance]
        public void Sweep_10()
        {
            SweepPerformanceTests(10, 1, 10);
        }

        [Test, Performance]
        public void Sweep_100()
        {
            SweepPerformanceTests(100, 56, 1000);
        }

        [Test, Performance]
        public void Sweep_1000()
        {
            SweepPerformanceTests(1000, 76389, 10000);
        }

        [Test, Performance]
        public void Sweep_10000()
        {
            SweepPerformanceTests(10000, 2348976, 1000000);
        }

        [Test, Performance]
        public void Sweep_20()
        {
            SweepPerformanceTests(20, 1, 30);
        }

        [Test, Performance]
        public void Sweep_200()
        {
            SweepPerformanceTests(200, 76, 3000);
        }

        [Test, Performance]
        public void Sweep_2000()
        {
            SweepPerformanceTests(2000, 56324, 30000);
        }

        [Test, Performance]
        public void Sweep_20000()
        {
            SweepPerformanceTests(20000, 2980457, 3000000);
        }

        [Test, Performance]
        public void Sweep_50()
        {
            SweepPerformanceTests(50, 1, 60);
        }

        [Test, Performance]
        public void Sweep_500()
        {
            SweepPerformanceTests(500, 23, 6000);
        }

        [Test, Performance]
        public void Sweep_5000()
        {
            SweepPerformanceTests(5000, 47893, 600000);
        }

        [Test, Performance]
        public void Sweep_50000()
        {
            SweepPerformanceTests(50000, 237648, 6000000);
        }

        //[Test, Performance]
        //public void Sweep_100000()
        //{
        //    SweepPerformanceTests(100000, 234896);
        //}
    }
}

