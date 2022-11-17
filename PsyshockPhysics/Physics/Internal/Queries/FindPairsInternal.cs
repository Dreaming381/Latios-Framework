using System;
using System.Diagnostics;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

//Todo: Stream types, single schedulers, scratchlists, and inflations
namespace Latios.Psyshock
{
    public partial struct FindPairsLayerSelfConfig<T> where T : struct, IFindPairsProcessor
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
            public struct LayerSelfPart1 : IJobParallelFor
            {
                [ReadOnly] public CollisionLayer layer;
                public T                         processor;

                public void Execute(int index)
                {
                    var bucket = layer.GetBucketSlices(index);
                    FindPairsSweepMethods.SelfSweep(layer, bucket, index, ref processor);
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
                        FindPairsSweepMethods.BipartiteSweep(layer, layer, bucket, crossBucket, layer.BucketCount + i, ref processor);
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
                        FindPairsSweepMethods.SelfSweep(layer, bucket, i, ref processor);
                    }
                    else
                    {
                        i               -= layer.BucketCount;
                        var bucket       = layer.GetBucketSlices(i);
                        var crossBucket  = layer.GetBucketSlices(layer.BucketCount - 1);
                        FindPairsSweepMethods.BipartiteSweep(layer, layer, bucket, crossBucket, i + layer.BucketCount, ref processor);
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
                    FindPairsSweepMethods.SelfSweep(layer, bucket, jobIndex++, ref processor, false);
                }

                var crossBucket = layer.GetBucketSlices(layer.BucketCount - 1);
                for (int i = 0; i < layer.BucketCount - 1; i++)
                {
                    var bucket = layer.GetBucketSlices(i);
                    FindPairsSweepMethods.BipartiteSweep(layer, layer, bucket, crossBucket, jobIndex++, ref processor, false);
                }
            }
            #endregion

