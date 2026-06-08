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
            return WithinDistance(in sphereA, in aTransform, in sphereB, in bTransform, 0f);
        }

        public static bool WithinDistance(in SphereCollider sphereA,
                                          in RigidTransform aTransform,
                                          in SphereCollider sphereB,
                                          in RigidTransform bTransform,
                                          float maxDistance)
        {
            var bInASpaceTransform = math.InverseTransformFast(aTransform, bTransform);
            return PointRaySphere.WithinDistance(sphereA.center, in sphereB, in bInASpaceTransform, maxDistance + sphereA.radius);
        }

        public static bool DistanceBetween(in SphereCollider sphereA,
                                           in RigidTransform aTransform,
                                           in SphereCollider sphereB,
                                           in RigidTransform bTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            var            bInASpaceTransform = math.InverseTransformFast(aTransform, bTransform);
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
            var hit = PointRaySphere.PointSphereDistance(sphereA.center, in sphereB, maxDistance + sphereA.radius, out var pointResult, out degenerate);
            result  = new ColliderDistanceResultInternal
            {
                hitpointA = sphereA.center - pointResult.normal * sphereA.radius,
                hitpointB = pointResult.hitpoint,
                normalA   = -pointResult.normal,
                normalB   = pointResult.normal,
                distance  = pointResult.distance - sphereA.radius
            };
            return hit;
        }
    }
}

