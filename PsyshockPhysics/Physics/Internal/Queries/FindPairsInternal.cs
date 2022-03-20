using System;
using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

//Todo: Stream types, single schedulers, scratchlists, and inflations
namespace Latios.Psyshock
{
    public partial struct FindPairsConfig<T> where T : struct, IFindPairsProcessor
    {
        internal static class FindPairsInternal
        {
            #region Jobs
            [BurstCompile]
            public struct LayerSelfSingle : IJob
            {
                [ReadOnly] public CollisionLayer layer;
                public T                         processor;

                public void Execute()
                {
                    RunImmediate(layer, processor);
                }
            }

            [BurstCompile]
            public struct LayerLayerSingle : IJob
            {
                [ReadOnly] public CollisionLayer layerA;
                [ReadOnly] public CollisionLayer layerB;
                public T                         processor;

                public void Execute()
                {
                    RunImmediate(layerA, layerB, processor);
                }
            }

            [BurstCompile]
            public struct LayerSelfPart1 : IJobParallelFor
            {
                [ReadOnly] public CollisionLayer layer;
                public T                         processor;

                public void Execute(int index)
                {
                    var bucket = layer.GetBucketSlices(index);
                    SelfSweep(bucket, index, processor);
                }
            }

            [BurstCompile]
            public struct LayerSelfPart2 : IJob
            {
                [ReadOnly] public CollisionLayer layer;
                public T                         processor;

                public void Execute()
                {
                    var crossBucket = layer.GetBucketSlices(layer.BucketCount - 1);
                    for (int i = 0; i < layer.BucketCount - 1; i++)
                    {
                        var bucket = layer.GetBucketSlices(i);
                        BipartiteSweep(bucket, crossBucket, layer.BucketCount + i, processor);
                    }
                }
            }

            [BurstCompile]
            public struct LayerLayerPart1 : IJobParallelFor
            {
                [ReadOnly] public CollisionLayer layerA;
                [ReadOnly] public CollisionLayer layerB;
                public T                         processor;

                public void Execute(int index)
                {
                    var bucketA = layerA.GetBucketSlices(index);
                    var bucketB = layerB.GetBucketSlices(index);
                    BipartiteSweep(bucketA, bucketB, index, processor);
                }
            }

            [BurstCompile]
            public struct LayerLayerPart2 : IJobParallelFor
            {
                [ReadOnly] public CollisionLayer layerA;
                [ReadOnly] public CollisionLayer layerB;
                public T                         processor;

                public void Execute(int index)
                {
                    if (index == 0)
                    {
                        var crossBucket = layerA.GetBucketSlices(layerA.BucketCount - 1);
                        for (int i = 0; i < layerB.BucketCount - 1; i++)
                        {
                            var bucket = layerB.GetBucketSlices(i);
                            BipartiteSweep(crossBucket, bucket, layerA.BucketCount + i, processor);
                        }
                    }
                    else if (index == 1)
                    {
                        var crossBucket = layerB.GetBucketSlices(layerB.BucketCount - 1);
                        for (int i = 0; i < layerA.BucketCount - 1; i++)
                        {
                            var bucket = layerA.GetBucketSlices(i);
                            BipartiteSweep(bucket, crossBucket, layerA.BucketCount + layerB.BucketCount + i, processor);
                        }
                    }
                }
            }

            [BurstCompile]
            public struct LayerSelfParallelUnsafe : IJobFor
            {
                [ReadOnly] public CollisionLayer layer;
                public T                         processor;

                public void Execute(int i)
                {
                    if (i < layer.BucketCount)
                    {
                        var bucket = layer.GetBucketSlices(i);
                        SelfSweep(bucket, i, processor);
                    }
                    else
                    {
                        i               -= layer.BucketCount;
                        var bucket       = layer.GetBucketSlices(i);
                        var crossBucket  = layer.GetBucketSlices(layer.BucketCount - 1);
                        BipartiteSweep(bucket, crossBucket, i + layer.BucketCount, processor);
                    }
                }
            }

            [BurstCompile]
            public struct LayerLayerParallelUnsafe : IJobFor
            {
                [ReadOnly] public CollisionLayer layerA;
                [ReadOnly] public CollisionLayer layerB;
                public T                         processor;

                public void Execute(int i)
                {
                    if (i < layerA.BucketCount)
                    {
                        var bucketA = layerA.GetBucketSlices(i);
                        var bucketB = layerB.GetBucketSlices(i);
                        BipartiteSweep(bucketA, bucketB, i, processor);
                    }
                    else if (i < 2 * layerB.BucketCount - 1)
                    {
                        i               -= layerB.BucketCount;
                        var bucket       = layerB.GetBucketSlices(i);
                        var crossBucket  = layerA.GetBucketSlices(layerA.BucketCount - 1);
                        BipartiteSweep(crossBucket, bucket, i + layerB.BucketCount, processor);
                    }
                    else
                    {
                        var jobIndex     = i;
                        i               -= (2 * layerB.BucketCount - 1);
                        var bucket       = layerA.GetBucketSlices(i);
                        var crossBucket  = layerB.GetBucketSlices(layerB.BucketCount - 1);
                        BipartiteSweep(bucket, crossBucket, jobIndex, processor);
                    }
                }
            }
            #endregion

