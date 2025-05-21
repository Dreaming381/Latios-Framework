using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Unsafe
{
    /// <summary>
    /// A virtual allocator for large buffers (typically graphics buffers) which must be packed
    /// with small arrays which are added and removed infrequently. This algorithm uses a best-fit
    /// search and is not constant time for allocations and deallocations. If speed is preferred
    /// over entropy reduction, a different allocator should be used.
    ///
    /// For this allocator, the strategy is to append all deallocated ranges to the end of the list,
    /// then call CoellesceGaps. And finally, make new allocations.
    /// </summary>
    public static class GapAllocator
    {
        /// <summary>
        /// Merges gaps together to be fit for future allocations.
        /// </summary>
        /// <param name="gaps">All gaps in any order</param>
        /// <param name="oldSize">The old size of the buffer (or used portion of it)</param>
        /// <returns>The new size of the buffer (or used portion of it)</returns>
        public static uint CoellesceGaps(NativeList<uint2> gaps, uint oldSize)
        {
            gaps.Sort(new GapSorter());
            int dst   = 1;
            var array = gaps.AsArray();
            for (int j = 1; j < array.Length; j++)
            {
                array[dst] = array[j];
                var prev   = array[dst - 1];
                if (prev.x + prev.y == array[j].x)
                {
                    prev.y         += array[j].y;
                    array[dst - 1]  = prev;
                }
                else
                    dst++;
            }

            gaps.Length = dst;

            if (!gaps.IsEmpty)
            {
                var backItem = gaps[gaps.Length - 1];
                if (backItem.x + backItem.y == oldSize)
                {
                    gaps.Length--;
                    return backItem.x;
                }
            }

            return oldSize;
        }

        /// <summary>
        /// Tries to allocate into the buffer using the best-fit algorithm.
        /// </summary>
        /// <param name="gaps">The gaps, after coellescing</param>
        /// <param name="countNeeded">The number of elements needed in the buffer to allocate</param>
        /// <param name="bufferUsedSize">The amount of buffer used, which may increase with the allocation</param>
        /// <param name="newLocation">The index of the buffer where the first element of the new allocation should go</param>
        /// <param name="bufferMaxSize">The max size of the buffer</param>
        /// <returns>True if the allocation was successful, false if it resulted in an overflow of the buffer's max size</returns>
        public static bool TryAllocate(NativeList<uint2> gaps, uint countNeeded, ref uint bufferUsedSize, out uint newLocation, uint bufferMaxSize = uint.MaxValue)
        {
            bool overflow = false;
            if (!AllocateInGap(gaps, countNeeded, out var result))
            {
                result          = bufferUsedSize;
                bufferUsedSize += countNeeded;
                if (bufferUsedSize > bufferMaxSize)
                {
                    overflow        = true;
                    bufferUsedSize -= countNeeded;
                }
            }
            newLocation = result;
            return !overflow;
        }

        static bool AllocateInGap(NativeList<uint2> gaps, uint countNeeded, out uint foundIndex)
        {
            int  bestIndex = -1;
            uint bestCount = uint.MaxValue;

            for (int i = 0; i < gaps.Length; i++)
            {
                if (gaps[i].y >= countNeeded && gaps[i].y < bestCount)
                {
                    bestIndex = i;
                    bestCount = gaps[i].y;
                }
            }

            if (bestIndex < 0)
            {
                foundIndex = 0;
                return false;
            }

            if (bestCount == countNeeded)
            {
                foundIndex = gaps[bestIndex].x;
                gaps.RemoveAtSwapBack(bestIndex);
                return true;
            }

            foundIndex       = gaps[bestIndex].x;
            var bestGap      = gaps[bestIndex];
            bestGap.x       += countNeeded;
            bestGap.y       -= countNeeded;
            gaps[bestIndex]  = bestGap;
            return true;
        }

        struct GapSorter : IComparer<uint2>
        {
            public int Compare(uint2 a, uint2 b)
            {
                return a.x.CompareTo(b.x);
            }
        }
    }
}

