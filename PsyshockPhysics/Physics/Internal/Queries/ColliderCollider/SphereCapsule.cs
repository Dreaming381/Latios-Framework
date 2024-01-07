using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class SphereCapsule
    {
        // Capsule is first because it is cheaper to transform a sphere into A-space
        public static bool DistanceBetween(in CapsuleCollider capsule,
                                           in RigidTransform capsuleTransform,
                                           in SphereCollider sphere,
                                           in RigidTransform sphereTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            var            capWorldToLocal           = math.inverse(capsuleTransform);
            var            sphereInCapSpaceTransfrom = math.mul(capWorldToLocal, sphereTransform);
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

        private static bool CapsuleSphereDistance(in CapsuleCollider capsule, in SphereCollider sphere, float maxDistance, out ColliderDistanceResultInternal result)
        {
            //Strategy: Project p onto the capsule's line clamped to the segment. Then inflate point on line as sphere
            float3 edge                   = capsule.pointB - capsule.pointA;
            float3 ap                     = sphere.center - capsule.pointA;
            float  dot                    = math.dot(ap, edge);
            float  edgeLengthSq           = math.lengthsq(edge);
            dot                           = math.clamp(dot, 0f, edgeLengthSq);
            float3         pointOnSegment = capsule.pointA + edge * dot / edgeLengthSq;
            SphereCollider sphereA        = new SphereCollider(pointOnSegment, capsule.radius);
            var            hit            = SphereSphere.SphereSphereDistance(in sphereA, in sphere, maxDistance, out result, out bool degenerate);
            result.featureCodeA           = 0x4000;
            result.featureCodeA           = (ushort)math.select(result.featureCodeA, 0, dot == 0f);
            result.featureCodeA           = (ushort)math.select(result.featureCodeA, 1, dot == edgeLengthSq);
            if (Hint.Likely(!degenerate))
                return hit;

            if (math.all(edge == 0f))
                return hit;

            mathex.GetDualPerpendicularNormalized(edge, out var capsuleNormal, out _);
            result.normalA   = capsuleNormal;
            result.normalB   = -capsuleNormal;
            result.hitpointA = pointOnSegment - capsule.radius * capsuleNormal;
            result.hitpointB = pointOnSegment + sphere.radius * capsuleNormal;
            result.distance  = -capsule.radius - sphere.radius;
            return hit;
        }
    }
}

