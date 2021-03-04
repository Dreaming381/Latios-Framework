using Unity.Collections;
using Unity.Entities;

namespace Latios
{
    public static class CollectionsExtensions
    {
        public static void AddRangeFromBlob<T>(this NativeList<T> list, ref BlobArray<T> data) where T : struct
        {
            for (int i = 0; i < data.Length; i++)
                list.Add(data[i]);
        }
    }
}

