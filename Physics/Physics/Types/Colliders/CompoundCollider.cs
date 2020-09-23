using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.PhysicsEngine
{
    [Serializable]
    public struct CompoundCollider
    {
        public BlobAssetReference<CompoundColliderBlob> compoundColliderBlob;
        public float                                    scale;

        public CompoundCollider(BlobAssetReference<CompoundColliderBlob> compoundColliderBlob, float scale = 1f)
        {
            this.compoundColliderBlob = compoundColliderBlob;
            this.scale                = scale;
        }
    }

    //Todo: Use some acceleration structure in a future version
    public struct CompoundColliderBlob
    {
        public BlobArray<Collider>       colliders;
        public BlobArray<RigidTransform> transforms;
        public Aabb                      localAabb;
    }
}

