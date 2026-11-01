using System;
using Unity.Burst;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class SphereBox
    {
        public static bool AreOverlapping(in BoxCollider box,
                                          in RigidTransform boxTransform,
                                          in SphereCollider sphere,
                                          in RigidTransform sphereTransform)
        {
            return WithinDistance(in box, in boxTransform, in sphere, in sphereTransform, 0f);
        }

        public static bool WithinDistance(in BoxCollider box,
                                          in RigidTransform boxTransform,
                                          in SphereCollider sphere,
                                          in RigidTransform sphereTransform,
                                          float maxDistance)
        {
            var    sphereCenterInWorldBoxRelative = sphereTransform.pos - boxTransform.pos + math.rotate(sphereTransform.rot, sphere.center);
            float3 sphereCenterInBoxSpace         = math.InverseRotateFast(boxTransform.rot, sphereCenterInWorldBoxRelative);
            Unity.Burst.CompilerServices.Hint.Assume(sphere.radius >= 0f);
            return PointRayBox.PointBoxWithin(sphereCenterInBoxSpace, in box, maxDistance + sphere.radius);
        }

        public static bool DistanceBetween(in BoxCollider box,
                                           in RigidTransform boxTransform,
                                           in SphereCollider sphere,
                                           in RigidTransform sphereTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            var            sphereCenterInWorldBoxRelative = sphereTransform.pos - boxTransform.pos + math.rotate(sphereTransform.rot, sphere.center);
            float3         sphereCenterInBoxSpace         = math.InverseRotateFast(boxTransform.rot, sphereCenterInWorldBoxRelative);
            SphereCollider sphereInBoxSpace               = new SphereCollider(sphereCenterInBoxSpace, sphere.radius);
            bool           hit                            = BoxSphereDistance(in box,
                                                                    in sphereInBoxSpace,
                                                                    maxDistance,
                                                                    out ColliderDistanceResultInternal localResult);
            result = new ColliderDistanceResult
            {
                hitpointA         = math.transform(boxTransform, localResult.hitpointA),
                hitpointB         = math.transform(boxTransform, localResult.hitpointB),
                normalA           = math.rotate(boxTransform, localResult.normalA),
                normalB           = math.rotate(boxTransform, localResult.normalB),
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
                                        in BoxCollider targetBox,
                                        in RigidTransform targetBoxTransform,
                                        out ColliderCastResult result)
        {
            // Move everything relative to the target box before transforming to box space.
            var  sphereCenter     = castStart.pos - targetBoxTransform.pos + math.rotate(castStart.rot, sphereToCast.center);
            var  castDisplacement = castEnd - castStart.pos;
            var  rayStart         = math.InverseRotateFast(targetBoxTransform.rot, sphereCenter);
            var  ray              = new Ray(rayStart, rayStart + math.InverseRotateFast(targetBoxTransform.rot, castDisplacement));
            bool hit              = PointRayBox.RaycastRoundedBox(in ray, in targetBox, sphereToCast.radius, out var fraction, out var roundedNormal, out var featureNormal);
            if (hit)
            {
                roundedNormal = math.rotate(targetBoxTransform.rot, roundedNormal);
                result        = new ColliderCastResult
                {
                    hitpoint                 = targetBoxTransform.pos + (sphereCenter - roundedNormal * sphereToCast.radius + castDisplacement * fraction),
                    normalOnCaster           = -roundedNormal,
                    normalOnTarget           = math.rotate(targetBoxTransform.rot, featureNormal),
                    subColliderIndexOnCaster = 0,
                    subColliderIndexOnTarget = 0,
                    distance                 = math.length(ray.displacement) * fraction,
                };
                return true;
            }
            result = default;
            return false;
        }

        public static bool ColliderCast(in BoxCollider boxToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in SphereCollider targetSphere,
                                        in RigidTransform targetSphereTransform,
                                        out ColliderCastResult result)
        {
            // Move everything relative to the cast start before transforming to caster space and reversing the cast direction
            var  sphereCenter = targetSphereTransform.pos - castStart.pos + math.rotate(targetSphereTransform.rot, targetSphere.center);
            var  rayStart     = math.InverseRotateFast(castStart.rot, sphereCenter);
            var  ray          = new Ray(rayStart, rayStart + math.InverseRotateFast(castStart.rot, castStart.pos - castEnd));
            bool hit          = PointRayBox.RaycastRoundedBox(in ray, in boxToCast, targetSphere.radius, out var fraction, out var roundedNormal, out var featureNormal);
            if (hit)
            {
                roundedNormal = math.rotate(castStart.rot, roundedNormal);
                result        = new ColliderCastResult
                {
                    hitpoint                 = castStart.pos + (sphereCenter - roundedNormal * targetSphere.radius),
                    normalOnCaster           = math.rotate(castStart.rot, featureNormal),
                    normalOnTarget           = -roundedNormal,
                    subColliderIndexOnCaster = 0,
                    subColliderIndexOnTarget = 0,
                    distance                 = math.length(ray.displacement) * fraction
                };
                return true;
            }
            result = default;
            return false;
        }

        public static UnitySim.ContactsBetweenResult UnityContactsBetween(in BoxCollider box,
                                                                          in RigidTransform boxTransform,
                                                                          in SphereCollider sphere,
                                                                          in RigidTransform sphereTransform,
                                                                          in ColliderDistanceResult distanceResult)
        {
            return ContactManifoldHelpers.GetSingleContactManifold(in distanceResult);
        }

        public static int LatiosContactsBetween(Span<LatiosSim.Contact>   contacts,
                                                float3 contactNormal,
                                                in BoxCollider box,
                                                in RigidTransform boxTransform,
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
            var   castDistance = math.abs(distanceResult.distance) + math.length(box.halfSize) + sphere.radius;
            var   castStart    = sphereTransform;
            float castOffset   = 0f;
            if (distanceResult.distance <= 0f)
            {
                castOffset     = (-distanceResult.distance / dot) * (1f + math.EPSILON) + math.EPSILON;
                castStart.pos += contactNormal * castOffset;
            }
            if (ColliderCast(in sphere, in castStart, castStart.pos - castDistance * contactNormal, in box, in boxTransform, out var castResult))
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

        private static bool BoxSphereDistance(in BoxCollider box, in SphereCollider sphere, float maxDistance, out ColliderDistanceResultInternal result)
        {
            bool   hit     = PointRayBox.PointBoxDistance(sphere.center, in box, maxDistance + sphere.radius, out PointDistanceResultInternal pointDistanceResult);
            float3 normalB = math.normalizesafe(pointDistanceResult.hitpoint - sphere.center, -pointDistanceResult.normal);
            normalB        = math.chgsign(normalB, pointDistanceResult.distance);
            result         = new ColliderDistanceResultInternal
            {
                distance     = pointDistanceResult.distance - sphere.radius,
                hitpointA    = pointDistanceResult.hitpoint,
                hitpointB    = sphere.center + normalB * sphere.radius,
                normalA      = pointDistanceResult.normal,
                normalB      = normalB,
                featureCodeA = pointDistanceResult.featureCode,
                featureCodeB = 0
            };
            return result.distance <= maxDistance;  // Rechecked for rounding
        }
    }
}

