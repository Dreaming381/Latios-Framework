using Unity.Burst;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class SphereSphere
    {
        public static bool DistanceBetween(in SphereCollider sphereA,
                                           in RigidTransform aTransform,
                                           in SphereCollider sphereB,
                                           in RigidTransform bTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            var            aWorldToLocal      = math.inverse(aTransform);
            var            bInASpaceTransform = math.mul(aWorldToLocal, bTransform);
            SphereCollider bInASpace          = new SphereCollider(math.transform(bInASpaceTransform, sphereB.center), sphereB.radius);
            bool           hit                = SphereSphereDistance(in sphereA, in bInASpace, maxDistance, out ColliderDistanceResultInternal localResult, out _);
            result                            = new ColliderDistanceResult
            {
                hitpointA         = math.transform(aTransform, localResult.hitpointA),
                hitpointB         = math.transform(aTransform, localResult.hitpointB),
                normalA           = math.rotate(aTransform, localResult.normalA),
                normalB           = math.rotate(aTransform, localResult.normalB),
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
            var cso     = targetSphere;
            cso.radius += sphereToCast.radius;
            var  start  = math.transform(castStart, sphereToCast.center);
            var  ray    = new Ray(start, start + castEnd - castStart.pos);
            bool hit    = PointRaySphere.Raycast(in ray, in cso, in targetSphereTransform, out var raycastResult);
            if (hit)
            {
                var hitTransform  = castStart;
                hitTransform.pos += raycastResult.position - start;
                DistanceBetween(in sphereToCast, in hitTransform, in targetSphere, in targetSphereTransform, 1f, out var distanceResult);
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

        public static UnitySim.ContactsBetweenResult UnityContactsBetween(in SphereCollider sphereA,
                                                                          in RigidTransform aTransform,
                                                                          in SphereCollider sphereB,
                                                                          in RigidTransform bTransform,
                                                                          in ColliderDistanceResult distanceResult)
        {
            return ContactManifoldHelpers.GetSingleContactManifold(in distanceResult);
        }

        internal static bool SphereSphereDistance(in SphereCollider sphereA,
                                                  in SphereCollider sphereB,
                                                  float maxDistance,
                                                  out ColliderDistanceResultInternal result,
                                                  out bool degenerate)
        {
            float3 delta          = sphereB.center - sphereA.center;
            float  ccDistanceSq   = math.lengthsq(delta);  //center center distance
            bool   distanceIsZero = ccDistanceSq == 0.0f;
            float  invCCDistance  = math.select(math.rsqrt(ccDistanceSq), 0.0f, distanceIsZero);
            float3 normalA        = math.select(delta * invCCDistance, new float3(0, 1, 0), distanceIsZero);  // choose an arbitrary normal when the distance is zero
            float  distance       = ccDistanceSq * invCCDistance - sphereA.radius - sphereB.radius;
            result                = new ColliderDistanceResultInternal
            {
                hitpointA = sphereA.center + normalA * sphereA.radius,
                hitpointB = sphereA.center + normalA * (sphereA.radius + distance),  //hitpoint A + A's normal * distance [expand distributive property]
                normalA   = normalA,
                normalB   = -normalA,
                distance  = distance,
            };
            degenerate = distanceIsZero;
            return distance <= maxDistance;
        }
    }
}

