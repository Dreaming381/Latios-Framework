using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace Latios
{
    public static class BlobBuilderExtensions
    {
        public static BlobBuilderArray<T> ConstructFromNativeArray<T>(this BlobBuilder builder, ref BlobArray<T> ptr, NativeArray<T> array) where T : struct
        {
            var result = builder.Allocate(ref ptr, array.Length);
            for (int i = 0; i < array.Length; i++)
                result[i] = array[i];
            return result;
        }

        unsafe public static void AllocateFixedString<T>(ref this BlobBuilder builder, ref BlobString blobStr, T fixedString) where T : INativeList<byte>, IUTF8Bytes
        {
            var res = builder.Allocate(ref UnsafeUtility.As<BlobString, BlobArray<byte> >(ref blobStr), fixedString.Length);
            for (int i = 0; i < fixedString.Length; i++)
            {
                res[i] = fixedString[i];
            }
        }
    }
}

namespace Latios.Unsafe
{
    public static class BlobBuilderUnsafeExtensions
    {
        public static unsafe BlobBuilderArray<T> ConstructFromNativeArray<T>(this BlobBuilder builder, ref BlobArray<T> ptr, T* array, int length) where T : unmanaged
        {
            var result = builder.Allocate(ref ptr, length);
            for (int i = 0; i < length; i++)
                result[i] = array[i];
            return result;
        }
    }
}

