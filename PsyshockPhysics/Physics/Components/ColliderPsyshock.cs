using System;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    // The concrete type of a collider
    public enum ColliderType : byte
    {
        //Convex Primitive types
        Sphere = 0,
        Capsule = 1,
        Box = 2,
        //Triangle = 3,
        //Quad = 4,
        //Cylinder = 5
        //Cone = 6

        //Beveled Convex Primitive Types
        //BeveledBox = 32

        //Concave Primitive Types
        //Torus = 64

        //Complex Convex Types
        //ConvexMesh = 128

        //Complex Concave types
        //Mesh = 160,
        Compound = 161,
        //Terrain = 162,

        //192+ ?
    }

    [Serializable]
    [StructLayout(LayoutKind.Explicit)]
    public unsafe partial struct Collider : IComponentData
    {
        [FieldOffset(0)]
        Storage m_storage;

        [FieldOffset(0)]
        SphereCollider m_sphere;

        [FieldOffset(0)]
        CapsuleCollider m_capsule;

        [FieldOffset(0)]
        BoxCollider m_box;

        //Todo: Make this a BlobAssetReference for all generic collider blobs.
        [FieldOffset(48)]
        UnsafeUntypedBlobAssetReference m_blobRef;
        //BlobAssetReference<CompoundColliderBlob> m_blobRef;

        [FieldOffset(56)]
        float m_reservedFloat;

        [FieldOffset(60)]
        byte m_reservedByte1;

        [FieldOffset(61)]
        byte m_reservedByte2;

        [FieldOffset(62)]
        byte m_reservedByte3;

        [FieldOffset(63)]
        ColliderType m_type;

        public ColliderType type => m_type;

        private struct Storage
        {
#pragma warning disable CS0649  //variable never assigned
            public float4 a;
            public float4 b;
            public float4 c;
#pragma warning restore CS0649
        }
    }
}

