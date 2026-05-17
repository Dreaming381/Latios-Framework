using System;
using Latios.Unsafe;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Latios.AuxEcs
{
    internal unsafe struct ComponentStore : IDisposable
    {
        UnsafePtrList<byte>         chunkPtrs;
        UnsafePtrList<int>          chunkVersionPtrs;
        UnsafeList<int>             freelist;
        long                        hash;
        int                         elementsPerChunk;
        int                         elementSize;
        int                         elementAlignment;
        int                         elementCount;
        IAuxDisposable.VPtrFunction disposePtr;

        public ComponentStore(int elementSize, int elementAlignment, long hash, AllocatorManager.AllocatorHandle allocator)
        {
            chunkPtrs             = new UnsafePtrList<byte>(8, allocator);
            chunkVersionPtrs      = new UnsafePtrList<int>(8, allocator);
            freelist              = new UnsafeList<int>(16, allocator);
            this.hash             = hash;
            elementsPerChunk      = CollectionHelper.Align(math.max(1, 1024 / elementSize), 16);
            this.elementSize      = elementSize;
            this.elementAlignment = elementAlignment;
            elementCount          = 0;
            IAuxDisposable.TryGetVptrFunctionFrom(hash, out disposePtr);
        }

        public void Dispose()
        {
            if (!disposePtr.Equals(default))
            {
                for (int chunkIndex = 0; chunkIndex < chunkPtrs.Length; chunkIndex++)
                {
                    var chunk    = chunkPtrs[chunkIndex];
                    var versions = chunkVersionPtrs[chunkIndex];
                    for (int i = 0; i < elementsPerChunk; i++)
                    {
                        if ((versions[i] & 1) == 1)
                        {
                            var ptr = new IAuxDisposable.VPtr(chunk + i * elementSize, disposePtr);
                            ptr.Dispose();
                        }
                    }
                }
            }

            var allocator = chunkPtrs.Allocator;
            // Note: Can't use foreach here because it isn't implemented for UnsafePtrList, despite the IDE suggesting otherwise.
            for (int i = 0; i < chunkPtrs.Length; i++)
                AllocatorManager.Free(allocator, chunkPtrs[i], elementSize, elementAlignment, elementsPerChunk);
            for (int i = 0; i < chunkVersionPtrs.Length; i++)
                AllocatorManager.Free(allocator, chunkVersionPtrs[i], UnsafeUtility.SizeOf<int>(), UnsafeUtility.AlignOf<int>(), elementsPerChunk);
            chunkPtrs.Dispose();
            chunkVersionPtrs.Dispose();
            freelist.Dispose();
        }

        public int instanceCount => elementCount;
        public int maxIndex => elementsPerChunk * chunkPtrs.Length;

        public int Add()
        {
            if (freelist.IsEmpty)
            {
                var nextFreeIndexInChunk = elementCount % elementsPerChunk;
                if (nextFreeIndexInChunk == 0)
                {
                    // Allocate new chunk
                    var allocator = chunkPtrs.Allocator;
                    chunkPtrs.Add(AllocatorManager.Allocate(allocator, elementSize, elementAlignment, elementsPerChunk));
                    var versionPtr = AllocatorManager.Allocate(allocator, UnsafeUtility.SizeOf<int>(), UnsafeUtility.AlignOf<int>(), elementsPerChunk);
                    UnsafeUtility.MemClear(versionPtr, UnsafeUtility.SizeOf<int>() * elementsPerChunk);
                    *(int*)versionPtr = 1;
                    chunkVersionPtrs.Add(versionPtr);
                    var result = elementCount;
                    elementCount++;
                    return result;
                }
                else
                {
                    var versionPtr = chunkVersionPtrs[chunkVersionPtrs.Length - 1];
                    versionPtr[nextFreeIndexInChunk]++;
                    var result = elementCount;
                    elementCount++;
                    return result;
                }
            }
            else
            {
                int result = freelist[freelist.Length - 1];
                freelist.Length--;
                int chunkIndex   = result / elementsPerChunk;
                int indexInChunk = result % elementsPerChunk;
                var versionPtr   = chunkVersionPtrs[chunkIndex];
                versionPtr[indexInChunk]++;
                elementCount++;
                return result;
            }
        }

        public void Remove(int index)
        {
            int chunkIndex   = index / elementsPerChunk;
            int indexInChunk = index % elementsPerChunk;
            if (!disposePtr.Equals(default))
            {
                var componentPtr = chunkPtrs[chunkIndex] + indexInChunk * elementSize;
                new IAuxDisposable.VPtr(componentPtr, disposePtr).Dispose();
            }
            var versionPtr = chunkVersionPtrs[chunkIndex];
            versionPtr[indexInChunk]++;
            elementCount--;
            freelist.Add(index);
        }

        public void Replace(int index)
        {
            if (!disposePtr.Equals(default))
            {
                int chunkIndex   = index / elementsPerChunk;
                int indexInChunk = index % elementsPerChunk;
                var componentPtr = chunkPtrs[chunkIndex] + indexInChunk * elementSize;
                new IAuxDisposable.VPtr(componentPtr, disposePtr).Dispose();
            }
        }

        public AuxRef<T> GetRef<T>(int index) where T : unmanaged
        {
            int chunkIndex   = index / elementsPerChunk;
            int indexInChunk = index % elementsPerChunk;
            var versionPtr   = chunkVersionPtrs[chunkIndex] + indexInChunk;
            var componentPtr = (T*)chunkPtrs[chunkIndex] + indexInChunk;
            return new AuxRef<T>
            {
                componentPtr = componentPtr,
                versionPtr   = versionPtr,
                version      = *versionPtr
            };
        }
    }
}

