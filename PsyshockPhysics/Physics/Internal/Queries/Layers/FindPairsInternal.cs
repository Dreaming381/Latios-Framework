using System;
using System.Diagnostics;
using Latios.Unsafe;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine.Scripting;

//Todo: FilteredCache playback and inflations
namespace Latios.Psyshock
{
    public partial struct FindPairsLayerSelfConfig<T> where T : struct, IFindPairsProcessor
    {
        internal enum ScheduleMode
        {
            Single,
            ParallelPart1,
            ParallelPart1AllowEntityAliasing,
            ParallelPart2,
            ParallelPart2AllowEntityAliasing,
            ParallelUnsafe
        }

        internal static class FindPairsInternal
        {
            [BurstCompile]
            public struct LayerSelfJob : IJobFor
            {
                [ReadOnly] CollisionLayer      layer;
                T                              processor;
                ScheduleMode                   scheduleMode;
                Unity.Profiling.ProfilerMarker modeAndTMarker;

                #region Construction and Scheduling
                public LayerSelfJob(in CollisionLayer layer, in T processor)
                {
                    this.layer     = layer;
                    this.processor = processor;
                    scheduleMode   = default;
                    modeAndTMarker = default;
                }

                public void Run()
                {
                    SetScheduleMode(ScheduleMode.Single);
                    this.Run(1);
                }

                public JobHandle ScheduleSingle(JobHandle inputDeps)
                {
                    SetScheduleMode(ScheduleMode.Single);
                    return this.Schedule(1, inputDeps);
                }

                public JobHandle ScheduleParallel(JobHandle inputDeps, ScheduleMode scheduleMode)
                {
                    SetScheduleMode(scheduleMode);
                    if (scheduleMode == ScheduleMode.ParallelPart1 || scheduleMode == ScheduleMode.ParallelPart1AllowEntityAliasing)
                        return this.ScheduleParallel(IndexStrategies.Part1Count(layer.cellCount), 1, inputDeps);
                    if (scheduleMode == ScheduleMode.ParallelPart2)
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                        return this.ScheduleParallel(2, 1, inputDeps);
#else
                        return this.ScheduleParallel(1, 1, inputDeps);
#endif
                    if (scheduleMode == ScheduleMode.ParallelPart2AllowEntityAliasing)
                        return this.ScheduleParallel(1, 1, inputDeps);
                    if (scheduleMode == ScheduleMode.ParallelUnsafe)
                        return this.ScheduleParallel(IndexStrategies.JobIndicesFromSingleLayerFindPairs(layer.cellCount), 1, inputDeps);
                    return inputDeps;
                }

                void SetScheduleMode(ScheduleMode scheduleMode)
                {
                    this.scheduleMode             = scheduleMode;
                    FixedString32Bytes modeString = default;
                    if (scheduleMode == ScheduleMode.Single)
                        modeString = "Single";
                    else if (scheduleMode == ScheduleMode.ParallelPart1)
                        modeString = "ParallelPart1";
                    else if (scheduleMode == ScheduleMode.ParallelPart1AllowEntityAliasing)
                        modeString = "ParallelPart1_EntityAliasing";
                    else if (scheduleMode == ScheduleMode.ParallelPart2)
                        modeString = "ParallelPart2";
                    else if (scheduleMode == ScheduleMode.ParallelPart2AllowEntityAliasing)
                        modeString = "ParallelPart2_EntityAliasing";
                    else if (scheduleMode == ScheduleMode.ParallelUnsafe)
                        modeString = "ParallelUnsafe";

                    bool isBurst = true;
                    IsBurst(ref isBurst);
                    if (isBurst)
                    {
                        modeAndTMarker = new Unity.Profiling.ProfilerMarker($"{modeString}_{processor}");
                    }
                    else
                    {
                        FixedString128Bytes processorName = default;
                        GetProcessorNameNoBurst(ref processorName);
                        modeAndTMarker = new Unity.Profiling.ProfilerMarker($"{modeString}_{processorName}");
                    }
                }

                [BurstDiscard]
                static void IsBurst(ref bool isBurst) => isBurst = false;

                [BurstDiscard]
                static void GetProcessorNameNoBurst(ref FixedString128Bytes name)
                {
                    name = nameof(T);
                }
                #endregion

