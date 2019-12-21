using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

//This code is copied from BufferHeader.cs in the Entities package.
//Its purpose is to get buffer metadata for EntityDataCopyKit.
namespace Latios
{
    [StructLayout(LayoutKind.Explicit)]
    internal unsafe struct FakeBufferHeader
    {
        public const int kMinimumCapacity = 8;

        [FieldOffset(0)] public byte* Pointer;
        [FieldOffset(8)] public int   Length;
        [FieldOffset(12)] public int  Capacity;

        public static byte* GetElementPointer(FakeBufferHeader* header)
        {
            if (header->Pointer != null)
                return header->Pointer;

            return (byte*)(header + 1);
        }

        public enum TrashMode
        {
            TrashOldData,
            RetainOldData
        }

        public static void EnsureCapacity(FakeBufferHeader* header, int count, int typeSize, int alignment, TrashMode trashMode, bool useMemoryInitPattern, byte memoryInitPattern)
        {
            if (header->Capacity >= count)
                return;

            int  newCapacity  = Math.Max(Math.Max(2 * header->Capacity, count), kMinimumCapacity);
            long newBlockSize = (long)newCapacity * typeSize;

            byte* oldData = GetElementPointer(header);
            byte* newData = (byte*)UnsafeUtility.Malloc(newBlockSize, alignment, Allocator.Persistent);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (useMemoryInitPattern)
            {
                if (trashMode == TrashMode.RetainOldData)
                {
                    var oldBlockSize  = (header->Capacity * typeSize);
                    var remainingSize = newBlockSize - oldBlockSize;
                    if (remainingSize > 0)
                    {
                        UnsafeUtility.MemSet(newData + oldBlockSize, memoryInitPattern, remainingSize);
                    }
                }
                else
                {
                    UnsafeUtility.MemSet(newData, memoryInitPattern, newBlockSize);
                }
            }
#endif
            if (trashMode == TrashMode.RetainOldData)
            {
                long oldBlockSize = (long)header->Capacity * typeSize;
                UnsafeUtility.MemCpy(newData, oldData, oldBlockSize);
            }

            // Note we're freeing the old buffer only if it was not using the internal capacity. Don't change this to 'oldData', because that would be a bug.
            if (header->Pointer != null)
            {
                UnsafeUtility.Free(header->Pointer, Allocator.Persistent);
            }

            header->Pointer  = newData;
            header->Capacity = newCapacity;
        }
    }
}

