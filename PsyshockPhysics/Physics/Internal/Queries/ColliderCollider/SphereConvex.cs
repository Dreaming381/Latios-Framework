using System;
using Unity.Burst;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class SphereConvex
    {
        public static bool AreOverlapping(in ConvexCollider convex, in RigidTransform convexTransform, in SphereCollider sphere, in RigidTransform sphereTransform)
        {
            return WithinDistance(in convex, in convexTransform, in sphere, in sphereTransform, 0f);
        }

        public static bool WithinDistance(in ConvexCollider convex, in RigidTransform convexTransform, in SphereCollider sphere, in RigidTransform sphereTransform,
                                          float maxDistance)
        {
            return DistanceBetween(in convex, in convexTransform, in sphere, in sphereTransform, maxDistance, out _);
        }

        public static bool DistanceBetween(in ConvexCollider convex, in RigidTransform convexTransform, in SphereCollider sphere, in RigidTransform sphereTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            var            convexWorldToLocal           = math.inverse(convexTransform);
            var            sphereInConvexSpaceTransform = math.mul(convexWorldToLocal, sphereTransform);
            float3         sphereCenterInConvexSpace    = math.transform(sphereInConvexSpaceTransform, sphere.center);
            SphereCollider sphereInConvexSpace          = new SphereCollider(sphereCenterInConvexSpace, sphere.radius);
            bool           hit                          = ConvexSphereDistance(in convex,
                                                                     in sphereInConvexSpace,
                                                                     maxDistance,
                                                                     out ColliderDistanceResultInternal localResult);
            result = new ColliderDistanceResult
            {
                hitpointA         = math.transform(convexTransform, localResult.hitpointA),
                hitpointB         = math.transform(convexTransform, localResult.hitpointB),
                normalA           = math.rotate(convexTransform, localResult.normalA),
                normalB           = math.rotate(convexTransform, localResult.normalB),
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
                                        in ConvexCollider targetConvex,
                                        in RigidTransform targetConvexTransform,
                                        out ColliderCastResult result)
        {
            var  targetConvexTransformInverse = math.inverse(targetConvexTransform);
            var  casterInTargetSpace          = math.mul(targetConvexTransformInverse, castStart);
            var  start                        = math.transform(casterInTargetSpace, sphereToCast.center);
            var  ray                          = new Ray(start, start + math.rotate(targetConvexTransformInverse, castEnd - castStart.pos));
            bool hit                          = PointRayConvex.RaycastRoundedConvex(in ray,
                                                                                    in targetConvex,
                                                                                    sphereToCast.radius,
                                                                                    out var fraction);
            if (hit)
            {
                var hitTransform = castStart;
                hitTransform.pos = math.lerp(castStart.pos, castEnd, fraction);
                DistanceBetween(in targetConvex, in targetConvexTransform, in sphereToCast, in hitTransform, 1f, out var distanceResult);
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

        public static bool ColliderCast(in ConvexCollider convexToCast,
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
            bool hit                = PointRayConvex.RaycastRoundedConvex(in ray, in convexToCast, targetSphere.radius, out var fraction);
            if (hit)
            {
                var hitTransform = castStart;
                hitTransform.pos = math.lerp(castStart.pos, castEnd, fraction);
                DistanceBetween(in convexToCast, in hitTransform, in targetSphere, in targetSphereTransform, 1f, out var distanceResult);
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

        public static UnitySim.ContactsBetweenResult UnityContactsBetween(in ConvexCollider convex,
                                                                          in RigidTransform convexTransform,
                                                                          in SphereCollider sphere,
                                                                          in RigidTransform sphereTransform,
                                                                          in ColliderDistanceResult distanceResult)
        {
            return ContactManifoldHelpers.GetSingleContactManifold(in distanceResult);
        }

        public static int LatiosContactsBetween(Span<LatiosSim.Contact>   contacts,
                                                float3 contactNormal,
                                                in ConvexCollider convex,
                                                in RigidTransform convexTransform,
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
            Physics.GetCenterExtents(Physics.AabbFrom(convex, convexTransform), out _, out var extents);
            var   castDistance = math.abs(distanceResult.distance) + math.length(extents) + sphere.radius;
            var   castStart    = sphereTransform;
            float castOffset   = 0f;
            if (distanceResult.distance <= 0f)
            {
                castOffset     = (-distanceResult.distance * (1f + math.EPSILON) + math.EPSILON);
                castStart.pos += contactNormal * castOffset;
            }
            if (ColliderCast(in sphere, in castStart, castStart.pos - castDistance * contactNormal, in convex, in convexTransform, out var castResult))
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

        private static bool ConvexSphereDistance(in ConvexCollider convex, in SphereCollider sphere, float maxDistance, out ColliderDistanceResultInternal result)
        {
            bool   hit     = PointRayConvex.PointConvexDistance(sphere.center, in convex, maxDistance + sphere.radius, out PointDistanceResultInternal pointDistanceResult);
            float3 normalB = math.normalizesafe(pointDistanceResult.hitpoint - sphere.center, -pointDistanceResult.normal);
            result         = new ColliderDistanceResultInternal
            {
                distance     = pointDistanceResult.distance - sphere.radius,
                hitpointA    = pointDistanceResult.hitpoint,
                hitpointB    = sphere.center + normalB * sphere.radius,
                normalA      = pointDistanceResult.normal,
                normalB      = normalB,
                featureCodeA = pointDistanceResult.featureCode,
            };
            return hit;
        }
    }
}

