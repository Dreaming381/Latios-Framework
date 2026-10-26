using System;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class SphereCapsule
    {
        public static bool AreOverlapping(in CapsuleCollider capsule,
                                          in RigidTransform capsuleTransform,
                                          in SphereCollider sphere,
                                          in RigidTransform sphereTransform)
        {
            return WithinDistance(in capsule, in capsuleTransform, in sphere, in sphereTransform, 0f);
        }

        public static bool WithinDistance(in CapsuleCollider capsule,
                                          in RigidTransform capsuleTransform,
                                          in SphereCollider sphere,
                                          in RigidTransform sphereTransform,
                                          float maxDistance)
        {
            var    sphereInCapSpaceTransform = math.InverseTransformFast(in capsuleTransform, in sphereTransform);
            float3 sphereCenterInCapSpace    = math.transform(sphereInCapSpaceTransform, sphere.center);
            return PointRayCapsule.PointCapsuleWithin(sphereCenterInCapSpace, in capsule, sphere.radius + maxDistance);
        }

        public static bool DistanceBetween(in CapsuleCollider capsule,
                                           in RigidTransform capsuleTransform,
                                           in SphereCollider sphere,
                                           in RigidTransform sphereTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            // Transform everything into the world-space relative to the capsule
            var transformedCapsule = new CapsuleCollider(math.rotate(capsuleTransform.rot, capsule.pointA),
                                                         math.rotate(capsuleTransform.rot, capsule.pointB),
                                                         capsule.radius);
            var transformedSphere = new SphereCollider(sphereTransform.pos - capsuleTransform.pos + math.rotate(sphereTransform.rot, sphere.center),
                                                       sphere.radius);
            bool hit = CapsuleSphereDistance(transformedCapsule, transformedSphere, maxDistance, out var localResult);
            result   = new ColliderDistanceResult
            {
                hitpointA         = localResult.hitpointA + capsuleTransform.pos,
                hitpointB         = localResult.hitpointB + capsuleTransform.pos,
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
                                        in CapsuleCollider targetCapsule,
                                        in RigidTransform targetCapsuleTransform,
                                        out ColliderCastResult result)
        {
            // Evaluate in world-space relative to the target
            var rayStart        = castStart.pos - targetCapsuleTransform.pos + math.rotate(castStart.rot, sphereToCast.center);
            var rayDisplacement = castEnd - castStart.pos;
            var cso             = new CapsuleCollider(math.rotate(targetCapsuleTransform.rot, targetCapsule.pointA),
                                                      math.rotate(targetCapsuleTransform.rot, targetCapsule.pointB),
                                                      targetCapsule.radius + sphereToCast.radius);
            bool hit = PointRayCapsule.RaycastCapsule(rayStart, rayDisplacement, in cso, out var fraction, out var normal);
            if (hit)
            {
                var rayHitpointRelative = rayStart + rayDisplacement * fraction;
                var hitpointRelative    = rayHitpointRelative - normal * sphereToCast.radius;  // Subtract the sphere from the CSO
                result                  = new ColliderCastResult
                {
                    hitpoint                 = hitpointRelative + targetCapsuleTransform.pos,
                    normalOnCaster           = -normal,
                    normalOnTarget           = normal,
                    subColliderIndexOnCaster = 0,
                    subColliderIndexOnTarget = 0,
                    distance                 = fraction * math.length(rayDisplacement)
                };
                return true;
            }
            result = default;
            return false;
        }

        public static bool ColliderCast(in CapsuleCollider capsuleToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in SphereCollider targetSphere,
                                        in RigidTransform targetSphereTransform,
                                        out ColliderCastResult result)
        {
            // Evaluate in world-space relative to the caster and reverse the cast direction
            var rayStart        = targetSphereTransform.pos - castStart.pos + math.rotate(targetSphereTransform.rot, targetSphere.center);
            var rayDisplacement = castStart.pos - castEnd;
            var cso             = new CapsuleCollider(math.rotate(castStart.rot, capsuleToCast.pointA),
                                                      math.rotate(castStart.rot, capsuleToCast.pointB),
                                                      targetSphere.radius + capsuleToCast.radius);
            bool hit = PointRayCapsule.RaycastCapsule(rayStart, rayDisplacement, in cso, out var fraction, out var normal);
            if (hit)
            {
                var hitpoint = (rayStart - normal * targetSphere.radius) + castStart.pos;
                result       = new ColliderCastResult
                {
                    hitpoint                 = hitpoint,
                    normalOnCaster           = normal,
                    normalOnTarget           = -normal,
                    subColliderIndexOnCaster = 0,
                    subColliderIndexOnTarget = 0,
                    distance                 = fraction * math.length(rayDisplacement)
                };
                return true;
            }
            result = default;
            return false;
        }

        public static UnitySim.ContactsBetweenResult UnityContactsBetween(in CapsuleCollider capsule,
                                                                          in RigidTransform capsuleTransform,
                                                                          in SphereCollider sphere,
                                                                          in RigidTransform sphereTransform,
                                                                          in ColliderDistanceResult distanceResult)
        {
            return ContactManifoldHelpers.GetSingleContactManifold(in distanceResult);
        }

        public static int LatiosContactsBetween(Span<LatiosSim.Contact>   contacts,
                                                float3 contactNormal,
                                                in CapsuleCollider capsule,
                                                in RigidTransform capsuleTransform,
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
            var   castDistance = math.abs(distanceResult.distance) + capsule.radius + sphere.radius;
            var   castStart    = sphereTransform;
            float castOffset   = 0f;
            if (distanceResult.distance <= 0f)
            {
                castOffset     = (-distanceResult.distance / dot) * (1f + math.EPSILON) + math.EPSILON;
                castStart.pos += contactNormal * castOffset;
            }
            if (ColliderCast(in sphere, in castStart, castStart.pos - castDistance * contactNormal, in capsule, in capsuleTransform, out var castResult))
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

        private static bool CapsuleSphereDistance(in CapsuleCollider capsule, in SphereCollider sphere, float maxDistance, out ColliderDistanceResultInternal result)
        {
            var hit = PointRayCapsule.PointCapsuleDistance(sphere.center, in capsule, maxDistance + sphere.radius, out var pointResult);
            result  = new ColliderDistanceResultInternal
            {
                hitpointA = pointResult.hitpoint,
                hitpointB = sphere.center - pointResult.normal * sphere.radius,
                normalA   = pointResult.normal,
                normalB   = -pointResult.normal,
                // In case of a rounding error, distance might still be too far after subtracting the radius, so just clamp it
                distance     = math.min(maxDistance, pointResult.distance - sphere.radius),
                featureCodeA = pointResult.featureCode,
                featureCodeB = 0,
            };
            return hit;
        }
    }
}

