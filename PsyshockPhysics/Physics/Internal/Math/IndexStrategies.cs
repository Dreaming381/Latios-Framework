using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class IndexStrategies
    {
        #region CollisionLayer
        public static int CellCountFromSubdivisionsPerAxis(int3 subdivisionsPerAxis)
        {
            ValidateSubdivisions(subdivisionsPerAxis);
            return subdivisionsPerAxis.x * subdivisionsPerAxis.y * subdivisionsPerAxis.z;
        }

        public static int BucketCountWithoutNaN(int cellCount)
        {
            ValidateCellCountPositive(cellCount);
            return math.select(1, cellCount + 1, cellCount > 1);
        }

        public static int BucketCountWithNaN(int cellCount) => BucketCountWithoutNaN(cellCount) + 1;

        public static int BucketCountWithoutNaNFromBucketCountWithNaN(int bucketCountWithNaN)
        {
            ValidateBucketCountWithNaNSufficient(bucketCountWithNaN);
            return bucketCountWithNaN - 1;
        }

        public static int CellCountFromBucketCountWithoutNaN(int bucketCountWithoutNaN)
        {
            ValidateCellCountPositive(bucketCountWithoutNaN);
            return math.max(bucketCountWithoutNaN - 1, 1);
        }

        // If cell count = 1, this will point to the same index as the cell.
        public static int CrossBucketIndex(int cellCount) => BucketCountWithoutNaN(cellCount) - 1;

        public static int NanBucketIndex(int cellCount) => BucketCountWithoutNaN(cellCount);

        public static int CellIndexFromSubdivisionIndices(int3 subdivisionIndices, int3 subdivisionsPerAxis)
        {
            ValidateSubdivisions(subdivisionsPerAxis);
            return (subdivisionIndices.x * subdivisionsPerAxis.y + subdivisionIndices.y) * subdivisionsPerAxis.z + subdivisionIndices.z;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void ValidateSubdivisions(int3 subdivisionsPerAxis)
        {
            if (math.any(subdivisionsPerAxis < 1))
                throw new InvalidOperationException("The number of subdivisions for each axis must be 1 or greater.");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void ValidateCellCountPositive(int cellCount)
        {
            if (cellCount < 1)
                throw new InvalidOperationException("The number of cells must be 1 or greater.");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void ValidateBucketCountWithNaNSufficient(int bucketCountWithNaN)
        {
            if (bucketCountWithNaN < 2)
                throw new InvalidOperationException("Bucket count including NaN must be 2 or greater.");
        }
        #endregion

        #region FindPairs
        public static int JobIndicesFromSingleLayerFindPairs(int cellCount) => BucketCountWithoutNaN(cellCount) * 2 - 1;

        public static int JobIndicesFromDualLayerFindPairs(int cellCount) => BucketCountWithoutNaN(cellCount) * 3 - 2;

        public static bool ScheduleParallelShouldActuallyBeSingle(int cellCount)
        {
            ValidateCellCountPositive(cellCount);
            return cellCount == 1;
        }

        public static int Part1Count(int cellCount) => BucketCountWithoutNaN(cellCount);

        public static int SingleLayerPart2Count(int cellCount)
        {
            ValidateCellCountPositive(cellCount);
            return cellCount;
        }

        public static int ParallelByACrossCount(int cellCount)
        {
            ValidateCellCountPositive(cellCount);
            return cellCount;
        }

        public static int ParallelPart2ACount(int cellCount)
        {
            ValidateCellCountPositive(cellCount);
            return cellCount;
        }

        public static int ParallelPart2BCount(int cellCount)
        {
            ValidateCellCountPositive(cellCount);
            return cellCount;
        }
        #endregion

        #region PairStream
        public static int PairStreamIndexCount(int cellCount)
        {
            ValidateCellCountPositive(cellCount);
            if (cellCount == 1)
                return 2;
            return cellCount * 7 + 3;
        }

        public static int NaNStreamIndex(int cellCount)
        {
            ValidateCellCountPositive(cellCount);
            if (cellCount == 1)
                return 1;
            return cellCount * 7 + 1;
        }

        public static int IslandAggregateStreamIndex(int cellCount)
        {
            // Should never be used when cellCount == 1
            ValidateCellCountPositive(cellCount);
            return cellCount * 7 + 2;
        }

        public static int FirstStreamIndexFromBucketIndex(int bucketIndex, int cellCount)
        {
            if (bucketIndex == NanBucketIndex(cellCount))
                return NaNStreamIndex(cellCount);
            return bucketIndex * 3;
        }

        public static int FirstMixedStreamIndex(int cellCount)
        {
            ValidateCellCountPositive(cellCount);
            if (cellCount == 1)
                return 0;
            return cellCount * 5 + 1;
        }

        public static int BucketStreamIndexFromFindPairsJobIndex(int bucketIndex, int jobIndex, int cellCount)
        {
            var result = FirstStreamIndexFromBucketIndex(bucketIndex, cellCount);
            if (cellCount == 1)
                return 0;
            if (bucketIndex == CrossBucketIndex(cellCount))
            {
                return result + jobIndex - cellCount;
            }

            if (jobIndex >= Part1Count(cellCount))
                result++;
            if (jobIndex >= Part1Count(cellCount) + ParallelPart2ACount(cellCount))
                result++;
            return result;
        }

        public static int MixedStreamIndexFromFindPairsJobIndex(int jobIndex, int cellCount)
        {
            var firstMixedStreamIndex = FirstMixedStreamIndex(cellCount);
            if (cellCount == 1)
                return 0;
            return firstMixedStreamIndex + jobIndex - Part1Count(cellCount);
        }

        public static int MixedStreamCount(int cellCount)
        {
            ValidateCellCountPositive(cellCount);
            if (cellCount == 1)
                return 0;
            return cellCount * 2;
        }

        public static int StreamCountFromBucketIndex(int bucketIndex, int cellCount)
        {
            if (bucketIndex == NanBucketIndex(cellCount))
                return 1;
            if (cellCount == 1)
                return 1;
            if (bucketIndex == CrossBucketIndex(cellCount))
                return cellCount * 2 + 1;
            return 3;
        }

        public static int BucketStreamCount(int cellCount)
        {
            return math.max(FirstMixedStreamIndex(cellCount), 1);
        }
        #endregion
    }
}

