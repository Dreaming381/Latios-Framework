using Unity.Collections;
using Unity.Entities;

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
    }
}