            #region ImmediateMethods
            public static void RunImmediate(CollisionLayer layer, T processor)
            {
                int jobIndex = 0;
                for (int i = 0; i < layer.BucketCount; i++)
                {
                    var bucket = layer.GetBucketSlices(i);
                    SelfSweep(bucket, jobIndex++, processor, false);
                }

                var crossBucket = layer.GetBucketSlices(layer.BucketCount - 1);
                for (int i = 0; i < layer.BucketCount - 1; i++)
                {
                    var bucket = layer.GetBucketSlices(i);
                    BipartiteSweep(bucket, crossBucket, jobIndex++, processor, false);
                }
            }

            public static void RunImmediate(CollisionLayer layerA, CollisionLayer layerB, T processor)
            {
                int jobIndex = 0;
                for (int i = 0; i < layerA.BucketCount; i++)
                {
                    var bucketA = layerA.GetBucketSlices(i);
                    var bucketB = layerB.GetBucketSlices(i);
                    BipartiteSweep(bucketA, bucketB, jobIndex++, processor, false);
                }

                var crossBucketA = layerA.GetBucketSlices(layerA.BucketCount - 1);
                for (int i = 0; i < layerA.BucketCount - 1; i++)
                {
                    var bucket = layerB.GetBucketSlices(i);
                    BipartiteSweep(crossBucketA, bucket, jobIndex++, processor, false);
                }

                var crossBucketB = layerB.GetBucketSlices(layerB.BucketCount - 1);
                for (int i = 0; i < layerA.BucketCount - 1; i++)
                {
                    var bucket = layerA.GetBucketSlices(i);
                    BipartiteSweep(bucket, crossBucketB, jobIndex++, processor, false);
                }
            }
            #endregion

            #region SafeChecks

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            [BurstCompile]
            public struct LayerSelfPart2_WithSafety : IJobFor
            {
                [ReadOnly] public CollisionLayer layer;
                public T processor;

                public void Execute(int index)
                {
                    if (index == 0)
                    {
                        var crossBucket = layer.GetBucketSlices(layer.BucketCount - 1);
                        for (int i = 0; i < layer.BucketCount - 1; i++)
                        {
                            var bucket = layer.GetBucketSlices(i);
                            BipartiteSweep(bucket, crossBucket, layer.BucketCount + i, processor);
                        }
                    }
                    else
                    {
                        EntityAliasCheck(layer);
                    }
                }
            }

            [BurstCompile]
            public struct LayerLayerPart2_WithSafety : IJobParallelFor
            {
                [ReadOnly] public CollisionLayer layerA;
                [ReadOnly] public CollisionLayer layerB;
                public T processor;

