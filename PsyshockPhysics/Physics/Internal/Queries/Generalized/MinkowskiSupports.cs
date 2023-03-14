using Latios.Transforms;
using Unity.Mathematics;

// This file contains all the support mapping and collider-specific utilty functions used by GJK, EPA, and MPR

namespace Latios.Psyshock
{
    internal static class MinkowskiSupports
    {
        public static float3 GetSupport(in Collider collider, int id)
        {
            switch (collider.type)
            {
                case ColliderType.Sphere:
                {
                    return collider.m_sphere.center;
                }
                case ColliderType.Capsule:
                {
                    return math.select(collider.m_capsule.pointA, collider.m_capsule.pointB, id > 0);
                }
                case ColliderType.Box:
                {
                    bool3 isNegative = (new int3(1, 2, 4) & id) != 0;
                    return collider.m_box.center + math.select(collider.m_box.halfSize, -collider.m_box.halfSize, isNegative);
                }
                case ColliderType.Triangle:
                {
                    var temp = math.select(collider.m_triangle.pointA, collider.m_triangle.pointB, id > 0);
                    return math.select(temp, collider.m_triangle.pointC, id > 1);
                }
                case ColliderType.Convex:
                {
                    ref var blob = ref collider.m_convex.convexColliderBlob.Value;
                    return new float3(blob.verticesX[id], blob.verticesY[id], blob.verticesZ[id]) * collider.m_convex.scale;
                }
                default: return float3.zero;
            }
        }

        private static SupportPoint GetSupport(in Collider collider, float3 direction)
        {
            switch (collider.type)
            {
                case ColliderType.Sphere:
                {
                    return new SupportPoint { pos = collider.m_sphere.center, id = 0 };
                }
                case ColliderType.Capsule:
                {
                    bool aWins = math.dot(collider.m_capsule.pointA, direction) >= math.dot(collider.m_capsule.pointB, direction);
                    return new SupportPoint
                    {
                        pos = math.select(collider.m_capsule.pointB, collider.m_capsule.pointA, aWins),
                        id  = math.select(1u, 0u, aWins)
                    };
                }
                case ColliderType.Box:
                {
                    bool4 isNegative = new bool4(direction < 0f, false);
                    return new SupportPoint
                    {
                        pos = collider.m_box.center + math.select(collider.m_box.halfSize, -collider.m_box.halfSize, isNegative.xyz),
                        id  = (uint)math.bitmask(isNegative)
                    };
                }
                case ColliderType.Triangle:
                {
                    simdFloat3 triPoints = new simdFloat3(collider.m_triangle.pointA, collider.m_triangle.pointB, collider.m_triangle.pointC, collider.m_triangle.pointA);
                    float3     dot       = simd.dot(triPoints, direction).xyz;
                    int        id        = math.tzcnt(math.bitmask(new bool4(math.cmax(dot) == dot, true)));
                    return new SupportPoint
                    {
                        pos = triPoints[id],
                        id  = (uint)id
                    };
                }
                case ColliderType.Convex:
                {
                    ref var blob            = ref collider.m_convex.convexColliderBlob.Value;
                    int     id              = 0;
                    float   bestDot         = float.MinValue;
                    float3  scaledDirection = direction * collider.m_convex.scale;
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
                        pos = new float3(blob.verticesX[id], blob.verticesY[id], blob.verticesZ[id]) * collider.m_convex.scale,
                        id  = (uint)id
                    };
                }
                default: return default;
            }
        }

