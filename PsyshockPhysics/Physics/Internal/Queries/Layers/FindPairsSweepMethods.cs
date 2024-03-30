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
        public static void SelfSweepCell<T>(in CollisionLayer layer,
                                            in BucketSlices bucket,
                                            int jobIndex,
                                            ref T processor,
                                            bool isAThreadSafe,
                                            bool isBThreadSafe,
                                            bool isImmediateContext = false) where T : struct, IFindPairsProcessor
        {
            if (bucket.count < 2)
                return;

            var result = new FindPairsResult(in layer, in layer, in bucket, in bucket, jobIndex, isAThreadSafe, isBThreadSafe, isImmediateContext);

            if (X86.Avx.IsAvxSupported)
            {
                SelfSweepWholeBucketAvx(ref result, bucket, ref processor);
            }
            else
            {
                SelfSweepWholeBucket(ref result, bucket, ref processor);
            }
        }

        public static void SelfSweepCross<T>(in CollisionLayer layer,
                                             in BucketSlices bucket,
                                             int jobIndex,
                                             ref T processor,
                                             bool isAThreadSafe,
                                             bool isBThreadSafe,
                                             bool isImmediateContext = false) where T : struct, IFindPairsProcessor
        {
            SelfSweepCell(in layer, in bucket, jobIndex, ref processor, isAThreadSafe, isBThreadSafe, isImmediateContext);
        }

        public static void BipartiteSweepCellCell<T>(in CollisionLayer layerA,
                                                     in CollisionLayer layerB,
                                                     in BucketSlices bucketA,
                                                     in BucketSlices bucketB,
                                                     int jobIndex,
                                                     ref T processor,
                                                     bool isAThreadSafe,
                                                     bool isBThreadSafe,
                                                     bool isImmediateContext = false) where T : struct, IFindPairsProcessor
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

        public static void BipartiteSweepCellCross<T>(in CollisionLayer layerA,
                                                      in CollisionLayer layerB,
                                                      in BucketSlices bucketA,
                                                      in BucketSlices bucketB,
                                                      int jobIndex,
                                                      ref T processor,
                                                      bool isAThreadSafe,
                                                      bool isBThreadSafe,
                                                      bool isImmediateContext = false) where T : struct, IFindPairsProcessor
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

        public static void BipartiteSweepCrossCell<T>(in CollisionLayer layerA,
                                                      in CollisionLayer layerB,
                                                      in BucketSlices bucketA,
                                                      in BucketSlices bucketB,
                                                      int jobIndex,
                                                      ref T processor,
                                                      bool isAThreadSafe,
                                                      bool isBThreadSafe,
                                                      bool isImmediateContext = false) where T : struct, IFindPairsProcessor
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

        public static void BipartiteSweepCrossCross<T>(in CollisionLayer layerA,
                                                       in CollisionLayer layerB,
                                                       in BucketSlices bucketA,
                                                       in BucketSlices bucketB,
                                                       int jobIndex,
                                                       ref T processor,
                                                       bool isAThreadSafe,
                                                       bool isBThreadSafe,
                                                       bool isImmediateContext = false) where T : struct, IFindPairsProcessor
        {
            BipartiteSweepCellCell(in layerA, in layerB, in bucketA, in bucketB, jobIndex, ref processor, isAThreadSafe, isBThreadSafe, isImmediateContext);
        }

        public static int BipartiteSweepPlayCache<T>(UnsafeIndexedBlockList.Enumerator enumerator,
                                                     in CollisionLayer layerA,
                                                     in CollisionLayer layerB,
                                                     int bucketIndexA,
                                                     int bucketIndexB,
                                                     int jobIndex,
                                                     ref T processor,
                                                     bool isAThreadSafe,
                                                     bool isBThreadSafe) where T : struct, IFindPairsProcessor
        {
            if (!enumerator.MoveNext())
                return 0;

            var result = FindPairsResult.CreateGlobalResult(in layerA, in layerB, bucketIndexA, bucketIndexB, jobIndex, isAThreadSafe, isBThreadSafe);
            int count  = 0;

            do
            {
                var indices = enumerator.GetCurrent<int2>();
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
        static void SelfSweepWholeBucket<T>(ref FindPairsResult result, in BucketSlices bucket, ref T processor) where T : struct, IFindPairsProcessor
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
                                                                  in BucketAabb bucketAabbForA) where T : struct, IFindPairsProcessor
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
                                                                  in BucketAabb bucketAabbForB) where T : struct, IFindPairsProcessor
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
        #endregion
    }
}

