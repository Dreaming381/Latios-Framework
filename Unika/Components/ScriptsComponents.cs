using Unity.Collections;
using Unity.Entities;
using Unity.Entities.LowLevel.Unsafe;
using Unity.Entities.Serialization;
using Unity.Mathematics;

namespace Latios.Unika
{
    public interface IUnikaInterface
    {
    }

    public interface IUnikaScript
    {
    }

    [InternalBufferCapacity(0)]
    public struct UnikaScripts : IBufferElementData
    {
        internal ScriptHeader header;
    }

    [InternalBufferCapacity(0)]
    public struct UnikaSerializedEntityReference : IBufferElementData
    {
        internal Entity entity;
        internal int    byteOffset;
    }

    [InternalBufferCapacity(0)]
    public struct UnikaSerializedBlobReference : IBufferElementData
    {
        internal UnsafeUntypedBlobAssetReference blob;
        internal int                             byteOffset;
    }

    [InternalBufferCapacity(0)]
    public struct UnikaSerializedAssetReference : IBufferElementData
    {
        internal UntypedWeakReferenceId asset;
        internal int                    byteOffset;
    }

    [InternalBufferCapacity(0)]
    public struct UnikaSerializedObjectReference : IBufferElementData
    {
        internal UnityObjectRef<UnityEngine.Object> obj;
        internal int                                byteOffset;
    }

    public struct UnikaSerializedTypeIds : IComponentData
    {
        internal BlobAssetReference<UnikaSerializedTypeIdsBlob> blob;
    }

    public struct UnikaSerializedTypeIdsBlob
    {
        public BlobArray<ulong> stableHashBySerializedTypeId;
    }
}