                #region Job Processing
                public void Execute(int index)
                {
                    using var jobName = modeAndTMarker.Auto();
                    if (scheduleMode == ScheduleMode.Single)
                    {
                        RunImmediate(in layer, ref processor, true);
                        return;
                    }
                    if (scheduleMode == ScheduleMode.ParallelPart1 || scheduleMode == ScheduleMode.ParallelPart1AllowEntityAliasing)
                    {
                        bool isThreadSafe = scheduleMode == ScheduleMode.ParallelPart1;
                        Physics.kCellMarker.Begin();
                        var bucket  = layer.GetBucketSlices(index);
                        var context = new FindPairsBucketContext(in layer,
                                                                 in layer,
                                                                 in bucket,
                                                                 in bucket,
                                                                 index,
                                                                 isThreadSafe,
                                                                 isThreadSafe);
                        if (processor.BeginBucket(in context))
                        {
                            if (index != IndexStrategies.CrossBucketIndex(layer.cellCount))
                                FindPairsSweepMethods.SelfSweepCell(in layer, in bucket, index, ref processor, isThreadSafe, isThreadSafe);
                            else
                                FindPairsSweepMethods.SelfSweepCross(in layer, in bucket, index, ref processor, isThreadSafe, isThreadSafe);
                            processor.EndBucket(in context);
                        }
                        Physics.kCellMarker.End();
                        return;
                    }
                    if (scheduleMode == ScheduleMode.ParallelPart2 || scheduleMode == ScheduleMode.ParallelPart2AllowEntityAliasing)
                    {
                        if (index == 1)
                        {
                            EntityAliasCheck(in layer);
                            return;
                        }

                        bool isThreadSafe = scheduleMode == ScheduleMode.ParallelPart2;
                        Physics.kCrossMarker.Begin();
                        var crossBucket = layer.GetBucketSlices(IndexStrategies.CrossBucketIndex(layer.cellCount));
                        for (int i = 0; i < IndexStrategies.SingleLayerPart2Count(layer.cellCount); i++)
                        {
                            var bucket   = layer.GetBucketSlices(i);
                            var jobIndex = IndexStrategies.Part1Count(layer.cellCount) + i;
                            var context  = new FindPairsBucketContext(in layer,
                                                                      in layer,
                                                                      in bucket,
                                                                      in crossBucket,
                                                                      jobIndex,
                                                                      isThreadSafe,
                                                                      isThreadSafe);
                            if (processor.BeginBucket(in context))
                            {
                                FindPairsSweepMethods.BipartiteSweepCellCross(in layer, in layer, bucket, crossBucket, jobIndex, ref processor, isThreadSafe, isThreadSafe);
                                processor.EndBucket(in context);
                            }
                        }
                        Physics.kCrossMarker.End();
                        return;
                    }
                    if (scheduleMode == ScheduleMode.ParallelUnsafe)
                    {
                        if (index < IndexStrategies.Part1Count(layer.cellCount))
                        {
                            Physics.kCellMarker.Begin();
                            var bucket  = layer.GetBucketSlices(index);
                            var context = new FindPairsBucketContext(in layer,
                                                                     in layer,
                                                                     in bucket,
                                                                     in bucket,
                                                                     index,
                                                                     false,
                                                                     false);
                            if (processor.BeginBucket(in context))
                            {
                                if (index != layer.bucketCount - 1)
                                    FindPairsSweepMethods.SelfSweepCell(in layer, in bucket, index, ref processor, false, false);
                                else
                                    FindPairsSweepMethods.SelfSweepCross(in layer, in bucket, index, ref processor, false, false);
                                processor.EndBucket(in context);
                            }
                            Physics.kCellMarker.End();
                        }
                        else
                        {
                            Physics.kCrossMarker.Begin();
                            var i           = index - layer.bucketCount;
                            var bucket      = layer.GetBucketSlices(i);
                            var crossBucket = layer.GetBucketSlices(IndexStrategies.CrossBucketIndex(layer.cellCount));
                            var jobIndex    = IndexStrategies.Part1Count(layer.cellCount) + i;
                            var context     = new FindPairsBucketContext(in layer,
                                                                         in layer,
                                                                         in bucket,
                                                                         in crossBucket,
                                                                         jobIndex,
                                                                         false,
                                                                         false);
                            if (processor.BeginBucket(in context))
                            {
                                FindPairsSweepMethods.BipartiteSweepCellCross(layer, layer, bucket, crossBucket, jobIndex, ref processor, false, false);
                                processor.EndBucket(in context);
                            }
                            Physics.kCrossMarker.End();
                        }
                    }
                }

