using System;
using System.Diagnostics;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    public static partial class Physics
    {
        public static Aabb CalculateAabb(SphereCollider sphere, RigidTransform transform)
        {
            float3 wc   = math.transform(transform, sphere.center);
            Aabb   aabb = new Aabb(wc - sphere.radius, wc + sphere.radius);
            return aabb;
        }

        public static Aabb CalculateAabb(CapsuleCollider capsule, RigidTransform transform)
        {
            float3 a = math.transform(transform, capsule.pointA);
            float3 b = math.transform(transform, capsule.pointB);
            return new Aabb(math.min(a, b) - capsule.radius, math.max(a, b) + capsule.radius);
        }

        public static Aabb CalculateAabb(BoxCollider box, RigidTransform transform)
        {
            return TransformAabb(new float4x4(transform), box.center, box.halfSize);
        }

        public static Aabb CalculateAabb(CompoundCollider compound, RigidTransform transform)
        {
            var         local = compound.compoundColliderBlob.Value.localAabb;
            float3      c     = (local.min + local.max) / 2f;
            BoxCollider box   = new BoxCollider(c, local.max - c);
            return CalculateAabb(Physics.ScaleCollider(box, new PhysicsScale(compound.scale)), transform);
        }

        #region Dispatch
        public static Aabb CalculateAabb(Collider collider, RigidTransform transform)
        {
            switch (collider.type)
            {
                case ColliderType.Sphere:
                    SphereCollider sphere = collider;
                    return CalculateAabb(sphere, transform);
                case ColliderType.Capsule:
                    CapsuleCollider capsule = collider;
                    return CalculateAabb(capsule, transform);
                case ColliderType.Box:
                    BoxCollider box = collider;
                    return CalculateAabb(box, transform);
                case ColliderType.Compound:
                    CompoundCollider compound = collider;
                    return CalculateAabb(compound, transform);
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