            #region SafeChecks

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            // Scheudle for 2 iterations
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
                            FindPairsSweepMethods.BipartiteSweep(layer, layer, bucket, crossBucket, layer.BucketCount + i, ref processor);
                        }
                    }
                    else
                    {
                        EntityAliasCheck(layer);
                    }
                }
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            private static void EntityAliasCheck(CollisionLayer layer)
            {
                var hashSet = new NativeParallelHashSet<Entity>(layer.Count, Allocator.Temp);
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
#endif

            #endregion
        }
    }

    public partial struct FindPairsLayerSelfWithCrossCacheConfig<T> where T : struct, IFindPairsProcessor
    {
        internal static class FindPairsInternal
        {
            #region Jobs

            // Schedule for (2 * layer.BucketCount - 1) iterations
            [BurstCompile]
            public struct LayerSelfPart1 : IJobParallelFor, IFindPairsProcessor
            {
                [ReadOnly] public CollisionLayer                                 layer;
                public T                                                         processor;
                [NativeDisableParallelForRestriction] public NativeStream.Writer cache;

                public void Execute(int index)
                {
                    if (index < layer.BucketCount)
                    {
                        var bucket = layer.GetBucketSlices(index);
                        FindPairsSweepMethods.SelfSweep(layer, bucket, index, ref processor);
                    }
                    else
                    {
                        var bucket      = layer.GetBucketSlices(index - layer.BucketCount);
                        var crossBucket = layer.GetBucketSlices(layer.BucketCount - 1);

                        cache.BeginForEachIndex(index - layer.BucketCount);
                        FindPairsSweepMethods.BipartiteSweep(layer, layer, bucket, crossBucket, index, ref this);
                        cache.EndForEachIndex();
                    }
                }

                public void Execute(in FindPairsResult result)
                {
                    int2 pair = new int2(result.indexA, result.indexB);
                    cache.Write(pair);
                }
            }

            [BurstCompile]
            public struct LayerSelfPart2 : IJob
            {
                [ReadOnly] public CollisionLayer      layer;
                public T                              processor;
                [ReadOnly] public NativeStream.Reader cache;

                public void Execute()
                {
                    for (int i = 0; i < layer.BucketCount - 1; i++)
                    {
                        var result = FindPairsResult.CreateGlobalResult(layer, layer, layer.BucketCount + i, true);

                        var count = cache.BeginForEachIndex(i);
                        for (; count > 0; count--)
                        {
                            var pair = cache.Read<int2>();
                            result.SetBucketRelativePairIndices(pair.x, pair.y);
                            processor.Execute(in result);
                        }
                        cache.EndForEachIndex();
                    }
                }
            }

            #endregion

            #region SafeChecks

#if ENABLE_UNITY_COLLECTIONS_CHECKS

            // Schedule for 2 iterations
            [BurstCompile]
            public struct LayerSelfPart2_WithSafety : IJobFor
            {
                [ReadOnly] public CollisionLayer layer;
                public T processor;
                [ReadOnly] public NativeStream.Reader cache;

                public void Execute(int index)
                {
                    if (index == 0)
                    {
                        for (int i = 0; i < layer.BucketCount - 1; i++)
                        {
                            var result = FindPairsResult.CreateGlobalResult(layer, layer, layer.BucketCount + i, true);

                            var count = cache.BeginForEachIndex(i);
                            for (; count > 0; count--)
                            {
                                var pair = cache.Read<int2>();
                                result.SetBucketRelativePairIndices(pair.x, pair.y);
                                processor.Execute(in result);
                            }
                            cache.EndForEachIndex();
                        }
                    }
                    else
                    {
                        EntityAliasCheck(layer);
                    }
                }
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            private static void EntityAliasCheck(CollisionLayer layer)
            {
                var hashSet = new NativeParallelHashSet<Entity>(layer.Count, Allocator.Temp);
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
#endif

            #endregion
        }
    }

    public partial struct FindPairsLayerLayerConfig<T> where T : struct, IFindPairsProcessor
    {
        internal static class FindPairsInternal
        {
            #region Jobs
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
            public struct LayerLayerPart1 : IJobParallelFor
            {
                [ReadOnly] public CollisionLayer layerA;
                [ReadOnly] public CollisionLayer layerB;
                public T                         processor;

                public void Execute(int index)
                {
                    var bucketA = layerA.GetBucketSlices(index);
                    var bucketB = layerB.GetBucketSlices(index);
                    FindPairsSweepMethods.BipartiteSweep(layerA, layerB, bucketA, bucketB, index, ref processor);
                }
            }

            // Schedule for 2 iterations
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
                            FindPairsSweepMethods.BipartiteSweep(layerA, layerB, crossBucket, bucket, layerA.BucketCount + i, ref processor);
                        }
                    }
                    else if (index == 1)
                    {
                        var crossBucket = layerB.GetBucketSlices(layerB.BucketCount - 1);
                        for (int i = 0; i < layerA.BucketCount - 1; i++)
                        {
                            var bucket = layerA.GetBucketSlices(i);
                            FindPairsSweepMethods.BipartiteSweep(layerA, layerB, bucket, crossBucket, layerA.BucketCount + layerB.BucketCount - 1 + i, ref processor);
                        }
                    }
                }
            }

            // Schedule for (3 * layer.BucketCount - 2) iterations
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
                        FindPairsSweepMethods.BipartiteSweep(layerA, layerB, bucketA, bucketB, i, ref processor);
                    }
                    else if (i < 2 * layerB.BucketCount - 1)
                    {
                        i               -= layerB.BucketCount;
                        var bucket       = layerB.GetBucketSlices(i);
                        var crossBucket  = layerA.GetBucketSlices(layerA.BucketCount - 1);
                        FindPairsSweepMethods.BipartiteSweep(layerA, layerB, crossBucket, bucket, i + layerB.BucketCount, ref processor);
                    }
                    else
                    {
                        var jobIndex     = i;
                        i               -= (2 * layerB.BucketCount - 1);
                        var bucket       = layerA.GetBucketSlices(i);
                        var crossBucket  = layerB.GetBucketSlices(layerB.BucketCount - 1);
                        FindPairsSweepMethods.BipartiteSweep(layerA, layerB, bucket, crossBucket, jobIndex, ref processor);
                    }
                }
            }
            #endregion

            #region ImmediateMethods

            public static void RunImmediate(CollisionLayer layerA, CollisionLayer layerB, T processor)
            {
                int jobIndex = 0;
                for (int i = 0; i < layerA.BucketCount; i++)
                {
                    var bucketA = layerA.GetBucketSlices(i);
                    var bucketB = layerB.GetBucketSlices(i);
                    FindPairsSweepMethods.BipartiteSweep(layerA, layerB, bucketA, bucketB, jobIndex++, ref processor, false);
                }

                var crossBucketA = layerA.GetBucketSlices(layerA.BucketCount - 1);
                for (int i = 0; i < layerA.BucketCount - 1; i++)
                {
                    var bucket = layerB.GetBucketSlices(i);
                    FindPairsSweepMethods.BipartiteSweep(layerA, layerB, crossBucketA, bucket, jobIndex++, ref processor, false);
                }

                var crossBucketB = layerB.GetBucketSlices(layerB.BucketCount - 1);
                for (int i = 0; i < layerA.BucketCount - 1; i++)
                {
                    var bucket = layerA.GetBucketSlices(i);
                    FindPairsSweepMethods.BipartiteSweep(layerA, layerB, bucket, crossBucketB, jobIndex++, ref processor, false);
                }
            }
            #endregion

            #region SafeChecks

