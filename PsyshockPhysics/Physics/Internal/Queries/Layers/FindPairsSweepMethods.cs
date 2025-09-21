using System;
using Latios.Unsafe;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class FindPairsSweepMethods
    {
        #region Dispatchers
        public static void SelfSweep<T>(in CollisionLayer layer,
                                        in BucketSlices bucket,
                                        int jobIndex,
                                        ref T processor,
                                        bool isThreadSafe,
                                        bool isImmediateContext = false) where T : unmanaged, IFindPairsProcessor
        {
            if (bucket.count < 2)
                return;

            var result = new FindPairsResult(in layer, in layer, in bucket, in bucket, jobIndex, isThreadSafe, isThreadSafe, isImmediateContext);

            if (X86.Avx.IsAvxSupported)
            {
                SelfSweepWholeBucketAvx(ref result, bucket, ref processor);
            }
            else
            {
                SelfSweepWholeBucket(ref result, bucket, ref processor);
            }
        }

        public static unsafe void SelfSweep<T>(in CollisionLayer layer,
                                               in WorldBucket bucket,
                                               in CollisionWorld.Mask mask,
                                               int jobIndex,
                                               ref T processor,
                                               bool isThreadSafe,
                                               bool isImmediateContext = false) where T : unmanaged, IFindPairsProcessor
        {
            int archetypeCount = 0;
            int bodyCount      = 0;
            foreach (var index in mask)
            {
                var asac = bucket.archetypeStartsAndCounts[index];
                if (asac.y == 0)
                    continue;
                archetypeCount++;
                bodyCount += asac.y;
            }

            if (bodyCount < 2)
                return;

            var       result    = new FindPairsResult(in layer, in layer, in bucket.slices, in bucket.slices, jobIndex, isThreadSafe, isThreadSafe, isImmediateContext);
            using var allocator = ThreadStackAllocator.GetAllocator();

            if (archetypeCount == 1)
            {
                int2 asac = default;
                foreach (var index in mask)
                {
                    var candidate = bucket.archetypeStartsAndCounts[index];
                    if (candidate.y != 0)
                    {
                        asac = candidate;
                        break;
                    }
                }
                var archetypeIndices = bucket.archetypeBodyIndices.GetSubArray(asac.x, asac.y).AsReadOnlySpan();
                SelfSweepIndices(ref result, in bucket.slices, archetypeIndices, ref processor, allocator);
                return;
            }

            var asacs      = allocator.AllocateAsSpan<int2>(archetypeCount);
            archetypeCount = 0;
            foreach (var index in mask)
            {
                var asac = bucket.archetypeStartsAndCounts[index];
                if (asac.y != 0)
                {
                    asacs[archetypeCount] = asac;
                    archetypeCount++;
                }
            }
            var indices = allocator.AllocateAsSpan<int>(bodyCount);
            GatherArchetypeIndicesGeneral(indices, asacs, bucket.archetypeBodyIndices);
            SelfSweepIndices(ref result, in bucket.slices, indices, ref processor, allocator);
        }

        public static unsafe void SelfSweep<T>(in CollisionLayer layer,
                                               in WorldBucket bucket,
                                               in CollisionWorld.Mask maskA,
                                               in CollisionWorld.Mask maskB,
                                               int jobIndex,
                                               ref T processor,
                                               bool isThreadSafe,
                                               bool isImmediateContext = false) where T : unmanaged, IFindPairsProcessor
        {
            using var allocator = ThreadStackAllocator.GetAllocator();
            if (!GatherBothIndicesSets(in bucket, in maskA, out var indicesA, in bucket, in maskB, out var indicesB, allocator))
                return;
            var result = new FindPairsResult(in layer, in layer, in bucket.slices, in bucket.slices, jobIndex, isThreadSafe, isThreadSafe, isImmediateContext);
            SelfSweepDualIndices(ref result, in bucket.slices, indicesA, indicesB, ref processor);
        }

        public static void BipartiteSweepCellCell<T>(in CollisionLayer layerA,
                                                     in CollisionLayer layerB,
                                                     in BucketSlices bucketA,
                                                     in BucketSlices bucketB,
                                                     int jobIndex,
                                                     ref T processor,
                                                     bool isAThreadSafe,
                                                     bool isBThreadSafe,
                                                     bool isImmediateContext = false) where T : unmanaged, IFindPairsProcessor
        {
            int countA = bucketA.xmins.Length;
            int countB = bucketB.xmins.Length;
            if (countA == 0 || countB == 0)
                return;

            var result = new FindPairsResult(in layerA, in layerB, in bucketA, in bucketB, jobIndex, isAThreadSafe, isBThreadSafe, isImmediateContext);

            if (X86.Avx.IsAvxSupported)
            {
                BipartiteSweepWholeBucketAvx(ref result, in bucketA, in bucketB, ref processor);
            }
            else
            {
                BipartiteSweepWholeBucket(ref result, in bucketA, in bucketB, ref processor);
            }
        }

        public static unsafe void BipartiteSweepCellCell<T>(in CollisionLayer layerA,
                                                            in CollisionLayer layerB,
                                                            in WorldBucket bucketA,
                                                            in WorldBucket bucketB,
                                                            in CollisionWorld.Mask maskA,
                                                            in CollisionWorld.Mask maskB,
                                                            int jobIndex,
                                                            ref T processor,
                                                            bool isAThreadSafe,
                                                            bool isBThreadSafe,
                                                            bool isImmediateContext = false) where T : unmanaged, IFindPairsProcessor
        {
            using var allocator = ThreadStackAllocator.GetAllocator();
            if (!GatherBothIndicesSets(in bucketA, in maskA, out var indicesA, in bucketB, in maskB, out var indicesB, allocator))
                return;
            var result = new FindPairsResult(in layerA, in layerB, in bucketA.slices, in bucketB.slices, jobIndex, isAThreadSafe, isBThreadSafe, isImmediateContext);
            BipartiteSweepDualIndices(ref result, in bucketA.slices, indicesA, in bucketB.slices, indicesB, ref processor, allocator);
        }

        public static void BipartiteSweepCellCross<T>(in CollisionLayer layerA,
                                                      in CollisionLayer layerB,
                                                      in BucketSlices bucketA,
                                                      in BucketSlices bucketB,
                                                      int jobIndex,
                                                      ref T processor,
                                                      bool isAThreadSafe,
                                                      bool isBThreadSafe,
                                                      bool isImmediateContext = false) where T : unmanaged, IFindPairsProcessor
        {
            int countA = bucketA.xmins.Length;
            int countB = bucketB.xmins.Length;
            if (countA == 0 || countB == 0)
                return;

            var result = new FindPairsResult(in layerA, in layerB, in bucketA, in bucketB, jobIndex, isAThreadSafe, isBThreadSafe, isImmediateContext);

            if (bucketB.count < 32)
                BipartiteSweepWholeBucket(ref result, in bucketA, in bucketB, ref processor);
            else
                BipartiteSweepBucketVsFilteredCross(ref result, in bucketA, in bucketB, ref processor, new BucketAabb(in layerA, bucketA.bucketIndex));
        }

        public static void BipartiteSweepCellCross<T>(in CollisionLayer layerA,
                                                      in CollisionLayer layerB,
                                                      in WorldBucket bucketA,
                                                      in WorldBucket bucketB,
                                                      in CollisionWorld.Mask maskA,
                                                      in CollisionWorld.Mask maskB,
                                                      int jobIndex,
                                                      ref T processor,
                                                      bool isAThreadSafe,
                                                      bool isBThreadSafe,
                                                      bool isImmediateContext = false) where T : unmanaged, IFindPairsProcessor
        {
            using var allocator = ThreadStackAllocator.GetAllocator();
            if (!GatherBothIndicesSets(in bucketA, in maskA, out var indicesA, in bucketB, in maskB, out var indicesB, allocator))
                return;
            var result     = new FindPairsResult(in layerA, in layerB, in bucketA.slices, in bucketB.slices, jobIndex, isAThreadSafe, isBThreadSafe, isImmediateContext);
            var bucketAabb = new BucketAabb(layerA, bucketA.slices.bucketIndex);
            BipartiteSweepDualIndicesFilteredCross(ref result, in bucketA.slices, indicesA, in bucketB.slices, indicesB, ref processor, in bucketAabb, false, allocator);
        }

        public static void BipartiteSweepCrossCell<T>(in CollisionLayer layerA,
                                                      in CollisionLayer layerB,
                                                      in BucketSlices bucketA,
                                                      in BucketSlices bucketB,
                                                      int jobIndex,
                                                      ref T processor,
                                                      bool isAThreadSafe,
                                                      bool isBThreadSafe,
                                                      bool isImmediateContext = false) where T : unmanaged, IFindPairsProcessor
        {
            int countA = bucketA.xmins.Length;
            int countB = bucketB.xmins.Length;
            if (countA == 0 || countB == 0)
                return;

            var result = new FindPairsResult(in layerA, in layerB, in bucketA, in bucketB, jobIndex, isAThreadSafe, isBThreadSafe, isImmediateContext);

            if (bucketA.count < 32)
                BipartiteSweepWholeBucket(ref result, in bucketA, in bucketB, ref processor);
            else
                BipartiteSweepFilteredCrossVsBucket(ref result, in bucketA, in bucketB, ref processor, new BucketAabb(in layerB, bucketB.bucketIndex));
        }

        public static void BipartiteSweepCrossCell<T>(in CollisionLayer layerA,
                                                      in CollisionLayer layerB,
                                                      in WorldBucket bucketA,
                                                      in WorldBucket bucketB,
                                                      in CollisionWorld.Mask maskA,
                                                      in CollisionWorld.Mask maskB,
                                                      int jobIndex,
                                                      ref T processor,
                                                      bool isAThreadSafe,
                                                      bool isBThreadSafe,
                                                      bool isImmediateContext = false) where T : unmanaged, IFindPairsProcessor
        {
            using var allocator = ThreadStackAllocator.GetAllocator();
            if (!GatherBothIndicesSets(in bucketA, in maskA, out var indicesA, in bucketB, in maskB, out var indicesB, allocator))
                return;
            var result     = new FindPairsResult(in layerA, in layerB, in bucketA.slices, in bucketB.slices, jobIndex, isAThreadSafe, isBThreadSafe, isImmediateContext);
            var bucketAabb = new BucketAabb(layerB, bucketB.slices.bucketIndex);
            BipartiteSweepDualIndicesFilteredCross(ref result, in bucketA.slices, indicesA, in bucketB.slices, indicesB, ref processor, in bucketAabb, true, allocator);
        }

        public static int BipartiteSweepPlayCache<T>(UnsafeIndexedBlockList<int2>.Enumerator enumerator,
                                                     in CollisionLayer layerA,
                                                     in CollisionLayer layerB,
                                                     int bucketIndexA,
                                                     int bucketIndexB,
                                                     int jobIndex,
                                                     ref T processor,
                                                     bool isAThreadSafe,
                                                     bool isBThreadSafe) where T : unmanaged, IFindPairsProcessor
        {
            if (!enumerator.MoveNext())
                return 0;

            var result = FindPairsResult.CreateGlobalResult(in layerA, in layerB, bucketIndexA, bucketIndexB, jobIndex, isAThreadSafe, isBThreadSafe);
            int count  = 0;

            do
            {
                var indices = enumerator.Current;
                result.SetBucketRelativePairIndices(indices.x, indices.y);
                processor.Execute(in result);
                count++;
            }
            while (enumerator.MoveNext());
            return count;
        }
        #endregion

        #region Utilities
        struct BucketAabb
        {
            public float  xmin;
            public float  xmax;
            public float4 yzMinMaxFlipped;
            public bool4  finiteMask;

            public BucketAabb(in CollisionLayer layer, int bucketIndex)
            {
                var dimensions  = layer.worldSubdivisionsPerAxis;
                int k           = bucketIndex % dimensions.z;
                int j           = ((bucketIndex - k) / dimensions.z) % dimensions.y;
                int i           = (((bucketIndex - k) / dimensions.z) - j) / dimensions.y;
                var bucketStart = layer.worldMin + layer.worldAxisStride * new float3(i, j, k);
                var bucketEnd   = bucketStart + layer.worldAxisStride;
                xmin            = math.select(float.NegativeInfinity, bucketStart.x, i > 0);
                xmax            = math.select(float.PositiveInfinity, bucketEnd.x, i < dimensions.x - 1);
                yzMinMaxFlipped = new float4(bucketStart.yz, -bucketEnd.yz);
                yzMinMaxFlipped = -yzMinMaxFlipped.zwxy;
                bool a          = j < dimensions.y - 1;  // cell.max.y < AABB.min.y
                bool b          = k < dimensions.z - 1;  // cell.max.z < AABB.min.z
                bool c          = j > 0;  // -cell.min.y < -AABB.max.y
                bool d          = k > 0;  // -cell.min.z < -AABB.max.z
                finiteMask      = new bool4(a, b, c, d);
            }
        }
        #endregion

        #region Self Sweeps
        static void SelfSweepWholeBucket<T>(ref FindPairsResult result, in BucketSlices bucket, ref T processor) where T : unmanaged, IFindPairsProcessor
        {
            Hint.Assume(bucket.xmins.Length == bucket.xmaxs.Length);
            Hint.Assume(bucket.xmins.Length == bucket.yzminmaxs.Length);
            Hint.Assume(bucket.xmins.Length == bucket.bodies.Length);

            int count = bucket.xmins.Length;
            for (int i = 0; i < count - 1; i++)
            {
                var current = -bucket.yzminmaxs[i].zwxy;
                var xmax    = bucket.xmaxs[i];
                for (int j = i + 1; j < count && bucket.xmins[j] <= xmax; j++)
                {
                    if (math.bitmask(current < bucket.yzminmaxs[j]) == 0)
                    {
                        result.SetBucketRelativePairIndices(i, j);
                        processor.Execute(in result);
                    }
                }
            }
        }

        static unsafe void SelfSweepWholeBucketAvx<T>(ref FindPairsResult result, in BucketSlices bucket, ref T processor) where T : struct,
        IFindPairsProcessor
        {
            if (X86.Avx.IsAvxSupported)
            {
                Hint.Assume(bucket.xmins.Length == bucket.xmaxs.Length);
                Hint.Assume(bucket.xmins.Length == bucket.yzminmaxs.Length);
                Hint.Assume(bucket.xmins.Length == bucket.bodies.Length);

                int count = bucket.xmins.Length;
                for (int i = 0; i < count - 1; i++)
                {
                    var   current        = -bucket.yzminmaxs[i].zwxy;
                    v256  current256     = new v256(current.x, current.y, current.z, current.w, current.x, current.y, current.z, current.w);
                    float xmax           = bucket.xmaxs[i];
                    var   xminsPtr       = (byte*)bucket.xmins.GetUnsafeReadOnlyPtr() + 4 * i + 4;
                    var   flippedPtr     = (byte*)bucket.yzminmaxs.GetUnsafeReadOnlyPtr() + 16 * i + 16;
                    var   countRemaining = 4 * (ulong)(count - (i + 1));

                    ulong j = 0;
                    for (; Hint.Likely(j < (countRemaining & ~0x7ul) && *(float*)(xminsPtr + j + 4) <= xmax); j += 8)
                    {
                        v256 otherPairs = X86.Avx.mm256_loadu_ps(flippedPtr + 4 * j);
                        var  cmpBools   = X86.Avx.mm256_cmp_ps(current256, otherPairs, (int)X86.Avx.CMP.LT_OQ);
                        var  cmpResult  = X86.Avx.mm256_movemask_ps(cmpBools);
                        if (Hint.Unlikely((cmpResult & 0xf) == 0))
                        {
                            result.SetBucketRelativePairIndices(i, i + (int)(j >> RuntimeConstants.two.Data) + 1);
                            processor.Execute(in result);
                        }
                        if (Hint.Unlikely((cmpResult & 0xf0) == 0))
                        {
                            result.SetBucketRelativePairIndices(i, i + (int)(j >> RuntimeConstants.two.Data) + 2);
                            processor.Execute(in result);
                        }
                    }
                    if (j < countRemaining && *(float*)(xminsPtr + j) <= xmax)
                    {
                        if (Hint.Unlikely(math.bitmask(current < *(float4*)(flippedPtr + 4 * j)) == 0))
                        {
                            result.SetBucketRelativePairIndices(i, i + (int)(j >> RuntimeConstants.two.Data) + 1);
                            processor.Execute(in result);
                        }
                    }
                }
            }
        }

        static unsafe void SelfSweepIndices<T>(ref FindPairsResult result, in BucketSlices bucket, ReadOnlySpan<int> indices, ref T processor, ThreadStackAllocator allocator)
            where T : unmanaged, IFindPairsProcessor
        {
            var xmins     = allocator.Allocate<float>(indices.Length + 1);
            var xmaxs     = allocator.Allocate<float>(indices.Length);
            var yzminmaxs = allocator.Allocate<float4>(indices.Length);

            for (int i = 0; i < indices.Length; i++)
            {
                var index    = indices[i];
                xmins[i]     = bucket.xmins[index];
                xmaxs[i]     = bucket.xmaxs[index];
                yzminmaxs[i] = bucket.yzminmaxs[index];
            }
            xmins[indices.Length] = float.NaN;

            for (int i = 0; i < indices.Length; i++)
            {
                var current = -yzminmaxs[i].zwxy;
                var xmax    = xmaxs[i];
                for (int j = i + 1; xmins[j] <= xmax; j++)
                {
                    if (math.bitmask(current < bucket.yzminmaxs[j]) == 0)
                    {
                        result.SetBucketRelativePairIndices(indices[i], indices[j]);
                        processor.Execute(in result);
                    }
                }
            }
        }

        static void SelfSweepDualIndices<T>(ref FindPairsResult result, in BucketSlices bucket, ReadOnlySpan<int> indicesA, ReadOnlySpan<int> indicesB, ref T processor)
            where T : unmanaged, IFindPairsProcessor
        {
            int progressA = 0, progressB = 0;
            while (true)
            {
                var indexA = indicesA[progressA];
                var indexB = indicesB[progressB];
                if (indexA < indexB)
                {
                    var current = -bucket.yzminmaxs[indexA].zwxy;
                    var xmax    = bucket.xmaxs[indexA];
                    for (int j = progressB; j < indicesB.Length && bucket.xmins[indicesB[j]] <= xmax; j++)
                    {
                        indexB = indicesB[j];
                        if (math.bitmask(current < bucket.yzminmaxs[indexB]) == 0)
                        {
                            result.SetBucketRelativePairIndices(indexA, indexB);
                            processor.Execute(in result);
                        }
                    }
                    progressA++;
                    if (progressA >= indicesA.Length)
                        return;
                }
                else if (indexA > indexB)
                {
                    var current = -bucket.yzminmaxs[indexB].zwxy;
                    var xmax    = bucket.xmaxs[indexB];
                    for (int j = progressA; j < indicesA.Length && bucket.xmins[indicesA[j]] <= xmax; j++)
                    {
                        indexA = indicesA[j];
                        if (math.bitmask(current < bucket.yzminmaxs[indexA]) == 0)
                        {
                            result.SetBucketRelativePairIndices(indexA, indexB);
                            processor.Execute(in result);
                        }
                    }
                    progressB++;
                    if (progressB >= indicesB.Length)
                        return;
                }
                else
                {
                    // indexA == indexB
                    // We need to sweep ahead both indices list but exclude indexA/B
                    var current = -bucket.yzminmaxs[indexA].zwxy;
                    var xmax    = bucket.xmaxs[indexA];
                    for (int j = progressB + 1; j < indicesB.Length && bucket.xmins[indicesB[j]] <= xmax; j++)
                    {
                        indexB = indicesB[j];
                        if (math.bitmask(current < bucket.yzminmaxs[indexB]) == 0)
                        {
                            result.SetBucketRelativePairIndices(indexA, indexB);
                            processor.Execute(in result);
                        }
                    }
                    indexB = indexA;
                    for (int j = progressA + 1; j < indicesA.Length && bucket.xmins[indicesA[j]] <= xmax; j++)
                    {
                        indexA = indicesA[j];
                        if (math.bitmask(current < bucket.yzminmaxs[indexA]) == 0)
                        {
                            result.SetBucketRelativePairIndices(indexA, indexB);
                            processor.Execute(in result);
                        }
                    }
                    progressA++;
                    progressB++;
                    if (progressA >= indicesA.Length || progressB >= indicesB.Length)
                        return;
                }
            }
        }
        #endregion

        #region Bipartite Sweeps
        static unsafe void BipartiteSweepWholeBucket<T>(ref FindPairsResult result,
                                                        in BucketSlices bucketA,
                                                        in BucketSlices bucketB,
                                                        ref T processor) where T : struct,
        IFindPairsProcessor
        {
            Hint.Assume(bucketA.xmins.Length == bucketA.xmaxs.Length);
            Hint.Assume(bucketA.xmins.Length == bucketA.yzminmaxs.Length);
            Hint.Assume(bucketA.xmins.Length == bucketA.bodies.Length);

            Hint.Assume(bucketB.xmins.Length == bucketB.xmaxs.Length);
            Hint.Assume(bucketB.xmins.Length == bucketB.yzminmaxs.Length);
            Hint.Assume(bucketB.xmins.Length == bucketB.bodies.Length);

            int countA = bucketA.xmins.Length;
            int countB = bucketB.xmins.Length;

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

                var current = -bucketA.yzminmaxs[i].zwxy;
                var xmax    = bucketA.xmaxs[i];
                for (int j = bstart; j < countB && bucketB.xmins[j] <= xmax; j++)
                {
                    if (math.bitmask(current < bucketB.yzminmaxs[j]) == 0)
                    {
                        result.SetBucketRelativePairIndices(i, j);
                        processor.Execute(in result);
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

                var current = -bucketB.yzminmaxs[i].zwxy;
                var xmax    = bucketB.xmaxs[i];
                for (int j = astart; j < countA && bucketA.xmins[j] <= xmax; j++)
                {
                    if (math.bitmask(current < bucketA.yzminmaxs[j]) == 0)
                    {
                        result.SetBucketRelativePairIndices(j, i);
                        processor.Execute(in result);
                    }
                }
            }
        }

        static unsafe void BipartiteSweepWholeBucketAvx<T>(ref FindPairsResult result,
                                                           in BucketSlices bucketA,
                                                           in BucketSlices bucketB,
                                                           ref T processor) where T : struct,
        IFindPairsProcessor
        {
            if (X86.Avx.IsAvxSupported)
            {
                Hint.Assume(bucketA.xmins.Length == bucketA.xmaxs.Length);
                Hint.Assume(bucketA.xmins.Length == bucketA.yzminmaxs.Length);
                Hint.Assume(bucketA.xmins.Length == bucketA.bodies.Length);

                Hint.Assume(bucketB.xmins.Length == bucketB.xmaxs.Length);
                Hint.Assume(bucketB.xmins.Length == bucketB.yzminmaxs.Length);
                Hint.Assume(bucketB.xmins.Length == bucketB.bodies.Length);

                int countA = bucketA.xmins.Length;
                int countB = bucketB.xmins.Length;

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

                    var   current        = -bucketA.yzminmaxs[i].zwxy;
                    v256  current256     = new v256(current.x, current.y, current.z, current.w, current.x, current.y, current.z, current.w);
                    var   xmax           = bucketA.xmaxs[i];
                    var   xminsPtr       = (byte*)bucketB.xmins.GetUnsafeReadOnlyPtr() + 4 * bstart;
                    var   flippedPtr     = (byte*)bucketB.yzminmaxs.GetUnsafeReadOnlyPtr() + 16 * bstart;
                    var   countRemaining = 4 * (ulong)(countB - bstart);
                    ulong j              = 0;
                    for (; j < (countRemaining & ~0x7ul) && *(float*)(xminsPtr + j + 4) <= xmax; j += 8)
                    {
                        v256 otherPairs = X86.Avx.mm256_loadu_ps(flippedPtr + 4 * j);
                        var  cmpBools   = X86.Avx.mm256_cmp_ps(current256, otherPairs, (int)X86.Avx.CMP.LT_OQ);
                        var  cmpResult  = X86.Avx.mm256_movemask_ps(cmpBools);
                        if (Hint.Unlikely((cmpResult & 0xf) == 0))
                        {
                            result.SetBucketRelativePairIndices(i, (int)(j >> RuntimeConstants.two.Data) + bstart);
                            processor.Execute(in result);
                        }
                        if (Hint.Unlikely((cmpResult & 0xf0) == 0))
                        {
                            result.SetBucketRelativePairIndices(i, (int)(j >> RuntimeConstants.two.Data) + 1 + bstart);
                            processor.Execute(in result);
                        }
                    }
                    if (j < countRemaining && *(float*)(xminsPtr + j) <= xmax)
                    {
                        if (Hint.Unlikely(math.bitmask(current < *(float4*)(flippedPtr + 4 * j)) == 0))
                        {
                            result.SetBucketRelativePairIndices(i, (int)(j >> RuntimeConstants.two.Data) + bstart);
                            processor.Execute(in result);
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

                    var   current        = -bucketB.yzminmaxs[i].zwxy;
                    v256  current256     = new v256(current.x, current.y, current.z, current.w, current.x, current.y, current.z, current.w);
                    var   xmax           = bucketB.xmaxs[i];
                    var   xminsPtr       = (byte*)bucketA.xmins.GetUnsafeReadOnlyPtr() + 4 * astart;
                    var   flippedPtr     = (byte*)bucketA.yzminmaxs.GetUnsafeReadOnlyPtr() + 16 * astart;
                    var   countRemaining = 4 * (ulong)(countA - astart);
                    ulong j              = 0;
                    for (; j < (countRemaining & ~0x7ul) && *(float*)(xminsPtr + j + 4) <= xmax; j += 8)
                    {
                        v256 otherPairs = X86.Avx.mm256_loadu_ps(flippedPtr + 4 * j);
                        var  cmpBools   = X86.Avx.mm256_cmp_ps(current256, otherPairs, (int)X86.Avx.CMP.LT_OQ);
                        var  cmpResult  = X86.Avx.mm256_movemask_ps(cmpBools);
                        if (Hint.Unlikely((cmpResult & 0xf) == 0))
                        {
                            result.SetBucketRelativePairIndices((int)(j >> RuntimeConstants.two.Data) + astart, i);
                            processor.Execute(in result);
                        }
                        if (Hint.Unlikely((cmpResult & 0xf0) == 0))
                        {
                            result.SetBucketRelativePairIndices((int)(j >> RuntimeConstants.two.Data) + 1 + astart, i);
                            processor.Execute(in result);
                        }
                    }
                    if (j < countRemaining && *(float*)(xminsPtr + j) <= xmax)
                    {
                        if (Hint.Unlikely(math.bitmask(current < *(float4*)(flippedPtr + 4 * j)) == 0))
                        {
                            result.SetBucketRelativePairIndices((int)(j >> RuntimeConstants.two.Data) + astart, i);
                            processor.Execute(in result);
                        }
                    }
                }
            }
        }

        static unsafe void BipartiteSweepBucketVsFilteredCross<T>(ref FindPairsResult result,
                                                                  in BucketSlices bucketA,
                                                                  in BucketSlices bucketB,
                                                                  ref T processor,
                                                                  in BucketAabb bucketAabbForA) where T : unmanaged, IFindPairsProcessor
        {
            Hint.Assume(bucketA.xmins.Length == bucketA.xmaxs.Length);
            Hint.Assume(bucketA.xmins.Length == bucketA.yzminmaxs.Length);
            Hint.Assume(bucketA.xmins.Length == bucketA.bodies.Length);

            Hint.Assume(bucketB.xmins.Length == bucketB.xmaxs.Length);
            Hint.Assume(bucketB.xmins.Length == bucketB.yzminmaxs.Length);
            Hint.Assume(bucketB.xmins.Length == bucketB.bodies.Length);

            int countA = bucketA.xmins.Length;
            int countB = bucketB.xmins.Length;

            using var allocator = ThreadStackAllocator.GetAllocator();

            var crossXMins     = allocator.Allocate<float>(countB + 1);
            var crossXMaxs     = allocator.Allocate<float>(countB);
            var crossYzMinMaxs = allocator.Allocate<float4>(countB);
            var crossIndices   = allocator.Allocate<int>(countB);
            int crossCount     = 0;

            for (int i = 0; i < countB; i++)
            {
                if (bucketAabbForA.xmax < bucketB.xmins[i])
                    break;
                if (bucketB.xmaxs[i] < bucketAabbForA.xmin)
                    continue;
                if (math.bitmask((bucketAabbForA.yzMinMaxFlipped < bucketB.yzminmaxs[i]) & bucketAabbForA.finiteMask) == 0)
                {
                    crossXMins[crossCount]     = bucketB.xmins[i];
                    crossXMaxs[crossCount]     = bucketB.xmaxs[i];
                    crossYzMinMaxs[crossCount] = bucketB.yzminmaxs[i];
                    crossIndices[crossCount]   = i;
                    crossCount++;
                }
            }
            crossXMins[crossCount] = float.NaN;
            //UnityEngine.Debug.Log($"Remaining after filter: {crossCount * 100f / countB}");

            // Check for b starting in a's x range
            int bstart = 0;
            for (int i = 0; i < countA; i++)
            {
                // Advance to b.xmin >= a.xmin
                // Include equals case by stopping when equal
                while (bstart < crossCount && crossXMins[bstart] < bucketA.xmins[i])
                    bstart++;
                if (bstart >= crossCount)
                    break;

                var current = -bucketA.yzminmaxs[i].zwxy;
                var xmax    = bucketA.xmaxs[i];
                for (int j = bstart; j < crossCount && crossXMins[j] <= xmax; j++)
                {
                    if (math.bitmask(current < crossYzMinMaxs[j]) == 0)
                    {
                        result.SetBucketRelativePairIndices(i, crossIndices[j]);
                        processor.Execute(in result);
                    }
                }
            }

            // Check for a starting in b's x range
            int astart = 0;
            for (int i = 0; i < crossCount; i++)
            {
                // Advance to a.xmin > b.xmin
                // Exclude equals case this time by continuing if equal
                while (astart < countA && bucketA.xmins[astart] <= crossXMins[i])
                    astart++;
                if (astart >= countA)
                    break;

                var current = -crossYzMinMaxs[i].zwxy;
                var xmax    = crossXMaxs[i];
                for (int j = astart; j < countA && bucketA.xmins[j] <= xmax; j++)
                {
                    if (math.bitmask(current < bucketA.yzminmaxs[j]) == 0)
                    {
                        result.SetBucketRelativePairIndices(j, crossIndices[i]);
                        processor.Execute(in result);
                    }
                }
            }
        }

        static unsafe void BipartiteSweepFilteredCrossVsBucket<T>(ref FindPairsResult result,
                                                                  in BucketSlices bucketA,
                                                                  in BucketSlices bucketB,
                                                                  ref T processor,
                                                                  in BucketAabb bucketAabbForB) where T : unmanaged, IFindPairsProcessor
        {
            Hint.Assume(bucketA.xmins.Length == bucketA.xmaxs.Length);
            Hint.Assume(bucketA.xmins.Length == bucketA.yzminmaxs.Length);
            Hint.Assume(bucketA.xmins.Length == bucketA.bodies.Length);

            Hint.Assume(bucketB.xmins.Length == bucketB.xmaxs.Length);
            Hint.Assume(bucketB.xmins.Length == bucketB.yzminmaxs.Length);
            Hint.Assume(bucketB.xmins.Length == bucketB.bodies.Length);

            int countA = bucketA.xmins.Length;
            int countB = bucketB.xmins.Length;

            using var allocator = ThreadStackAllocator.GetAllocator();

            var crossXMins     = allocator.Allocate<float>(countA + 1);
            var crossXMaxs     = allocator.Allocate<float>(countA);
            var crossYzMinMaxs = allocator.Allocate<float4>(countA);
            var crossIndices   = allocator.Allocate<int>(countA);
            int crossCount     = 0;

            for (int i = 0; i < countA; i++)
            {
                if (bucketAabbForB.xmax < bucketA.xmins[i])
                    break;
                if (bucketA.xmaxs[i] < bucketAabbForB.xmin)
                    continue;
                if (math.bitmask((bucketAabbForB.yzMinMaxFlipped < bucketA.yzminmaxs[i]) & bucketAabbForB.finiteMask) == 0)
                {
                    crossXMins[crossCount]     = bucketA.xmins[i];
                    crossXMaxs[crossCount]     = bucketA.xmaxs[i];
                    crossYzMinMaxs[crossCount] = bucketA.yzminmaxs[i];
                    crossIndices[crossCount]   = i;
                    crossCount++;
                }
            }
            crossXMins[crossCount] = float.NaN;
            //UnityEngine.Debug.Log($"Remaining after filter: {crossCount * 100f / countA}");

            // Check for b starting in a's x range
            int bstart = 0;
            for (int i = 0; i < crossCount; i++)
            {
                // Advance to b.xmin >= a.xmin
                // Include equals case by stopping when equal
                while (bstart < countB && bucketB.xmins[bstart] < crossXMins[i])
                    bstart++;
                if (bstart >= countB)
                    break;

                var current = -crossYzMinMaxs[i].zwxy;
                var xmax    = crossXMaxs[i];
                for (int j = bstart; j < countB && bucketB.xmins[j] <= xmax; j++)
                {
                    if (math.bitmask(current < bucketB.yzminmaxs[j]) == 0)
                    {
                        result.SetBucketRelativePairIndices(crossIndices[i], j);
                        processor.Execute(in result);
                    }
                }
            }

            // Check for a starting in b's x range
            int astart = 0;
            for (int i = 0; i < countB; i++)
            {
                // Advance to a.xmin > b.xmin
                // Exclude equals case this time by continuing if equal
                while (astart < crossCount && crossXMins[astart] <= bucketB.xmins[i])
                    astart++;
                if (astart >= crossCount)
                    break;

                var current = -bucketB.yzminmaxs[i].zwxy;
                var xmax    = bucketB.xmaxs[i];
                for (int j = astart; j < crossCount && crossXMins[j] <= xmax; j++)
                {
                    if (math.bitmask(current < crossYzMinMaxs[j]) == 0)
                    {
                        result.SetBucketRelativePairIndices(crossIndices[j], i);
                        processor.Execute(in result);
                    }
                }
            }
        }

        static unsafe void BipartiteSweepDualIndices<T>(ref FindPairsResult result,
                                                        in BucketSlices bucketA,
                                                        ReadOnlySpan<int>    indicesA,
                                                        in BucketSlices bucketB,
                                                        ReadOnlySpan<int>    indicesB,
                                                        ref T processor,
                                                        ThreadStackAllocator allocator)
            where T : unmanaged, IFindPairsProcessor
        {
            var xminsA     = allocator.Allocate<float>(indicesA.Length + 1);
            var xmaxsA     = allocator.Allocate<float>(indicesA.Length);
            var yzminmaxsA = allocator.Allocate<float4>(indicesA.Length);

            for (int i = 0; i < indicesA.Length; i++)
            {
                var index     = indicesA[i];
                xminsA[i]     = bucketA.xmins[index];
                xmaxsA[i]     = bucketA.xmaxs[index];
                yzminmaxsA[i] = bucketA.yzminmaxs[index];
            }
            xminsA[indicesA.Length] = float.NaN;

            var xminsB     = allocator.Allocate<float>(indicesB.Length + 1);
            var xmaxsB     = allocator.Allocate<float>(indicesB.Length);
            var yzminmaxsB = allocator.Allocate<float4>(indicesB.Length);

            for (int i = 0; i < indicesB.Length; i++)
            {
                var index     = indicesB[i];
                xminsB[i]     = bucketB.xmins[index];
                xmaxsB[i]     = bucketB.xmaxs[index];
                yzminmaxsB[i] = bucketB.yzminmaxs[index];
            }
            xminsB[indicesB.Length] = float.NaN;

            // Check for b starting in a's x range
            int bstart = 0;
            for (int i = 0; i < indicesA.Length; i++)
            {
                // Advance to b.xmin >= a.xmin
                // Include equals case by stopping when equal
                while (bstart < indicesB.Length && xminsB[bstart] < xminsA[i])
                    bstart++;
                if (bstart >= indicesB.Length)
                    break;

                var current = -yzminmaxsA[i].zwxy;
                var xmax    = xmaxsA[i];
                for (int j = bstart; j < indicesB.Length && xminsB[j] <= xmax; j++)
                {
                    if (math.bitmask(current < yzminmaxsB[j]) == 0)
                    {
                        result.SetBucketRelativePairIndices(indicesA[i], indicesB[j]);
                        processor.Execute(in result);
                    }
                }
            }

            // Check for a starting in b's x range
            int astart = 0;
            for (int i = 0; i < indicesB.Length; i++)
            {
                // Advance to a.xmin > b.xmin
                // Exclude equals case this time by continuing if equal
                while (astart < indicesA.Length && bucketA.xmins[astart] <= xminsB[i])
                    astart++;
                if (astart >= indicesA.Length)
                    break;

                var current = -yzminmaxsB[i].zwxy;
                var xmax    = xmaxsB[i];
                for (int j = astart; j < indicesA.Length && bucketA.xmins[j] <= xmax; j++)
                {
                    if (math.bitmask(current < bucketA.yzminmaxs[j]) == 0)
                    {
                        result.SetBucketRelativePairIndices(indicesA[j], indicesB[i]);
                        processor.Execute(in result);
                    }
                }
            }
        }

        static unsafe void BipartiteSweepDualIndicesFilteredCross<T>(ref FindPairsResult result,
                                                                     in BucketSlices bucketA,
                                                                     ReadOnlySpan<int>    indicesA,
                                                                     in BucketSlices bucketB,
                                                                     ReadOnlySpan<int>    indicesB,
                                                                     ref T processor,
                                                                     in BucketAabb cellAabb,
                                                                     bool cellIsB,
                                                                     ThreadStackAllocator allocator)
            where T : unmanaged, IFindPairsProcessor
        {
            var xminsA     = allocator.Allocate<float>(indicesA.Length + 1);
            var xmaxsA     = allocator.Allocate<float>(indicesA.Length);
            var yzminmaxsA = allocator.Allocate<float4>(indicesA.Length);
            if (cellIsB)
            {
                var newIndices = allocator.Allocate<int>(indicesA.Length);
                int newCount   = 0;
                for (int i = 0; i < indicesA.Length; i++)
                {
                    var index = indicesA[i];
                    if (cellAabb.xmax < bucketA.xmins[index])
                        break;
                    if (bucketA.xmaxs[i] < cellAabb.xmin)
                        continue;
                    if (math.bitmask((cellAabb.yzMinMaxFlipped < bucketA.yzminmaxs[i]) & cellAabb.finiteMask) == 0)
                    {
                        xminsA[newCount]     = bucketA.xmins[index];
                        xmaxsA[newCount]     = bucketA.xmaxs[index];
                        yzminmaxsA[newCount] = bucketA.yzminmaxs[index];
                        newIndices[newCount] = index;
                        newCount++;
                    }
                }
                indicesA = new ReadOnlySpan<int>(newIndices, newCount);
            }
            else
            {
                for (int i = 0; i < indicesA.Length; i++)
                {
                    var index     = indicesA[i];
                    xminsA[i]     = bucketA.xmins[index];
                    xmaxsA[i]     = bucketA.xmaxs[index];
                    yzminmaxsA[i] = bucketA.yzminmaxs[index];
                }
            }
            xminsA[indicesA.Length] = float.NaN;

            var xminsB     = allocator.Allocate<float>(indicesB.Length + 1);
            var xmaxsB     = allocator.Allocate<float>(indicesB.Length);
            var yzminmaxsB = allocator.Allocate<float4>(indicesB.Length);
            if (!cellIsB)
            {
                var newIndices = allocator.Allocate<int>(indicesB.Length);
                int newCount   = 0;
                for (int i = 0; i < indicesB.Length; i++)
                {
                    var index = indicesB[i];
                    if (cellAabb.xmax < bucketB.xmins[index])
                        break;
                    if (bucketB.xmaxs[i] < cellAabb.xmin)
                        continue;
                    if (math.bitmask((cellAabb.yzMinMaxFlipped < bucketB.yzminmaxs[i]) & cellAabb.finiteMask) == 0)
                    {
                        xminsB[newCount]     = bucketB.xmins[index];
                        xmaxsB[newCount]     = bucketB.xmaxs[index];
                        yzminmaxsB[newCount] = bucketB.yzminmaxs[index];
                        newIndices[newCount] = index;
                        newCount++;
                    }
                }
                indicesB = new ReadOnlySpan<int>(newIndices, newCount);
            }
            else
            {
                for (int i = 0; i < indicesB.Length; i++)
                {
                    var index     = indicesB[i];
                    xminsB[i]     = bucketB.xmins[index];
                    xmaxsB[i]     = bucketB.xmaxs[index];
                    yzminmaxsB[i] = bucketB.yzminmaxs[index];
                }
            }
            xminsB[indicesB.Length] = float.NaN;

            // Check for b starting in a's x range
            int bstart = 0;
            for (int i = 0; i < indicesA.Length; i++)
            {
                // Advance to b.xmin >= a.xmin
                // Include equals case by stopping when equal
                while (bstart < indicesB.Length && xminsB[bstart] < xminsA[i])
                    bstart++;
                if (bstart >= indicesB.Length)
                    break;

                var current = -yzminmaxsA[i].zwxy;
                var xmax    = xmaxsA[i];
                for (int j = bstart; j < indicesB.Length && xminsB[j] <= xmax; j++)
                {
                    if (math.bitmask(current < yzminmaxsB[j]) == 0)
                    {
                        result.SetBucketRelativePairIndices(indicesA[i], indicesB[j]);
                        processor.Execute(in result);
                    }
                }
            }

            // Check for a starting in b's x range
            int astart = 0;
            for (int i = 0; i < indicesB.Length; i++)
            {
                // Advance to a.xmin > b.xmin
                // Exclude equals case this time by continuing if equal
                while (astart < indicesA.Length && bucketA.xmins[astart] <= xminsB[i])
                    astart++;
                if (astart >= indicesA.Length)
                    break;

                var current = -yzminmaxsB[i].zwxy;
                var xmax    = xmaxsB[i];
                for (int j = astart; j < indicesA.Length && bucketA.xmins[j] <= xmax; j++)
                {
                    if (math.bitmask(current < bucketA.yzminmaxs[j]) == 0)
                    {
                        result.SetBucketRelativePairIndices(indicesA[j], indicesB[i]);
                        processor.Execute(in result);
                    }
                }
            }
        }
        #endregion

        #region Archetype Index Merging
        static unsafe bool GatherBothIndicesSets(in WorldBucket bucketA,
                                                 in CollisionWorld.Mask maskA,
                                                 out ReadOnlySpan<int>  indicesA,
                                                 in WorldBucket bucketB,
                                                 in CollisionWorld.Mask maskB,
                                                 out ReadOnlySpan<int>  indicesB,
                                                 ThreadStackAllocator allocator)
        {
            indicesA            = default;
            indicesB            = default;
            int archetypeCountA = 0;
            int bodyCountA      = 0;
            foreach (var index in maskA)
            {
                var asac = bucketA.archetypeStartsAndCounts[index];
                if (asac.y == 0)
                    continue;
                archetypeCountA++;
                bodyCountA += asac.y;
            }

            if (bodyCountA == 0)
                return false;

            int archetypeCountB = 0;
            int bodyCountB      = 0;
            foreach (var index in maskB)
            {
                var asac = bucketB.archetypeStartsAndCounts[index];
                if (asac.y == 0)
                    continue;
                archetypeCountB++;
                bodyCountB += asac.y;
            }

            if (bodyCountB == 0)
                return false;

            if (archetypeCountA == 1)
            {
                int2 asac = default;
                foreach (var index in maskA)
                {
                    var candidate = bucketA.archetypeStartsAndCounts[index];
                    if (candidate.y != 0)
                    {
                        asac = candidate;
                        break;
                    }
                }
                indicesA = bucketA.archetypeBodyIndices.GetSubArray(asac.x, asac.y).AsReadOnlySpan();
            }
            else
            {
                var asacs       = allocator.AllocateAsSpan<int2>(archetypeCountA);
                archetypeCountA = 0;
                foreach (var index in maskA)
                {
                    var asac = bucketA.archetypeStartsAndCounts[index];
                    if (asac.y != 0)
                    {
                        asacs[archetypeCountA] = asac;
                        archetypeCountA++;
                    }
                }
                var indices = allocator.AllocateAsSpan<int>(bodyCountA);
                GatherArchetypeIndicesGeneral(indices, asacs, bucketA.archetypeBodyIndices);
                indicesA = indices;
            }

            if (archetypeCountB == 1)
            {
                int2 asac = default;
                foreach (var index in maskB)
                {
                    var candidate = bucketB.archetypeStartsAndCounts[index];
                    if (candidate.y != 0)
                    {
                        asac = candidate;
                        break;
                    }
                }
                indicesB = bucketB.archetypeBodyIndices.GetSubArray(asac.x, asac.y).AsReadOnlySpan();
            }
            else
            {
                var asacs       = allocator.AllocateAsSpan<int2>(archetypeCountB);
                archetypeCountB = 0;
                foreach (var index in maskB)
                {
                    var asac = bucketB.archetypeStartsAndCounts[index];
                    if (asac.y != 0)
                    {
                        asacs[archetypeCountB] = asac;
                        archetypeCountB++;
                    }
                }
                var indices = allocator.AllocateAsSpan<int>(bodyCountB);
                GatherArchetypeIndicesGeneral(indices, asacs, bucketB.archetypeBodyIndices);
                indicesB = indices;
            }

            return true;
        }

        static unsafe void GatherArchetypeIndicesGeneral(Span<int> result, Span<int2> startsAndCounts, ReadOnlySpan<int> archetypeBodyIndices)
        {
            // Allocate tournament tree levels and initialize first games
            var levelCount        = math.ceillog2(startsAndCounts.Length);
            var levels            = stackalloc ulong*[levelCount];
            int combatantsInLevel = startsAndCounts.Length;
            var winnersScratch    = stackalloc ulong[startsAndCounts.Length];
            for (int i = 0; i < startsAndCounts.Length; i++)
            {
                var c               = (ulong)archetypeBodyIndices[startsAndCounts[i].x];
                c                 <<= 32;
                c                  |= (uint)i;
                winnersScratch[i]   = c;
            }

            // Allocate and play first games for each level
            for (int i = 0; i < levelCount; i++)
            {
                var games   = (combatantsInLevel + 1) / 2;
                var inLevel = stackalloc ulong[games];
                levels[i]   = inLevel;

                for (int j = 0; j < games; j++)
                {
                    var a             = winnersScratch[2 * j];
                    var bIndex        = 2 * j + 1;
                    var b             = bIndex < combatantsInLevel ? winnersScratch[bIndex] : ulong.MaxValue;
                    inLevel[j]        = math.max(a, b);
                    winnersScratch[j] = math.min(a, b);
                }
                combatantsInLevel = games;
            }

            // Output first winner
            var winner = winnersScratch[0];
            result[0]  = (int)(winner >> 32);

            // Stream all remaining games
            for (int i = 1; i < result.Length; i++)
            {
                var     streamOfPrevious = (int)(winner & 0xffffffff);
                ref var asac             = ref startsAndCounts[streamOfPrevious];
                asac.y--;
                asac.x++;
                if (asac.y == 0)
                    winner = ulong.MaxValue;
                else
                {
                    winner   = (ulong)archetypeBodyIndices[asac.x];
                    winner <<= 32;
                    winner  |= (uint)streamOfPrevious;
                }

                var gameIndex = streamOfPrevious;
                for (int j = 0; j < levelCount; j++)
                {
                    gameIndex          >>= 1;
                    var previousWinner   = winner;
                    var level            = levels[j];
                    var otherCombatant   = level[gameIndex];
                    winner               = math.min(otherCombatant, previousWinner);
                    level[gameIndex]     = math.max(otherCombatant, previousWinner);
                }

                result[i] = (int)(winner >> 32);
            }
        }
        #endregion
    }
}

