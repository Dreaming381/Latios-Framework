using System;
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
            return DistanceBetween(in triangle, in triangleTransform, in sphere, in sphereTransform, maxDistance, out _);
        }

        public static bool DistanceBetween(in TriangleCollider triangle,
                                           in RigidTransform triangleTransform,
                                           in SphereCollider sphere,
                                           in RigidTransform sphereTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            var            triangleWorldToLocal           = math.inverse(triangleTransform);
            var            sphereInTriangleSpaceTransform = math.mul(triangleWorldToLocal, sphereTransform);
            float3         sphereCenterInTriangleSpace    = math.transform(sphereInTriangleSpaceTransform, sphere.center);
            SphereCollider sphereInTriangleSpace          = new SphereCollider(sphereCenterInTriangleSpace, sphere.radius);
            bool           hit                            = TriangleSphereDistance(in triangle,
                                                                         in sphereInTriangleSpace,
                                                                         maxDistance,
                                                                         out ColliderDistanceResultInternal localResult);
            result = new ColliderDistanceResult
            {
                hitpointA         = math.transform(triangleTransform, localResult.hitpointA),
                hitpointB         = math.transform(triangleTransform, localResult.hitpointB),
                normalA           = math.rotate(triangleTransform, localResult.normalA),
                normalB           = math.rotate(triangleTransform, localResult.normalB),
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
            var  targetTriangleTransformInverse = math.inverse(targetTriangleTransform);
            var  casterInTargetSpace            = math.mul(targetTriangleTransformInverse, castStart);
            var  start                          = math.transform(casterInTargetSpace, sphereToCast.center);
            var  ray                            = new Ray(start, start + math.rotate(targetTriangleTransformInverse, castEnd - castStart.pos));
            bool hit                            = PointRayTriangle.RaycastRoundedTriangle( in ray,
                                                                                           targetTriangle.AsSimdFloat3(),
                                                                                           sphereToCast.radius,
                                                                                           out var fraction,
                                                                                           out _);
            if (hit)
            {
                var hitTransform = castStart;
                hitTransform.pos = math.lerp(castStart.pos, castEnd, fraction);
                DistanceBetween(in targetTriangle, in targetTriangleTransform, in sphereToCast, in hitTransform, 1f, out var distanceResult);
                result = new ColliderCastResult
                {
                    hitpoint                 = distanceResult.hitpointB,
                    normalOnCaster           = distanceResult.normalB,
                    normalOnTarget           = distanceResult.normalA,
                    subColliderIndexOnCaster = distanceResult.subColliderIndexB,
                    subColliderIndexOnTarget = distanceResult.subColliderIndexA,
                    distance                 = math.distance(hitTransform.pos, castStart.pos)
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
            var  castReverse        = castStart.pos - castEnd;
            var  worldToCasterSpace = math.inverse(castStart);
            var  start              = math.transform(targetSphereTransform, targetSphere.center);
            var  ray                = new Ray(math.transform(worldToCasterSpace, start), math.transform(worldToCasterSpace, start + castReverse));
            bool hit                = PointRayTriangle.RaycastRoundedTriangle(in ray, triangleToCast.AsSimdFloat3(), targetSphere.radius, out var fraction, out _);
            if (hit)
            {
                var hitTransform = castStart;
                hitTransform.pos = math.lerp(castStart.pos, castEnd, fraction);
                DistanceBetween(in triangleToCast, in hitTransform, in targetSphere, in targetSphereTransform, 1f, out var distanceResult);
                result = new ColliderCastResult
                {
                    hitpoint                 = distanceResult.hitpointA,
                    normalOnCaster           = distanceResult.normalA,
                    normalOnTarget           = distanceResult.normalB,
                    subColliderIndexOnCaster = distanceResult.subColliderIndexA,
                    subColliderIndexOnTarget = distanceResult.subColliderIndexB,
                    distance                 = math.distance(hitTransform.pos, castStart.pos)
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
            if (dot <= 0f)
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
                castOffset     = (-distanceResult.distance * (1f + math.EPSILON) + math.EPSILON);
                castStart.pos += contactNormal * castOffset;
            }
            if (ColliderCast(in sphere, in castStart, castStart.pos - castDistance * contactNormal, in triangle, in triangleTransform, out var castResult))
            {
                castResult.distance += castOffset;
                contacts[0]          = new LatiosSim.Contact
                {
                    contactOnA  = castResult.hitpoint,
                    distanceToB = castResult.distance
                };
                return 1;
            }
            return 0;
        }

        internal static bool TriangleSphereDistance(in TriangleCollider triangle, in SphereCollider sphere, float maxDistance, out ColliderDistanceResultInternal result)
        {
            bool   hit     = PointRayTriangle.PointTriangleDistance(sphere.center, in triangle, maxDistance + sphere.radius, out PointDistanceResultInternal pointDistanceResult);
            float3 normalB = math.normalizesafe(pointDistanceResult.hitpoint - sphere.center, -pointDistanceResult.normal);
            result         = new ColliderDistanceResultInternal
            {
                distance     = pointDistanceResult.distance - sphere.radius,
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