#if ENABLE_UNITY_COLLECTIONS_CHECKS

            // Schedule for 3 iterations
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
                            FindPairsSweepMethods.BipartiteSweep(layerA, layerB, crossBucket, bucket, layerA.BucketCount + i, ref processor);
                        }
                    }
                    else if (index == 1)
                    {
                        var crossBucket = layerB.GetBucketSlices(layerB.BucketCount - 1);
                        for (int i = 0; i < layerA.BucketCount - 1; i++)
                        {
                            var bucket = layerA.GetBucketSlices(i);
                            //var marker = new Unity.Profiling.ProfilerMarker($"Cross B {crossBucket.count} - {bucket.count}");
                            //marker.Begin();
                            FindPairsSweepMethods.BipartiteSweep(layerA, layerB, bucket, crossBucket, layerA.BucketCount + layerB.BucketCount - 1 + i, ref processor);
                            //marker.End();
                        }
                    }
                    else
                    {
                        EntityAliasCheck(layerA, layerB);
                    }
                }
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            private static void EntityAliasCheck(CollisionLayer layerA, CollisionLayer layerB)
            {
                var hashSet = new NativeParallelHashSet<Entity>(layerA.Count + layerB.Count, Allocator.Temp);
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
        }
    }

    public partial struct FindPairsLayerLayerWithCrossCacheConfig<T> where T : struct, IFindPairsProcessor
    {
        internal static class FindPairsInternal
        {
            #region Jobs

            // Schedule for (3 * layer.BucketCount - 2) iterations
            [BurstCompile]
            public struct LayerLayerPart1 : IJobParallelFor, IFindPairsProcessor
            {
                [ReadOnly] public CollisionLayer                                 layerA;
                [ReadOnly] public CollisionLayer                                 layerB;
                public T                                                         processor;
                [NativeDisableParallelForRestriction] public NativeStream.Writer cache;

                public void Execute(int i)
                {
                    if (i < layerA.BucketCount)
                    {
                        var bucketA = layerA.GetBucketSlices(i);
                        var bucketB = layerB.GetBucketSlices(i);
                        FindPairsSweepMethods.BipartiteSweep(layerA, layerB, bucketA, bucketB, i, ref processor);
                    }
                    else if (i < 2 * layerB.BucketCount - 1)
                    {
                        i               -= layerB.BucketCount;
                        var bucket       = layerB.GetBucketSlices(i);
                        var crossBucket  = layerA.GetBucketSlices(layerA.BucketCount - 1);
                        cache.BeginForEachIndex(i);
                        FindPairsSweepMethods.BipartiteSweep(layerA, layerB, crossBucket, bucket, i + layerB.BucketCount, ref this);
                        cache.EndForEachIndex();
                    }
                    else
                    {
                        var jobIndex     = i;
                        i               -= (2 * layerB.BucketCount - 1);
                        var bucket       = layerA.GetBucketSlices(i);
                        var crossBucket  = layerB.GetBucketSlices(layerB.BucketCount - 1);
                        cache.BeginForEachIndex(i + layerB.BucketCount - 1);
                        FindPairsSweepMethods.BipartiteSweep(layerA, layerB, bucket, crossBucket, jobIndex, ref this);
                        cache.EndForEachIndex();
                    }
                }

                public void Execute(in FindPairsResult result)
                {
                    int2 pair = new int2(result.indexA, result.indexB);
                    cache.Write(pair);
                }
            }

            // Schedule for 2 iterations
            [BurstCompile]
            public struct LayerLayerPart2 : IJobParallelFor
            {
                [ReadOnly] public CollisionLayer layerA;
                [ReadOnly] public CollisionLayer layerB;
                public T                         processor;
                public NativeStream.Reader       cache;

                public void Execute(int index)
                {
                    if (index == 0)
                    {
                        for (int i = 0; i < layerB.BucketCount - 1; i++)
                        {
                            var result = FindPairsResult.CreateGlobalResult(layerA, layerB, layerA.BucketCount + i, true);

                            var count = cache.BeginForEachIndex(i);
                            for (; count > 0; count--)
                            {
                                var pair = cache.Read<int2>();
                                result.SetBucketRelativePairIndices(pair.x, pair.y);
                                processor.Execute(in result);
                            }
                            cache.EndForEachIndex();
                        }
                    }
                    else if (index == 1)
                    {
                        for (int i = 0; i < layerA.BucketCount - 1; i++)
                        {
                            var result = FindPairsResult.CreateGlobalResult(layerA, layerB, layerA.BucketCount + layerB.BucketCount - 1 + i, true);

                            var count = cache.BeginForEachIndex(i + layerA.BucketCount - 1);
                            for (; count > 0; count--)
                            {
                                var pair = cache.Read<int2>();
                                result.SetBucketRelativePairIndices(pair.x, pair.y);
                                processor.Execute(in result);
                            }
                            cache.EndForEachIndex();
                        }
                    }
                }
            }

            #endregion

            #region SafeChecks

#if ENABLE_UNITY_COLLECTIONS_CHECKS

            // Schedule for 3 iterations
            [BurstCompile]
            public struct LayerLayerPart2_WithSafety : IJobParallelFor
            {
                [ReadOnly] public CollisionLayer layerA;
                [ReadOnly] public CollisionLayer layerB;
                public T processor;
                public NativeStream.Reader cache;

                public void Execute(int index)
                {
                    if (index == 0)
                    {
                        for (int i = 0; i < layerB.BucketCount - 1; i++)
                        {
                            var result = FindPairsResult.CreateGlobalResult(layerA, layerB, layerA.BucketCount + i, true);

                            var count = cache.BeginForEachIndex(i);
                            for (; count > 0; count--)
                            {
                                var pair = cache.Read<int2>();
                                result.SetBucketRelativePairIndices(pair.x, pair.y);
                                processor.Execute(in result);
                            }
                            cache.EndForEachIndex();
                        }
                    }
                    else if (index == 1)
                    {
                        for (int i = 0; i < layerA.BucketCount - 1; i++)
                        {
                            var result = FindPairsResult.CreateGlobalResult(layerA, layerB, layerA.BucketCount + layerB.BucketCount - 1 + i, true);

                            var count = cache.BeginForEachIndex(i + layerA.BucketCount - 1);
                            for (; count > 0; count--)
                            {
                                var pair = cache.Read<int2>();
                                result.SetBucketRelativePairIndices(pair.x, pair.y);
                                processor.Execute(in result);
                            }
                            cache.EndForEachIndex();
                        }
                    }
                    else
                    {
                        EntityAliasCheck(layerA, layerB);
                    }
                }
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            private static void EntityAliasCheck(CollisionLayer layerA, CollisionLayer layerB)
            {
                var hashSet = new NativeParallelHashSet<Entity>(layerA.Count + layerB.Count, Allocator.Temp);
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
        }
    }

    internal partial struct FindPairsLayerSelfConfigUnrolled<T> where T : struct, IFindPairsProcessor
    {
        internal static class FindPairsInternalUnrolled
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
            public struct LayerSelfPart1 : IJobParallelFor
            {
                [ReadOnly] public CollisionLayer layer;
                public T                         processor;

                public void Execute(int index)
                {
                    var bucket = layer.GetBucketSlices(index);
                    var drain  = new FindPairsSweepMethods.FindPairsProcessorDrain<T>();
                    //drain.drainBuffer1024 = new NativeArray<ulong>(1024, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                    drain.processor = processor;
                    FindPairsSweepMethods.SelfSweepUnrolled(layer, bucket, index, ref drain);
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
                    var drain       = new FindPairsSweepMethods.FindPairsProcessorDrain<T>();
                    //drain.drainBuffer1024 = new NativeArray<ulong>(1024, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                    drain.processor = processor;
                    for (int i = 0; i < layer.BucketCount - 1; i++)
                    {
                        var bucket = layer.GetBucketSlices(i);
                        FindPairsSweepMethods.BipartiteSweepUnrolled(layer, layer, bucket, crossBucket, layer.BucketCount + i, ref drain);
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
                    var drain = new FindPairsSweepMethods.FindPairsProcessorDrain<T>();
                    //drain.drainBuffer1024 = new NativeArray<ulong>(1024, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                    drain.processor = processor;

                    if (i < layer.BucketCount)
                    {
                        var bucket = layer.GetBucketSlices(i);
                        FindPairsSweepMethods.SelfSweepUnrolled(layer, bucket, i, ref drain);
                    }
                    else
                    {
                        i               -= layer.BucketCount;
                        var bucket       = layer.GetBucketSlices(i);
                        var crossBucket  = layer.GetBucketSlices(layer.BucketCount - 1);
                        FindPairsSweepMethods.BipartiteSweepUnrolled(layer, layer, bucket, crossBucket, i + layer.BucketCount, ref drain);
                    }
                }
            }
            #endregion

            #region ImmediateMethods
            public static void RunImmediate(CollisionLayer layer, T processor)
            {
                var drain = new FindPairsSweepMethods.FindPairsProcessorDrain<T>();
                //drain.drainBuffer1024 = new NativeArray<ulong>(1024, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                drain.processor = processor;

                int jobIndex = 0;
                for (int i = 0; i < layer.BucketCount; i++)
                {
                    var bucket = layer.GetBucketSlices(i);
                    FindPairsSweepMethods.SelfSweepUnrolled(layer, bucket, jobIndex++, ref drain, false);
                }

                var crossBucket = layer.GetBucketSlices(layer.BucketCount - 1);
                for (int i = 0; i < layer.BucketCount - 1; i++)
                {
                    var bucket = layer.GetBucketSlices(i);
                    FindPairsSweepMethods.BipartiteSweepUnrolled(layer, layer, bucket, crossBucket, jobIndex++, ref drain, false);
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
                        var drain = new FindPairsSweepMethods.FindPairsProcessorDrain<T>();
                        //drain.drainBuffer1024 = new NativeArray<ulong>(1024, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                        drain.processor = processor;

                        var crossBucket = layer.GetBucketSlices(layer.BucketCount - 1);
                        for (int i = 0; i < layer.BucketCount - 1; i++)
                        {
                            var bucket = layer.GetBucketSlices(i);
                            FindPairsSweepMethods.BipartiteSweepUnrolled(layer, layer, bucket, crossBucket, layer.BucketCount + i, ref drain);
                        }
                    }
                    else
                    {
                        EntityAliasCheck(layer);
                    }
                }
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            private static void EntityAliasCheck(CollisionLayer layer)
            {
                var hashSet = new NativeParallelHashSet<Entity>(layer.Count, Allocator.Temp);
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
#endif

            #endregion

            #region SweepUnrolledMethods

            #endregion SweepUnrolledMethods
        }
    }

    internal partial struct FindPairsLayerLayerConfigUnrolled<T> where T : struct, IFindPairsProcessor
    {
        internal static class FindPairsInternalUnrolled
        {
            #region Jobs
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
            public struct LayerLayerPart1 : IJobParallelFor
            {
                [ReadOnly] public CollisionLayer layerA;
                [ReadOnly] public CollisionLayer layerB;
                public T                         processor;

                public void Execute(int index)
                {
                    var bucketA = layerA.GetBucketSlices(index);
                    var bucketB = layerB.GetBucketSlices(index);

                    var drain = new FindPairsSweepMethods.FindPairsProcessorDrain<T>();
                    //drain.drainBuffer1024 = new NativeArray<ulong>(1024, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                    drain.processor = processor;

                    FindPairsSweepMethods.BipartiteSweepUnrolled(layerA, layerB, bucketA, bucketB, index, ref drain);
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
                    var drain = new FindPairsSweepMethods.FindPairsProcessorDrain<T>();
                    //drain.drainBuffer1024 = new NativeArray<ulong>(1024, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                    drain.processor = processor;

                    if (index == 0)
                    {
                        var crossBucket = layerA.GetBucketSlices(layerA.BucketCount - 1);
                        for (int i = 0; i < layerB.BucketCount - 1; i++)
                        {
                            var bucket = layerB.GetBucketSlices(i);
                            FindPairsSweepMethods.BipartiteSweepUnrolled(layerA, layerB, crossBucket, bucket, layerA.BucketCount + i, ref drain);
                        }
                    }
                    else if (index == 1)
                    {
                        var crossBucket = layerB.GetBucketSlices(layerB.BucketCount - 1);
                        for (int i = 0; i < layerA.BucketCount - 1; i++)
                        {
                            var bucket = layerA.GetBucketSlices(i);
                            FindPairsSweepMethods.BipartiteSweepUnrolled(layerA, layerB, bucket, crossBucket, layerA.BucketCount + layerB.BucketCount + i, ref drain);
                        }
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
                    var drain = new FindPairsSweepMethods.FindPairsProcessorDrain<T>();
                    //drain.drainBuffer1024 = new NativeArray<ulong>(1024, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                    drain.processor = processor;

                    if (i < layerA.BucketCount)
                    {
                        var bucketA = layerA.GetBucketSlices(i);
                        var bucketB = layerB.GetBucketSlices(i);
                        FindPairsSweepMethods.BipartiteSweepUnrolled(layerA, layerB, bucketA, bucketB, i, ref drain);
                    }
                    else if (i < 2 * layerB.BucketCount - 1)
                    {
                        i               -= layerB.BucketCount;
                        var bucket       = layerB.GetBucketSlices(i);
                        var crossBucket  = layerA.GetBucketSlices(layerA.BucketCount - 1);
                        FindPairsSweepMethods.BipartiteSweepUnrolled(layerA, layerB, crossBucket, bucket, i + layerB.BucketCount, ref drain);
                    }
                    else
                    {
                        var jobIndex     = i;
                        i               -= (2 * layerB.BucketCount - 1);
                        var bucket       = layerA.GetBucketSlices(i);
                        var crossBucket  = layerB.GetBucketSlices(layerB.BucketCount - 1);
                        FindPairsSweepMethods.BipartiteSweepUnrolled(layerA, layerB, bucket, crossBucket, jobIndex, ref drain);
                    }
                }
            }
            #endregion

            #region ImmediateMethods

            public static void RunImmediate(CollisionLayer layerA, CollisionLayer layerB, T processor)
            {
                var drain = new FindPairsSweepMethods.FindPairsProcessorDrain<T>();
                //drain.drainBuffer1024 = new NativeArray<ulong>(1024, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                drain.processor = processor;

                int jobIndex = 0;
                for (int i = 0; i < layerA.BucketCount; i++)
                {
                    var bucketA = layerA.GetBucketSlices(i);
                    var bucketB = layerB.GetBucketSlices(i);
                    FindPairsSweepMethods.BipartiteSweepUnrolled(layerA, layerB, bucketA, bucketB, jobIndex++, ref drain, false);
                }

                var crossBucketA = layerA.GetBucketSlices(layerA.BucketCount - 1);
                for (int i = 0; i < layerA.BucketCount - 1; i++)
                {
                    var bucket = layerB.GetBucketSlices(i);
                    FindPairsSweepMethods.BipartiteSweepUnrolled(layerA, layerB, crossBucketA, bucket, jobIndex++, ref drain, false);
                }

                var crossBucketB = layerB.GetBucketSlices(layerB.BucketCount - 1);
                for (int i = 0; i < layerA.BucketCount - 1; i++)
                {
                    var bucket = layerA.GetBucketSlices(i);
                    FindPairsSweepMethods.BipartiteSweepUnrolled(layerA, layerB, bucket, crossBucketB, jobIndex++, ref drain, false);
                }
            }
            #endregion

            #region SafeChecks

#if ENABLE_UNITY_COLLECTIONS_CHECKS

            [BurstCompile]
            public struct LayerLayerPart2_WithSafety : IJobParallelFor
            {
                [ReadOnly] public CollisionLayer layerA;
                [ReadOnly] public CollisionLayer layerB;
                public T processor;

                public void Execute(int index)
                {
                    var drain = new FindPairsSweepMethods.FindPairsProcessorDrain<T>();
                    //drain.drainBuffer1024 = new NativeArray<ulong>(1024, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                    drain.processor = processor;

                    //var marker0 = new Unity.Profiling.ProfilerMarker("Cross A Unrolled");
                    //var marker1 = new Unity.Profiling.ProfilerMarker("Cross B Unrolled");

                    if (index == 0)
                    {
                        var crossBucket = layerA.GetBucketSlices(layerA.BucketCount - 1);
                        for (int i = 0; i < layerB.BucketCount - 1; i++)
                        {
                            var bucket = layerB.GetBucketSlices(i);
                            //marker0.Begin();
                            FindPairsSweepMethods.BipartiteSweepUnrolled(layerA, layerB, crossBucket, bucket, layerA.BucketCount + i, ref drain);
                            //marker0.End();
                        }
                    }
                    else if (index == 1)
                    {
                        var crossBucket = layerB.GetBucketSlices(layerB.BucketCount - 1);
                        for (int i = 0; i < layerA.BucketCount - 1; i++)
                        {
                            var bucket = layerA.GetBucketSlices(i);
                            //var marker1Detailed = new Unity.Profiling.ProfilerMarker($"Cross B Unrolled {crossBucket.count} - {bucket.count}");
                            //marker1Detailed.Begin();
                            //marker1.Begin();
                            FindPairsSweepMethods.BipartiteSweepUnrolled(layerA, layerB, bucket, crossBucket, layerA.BucketCount + layerB.BucketCount + i, ref drain);
                            //marker1.End();
                            //marker1Detailed.End();
                        }
                    }
                    else
                    {
                        EntityAliasCheck(layerA, layerB);
                    }

                    //if (drain.maxCount > 1000)
                    //    UnityEngine.Debug.Log($"hit count: {drain.maxCount}");
                }
            }

            [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
            private static void EntityAliasCheck(CollisionLayer layerA, CollisionLayer layerB)
            {
                var hashSet = new NativeParallelHashSet<Entity>(layerA.Count + layerB.Count, Allocator.Temp);
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
        }
    }
}

