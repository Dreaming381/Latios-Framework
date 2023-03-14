using System;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    /// <summary>
    /// An enum representing the specific type of collider stored in the Collider struct
    /// </summary>
    public enum ColliderType : byte
    {
        //Convex Primitive types
        Sphere = 0,
        Capsule = 1,
        Box = 2,
        Triangle = 3,
        //Quad = 4,
        //Cylinder = 5
        //Cone = 6

        //Beveled Convex Primitive Types
        //BeveledBox = 32

        //Concave Primitive Types
        //Torus = 64

        //Complex Convex Types
        Convex = 128,
        //Complex Concave types
        //Mesh = 160,
        Compound = 161,
        //Terrain = 162,

        //192+ ?
    }

    /// <summary>
    /// A struct which contains one of any of the types of collider shapes supported by Psyshock.
    /// For those familiar with C or C++, this struct effectively acts like a union of all those types.
    /// This type can be implicitly casted to any of those types and those types can also be implicitly
    /// casted to this type. The specific shape type stored in this struct can be obtained via the `type` property.
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Explicit)]
    public unsafe partial struct Collider : IComponentData
    {
        /// <summary>
        /// The specific shape type stored in this instance.
        /// </summary>
        public ColliderType type => m_type;

        [FieldOffset(0)]
        Storage m_storage;

        [FieldOffset(0)]
        ColliderType m_type;

        [FieldOffset(16)]
        internal SphereCollider m_sphere;

        [FieldOffset(16)]
        internal CapsuleCollider m_capsule;

        [FieldOffset(16)]
        internal BoxCollider m_box;

        [FieldOffset(16)]
        internal TriangleCollider m_triangle;

        [FieldOffset(8)]
        internal ConvexCollider m_convex;

        //[FieldOffset(8)]
        internal ref CompoundCollider m_compound
        {
            get
            {
                CompoundCollider* ret;
                fixed (void*      ptr = &m_convex)
                ret                   = (CompoundCollider*)ptr;
                return ref *ret;
            }
        }

        //[FieldOffset(8)]
        //UnsafeUntypedBlobAssetReference m_blobRef;

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

