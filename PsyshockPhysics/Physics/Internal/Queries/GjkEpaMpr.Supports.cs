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
                case ColliderType.Triangle:
                {
                    TriangleCollider triangle = collider;
                    var              temp     = math.select(triangle.pointA, triangle.pointB, id > 0);
                    return math.select(temp, triangle.pointC, id > 1);
                }
                case ColliderType.Convex:
                {
                    ConvexCollider convex = collider;
                    ref var        blob   = ref convex.convexColliderBlob.Value;
                    return new float3(blob.verticesX[id], blob.verticesY[id], blob.verticesZ[id]) * convex.scale;
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
                case ColliderType.Triangle:
                {
                    TriangleCollider triangle  = collider;
                    simdFloat3       triPoints = new simdFloat3(triangle.pointA, triangle.pointB, triangle.pointC, triangle.pointA);
                    float3           dot       = simd.dot(triPoints, direction).xyz;
                    int              id        = math.tzcnt(math.bitmask(new bool4(math.cmax(dot) == dot, true)));
                    return new SupportPoint
                    {
                        pos = triPoints[id],
                        id  = (uint)id
                    };
                }
                case ColliderType.Convex:
                {
                    ConvexCollider convex          = collider;
                    ref var        blob            = ref convex.convexColliderBlob.Value;
                    int            id              = 0;
                    float          bestDot         = float.MinValue;
                    float3         scaledDirection = direction * convex.scale;
                    for (int i = 0; i < blob.verticesX.Length; i++)
                    {
                        float dot = scaledDirection.x * blob.verticesX[i] + scaledDirection.y * blob.verticesY[i] + scaledDirection.z * blob.verticesZ[i];
                        if (dot > bestDot)
                        {
                            bestDot = dot;
                            id      = i;
                        }
                    }
                    return new SupportPoint
                    {
                        pos = new float3(blob.verticesX[id], blob.verticesY[id], blob.verticesZ[id]) * convex.scale,
                        id  = (uint)id
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
                case ColliderType.Triangle:
                {
                    TriangleCollider triangle     = collider;
                    float3           directionInA = math.rotate(math.inverse(bInA.rot), direction);
                    simdFloat3       triPoints    = new simdFloat3(triangle.pointA, triangle.pointB, triangle.pointC, triangle.pointA);
                    float3           dot          = simd.dot(triPoints, directionInA).xyz;
                    int              id           = math.tzcnt(math.bitmask(new bool4(math.cmax(dot) == dot, true)));
                    return new SupportPoint
                    {
                        pos = math.transform(bInA, triPoints[id]),
                        id  = (uint)id
                    };
                }
                case ColliderType.Convex:
                {
                    ConvexCollider convex             = collider;
                    float3         scaledDirectionInA = math.rotate(math.inverse(bInA.rot), direction) * convex.scale;
                    ref var        blob               = ref convex.convexColliderBlob.Value;
                    int            id                 = 0;
                    float          bestDot            = float.MinValue;
                    for (int i = 0; i < blob.verticesX.Length; i++)
                    {
                        float dot = scaledDirectionInA.x * blob.verticesX[i] + scaledDirectionInA.y * blob.verticesY[i] + scaledDirectionInA.z * blob.verticesZ[i];
                        if (dot > bestDot)
                        {
                            bestDot = dot;
                            id      = i;
                        }
                    }
                    return new SupportPoint
                    {
                        pos = math.transform(bInA, new float3(blob.verticesX[id], blob.verticesY[id], blob.verticesZ[id]) * convex.scale),
                        id  = (uint)id
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
                case ColliderType.Triangle: return 0f;
                case ColliderType.Convex: return 0f;
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
    }
}