                [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
                private static void EntityAliasCheck(in CollisionLayer layer)
                {
                    var hashSet = new NativeParallelHashSet<Entity>(layer.count, Allocator.Temp);
                    for (int i = 0; i < layer.count; i++)
                    {
                        if (!hashSet.Add(layer.bodies[i].entity))
                        {
                            var entity = layer.bodies[i].entity;
                            throw new InvalidOperationException(
                                $"A parallel FindPairs job was scheduled using a layer containing more than one instance of Entity {entity}");
                        }
                    }
                }
                #endregion

                [Preserve]
                void RequireEarlyJobInit()
                {
                    new InitJobsForProcessors.FindPairsIniter<T>().Init();
                }
            }

            public static void RunImmediate(in CollisionLayer layer, ref T processor, bool isThreadSafe)
            {
                int jobIndex = 0;
                for (int i = 0; i < IndexStrategies.Part1Count(layer.cellCount); i++)
                {
                    var bucket  = layer.GetBucketSlices(i);
                    var context = new FindPairsBucketContext(in layer,
                                                             in layer,
                                                             in bucket,
                                                             in bucket,
                                                             jobIndex,
                                                             isThreadSafe,
                                                             isThreadSafe,
                                                             !isThreadSafe);
                    if (processor.BeginBucket(in context))
                    {
                        if (i != layer.bucketCount - 1)
                            FindPairsSweepMethods.SelfSweepCell(in layer, in bucket, jobIndex, ref processor, isThreadSafe, isThreadSafe, !isThreadSafe);
                        else
                            FindPairsSweepMethods.SelfSweepCross(in layer, in bucket, jobIndex, ref processor, isThreadSafe, isThreadSafe, !isThreadSafe);
                        processor.EndBucket(in context);
                    }
                    jobIndex++;
                }

                if (IndexStrategies.ScheduleParallelShouldActuallyBeSingle(layer.cellCount))
                    return;

                var crossBucket = layer.GetBucketSlices(IndexStrategies.CrossBucketIndex(layer.cellCount));
                for (int i = 0; i < IndexStrategies.SingleLayerPart2Count(layer.cellCount); i++)
                {
                    var bucket  = layer.GetBucketSlices(i);
                    var context = new FindPairsBucketContext(in layer,
                                                             in layer,
                                                             in bucket,
                                                             in crossBucket,
                                                             jobIndex,
                                                             isThreadSafe,
                                                             isThreadSafe,
                                                             !isThreadSafe);
                    if (processor.BeginBucket(in context))
                    {
                        FindPairsSweepMethods.BipartiteSweepCellCross(in layer,
                                                                      in layer,
                                                                      in bucket,
                                                                      in crossBucket,
                                                                      jobIndex,
                                                                      ref processor,
                                                                      isThreadSafe,
                                                                      isThreadSafe,
                                                                      !isThreadSafe);
                        processor.EndBucket(in context);
                    }
                    jobIndex++;
                }
            }
        }
    }

    public partial struct FindPairsLayerLayerConfig<T> where T : struct, IFindPairsProcessor
    {
        internal enum ScheduleMode
        {
            Single = 0x0,
            ParallelPart1 = 0x1,
            ParallelPart2 = 0x2,
            ParallelByA = 0x3,
            ParallelUnsafe = 0x4,
            UseCrossCache = 0x40,
            AllowEntityAliasing = 0x80,
        }

        internal static class FindPairsInternal
        {
            [BurstCompile]
            public struct LayerLayerJob : IJobFor
            {
                [ReadOnly] CollisionLayer      layerA;
                [ReadOnly] CollisionLayer      layerB;
                T                              processor;
                UnsafeIndexedBlockList         blockList;
                ScheduleMode                   m_scheduleMode;
                Unity.Profiling.ProfilerMarker modeAndTMarker;

                #region Construction and Scheduling
                public LayerLayerJob(in CollisionLayer layerA, in CollisionLayer layerB, in T processor)
                {
                    this.layerA    = layerA;
                    this.layerB    = layerB;
                    this.processor = processor;
                    blockList      = default;
                    m_scheduleMode = default;
                    modeAndTMarker = default;
                }

                public void Run()
                {
                    SetScheduleMode(ScheduleMode.Single);
                    this.Run(1);
                }

                public JobHandle ScheduleSingle(JobHandle inputDeps)
                {
                    SetScheduleMode(ScheduleMode.Single);
                    return this.Schedule(1, inputDeps);
                }

