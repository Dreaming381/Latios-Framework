using Unity.Burst;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class BoxConvex
    {
        public static bool DistanceBetween(in ConvexCollider convex,
                                           in RigidTransform convexTransform,
                                           in BoxCollider box,
                                           in RigidTransform boxTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            var bInATransform = math.mul(math.inverse(convexTransform), boxTransform);
            var gjkResult     = GjkEpa.DoGjkEpa(convex, box, in bInATransform);
            var epsilon       = gjkResult.normalizedOriginToClosestCsoPoint * math.select(1e-4f, -1e-4f, gjkResult.distance < 0f);
            SphereConvex.DistanceBetween(in convex,
                                         in RigidTransform.identity,
                                         new SphereCollider(gjkResult.hitpointOnAInASpace + epsilon, 0f),
                                         RigidTransform.identity,
                                         float.MaxValue,
                                         out var closestOnA);
            SphereBox.DistanceBetween(in box,
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
            }, convexTransform);
            return result.distance <= maxDistance;
        }

        public static bool ColliderCast(in BoxCollider boxToCast,
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
            bool hit                          = Mpr.MprCastNoRoundness(boxToCast,
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
            DistanceBetween(in targetConvex, in targetConvexTransform, in boxToCast, in casterHitTransform, float.MaxValue, out var distanceResult);

            result = new ColliderCastResult
            {
                distance                 = distanceOfImpact,
                hitpoint                 = distanceResult.hitpointB,
                normalOnCaster           = distanceResult.normalB,
                normalOnTarget           = distanceResult.normalA,
                subColliderIndexOnCaster = 0,
                subColliderIndexOnTarget = 0
            };

            return true;
        }

        public static bool ColliderCast(in ConvexCollider convexToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in BoxCollider targetBox,
                                        in RigidTransform targetBoxTransform,
                                        out ColliderCastResult result)
        {
            var  castStartInverse             = math.inverse(castStart);
            var  targetInCasterSpaceTransform = math.mul(castStartInverse, targetBoxTransform);
            var  castDirection                = math.rotate(castStartInverse, castEnd - castStart.pos);
            var  normalizedCastDirection      = math.normalize(castDirection);
            bool hit                          = Mpr.MprCastNoRoundness(convexToCast,
                                                                       targetBox,
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
            DistanceBetween(in convexToCast, in casterHitTransform, in targetBox, in targetBoxTransform, float.MaxValue, out var distanceResult);

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

