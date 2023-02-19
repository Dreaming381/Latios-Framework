using System;
using System.Diagnostics;
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

        #region Colliders
        public static Aabb AabbFrom(in SphereCollider sphere, in RigidTransform transform)
        {
            float3 wc   = math.transform(transform, sphere.center);
            Aabb   aabb = new Aabb(wc - sphere.radius, wc + sphere.radius);
            return aabb;
        }

        public static Aabb AabbFrom(in CapsuleCollider capsule, in RigidTransform transform)
        {
            float3 a = math.transform(transform, capsule.pointA);
            float3 b = math.transform(transform, capsule.pointB);
            return new Aabb(math.min(a, b) - capsule.radius, math.max(a, b) + capsule.radius);
        }

        public static Aabb AabbFrom(in BoxCollider box, in RigidTransform transform)
        {
            return TransformAabb(new float4x4(transform), box.center, box.halfSize);
        }

        public static Aabb AabbFrom(in TriangleCollider triangle, in RigidTransform transform)
        {
            var transformedTriangle = simd.transform(transform, new simdFloat3(triangle.pointA, triangle.pointB, triangle.pointC, triangle.pointA));
            var aabb                = new Aabb(math.min(transformedTriangle.a, transformedTriangle.b), math.max(transformedTriangle.a, transformedTriangle.b));
            return CombineAabb(transformedTriangle.c, aabb);
        }

        public static Aabb AabbFrom(in ConvexCollider convex, in RigidTransform transform)
        {
            var         local = convex.convexColliderBlob.Value.localAabb;
            float3      c     = (local.min + local.max) / 2f;
            BoxCollider box   = new BoxCollider(c, local.max - c);
            return AabbFrom(ScaleCollider(box, new PhysicsScale(convex.scale)), transform);
        }

        public static Aabb AabbFrom(in CompoundCollider compound, in RigidTransform transform)
        {
            var         local = compound.compoundColliderBlob.Value.localAabb;
            float3      c     = (local.min + local.max) / 2f;
            BoxCollider box   = new BoxCollider(c, local.max - c);
            return AabbFrom(ScaleCollider(box, new PhysicsScale(compound.scale)), transform);
        }
        #endregion

        #region ColliderCasts
        public static Aabb AabbFrom(in SphereCollider sphereToCast, in RigidTransform castStart, float3 castEnd)
        {
            var aabbStart = AabbFrom(sphereToCast, castStart);
            var diff      = castEnd - castStart.pos;
            var aabbEnd   = new Aabb(aabbStart.min + diff, aabbStart.max + diff);
            return CombineAabb(aabbStart, aabbEnd);
        }

        public static Aabb AabbFrom(in CapsuleCollider capsuleToCast, in RigidTransform castStart, float3 castEnd)
        {
            var aabbStart = AabbFrom(capsuleToCast, castStart);
            var diff      = castEnd - castStart.pos;
            var aabbEnd   = new Aabb(aabbStart.min + diff, aabbStart.max + diff);
            return CombineAabb(aabbStart, aabbEnd);
        }

        public static Aabb AabbFrom(in BoxCollider boxToCast, in RigidTransform castStart, float3 castEnd)
        {
            var aabbStart = AabbFrom(boxToCast, castStart);
            var diff      = castEnd - castStart.pos;
            var aabbEnd   = new Aabb(aabbStart.min + diff, aabbStart.max + diff);
            return CombineAabb(aabbStart, aabbEnd);
        }

        public static Aabb AabbFrom(in TriangleCollider triangleToCast, in RigidTransform castStart, float3 castEnd)
        {
            var aabbStart = AabbFrom(triangleToCast, castStart);
            var diff      = castEnd - castStart.pos;
            var aabbEnd   = new Aabb(aabbStart.min + diff, aabbStart.max + diff);
            return CombineAabb(aabbStart, aabbEnd);
        }

        public static Aabb AabbFrom(in ConvexCollider convexToCast, in RigidTransform castStart, float3 castEnd)
        {
            var aabbStart = AabbFrom(convexToCast, castStart);
            var diff      = castEnd - castStart.pos;
            var aabbEnd   = new Aabb(aabbStart.min + diff, aabbStart.max + diff);
            return CombineAabb(aabbStart, aabbEnd);
        }

        public static Aabb AabbFrom(in CompoundCollider compoundToCast, in RigidTransform castStart, float3 castEnd)
        {
            var aabbStart = AabbFrom(compoundToCast, castStart);
            var diff      = castEnd - castStart.pos;
            var aabbEnd   = new Aabb(aabbStart.min + diff, aabbStart.max + diff);
            return CombineAabb(aabbStart, aabbEnd);
        }
        #endregion

        #region Dispatch
        public static Aabb AabbFrom(in Collider collider, in RigidTransform transform)
        {
            switch (collider.type)
            {
                case ColliderType.Sphere:
                    return AabbFrom(in collider.m_sphere, in transform);
                case ColliderType.Capsule:
                    return AabbFrom(in collider.m_capsule, in transform);
                case ColliderType.Box:
                    return AabbFrom(in collider.m_box, in transform);
                case ColliderType.Triangle:
                    return AabbFrom(in collider.m_triangle, in transform);
                case ColliderType.Convex:
                    return AabbFrom(in collider.m_convex, in transform);
                case ColliderType.Compound:
                    return AabbFrom(in collider.m_compound, in transform);
                default:
                    ThrowUnsupportedType();
                    return new Aabb();
            }
        }

        public static Aabb AabbFrom(in Collider colliderToCast, in RigidTransform castStart, float3 castEnd)
        {
            switch (colliderToCast.type)
            {
                case ColliderType.Sphere:
                    return AabbFrom(in colliderToCast.m_sphere, in castStart, castEnd);
                case ColliderType.Capsule:
                    return AabbFrom(in colliderToCast.m_capsule, in castStart, castEnd);
                case ColliderType.Box:
                    return AabbFrom(in colliderToCast.m_box, in castStart, castEnd);
                case ColliderType.Triangle:
                    return AabbFrom(in colliderToCast.m_triangle, in castStart, castEnd);
                case ColliderType.Convex:
                    return AabbFrom(in colliderToCast.m_convex, in castStart, castEnd);
                case ColliderType.Compound:
                    return AabbFrom(in colliderToCast.m_compound, in castStart, castEnd);
                default:
                    ThrowUnsupportedType();
                    return new Aabb();
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void ThrowUnsupportedType()
        {
            throw new InvalidOperationException("Collider type not supported yet");
        }
        #endregion
    }
}

