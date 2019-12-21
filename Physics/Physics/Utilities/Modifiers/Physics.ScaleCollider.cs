using System;
using Unity.Mathematics;

namespace Latios.PhysicsEngine
{
    public static partial class Physics
    {
        public static Collider ScaleCollider(Collider collider, PhysicsScale scale)
        {
            switch (collider.type)
            {
                case ColliderType.Sphere: return ScaleCollider((SphereCollider)collider, scale);
                case ColliderType.Capsule: return ScaleCollider((CapsuleCollider)collider, scale);
                default: throw new InvalidOperationException("Collider type not supported yet.");
            }
        }

        public static SphereCollider ScaleCollider(SphereCollider sphere, PhysicsScale scale)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (scale.state == PhysicsScale.State.NonComputable | scale.state == PhysicsScale.State.NonUniform)
                throw new InvalidOperationException("Error: Sphere Collider must be scaled with no scale or uniform scale.");
#endif
            sphere.center *= scale.scale.x;
            sphere.radius *= scale.scale.x;
            return sphere;
        }

        public static CapsuleCollider ScaleCollider(CapsuleCollider capsule, PhysicsScale scale)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (scale.state == PhysicsScale.State.NonComputable | scale.state == PhysicsScale.State.NonUniform)
                throw new InvalidOperationException("Error: Capsule Collider must be scaled with no scale or uniform scale.");
#endif
            capsule.pointA *= scale.scale.x;
            capsule.pointB *= scale.scale.x;
            capsule.radius *= scale.scale.x;
            return capsule;
        }
    }
}

