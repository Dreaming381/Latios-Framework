using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

//Todo: Stream types, single schedulers, scratchlists, and inflations
namespace Latios.PhysicsEngine
{
    internal static class FindPairsInternal
    {
        #region Jobs
        [BurstPatcher(typeof(IFindPairsProcessor))]
        [BurstCompile]
        public struct LayerSelfSingle<T> : IJob where T : struct, IFindPairsProcessor
        {
            [ReadOnly] public CollisionLayer layer;
            public T                         processor;

            public void Execute()
            {
                RunImmediate(layer, processor);
            }
        }

        [BurstPatcher(typeof(IFindPairsProcessor))]
        [BurstCompile]
        public struct LayerLayerSingle<T> : IJob where T : struct, IFindPairsProcessor
        {
            [ReadOnly] public CollisionLayer layerA;
            [ReadOnly] public CollisionLayer layerB;
            public T                         processor;

            public void Execute()
            {
                RunImmediate(layerA, layerB, processor);
            }
        }

        [BurstPatcher(typeof(IFindPairsProcessor))]
        [BurstCompile]
        public struct LayerSelfPart1<T> : IJobParallelFor where T : struct, IFindPairsProcessor
        {
            [ReadOnly] public CollisionLayer layer;
            public T                         processor;

            public void Execute(int index)
            {
                var bucket = layer.GetBucketSlices(index);
                SelfSweep(bucket, index, processor);
            }
        }

        [BurstPatcher(typeof(IFindPairsProcessor))]
        [BurstCompile]
        public struct LayerSelfPart2<T> : IJob where T : struct, IFindPairsProcessor
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

        [BurstPatcher(typeof(IFindPairsProcessor))]
        [BurstCompile]
        public struct LayerLayerPart1<T> : IJobParallelFor where T : struct, IFindPairsProcessor
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

        [BurstPatcher(typeof(IFindPairsProcessor))]
        [BurstCompile]
        public struct LayerLayerPart2<T> : IJobParallelFor where T : struct, IFindPairsProcessor
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
        #endregion

        #region ImmediateMethods
        public static void RunImmediate<T>(CollisionLayer layer, T processor) where T : struct, IFindPairsProcessor
        {
            int jobIndex = 0;
            for (int i = 0; i < layer.BucketCount; i++)
            {
                var bucket = layer.GetBucketSlices(i);
                SelfSweep(bucket, jobIndex++, processor);
            }

            var crossBucket = layer.GetBucketSlices(layer.BucketCount - 1);
            for (int i = 0; i < layer.BucketCount - 1; i++)
            {
                var bucket = layer.GetBucketSlices(i);
                BipartiteSweep(bucket, crossBucket, jobIndex++, processor);
            }
        }

        public static void RunImmediate<T>(CollisionLayer layerA, CollisionLayer layerB, T processor) where T : struct, IFindPairsProcessor
        {
            int jobIndex = 0;
            for (int i = 0; i < layerA.BucketCount; i++)
            {
                var bucketA = layerA.GetBucketSlices(i);
                var bucketB = layerB.GetBucketSlices(i);
                BipartiteSweep(bucketA, bucketB, jobIndex++, processor);
            }

            var crossBucketA = layerA.GetBucketSlices(layerA.BucketCount - 1);
            for (int i = 0; i < layerA.BucketCount - 1; i++)
            {
                var bucket = layerB.GetBucketSlices(i);
                BipartiteSweep(crossBucketA, bucket, jobIndex++, processor);
            }

            var crossBucketB = layerB.GetBucketSlices(layerB.BucketCount - 1);
            for (int i = 0; i < layerA.BucketCount - 1; i++)
            {
                var bucket = layerA.GetBucketSlices(i);
                BipartiteSweep(bucket, crossBucketB, jobIndex++, processor);
            }
        }
        #endregion

        #region SweepMethods
        private static void SelfSweep<T>(BucketSlices bucket, int jobIndex, T processor) where T : struct, IFindPairsProcessor
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
                            bodyA      = bucket.bodies[i],
                            bodyB      = bucket.bodies[j],
                            bodyAIndex = i,
                            bodyBIndex = j,
                            jobIndex   = jobIndex
                        });
                    }
                }
            }
        }

        private static void BipartiteSweep<T>(BucketSlices bucketA, BucketSlices bucketB, int jobIndex, T processor) where T : struct, IFindPairsProcessor
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
                            bodyA      = bucketA.bodies[i],
                            bodyB      = bucketB.bodies[j],
                            bodyAIndex = i,
                            bodyBIndex = j,
                            jobIndex   = jobIndex
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
                            bodyA      = bucketA.bodies[j],
                            bodyB      = bucketB.bodies[i],
                            bodyAIndex = i,
                            bodyBIndex = j,
                            jobIndex   = jobIndex
                        });
                    }
                }
            }
        }
        #endregion SweepMethods
    }
}

