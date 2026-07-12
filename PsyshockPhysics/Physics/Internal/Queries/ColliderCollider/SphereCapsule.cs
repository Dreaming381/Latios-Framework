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
            var    sphereInCapSpaceTransfrom = math.InverseTransformFast(in capsuleTransform, in sphereTransform);
            float3 sphereCenterInCapSpace    = math.transform(sphereInCapSpaceTransfrom, sphere.center);
            return PointRayCapsule.PointCapsuleWithin(sphereCenterInCapSpace, in capsule, sphere.radius + maxDistance);
        }

        public static bool DistanceBetween(in CapsuleCollider capsule,
                                           in RigidTransform capsuleTransform,
                                           in SphereCollider sphere,
                                           in RigidTransform sphereTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            var            sphereInCapSpaceTransfrom = math.InverseTransformFast(in capsuleTransform, in sphereTransform);
            float3         sphereCenterInCapSpace    = math.transform(sphereInCapSpaceTransfrom, sphere.center);
            SphereCollider sphereInCapSpace          = new SphereCollider(sphereCenterInCapSpace, sphere.radius);
            bool           hit                       = CapsuleSphereDistance(capsule,
                                                                   sphereInCapSpace,
                                                                   maxDistance,
                                                                   out ColliderDistanceResultInternal localResult);
            result = new ColliderDistanceResult
            {
                hitpointA         = math.transform(capsuleTransform, localResult.hitpointA),
                hitpointB         = math.transform(capsuleTransform, localResult.hitpointB),
                normalA           = math.rotate(capsuleTransform, localResult.normalA),
                normalB           = math.rotate(capsuleTransform, localResult.normalB),
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
            var cso     = targetCapsule;
            cso.radius += sphereToCast.radius;
            var  start  = math.transform(castStart, sphereToCast.center);
            var  ray    = new Ray(start, start + castEnd - castStart.pos);
            bool hit    = PointRayCapsule.Raycast(in ray, in cso, in targetCapsuleTransform, out var raycastResult);
            if (hit)
            {
                var hitTransform  = castStart;
                hitTransform.pos += raycastResult.position - start;
                DistanceBetween(in targetCapsule, in targetCapsuleTransform, in sphereToCast, in hitTransform, 1f, out var distanceResult);
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

        public static bool ColliderCast(in CapsuleCollider capsuleToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in SphereCollider targetSphere,
                                        in RigidTransform targetSphereTransform,
                                        out ColliderCastResult result)
        {
            var cso           = capsuleToCast;
            cso.radius       += targetSphere.radius;
            var  castReverse  = castStart.pos - castEnd;
            var  start        = math.transform(targetSphereTransform, targetSphere.center);
            var  ray          = new Ray(start, start + castReverse);
            bool hit          = PointRayCapsule.Raycast(in ray, in cso, in castStart, out var raycastResult);
            if (hit)
            {
                var hitTransform  = castStart;
                hitTransform.pos -= raycastResult.position - start;
                DistanceBetween(in capsuleToCast, in hitTransform, in targetSphere, in targetSphereTransform, 1f, out var distanceResult);
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
            if (dot <= 0f)
                return 0;
            var   castDistance = math.abs(distanceResult.distance) + capsule.radius + sphere.radius;
            var   castStart    = sphereTransform;
            float castOffset   = 0f;
            if (distanceResult.distance <= 0f)
            {
                castOffset     = (-distanceResult.distance * (1f + math.EPSILON) + math.EPSILON);
                castStart.pos += contactNormal * castOffset;
            }
            if (ColliderCast(in sphere, in castStart, castStart.pos - castDistance * contactNormal, in capsule, in capsuleTransform, out var castResult))
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

        private static bool CapsuleSphereDistance(in CapsuleCollider capsule, in SphereCollider sphere, float maxDistance, out ColliderDistanceResultInternal result)
        {
            var hit = PointRayCapsule.PointCapsuleDistance(sphere.center, in capsule, maxDistance + sphere.radius, out var pointResult);
            result  = new ColliderDistanceResultInternal
            {
                hitpointA    = pointResult.hitpoint,
                hitpointB    = sphere.center - pointResult.normal * sphere.radius,
                normalA      = pointResult.normal,
                normalB      = -pointResult.normal,
                distance     = pointResult.distance - sphere.radius,
                featureCodeA = pointResult.featureCode,
                featureCodeB = 0,
            };
            return hit;
        }
    }
}

