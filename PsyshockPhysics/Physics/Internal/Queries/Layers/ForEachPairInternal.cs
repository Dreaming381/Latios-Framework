using System.Collections.Generic;
using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    public partial struct ForEachPairConfig<T>
    {
        internal enum ScheduleMode
        {
            Single,
            ParallelPart1,
            ParallelPart2,
            ParallelUnsafe
        }

        internal static class ForEachPairInternal
        {
            [BurstCompile]
            public struct ForEachPairJob : IJobFor
            {
                [NativeDisableParallelForRestriction] public PairStream pairStream;
                T                                                       processor;
                Unity.Profiling.ProfilerMarker                          modeAndTMarker;
                ScheduleMode                                            mode;
                bool                                                    includeDisabled;

                public ForEachPairJob(in PairStream pairStream, in T processor, bool includeDisabled)
                {
                    this.pairStream      = pairStream;
                    this.processor       = processor;
                    this.includeDisabled = includeDisabled;
                    modeAndTMarker       = default;
                    mode                 = default;
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
                    if (scheduleMode == ScheduleMode.ParallelPart1)
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                        return this.ScheduleParallel(IndexStrategies.BucketCountWithNaN(pairStream.data.cellCount) + 2, 1, inputDeps);
#else
                        return this.ScheduleParallel(IndexStrategies.BucketCountWithNaN(pairStream.data.cellCount) + 1, 1, inputDeps);
#endif
                    if (scheduleMode == ScheduleMode.ParallelPart2)
                        return this.ScheduleParallel(IndexStrategies.MixedStreamCount(pairStream.data.cellCount), 1, inputDeps);
                    if (scheduleMode == ScheduleMode.ParallelUnsafe)
                        return this.ScheduleParallel(pairStream.data.pairHeaders.indexCount, 1, inputDeps);
                    return inputDeps;
                }

                void SetScheduleMode(ScheduleMode scheduleMode)
                {
                    mode                          = scheduleMode;
                    FixedString64Bytes modeString = default;
                    if (scheduleMode == ScheduleMode.Single)
                        modeString = "Single";
                    else if (scheduleMode == ScheduleMode.ParallelPart1)
                        modeString = "ParallelPart1";
                    else if (scheduleMode == ScheduleMode.ParallelPart2)
                        modeString = "ParallelPart2";
                    else if (scheduleMode == ScheduleMode.ParallelUnsafe)
                        modeString = "ParallelUnsafe";

                    if (includeDisabled)
                        modeString = $"{modeString}_includeDisabled";

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

                public unsafe void Execute(int jobIndex)
                {
                    using var jobName = modeAndTMarker.Auto();
                    if (mode == ScheduleMode.Single)
                    {
                        ForEachPairMethods.ExecuteBatch(ref pairStream, ref processor, 0, pairStream.data.pairHeaders.indexCount, true, true, includeDisabled);
                    }
                    else if (mode == ScheduleMode.ParallelPart1)
                    {
                        var bucketCount = IndexStrategies.BucketCountWithNaN(pairStream.data.cellCount);
                        if (jobIndex < bucketCount)
                        {
                            var start = IndexStrategies.FirstStreamIndexFromBucketIndex(jobIndex, pairStream.data.cellCount);
                            var count = IndexStrategies.StreamCountFromBucketIndex(jobIndex, pairStream.data.cellCount);
                            ForEachPairMethods.ExecuteBatch(ref pairStream, ref processor, start, count, true, true, includeDisabled);
                        }
                        else if (jobIndex == bucketCount)
                        {
                            if (pairStream.data.state->needsIslanding)
                                ForEachPairMethods.CreateIslands(ref pairStream);
                        }
                        else if (jobIndex == bucketCount + 1)
                        {
                            if (pairStream.data.state->needsAliasChecks)
                                ForEachPairMethods.CheckAliasing(pairStream);
                        }
                    }
                    else if (mode == ScheduleMode.ParallelPart2)
                    {
                        ForEachPairMethods.ExecuteBatch(ref pairStream,
                                                        ref processor,
                                                        jobIndex + IndexStrategies.FirstMixedStreamIndex(pairStream.data.cellCount),
                                                        1,
                                                        true,
                                                        true,
                                                        includeDisabled);
                    }
                    else if (mode == ScheduleMode.ParallelUnsafe)
                    {
                        ForEachPairMethods.ExecuteBatch(ref pairStream, ref processor, jobIndex, 1, false, true, includeDisabled);
                    }
                }

                [UnityEngine.Scripting.Preserve]
                void RequireEarlyJobInit()
                {
                    new InitJobsForProcessors.ForeachIniter<T>().Init();
                }
            }
        }
    }

    internal static unsafe class ForEachPairMethods
    {
        public static void ExecuteBatch<T>(ref PairStream pairStream, ref T processor, int startIndex, int count, bool isThreadSafe, bool isKeySafe,
                                           bool includeDisabled) where T : struct,
        IForEachPairProcessor
        {
            var enumerator                           = pairStream.GetEnumerator();
            enumerator.enumerator                    = pairStream.data.pairHeaders.GetEnumerator(startIndex);
            enumerator.pair.index                    = startIndex;
            enumerator.pair.areEntitiesSafeInContext = isThreadSafe;
            enumerator.pair.isParallelKeySafe        = isKeySafe;
            enumerator.onePastLastStreamIndex        = startIndex + count;

            var context = new ForEachPairBatchContext { enumerator = enumerator };
            if (processor.BeginStreamBatch(context))
            {
                while (enumerator.MoveNext())
                {
                    if (includeDisabled || enumerator.pair.enabled)
                        processor.Execute(ref enumerator.pair);
                }
                processor.EndStreamBatch(context);
            }
        }

        public static void CreateIslands(ref PairStream pairStream)
        {
            pairStream.CheckWriteAccess();

            var aggregateStream        = IndexStrategies.IslandAggregateStreamIndex(pairStream.data.cellCount);
            var firstMixedBucketStream = IndexStrategies.FirstMixedStreamIndex(pairStream.data.cellCount);
            var mixedStreamCount       = IndexStrategies.MixedStreamCount(pairStream.data.cellCount);
            pairStream.data.pairHeaders.ClearIndex(aggregateStream);
            for (int i = firstMixedBucketStream; i < firstMixedBucketStream + mixedStreamCount; i++)
            {
                pairStream.data.pairHeaders.MoveIndexToOtherIndexUnordered(i, aggregateStream);
            }

            int totalPairs   = pairStream.data.pairHeaders.CountForIndex(aggregateStream);
            var entityLookup = new NativeHashMap<Entity, int>(totalPairs, Allocator.Temp);
            var ranks        = new NativeList<int>(totalPairs, Allocator.Temp);
            var stack        = new NativeList<int>(32, Allocator.Temp);

            // Join islands
            var enumerator = pairStream.data.pairHeaders.GetEnumerator(aggregateStream);
            while (enumerator.MoveNext())
            {
                ref var header = ref enumerator.GetCurrentAsRef<PairStream.PairHeader>();

                bool addedA = entityLookup.TryGetValue(header.entityA, out var indexA);
                bool addedB = entityLookup.TryGetValue(header.entityB, out var indexB);

                if (!addedA && !addedB)
                {
                    var index = ranks.Length;
                    ranks.AddNoResize(index);
                    entityLookup.Add(header.entityA, index);
                    entityLookup.Add(header.entityB, index);
                }
                else if (addedA && !addedB)
                {
                    entityLookup.Add(header.entityB, indexA);
                }
                else if (!addedA && addedB)
                {
                    entityLookup.Add(header.entityA, indexB);
                }
                else
                {
                    bool aDirty = false;
                    bool bDirty = false;
                    if (ranks[indexA] != indexA)
                    {
                        indexA = CollapseRanks(indexA, ref ranks, ref stack);
                        aDirty = true;
                    }
                    if (ranks[indexB] != indexB)
                    {
                        indexB = CollapseRanks(indexB, ref ranks, ref stack);
                        bDirty = true;
                    }
                    if (indexA < indexB)
                    {
                        ranks[indexB] = indexA;
                        indexB        = indexA;
                        bDirty        = true;
                    }
                    else if (indexB < indexA)
                    {
                        ranks[indexA] = indexB;
                        indexA        = indexB;
                        aDirty        = true;
                    }
                    if (aDirty)
                        entityLookup[header.entityA] = indexA;
                    if (bDirty)
                        entityLookup[header.entityB] = indexB;
                }
            }

            // Collapse any ranks not yet collapsed and identify unique islands
            var uniqueIslandIndices = new NativeList<int>(ranks.Length, Allocator.Temp);
            for (int i = 0; i < ranks.Length; i++)
            {
                var parent = ranks[i];
                if (parent != i)
                    ranks[i] = ranks[parent];
                else
                    uniqueIslandIndices.Add(i);
            }

            // Remap ranks to unique island indices
            for (int islandIndex = 0, i = 0; i < ranks.Length; i++)
            {
                if (i == uniqueIslandIndices[islandIndex])
                    ranks[i] = islandIndex;
                else
                    ranks[i] = ranks[ranks[i]];
            }

            // If we have more islands than streams, we need to distribute them.
            if (uniqueIslandIndices.Length > mixedStreamCount)
            {
                // Count elements in each island and sort
                var islandIndicesAndCounts = new NativeArray<int2>(uniqueIslandIndices.Length, Allocator.Temp);
                for (int i = 0; i < uniqueIslandIndices.Length; i++)
                    islandIndicesAndCounts[i] = new int2(i, 0);
                enumerator                    = pairStream.data.pairHeaders.GetEnumerator(aggregateStream);
                while (enumerator.MoveNext())
                {
                    var entityA                           = enumerator.GetCurrentAsRef<PairStream.PairHeader>().entityA;
                    var targetIsland                      = ranks[entityLookup[entityA]];
                    islandIndicesAndCounts[targetIsland] += new int2(0, 1);
                }
                islandIndicesAndCounts.Sort(new GreatestToLeastComparer());

                // Distribute largest islands
                var streamIndicesAndCounts = new NativeArray<int2>(mixedStreamCount, Allocator.Temp);
                for (int i = 0; i < streamIndicesAndCounts.Length; i++)
                {
                    var island                    = islandIndicesAndCounts[i];
                    streamIndicesAndCounts[i]     = new int2(i, island.y);
                    uniqueIslandIndices[island.x] = i;
                }

                // Distribute remaining islands using a greedy algorithm that distributes each island to the stream
                // with the least elements.
                int currentStream                = streamIndicesAndCounts.Length - 1;
                int lowestCountInAPreviousStream = int.MaxValue;
                int countInNextStream            = streamIndicesAndCounts[streamIndicesAndCounts.Length - 2].y;
                for (int i = streamIndicesAndCounts.Length; i < islandIndicesAndCounts.Length; i++)
                {
                    var island                             = islandIndicesAndCounts[i];
                    streamIndicesAndCounts[currentStream] += new int2(0, island.y);
                    var stream                             = streamIndicesAndCounts[currentStream];
                    uniqueIslandIndices[island.x]          = stream.x;

                    if (stream.y < math.max(lowestCountInAPreviousStream, countInNextStream))
                        continue;

                    if (countInNextStream <= lowestCountInAPreviousStream)
                    {
                        lowestCountInAPreviousStream = math.min(lowestCountInAPreviousStream, stream.y);
                        currentStream--;
                        if (currentStream == 0)
                            countInNextStream = int.MaxValue;
                        else
                            countInNextStream = streamIndicesAndCounts[currentStream - 1].y;
                        continue;
                    }

                    // At this point, a previous stream has the minimum. Re-sort the streams and start from the back again.
                    streamIndicesAndCounts.Sort(new GreatestToLeastComparer());
                    currentStream                = streamIndicesAndCounts.Length - 1;
                    lowestCountInAPreviousStream = int.MaxValue;
                    countInNextStream            = streamIndicesAndCounts[currentStream - 1].y;

                    if (stream.y == 1)
                    {
                        // Use a single-element distribution loop in case we end up with lots of independent islands
                        while (i < islandIndicesAndCounts.Length)
                        {
                            for (int currentCount = streamIndicesAndCounts[currentStream].y; i < islandIndicesAndCounts.Length && currentCount < countInNextStream; currentCount++)
                            {
                                for (int streamIndex = currentStream; i < islandIndicesAndCounts.Length && streamIndex < streamIndicesAndCounts.Length; streamIndex++, i++)
                                {
                                    island                               = islandIndicesAndCounts[i];
                                    streamIndicesAndCounts[streamIndex] += new int2(0, island.y);
                                    stream                               = streamIndicesAndCounts[streamIndex];
                                    uniqueIslandIndices[island.x]        = stream.x;
                                }
                            }
                            currentStream--;
                            if (currentStream == 0)
                                countInNextStream = int.MaxValue;
                            else
                                countInNextStream = streamIndicesAndCounts[currentStream - 1].y;
                        }
                    }
                }

                // At this point, uniqueIslandIndices contains stream indices. Assign to ranks.
                for (int i = 0; i < ranks.Length; i++)
                {
                    ranks[i] = uniqueIslandIndices[ranks[i]];
                }
            }

            // At this point, ranks contain stream indices offset from the first mixed bucket stream.
            // We can now redistribute the pair headers.
            var baseStream = firstMixedBucketStream;
            enumerator     = pairStream.data.pairHeaders.GetEnumerator(aggregateStream);
            while (enumerator.MoveNext())
            {
                ref var header       = ref enumerator.GetCurrentAsRef<PairStream.PairHeader>();
                int     targetStream = ranks[entityLookup[header.entityA]];
                pairStream.data.pairHeaders.Write(header, baseStream + targetStream);
            }

            pairStream.data.state->needsIslanding = false;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        public static void CheckAliasing(PairStream pairStream)
        {
            // Note: It is allowed to have an entity with write access in bucket 0 and read access in bucket 1.
            // This is because the entity cannot index a PhysicsComponentLookup in bucket, so only read-only components
            // are accessed. That container should be marked read-only, which makes it safe to read in both bucket 0
            // and bucket 1 in parallel.

            // Todo: If we ever make a readonly mode, this should be checking read access and not update the state flag at the end.
            pairStream.CheckWriteAccess();

            int pairsToCheck = 0;
            for (int i = 0; i < IndexStrategies.BucketStreamCount(pairStream.data.cellCount); i++)
            {
                pairsToCheck += pairStream.data.pairHeaders.CountForIndex(i);
            }

            var map = new NativeHashMap<Entity, int>(pairsToCheck, Allocator.Temp);

            for (int bucketIndex = 0; bucketIndex < IndexStrategies.BucketCountWithNaN(pairStream.data.cellCount); bucketIndex++)
            {
                var firstIndex  = IndexStrategies.FirstStreamIndexFromBucketIndex(bucketIndex, pairStream.data.cellCount);
                var streamCount = IndexStrategies.StreamCountFromBucketIndex(bucketIndex, pairStream.data.cellCount);
                for (int i = firstIndex; i < firstIndex + streamCount; i++)
                {
                    var enumerator = pairStream.data.pairHeaders.GetEnumerator(i);
                    while (enumerator.MoveNext())
                    {
                        ref var header = ref enumerator.GetCurrentAsRef<PairStream.PairHeader>();
                        bool    aIsRW  = (header.flags & PairStream.PairHeader.kWritableA) == PairStream.PairHeader.kWritableA;
                        if (aIsRW && map.TryGetValue(header.entityA, out var oldIndex))
                        {
                            if (oldIndex != bucketIndex)
                                throw new System.InvalidOperationException(
                                    $"Entity {header.entityA.ToFixedString()} is contained in both buckets {oldIndex} and {bucketIndex} within the PairStream with write access for both. This is not allowed.");
                        }
                        else if (aIsRW)
                            map.Add(header.entityA, bucketIndex);

                        bool bIsRW = (header.flags & PairStream.PairHeader.kWritableB) == PairStream.PairHeader.kWritableB;
                        if (bIsRW && map.TryGetValue(header.entityB, out oldIndex))
                        {
                            if (oldIndex != bucketIndex)
                                throw new System.InvalidOperationException(
                                    $"Entity {header.entityB.ToFixedString()} is contained in both buckets {oldIndex} and {bucketIndex} within the PairStream with write access for both. This is not allowed.");
                        }
                        else if (bIsRW)
                            map.Add(header.entityB, bucketIndex);
                    }
                }
            }

            pairStream.data.state->needsAliasChecks = false;
        }

        public static void ScheduleBumpVersions(ref PairStream pairStream, ref JobHandle jobHandle)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            jobHandle = new BumpVersionsJob { pairStream = pairStream }.Schedule(jobHandle);
#endif
        }

        static int CollapseRanks(int tail, ref NativeList<int> ranks, ref NativeList<int> stack)
        {
            stack.Clear();
            stack.Add(tail);
            int current = ranks[tail];
            while (ranks[current] != current)
            {
                stack.Add(current);
                current = ranks[current];
            }
            for (int i = 0; i < stack.Length; i++)
                ranks[stack[i]] = current;
            return current;
        }

        struct GreatestToLeastComparer : IComparer<int2>
        {
            public int Compare(int2 x, int2 y)
            {
                return -x.y.CompareTo(y.y);
            }
        }

        [BurstCompile]
        struct BumpVersionsJob : IJob
        {
            public PairStream pairStream;

            public void Execute()
            {
                pairStream.CheckWriteAccess();
                pairStream.data.state->enumeratorVersion++;
                pairStream.data.state->pairPtrVersion++;
            }
        }
    }
}

