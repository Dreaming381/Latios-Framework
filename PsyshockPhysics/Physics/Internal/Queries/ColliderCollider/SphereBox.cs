using Unity.Burst;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class SphereBox
    {
        // Box is first because it is cheaper to transform a sphere into A-space
        public static bool DistanceBetween(in BoxCollider box,
                                           in RigidTransform boxTransform,
                                           in SphereCollider sphere,
                                           in RigidTransform sphereTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            var            boxWorldToLocal           = math.inverse(boxTransform);
            var            sphereInBoxSpaceTransform = math.mul(boxWorldToLocal, sphereTransform);
            float3         sphereCenterInBoxSpace    = math.transform(sphereInBoxSpaceTransform, sphere.center);
            SphereCollider sphereInBoxSpace          = new SphereCollider(sphereCenterInBoxSpace, sphere.radius);
            bool           hit                       = BoxSphereDistance(in box,
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
            var  targetBoxTransformInverse = math.inverse(targetBoxTransform);
            var  casterInTargetSpace       = math.mul(targetBoxTransformInverse, castStart);
            var  start                     = math.transform(casterInTargetSpace, sphereToCast.center);
            var  ray                       = new Ray(start, start + math.rotate(targetBoxTransformInverse, castEnd - castStart.pos));
            bool hit                       = PointRayBox.RaycastRoundedBox(in ray, in targetBox, sphereToCast.radius, out var fraction, out var normal);
            if (hit)
            {
                var hitTransform = castStart;
                hitTransform.pos = math.lerp(castStart.pos, castEnd, fraction);
                DistanceBetween(in targetBox, in targetBoxTransform, in sphereToCast, in hitTransform, 1f, out var distanceResult);
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

        public static bool ColliderCast(in BoxCollider boxToCast,
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
            bool hit                = PointRayBox.RaycastRoundedBox(in ray, in boxToCast, targetSphere.radius, out var fraction, out _);
            if (hit)
            {
                var hitTransform = castStart;
                hitTransform.pos = math.lerp(castStart.pos, castEnd, fraction);
                DistanceBetween(in boxToCast, in hitTransform, in targetSphere, in targetSphereTransform, 1f, out var distanceResult);
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

        public static UnitySim.ContactsBetweenResult UnityContactsBetween(in BoxCollider box,
                                                                          in RigidTransform boxTransform,
                                                                          in SphereCollider sphere,
                                                                          in RigidTransform sphereTransform,
                                                                          in ColliderDistanceResult distanceResult)
        {
            return ContactManifoldHelpers.GetSingleContactManifold(in distanceResult);
        }

        private static bool BoxSphereDistance(in BoxCollider box, in SphereCollider sphere, float maxDistance, out ColliderDistanceResultInternal result)
        {
            bool   hit     = PointRayBox.PointBoxDistance(sphere.center, in box, maxDistance + sphere.radius, out PointDistanceResultInternal pointDistanceResult);
            float3 normalB = math.normalizesafe(pointDistanceResult.hitpoint - sphere.center, -pointDistanceResult.normal);
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
            return hit;
        }
    }
}