        private static SupportPoint GetSupport(in Collider collider, float3 direction, in RigidTransform bInA)
        {
            switch (collider.type)
            {
                case ColliderType.Sphere:
                {
                    return new SupportPoint { pos = math.transform(bInA, collider.m_sphere.center), id = 0 };
                }
                case ColliderType.Capsule:
                {
                    float3 directionInA = math.rotate(math.inverse(bInA.rot), direction);
                    bool   aWins        = math.dot(collider.m_capsule.pointA, directionInA) >= math.dot(collider.m_capsule.pointB, directionInA);
                    return new SupportPoint
                    {
                        pos = math.transform(bInA, math.select(collider.m_capsule.pointB, collider.m_capsule.pointA, aWins)),
                        id  = math.select(1u, 0u, aWins)
                    };
                }
                case ColliderType.Box:
                {
                    float3 directionInA = math.rotate(math.inverse(bInA.rot), direction);
                    bool4  isNegative   = new bool4(directionInA < 0f, false);
                    return new SupportPoint
                    {
                        pos = math.transform(bInA, collider.m_box.center + math.select(collider.m_box.halfSize, -collider.m_box.halfSize, isNegative.xyz)),
                        id  = (uint)math.bitmask(isNegative)
                    };
                }
                case ColliderType.Triangle:
                {
                    float3     directionInA = math.rotate(math.inverse(bInA.rot), direction);
                    simdFloat3 triPoints    = new simdFloat3(collider.m_triangle.pointA, collider.m_triangle.pointB, collider.m_triangle.pointC, collider.m_triangle.pointA);
                    float3     dot          = simd.dot(triPoints, directionInA).xyz;
                    int        id           = math.tzcnt(math.bitmask(new bool4(math.cmax(dot) == dot, true)));
                    return new SupportPoint
                    {
                        pos = math.transform(bInA, triPoints[id]),
                        id  = (uint)id
                    };
                }
                case ColliderType.Convex:
                {
                    float3  scaledDirectionInA = math.rotate(math.inverse(bInA.rot), direction) * collider.m_convex.scale;
                    ref var blob               = ref collider.m_convex.convexColliderBlob.Value;
                    int     id                 = 0;
                    float   bestDot            = float.MinValue;
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
                        pos = math.transform(bInA, new float3(blob.verticesX[id], blob.verticesY[id], blob.verticesZ[id]) * collider.m_convex.scale),
                        id  = (uint)id
                    };
                }
                default: return default;
            }
        }

        public static SupportPoint GetSupport(in Collider colliderA, in Collider colliderB, float3 direction, in RigidTransform bInA)
        {
            var a = GetSupport(in colliderA, direction);
            var b = GetSupport(in colliderB, -direction, in bInA);
            return new SupportPoint
            {
                pos = a.pos - b.pos,
                id  = (a.id << 16) | b.id
            };
        }

        public static SupportPoint Get3DSupportFromPlanar(in Collider colliderA, in Collider colliderB, in RigidTransform bInA, in SupportPoint planarSupport)
        {
            var a = GetSupport(in colliderA, planarSupport.idA);
            var b = GetSupport(in colliderB, planarSupport.idB);
            return new SupportPoint
            {
                pos = a - math.transform(bInA, b),
                id  = planarSupport.id
            };
        }

        public static Aabb GetCsoAabb(in Collider colliderA, in Collider colliderB, in RigidTransform bInA)
        {
            var aabbA = Physics.AabbFrom(in colliderA, in RigidTransform.identity);
            var aabbB = Physics.AabbFrom(in colliderB, in bInA);
            return new Aabb(aabbA.min - aabbB.max, aabbA.max - aabbB.min);
        }

        public static float GetRadialPadding(in Collider collider)
        {
            switch (collider.type)
            {
                case ColliderType.Sphere:
                {
                    return collider.m_sphere.radius;
                }
                case ColliderType.Capsule:
                {
                    return collider.m_capsule.radius;
                }
                case ColliderType.Box: return 0f;
                case ColliderType.Triangle: return 0f;
                case ColliderType.Convex: return 0f;
                default: return 0f;
            }
        }

        public static SupportPoint GetPlanarSupport(in Collider colliderA, in Collider colliderB, float3 direction, in RigidTransform bInA, float3 planeNormal)
        {
            var    a          = GetSupport(in colliderA, direction);
            var    b          = GetSupport(in colliderB, -direction, in bInA);
            float3 supportPos = a.pos - b.pos;

            return new SupportPoint
            {
                pos = supportPos - math.dot(planeNormal, supportPos) * planeNormal,
                id  = (a.id << 16) | b.id
            };
        }
    }
}

