using System;
using Latios.Calci;
using Unity.Burst;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class SphereTriangle
    {
        public static bool AreOverlapping(in TriangleCollider triangle,
                                          in RigidTransform triangleTransform,
                                          in SphereCollider sphere,
                                          in RigidTransform sphereTransform)
        {
            return WithinDistance(in triangle, in triangleTransform, in sphere, in sphereTransform, 0f);
        }

        public static bool WithinDistance(in TriangleCollider triangle,
                                          in RigidTransform triangleTransform,
                                          in SphereCollider sphere,
                                          in RigidTransform sphereTransform,
                                          float maxDistance)
        {
            // Transform to world-space relative to the triangle
            // Todo: Is it faster to transform the sphere into triangle space? It is less instructions, but a larger latency chain.
            float3     sphereCenterInWorldTriangleRelative = sphereTransform.pos - triangleTransform.pos + math.rotate(sphereTransform.rot, sphere.center);
            simdFloat3 triPointsLocal                      = triangle.AsSimdFloat3();
            simdFloat3 triPointsTransformed                = simd.rotate(triangleTransform.rot, triPointsLocal);
            return PointRayTriangle.PointTriangleWithin(sphereCenterInWorldTriangleRelative, in triPointsTransformed, maxDistance + sphere.radius);
        }

        public static bool DistanceBetween(in TriangleCollider triangle,
                                           in RigidTransform triangleTransform,
                                           in SphereCollider sphere,
                                           in RigidTransform sphereTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            // Transform to world-space relative to the triangle
            float3         sphereCenterInWorldTriangleRelative = sphereTransform.pos - triangleTransform.pos + math.rotate(sphereTransform.rot, sphere.center);
            simdFloat3     triPointsLocal                      = triangle.AsSimdFloat3();
            simdFloat3     triPointsTransformed                = simd.rotate(triangleTransform.rot, triPointsLocal);
            SphereCollider sphereTransformed                   = new SphereCollider(sphereCenterInWorldTriangleRelative, sphere.radius);
            bool           hit                                 = TriangleSphereDistance(in triPointsTransformed,
                                                                              in sphereTransformed,
                                                                              maxDistance,
                                                                              out ColliderDistanceResultInternal localResult);
            result = new ColliderDistanceResult
            {
                hitpointA         = localResult.hitpointA + triangleTransform.pos,
                hitpointB         = localResult.hitpointB + triangleTransform.pos,
                normalA           = localResult.normalA,
                normalB           = localResult.normalB,
                distance          = localResult.distance,
                subColliderIndexA = 0,
                subColliderIndexB = 0,
                featureCodeA      = localResult.featureCodeA,
                featureCodeB      = 0
            };
            return hit;
        }

        public static bool ColliderCast(in SphereCollider sphereToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in TriangleCollider targetTriangle,
                                        in RigidTransform targetTriangleTransform,
                                        out ColliderCastResult result)
        {
            // Transform into world-space relative to the triangle
            var  triPoints        = simd.rotate(targetTriangleTransform.rot, targetTriangle.AsSimdFloat3());
            var  sphereCenter     = castStart.pos - targetTriangleTransform.pos + math.rotate(castStart.rot, sphereToCast.center);
            var  castDisplacement = castEnd - castStart.pos;
            var  ray              = new Ray(sphereCenter, sphereCenter + castDisplacement);
            bool hit              = PointRayTriangle.RaycastRoundedTriangle(in ray, in triPoints, sphereToCast.radius, out var fraction, out _);
            if (hit)
            {
                var sphereAtHit = new SphereCollider(sphereCenter + castDisplacement * fraction, sphereToCast.radius);
                TriangleSphereDistance(in triPoints, in sphereAtHit, float.MaxValue, out var distanceResult);
                result = new ColliderCastResult
                {
                    hitpoint                 = distanceResult.hitpointB + targetTriangleTransform.pos,
                    normalOnCaster           = distanceResult.normalB,
                    normalOnTarget           = distanceResult.normalA,
                    subColliderIndexOnCaster = 0,
                    subColliderIndexOnTarget = 0,
                    distance                 = math.length(castDisplacement) * fraction,
                };
                return true;
            }
            result = default;
            return false;
        }

        public static bool ColliderCast(in TriangleCollider triangleToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in SphereCollider targetSphere,
                                        in RigidTransform targetSphereTransform,
                                        out ColliderCastResult result)
        {
            // Transform to world-space relative to the triangle and reverse-cast
            var  triPoints          = simd.rotate(castStart.rot, triangleToCast.AsSimdFloat3());
            var  sphereCenterOffset = math.rotate(targetSphereTransform.rot, targetSphere.center);
            var  sphereCenter       = targetSphereTransform.pos - castStart.pos + sphereCenterOffset;
            var  castReverse        = castStart.pos - castEnd;
            var  ray                = new Ray(sphereCenter, sphereCenter + castReverse);
            bool hit                = PointRayTriangle.RaycastRoundedTriangle(in ray, in triPoints, targetSphere.radius, out var fraction, out _);
            if (hit)
            {
                var sphereAtHit = new SphereCollider(sphereCenter + castReverse * fraction, targetSphere.radius);
                TriangleSphereDistance(in triPoints, in sphereAtHit, float.MaxValue, out var distanceResult);
                result = new ColliderCastResult
                {
                    hitpoint                 = distanceResult.hitpointA - sphereAtHit.center + sphereCenterOffset + targetSphereTransform.pos,
                    normalOnCaster           = distanceResult.normalA,
                    normalOnTarget           = distanceResult.normalB,
                    subColliderIndexOnCaster = 0,
                    subColliderIndexOnTarget = 0,
                    distance                 = math.length(castReverse) * fraction
                };
                return true;
            }
            result = default;
            return false;
        }

        public static UnitySim.ContactsBetweenResult UnityContactsBetween(in TriangleCollider triangle,
                                                                          in RigidTransform triangleTransform,
                                                                          in SphereCollider sphere,
                                                                          in RigidTransform sphereTransform,
                                                                          in ColliderDistanceResult distanceResult)
        {
            return ContactManifoldHelpers.GetSingleContactManifold(in distanceResult);
        }

        public static int LatiosContactsBetween(Span<LatiosSim.Contact>   contacts,
                                                float3 contactNormal,
                                                in TriangleCollider triangle,
                                                in RigidTransform triangleTransform,
                                                in SphereCollider sphere,
                                                in RigidTransform sphereTransform,
                                                in ColliderDistanceResult distanceResult)
        {
            if (contacts.Length == 0)
                return 0;
            var dot = math.dot(contactNormal, distanceResult.normalA);
            if (dot > 0.999f)
            {
                contacts[0] = new LatiosSim.Contact
                {
                    contactOnA  = distanceResult.hitpointA,
                    distanceToB = distanceResult.distance
                };
                return 1;
            }
            if (dot <= math.EPSILON)
                return 0;
            var triangleCenter = (triangle.pointA + triangle.pointB + triangle.pointC) / 3f;
            var triangleRadius =
                math.sqrt(math.max(math.distancesq(triangle.pointA, triangleCenter),
                                   math.max(math.distancesq(triangle.pointB, triangleCenter), math.distancesq(triangle.pointC, triangleCenter))));
            var   castDistance = math.abs(distanceResult.distance) + triangleRadius + sphere.radius;
            var   castStart    = sphereTransform;
            float castOffset   = 0f;
            if (distanceResult.distance <= 0f)
            {
                castOffset     = (-distanceResult.distance / dot) * (1f + math.EPSILON) + math.EPSILON;
                castStart.pos += contactNormal * castOffset;
            }
            if (ColliderCast(in sphere, in castStart, castStart.pos - castDistance * contactNormal, in triangle, in triangleTransform, out var castResult))
            {
                castResult.distance -= castOffset;
                contacts[0]          = new LatiosSim.Contact
                {
                    contactOnA  = castResult.hitpoint,
                    distanceToB = castResult.distance
                };
                return 1;
            }
            return 0;
        }

        internal static bool TriangleSphereDistance(in simdFloat3 triPoints, in SphereCollider sphere, float maxDistance, out ColliderDistanceResultInternal result)
        {
            bool   hit     = PointRayTriangle.PointTriangleDistance(sphere.center, in triPoints, maxDistance + sphere.radius, out PointDistanceResultInternal pointDistanceResult);
            float3 normalB = math.normalizesafe(pointDistanceResult.hitpoint - sphere.center, -pointDistanceResult.normal);
            result         = new ColliderDistanceResultInternal
            {
                distance     = math.min(pointDistanceResult.distance - sphere.radius, maxDistance),
                hitpointA    = pointDistanceResult.hitpoint,
                hitpointB    = sphere.center + normalB * sphere.radius,
                normalA      = pointDistanceResult.normal,
                normalB      = normalB,
                featureCodeA = pointDistanceResult.featureCode
            };
            return hit;
        }
    }
}

