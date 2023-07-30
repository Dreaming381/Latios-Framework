using Unity.Burst;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class TriangleTriangle
    {
        public static bool DistanceBetween(in TriangleCollider triangleA,
                                           in RigidTransform aTransform,
                                           in TriangleCollider triangleB,
                                           in RigidTransform bTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            // Todo: SAT algorithm similar to box vs box.
            var bInATransform = math.mul(math.inverse(aTransform), bTransform);
            var gjkResult     = GjkEpa.DoGjkEpa(triangleA, triangleB, in bInATransform);
            var epsilon       = gjkResult.normalizedOriginToClosestCsoPoint * math.select(1e-4f, -1e-4f, gjkResult.distance < 0f);
            SphereTriangle.DistanceBetween(in triangleA,
                                           in RigidTransform.identity,
                                           new SphereCollider(gjkResult.hitpointOnAInASpace + epsilon, 0f),
                                           RigidTransform.identity,
                                           float.MaxValue,
                                           out var closestOnA);
            SphereTriangle.DistanceBetween(in triangleB,
                                           in bInATransform,
                                           new SphereCollider(gjkResult.hitpointOnBInASpace - epsilon, 0f),
                                           RigidTransform.identity,
                                           float.MaxValue,
                                           out var closestOnB);
            result = InternalQueryTypeUtilities.BinAResultToWorld(new ColliderDistanceResultInternal
            {
                distance     = gjkResult.distance,
                hitpointA    = gjkResult.hitpointOnAInASpace,
                hitpointB    = gjkResult.hitpointOnBInASpace,
                normalA      = closestOnA.normalA,
                normalB      = closestOnB.normalA,
                featureCodeA = closestOnA.featureCodeA,
                featureCodeB = closestOnB.featureCodeA
            }, aTransform);
            return result.distance <= maxDistance;
        }

        public static bool ColliderCast(in TriangleCollider triangleToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in TriangleCollider targetTriangle,
                                        in RigidTransform targetTriangleTransform,
                                        out ColliderCastResult result)
        {
            var  castStartInverse             = math.inverse(castStart);
            var  targetInCasterSpaceTransform = math.mul(castStartInverse, targetTriangleTransform);
            var  castDirection                = math.rotate(castStartInverse, castEnd - castStart.pos);
            var  normalizedCastDirection      = math.normalize(castDirection);
            bool hit                          = Mpr.MprCastNoRoundness(triangleToCast,
                                                                       targetTriangle,
                                                                       in targetInCasterSpaceTransform,
                                                                       normalizedCastDirection,
                                                                       math.length(castDirection),
                                                                       out float distanceOfImpact,
                                                                       out bool somethingWentWrong);
            InternalQueryTypeUtilities.CheckMprResolved(somethingWentWrong);
            if (!hit || distanceOfImpact <= 0f)
            {
                result = default;
                return false;
            }

            var castHitOffset       = math.rotate(castStart, normalizedCastDirection * distanceOfImpact);
            var casterHitTransform  = castStart;
            casterHitTransform.pos += castHitOffset;
            DistanceBetween(in triangleToCast, in casterHitTransform, in targetTriangle, in targetTriangleTransform, float.MaxValue, out var distanceResult);

            result = new ColliderCastResult
            {
                distance                 = distanceOfImpact,
                hitpoint                 = distanceResult.hitpointA,
                normalOnCaster           = distanceResult.normalA,
                normalOnTarget           = distanceResult.normalB,
                subColliderIndexOnCaster = 0,
                subColliderIndexOnTarget = 0
            };

            return true;
        }
    }
}

