using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    [BurstCompile]
    internal static class LayerQuerySweepMethods
    {
        public static void AabbSweep<T>(Aabb aabb, CollisionLayer layer, ref T processor) where T : struct, IFindObjectsProcessor
        {
            if (math.any(math.isnan(aabb.min) | math.isnan(aabb.max)))
                return;

            int3 minBucket = math.int3(math.floor((aabb.min - layer.worldMin) / layer.worldAxisStride));
            int3 maxBucket = math.int3(math.floor((aabb.max - layer.worldMin) / layer.worldAxisStride));
            minBucket      = math.clamp(minBucket, 0, layer.worldSubdivisionsPerAxis - 1);
            maxBucket      = math.clamp(maxBucket, 0, layer.worldSubdivisionsPerAxis - 1);

            for (int i = minBucket.x; i <= maxBucket.x; i++)
            {
                for (int j = minBucket.y; j <= maxBucket.y; j++)
                {
                    for (int k = minBucket.z; k <= maxBucket.z; k++)
                    {
                        var bucketIndex = (i * layer.worldSubdivisionsPerAxis.y + j) * layer.worldSubdivisionsPerAxis.z + k;
                        AabbSweepBucket(aabb, layer, layer.GetBucketSlices(bucketIndex), ref processor);
                    }
                }
            }

            AabbSweepBucket(aabb, layer, layer.GetBucketSlices(layer.BucketCount - 1), ref processor);
        }

        private static void AabbSweepBucket<T>(Aabb aabb, CollisionLayer layer, BucketSlices bucket, ref T processor) where T : struct, IFindObjectsProcessor
        {
            if (bucket.count == 0)
                return;

            var context = new AabbSweepRecursiveContext
            {
                bucket    = bucket,
                qxmin     = aabb.min.x,
                qyzMinMax = new float4(aabb.max.yz, -aabb.min.yz),
                result    = new FindObjectsResult(layer, bucket, 0, false)
            };

            var qxmax = aabb.max.x;

            var linearSweepStartIndex = BinarySearchFirstGreaterOrEqual(in bucket.xmins, context.qxmin);

            for (int indexInBucket = linearSweepStartIndex; indexInBucket < bucket.count && bucket.xmins[indexInBucket] <= qxmax; indexInBucket++)
            {
                if (math.bitmask(context.qyzMinMax < bucket.yzminmaxs[indexInBucket]) == 0)
                {
                    context.result.SetBucketRelativeIndex(indexInBucket);
                    processor.Execute(in context.result);
                }
            }

            SearchTree(0, ref context, ref processor);
        }

        private static unsafe int BinarySearchFirstGreaterOrEqual(in NativeArray<float> array, float searchValue)
        {
            return BinarySearchFirstGreaterOrEqual((float*)array.GetUnsafeReadOnlyPtr(), array.Length, searchValue);
        }

        // Returns count if nothing is greater or equal
        //   The following function is a C# and Burst adaptation of Paul-Virak Khuong and Pat Morin's
        //   optimized sequential order binary search: https://github.com/patmorin/arraylayout/blob/master/src/sorted_array.h
        //   This code is licensed under the Creative Commons Attribution 4.0 International License (CC BY 4.0)
        private static unsafe int BinarySearchFirstGreaterOrEqual(float* array, [AssumeRange(0, int.MaxValue)] int count, float searchValue)
        {
            for (int i = 1; i < count; i++)
            {
                Hint.Assume(array[i] >= array[i - 1]);
            }

            var  basePtr = array;
            uint n       = (uint)count;
            while (Hint.Likely(n > 1))
            {
                var half    = n / 2;
                n          -= half;
                var newPtr  = &basePtr[half];

                // As of Burst 1.8.0 prev 2
                // Burst never loads &basePtr[half] into a register for newPtr, and instead uses dual register addressing instead.
                // Because of this, instead of loading into the register, performing the comparison, using a cmov, and then a jump,
                // Burst immediately performs the comparison, conditionally jumps, uses a lea, and then a jump.
                // This is technically less instructions on average. But branch prediction may suffer as a result.
                basePtr = *newPtr < searchValue ? newPtr : basePtr;
            }

            if (*basePtr < searchValue)
                basePtr++;

            return (int)(basePtr - array);
        }

        private struct AabbSweepRecursiveContext
        {
            public FindObjectsResult result;
            public BucketSlices      bucket;
            public float4            qyzMinMax;
            public float             qxmin;
        }

        private static void SearchTree<T>(uint currentIndex, ref AabbSweepRecursiveContext context, ref T processor) where T : struct, IFindObjectsProcessor
        {
            if (currentIndex >= context.bucket.count)
                return;

            var node = context.bucket.intervalTree[(int)currentIndex];
            if (context.qxmin >= node.subtreeXmax)
                return;

            SearchTree(GetLeftChildIndex(currentIndex), ref context, ref processor);

            if (context.qxmin < node.xmin)
                return;

            if (context.qxmin > node.xmin && context.qxmin <= node.xmax)
            {
                if (math.bitmask(context.qyzMinMax < context.bucket.yzminmaxs[node.bucketRelativeBodyIndex]) == 0)
                {
                    context.result.SetBucketRelativeIndex(node.bucketRelativeBodyIndex);
                    processor.Execute(in context.result);
                }
            }

            SearchTree(GetRightChildIndex(currentIndex), ref context, ref processor);
        }

        public static uint GetLeftChildIndex(uint currentIndex) => 2 * currentIndex + 1;
        public static uint GetRightChildIndex(uint currentIndex) => 2 * currentIndex + 2;
        public static uint GetParentIndex(uint currentIndex) => (currentIndex - 1) / 2;
    }
}

