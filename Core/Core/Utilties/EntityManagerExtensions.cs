using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.LowLevel.Unsafe;

namespace Latios
{
    public static class EntityManagerExtensions
    {
        [BurstCompatible]
        public static unsafe void CopyComponentData(this EntityManager entityManager, Entity src, Entity dst, ComponentType componentType)
        {
            CheckComponentTypeIsUnmanagedComponentData(componentType);

            // We do this to prevent MemCpy from being confused (might not actually be an issue)
            if (src == dst)
                return;

            entityManager.AddComponent(dst, componentType);

            if (componentType.IsZeroSized)
                return;

            var typeInfo           = TypeManager.GetTypeInfo(componentType.TypeIndex);
            var size               = typeInfo.SizeInChunk;
            var typeRO             = componentType;
            typeRO.AccessModeType  = ComponentType.AccessMode.ReadOnly;
            var typeRW             = componentType;
            typeRW.AccessModeType  = ComponentType.AccessMode.ReadWrite;
            var handleRW           = entityManager.GetDynamicComponentTypeHandle(typeRW);
            var handleRO           = entityManager.GetDynamicComponentTypeHandle(typeRO);
            var srcChunk           = entityManager.GetStorageInfo(src);
            var dstChunk           = entityManager.GetStorageInfo(dst);
            var dstPtr             = (byte*)dstChunk.Chunk.GetDynamicComponentDataArrayReinterpret<byte>(handleRW, size).GetUnsafePtr();
            dstPtr                += dstChunk.IndexInChunk * size;
            var srcPtr             = (byte*)srcChunk.Chunk.GetDynamicComponentDataArrayReinterpret<byte>(handleRO, size).GetUnsafeReadOnlyPtr();
            srcPtr                += srcChunk.IndexInChunk * size;
            UnsafeUtility.MemCpy(dstPtr, srcPtr, size);
        }

        [BurstCompatible]
        public static unsafe void CopyDynamicBuffer(this EntityManager entityManager, Entity src, Entity dst, ComponentType componentType)
        {
            CheckComponentTypeIsBuffer(componentType);

            entityManager.AddComponent(dst, componentType);

            // We do this to prevent MemCpy from being confused (might not actually be an issue)
            if (src == dst)
                return;

            var typeRO            = componentType;
            typeRO.AccessModeType = ComponentType.AccessMode.ReadOnly;
            var typeRW            = componentType;
            typeRW.AccessModeType = ComponentType.AccessMode.ReadWrite;
            var handleRW          = entityManager.GetDynamicComponentTypeHandle(typeRW);
            var handleRO          = entityManager.GetDynamicComponentTypeHandle(typeRO);
            var srcChunk          = entityManager.GetStorageInfo(src);
            var dstChunk          = entityManager.GetStorageInfo(dst);
            var dstBufferAccess   = dstChunk.Chunk.GetUntypedBufferAccessor(ref handleRW);
            var srcBufferAccess   = srcChunk.Chunk.GetUntypedBufferAccessor(ref handleRO);
            var srcPtr            = srcBufferAccess.GetUnsafeReadOnlyPtrAndLength(srcChunk.IndexInChunk, out int length);
            dstBufferAccess.ResizeUninitialized(dstChunk.IndexInChunk, length);
            UnsafeUtility.MemCpy(dstBufferAccess.GetUnsafePtr(dstChunk.IndexInChunk), srcPtr, dstBufferAccess.ElementSize * (long)length);
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        private static void CheckComponentTypeIsUnmanagedComponentData(ComponentType type)
        {
            if (type.IsBuffer || type.IsChunkComponent || type.IsManagedComponent || type.IsSharedComponent)
                throw new ArgumentException($"Attempted to call EntityManager.CopyComponentData on {type} which is not an unmanaged IComponentData");
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS"), Conditional("UNITY_DOTS_DEBUG")]
        private static void CheckComponentTypeIsBuffer(ComponentType type)
        {
            if (!type.IsBuffer)
                throw new ArgumentException($"Attempted to call EntityManager.CopyDynamicBuffer on {type} which is not an IBufferElementData");
        }
    }
}

