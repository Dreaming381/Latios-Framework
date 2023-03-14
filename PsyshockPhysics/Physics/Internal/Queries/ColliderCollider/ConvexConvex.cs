using Unity.Burst;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class ConvexConvex
    {
        public static bool DistanceBetween(in ConvexCollider convexA,
                                           in RigidTransform aTransform,
                                           in ConvexCollider convexB,
                                           in RigidTransform bTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            var bInATransform = math.mul(math.inverse(aTransform), bTransform);
            var gjkResult     = GjkEpa.DoGjkEpa(convexA, convexB, in bInATransform);
            var epsilon       = gjkResult.normalizedOriginToClosestCsoPoint * math.select(1e-4f, -1e-4f, gjkResult.distance < 0f);
            PointRayConvex.DistanceBetween(gjkResult.hitpointOnAInASpace + epsilon, in convexA, in RigidTransform.identity, float.MaxValue, out var closestOnA);
            PointRayConvex.DistanceBetween(gjkResult.hitpointOnBInASpace - epsilon, in convexB, in bInATransform,           float.MaxValue, out var closestOnB);
            result = InternalQueryTypeUtilities.BinAResultToWorld(new ColliderDistanceResultInternal
            {
                distance  = gjkResult.distance,
                hitpointA = gjkResult.hitpointOnAInASpace,
                hitpointB = gjkResult.hitpointOnBInASpace,
                normalA   = closestOnA.normal,
                normalB   = closestOnB.normal
            }, aTransform);
            return result.distance <= maxDistance;
        }

        public static bool ColliderCast(in ConvexCollider convexToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in ConvexCollider targetConvex,
                                        in RigidTransform targetConvexTransform,
                                        out ColliderCastResult result)
        {
            var  castStartInverse             = math.inverse(castStart);
            var  targetInCasterSpaceTransform = math.mul(castStartInverse, targetConvexTransform);
            var  castDirection                = math.rotate(castStartInverse, castEnd - castStart.pos);
            var  normalizedCastDirection      = math.normalize(castDirection);
            bool hit                          = Mpr.MprCastNoRoundness(convexToCast,
                                                                       targetConvex,
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
            DistanceBetween(in convexToCast, in casterHitTransform, in targetConvex, in targetConvexTransform, float.MaxValue, out var distanceResult);

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

