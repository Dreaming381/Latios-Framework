using System;
using System.Diagnostics;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    public static partial class Physics
    {
        public static Collider ScaleCollider(Collider collider, PhysicsScale scale)
        {
            switch (collider.type)
            {
                case ColliderType.Sphere: return ScaleCollider((SphereCollider)collider, scale);
                case ColliderType.Capsule: return ScaleCollider((CapsuleCollider)collider, scale);
                case ColliderType.Box: return ScaleCollider((BoxCollider)collider, scale);
                case ColliderType.Compound: return ScaleCollider((CompoundCollider)collider, scale);
                default: ThrowUnsupportedType(); return new Collider();
            }
        }

        public static SphereCollider ScaleCollider(SphereCollider sphere, PhysicsScale scale)
        {
            CheckNoOrUniformScale(scale, ColliderType.Sphere);
            sphere.center *= scale.scale.x;
            sphere.radius *= scale.scale.x;
            return sphere;
        }

        public static CapsuleCollider ScaleCollider(CapsuleCollider capsule, PhysicsScale scale)
        {
            CheckNoOrUniformScale(scale, ColliderType.Capsule);
            capsule.pointA *= scale.scale.x;
            capsule.pointB *= scale.scale.x;
            capsule.radius *= scale.scale.x;
            return capsule;
        }

        public static BoxCollider ScaleCollider(BoxCollider box, PhysicsScale scale)
        {
            CheckNoOrValidScale(scale, ColliderType.Box);
            box.center   *= scale.scale;
            box.halfSize *= scale.scale;
            return box;
        }

        public static TriangleCollider ScaleCollider(TriangleCollider triangle, PhysicsScale scale)
        {
            CheckNoOrValidScale(scale, ColliderType.Triangle);
            triangle.pointA *= scale.scale;
            triangle.pointB *= scale.scale;
            triangle.pointC *= scale.scale;
            return triangle;
        }

        public static ConvexCollider ScaleCollider(ConvexCollider convex, PhysicsScale scale)
        {
            CheckNoOrValidScale(scale, ColliderType.Convex);
            convex.scale *= scale.scale;
            return convex;
        }

        public static CompoundCollider ScaleCollider(CompoundCollider compound, PhysicsScale scale)
        {
            CheckNoOrUniformScale(scale, ColliderType.Compound);
            compound.scale *= scale.scale.x;
            return compound;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void CheckNoOrUniformScale(PhysicsScale scale, ColliderType type)
        {
            if (scale.state == PhysicsScale.State.NonComputable | scale.state == PhysicsScale.State.NonUniform)
            {
                switch (type)
                {
                    case ColliderType.Sphere: throw new InvalidOperationException("Sphere Collider must be scaled with no scale or uniform scale.");
                    case ColliderType.Capsule: throw new InvalidOperationException("Capsule Collider must be scaled with no scale or uniform scale.");
                    case ColliderType.Box: throw new InvalidOperationException("Box Collider must be scaled with no scale or uniform scale.");
                    case ColliderType.Compound: throw new InvalidOperationException("Compound Collider must be scaled with no scale or uniform scale.");
                    default: throw new InvalidOperationException("Failed to scale unknown collider type.");
                }
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void CheckNoOrValidScale(PhysicsScale scale, ColliderType type)
        {
            if (scale.state == PhysicsScale.State.NonComputable)
            {
                throw new InvalidOperationException("The collider cannot be scaled with a noncomputable scale");
            }
        }
    }
}

