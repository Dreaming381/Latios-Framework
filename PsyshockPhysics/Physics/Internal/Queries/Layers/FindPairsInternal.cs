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
    public partial struct FindPairsLayerSelfConfig<T> where T : unmanaged, IFindPairsProcessor
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
                    this.RunByRef(1);
                }

                public JobHandle ScheduleSingle(JobHandle inputDeps)
                {
                    SetScheduleMode(ScheduleMode.Single);
                    return this.ScheduleByRef(1, inputDeps);
                }

                public JobHandle ScheduleParallel(JobHandle inputDeps, ScheduleMode scheduleMode)
                {
                    SetScheduleMode(scheduleMode);
                    if (scheduleMode == ScheduleMode.ParallelPart1 || scheduleMode == ScheduleMode.ParallelPart1AllowEntityAliasing)
                        return this.ScheduleParallelByRef(IndexStrategies.Part1Count(layer.cellCount), 1, inputDeps);
                    if (scheduleMode == ScheduleMode.ParallelPart2)
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                        return this.ScheduleParallelByRef(2, 1, inputDeps);
#else
                        return this.ScheduleParallelByRef(1, 1, inputDeps);
#endif
                    if (scheduleMode == ScheduleMode.ParallelPart2AllowEntityAliasing)
                        return this.ScheduleParallelByRef(1, 1, inputDeps);
                    if (scheduleMode == ScheduleMode.ParallelUnsafe)
                        return this.ScheduleParallelByRef(IndexStrategies.JobIndicesFromSingleLayerFindPairs(layer.cellCount), 1, inputDeps);
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
                    name = typeof(T).FullName;
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
                            FindPairsSweepMethods.SelfSweep(in layer, in bucket, index, ref processor, isThreadSafe);
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
                                FindPairsSweepMethods.SelfSweep(in layer, in bucket, index, ref processor, false);
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
                                $"A parallel FindPairs job was scheduled using a layer containing more than one instance of Entity {entity.ToFixedString()}");
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
                        FindPairsSweepMethods.SelfSweep(in layer, in bucket, jobIndex, ref processor, isThreadSafe, !isThreadSafe);
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

    public partial struct FindPairsWorldSelfConfig<T> where T : unmanaged, IFindPairsProcessor
    {
        internal enum ScheduleMode
        {
            Single,
            ParallelPart1,
            ParallelPart2,
            ParallelUnsafe
        }

        internal static class FindPairsInternal
        {
            [BurstCompile]
            public struct WorldSelfJob : IJobFor
            {
                [ReadOnly] CollisionWorld      world;
                EntityQueryMask                queryMaskA;
                EntityQueryMask                queryMaskB;
                CollisionWorld.Mask            maskA;
                CollisionWorld.Mask            maskB;
                bool                           usesBothMasks;
                T                              processor;
                ScheduleMode                   scheduleMode;
                Unity.Profiling.ProfilerMarker modeAndTMarker;

                #region Construction and Scheduling
                public WorldSelfJob(in CollisionWorld world, in EntityQueryMask queryMask, in T processor)
                {
                    this.world     = world;
                    queryMaskA     = queryMask;
                    queryMaskB     = default;
                    maskA          = default;
                    maskB          = default;
                    usesBothMasks  = false;
                    this.processor = processor;
                    scheduleMode   = default;
                    modeAndTMarker = default;
                }

                public WorldSelfJob(in CollisionWorld world, in EntityQueryMask queryMaskA, in EntityQueryMask queryMaskB, in T processor)
                {
                    this.world      = world;
                    this.queryMaskA = queryMaskA;
                    this.queryMaskB = queryMaskB;
                    maskA           = default;
                    maskB           = default;
                    usesBothMasks   = true;
                    this.processor  = processor;
                    scheduleMode    = default;
                    modeAndTMarker  = default;
                }

                public int cellCount => world.layer.cellCount;

                public void RunImmediate()
                {
                    if (usesBothMasks)
                        FindPairsInternal.RunImmediate(in world, world.CreateMask(queryMaskA), world.CreateMask(queryMaskB), ref processor, false);
                    else
                        FindPairsInternal.RunImmediate(in world, world.CreateMask(queryMaskA), ref processor, false);
                }

                public void Run()
                {
                    SetScheduleMode(ScheduleMode.Single);
                    this.RunByRef(1);
                }

                public JobHandle ScheduleSingle(JobHandle inputDeps)
                {
                    SetScheduleMode(ScheduleMode.Single);
                    return this.ScheduleByRef(1, inputDeps);
                }

                public JobHandle ScheduleParallel(JobHandle inputDeps, ScheduleMode scheduleMode)
                {
                    SetScheduleMode(scheduleMode);
                    if (scheduleMode == ScheduleMode.ParallelPart1)
                        return this.ScheduleParallelByRef(IndexStrategies.Part1Count(world.layer.cellCount), 1, inputDeps);
                    if (scheduleMode == ScheduleMode.ParallelPart2)
                        return this.ScheduleParallelByRef(1, 1, inputDeps);
                    if (scheduleMode == ScheduleMode.ParallelUnsafe)
                        return this.ScheduleParallelByRef(IndexStrategies.JobIndicesFromSingleLayerFindPairs(world.layer.cellCount), 1, inputDeps);
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
                    else if (scheduleMode == ScheduleMode.ParallelPart2)
                        modeString = "ParallelPart2";
                    else if (scheduleMode == ScheduleMode.ParallelUnsafe)
                        modeString = "ParallelUnsafe";
                    FixedString32Bytes maskString;
                    if (usesBothMasks)
                        maskString = "DualMasks";
                    else
                        maskString = "Mask";

                    bool isBurst = true;
                    IsBurst(ref isBurst);
                    if (isBurst)
                    {
                        modeAndTMarker = new Unity.Profiling.ProfilerMarker($"{modeString}_{maskString}_{processor}");
                    }
                    else
                    {
                        FixedString128Bytes processorName = default;
                        GetProcessorNameNoBurst(ref processorName);
                        modeAndTMarker = new Unity.Profiling.ProfilerMarker($"{modeString}_{maskString}_{processorName}");
                    }
                }

                [BurstDiscard]
                static void IsBurst(ref bool isBurst) => isBurst = false;

                [BurstDiscard]
                static void GetProcessorNameNoBurst(ref FixedString128Bytes name)
                {
                    name = typeof(T).FullName;
                }
                #endregion

                #region Job Processing
                public void Execute(int index)
                {
                    using var jobName = modeAndTMarker.Auto();

                    if (!maskA.isCreated)
                    {
                        maskA = world.CreateMask(queryMaskA);
                        if (usesBothMasks)
                            maskB = world.CreateMask(queryMaskB);
                    }

                    if (scheduleMode == ScheduleMode.Single)
                    {
                        if (usesBothMasks)
                            FindPairsInternal.RunImmediate(in world, in maskA, in maskB, ref processor, true);
                        else
                            FindPairsInternal.RunImmediate(in world, in maskA, ref processor, true);
                        return;
                    }
                    if (scheduleMode == ScheduleMode.ParallelPart1)
                    {
                        Physics.kCellMarker.Begin();
                        var bucket  = world.GetBucket(index);
                        var context = new FindPairsBucketContext(in world.layer,
                                                                 in world.layer,
                                                                 in bucket.slices,
                                                                 in bucket.slices,
                                                                 index,
                                                                 true,
                                                                 true);
                        if (processor.BeginBucket(in context))
                        {
                            if (usesBothMasks)
                                FindPairsSweepMethods.SelfSweep(in world.layer, in bucket, in maskA, in maskB, index, ref processor, true);
                            else
                                FindPairsSweepMethods.SelfSweep(in world.layer, in bucket, in maskA, index, ref processor, true);

                            processor.EndBucket(in context);
                        }
                        Physics.kCellMarker.End();
                        return;
                    }
                    if (scheduleMode == ScheduleMode.ParallelPart2)
                    {
                        Physics.kCrossMarker.Begin();
                        var crossBucket = world.GetBucket(IndexStrategies.CrossBucketIndex(world.layer.cellCount));
                        for (int i = 0; i < IndexStrategies.SingleLayerPart2Count(world.layer.cellCount); i++)
                        {
                            var bucket   = world.GetBucket(i);
                            var jobIndex = IndexStrategies.Part1Count(world.layer.cellCount) + i;
                            var context  = new FindPairsBucketContext(in world.layer,
                                                                      in world.layer,
                                                                      in bucket.slices,
                                                                      in crossBucket.slices,
                                                                      jobIndex,
                                                                      true,
                                                                      true);
                            if (processor.BeginBucket(in context))
                            {
                                if (usesBothMasks)
                                {
                                    FindPairsSweepMethods.BipartiteSweepCellCross(in world.layer,
                                                                                  in world.layer,
                                                                                  in bucket,
                                                                                  in crossBucket,
                                                                                  in maskA,
                                                                                  in maskB,
                                                                                  jobIndex,
                                                                                  ref processor,
                                                                                  true,
                                                                                  true);
                                    FindPairsSweepMethods.BipartiteSweepCrossCell(in world.layer,
                                                                                  in world.layer,
                                                                                  in crossBucket,
                                                                                  in bucket,
                                                                                  in maskA,
                                                                                  in maskB,
                                                                                  jobIndex,
                                                                                  ref processor,
                                                                                  true,
                                                                                  true);
                                }
                                else
                                    FindPairsSweepMethods.BipartiteSweepCellCross(in world.layer,
                                                                                  in world.layer,
                                                                                  in bucket,
                                                                                  in crossBucket,
                                                                                  in maskA,
                                                                                  in maskA,
                                                                                  jobIndex,
                                                                                  ref processor,
                                                                                  true,
                                                                                  true);

                                processor.EndBucket(in context);
                            }
                        }
                        Physics.kCrossMarker.End();
                        return;
                    }
                    if (scheduleMode == ScheduleMode.ParallelUnsafe)
                    {
                        if (index < IndexStrategies.Part1Count(world.layer.cellCount))
                        {
                            Physics.kCellMarker.Begin();
                            var bucket  = world.GetBucket(index);
                            var context = new FindPairsBucketContext(in world.layer,
                                                                     in world.layer,
                                                                     in bucket.slices,
                                                                     in bucket.slices,
                                                                     index,
                                                                     false,
                                                                     false);
                            if (processor.BeginBucket(in context))
                            {
                                if (usesBothMasks)
                                    FindPairsSweepMethods.SelfSweep(in world.layer, in bucket, in maskA, in maskB, index, ref processor, false);
                                else
                                    FindPairsSweepMethods.SelfSweep(in world.layer, in bucket, in maskA, index, ref processor, false);
                                processor.EndBucket(in context);
                            }
                            Physics.kCellMarker.End();
                        }
                        else
                        {
                            Physics.kCrossMarker.Begin();
                            var i           = index - world.layer.bucketCount;
                            var bucket      = world.GetBucket(i);
                            var crossBucket = world.GetBucket(IndexStrategies.CrossBucketIndex(world.layer.cellCount));
                            var jobIndex    = IndexStrategies.Part1Count(world.layer.cellCount) + i;
                            var context     = new FindPairsBucketContext(in world.layer,
                                                                         in world.layer,
                                                                         in bucket.slices,
                                                                         in crossBucket.slices,
                                                                         jobIndex,
                                                                         false,
                                                                         false);
                            if (processor.BeginBucket(in context))
                            {
                                if (usesBothMasks)
                                {
                                    FindPairsSweepMethods.BipartiteSweepCellCross(world.layer,
                                                                                  world.layer,
                                                                                  in bucket,
                                                                                  in crossBucket,
                                                                                  in maskA,
                                                                                  in maskB,
                                                                                  jobIndex,
                                                                                  ref processor,
                                                                                  false,
                                                                                  false);
                                    FindPairsSweepMethods.BipartiteSweepCrossCell(world.layer,
                                                                                  world.layer,
                                                                                  in crossBucket,
                                                                                  in bucket,
                                                                                  in maskA,
                                                                                  in maskB,
                                                                                  jobIndex,
                                                                                  ref processor,
                                                                                  false,
                                                                                  false);
                                }
                                processor.EndBucket(in context);
                            }
                            Physics.kCrossMarker.End();
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

            public static void RunImmediate(in CollisionWorld world, in CollisionWorld.Mask mask, ref T processor, bool isThreadSafe)
            {
                int jobIndex = 0;
                for (int i = 0; i < IndexStrategies.Part1Count(world.layer.cellCount); i++)
                {
                    var bucket  = world.GetBucket(i);
                    var context = new FindPairsBucketContext(in world.layer,
                                                             in world.layer,
                                                             in bucket.slices,
                                                             in bucket.slices,
                                                             jobIndex,
                                                             isThreadSafe,
                                                             isThreadSafe,
                                                             !isThreadSafe);
                    if (processor.BeginBucket(in context))
                    {
                        FindPairsSweepMethods.SelfSweep(in world.layer, in bucket, in mask, jobIndex, ref processor, isThreadSafe, !isThreadSafe);
                        processor.EndBucket(in context);
                    }
                    jobIndex++;
                }

                if (IndexStrategies.ScheduleParallelShouldActuallyBeSingle(world.layer.cellCount))
                    return;

                var crossBucket = world.GetBucket(IndexStrategies.CrossBucketIndex(world.layer.cellCount));
                for (int i = 0; i < IndexStrategies.SingleLayerPart2Count(world.layer.cellCount); i++)
                {
                    var bucket  = world.GetBucket(i);
                    var context = new FindPairsBucketContext(in world.layer,
                                                             in world.layer,
                                                             in bucket.slices,
                                                             in crossBucket.slices,
                                                             jobIndex,
                                                             isThreadSafe,
                                                             isThreadSafe,
                                                             !isThreadSafe);
                    if (processor.BeginBucket(in context))
                    {
                        FindPairsSweepMethods.BipartiteSweepCellCross(in world.layer,
                                                                      in world.layer,
                                                                      in bucket,
                                                                      in crossBucket,
                                                                      in mask,
                                                                      in mask,
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

            public static void RunImmediate(in CollisionWorld world, in CollisionWorld.Mask maskA, in CollisionWorld.Mask maskB, ref T processor, bool isThreadSafe)
            {
                int jobIndex = 0;
                for (int i = 0; i < IndexStrategies.Part1Count(world.layer.cellCount); i++)
                {
                    var bucket  = world.GetBucket(i);
                    var context = new FindPairsBucketContext(in world.layer,
                                                             in world.layer,
                                                             in bucket.slices,
                                                             in bucket.slices,
                                                             jobIndex,
                                                             isThreadSafe,
                                                             isThreadSafe,
                                                             !isThreadSafe);
                    if (processor.BeginBucket(in context))
                    {
                        FindPairsSweepMethods.SelfSweep(in world.layer, in bucket, in maskA, in maskB, jobIndex, ref processor, isThreadSafe, !isThreadSafe);
                        processor.EndBucket(in context);
                    }
                    jobIndex++;
                }

                if (IndexStrategies.ScheduleParallelShouldActuallyBeSingle(world.layer.cellCount))
                    return;

                var crossBucket = world.GetBucket(IndexStrategies.CrossBucketIndex(world.layer.cellCount));
                for (int i = 0; i < IndexStrategies.SingleLayerPart2Count(world.layer.cellCount); i++)
                {
                    var bucket  = world.GetBucket(i);
                    var context = new FindPairsBucketContext(in world.layer,
                                                             in world.layer,
                                                             in bucket.slices,
                                                             in crossBucket.slices,
                                                             jobIndex,
                                                             isThreadSafe,
                                                             isThreadSafe,
                                                             !isThreadSafe);
                    if (processor.BeginBucket(in context))
                    {
                        FindPairsSweepMethods.BipartiteSweepCellCross(in world.layer,
                                                                      in world.layer,
                                                                      in bucket,
                                                                      in crossBucket,
                                                                      in maskA,
                                                                      in maskB,
                                                                      jobIndex,
                                                                      ref processor,
                                                                      isThreadSafe,
                                                                      isThreadSafe,
                                                                      !isThreadSafe);
                        FindPairsSweepMethods.BipartiteSweepCrossCell(in world.layer,
                                                                      in world.layer,
                                                                      in crossBucket,
                                                                      in bucket,
                                                                      in maskA,
                                                                      in maskB,
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

    public partial struct FindPairsLayerLayerConfig<T> where T : unmanaged, IFindPairsProcessor
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
                UnsafeIndexedBlockList<int2>   blockList;
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
                    this.RunByRef(1);
                }

                public JobHandle ScheduleSingle(JobHandle inputDeps)
                {
                    SetScheduleMode(ScheduleMode.Single);
                    return this.ScheduleByRef(1, inputDeps);
                }

                public JobHandle ScheduleParallel(JobHandle inputDeps, ScheduleMode scheduleMode)
                {
                    SetScheduleMode(scheduleMode);
                    scheduleMode = ExtractEnum(scheduleMode, out var useCrossCache, out var allowEntityAliasing);

                    var part1Count = IndexStrategies.Part1Count(layerA.cellCount);

                    if (scheduleMode == ScheduleMode.ParallelPart1)
                    {
                        return this.ScheduleParallelByRef(part1Count, 1, inputDeps);
                    }
                    if (scheduleMode == ScheduleMode.ParallelPart2 && allowEntityAliasing)
                        return this.ScheduleParallelByRef(2, 1, inputDeps);
                    if (scheduleMode == ScheduleMode.ParallelPart2)
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                        return this.ScheduleParallelByRef(3, 1, inputDeps);
#else
                        return this.ScheduleParallelByRef(2, 1, inputDeps);
#endif
                    if (scheduleMode == ScheduleMode.ParallelByA)
                    {
                        if (useCrossCache)
                        {
                            var crossCount = IndexStrategies.ParallelByACrossCount(layerA.cellCount);
                            blockList      = new UnsafeIndexedBlockList<int2>(1024, crossCount, Allocator.TempJob);
                            inputDeps      = new FindPairsParallelByACrossCacheJob { layerA = layerA, layerB = layerB, cache = blockList }.ScheduleParallel(crossCount,
                                                                                                                                                            1,
                                                                                                                                                            inputDeps);
                            scheduleMode |= ScheduleMode.UseCrossCache;
                        }

                        if (allowEntityAliasing)
                            inputDeps = this.ScheduleParallelByRef(part1Count, 1, inputDeps);
                        else
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                            inputDeps = this.ScheduleParallelByRef(part1Count + 1, 1, inputDeps);
#else
                            inputDeps = this.ScheduleParallelByRef(part1Count, 1, inputDeps);
#endif
                        if (useCrossCache)
                            return blockList.Dispose(inputDeps);
                        return inputDeps;
                    }
                    if (scheduleMode == ScheduleMode.ParallelUnsafe)
                        return this.ScheduleParallelByRef(IndexStrategies.JobIndicesFromDualLayerFindPairs(layerA.cellCount), 1, inputDeps);
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
                    name = typeof(T).FullName;
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
                            FindPairsSweepMethods.BipartiteSweepCellCell(in layerA, in layerB, in bucketA, in bucketB, index, ref processor, isThreadSafe, isThreadSafe);
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
                            EntityAliasCheckLayerA(in layerA);
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
                            FindPairsSweepMethods.BipartiteSweepCellCell(in layerA, in layerB, in bucketA, in bucketB, index, ref processor, isThreadSafe, false);
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
                                FindPairsSweepMethods.BipartiteSweepCellCell(in layerA, in layerB, in bucketA, in bucketB, index, ref processor, false, false);
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
                                $"A parallel FindPairs job was scheduled using a layer containing more than one instance of Entity {entity.ToFixedString()}");
                        }
                    }
                    for (int i = 0; i < layerB.count; i++)
                    {
                        if (!hashSet.Add(layerB.bodies[i].entity))
                        {
                            //Note: At this point, it is unknown whether the repeating entity first showed up in layerA or layerB.
                            var entity = layerB.bodies[i].entity;
                            throw new InvalidOperationException(
                                $"A parallel FindPairs job was scheduled using two layers combined containing more than one instance of Entity {entity.ToFixedString()}");
                        }
                    }
                }

                [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
                private static void EntityAliasCheckLayerA(in CollisionLayer layerA)
                {
                    var hashSet = new NativeParallelHashSet<Entity>(layerA.count, Allocator.Temp);
                    for (int i = 0; i < layerA.count; i++)
                    {
                        if (!hashSet.Add(layerA.bodies[i].entity))
                        {
                            //Note: At this point, we know the issue lies exclusively in layerA.
                            var entity = layerA.bodies[i].entity;
                            throw new InvalidOperationException(
                                $"A parallel FindPairs job was scheduled by A using a layer containing more than one instance of Entity {entity.ToFixedString()}");
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
                        FindPairsSweepMethods.BipartiteSweepCellCell(in layerA,
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

    public partial struct FindPairsWorldWorldConfig<T> where T : unmanaged, IFindPairsProcessor
    {
        internal enum ScheduleMode
        {
            Single = 0x0,
            ParallelPart1 = 0x1,
            ParallelPart2 = 0x2,
            ParallelByA = 0x3,
            ParallelUnsafe = 0x4,
            AllowEntityAliasing = 0x80,
        }

        internal static class FindPairsInternal
        {
            [BurstCompile]
            public struct WorldWorldJob : IJobFor
            {
                [ReadOnly] CollisionWorld      worldA;
                [ReadOnly] CollisionWorld      worldB;
                EntityQueryMask                queryMaskA;
                EntityQueryMask                queryMaskB;
                CollisionWorld.Mask            maskA;
                CollisionWorld.Mask            maskB;
                T                              processor;
                ScheduleMode                   m_scheduleMode;
                Unity.Profiling.ProfilerMarker modeAndTMarker;

                #region Construction and Scheduling
                public WorldWorldJob(in CollisionWorld worldA, in EntityQueryMask queryMaskA, in CollisionWorld worldB, in EntityQueryMask queryMaskB, in T processor)
                {
                    this.worldA     = worldA;
                    this.worldB     = worldB;
                    this.queryMaskA = queryMaskA;
                    this.queryMaskB = queryMaskB;
                    maskA           = default;
                    maskB           = default;
                    this.processor  = processor;
                    m_scheduleMode  = default;
                    modeAndTMarker  = default;
                }

                public int cellCount => worldA.layer.cellCount;

                public void RunImmediate()
                {
                    FindPairsInternal.RunImmediate(in worldA, in worldB, worldA.CreateMask(queryMaskA), worldB.CreateMask(queryMaskB), ref processor, false);
                }

                public void Run()
                {
                    SetScheduleMode(ScheduleMode.Single);
                    this.RunByRef(1);
                }

                public JobHandle ScheduleSingle(JobHandle inputDeps)
                {
                    SetScheduleMode(ScheduleMode.Single);
                    return this.ScheduleByRef(1, inputDeps);
                }

                public JobHandle ScheduleParallel(JobHandle inputDeps, ScheduleMode scheduleMode)
                {
                    SetScheduleMode(scheduleMode);
                    scheduleMode = ExtractEnum(scheduleMode, out var allowEntityAliasing);

                    var part1Count = IndexStrategies.Part1Count(worldA.layer.cellCount);

                    if (scheduleMode == ScheduleMode.ParallelPart1)
                    {
                        return this.ScheduleParallelByRef(part1Count, 1, inputDeps);
                    }
                    if (scheduleMode == ScheduleMode.ParallelPart2 && allowEntityAliasing)
                        return this.ScheduleParallelByRef(2, 1, inputDeps);
                    if (scheduleMode == ScheduleMode.ParallelPart2)
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                        return this.ScheduleParallelByRef(3, 1, inputDeps);
#else
                        return this.ScheduleParallelByRef(2, 1, inputDeps);
#endif
                    if (scheduleMode == ScheduleMode.ParallelByA)
                    {
                        return this.ScheduleParallelByRef(part1Count, 1, inputDeps);
                    }
                    if (scheduleMode == ScheduleMode.ParallelUnsafe)
                        return this.ScheduleParallelByRef(IndexStrategies.JobIndicesFromDualLayerFindPairs(worldA.layer.cellCount), 1, inputDeps);
                    return inputDeps;
                }

                void SetScheduleMode(ScheduleMode scheduleMode)
                {
                    m_scheduleMode                = scheduleMode;
                    scheduleMode                  = ExtractEnum(scheduleMode, out var allowEntityAliasing);
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

                ScheduleMode ExtractEnum(ScheduleMode mode, out bool allowEntityAliasing)
                {
                    allowEntityAliasing = (mode & ScheduleMode.AllowEntityAliasing) == ScheduleMode.AllowEntityAliasing;
                    return mode & ~ScheduleMode.AllowEntityAliasing;
                }

                [BurstDiscard]
                static void IsBurst(ref bool isBurst) => isBurst = false;

                [BurstDiscard]
                static void GetProcessorNameNoBurst(ref FixedString128Bytes name)
                {
                    name = typeof(T).FullName;
                }
                #endregion

                #region Job Processing
                public void Execute(int index)
                {
                    using var jobName = modeAndTMarker.Auto();

                    if (!maskA.isCreated)
                    {
                        maskA = worldA.CreateMask(queryMaskA);
                        maskB = worldB.CreateMask(queryMaskB);
                    }

                    var scheduleMode = ExtractEnum(m_scheduleMode, out var allowEntityAliasing);
                    if (scheduleMode == ScheduleMode.Single)
                    {
                        FindPairsInternal.RunImmediate(in worldA, in worldB, in maskA, in maskB, ref processor, true);
                        return;
                    }
                    if (scheduleMode == ScheduleMode.ParallelPart1)
                    {
                        bool isThreadSafe = !allowEntityAliasing;
                        Physics.kCellMarker.Begin();
                        var bucketA = worldA.GetBucket(index);
                        var bucketB = worldB.GetBucket(index);
                        var context = new FindPairsBucketContext(in worldA.layer,
                                                                 in worldB.layer,
                                                                 in bucketA.slices,
                                                                 in bucketB.slices,
                                                                 index,
                                                                 isThreadSafe,
                                                                 isThreadSafe);
                        if (processor.BeginBucket(in context))
                        {
                            FindPairsSweepMethods.BipartiteSweepCellCell(in worldA.layer,
                                                                         in worldB.layer,
                                                                         in bucketA,
                                                                         in bucketB,
                                                                         in maskA,
                                                                         in maskB,
                                                                         index,
                                                                         ref processor,
                                                                         isThreadSafe,
                                                                         isThreadSafe);
                            processor.EndBucket(in context);
                        }
                        Physics.kCellMarker.End();
                        return;
                    }
                    if (scheduleMode == ScheduleMode.ParallelPart2)
                    {
                        if (index == 2)
                        {
                            EntityAliasCheck(in worldA, in worldB, in maskA, in maskB);
                            return;
                        }

                        bool isThreadSafe = !allowEntityAliasing;
                        Physics.kCrossMarker.Begin();
                        if (index == 0)
                        {
                            var crossBucket = worldA.GetBucket(IndexStrategies.CrossBucketIndex(worldA.layer.cellCount));
                            for (int i = 0; i < IndexStrategies.ParallelPart2ACount(worldA.layer.cellCount); i++)
                            {
                                var bucket   = worldB.GetBucket(i);
                                var jobIndex = IndexStrategies.Part1Count(worldA.layer.cellCount) + i;
                                var context  = new FindPairsBucketContext(in worldA.layer,
                                                                          in worldB.layer,
                                                                          in crossBucket.slices,
                                                                          in bucket.slices,
                                                                          jobIndex,
                                                                          isThreadSafe,
                                                                          isThreadSafe);
                                if (processor.BeginBucket(in context))
                                {
                                    FindPairsSweepMethods.BipartiteSweepCrossCell(in worldA.layer,
                                                                                  in worldB.layer,
                                                                                  in crossBucket,
                                                                                  in bucket,
                                                                                  in maskA,
                                                                                  in maskB,
                                                                                  jobIndex,
                                                                                  ref processor,
                                                                                  isThreadSafe,
                                                                                  isThreadSafe);
                                    processor.EndBucket(in context);
                                }
                            }
                        }
                        else if (index == 1)
                        {
                            var crossBucket = worldB.GetBucket(IndexStrategies.CrossBucketIndex(worldA.layer.cellCount));
                            for (int i = 0; i < IndexStrategies.ParallelPart2BCount(worldA.layer.cellCount); i++)
                            {
                                var bucket   = worldA.GetBucket(i);
                                var jobIndex = IndexStrategies.Part1Count(worldA.layer.cellCount) + IndexStrategies.ParallelPart2ACount(worldA.layer.cellCount) + i;
                                var context  = new FindPairsBucketContext(in worldA.layer,
                                                                          in worldB.layer,
                                                                          in bucket.slices,
                                                                          in crossBucket.slices,
                                                                          jobIndex,
                                                                          isThreadSafe,
                                                                          isThreadSafe);
                                if (processor.BeginBucket(in context))
                                {
                                    FindPairsSweepMethods.BipartiteSweepCellCross(in worldA.layer,
                                                                                  in worldB.layer,
                                                                                  in bucket,
                                                                                  in crossBucket,
                                                                                  in maskA,
                                                                                  in maskB,
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
                        int crossBucketIndex = IndexStrategies.CrossBucketIndex(worldA.layer.cellCount);

                        bool isThreadSafe = !allowEntityAliasing;
                        Physics.kCellMarker.Begin();
                        var bucketA = worldA.GetBucket(index);
                        var bucketB = worldB.GetBucket(index);
                        var context = new FindPairsBucketContext(in worldA.layer,
                                                                 in worldB.layer,
                                                                 in bucketA.slices,
                                                                 in bucketB.slices,
                                                                 index,
                                                                 isThreadSafe,
                                                                 false);
                        if (processor.BeginBucket(in context))
                        {
                            FindPairsSweepMethods.BipartiteSweepCellCell(in worldA.layer,
                                                                         in worldB.layer,
                                                                         in bucketA,
                                                                         in bucketB,
                                                                         in maskA,
                                                                         in maskB,
                                                                         index,
                                                                         ref processor,
                                                                         isThreadSafe,
                                                                         false);
                            processor.EndBucket(in context);
                        }
                        Physics.kCellMarker.End();

                        Physics.kCrossMarker.Begin();
                        if (index != crossBucketIndex)
                        {
                            var crossBucket = worldB.GetBucket(crossBucketIndex);
                            var jobIndex    = IndexStrategies.Part1Count(worldA.layer.cellCount) + IndexStrategies.ParallelPart2ACount(worldA.layer.cellCount) + index;
                            context         = new FindPairsBucketContext(in worldA.layer,
                                                                         in worldB.layer,
                                                                         in bucketA.slices,
                                                                         in crossBucket.slices,
                                                                         jobIndex,
                                                                         isThreadSafe,
                                                                         false);
                            if (processor.BeginBucket(in context))
                            {
                                FindPairsSweepMethods.BipartiteSweepCellCross(in worldA.layer,
                                                                              in worldB.layer,
                                                                              in bucketA,
                                                                              in crossBucket,
                                                                              in maskA,
                                                                              in maskB,
                                                                              jobIndex,
                                                                              ref processor,
                                                                              isThreadSafe,
                                                                              false);
                                processor.EndBucket(in context);
                            }
                        }
                        else
                        {
                            var crossBucket = worldA.GetBucket(crossBucketIndex);
                            for (int i = 0; i < IndexStrategies.ParallelByACrossCount(worldA.layer.cellCount); i++)
                            {
                                var bucket   = worldB.GetBucket(i);
                                var jobIndex = IndexStrategies.Part1Count(worldA.layer.cellCount) + i;
                                context      = new FindPairsBucketContext(in worldA.layer,
                                                                          in worldB.layer,
                                                                          in crossBucket.slices,
                                                                          in bucket.slices,
                                                                          jobIndex,
                                                                          isThreadSafe,
                                                                          isThreadSafe);
                                if (processor.BeginBucket(in context))
                                {
                                    FindPairsSweepMethods.BipartiteSweepCrossCell(in worldA.layer,
                                                                                  in worldB.layer,
                                                                                  in crossBucket,
                                                                                  in bucket,
                                                                                  in maskA,
                                                                                  in maskB,
                                                                                  jobIndex,
                                                                                  ref processor,
                                                                                  isThreadSafe,
                                                                                  false);
                                    processor.EndBucket(in context);
                                }
                            }
                        }
                        Physics.kCrossMarker.End();
                        return;
                    }
                    if (scheduleMode == ScheduleMode.ParallelUnsafe)
                    {
                        int crossBucketIndex = IndexStrategies.CrossBucketIndex(worldA.layer.cellCount);
                        var part1Count       = IndexStrategies.Part1Count(worldA.layer.cellCount);
                        if (index < part1Count)
                        {
                            Physics.kCellMarker.Begin();
                            var bucketA = worldA.GetBucket(index);
                            var bucketB = worldB.GetBucket(index);
                            var context = new FindPairsBucketContext(in worldA.layer,
                                                                     in worldB.layer,
                                                                     in bucketA.slices,
                                                                     in bucketB.slices,
                                                                     index,
                                                                     false,
                                                                     false);
                            if (processor.BeginBucket(in context))
                            {
                                FindPairsSweepMethods.BipartiteSweepCellCell(in worldA.layer,
                                                                             in worldB.layer,
                                                                             in bucketA,
                                                                             in bucketB,
                                                                             in maskA,
                                                                             in maskB,
                                                                             index,
                                                                             ref processor,
                                                                             false,
                                                                             false);
                                processor.EndBucket(in context);
                            }
                            Physics.kCellMarker.End();
                        }
                        else if (index < part1Count + IndexStrategies.ParallelPart2ACount(worldA.layer.cellCount))
                        {
                            Physics.kCrossMarker.Begin();
                            var bucket      = worldB.GetBucket(index - part1Count);
                            var crossBucket = worldA.GetBucket(worldA.bucketCount - 1);
                            var context     = new FindPairsBucketContext(in worldA.layer,
                                                                         in worldB.layer,
                                                                         in crossBucket.slices,
                                                                         in bucket.slices,
                                                                         index,
                                                                         false,
                                                                         false);
                            if (processor.BeginBucket(in context))
                            {
                                FindPairsSweepMethods.BipartiteSweepCrossCell(in worldA.layer,
                                                                              in worldB.layer,
                                                                              in crossBucket,
                                                                              in bucket,
                                                                              in maskA,
                                                                              in maskB,
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
                            var bucket      = worldA.GetBucket(index - part1Count - IndexStrategies.ParallelPart2ACount(worldA.layer.cellCount));
                            var crossBucket = worldB.GetBucket(worldB.bucketCount - 1);
                            var context     = new FindPairsBucketContext(in worldA.layer,
                                                                         in worldB.layer,
                                                                         in bucket.slices,
                                                                         in crossBucket.slices,
                                                                         index,
                                                                         false,
                                                                         false);
                            if (processor.BeginBucket(in context))
                            {
                                FindPairsSweepMethods.BipartiteSweepCellCross(in worldA.layer,
                                                                              in worldB.layer,
                                                                              in bucket,
                                                                              in crossBucket,
                                                                              in maskA,
                                                                              in maskB,
                                                                              index,
                                                                              ref processor,
                                                                              false,
                                                                              false);
                                processor.EndBucket(in context);
                            }
                            Physics.kCrossMarker.End();
                        }
                        return;
                    }
                }

                [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
                private static void EntityAliasCheck(in CollisionWorld worldA, in CollisionWorld worldB, in CollisionWorld.Mask maskA, in CollisionWorld.Mask maskB)
                {
                    var hashSet = new NativeParallelHashSet<Entity>(worldA.count + worldB.count, Allocator.Temp);
                    for (int bucketIndex = 0; bucketIndex < worldA.bucketCount; bucketIndex++)
                    {
                        var bucket = worldA.GetBucket(bucketIndex);
                        foreach (var archetypeIndex in maskA)
                        {
                            var bodyRange = bucket.archetypeStartsAndCounts[archetypeIndex];
                            for (int i = 0; i < bodyRange.y; i++)
                            {
                                var bodyIndex = bucket.archetypeBodyIndices[bodyRange.x + i];
                                var entity    = bucket.slices.bodies[bodyIndex].entity;
                                // We don't need to deduplicate because CollisionWorld ensures uniqueness
                                hashSet.Add(entity);
                            }
                        }
                    }
                    for (int bucketIndex = 0; bucketIndex < worldB.bucketCount; bucketIndex++)
                    {
                        var bucket = worldB.GetBucket(bucketIndex);
                        foreach (var archetypeIndex in maskB)
                        {
                            var bodyRange = bucket.archetypeStartsAndCounts[archetypeIndex];
                            for (int i = 0; i < bodyRange.y; i++)
                            {
                                var bodyIndex = bucket.archetypeBodyIndices[bodyRange.x + i];
                                var entity    = bucket.slices.bodies[bodyIndex].entity;
                                if (hashSet.Contains(entity))
                                {
                                    throw new InvalidOperationException(
                                        $"A parallel FindPairs job was scheduled using two CollisionWorlds which both contain {entity.ToFixedString()}");
                                }
                            }
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

            public static void RunImmediate(in CollisionWorld worldA,
                                            in CollisionWorld worldB,
                                            in CollisionWorld.Mask maskA,
                                            in CollisionWorld.Mask maskB,
                                            ref T processor,
                                            bool isThreadSafe)
            {
                int jobIndex         = 0;
                var crossBucketIndex = IndexStrategies.CrossBucketIndex(worldA.layer.cellCount);
                for (int i = 0; i < IndexStrategies.Part1Count(worldA.layer.cellCount); i++)
                {
                    var bucketA = worldA.GetBucket(i);
                    var bucketB = worldB.GetBucket(i);
                    var context = new FindPairsBucketContext(in worldA.layer,
                                                             in worldB.layer,
                                                             in bucketA.slices,
                                                             in bucketB.slices,
                                                             jobIndex,
                                                             isThreadSafe,
                                                             isThreadSafe,
                                                             !isThreadSafe);
                    if (processor.BeginBucket(in context))
                    {
                        FindPairsSweepMethods.BipartiteSweepCellCell(in worldA.layer,
                                                                     in worldB.layer,
                                                                     in bucketA,
                                                                     in bucketB,
                                                                     in maskA,
                                                                     in maskB,
                                                                     jobIndex,
                                                                     ref processor,
                                                                     isThreadSafe,
                                                                     isThreadSafe,
                                                                     !isThreadSafe);
                        processor.EndBucket(in context);
                    }
                    jobIndex++;
                }

                if (IndexStrategies.ScheduleParallelShouldActuallyBeSingle(worldA.layer.cellCount))
                    return;

                var crossBucketA = worldA.GetBucket(crossBucketIndex);
                for (int i = 0; i < IndexStrategies.ParallelPart2ACount(worldA.layer.cellCount); i++)
                {
                    var bucket  = worldB.GetBucket(i);
                    var context = new FindPairsBucketContext(in worldA.layer,
                                                             in worldB.layer,
                                                             in crossBucketA.slices,
                                                             in bucket.slices,
                                                             jobIndex,
                                                             isThreadSafe,
                                                             isThreadSafe,
                                                             !isThreadSafe);
                    if (processor.BeginBucket(in context))
                    {
                        FindPairsSweepMethods.BipartiteSweepCrossCell(in worldA.layer,
                                                                      in worldB.layer,
                                                                      in crossBucketA,
                                                                      in bucket,
                                                                      in maskA,
                                                                      in maskB,
                                                                      jobIndex,
                                                                      ref processor,
                                                                      isThreadSafe,
                                                                      isThreadSafe,
                                                                      !isThreadSafe);
                        processor.EndBucket(in context);
                    }
                    jobIndex++;
                }

                var crossBucketB = worldB.GetBucket(crossBucketIndex);
                for (int i = 0; i < IndexStrategies.ParallelPart2BCount(worldA.layer.cellCount); i++)
                {
                    var bucket  = worldA.GetBucket(i);
                    var context = new FindPairsBucketContext(in worldA.layer,
                                                             in worldB.layer,
                                                             in bucket.slices,
                                                             in crossBucketB.slices,
                                                             jobIndex,
                                                             isThreadSafe,
                                                             isThreadSafe,
                                                             !isThreadSafe);
                    if (processor.BeginBucket(in context))
                    {
                        FindPairsSweepMethods.BipartiteSweepCellCross(in worldA.layer,
                                                                      in worldB.layer,
                                                                      in bucket,
                                                                      in crossBucketB,
                                                                      in maskA,
                                                                      in maskB,
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
        [ReadOnly] public CollisionLayer    layerA;
        [ReadOnly] public CollisionLayer    layerB;
        public UnsafeIndexedBlockList<int2> cache;

        public void Execute(int index)
        {
            var a      = layerA.GetBucketSlices(IndexStrategies.CrossBucketIndex(layerA.cellCount));
            var b      = layerB.GetBucketSlices(index);
            var cacher = new Cacher { cache = cache, writeIndex = index };
            FindPairsSweepMethods.BipartiteSweepCrossCell(in layerA, in layerB, in a, in b, index, ref cacher, false, false);
        }

        struct Cacher : IFindPairsProcessor
        {
            public UnsafeIndexedBlockList<int2> cache;
            public int                          writeIndex;

            public void Execute(in FindPairsResult result)
            {
                cache.Write(new int2(result.bodyIndexA, result.bodyIndexB), writeIndex);
            }
        }
    }
}

