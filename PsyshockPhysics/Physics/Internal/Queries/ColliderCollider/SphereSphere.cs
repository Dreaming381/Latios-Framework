using System;
using System.Drawing;
using Unity.Burst;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class SphereSphere
    {
        public static bool AreOverlapping(in SphereCollider sphereA,
                                          in RigidTransform aTransform,
                                          in SphereCollider sphereB,
                                          in RigidTransform bTransform)
        {
            // Evaluate in world-space shifted to A's position.
            var aCenter = math.rotate(aTransform.rot, sphereA.center);
            var bCenter = bTransform.pos - aTransform.pos + math.rotate(bTransform.rot, sphereB.center);
            return math.distancesq(aCenter, bCenter) <= math.square(sphereA.radius + sphereB.radius);
        }

        public static bool WithinDistance(in SphereCollider sphereA,
                                          in RigidTransform aTransform,
                                          in SphereCollider sphereB,
                                          in RigidTransform bTransform,
                                          float maxDistance)
        {
            // Evaluate in world-space shifted to A's position.
            var aCenter   = math.rotate(aTransform.rot, sphereA.center);
            var bCenter   = bTransform.pos - aTransform.pos + math.rotate(bTransform.rot, sphereB.center);
            var threshold = sphereA.radius + sphereB.radius + maxDistance;
            return math.distancesq(aCenter, bCenter) <= math.square(threshold) && threshold >= 0f;
        }

        public static bool DistanceBetween(in SphereCollider sphereA,
                                           in RigidTransform aTransform,
                                           in SphereCollider sphereB,
                                           in RigidTransform bTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            // Evaluate in world-space shifted to A's position.
            var  aWorldRelative = new SphereCollider(math.rotate(aTransform.rot, sphereA.center), sphereA.radius);
            var  bWorldRelative = new SphereCollider(bTransform.pos - aTransform.pos + math.rotate(bTransform.rot, sphereB.center), sphereB.radius);
            bool hit            = SphereSphereDistance(in aWorldRelative, in bWorldRelative, maxDistance, out ColliderDistanceResultInternal localResult, out _);
            result              = new ColliderDistanceResult
            {
                hitpointA         = localResult.hitpointA + aTransform.pos,
                hitpointB         = localResult.hitpointB + aTransform.pos,
                normalA           = localResult.normalA,
                normalB           = localResult.normalB,
                distance          = localResult.distance,
                subColliderIndexA = 0,
                subColliderIndexB = 0,
                featureCodeA      = 0,
                featureCodeB      = 0,
            };
            return hit;
        }

        public static bool ColliderCast(in SphereCollider sphereToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in SphereCollider targetSphere,
                                        in RigidTransform targetSphereTransform,
                                        out ColliderCastResult result)
        {
            // Shift everything relative to target's position in world-space
            var  castCenter   = castStart.pos - targetSphereTransform.pos + math.rotate(castStart.rot, sphereToCast.center);
            var  targetCenter = math.rotate(targetSphereTransform.rot, targetSphere.center);
            var  cso          = new SphereCollider(targetCenter, sphereToCast.radius + targetSphere.radius);
            var  displacement = castEnd - castStart.pos;
            bool hit          = PointRaySphere.RaycastSphere(castCenter, displacement, in cso, out var fraction, out var normal);
            if (hit)
            {
                var hitpoint = targetSphereTransform.pos + targetCenter + normal * targetSphere.radius;
                result       = new ColliderCastResult
                {
                    hitpoint                 = hitpoint,
                    normalOnCaster           = -normal,
                    normalOnTarget           = normal,
                    subColliderIndexOnCaster = 0,
                    subColliderIndexOnTarget = 0,
                    distance                 = fraction * math.length(displacement),
                };
                return true;
            }
            result = default;
            return false;
        }

        public static UnitySim.ContactsBetweenResult UnityContactsBetween(in SphereCollider sphereA,
                                                                          in RigidTransform aTransform,
                                                                          in SphereCollider sphereB,
                                                                          in RigidTransform bTransform,
                                                                          in ColliderDistanceResult distanceResult)
        {
            return ContactManifoldHelpers.GetSingleContactManifold(in distanceResult);
        }

        public static int LatiosContactsBetween(Span<LatiosSim.Contact>   contacts,
                                                float3 contactNormal,
                                                in SphereCollider sphereA,
                                                in RigidTransform aTransform,
                                                in SphereCollider sphereB,
                                                in RigidTransform bTransform,
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
            var   castDistance = math.abs(distanceResult.distance) + sphereA.radius + sphereB.radius;
            var   castStart    = bTransform;
            float castOffset   = 0f;
            if (distanceResult.distance <= 0f)
            {
                castOffset     = (-distanceResult.distance / dot) * (1f + math.EPSILON) + math.EPSILON;
                castStart.pos += contactNormal * castOffset;
            }
            if (ColliderCast(in sphereB, in castStart, castStart.pos - castDistance * contactNormal, in sphereA, in aTransform, out var castResult))
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

        internal static bool SphereSphereDistance(in SphereCollider sphereA,
                                                  in SphereCollider sphereB,
                                                  float maxDistance,
                                                  out ColliderDistanceResultInternal result,
                                                  out bool degenerate)
        {
            float3 delta                    = sphereB.center - sphereA.center;
            float  distanceSqBetweenCenters = math.lengthsq(delta);  //point center distance
            float  distanceBetweenCenters   = math.sqrt(distanceSqBetweenCenters);
            degenerate                      = distanceSqBetweenCenters < PointRaySphere.kUnmeasurableLengthSqThreshold;
            float3 normalA                  = math.select(delta / distanceBetweenCenters, new float3(0, 1, 0), degenerate);  // choose an arbitrary normal when the distance is zero
            result                          = new ColliderDistanceResultInternal
            {
                hitpointA = sphereA.center + normalA * sphereA.radius,
                hitpointB = sphereB.center - normalA * sphereB.radius,
                normalA   = normalA,
                normalB   = -normalA,
                distance  = distanceBetweenCenters - sphereA.radius - sphereB.radius
            };
            return result.distance <= maxDistance;
        }
    }
}

