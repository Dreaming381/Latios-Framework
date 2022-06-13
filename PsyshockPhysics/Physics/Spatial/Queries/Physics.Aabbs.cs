using System;
using System.Diagnostics;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    public static partial class Physics
    {
        public static Aabb AabbFrom(SphereCollider sphere, RigidTransform transform)
        {
            float3 wc   = math.transform(transform, sphere.center);
            Aabb   aabb = new Aabb(wc - sphere.radius, wc + sphere.radius);
            return aabb;
        }

        public static Aabb AabbFrom(CapsuleCollider capsule, RigidTransform transform)
        {
            float3 a = math.transform(transform, capsule.pointA);
            float3 b = math.transform(transform, capsule.pointB);
            return new Aabb(math.min(a, b) - capsule.radius, math.max(a, b) + capsule.radius);
        }

        public static Aabb AabbFrom(BoxCollider box, RigidTransform transform)
        {
            return TransformAabb(new float4x4(transform), box.center, box.halfSize);
        }

        public static Aabb AabbFrom(TriangleCollider triangle, RigidTransform transform)
        {
            var transformedTriangle = simd.transform(transform, new simdFloat3(triangle.pointA, triangle.pointB, triangle.pointC, triangle.pointA));
            var aabb                = new Aabb(math.min(transformedTriangle.a, transformedTriangle.b), math.max(transformedTriangle.a, transformedTriangle.b));
            return CombineAabb(transformedTriangle.c, aabb);
        }

        public static Aabb AabbFrom(ConvexCollider convex, RigidTransform transform)
        {
            var         local = convex.convexColliderBlob.Value.localAabb;
            float3      c     = (local.min + local.max) / 2f;
            BoxCollider box   = new BoxCollider(c, local.max - c);
            return AabbFrom(ScaleCollider(box, new PhysicsScale(convex.scale)), transform);
        }

        public static Aabb AabbFrom(CompoundCollider compound, RigidTransform transform)
        {
            var         local = compound.compoundColliderBlob.Value.localAabb;
            float3      c     = (local.min + local.max) / 2f;
            BoxCollider box   = new BoxCollider(c, local.max - c);
            return AabbFrom(ScaleCollider(box, new PhysicsScale(compound.scale)), transform);
        }

        #region Dispatch
        public static Aabb AabbFrom(Collider collider, RigidTransform transform)
        {
            switch (collider.type)
            {
                case ColliderType.Sphere:
                    SphereCollider sphere = collider;
                    return AabbFrom(sphere, transform);
                case ColliderType.Capsule:
                    CapsuleCollider capsule = collider;
                    return AabbFrom(capsule, transform);
                case ColliderType.Box:
                    BoxCollider box = collider;
                    return AabbFrom(box, transform);
                case ColliderType.Triangle:
                    TriangleCollider triangle = collider;
                    return AabbFrom(triangle, transform);
                case ColliderType.Convex:
                    ConvexCollider convex = collider;
                    return AabbFrom(convex, transform);
                case ColliderType.Compound:
                    CompoundCollider compound = collider;
                    return AabbFrom(compound, transform);
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

