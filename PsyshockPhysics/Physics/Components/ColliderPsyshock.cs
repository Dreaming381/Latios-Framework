using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    /// <summary>
    /// An enum representing the specific type of collider stored in the Collider struct
    /// </summary>
    public enum ColliderType : byte
    {
        // Convex Primitive types
        Sphere = 0,
        Capsule = 1,
        Box = 2,
        Triangle = 3,
        //Quad = 4,
        //Cylinder = 5
        //Cone = 6

        // Beveled Convex Primitive Types
        //BeveledBox = 32

        // Concave Primitive Types
        //Torus = 64

        // Complex Convex Types
        Convex = 128,
        // Complex Concave types
        TriMesh = 160,
        Compound = 161,
        //Terrain = 162,
        // Layer embeds
        //LayerCompound = 192;
    }

    /// <summary>
    /// A struct which contains one of any of the types of collider shapes supported by Psyshock.
    /// For those familiar with C or C++, this struct effectively acts like a union of all those types.
    /// This type can be implicitly casted to any of those types and those types can also be implicitly
    /// casted to this type. The specific shape type stored in this struct can be obtained via the `type` property.
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct Collider : IComponentData
    {
        #region Definition
        /// <summary>
        /// The specific shape type stored in this instance.
        /// </summary>
        public ColliderType type => m_type;

        [FieldOffset(0)]
        Storage m_storage;

        [FieldOffset(0)]
        ColliderType m_type;

        [FieldOffset(4)]
        internal SphereCollider m_sphere;

        [FieldOffset(4)]
        internal CapsuleCollider m_capsule;

        [FieldOffset(4)]
        internal BoxCollider m_box;

        [FieldOffset(4)]
        internal TriangleCollider m_triangle;

        [FieldOffset(16)]
        internal ConvexCollider m_convex;

        // Unity crashes when there are aliased BlobAssetReferences.
        // So we have to use ColliderBlobHelpers instead.
        //internal ref TriMeshCollider m_triMesh => ref ColliderBlobHelpers.AsTriMesh(ref m_convex);
        //{
        //    get
        //    {
        //        UnsafeUtility.As<TriMeshCollider>()
        //
        //        TriMeshCollider* ret;
        //        fixed (void*     ptr = &m_convex)
        //        ret                  = (TriMeshCollider*)ptr;
        //        return ref *ret;
        //    }
        //}

        //internal ref CompoundCollider m_compound
        //{
        //    get
        //    {
        //        CompoundCollider* ret;
        //        fixed (void*      ptr = &m_convex)
        //        ret                   = (CompoundCollider*)ptr;
        //        return ref *ret;
        //    }
        //}

        private struct Storage
        {
#pragma warning disable CS0649  //variable never assigned
            public float4 a;
            public float4 b;
            public float4 c;
#pragma warning restore CS0649
        }
        #endregion

        #region Casts
        public static implicit operator Collider(SphereCollider sphereCollider)
        {
            Collider collider = default;
            collider.m_type   = ColliderType.Sphere;
            collider.m_sphere = sphereCollider;
            return collider;
        }

        public static implicit operator SphereCollider(Collider collider)
        {
            CheckColliderIsCastTargetType(in collider, ColliderType.Sphere);
            return collider.m_sphere;
        }

        public static implicit operator Collider(CapsuleCollider capsuleCollider)
        {
            Collider collider  = default;
            collider.m_type    = ColliderType.Capsule;
            collider.m_capsule = capsuleCollider;
            return collider;
        }

        public static implicit operator CapsuleCollider(Collider collider)
        {
            CheckColliderIsCastTargetType(in collider, ColliderType.Capsule);
            return collider.m_capsule;
        }

        public static implicit operator Collider(BoxCollider boxCollider)
        {
            Collider collider = default;
            collider.m_type   = ColliderType.Box;
            collider.m_box    = boxCollider;
            return collider;
        }

        public static implicit operator BoxCollider(Collider collider)
        {
            CheckColliderIsCastTargetType(in collider, ColliderType.Box);
            return collider.m_box;
        }

        public static implicit operator Collider(TriangleCollider triangleCollider)
        {
            Collider collider   = default;
            collider.m_type     = ColliderType.Triangle;
            collider.m_triangle = triangleCollider;
            return collider;
        }

        public static implicit operator TriangleCollider(Collider collider)
        {
            CheckColliderIsCastTargetType(in collider, ColliderType.Triangle);
            return collider.m_triangle;
        }

        public static implicit operator Collider(ConvexCollider convexCollider)
        {
            Collider collider = default;
            collider.m_type   = ColliderType.Convex;
            collider.m_convex = convexCollider;
            return collider;
        }

        public static implicit operator ConvexCollider(Collider collider)
        {
            CheckColliderIsCastTargetType(in collider, ColliderType.Convex);
            return collider.m_convex;
        }

        public static implicit operator Collider(TriMeshCollider triMeshCollider)
        {
            Collider collider      = default;
            collider.m_type        = ColliderType.TriMesh;
            collider.m_triMeshRW() = triMeshCollider;
            return collider;
        }

        public static implicit operator TriMeshCollider(Collider collider)
        {
            CheckColliderIsCastTargetType(in collider, ColliderType.TriMesh);
            return collider.m_triMesh();
        }

        public static implicit operator Collider(CompoundCollider compoundCollider)
        {
            Collider collider       = default;
            collider.m_type         = ColliderType.Compound;
            collider.m_compoundRW() = compoundCollider;
            return collider;
        }

        public static implicit operator CompoundCollider(Collider collider)
        {
            CheckColliderIsCastTargetType(in collider, ColliderType.Compound);
            return collider.m_compound();
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        internal static void CheckColliderIsCastTargetType(in Collider c, ColliderType targetType)
        {
            if (c.m_type != targetType)
            {
                switch (targetType)
                {
                    case ColliderType.Sphere: throw new InvalidOperationException("Collider is not a SphereCollider but is being casted to one.");
                    case ColliderType.Capsule: throw new InvalidOperationException("Collider is not a CapsuleCollider but is being casted to one.");
                    case ColliderType.Box: throw new InvalidOperationException("Collider is not a BoxCollider but is being casted to one.");
                    case ColliderType.Triangle: throw new InvalidOperationException("Collider is not a TriangleCollider but is being casted to one.");
                    case ColliderType.Convex:
                        throw new InvalidOperationException(
                            "Collider is not a ConvexCollider but is being casted to one. Unlike Unity.Physics, ConvexColliders do not aggregate Spheres, Capsules, Boxes, or Triangles.");
                    case ColliderType.TriMesh: throw new InvalidCastException("Collider is not a TriMeshCollider but is being casted to one.");
                    case ColliderType.Compound: throw new InvalidOperationException("Collider is not a CompoundCollider but is being casted to one.");
                }
            }
        }
        #endregion
    }

    internal static class ColliderBlobHelpers
    {
        public static ref TriMeshCollider m_triMeshRW(ref this Collider collider) => ref UnsafeUtility.As<ConvexCollider, TriMeshCollider>(ref collider.m_convex);
        public static ref CompoundCollider m_compoundRW(ref this Collider collider) => ref UnsafeUtility.As<ConvexCollider, CompoundCollider>(ref collider.m_convex);
        public static ref TriMeshCollider m_triMesh(in this Collider collider) => ref UnsafeUtility.As<ConvexCollider,
                                                                                                       TriMeshCollider>(ref UnsafeUtilityExtensions.AsRef(in collider.m_convex));
        public static ref CompoundCollider m_compound(in this Collider collider) => ref UnsafeUtility.As<ConvexCollider,
                                                                                                         CompoundCollider>(ref UnsafeUtilityExtensions.AsRef(in collider.m_convex));
    }
}

