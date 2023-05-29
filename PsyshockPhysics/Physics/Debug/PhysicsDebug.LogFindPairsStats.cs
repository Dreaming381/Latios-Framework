using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    public static partial class PhysicsDebug
    {
        /// <summary>
        /// Prints to console the number of elements in each cell (bucket) of the CollisionLayer.
        /// This is the start of a Fluent chain.
        /// </summary>
        /// <param name="layer">The layer to extract counts from</param>
        /// <param name="layerName">A name associated with the layer to be printed in the log message</param>
        /// <returns>A config object from which a scheduler should be called</returns>
        public static LogBucketCountsForLayerConfig LogBucketCountsForLayer(in CollisionLayer layer, in FixedString128Bytes layerName)
        {
            return new LogBucketCountsForLayerConfig { layer = layer, layerName = layerName };
        }

        public struct LogBucketCountsForLayerConfig
        {
            internal CollisionLayer      layer;
            internal FixedString128Bytes layerName;

            /// <summary>
            /// Run without using the job system
            /// </summary>
            public void RunImmediate()
            {
                new LogBucketCountsForLayerJob
                {
                    layer     = layer,
                    layerName = layerName
                }.Execute();
            }

            /// <summary>
            /// Run on the same thread using a Burst job
            /// </summary>
            public void Run()
            {
                new LogBucketCountsForLayerJob
                {
                    layer     = layer,
                    layerName = layerName
                }.Run();
            }

            /// <summary>
            /// Schedule on a worker thread as part of a job chain
            /// </summary>
            /// <param name="inputDeps">The previous job this job should wait upon</param>
            /// <returns>A handle associated with the scheduled job</returns>
            public JobHandle Schedule(JobHandle inputDeps)
            {
                return new LogBucketCountsForLayerJob
                {
                    layer     = layer,
                    layerName = layerName
                }.Schedule(inputDeps);
            }
        }

        internal static JobHandle LogFindPairsStats(CollisionLayer layer, FixedString128Bytes layerName, JobHandle inputDeps)
        {
            return new FindPairsLayerSelfStatsJob
            {
                layer     = layer,
                layerName = layerName
            }.ScheduleParallel(layer.bucketCount * 2 - 1, 1, inputDeps);
        }

        internal static JobHandle LogFindPairsStats(CollisionLayer layerA,
                                                    FixedString128Bytes layerNameA,
                                                    CollisionLayer layerB,
                                                    FixedString128Bytes layerNameB,
                                                    JobHandle inputDeps)
        {
            return new FindPairsLayerLayerStatsJob
            {
                layerA     = layerA,
                layerNameA = layerNameA,
                layerB     = layerB,
                layerNameB = layerNameB
            }.ScheduleParallel(3 * layerA.bucketCount - 2, 1, inputDeps);
        }

        [BurstCompile]
        struct LogBucketCountsForLayerJob : IJob
        {
            [ReadOnly] public CollisionLayer layer;
            public FixedString128Bytes       layerName;

            public void Execute()
            {
                FixedString4096Bytes countsAsString = default;
                for (int i = 0; i < layer.bucketStartsAndCounts.Length; i++)
                {
                    if (i == layer.bucketCount - 1)
                    {
                        //countsAsString.Append("cross: ");
                        countsAsString.Append('c');
                        countsAsString.Append('r');
                        countsAsString.Append('o');
                        countsAsString.Append('s');
                        countsAsString.Append('s');
                        countsAsString.Append(':');
                        countsAsString.Append(' ');
                    }
                    else if (i == layer.bucketCount)
                    {
                        //countsAsString.Append("NaN: ");
                        countsAsString.Append('N');
                        countsAsString.Append('a');
                        countsAsString.Append('N');
                        countsAsString.Append(':');
                        countsAsString.Append(' ');
                    }
                    countsAsString.Append(layer.bucketStartsAndCounts[i].y);
                    countsAsString.Append(',');
                    countsAsString.Append(' ');
                }
                UnityEngine.Debug.Log($"Colliders counts per bucket in layer {layerName}:\n{countsAsString}");
            }
        }

        [BurstCompile]
        struct FindPairsLayerSelfStatsJob : IJobFor
        {
            [ReadOnly] public CollisionLayer layer;
            public FixedString128Bytes       layerName;

            public void Execute(int index)
            {
                if (index < layer.bucketCount)
                {
                    var bucket = layer.GetBucketSlices(index);
                    FindPairsSweepMethods.SelfSweepStats(bucket, in layerName);
                }
                else
                {
                    index           -= layer.bucketCount;
                    var bucket       = layer.GetBucketSlices(index);
                    var crossBucket  = layer.GetBucketSlices(layer.bucketCount - 1);
                    FindPairsSweepMethods.BipartiteSweepStats(bucket, in layerName, crossBucket, in layerName);
                }
            }
        }

        [BurstCompile]
        struct FindPairsLayerLayerStatsJob : IJobFor
        {
            [ReadOnly] public CollisionLayer layerA;
            [ReadOnly] public CollisionLayer layerB;
            public FixedString128Bytes       layerNameA;
            public FixedString128Bytes       layerNameB;

            public void Execute(int i)
            {
                if (i < layerA.bucketCount)
                {
                    var bucketA = layerA.GetBucketSlices(i);
                    var bucketB = layerB.GetBucketSlices(i);
                    FindPairsSweepMethods.BipartiteSweepStats(bucketA, layerNameA, bucketB, layerNameB);
                }
                else if (i < 2 * layerB.bucketCount - 1)
                {
                    i               -= layerB.bucketCount;
                    var bucket       = layerB.GetBucketSlices(i);
                    var crossBucket  = layerA.GetBucketSlices(layerA.bucketCount - 1);
                    FindPairsSweepMethods.BipartiteSweepStats(crossBucket, layerNameA, bucket, layerNameB);
                }
                else
                {
                    var jobIndex     = i;
                    i               -= (2 * layerB.bucketCount - 1);
                    var bucket       = layerA.GetBucketSlices(i);
                    var crossBucket  = layerB.GetBucketSlices(layerB.bucketCount - 1);
                    FindPairsSweepMethods.BipartiteSweepStats(bucket, layerNameA, crossBucket, layerNameB);
                }
            }
        }
    }
}