                public JobHandle ScheduleParallel(JobHandle inputDeps, ScheduleMode scheduleMode)
                {
                    SetScheduleMode(scheduleMode);
                    scheduleMode = ExtractEnum(scheduleMode, out var useCrossCache, out var allowEntityAliasing);

                    var part1Count = IndexStrategies.Part1Count(layerA.cellCount);

                    if (scheduleMode == ScheduleMode.ParallelPart1)
                    {
                        return this.ScheduleParallel(part1Count, 1, inputDeps);
                    }
                    if (scheduleMode == ScheduleMode.ParallelPart2 && allowEntityAliasing)
                        return this.ScheduleParallel(2, 1, inputDeps);
                    if (scheduleMode == ScheduleMode.ParallelPart2)
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                        return this.ScheduleParallel(3, 1, inputDeps);
#else
                        return this.ScheduleParallel(2, 1, inputDeps);
#endif
                    if (scheduleMode == ScheduleMode.ParallelByA)
                    {
                        if (useCrossCache)
                        {
                            var crossCount = IndexStrategies.ParallelByACrossCount(layerA.cellCount);
                            blockList      = new UnsafeIndexedBlockList(8, 1024, crossCount, Allocator.TempJob);
                            inputDeps      = new FindPairsParallelByACrossCacheJob { layerA = layerA, layerB = layerB, cache = blockList }.ScheduleParallel(crossCount,
                                                                                                                                                            1,
                                                                                                                                                            inputDeps);
                            scheduleMode |= ScheduleMode.UseCrossCache;
                        }

                        if (allowEntityAliasing)
                            inputDeps = this.ScheduleParallel(part1Count, 1, inputDeps);
                        else
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                            inputDeps = this.ScheduleParallel(part1Count + 1, 1, inputDeps);
#else
                            inputDeps = this.ScheduleParallel(part1Count, 1, inputDeps);
#endif
                        if (useCrossCache)
                            return blockList.Dispose(inputDeps);
                        return inputDeps;
                    }
                    if (scheduleMode == ScheduleMode.ParallelUnsafe)
                        return this.ScheduleParallel(IndexStrategies.JobIndicesFromDualLayerFindPairs(layerA.cellCount), 1, inputDeps);
                    return inputDeps;
                }

                void SetScheduleMode(ScheduleMode scheduleMode)
                {
                    m_scheduleMode                = scheduleMode;
                    scheduleMode                  = ExtractEnum(scheduleMode, out var useCrossCache, out var allowEntityAliasing);
                    FixedString64Bytes modeString = default;
                    if (scheduleMode == ScheduleMode.Single)
                        modeString = "Single";
                    else if (scheduleMode == ScheduleMode.ParallelPart1)
                        modeString = "ParallelPart1";
                    else if (scheduleMode == ScheduleMode.ParallelPart2)
                        modeString = "ParallelPart2";
                    else if (scheduleMode == ScheduleMode.ParallelByA)
                        modeString = "ParallelByA";
                    else if (scheduleMode == ScheduleMode.ParallelUnsafe)
                        modeString = "ParallelUnsafe";

                    FixedString32Bytes appendString;
                    if (useCrossCache)
                    {
                        appendString = "_CrossCache";
                        modeString.Append(appendString);
                    }
                    if (allowEntityAliasing)
                    {
                        appendString = "_EntityAliasing";
                        modeString.Append(appendString);
                    }

                    bool isBurst = true;
                    IsBurst(ref isBurst);
                    if (isBurst)
                    {
                        modeAndTMarker = new Unity.Profiling.ProfilerMarker($"{modeString}_{processor}");
                    }
                    else
                    {
                        FixedString128Bytes processorName = default;
                        GetProcessorNameNoBurst(ref processorName);
                        modeAndTMarker = new Unity.Profiling.ProfilerMarker($"{modeString}_{processorName}");
                    }
                }

                ScheduleMode ExtractEnum(ScheduleMode mode, out bool useCrossCache, out bool allowEntityAliasing)
                {
                    allowEntityAliasing = (mode & ScheduleMode.AllowEntityAliasing) == ScheduleMode.AllowEntityAliasing;
                    useCrossCache       = (mode & ScheduleMode.UseCrossCache) == ScheduleMode.UseCrossCache;
                    return mode & ~(ScheduleMode.AllowEntityAliasing | ScheduleMode.UseCrossCache);
                }

                [BurstDiscard]
                static void IsBurst(ref bool isBurst) => isBurst = false;

