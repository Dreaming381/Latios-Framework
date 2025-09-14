using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Latios.Calci
{
    public static unsafe class BinarySearch
    {
        /// <summary>
        /// Returns the first index in the array that is greater or equal to the requested value.
        /// If no value in the array is greater or equal to the search value, this method returns the length of the array.
        /// Elements in the array must be sorted.
        /// </summary>
        /// <param name="array">The sorted array to search in</param>
        /// <param name="searchValue">The value to search for</param>
        /// <returns>The first index greater or equal, or the length if all values are less than the search value</returns>
        public static int FirstGreaterOrEqual<T>(in ReadOnlySpan<T> array, T searchValue) where T : unmanaged, IComparable<T>
        {
            fixed (T* ptr = &array[0])
            return FirstGreaterOrEqual(ptr, array.Length, searchValue);
        }

        /// <summary>
        /// Returns the first index in the array that is greater or equal to the requested value.
        /// If no value in the array is greater or equal to the search value, this method returns the length of the array.
        /// Elements in the array must be sorted.
        /// </summary>
        /// <param name="array">The sorted array to search in</param>
        /// <param name="searchValue">The value to search for</param>
        /// <param name="comparer">A struct that defines the comparison operation</param>
        /// <returns>The first index greater or equal, or the length if all values are less than the search value</returns>
        public static int FirstGreaterOrEqual<T, U>(in ReadOnlySpan<T> array, T searchValue, U comparer) where T : unmanaged where U : unmanaged, IComparer<T>
        {
            fixed (T* ptr = &array[0])
            return FirstGreaterOrEqual(ptr, array.Length, searchValue, comparer);
        }

        /// <summary>
        /// Returns the first index in the array that is greater or equal to the requested value.
        /// If no value in the array is greater or equal to the search value, this method returns the length of the array.
        /// Elements in the array must be sorted.
        /// </summary>
        /// <param name="array">The sorted array to search in</param>
        /// <param name="searchValue">The value to search for</param>
        /// <returns>The first index greater or equal, or the length if all values are less than the search value</returns>
        public static int FirstGreaterOrEqual<T>(in NativeArray<T> array, T searchValue) where T : unmanaged, IComparable<T>
        {
            return FirstGreaterOrEqual((T*)array.GetUnsafeReadOnlyPtr(), array.Length, searchValue);
        }

        /// <summary>
        /// Returns the first index in the array that is greater or equal to the requested value.
        /// If no value in the array is greater or equal to the search value, this method returns the length of the array.
        /// Elements in the array must be sorted.
        /// </summary>
        /// <param name="array">The sorted array to search in</param>
        /// <param name="searchValue">The value to search for</param>
        /// <param name="comparer">A struct that defines the comparison operation</param>
        /// <returns>The first index greater or equal, or the length if all values are less than the search value</returns>
        public static int FirstGreaterOrEqual<T, U>(in NativeArray<T> array, T searchValue, U comparer) where T : unmanaged where U : unmanaged, IComparer<T>
        {
            return FirstGreaterOrEqual((T*)array.GetUnsafeReadOnlyPtr(), array.Length, searchValue, comparer);
        }

        /// <summary>
        /// Returns the first index in the array that is greater or equal to the requested value.
        /// If no value in the array is greater or equal to the search value, this method returns the length of the array.
        /// Elements in the array must be sorted.
        /// </summary>
        /// <param name="array">The sorted array to search in</param>
        /// <param name="arrayLength">The number of elements in the array</param>
        /// <param name="searchValue">The value to search for</param>
        /// <returns>The first index greater or equal, or the length if all values are less than the search value</returns>
        public static int FirstGreaterOrEqual<T>(T* array, [AssumeRange(0, int.MaxValue)] int arrayLength, T searchValue) where T : unmanaged, IComparable<T>
        {
            return FirstGreaterOrEqual<T, NativeSortExtension.DefaultComparer<T> >(array, arrayLength, searchValue, default);
        }

        /// <summary>
        /// Returns the first index in the array that is greater or equal to the requested value.
        /// If no value in the array is greater or equal to the search value, this method returns the length of the array.
        /// Elements in the array must be sorted.
        /// </summary>
        /// <param name="array">The sorted array to search in</param>
        /// <param name="arrayLength">The number of elements in the array</param>
        /// <param name="searchValue">The value to search for</param>
        /// <param name="comparer">A struct that defines the comparison operation</param>
        /// <returns>The first index greater or equal, or the length if all values are less than the search value</returns>
        public static int FirstGreaterOrEqual<T, U>(T* array, [AssumeRange(0, int.MaxValue)] int arrayLength, T searchValue, U comparer) where T : unmanaged where U : unmanaged,
        IComparer<T>
        {
            //   The implementation as follows is a C# and Burst adaptation of Paul-Virak Khuong and Pat Morin's
            //   optimized sequential order binary search: https://github.com/patmorin/arraylayout/blob/master/src/sorted_array.h
            //   This code is licensed under the Creative Commons Attribution 4.0 International License (CC BY 4.0)
            bool isBurst = true;
            SkipWithoutBurst(ref isBurst);
            if (isBurst)
            {
                for (int i = 1; i < arrayLength; i++)
                {
                    Hint.Assume(comparer.Compare(array[i], searchValue) >= 0);
                }
            }

            var  basePtr = array;
            uint n       = (uint)arrayLength;
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
                basePtr = comparer.Compare(*newPtr, searchValue) < 0 ? newPtr : basePtr;
            }

            if (comparer.Compare(*basePtr, searchValue) < 0)
                basePtr++;

            return (int)(basePtr - array);
        }

        [BurstDiscard]
        static void SkipWithoutBurst(ref bool isBurst) => isBurst = false;
    }
}

