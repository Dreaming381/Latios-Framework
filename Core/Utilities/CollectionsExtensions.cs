using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;

namespace Latios
{
    public static class CollectionsExtensions
    {
        public static void AddRangeFromBlob<T>(this NativeList<T> list, ref BlobArray<T> data) where T : unmanaged
        {
            for (int i = 0; i < data.Length; i++)
                list.Add(data[i]);
        }

        public static NativeList<T> Clone<T>(this NativeList<T> list, AllocatorManager.AllocatorHandle allocator) where T : unmanaged
        {
            var result = new NativeList<T>(list.Length, allocator);
            result.AddRangeNoResize(list);
            return result;
        }

        public static unsafe JobHandle CombineDependencies(Span<JobHandle> jobHandles)
        {
            return JobHandleUnsafeUtility.CombineDependencies((JobHandle*)UnsafeUtility.AddressOf(ref jobHandles[0]), jobHandles.Length);
        }

        public static SharedStatic<T> Initialize<T>(this SharedStatic<T> s, in T t) where T : struct
        {
            s.Data = t;
            return s;
        }
    }
}