                [BurstDiscard]
                static void GetProcessorNameNoBurst(ref FixedString128Bytes name)
                {
                    name = nameof(T);
                }
                #endregion

                #region Job Processing
                public void Execute(int index)
                {
                    using var jobName      = modeAndTMarker.Auto();
                    var       scheduleMode = ExtractEnum(m_scheduleMode, out var useCrossCache, out var allowEntityAliasing);
                    if (scheduleMode == ScheduleMode.Single)
                    {
                        RunImmediate(in layerA, layerB, ref processor, true);
                        return;
                    }
                    if (scheduleMode == ScheduleMode.ParallelPart1)
                    {
                        bool isThreadSafe = !allowEntityAliasing;
                        Physics.kCellMarker.Begin();
                        var bucketA = layerA.GetBucketSlices(index);
                        var bucketB = layerB.GetBucketSlices(index);
                        var context = new FindPairsBucketContext(in layerA,
                                                                 in layerB,
                                                                 in bucketA,
                                                                 in bucketB,
                                                                 index,
                                                                 isThreadSafe,
                                                                 isThreadSafe);
                        if (processor.BeginBucket(in context))
                        {
                            if (index != IndexStrategies.CrossBucketIndex(layerA.cellCount))
                                FindPairsSweepMethods.BipartiteSweepCellCell(in layerA, in layerB, in bucketA, in bucketB, index, ref processor, isThreadSafe, isThreadSafe);
                            else
                                FindPairsSweepMethods.BipartiteSweepCrossCross(in layerA, in layerB, in bucketA, in bucketB, index, ref processor, isThreadSafe, isThreadSafe);
                            processor.EndBucket(in context);
                        }
                        Physics.kCellMarker.End();
                        return;
                    }
                    if (scheduleMode == ScheduleMode.ParallelPart2)
                    {
                        if (index == 2)
                        {
                            EntityAliasCheck(in layerA, in layerB);
                            return;
                        }

                        bool isThreadSafe = !allowEntityAliasing;
                        Physics.kCrossMarker.Begin();
                        if (index == 0)
                        {
                            var crossBucket = layerA.GetBucketSlices(IndexStrategies.CrossBucketIndex(layerA.cellCount));
                            for (int i = 0; i < IndexStrategies.ParallelPart2ACount(layerA.cellCount); i++)
                            {
                                var bucket   = layerB.GetBucketSlices(i);
                                var jobIndex = IndexStrategies.Part1Count(layerA.cellCount) + i;
                                var context  = new FindPairsBucketContext(in layerA,
                                                                          in layerB,
                                                                          in crossBucket,
                                                                          in bucket,
                                                                          jobIndex,
                                                                          isThreadSafe,
                                                                          isThreadSafe);
                                if (processor.BeginBucket(in context))
                                {
                                    FindPairsSweepMethods.BipartiteSweepCrossCell(layerA, layerB, crossBucket, bucket, jobIndex, ref processor, isThreadSafe,
                                                                                  isThreadSafe);
                                    processor.EndBucket(in context);
                                }
                            }
                        }
                        else if (index == 1)
                        {
                            var crossBucket = layerB.GetBucketSlices(IndexStrategies.CrossBucketIndex(layerA.cellCount));
                            for (int i = 0; i < IndexStrategies.ParallelPart2BCount(layerA.cellCount); i++)
                            {
                                var bucket   = layerA.GetBucketSlices(i);
                                var jobIndex = IndexStrategies.Part1Count(layerA.cellCount) + IndexStrategies.ParallelPart2ACount(layerA.cellCount) + i;
                                var context  = new FindPairsBucketContext(in layerA,
                                                                          in layerB,
                                                                          in bucket,
                                                                          in crossBucket,
                                                                          jobIndex,
                                                                          isThreadSafe,
                                                                          isThreadSafe);
                                if (processor.BeginBucket(in context))
                                {
                                    FindPairsSweepMethods.BipartiteSweepCellCross(layerA,
                                                                                  layerB,
                                                                                  bucket,
                                                                                  crossBucket,
                                                                                  jobIndex,
                                                                                  ref processor,
                                                                                  isThreadSafe,
                                                                                  isThreadSafe);
                                    processor.EndBucket(in context);
                                }
                            }
                        }
                        Physics.kCrossMarker.End();
                        return;
                    }
                    if (scheduleMode == ScheduleMode.ParallelByA)
                    {
                        if (index == IndexStrategies.Part1Count(layerA.cellCount))
                        {
                            EntityAliasCheck(in layerA, in layerB);
                            return;
                        }

                        int crossBucketIndex = IndexStrategies.CrossBucketIndex(layerA.cellCount);

                        bool isThreadSafe = !allowEntityAliasing;
                        Physics.kCellMarker.Begin();
                        var bucketA = layerA.GetBucketSlices(index);
                        var bucketB = layerB.GetBucketSlices(index);
                        var context = new FindPairsBucketContext(in layerA,
                                                                 in layerB,
                                                                 in bucketA,
                                                                 in bucketB,
                                                                 index,
                                                                 isThreadSafe,
                                                                 false);
                        if (processor.BeginBucket(in context))
                        {
                            if (index != crossBucketIndex)
                                FindPairsSweepMethods.BipartiteSweepCellCell(in layerA, in layerB, in bucketA, in bucketB, index, ref processor, isThreadSafe, false);
                            else
                                FindPairsSweepMethods.BipartiteSweepCrossCross(in layerA, in layerB, in bucketA, in bucketB, index, ref processor, isThreadSafe, false);
                            processor.EndBucket(in context);
                        }
                        Physics.kCellMarker.End();

                        Physics.kCrossMarker.Begin();
                        if (index != crossBucketIndex)
                        {
                            var crossBucket = layerB.GetBucketSlices(crossBucketIndex);
                            var jobIndex    = IndexStrategies.Part1Count(layerA.cellCount) + IndexStrategies.ParallelPart2ACount(layerA.cellCount) + index;
                            context         = new FindPairsBucketContext(in layerA,
                                                                         in layerB,
                                                                         in bucketA,
                                                                         in crossBucket,
                                                                         jobIndex,
                                                                         isThreadSafe,
                                                                         false);
                            if (processor.BeginBucket(in context))
                            {
                                FindPairsSweepMethods.BipartiteSweepCellCross(in layerA,
                                                                              in layerB,
                                                                              in bucketA,
                                                                              in crossBucket,
                                                                              jobIndex,
                                                                              ref processor,
                                                                              isThreadSafe,
                                                                              false);
                                processor.EndBucket(in context);
                            }
                        }
                        else if (useCrossCache)
                        {
                            var crossBucket = layerA.GetBucketSlices(crossBucketIndex);
                            for (int i = 0; i < IndexStrategies.ParallelByACrossCount(layerA.cellCount); i++)
                            {
                                var bucket   = layerB.GetBucketSlices(i);
                                var jobIndex = IndexStrategies.Part1Count(layerA.cellCount) + i;
                                context      = new FindPairsBucketContext(in layerA,
                                                                          in layerB,
                                                                          in crossBucket,
                                                                          in bucket,
                                                                          jobIndex,
                                                                          isThreadSafe,
                                                                          isThreadSafe);
                                if (processor.BeginBucket(in context))
                                {
                                    var enumerator = blockList.GetEnumerator(i);
                                    //var tempMarker = new ProfilerMarker($"Cache_{i}");
                                    //tempMarker.Begin();
                                    var count = FindPairsSweepMethods.BipartiteSweepPlayCache(enumerator,
                                                                                              in layerA,
                                                                                              in layerB,
                                                                                              crossBucket.bucketIndex,
                                                                                              bucket.bucketIndex,
                                                                                              jobIndex,
                                                                                              ref processor,
                                                                                              isThreadSafe,
                                                                                              false);
                                    //tempMarker.End();
                                    processor.EndBucket(in context);
                                }

                                //if (count > 0)
                                //    UnityEngine.Debug.Log($"Bucket cache had count {count}");
                            }
                        }
                        else
                        {
                            var crossBucket = layerA.GetBucketSlices(crossBucketIndex);
                            for (int i = 0; i < IndexStrategies.ParallelByACrossCount(layerA.cellCount); i++)
                            {
                                var bucket   = layerB.GetBucketSlices(i);
                                var jobIndex = IndexStrategies.Part1Count(layerA.cellCount) + i;
                                context      = new FindPairsBucketContext(in layerA,
                                                                          in layerB,
                                                                          in crossBucket,
                                                                          in bucket,
                                                                          jobIndex,
                                                                          isThreadSafe,
                                                                          isThreadSafe);
                                if (processor.BeginBucket(in context))
                                {
                                    FindPairsSweepMethods.BipartiteSweepCrossCell(layerA, layerB, crossBucket, bucket, jobIndex, ref processor, isThreadSafe, false);
                                    processor.EndBucket(in context);
                                }
                            }
                        }
                        Physics.kCrossMarker.End();
                        return;
                    }
                    if (scheduleMode == ScheduleMode.ParallelUnsafe)
                    {
                        int crossBucketIndex = IndexStrategies.CrossBucketIndex(layerA.cellCount);
                        var part1Count       = IndexStrategies.Part1Count(layerA.cellCount);
                        if (index < part1Count)
                        {
                            Physics.kCellMarker.Begin();
                            var bucketA = layerA.GetBucketSlices(index);
                            var bucketB = layerB.GetBucketSlices(index);
                            var context = new FindPairsBucketContext(in layerA,
                                                                     in layerB,
                                                                     in bucketA,
                                                                     in bucketB,
                                                                     index,
                                                                     false,
                                                                     false);
                            if (processor.BeginBucket(in context))
                            {
                                if (index != crossBucketIndex)
                                    FindPairsSweepMethods.BipartiteSweepCellCell(in layerA, in layerB, in bucketA, in bucketB, index, ref processor, false, false);
                                else
                                    FindPairsSweepMethods.BipartiteSweepCrossCross(in layerA, in layerB, in bucketA, in bucketB, index, ref processor, false, false);
                                processor.EndBucket(in context);
                            }
                            Physics.kCellMarker.End();
                        }
                        else if (index < part1Count + IndexStrategies.ParallelPart2ACount(layerA.cellCount))
                        {
                            Physics.kCrossMarker.Begin();
                            var bucket      = layerB.GetBucketSlices(index - part1Count);
                            var crossBucket = layerA.GetBucketSlices(layerA.bucketCount - 1);
                            var context     = new FindPairsBucketContext(in layerA,
                                                                         in layerB,
                                                                         in crossBucket,
                                                                         in bucket,
                                                                         index,
                                                                         false,
                                                                         false);
                            if (processor.BeginBucket(in context))
                            {
                                FindPairsSweepMethods.BipartiteSweepCrossCell(in layerA,
                                                                              in layerB,
                                                                              in crossBucket,
                                                                              in bucket,
                                                                              index,
                                                                              ref processor,
                                                                              false,
                                                                              false);
                                processor.EndBucket(in context);
                            }
                            Physics.kCrossMarker.End();
                        }
                        else
                        {
                            Physics.kCrossMarker.Begin();
                            var bucket      = layerA.GetBucketSlices(index - part1Count - IndexStrategies.ParallelPart2ACount(layerA.cellCount));
                            var crossBucket = layerB.GetBucketSlices(layerB.bucketCount - 1);
                            var context     = new FindPairsBucketContext(in layerA,
                                                                         in layerB,
                                                                         in bucket,
                                                                         in crossBucket,
                                                                         index,
                                                                         false,
                                                                         false);
                            if (processor.BeginBucket(in context))
                            {
                                FindPairsSweepMethods.BipartiteSweepCellCross(in layerA, in layerB, in bucket, in crossBucket, index, ref processor, false, false);
                                processor.EndBucket(in context);
                            }
                            Physics.kCrossMarker.End();
                        }
                        return;
                    }
                }

