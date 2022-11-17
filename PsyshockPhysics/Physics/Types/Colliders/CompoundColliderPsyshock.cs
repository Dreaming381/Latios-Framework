using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    /// <summary>
    /// A uniformly scaled collection of colliders and their relative transforms.
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Explicit)]
    public struct CompoundCollider
    {
        [FieldOffset(0)] public BlobAssetReference<CompoundColliderBlob> compoundColliderBlob;
        [FieldOffset(8)] public float                                    scale;

        public CompoundCollider(BlobAssetReference<CompoundColliderBlob> compoundColliderBlob, float scale = 1f)
        {
            this.compoundColliderBlob = compoundColliderBlob;
            this.scale                = scale;
        }
    }

    internal struct BlobCollider
    {
#pragma warning disable CS0649
        internal float4x4 storage;
#pragma warning restore CS0649
    }

    //Todo: Use some acceleration structure in a future version
    /// <summary>
    /// A blob asset composed of a collection of colliders and their transforms in a unified coordinate space
    /// </summary>
    public struct CompoundColliderBlob
    {
        internal BlobArray<BlobCollider> blobColliders;
        public BlobArray<RigidTransform> transforms;
        public Aabb                      localAabb;

        public unsafe ref BlobArray<Collider> colliders => ref UnsafeUtility.AsRef<BlobArray<Collider> >(UnsafeUtility.AddressOf(ref blobColliders));
        //ref UnsafeUtility.As<BlobArray<BlobCollider>, BlobArray<Collider>>(ref blobColliders);
    }
}