                public void Execute(int index)
                {
                    if (index == 0)
                    {
                        var crossBucket = layerA.GetBucketSlices(layerA.BucketCount - 1);
                        for (int i = 0; i < layerB.BucketCount - 1; i++)
                        {
                            var bucket = layerB.GetBucketSlices(i);
                            BipartiteSweep(crossBucket, bucket, layerA.BucketCount + i, processor);
                        }
                    }
                    else if (index == 1)
                    {
                        var crossBucket = layerB.GetBucketSlices(layerB.BucketCount - 1);
                        for (int i = 0; i < layerA.BucketCount - 1; i++)
                        {
                            var bucket = layerA.GetBucketSlices(i);
                            BipartiteSweep(bucket, crossBucket, layerA.BucketCount + layerB.BucketCount + i, processor);
                        }
                    }
                    else
                    {
                        EntityAliasCheck(layerA, layerB);
                    }
                }
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            private static void EntityAliasCheck(CollisionLayer layer)
            {
                var hashSet = new NativeHashSet<Entity>(layer.Count, Allocator.Temp);
                for (int i = 0; i < layer.Count; i++)
                {
                    if (!hashSet.Add(layer.bodies[i].entity))
                    {
                        var entity = layer.bodies[i].entity;
                        throw new InvalidOperationException(
                            $"A parallel FindPairs job was scheduled using a layer containing more than one instance of Entity {entity}");
                    }
                }
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            private static void EntityAliasCheck(CollisionLayer layerA, CollisionLayer layerB)
            {
                var hashSet = new NativeHashSet<Entity>(layerA.Count + layerB.Count, Allocator.Temp);
                for (int i = 0; i < layerA.Count; i++)
                {
                    if (!hashSet.Add(layerA.bodies[i].entity))
                    {
                        //Note: At this point, we know the issue lies exclusively in layerA.
                        var entity = layerA.bodies[i].entity;
                        throw new InvalidOperationException(
                            $"A parallel FindPairs job was scheduled using a layer containing more than one instance of Entity {entity}");
                    }
                }
                for (int i = 0; i < layerB.Count; i++)
                {
                    if (!hashSet.Add(layerB.bodies[i].entity))
                    {
                        //Note: At this point, it is unknown whether the repeating entity first showed up in layerA or layerB.
                        var entity = layerB.bodies[i].entity;
                        throw new InvalidOperationException(
                            $"A parallel FindPairs job was scheduled using two layers combined containing more than one instance of Entity {entity}");
                    }
                }
            }
#endif

            #endregion

            #region SweepMethods
            private static void SelfSweep(BucketSlices bucket, int jobIndex, T processor, bool isThreadSafe = true)
            {
                int count = bucket.xmins.Length;
                for (int i = 0; i < count - 1; i++)
                {
                    for (int j = i + 1; j < count && bucket.xmins[j] <= bucket.xmaxs[i]; j++)
                    {
                        float4 less = new float4(bucket.yzminmaxs[i].z, bucket.yzminmaxs[j].z, bucket.yzminmaxs[i].w, bucket.yzminmaxs[j].w);
                        float4 more = new float4(bucket.yzminmaxs[j].x, bucket.yzminmaxs[i].x, bucket.yzminmaxs[j].y, bucket.yzminmaxs[i].y);

                        bool4 tests = less < more;
                        if (math.bitmask(tests) == 0)
                        {
                            processor.Execute(new FindPairsResult
                            {
                                bodyA        = bucket.bodies[i],
                                bodyB        = bucket.bodies[j],
                                bodyAIndex   = i,
                                bodyBIndex   = j,
                                jobIndex     = jobIndex,
                                isThreadSafe = isThreadSafe
                            });
                        }
                    }
                }
            }

            private static void BipartiteSweep(BucketSlices bucketA, BucketSlices bucketB, int jobIndex, T processor, bool isThreadSafe = true)
            {
                int countA = bucketA.xmins.Length;
                int countB = bucketB.xmins.Length;
                if (countA == 0 || countB == 0)
                    return;

                //Check for b starting in a's x range
                int bstart = 0;
                for (int i = 0; i < countA; i++)
                {
                    //Advance to b.xmin >= a.xmin
                    //Include equals case by stopping when equal
                    while (bstart < countB && bucketB.xmins[bstart] < bucketA.xmins[i])
                        bstart++;
                    if (bstart >= countB)
                        break;

                    for (int j = bstart; j < countB && bucketB.xmins[j] <= bucketA.xmaxs[i]; j++)
                    {
                        float4 less = new float4(bucketA.yzminmaxs[i].z, bucketB.yzminmaxs[j].z, bucketA.yzminmaxs[i].w, bucketB.yzminmaxs[j].w);
                        float4 more = new float4(bucketB.yzminmaxs[j].x, bucketA.yzminmaxs[i].x, bucketB.yzminmaxs[j].y, bucketA.yzminmaxs[i].y);

                        bool4 tests = less < more;

                        if (math.bitmask(tests) == 0)
                        {
                            processor.Execute(new FindPairsResult
                            {
                                bodyA        = bucketA.bodies[i],
                                bodyB        = bucketB.bodies[j],
                                bodyAIndex   = i,
                                bodyBIndex   = j,
                                jobIndex     = jobIndex,
                                isThreadSafe = isThreadSafe
                            });
                        }
                    }
                }

                //Check for a starting in b's x range
                int astart = 0;
                for (int i = 0; i < countB; i++)
                {
                    //Advance to a.xmin > b.xmin
                    //Exclude equals case this time by continuing if equal
                    while (astart < countA && bucketA.xmins[astart] <= bucketB.xmins[i])
                        astart++;
                    if (astart >= countA)
                        break;

                    for (int j = astart; j < countA && bucketA.xmins[j] <= bucketB.xmaxs[i]; j++)
                    {
                        float4 less = new float4(bucketB.yzminmaxs[i].z, bucketA.yzminmaxs[j].z, bucketB.yzminmaxs[i].w, bucketA.yzminmaxs[j].w);
                        float4 more = new float4(bucketA.yzminmaxs[j].x, bucketB.yzminmaxs[i].x, bucketA.yzminmaxs[j].y, bucketB.yzminmaxs[i].y);

                        bool4 tests = less < more;

                        if (math.bitmask(tests) == 0)
                        {
                            processor.Execute(new FindPairsResult
                            {
                                bodyA        = bucketA.bodies[j],
                                bodyB        = bucketB.bodies[i],
                                bodyAIndex   = i,
                                bodyBIndex   = j,
                                jobIndex     = jobIndex,
                                isThreadSafe = isThreadSafe
                            });
                        }
                    }
                }
            }
            #endregion SweepMethods
        }
    }
}