                [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
                private static void EntityAliasCheck(in CollisionLayer layerA, in CollisionLayer layerB)
                {
                    var hashSet = new NativeParallelHashSet<Entity>(layerA.count + layerB.count, Allocator.Temp);
                    for (int i = 0; i < layerA.count; i++)
                    {
                        if (!hashSet.Add(layerA.bodies[i].entity))
                        {
                            //Note: At this point, we know the issue lies exclusively in layerA.
                            var entity = layerA.bodies[i].entity;
                            throw new InvalidOperationException(
                                $"A parallel FindPairs job was scheduled using a layer containing more than one instance of Entity {entity}");
                        }
                    }
                    for (int i = 0; i < layerB.count; i++)
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
                #endregion

                [Preserve]
                void RequireEarlyJobInit()
                {
                    new InitJobsForProcessors.FindPairsIniter<T>().Init();
                }
            }

            public static void RunImmediate(in CollisionLayer layerA, in CollisionLayer layerB, ref T processor, bool isThreadSafe)
            {
                int jobIndex         = 0;
                var crossBucketIndex = IndexStrategies.CrossBucketIndex(layerA.cellCount);
                for (int i = 0; i < IndexStrategies.Part1Count(layerA.cellCount); i++)
                {
                    var bucketA = layerA.GetBucketSlices(i);
                    var bucketB = layerB.GetBucketSlices(i);
                    var context = new FindPairsBucketContext(in layerA,
                                                             in layerB,
                                                             in bucketA,
                                                             in bucketB,
                                                             jobIndex,
                                                             isThreadSafe,
                                                             isThreadSafe,
                                                             !isThreadSafe);
                    if (processor.BeginBucket(in context))
                    {
                        if (i != crossBucketIndex)
                            FindPairsSweepMethods.BipartiteSweepCellCell(in layerA,
                                                                         in layerB,
                                                                         in bucketA,
                                                                         in bucketB,
                                                                         jobIndex,
                                                                         ref processor,
                                                                         isThreadSafe,
                                                                         isThreadSafe,
                                                                         !isThreadSafe);
                        else
                            FindPairsSweepMethods.BipartiteSweepCrossCross(in layerA,
                                                                           in layerB,
                                                                           in bucketA,
                                                                           in bucketB,
                                                                           jobIndex,
                                                                           ref processor,
                                                                           isThreadSafe,
                                                                           isThreadSafe,
                                                                           !isThreadSafe);
                        processor.EndBucket(in context);
                    }
                    jobIndex++;
                }

                if (IndexStrategies.ScheduleParallelShouldActuallyBeSingle(layerA.cellCount))
                    return;

                var crossBucketA = layerA.GetBucketSlices(crossBucketIndex);
                for (int i = 0; i < IndexStrategies.ParallelPart2ACount(layerA.cellCount); i++)
                {
                    var bucket  = layerB.GetBucketSlices(i);
                    var context = new FindPairsBucketContext(in layerA,
                                                             in layerB,
                                                             in crossBucketA,
                                                             in bucket,
                                                             jobIndex,
                                                             isThreadSafe,
                                                             isThreadSafe,
                                                             !isThreadSafe);
                    if (processor.BeginBucket(in context))
                    {
                        FindPairsSweepMethods.BipartiteSweepCrossCell(in layerA,
                                                                      in layerB,
                                                                      in crossBucketA,
                                                                      in bucket,
                                                                      jobIndex,
                                                                      ref processor,
                                                                      isThreadSafe,
                                                                      isThreadSafe,
                                                                      !isThreadSafe);
                        processor.EndBucket(in context);
                    }
                    jobIndex++;
                }

                var crossBucketB = layerB.GetBucketSlices(crossBucketIndex);
                for (int i = 0; i < IndexStrategies.ParallelPart2BCount(layerA.cellCount); i++)
                {
                    var bucket  = layerA.GetBucketSlices(i);
                    var context = new FindPairsBucketContext(in layerA,
                                                             in layerB,
                                                             in bucket,
                                                             in crossBucketB,
                                                             jobIndex,
                                                             isThreadSafe,
                                                             isThreadSafe,
                                                             !isThreadSafe);
                    if (processor.BeginBucket(in context))
                    {
                        FindPairsSweepMethods.BipartiteSweepCellCross(in layerA,
                                                                      in layerB,
                                                                      in bucket,
                                                                      in crossBucketB,
                                                                      jobIndex,
                                                                      ref processor,
                                                                      isThreadSafe,
                                                                      isThreadSafe,
                                                                      !isThreadSafe);
                        processor.EndBucket(in context);
                    }
                    jobIndex++;
                }
            }
        }
    }

    [BurstCompile]
    internal struct FindPairsParallelByACrossCacheJob : IJobFor
    {
        [ReadOnly] public CollisionLayer layerA;
        [ReadOnly] public CollisionLayer layerB;
        public UnsafeIndexedBlockList    cache;

        public void Execute(int index)
        {
            var a      = layerA.GetBucketSlices(IndexStrategies.CrossBucketIndex(layerA.cellCount));
            var b      = layerB.GetBucketSlices(index);
            var cacher = new Cacher { cache = cache, writeIndex = index };
            FindPairsSweepMethods.BipartiteSweepCrossCell(in layerA, in layerB, in a, in b, index, ref cacher, false, false);
        }

        struct Cacher : IFindPairsProcessor
        {
            public UnsafeIndexedBlockList cache;
            public int                    writeIndex;

            public void Execute(in FindPairsResult result)
            {
                cache.Write(new int2(result.bodyIndexA, result.bodyIndexB), writeIndex);
            }
        }
    }
}

