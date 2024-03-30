using System;
using System.Diagnostics;
using Latios.Transforms;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    public static partial class Physics
    {
        #region Rays
        /// <summary>
        /// Returns an Aabb that encompasses the ray
        /// </summary>
        public static Aabb AabbFrom(in Ray ray)
        {
            return new Aabb(math.min(ray.start, ray.end), math.max(ray.start, ray.end));
        }

        /// <summary>
        /// Returns an Aabb that encompasses a ray with the provided endpoints
        /// </summary>
        public static Aabb AabbFrom(float3 rayStart, float3 rayEnd)
        {
            return new Aabb(math.min(rayStart, rayEnd), math.max(rayStart, rayEnd));
        }
        #endregion

        #region Dispatch
        /// <summary>
        /// Returns an Aabb that encompasses the collider after being transformed by transform
        /// </summary>
        /// <param name="collider">The collider around which an Aabb should be created</param>
        /// <param name="transform">A transform which specifies how the collider is oriented within
        /// the coordinate space that the Aabb should be created in</param>
        /// <returns>An Aabb that encompasses the collider with the specified transform</returns>
        public static Aabb AabbFrom(in Collider collider, in TransformQvvs transform)
        {
            if (math.all(new float4(transform.stretch, transform.scale) == 1f))
            {
                var rigidTransform = new RigidTransform(transform.rotation, transform.position);
                return AabbFrom(in collider, in rigidTransform);
            }

            switch (collider.type)
            {
                case ColliderType.Sphere:
                    return AabbFrom(collider.m_sphere, in transform);
                case ColliderType.Capsule:
                    return AabbFrom(collider.m_capsule, in transform);
                case ColliderType.Box:
                    return AabbFrom(collider.m_box, in transform);
                case ColliderType.Triangle:
                    return AabbFrom(collider.m_triangle, in transform);
                case ColliderType.Convex:
                    return AabbFrom(collider.m_convex, in transform);
                case ColliderType.TriMesh:
                    return AabbFrom(collider.m_triMesh(), in transform);
                case ColliderType.Compound:
                    return AabbFrom(collider.m_compound(), in transform);
                default:
                    ThrowUnsupportedType(collider.type);
                    return new Aabb();
            }
        }

        /// <summary>
        /// Returns an Aabb that encompasses a ColliderCast operation, that is, it encompasses the full range of space
        /// caused by sweeping a collider from a start to an end point. It is assumed rotation, scale, and stretch are
        /// constant throughout the cast.
        /// </summary>
        /// <param name="colliderToCast">The collider shape that is being casted</param>
        /// <param name="castStart">The transform of the collider at the start of the cast</param>
        /// <param name="castEnd">The position of the collider at the end of the cast</param>
        /// <returns>An encompassing Aabb for a ColliderCast operation</returns>
        public static Aabb AabbFrom(in Collider colliderToCast, in TransformQvvs castStart, float3 castEnd)
        {
            if (math.all(new float4(castStart.stretch, castStart.scale) == 1f))
            {
                var rigidTransform = new RigidTransform(castStart.rotation, castStart.position);
                return AabbFrom(in colliderToCast, in rigidTransform, castEnd);
            }

            switch (colliderToCast.type)
            {
                case ColliderType.Sphere:
                    return AabbFrom(colliderToCast.m_sphere, in castStart, castEnd);
                case ColliderType.Capsule:
                    return AabbFrom(colliderToCast.m_capsule, in castStart, castEnd);
                case ColliderType.Box:
                    return AabbFrom(colliderToCast.m_box, in castStart, castEnd);
                case ColliderType.Triangle:
                    return AabbFrom(colliderToCast.m_triangle, in castStart, castEnd);
                case ColliderType.Convex:
                    return AabbFrom(colliderToCast.m_convex, in castStart, castEnd);
                case ColliderType.TriMesh:
                    return AabbFrom(colliderToCast.m_triMesh(), in castStart, castEnd);
                case ColliderType.Compound:
                    return AabbFrom(colliderToCast.m_compound(), in castStart, castEnd);
                default:
                    ThrowUnsupportedType(colliderToCast.type);
                    return new Aabb();
            }
        }

        internal static Aabb AabbFrom(in Collider collider, in RigidTransform transform)
        {
            switch (collider.type)
            {
                case ColliderType.Sphere:
                    return AabbFrom(collider.m_sphere, in transform);
                case ColliderType.Capsule:
                    return AabbFrom(collider.m_capsule, in transform);
                case ColliderType.Box:
                    return AabbFrom(collider.m_box, in transform);
                case ColliderType.Triangle:
                    return AabbFrom(collider.m_triangle, in transform);
                case ColliderType.Convex:
                    return AabbFrom(collider.m_convex, in transform);
                case ColliderType.TriMesh:
                    return AabbFrom(collider.m_triMesh(), in transform);
                case ColliderType.Compound:
                    return AabbFrom(collider.m_compound(), in transform);
                default:
                    ThrowUnsupportedType(collider.type);
                    return new Aabb();
            }
        }

        internal static Aabb AabbFrom(in Collider colliderToCast, in RigidTransform castStart, float3 castEnd)
        {
            switch (colliderToCast.type)
            {
                case ColliderType.Sphere:
                    return AabbFrom(colliderToCast.m_sphere, in castStart, castEnd);
                case ColliderType.Capsule:
                    return AabbFrom(colliderToCast.m_capsule, in castStart, castEnd);
                case ColliderType.Box:
                    return AabbFrom(colliderToCast.m_box, in castStart, castEnd);
                case ColliderType.Triangle:
                    return AabbFrom(colliderToCast.m_triangle, in castStart, castEnd);
                case ColliderType.Convex:
                    return AabbFrom(colliderToCast.m_convex, in castStart, castEnd);
                case ColliderType.Compound:
                    return AabbFrom(colliderToCast.m_compound(), in castStart, castEnd);
                default:
                    ThrowUnsupportedType(colliderToCast.type);
                    return new Aabb();
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void ThrowUnsupportedType(ColliderType type)
        {
            throw new InvalidOperationException($"Collider type not supported yet. Type code is {(int)type}");
        }
        #endregion

        #region Colliders
        private static Aabb AabbFrom(in SphereCollider sphere, in RigidTransform transform)
        {
            float3 wc   = math.transform(transform, sphere.center);
            Aabb   aabb = new Aabb(wc - sphere.radius, wc + sphere.radius);
            return aabb;
        }

        private static Aabb AabbFrom(SphereCollider sphere, in TransformQvvs transform)
        {
            ScaleStretchCollider(ref sphere, transform.scale, transform.stretch);
            return AabbFrom(in sphere, new RigidTransform(transform.rotation, transform.position));
        }

        private static Aabb AabbFrom(in CapsuleCollider capsule, in RigidTransform transform)
        {
            float3 a = math.transform(transform, capsule.pointA);
            float3 b = math.transform(transform, capsule.pointB);
            return new Aabb(math.min(a, b) - capsule.radius, math.max(a, b) + capsule.radius);
        }

        private static Aabb AabbFrom(CapsuleCollider capsule, in TransformQvvs transform)
        {
            ScaleStretchCollider(ref capsule, transform.scale, transform.stretch);
            return AabbFrom(in capsule, new RigidTransform(transform.rotation, transform.position));
        }

        private static Aabb AabbFrom(in BoxCollider box, in RigidTransform transform)
        {
            return TransformAabb(new float4x4(transform), box.center, box.halfSize);
        }

        private static Aabb AabbFrom(BoxCollider box, in TransformQvvs transform)
        {
            ScaleStretchCollider(ref box, transform.scale, transform.stretch);
            return AabbFrom(in box, new RigidTransform(transform.rotation, transform.position));
        }

        private static Aabb AabbFrom(in TriangleCollider triangle, in RigidTransform transform)
        {
            var transformedTriangle = simd.transform(transform, new simdFloat3(triangle.pointA, triangle.pointB, triangle.pointC, triangle.pointA));
            var aabb                = new Aabb(math.min(transformedTriangle.a, transformedTriangle.b), math.max(transformedTriangle.a, transformedTriangle.b));
            return CombineAabb(transformedTriangle.c, aabb);
        }

        private static Aabb AabbFrom(TriangleCollider triangle, in TransformQvvs transform)
        {
            ScaleStretchCollider(ref triangle, transform.scale, transform.stretch);
            return AabbFrom(in triangle, new RigidTransform(transform.rotation, transform.position));
        }

        private static Aabb AabbFrom(in ConvexCollider convex, in RigidTransform transform)
        {
            var         local = convex.convexColliderBlob.Value.localAabb;
            float3      c     = (local.min + local.max) / 2f;
            BoxCollider box   = new BoxCollider(c, local.max - c);
            ScaleStretchCollider(ref box, 1f, convex.scale);
            return AabbFrom(in box, transform);
        }

        private static Aabb AabbFrom(ConvexCollider convex, in TransformQvvs transform)
        {
            ScaleStretchCollider(ref convex, transform.scale, transform.stretch);
            return AabbFrom(in convex, new RigidTransform(transform.rotation, transform.position));
        }

        private static Aabb AabbFrom(in TriMeshCollider triMesh, in RigidTransform transform)
        {
            var         local = triMesh.triMeshColliderBlob.Value.localAabb;
            float3      c     = (local.min + local.max) / 2f;
            BoxCollider box   = new BoxCollider(c, local.max - c);
            ScaleStretchCollider(ref box, 1f, triMesh.scale);
            return AabbFrom(in box, transform);
        }

        private static Aabb AabbFrom(TriMeshCollider triMesh, in TransformQvvs transform)
        {
            ScaleStretchCollider(ref triMesh, transform.scale, transform.stretch);
            return AabbFrom(in triMesh, new RigidTransform(transform.rotation, transform.position));
        }

        private static Aabb AabbFrom(in CompoundCollider compound, in RigidTransform transform)
        {
            var         local = compound.compoundColliderBlob.Value.localAabb;
            float3      c     = (local.min + local.max) / 2f;
            BoxCollider box   = new BoxCollider(c, local.max - c);
            ScaleStretchCollider(ref box, compound.scale, math.max(1f, compound.stretch));  // Be conservative here to avoid enum parsing
            return AabbFrom(in box, transform);
        }

        private static Aabb AabbFrom(CompoundCollider compound, in TransformQvvs transform)
        {
            ScaleStretchCollider(ref compound, transform.scale, transform.stretch);
            return AabbFrom(in compound, new RigidTransform(transform.rotation, transform.position));
        }
        #endregion

        #region ColliderCasts
        private static Aabb AabbFrom(in SphereCollider sphereToCast, in RigidTransform castStart, float3 castEnd)
        {
            var aabbStart = AabbFrom(sphereToCast, castStart);
            var diff      = castEnd - castStart.pos;
            var aabbEnd   = new Aabb(aabbStart.min + diff, aabbStart.max + diff);
            return CombineAabb(aabbStart, aabbEnd);
        }

        private static Aabb AabbFrom(SphereCollider sphere, in TransformQvvs castStart, float3 castEnd)
        {
            ScaleStretchCollider(ref sphere, castStart.scale, castStart.stretch);
            return AabbFrom(in sphere, new RigidTransform(castStart.rotation, castStart.position), castEnd);
        }

        private static Aabb AabbFrom(in CapsuleCollider capsuleToCast, in RigidTransform castStart, float3 castEnd)
        {
            var aabbStart = AabbFrom(capsuleToCast, castStart);
            var diff      = castEnd - castStart.pos;
            var aabbEnd   = new Aabb(aabbStart.min + diff, aabbStart.max + diff);
            return CombineAabb(aabbStart, aabbEnd);
        }

        private static Aabb AabbFrom(CapsuleCollider capsule, in TransformQvvs castStart, float3 castEnd)
        {
            ScaleStretchCollider(ref capsule, castStart.scale, castStart.stretch);
            return AabbFrom(in capsule, new RigidTransform(castStart.rotation, castStart.position), castEnd);
        }

        private static Aabb AabbFrom(in BoxCollider boxToCast, in RigidTransform castStart, float3 castEnd)
        {
            var aabbStart = AabbFrom(boxToCast, castStart);
            var diff      = castEnd - castStart.pos;
            var aabbEnd   = new Aabb(aabbStart.min + diff, aabbStart.max + diff);
            return CombineAabb(aabbStart, aabbEnd);
        }

        private static Aabb AabbFrom(BoxCollider box, in TransformQvvs castStart, float3 castEnd)
        {
            ScaleStretchCollider(ref box, castStart.scale, castStart.stretch);
            return AabbFrom(in box, new RigidTransform(castStart.rotation, castStart.position), castEnd);
        }

        private static Aabb AabbFrom(in TriangleCollider triangleToCast, in RigidTransform castStart, float3 castEnd)
        {
            var aabbStart = AabbFrom(triangleToCast, castStart);
            var diff      = castEnd - castStart.pos;
            var aabbEnd   = new Aabb(aabbStart.min + diff, aabbStart.max + diff);
            return CombineAabb(aabbStart, aabbEnd);
        }

        private static Aabb AabbFrom(TriangleCollider triangle, in TransformQvvs castStart, float3 castEnd)
        {
            ScaleStretchCollider(ref triangle, castStart.scale, castStart.stretch);
            return AabbFrom(in triangle, new RigidTransform(castStart.rotation, castStart.position), castEnd);
        }

        private static Aabb AabbFrom(in ConvexCollider convexToCast, in RigidTransform castStart, float3 castEnd)
        {
            var aabbStart = AabbFrom(convexToCast, castStart);
            var diff      = castEnd - castStart.pos;
            var aabbEnd   = new Aabb(aabbStart.min + diff, aabbStart.max + diff);
            return CombineAabb(aabbStart, aabbEnd);
        }

        private static Aabb AabbFrom(ConvexCollider convex, in TransformQvvs castStart, float3 castEnd)
        {
            ScaleStretchCollider(ref convex, castStart.scale, castStart.stretch);
            return AabbFrom(in convex, new RigidTransform(castStart.rotation, castStart.position), castEnd);
        }

        private static Aabb AabbFrom(in TriMeshCollider triMeshToCast, in RigidTransform castStart, float3 castEnd)
        {
            var aabbStart = AabbFrom(triMeshToCast, castStart);
            var diff      = castEnd - castStart.pos;
            var aabbEnd   = new Aabb(aabbStart.min + diff, aabbStart.max + diff);
            return CombineAabb(aabbStart, aabbEnd);
        }

        private static Aabb AabbFrom(TriMeshCollider triMesh, in TransformQvvs castStart, float3 castEnd)
        {
            ScaleStretchCollider(ref triMesh, castStart.scale, castStart.stretch);
            return AabbFrom(in triMesh, new RigidTransform(castStart.rotation, castStart.position), castEnd);
        }

        private static Aabb AabbFrom(in CompoundCollider compoundToCast, in RigidTransform castStart, float3 castEnd)
        {
            var aabbStart = AabbFrom(compoundToCast, castStart);
            var diff      = castEnd - castStart.pos;
            var aabbEnd   = new Aabb(aabbStart.min + diff, aabbStart.max + diff);
            return CombineAabb(aabbStart, aabbEnd);
        }

        private static Aabb AabbFrom(CompoundCollider compound, in TransformQvvs castStart, float3 castEnd)
        {
            ScaleStretchCollider(ref compound, castStart.scale, castStart.stretch);
            return AabbFrom(in compound, new RigidTransform(castStart.rotation, castStart.position), castEnd);
        }
        #endregion
    }
}

