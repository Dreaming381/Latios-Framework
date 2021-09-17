using Unity.Mathematics;

// This file contains all the support mapping and collider-specific utilty functions used by GJK, EPA, and MPR

namespace Latios.Psyshock
{
    internal static partial class SpatialInternal
    {
        internal struct SupportPoint
        {
            public float3 pos;
            public uint   id;

            public int idA => (int)(id >> 16);
            public int idB => (int)(id & 0xffff);
        }

        private static float3 GetSupport(Collider collider, int id)
        {
            switch (collider.type)
            {
                case ColliderType.Sphere:
                {
                    SphereCollider sphere = collider;
                    return sphere.center;
                }
                case ColliderType.Capsule:
                {
                    CapsuleCollider capsule = collider;
                    return math.select(capsule.pointA, capsule.pointB, id > 0);
                }
                case ColliderType.Box:
                {
                    BoxCollider box        = collider;
                    bool3       isNegative = (new int3(1, 2, 4) & id) != 0;
                    return box.center + math.select(box.halfSize, -box.halfSize, isNegative);
                }
                default: return float3.zero;
            }
        }

        private static SupportPoint GetSupport(Collider collider, float3 direction)
        {
            switch (collider.type)
            {
                case ColliderType.Sphere:
                {
                    SphereCollider sphere         = collider;
                    return new SupportPoint { pos = sphere.center, id = 0 };
                }
                case ColliderType.Capsule:
                {
                    CapsuleCollider capsule = collider;
                    bool            aWins   = math.dot(capsule.pointA, direction) >= math.dot(capsule.pointB, direction);
                    return new SupportPoint
                    {
                        pos = math.select(capsule.pointB, capsule.pointA, aWins),
                        id  = math.select(1u, 0u, aWins)
                    };
                }
                case ColliderType.Box:
                {
                    BoxCollider box        = collider;
                    bool4       isNegative = new bool4(direction < 0f, false);
                    return new SupportPoint
                    {
                        pos = box.center + math.select(box.halfSize, -box.halfSize, isNegative.xyz),
                        id  = (uint)math.bitmask(isNegative)
                    };
                }
                default: return default;
            }
        }

        private static SupportPoint GetSupport(Collider collider, float3 direction, RigidTransform bInA)
        {
            switch (collider.type)
            {
                case ColliderType.Sphere:
                {
                    SphereCollider sphere         = collider;
                    return new SupportPoint { pos = math.transform(bInA, sphere.center), id = 0 };
                }
                case ColliderType.Capsule:
                {
                    CapsuleCollider capsule      = collider;
                    float3          directionInA = math.rotate(math.inverse(bInA.rot), direction);
                    bool            aWins        = math.dot(capsule.pointA, directionInA) >= math.dot(capsule.pointB, directionInA);
                    return new SupportPoint
                    {
                        pos = math.transform(bInA, math.select(capsule.pointB, capsule.pointA, aWins)),
                        id  = math.select(1u, 0u, aWins)
                    };
                }
                case ColliderType.Box:
                {
                    BoxCollider box          = collider;
                    float3      directionInA = math.rotate(math.inverse(bInA.rot), direction);
                    bool4       isNegative   = new bool4(directionInA < 0f, false);
                    return new SupportPoint
                    {
                        pos = math.transform(bInA, box.center + math.select(box.halfSize, -box.halfSize, isNegative.xyz)),
                        id  = (uint)math.bitmask(isNegative)
                    };
                }
                default: return default;
            }
        }

        private static SupportPoint GetSupport(Collider colliderA, Collider colliderB, float3 direction, RigidTransform bInA)
        {
            var a = GetSupport(colliderA, direction);
            var b = GetSupport(colliderB, -direction, bInA);
            return new SupportPoint
            {
                pos = a.pos - b.pos,
                id  = (a.id << 16) | b.id
            };
        }

        private static SupportPoint Get3DSupportFromPlanar(Collider colliderA, Collider colliderB, RigidTransform bInA, SupportPoint planarSupport)
        {
            var a = GetSupport(colliderA, planarSupport.idA);
            var b = GetSupport(colliderB, planarSupport.idB);
            return new SupportPoint
            {
                pos = a - math.transform(bInA, b),
                id  = planarSupport.id
            };
        }

        private static Aabb GetCsoAabb(Collider colliderA, Collider colliderB, RigidTransform bInA)
        {
            var aabbA = Physics.AabbFrom(colliderA, RigidTransform.identity);
            var aabbB = Physics.AabbFrom(colliderB, bInA);
            return new Aabb(aabbA.min - aabbB.max, aabbA.max - aabbB.min);
        }

        private static float GetRadialPadding(Collider collider)
        {
            switch (collider.type)
            {
                case ColliderType.Sphere:
                {
                    SphereCollider sphere = collider;
                    return sphere.radius;
                }
                case ColliderType.Capsule:
                {
                    CapsuleCollider capsule = collider;
                    return capsule.radius;
                }
                case ColliderType.Box: return 0f;
                default: return 0f;
            }
        }

        private static float3 GetCenter(Collider collider)
        {
            switch (collider.type)
            {
                case ColliderType.Sphere:
                {
                    SphereCollider sphere = collider;
                    return sphere.center;
                }
                case ColliderType.Capsule:
                {
                    CapsuleCollider capsule = collider;
                    return (capsule.pointA + capsule.pointB) / 2f;
                }
                case ColliderType.Box:
                {
                    BoxCollider box = collider;
                    return box.center;
                }
                default: return 0f;
            }
        }

        private static SupportPoint GetPlanarSupport(Collider colliderA, Collider colliderB, float3 direction, RigidTransform bInA, float3 planeNormal)
        {
            var    a          = GetSupport(colliderA, direction);
            var    b          = GetSupport(colliderB, -direction, bInA);
            float3 supportPos = a.pos - b.pos;

            return new SupportPoint
            {
                pos = supportPos - math.dot(planeNormal, supportPos) * planeNormal,
                id  = (a.id << 16) | b.id
            };
        }

        private static float3 GetPlanarCenter(Collider colliderA, Collider colliderB, RigidTransform bInA, float3 planeNormal)
        {
            var    a         = GetCenter(colliderA);
            var    b         = math.transform(bInA, GetCenter(colliderB));
            float3 centerPos = a - b;
            return centerPos - math.dot(planeNormal, centerPos) * planeNormal;
        }
    }
}

