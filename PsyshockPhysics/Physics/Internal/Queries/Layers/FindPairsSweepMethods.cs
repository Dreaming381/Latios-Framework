using System.Net.Sockets;
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
        #region Production Sweeps
        public static void SelfSweep<T>(in CollisionLayer layer, in BucketSlices bucket, int jobIndex, ref T processor, bool isThreadSafe = true) where T : struct,
        IFindPairsProcessor
        {
            if (X86.Avx.IsAvxSupported)
            {
                SelfSweepAvx(layer, bucket, jobIndex, ref processor, isThreadSafe);
                return;
            }

            Hint.Assume(bucket.xmins.Length == bucket.xmaxs.Length);
            Hint.Assume(bucket.xmins.Length == bucket.yzminmaxs.Length);
            Hint.Assume(bucket.xmins.Length == bucket.bodies.Length);

            var result = new FindPairsResult(in layer, in layer, in bucket, in bucket, jobIndex, isThreadSafe);

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

        static unsafe void SelfSweepAvx<T>(in CollisionLayer layer, in BucketSlices bucket, int jobIndex, ref T processor, bool isThreadSafe = true) where T : struct,
        IFindPairsProcessor
        {
            Hint.Assume(bucket.xmins.Length == bucket.xmaxs.Length);
            Hint.Assume(bucket.xmins.Length == bucket.yzminmaxs.Length);
            Hint.Assume(bucket.xmins.Length == bucket.bodies.Length);

            var result = new FindPairsResult(in layer, in layer, in bucket, in bucket, jobIndex, isThreadSafe);

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

        public static void BipartiteSweep<T>(in CollisionLayer layerA,
                                             in CollisionLayer layerB,
                                             in BucketSlices bucketA,
                                             in BucketSlices bucketB,
                                             int jobIndex,
                                             ref T processor,
                                             bool isThreadSafe = true) where T : struct,
        IFindPairsProcessor
        {
            if (X86.Avx.IsAvxSupported)
            {
                BipartiteSweepAvx(in layerA, in layerB, in bucketA, in bucketB, jobIndex, ref processor, isThreadSafe);
                return;
            }

            int countA = bucketA.xmins.Length;
            int countB = bucketB.xmins.Length;
            if (countA == 0 || countB == 0)
                return;

            Hint.Assume(bucketA.xmins.Length == bucketA.xmaxs.Length);
            Hint.Assume(bucketA.xmins.Length == bucketA.yzminmaxs.Length);
            Hint.Assume(bucketA.xmins.Length == bucketA.bodies.Length);

            Hint.Assume(bucketB.xmins.Length == bucketB.xmaxs.Length);
            Hint.Assume(bucketB.xmins.Length == bucketB.yzminmaxs.Length);
            Hint.Assume(bucketB.xmins.Length == bucketB.bodies.Length);

            var result = new FindPairsResult(in layerA, in layerB, in bucketA, in bucketB, jobIndex, isThreadSafe);

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

        static unsafe void BipartiteSweepAvx<T>(in CollisionLayer layerA,
                                                in CollisionLayer layerB,
                                                in BucketSlices bucketA,
                                                in BucketSlices bucketB,
                                                int jobIndex,
                                                ref T processor,
                                                bool isThreadSafe = true) where T : struct,
        IFindPairsProcessor
        {
            int countA = bucketA.xmins.Length;
            int countB = bucketB.xmins.Length;
            if (countA == 0 || countB == 0)
                return;

            Hint.Assume(bucketA.xmins.Length == bucketA.xmaxs.Length);
            Hint.Assume(bucketA.xmins.Length == bucketA.yzminmaxs.Length);
            Hint.Assume(bucketA.xmins.Length == bucketA.bodies.Length);

            Hint.Assume(bucketB.xmins.Length == bucketB.xmaxs.Length);
            Hint.Assume(bucketB.xmins.Length == bucketB.yzminmaxs.Length);
            Hint.Assume(bucketB.xmins.Length == bucketB.bodies.Length);

            var result = new FindPairsResult(in layerA, in layerB, in bucketA, in bucketB, jobIndex, isThreadSafe);

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
        #endregion

        #region Sweep Stats
        public static void SelfSweepStats(BucketSlices bucket, in FixedString128Bytes layerName)
        {
            int hitCount            = 0;
            int innerLoopEnterCount = 0;
            int innerLoopTestCount  = 0;
            int innerLoopRunMin     = int.MaxValue;
            int innerLoopRunMax     = 0;
            int innerLoopZHits      = 0;

            Hint.Assume(bucket.xmins.Length == bucket.xmaxs.Length);
            Hint.Assume(bucket.xmins.Length == bucket.yzminmaxs.Length);
            Hint.Assume(bucket.xmins.Length == bucket.bodies.Length);

            int count = bucket.xmins.Length;
            for (int i = 0; i < count - 1; i++)
            {
                int runCount = 0;
                var current  = -bucket.yzminmaxs[i].zwxy;
                for (int j = i + 1; j < count && bucket.xmins[j] <= bucket.xmaxs[i]; j++)
                {
                    runCount++;
                    //float4 less = math.shuffle(current,
                    //                           bucket.yzminmaxs[j],
                    //                           math.ShuffleComponent.RightZ,
                    //                           math.ShuffleComponent.RightW,
                    //                           math.ShuffleComponent.LeftZ,
                    //                           math.ShuffleComponent.LeftW
                    //                           );
                    //float4 more = math.shuffle(current,
                    //                           bucket.yzminmaxs[j],
                    //                           math.ShuffleComponent.LeftX,
                    //                           math.ShuffleComponent.LeftY,
                    //                           math.ShuffleComponent.RightX,
                    //                           math.ShuffleComponent.RightY
                    //                           );

                    if (math.bitmask(current < bucket.yzminmaxs[j]) == 0)
                    {
                        hitCount++;
                    }
                    if ((math.bitmask(current < bucket.yzminmaxs[j]) & 0xa) == 0)
                        innerLoopZHits++;
                    //if (less.y >= more.y && less.w >= more.w)
                    //    innerLoopZHits++;
                }
                if (runCount > 0)
                    innerLoopEnterCount++;
                innerLoopTestCount += runCount;
                innerLoopRunMax     = math.max(innerLoopRunMax, runCount);
                innerLoopRunMin     = math.min(innerLoopRunMin, runCount);
            }

            //SelfSweepDualGenAndStats(bucket, in layerName);
            UnityEngine.Debug.Log(
                $"FindPairs Self Sweep stats for layer {layerName} at bucket index {bucket.bucketIndex} and count {bucket.count}\nHits: {hitCount}, inner loop enters: {innerLoopEnterCount}, inner loop tests: {innerLoopTestCount}, inner loop run (min, max): ({innerLoopRunMin}, {innerLoopRunMax}), inner loop z hits: {innerLoopZHits}");
        }

        public static void BipartiteSweepStats(BucketSlices bucketA, in FixedString128Bytes layerNameA, BucketSlices bucketB, in FixedString128Bytes layerNameB)
        {
            int hitCountA            = 0;
            int innerLoopEnterCountA = 0;
            int innerLoopTestCountA  = 0;
            int innerLoopRunMinA     = int.MaxValue;
            int innerLoopRunMaxA     = 0;

            int hitCountB            = 0;
            int innerLoopEnterCountB = 0;
            int innerLoopTestCountB  = 0;
            int innerLoopRunMinB     = int.MaxValue;
            int innerLoopRunMaxB     = 0;

            int countA = bucketA.xmins.Length;
            int countB = bucketB.xmins.Length;
            if (countA == 0 || countB == 0)
                return;

            Hint.Assume(bucketA.xmins.Length == bucketA.xmaxs.Length);
            Hint.Assume(bucketA.xmins.Length == bucketA.yzminmaxs.Length);
            Hint.Assume(bucketA.xmins.Length == bucketA.bodies.Length);

            Hint.Assume(bucketB.xmins.Length == bucketB.xmaxs.Length);
            Hint.Assume(bucketB.xmins.Length == bucketB.yzminmaxs.Length);
            Hint.Assume(bucketB.xmins.Length == bucketB.bodies.Length);

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

                int runCount = 0;
                var current  = -bucketA.yzminmaxs[i].zwxy;
                for (int j = bstart; j < countB && bucketB.xmins[j] <= bucketA.xmaxs[i]; j++)
                {
                    runCount++;

                    if (math.bitmask(current < bucketB.yzminmaxs[j]) == 0)
                    {
                        hitCountA++;
                    }
                }
                if (runCount > 0)
                    innerLoopEnterCountA++;
                innerLoopTestCountA += runCount;
                innerLoopRunMaxA     = math.max(innerLoopRunMaxA, runCount);
                innerLoopRunMinA     = math.min(innerLoopRunMinA, runCount);
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

                int runCount = 0;
                var current  = -bucketB.yzminmaxs[i].zwxy;
                for (int j = astart; j < countA && bucketA.xmins[j] <= bucketB.xmaxs[i]; j++)
                {
                    runCount++;

                    if (math.bitmask(current < bucketA.yzminmaxs[j]) == 0)
                    {
                        hitCountB++;
                    }
                }
                if (runCount > 0)
                    innerLoopEnterCountB++;
                innerLoopTestCountB += runCount;
                innerLoopRunMaxB     = math.max(innerLoopRunMaxB, runCount);
                innerLoopRunMinB     = math.min(innerLoopRunMinB, runCount);
            }

            UnityEngine.Debug.Log(
                $"FindPairs Bipartite Sweep stats for layerA {layerNameA} at bucket index {bucketA.bucketIndex} and count {bucketA.count}; and layerB {layerNameB} at bucket index {bucketB.bucketIndex} and count {bucketB.count}\n::A SWEEP B::  Hits: {hitCountA}, inner loop enters: {innerLoopEnterCountA}, inner loop tests: {innerLoopTestCountA}, inner loop run (min, max): ({innerLoopRunMinA}, {innerLoopRunMaxA})\n::B SWEEP A::  Hits: {hitCountB}, inner loop enters: {innerLoopEnterCountB}, inner loop tests: {innerLoopTestCountB}, inner loop run (min, max): ({innerLoopRunMinB}, {innerLoopRunMaxB})");
        }
        #endregion

        #region Experimental Sweeps
        public unsafe interface IFindPairsDrainable
        {
            public void SetBuckets(CollisionLayer layerA, CollisionLayer layerB, BucketSlices bucketA, BucketSlices bucketB, int jobIndex, bool isThreadSafe);
            public ulong* AcquireDrainBuffer1024ForWrite();
            public void Drain(ulong count);
            public void DrainStats(ulong count);
            public void DirectInvoke(int a, int b);
        }

        public unsafe struct FindPairsProcessorDrain<T> : IFindPairsDrainable where T : struct, IFindPairsProcessor
        {
            public T processor;

            // Packed pairs. If [63:32] is negative, b[63:32], a[31:0] otherwise a[63:32], b[31:0]
            //public NativeArray<ulong> drainBuffer1024;
            fixed ulong     drainBuffer1024[1024];
            FindPairsResult m_result;
            ulong           m_combinedCount;

            public ulong maxCount;

            public void SetBuckets(CollisionLayer layerA, CollisionLayer layerB, BucketSlices bucketA, BucketSlices bucketB, int jobIndex, bool isThreadSafe)
            {
                m_result        = new FindPairsResult(layerA, layerB, bucketA, bucketB, jobIndex, isThreadSafe);
                m_combinedCount = (ulong)bucketA.count + (ulong)bucketB.count;
            }

            public ulong* AcquireDrainBuffer1024ForWrite()
            {
                fixed (ulong* ptr = drainBuffer1024)
                return ptr;
                //return (ulong*)drainBuffer1024.GetUnsafePtr();
            }

            public void DirectInvoke(int a, int b)
            {
                m_result.SetBucketRelativePairIndices(a, b);
                processor.Execute(in m_result);
            }

            public void Drain(ulong count)
            {
                maxCount += count;
                //var marker  = new Unity.Profiling.ProfilerMarker("Drain");

                //marker.Begin();

                for (ulong i = 0; i < count; i++)
                {
                    var   pair     = drainBuffer1024[(int)i];
                    ulong shiftedA = pair >> 32;

                    int b = (int)(pair & 0xffffffff);

                    if (shiftedA >= m_combinedCount)
                    {
                        int a = (int)(shiftedA - m_combinedCount);
                        m_result.SetBucketRelativePairIndices(b, a);
                        processor.Execute(in m_result);
                    }
                    else
                    {
                        int a = (int)shiftedA;
                        m_result.SetBucketRelativePairIndices(a, b);
                        processor.Execute(in m_result);
                    }
                }

                //marker.End();
            }

            public void DrainStats(ulong count)
            {
                maxCount += count;

                //var processNormal  = new Unity.Profiling.ProfilerMarker("Normal");
                //var processFlipped = new Unity.Profiling.ProfilerMarker("Flipped");

                for (ulong i = 0; i < count; i++)
                {
                    var   pair     = drainBuffer1024[(int)i];
                    ulong shiftedA = pair >> 32;

                    int b = (int)(pair & 0xffffffff);

                    if (shiftedA >= m_combinedCount)
                    {
                        int a = (int)(shiftedA - m_combinedCount);

                        //processFlipped.Begin();
                        m_result.SetBucketRelativePairIndices(b, a);
                        processor.Execute(in m_result);
                        //processFlipped.End();
                    }
                    else
                    {
                        int a = (int)shiftedA;
                        m_result.SetBucketRelativePairIndices(a, b);
                        //processNormal.Begin();
                        processor.Execute(in m_result);
                        //processNormal.End();
                    }
                }
            }
        }

        public static unsafe void SelfSweepUnrolled<T>(CollisionLayer layer, BucketSlices bucket, int jobIndex, ref T drain, bool isThreadSafe = true) where T : struct,
        IFindPairsDrainable
        {
            Hint.Assume(bucket.xmins.Length == bucket.xmaxs.Length);
            Hint.Assume(bucket.xmins.Length == bucket.yzminmaxs.Length);
            Hint.Assume(bucket.xmins.Length == bucket.bodies.Length);

            int   count        = bucket.xmins.Length;
            ulong nextHitIndex = 0;
            drain.SetBuckets(layer, layer, bucket, bucket, jobIndex, isThreadSafe);
            var hitCachePtr = drain.AcquireDrainBuffer1024ForWrite();
            var minMaxPtr   = (float4*)bucket.yzminmaxs.GetUnsafeReadOnlyPtr();
            var xminsPtr    = (float*)bucket.xmins.GetUnsafeReadOnlyPtr();

            for (int i = 0; i < bucket.xmins.Length - 1; i++)
            {
                float4 current = -minMaxPtr[i].zwxy;

                float currentX = bucket.xmaxs[i];

                ulong j = (ulong)i + 1;

                ulong pair  = (((ulong)i) << 32) | j;
                ulong final = (((ulong)i) << 32) | ((uint)bucket.xmins.Length);

                while (pair + 15 < final)
                {
                    if (Hint.Unlikely(xminsPtr[(j + 15)] >= currentX))
                        break;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtr[j]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtr[j + 1]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtr[j + 2]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtr[j + 3]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtr[j + 4]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtr[j + 5]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtr[j + 6]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtr[j + 7]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtr[j + 8]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtr[j + 9]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtr[j + 10]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtr[j + 11]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtr[j + 12]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtr[j + 13]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtr[j + 14]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtr[j + 15]) == 0)
                        nextHitIndex++;
                    pair++;
                    j += 16;

                    if (Hint.Unlikely(nextHitIndex >= 1008))
                    {
                        drain.Drain(nextHitIndex);
                        hitCachePtr  = drain.AcquireDrainBuffer1024ForWrite();
                        nextHitIndex = 0;
                    }
                }

                while (pair < final && xminsPtr[j] < currentX)
                {
                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtr[j]) == 0)
                        nextHitIndex++;
                    pair++;
                    j++;
                }

                if (nextHitIndex >= 1008)
                {
                    drain.Drain(nextHitIndex);
                    hitCachePtr  = drain.AcquireDrainBuffer1024ForWrite();
                    nextHitIndex = 0;
                }
            }

            if (nextHitIndex > 0)
                drain.Drain(nextHitIndex);
        }

        public static unsafe void BipartiteSweepUnrolled2<T>(CollisionLayer layerA,
                                                             CollisionLayer layerB,
                                                             BucketSlices bucketA,
                                                             BucketSlices bucketB,
                                                             int jobIndex,
                                                             ref T drain,
                                                             bool isThreadSafe = true) where T : struct,
        IFindPairsDrainable
        {
            int countA = bucketA.xmins.Length;
            int countB = bucketB.xmins.Length;
            if (countA == 0 || countB == 0)
                return;

            Hint.Assume(bucketA.xmins.Length == bucketA.xmaxs.Length);
            Hint.Assume(bucketA.xmins.Length == bucketA.yzminmaxs.Length);
            Hint.Assume(bucketA.xmins.Length == bucketA.bodies.Length);

            Hint.Assume(bucketB.xmins.Length == bucketB.xmaxs.Length);
            Hint.Assume(bucketB.xmins.Length == bucketB.yzminmaxs.Length);
            Hint.Assume(bucketB.xmins.Length == bucketB.bodies.Length);

            ulong nextHitIndex = 0;
            drain.SetBuckets(layerA, layerB, bucketA, bucketB, jobIndex, isThreadSafe);
            var hitCachePtr = drain.AcquireDrainBuffer1024ForWrite();
            var minMaxPtrA  = (float4*)bucketA.yzminmaxs.GetUnsafeReadOnlyPtr();
            var minMaxPtrB  = (float4*)bucketB.yzminmaxs.GetUnsafeReadOnlyPtr();

            //ulong tests = 0;

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

                float4 current = -minMaxPtrA[i].zwxy;

                float currentX = bucketA.xmaxs[i];

                ulong j = (ulong)bstart;

                ulong pair  = (((ulong)i) << 32) | j;
                ulong final = (((ulong)i) << 32) | ((uint)countB);

                while (pair + 3 < final)
                {
                    if (Hint.Unlikely(bucketB.xmins[(int)(j + 3)] >= currentX))
                        break;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrB[j]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrB[j + 1]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrB[j + 2]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrB[j + 3]) == 0)
                        nextHitIndex++;
                    pair++;

                    j += 4;

                    //tests += 16;

                    if (Hint.Unlikely(nextHitIndex >= 1008))
                    {
                        drain.Drain(nextHitIndex);
                        hitCachePtr  = drain.AcquireDrainBuffer1024ForWrite();
                        nextHitIndex = 0;
                    }
                }

                while (pair < final && bucketB.xmins[(int)j] < currentX)
                {
                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrB[j]) == 0)
                        nextHitIndex++;
                    pair++;
                    j++;
                    //tests++;
                }

                if (nextHitIndex >= 1008)
                {
                    drain.Drain(nextHitIndex);
                    hitCachePtr  = drain.AcquireDrainBuffer1024ForWrite();
                    nextHitIndex = 0;
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

                float4 current = -minMaxPtrB[i].zwxy;

                float currentX = bucketB.xmaxs[i];

                ulong j = (ulong)astart;

                ulong bucketsSum = (ulong)bucketA.count + (ulong)bucketB.count;

                ulong pair  = ((((ulong)i) + bucketsSum) << 32) | j;
                ulong final = ((((ulong)i) + bucketsSum) << 32) | ((uint)countA);

                while (pair + 3 < final)
                {
                    if (Hint.Unlikely(bucketA.xmins[(int)(j + 3)] >= currentX))
                        break;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrA[j]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrA[j + 1]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrA[j + 2]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrA[j + 3]) == 0)
                        nextHitIndex++;
                    pair++;

                    j += 4;
                    //tests += 16;

                    if (Hint.Unlikely(nextHitIndex >= 1008))
                    {
                        drain.Drain(nextHitIndex);
                        hitCachePtr  = drain.AcquireDrainBuffer1024ForWrite();
                        nextHitIndex = 0;
                    }
                }

                while (pair < final && bucketA.xmins[(int)j] < currentX)
                {
                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrA[j]) == 0)
                        nextHitIndex++;
                    pair++;
                    j++;
                    //tests++;
                }

                if (nextHitIndex >= 1008)
                {
                    drain.Drain(nextHitIndex);
                    hitCachePtr  = drain.AcquireDrainBuffer1024ForWrite();
                    nextHitIndex = 0;
                }
            }

            if (nextHitIndex > 0)
                drain.Drain(nextHitIndex);

            //if (tests > 10000)
            //    UnityEngine.Debug.Log($"Unrolled tests: {tests}");
        }

        public static unsafe void BipartiteSweepUnrolled<T>(CollisionLayer layerA,
                                                            CollisionLayer layerB,
                                                            in BucketSlices bucketA,
                                                            in BucketSlices bucketB,
                                                            int jobIndex,
                                                            ref T drain,
                                                            bool isThreadSafe = true) where T : struct,
        IFindPairsDrainable
        {
            int countA = bucketA.xmins.Length;
            int countB = bucketB.xmins.Length;
            if (countA == 0 || countB == 0)
                return;

            Hint.Assume(bucketA.xmins.Length == bucketA.xmaxs.Length);
            Hint.Assume(bucketA.xmins.Length == bucketA.yzminmaxs.Length);
            Hint.Assume(bucketA.xmins.Length == bucketA.bodies.Length);

            Hint.Assume(bucketB.xmins.Length == bucketB.xmaxs.Length);
            Hint.Assume(bucketB.xmins.Length == bucketB.yzminmaxs.Length);
            Hint.Assume(bucketB.xmins.Length == bucketB.bodies.Length);

            ulong nextHitIndex = 0;
            drain.SetBuckets(layerA, layerB, bucketA, bucketB, jobIndex, isThreadSafe);
            var hitCachePtr = drain.AcquireDrainBuffer1024ForWrite();
            var minMaxPtrA  = (float4*)bucketA.yzminmaxs.GetUnsafeReadOnlyPtr();
            var minMaxPtrB  = (float4*)bucketB.yzminmaxs.GetUnsafeReadOnlyPtr();
            var xminsPtrA   = (float*)bucketA.xmins.GetUnsafeReadOnlyPtr();
            var xminsPtrB   = (float*)bucketB.xmins.GetUnsafeReadOnlyPtr();

            //ulong tests = 0;

            //Check for b starting in a's x range
            int bstart = 0;
            for (int i = 0; i < countA; i++)
            {
                //Advance to b.xmin >= a.xmin
                //Include equals case by stopping when equal
                while (bstart < countB && xminsPtrB[bstart] < xminsPtrA[i])
                    bstart++;
                if (bstart >= countB)
                    break;

                float4 current = -minMaxPtrA[i].zwxy;

                float currentX = bucketA.xmaxs[i];

                ulong j = (ulong)bstart;

                ulong pair  = (((ulong)i) << 32) | j;
                ulong final = (((ulong)i) << 32) | ((uint)countB);

                while (Hint.Likely(pair + 15 < final))
                {
                    if (xminsPtrB[(j + 15)] >= currentX)
                        break;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrB[j]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrB[j + 1]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrB[j + 2]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrB[j + 3]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrB[j + 4]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrB[j + 5]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrB[j + 6]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrB[j + 7]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrB[j + 8]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrB[j + 9]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrB[j + 10]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrB[j + 11]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrB[j + 12]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrB[j + 13]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrB[j + 14]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrB[j + 15]) == 0)
                        nextHitIndex++;
                    pair++;
                    j += 16;

                    //tests += 16;

                    if (Hint.Unlikely(nextHitIndex >= 1008))
                    {
                        drain.Drain(nextHitIndex);
                        hitCachePtr  = drain.AcquireDrainBuffer1024ForWrite();
                        nextHitIndex = 0;
                    }
                }

                Hint.Assume((pair & 0xffffffff00000000) == (final & 0xffffffff00000000));
                Hint.Assume((pair & 0xffffffff) == j);
                Hint.Assume(j <= int.MaxValue);
                while (Hint.Likely(pair < final && xminsPtrB[j] < currentX))
                {
                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrB[j]) == 0)
                        nextHitIndex++;
                    pair++;
                    j++;
                    //tests++;
                }

                if (Hint.Unlikely(nextHitIndex >= 1008))
                {
                    drain.Drain(nextHitIndex);
                    hitCachePtr  = drain.AcquireDrainBuffer1024ForWrite();
                    nextHitIndex = 0;
                }
            }

            //Check for a starting in b's x range
            int astart = 0;
            for (int i = 0; i < countB; i++)
            {
                //Advance to a.xmin > b.xmin
                //Exclude equals case this time by continuing if equal
                while (astart < countA && xminsPtrA[astart] <= xminsPtrB[i])
                    astart++;
                if (astart >= countA)
                    break;

                float4 current = -minMaxPtrB[i].zwxy;

                float currentX = bucketB.xmaxs[i];

                ulong j = (ulong)astart;

                ulong bucketsSum = (ulong)bucketA.count + (ulong)bucketB.count;

                ulong pair  = ((((ulong)i) + bucketsSum) << 32) | j;
                ulong final = ((((ulong)i) + bucketsSum) << 32) | ((uint)countA);

                while (Hint.Likely(pair + 15 < final))
                {
                    if (xminsPtrA[(j + 15)] >= currentX)
                        break;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrA[j]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrA[j + 1]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrA[j + 2]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrA[j + 3]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrA[j + 4]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrA[j + 5]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrA[j + 6]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrA[j + 7]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrA[j + 8]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrA[j + 9]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrA[j + 10]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrA[j + 11]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrA[j + 12]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrA[j + 13]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrA[j + 14]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrA[j + 15]) == 0)
                        nextHitIndex++;
                    pair++;
                    j += 16;
                    //tests += 16;

                    if (Hint.Unlikely(nextHitIndex >= 1008))
                    {
                        drain.Drain(nextHitIndex);
                        hitCachePtr  = drain.AcquireDrainBuffer1024ForWrite();
                        nextHitIndex = 0;
                    }
                }

                Hint.Assume((pair & 0xffffffff00000000) == (final & 0xffffffff00000000));
                Hint.Assume((pair & 0xffffffff) == j);
                Hint.Assume(j <= int.MaxValue);
                while (Hint.Likely(pair < final && xminsPtrA[j] < currentX))
                {
                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrA[j]) == 0)
                        nextHitIndex++;
                    pair++;
                    j++;
                    //tests++;
                }

                if (nextHitIndex >= 1008)
                {
                    drain.Drain(nextHitIndex);
                    hitCachePtr  = drain.AcquireDrainBuffer1024ForWrite();
                    nextHitIndex = 0;
                }
            }

            if (nextHitIndex > 0)
                drain.Drain(nextHitIndex);

            //if (tests > 10000)
            //    UnityEngine.Debug.Log($"Unrolled tests: {tests}");
        }

        public static unsafe void BipartiteSweepUnrolledStats<T>(CollisionLayer layerA,
                                                                 CollisionLayer layerB,
                                                                 BucketSlices bucketA,
                                                                 BucketSlices bucketB,
                                                                 int jobIndex,
                                                                 ref T drain,
                                                                 bool isThreadSafe = true) where T : struct,
        IFindPairsDrainable
        {
            int countA = bucketA.xmins.Length;
            int countB = bucketB.xmins.Length;
            if (countA == 0 || countB == 0)
                return;

            Hint.Assume(bucketA.xmins.Length == bucketA.xmaxs.Length);
            Hint.Assume(bucketA.xmins.Length == bucketA.yzminmaxs.Length);
            Hint.Assume(bucketA.xmins.Length == bucketA.bodies.Length);

            Hint.Assume(bucketB.xmins.Length == bucketB.xmaxs.Length);
            Hint.Assume(bucketB.xmins.Length == bucketB.yzminmaxs.Length);
            Hint.Assume(bucketB.xmins.Length == bucketB.bodies.Length);

            ulong nextHitIndex = 0;
            drain.SetBuckets(layerA, layerB, bucketA, bucketB, jobIndex, isThreadSafe);
            var hitCachePtr = drain.AcquireDrainBuffer1024ForWrite();
            var minMaxPtrA  = (float4*)bucketA.yzminmaxs.GetUnsafeReadOnlyPtr();
            var minMaxPtrB  = (float4*)bucketB.yzminmaxs.GetUnsafeReadOnlyPtr();

            //ulong tests          = 0;
            var inner0Marker   = new Unity.Profiling.ProfilerMarker("Inner 0");
            var inner1Marker   = new Unity.Profiling.ProfilerMarker("Inner 1");
            var lead0Marker    = new Unity.Profiling.ProfilerMarker("Lead 0");
            var lead1Marker    = new Unity.Profiling.ProfilerMarker("Lead 1");
            var cleanup0Marker = new Unity.Profiling.ProfilerMarker("Cleanup 0");
            var cleanup1Marker = new Unity.Profiling.ProfilerMarker("Cleanup 1");
            var drain0Marker   = new Unity.Profiling.ProfilerMarker("Drain 0");
            var drain1Marker   = new Unity.Profiling.ProfilerMarker("Drain 1");
            var outer0Marker   = new Unity.Profiling.ProfilerMarker("Outer 0");
            var outer1Marker   = new Unity.Profiling.ProfilerMarker("Outer 1");

            outer0Marker.Begin();
            //Check for b starting in a's x range
            int bstart = 0;
            for (int i = 0; i < countA; i++)
            {
                //Advance to b.xmin >= a.xmin
                //Include equals case by stopping when equal
                //lead0Marker.Begin();
                while (bstart < countB && bucketB.xmins[bstart] < bucketA.xmins[i])
                    bstart++;
                //lead0Marker.End();
                if (bstart >= countB)
                    break;

                float4 current = -minMaxPtrA[i].zwxy;

                float currentX = bucketA.xmaxs[i];

                ulong j = (ulong)bstart;

                ulong pair  = (((ulong)i) << 32) | j;
                ulong final = (((ulong)i) << 32) | ((uint)countB);

                //inner0Marker.Begin();
                while (pair + 15 < final)
                {
                    if (Hint.Unlikely(bucketB.xmins[(int)(j + 15)] > currentX))
                        break;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrB[j]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrB[j + 1]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrB[j + 2]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrB[j + 3]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrB[j + 4]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrB[j + 5]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrB[j + 6]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrB[j + 7]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrB[j + 8]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrB[j + 9]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrB[j + 10]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrB[j + 11]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrB[j + 12]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrB[j + 13]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrB[j + 14]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrB[j + 15]) == 0)
                        nextHitIndex++;
                    pair++;
                    j += 16;

                    //tests += 16;

                    if (Hint.Unlikely(nextHitIndex >= 1008))
                    {
                        //drain0Marker.Begin();
                        drain.Drain(nextHitIndex);
                        //drain0Marker.End();
                        hitCachePtr  = drain.AcquireDrainBuffer1024ForWrite();
                        nextHitIndex = 0;
                    }

                    //if (nextHitIndex > 10)
                    //{
                    //    UnityEngine.Debug.Log($"i : {i}, j : {j}, drain: {nextHitIndex}");
                    //}
                }
                //inner0Marker.End();

                //cleanup0Marker.Begin();
                while (pair < final && bucketB.xmins[(int)j] <= currentX)
                {
                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrB[j]) == 0)
                        nextHitIndex++;
                    pair++;
                    j++;
                    //tests++;
                }
                //cleanup0Marker.End();

                if (nextHitIndex >= 1008)
                {
                    //drain0Marker.Begin();
                    drain.Drain(nextHitIndex);
                    //drain0Marker.End();
                    hitCachePtr  = drain.AcquireDrainBuffer1024ForWrite();
                    nextHitIndex = 0;
                }
            }

            outer0Marker.End();
            outer1Marker.Begin();

            //Check for a starting in b's x range
            int astart = 0;
            for (int i = 0; i < countB; i++)
            {
                //Advance to a.xmin > b.xmin
                //Exclude equals case this time by continuing if equal
                //lead1Marker.Begin();
                while (astart < countA && bucketA.xmins[astart] <= bucketB.xmins[i])
                    astart++;
                //lead1Marker.End();
                if (astart >= countA)
                    break;

                float4 current = -minMaxPtrB[i].zwxy;

                float currentX = bucketB.xmaxs[i];

                ulong j = (ulong)astart;

                ulong bucketsSum = (ulong)bucketA.count + (ulong)bucketB.count;

                ulong pair  = ((((ulong)i) + bucketsSum) << 32) | j;
                ulong final = ((((ulong)i) + bucketsSum) << 32) | ((uint)countA);

                //inner1Marker.Begin();
                while (pair + 15 < final)
                {
                    if (Hint.Unlikely(bucketA.xmins[(int)(j + 15)] > currentX))
                        break;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrA[j]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrA[j + 1]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrA[j + 2]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrA[j + 3]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrA[j + 4]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrA[j + 5]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrA[j + 6]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrA[j + 7]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrA[j + 8]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrA[j + 9]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrA[j + 10]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrA[j + 11]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrA[j + 12]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrA[j + 13]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrA[j + 14]) == 0)
                        nextHitIndex++;
                    pair++;

                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrA[j + 15]) == 0)
                        nextHitIndex++;
                    pair++;
                    j += 16;
                    //tests += 16;

                    if (Hint.Unlikely(nextHitIndex >= 1008))
                    {
                        //drain1Marker.Begin();
                        drain.Drain(nextHitIndex);
                        //drain1Marker.End();
                        hitCachePtr  = drain.AcquireDrainBuffer1024ForWrite();
                        nextHitIndex = 0;
                    }
                }
                //inner1Marker.End();

                //cleanup1Marker.Begin();
                while (pair < final && bucketA.xmins[(int)j] <= currentX)
                {
                    hitCachePtr[nextHitIndex] = pair;
                    if (math.bitmask(current < minMaxPtrA[j]) == 0)
                        nextHitIndex++;
                    pair++;
                    j++;
                    //tests++;
                }
                //cleanup1Marker.End();

                if (nextHitIndex >= 1008)
                {
                    //drain1Marker.Begin();
                    drain.Drain(nextHitIndex);
                    //drain1Marker.End();
                    hitCachePtr  = drain.AcquireDrainBuffer1024ForWrite();
                    nextHitIndex = 0;
                }
            }

            outer1Marker.End();

            drain0Marker.Begin();
            drain.Drain(nextHitIndex);
            drain0Marker.End();

            //if (tests > 10000)
            //    UnityEngine.Debug.Log($"Unrolled tests: {tests}");
        }
        #endregion

        #region Broken
        // Todo: Fix for sign flip
        static void SelfSweepDualGenAndStats(BucketSlices bucket, in FixedString128Bytes layerName)
        {
            if (bucket.count <= 1)
                return;

            var zToXMinsMaxes = new NativeArray<uint>(2 * bucket.count, Allocator.Temp);
            var xs            = new NativeArray<uint>(2 * bucket.count, Allocator.Temp);

            var xSort = new NativeArray<Sortable>(bucket.count * 2, Allocator.Temp);
            var zSort = new NativeArray<Sortable>(bucket.count * 2, Allocator.Temp);

            for (int i = 0; i < bucket.count; i++)
            {
                var xmin         = bucket.xmins[i];
                var xmax         = bucket.xmaxs[i];
                var minYZmaxYZ   = bucket.yzminmaxs[i];
                xSort[2 * i]     = new Sortable { f = xmin, index = (uint)i };
                xSort[2 * i + 1] = new Sortable { f = xmax, index = (uint)i + (uint)bucket.count };

                zSort[2 * i]     = new Sortable { f = minYZmaxYZ.y, index = (uint)i };
                zSort[2 * i + 1]                                          = new Sortable { f = minYZmaxYZ.w, index = (uint)i + (uint)bucket.count };
            }

            xSort.Sort();
            zSort.Sort();

            for (int i = 0; i < xSort.Length; i++)
            {
                xs[i]            = xSort[i].index;
                zToXMinsMaxes[i] = zSort[i].index;
            }

            var minYZmaxYZs = bucket.yzminmaxs;

            var zIntervals = new NativeList<ZInterval>(minYZmaxYZs.Length, Allocator.Temp);
            zIntervals.ResizeUninitialized(minYZmaxYZs.Length);

            var zBits = new NativeList<BitField64>(minYZmaxYZs.Length / 64 + 1, Allocator.Temp);
            zBits.Resize(minYZmaxYZs.Length / 64 + 1, NativeArrayOptions.ClearMemory);

            {
                int minBit = 0;
                int index  = 0;
                for (int i = 0; i < zToXMinsMaxes.Length; i++)
                {
                    if (zToXMinsMaxes[i] < minYZmaxYZs.Length)
                    {
                        ref var interval = ref zIntervals.ElementAt((int)zToXMinsMaxes[i]);
                        interval.index   = index;
                        interval.min     = minBit;
                        ref var bitField = ref zBits.ElementAt(index >> 6);
                        bitField.SetBits(index & 0x3f, true);
                        index++;
                    }
                    else
                    {
                        ref var interval = ref zIntervals.ElementAt((int)(zToXMinsMaxes[i] - (uint)minYZmaxYZs.Length));
                        interval.max     = index;
                        ref var bitField = ref zBits.ElementAt(interval.index >> 6);
                        bitField.SetBits(interval.index & 0x3f, false);
                        if (interval.index == minBit)
                        {
                            while (minBit <= index)
                            {
                                var scanBits = zBits.ElementAt(minBit >> 6);
                                var tzcnt    = scanBits.CountTrailingZeros();
                                if (tzcnt < 64)
                                {
                                    minBit = (minBit & ~0x3f) + tzcnt;
                                    break;
                                }
                                minBit = (minBit & ~0x3f) + 64;
                            }
                            minBit = math.min(minBit, index + 1);
                        }
                    }
                }
            }

            var zToXs = new NativeArray<int>(minYZmaxYZs.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            int hitCount            = 0;
            int innerLoopEnterCount = 0;
            int innerLoopTestCount  = 0;
            int innerLoopRunMin     = int.MaxValue;
            int innerLoopRunMax     = 0;
            int maxRunIntervalIndex = 0;
            int touchedZeroBitfield = 0;

            for (int i = 0; i < xs.Length; i++)
            {
                if (xs[i] < minYZmaxYZs.Length)
                {
                    int runCount = 0;

                    var interval    = zIntervals[(int)xs[i]];
                    int minBitfield = interval.min >> 6;
                    int maxBitfield = interval.max >> 6;
                    if (minBitfield == maxBitfield)
                    {
                        int minBit   = interval.min & 0x3f;
                        int maxBit   = interval.max & 0x3f;
                        var bitField = zBits[minBitfield];
                        if (minBit > 0)
                            bitField.SetBits(0, false, minBit);

                        if (bitField.Value == 0)
                            touchedZeroBitfield++;

                        for (var j = bitField.CountTrailingZeros(); j <= maxBit; bitField.SetBits(j, false), j = bitField.CountTrailingZeros())
                        {
                            runCount++;
                            var currentIndex = (int)xs[i];
                            var otherIndex   = zToXs[j + 64 * minBitfield];

                            float4 less = math.shuffle(minYZmaxYZs[currentIndex],
                                                       minYZmaxYZs[otherIndex],
                                                       math.ShuffleComponent.RightZ,
                                                       math.ShuffleComponent.RightW,
                                                       math.ShuffleComponent.LeftZ,
                                                       math.ShuffleComponent.LeftW
                                                       );
                            float4 more = math.shuffle(minYZmaxYZs[currentIndex],
                                                       minYZmaxYZs[otherIndex],
                                                       math.ShuffleComponent.LeftX,
                                                       math.ShuffleComponent.LeftY,
                                                       math.ShuffleComponent.RightX,
                                                       math.ShuffleComponent.RightY
                                                       );

                            if (math.bitmask(less < more) == 0)
                            {
                                //overlaps.Add(new EntityPair(entities[currentIndex], entities[otherIndex]));
                                hitCount++;
                            }
                        }
                    }
                    else
                    {
                        {
                            int minBit   = interval.min & 0x3f;
                            var bitField = zBits[minBitfield];
                            if (minBit > 0)
                                bitField.SetBits(0, false, minBit);

                            if (bitField.Value == 0)
                                touchedZeroBitfield++;

                            for (var j = bitField.CountTrailingZeros(); j < 64; bitField.SetBits(j, false), j = bitField.CountTrailingZeros())
                            {
                                runCount++;
                                var currentIndex = (int)xs[i];
                                var otherIndex   = zToXs[j + 64 * minBitfield];

                                float4 less = math.shuffle(minYZmaxYZs[currentIndex],
                                                           minYZmaxYZs[otherIndex],
                                                           math.ShuffleComponent.RightZ,
                                                           math.ShuffleComponent.RightW,
                                                           math.ShuffleComponent.LeftZ,
                                                           math.ShuffleComponent.LeftW
                                                           );
                                float4 more = math.shuffle(minYZmaxYZs[currentIndex],
                                                           minYZmaxYZs[otherIndex],
                                                           math.ShuffleComponent.LeftX,
                                                           math.ShuffleComponent.LeftY,
                                                           math.ShuffleComponent.RightX,
                                                           math.ShuffleComponent.RightY
                                                           );

                                if (math.bitmask(less < more) == 0)
                                {
                                    //overlaps.Add(new EntityPair(entities[currentIndex], entities[otherIndex]));
                                    hitCount++;
                                }
                            }
                        }

                        for (int k = minBitfield + 1; k < maxBitfield; k++)
                        {
                            var bitField = zBits[k];

                            if (bitField.Value == 0)
                                touchedZeroBitfield++;

                            for (var j = bitField.CountTrailingZeros(); j < 64; bitField.SetBits(j, false), j = bitField.CountTrailingZeros())
                            {
                                runCount++;
                                var currentIndex = (int)xs[i];
                                var otherIndex   = zToXs[j + 64 * k];

                                float4 less = math.shuffle(minYZmaxYZs[currentIndex],
                                                           minYZmaxYZs[otherIndex],
                                                           math.ShuffleComponent.RightZ,
                                                           math.ShuffleComponent.RightW,
                                                           math.ShuffleComponent.LeftZ,
                                                           math.ShuffleComponent.LeftW
                                                           );
                                float4 more = math.shuffle(minYZmaxYZs[currentIndex],
                                                           minYZmaxYZs[otherIndex],
                                                           math.ShuffleComponent.LeftX,
                                                           math.ShuffleComponent.LeftY,
                                                           math.ShuffleComponent.RightX,
                                                           math.ShuffleComponent.RightY
                                                           );

                                if (math.bitmask(less < more) == 0)
                                {
                                    //overlaps.Add(new EntityPair(entities[currentIndex], entities[otherIndex]));
                                    hitCount++;
                                }
                            }
                        }

                        {
                            int maxBit   = interval.max & 0x3f;
                            var bitField = zBits[maxBitfield];

                            if (bitField.Value == 0)
                                touchedZeroBitfield++;

                            for (var j = bitField.CountTrailingZeros(); j <= maxBit; bitField.SetBits(j, false), j = bitField.CountTrailingZeros())
                            {
                                runCount++;
                                var currentIndex = (int)xs[i];
                                var otherIndex   = zToXs[j + 64 * maxBitfield];

                                float4 less = math.shuffle(minYZmaxYZs[currentIndex],
                                                           minYZmaxYZs[otherIndex],
                                                           math.ShuffleComponent.RightZ,
                                                           math.ShuffleComponent.RightW,
                                                           math.ShuffleComponent.LeftZ,
                                                           math.ShuffleComponent.LeftW
                                                           );
                                float4 more = math.shuffle(minYZmaxYZs[currentIndex],
                                                           minYZmaxYZs[otherIndex],
                                                           math.ShuffleComponent.LeftX,
                                                           math.ShuffleComponent.LeftY,
                                                           math.ShuffleComponent.RightX,
                                                           math.ShuffleComponent.RightY
                                                           );

                                if (math.bitmask(less < more) == 0)
                                {
                                    //overlaps.Add(new EntityPair(entities[currentIndex], entities[otherIndex]));
                                    hitCount++;
                                }
                            }
                        }
                    }

                    ref var currentBitfield = ref zBits.ElementAt(interval.index >> 6);
                    currentBitfield.SetBits(interval.index & 0x3f, true);
                    zToXs[interval.index] = (int)xs[i];

                    if (runCount > 0)
                        innerLoopEnterCount++;
                    innerLoopTestCount += runCount;
                    if (runCount > innerLoopRunMax)
                        maxRunIntervalIndex = (int)xs[i];
                    innerLoopRunMax         = math.max(innerLoopRunMax, runCount);
                    innerLoopRunMin         = math.min(innerLoopRunMin, runCount);
                }
                else
                {
                    var     interval        = zIntervals[(int)(xs[i] - minYZmaxYZs.Length)];
                    ref var currentBitfield = ref zBits.ElementAt(interval.index >> 6);
                    currentBitfield.SetBits(interval.index & 0x3f, false);
                }
            }
            var maxInterval = zIntervals[maxRunIntervalIndex];

            UnityEngine.Debug.Log(
                $"Dual Self Sweep stats for layer {layerName} at bucket index {bucket.bucketIndex} and count {bucket.count}\nHits: {hitCount}, inner loop enters: {innerLoopEnterCount}, inner loop tests: {innerLoopTestCount}, inner loop run (min, max): ({innerLoopRunMin}, {innerLoopRunMax}), maxInterval: ({maxInterval.min}, {maxInterval.index}, {maxInterval.max}), touched zero bitfields: {touchedZeroBitfield}");
        }

        struct ZInterval
        {
            public int index;
            public int min;
            public int max;
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
        #endregion
    }
}

