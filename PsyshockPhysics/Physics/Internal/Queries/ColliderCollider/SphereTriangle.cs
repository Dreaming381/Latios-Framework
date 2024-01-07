using Unity.Burst;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class SphereTriangle
    {
        // Triangle is first because it is cheaper to transform a sphere into A-space
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

