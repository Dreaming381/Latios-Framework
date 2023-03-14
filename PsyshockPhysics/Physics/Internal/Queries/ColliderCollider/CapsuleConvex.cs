using Unity.Burst;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class CapsuleConvex
    {
        // Todo: Does it make more sense to have capsule be first or convex?
        public static bool DistanceBetween(in ConvexCollider convex,
                                           in RigidTransform convexTransform,
                                           in CapsuleCollider capsule,
                                           in RigidTransform capsuleTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            var bInATransform = math.mul(math.inverse(convexTransform), capsuleTransform);
            var gjkResult     = GjkEpa.DoGjkEpa(convex, capsule, in bInATransform);
            var epsilon       = gjkResult.normalizedOriginToClosestCsoPoint * math.select(1e-4f, -1e-4f, gjkResult.distance < 0f);
            PointRayConvex.DistanceBetween(gjkResult.hitpointOnAInASpace + epsilon, in convex, in RigidTransform.identity, float.MaxValue, out var closestOnA);
            PointRayCapsule.DistanceBetween(gjkResult.hitpointOnBInASpace - epsilon, in capsule, in bInATransform, float.MaxValue, out var closestOnB);
            result = InternalQueryTypeUtilities.BinAResultToWorld(new ColliderDistanceResultInternal
            {
                distance  = gjkResult.distance,
                hitpointA = gjkResult.hitpointOnAInASpace,
                hitpointB = gjkResult.hitpointOnBInASpace,
                normalA   = closestOnA.normal,
                normalB   = closestOnB.normal
            }, convexTransform);
            return result.distance <= maxDistance;
        }

        public static bool ColliderCast(in CapsuleCollider capsuleToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in ConvexCollider targetConvex,
                                        in RigidTransform targetConvexTransform,
                                        out ColliderCastResult result)
        {
            if (DistanceBetween(in targetConvex, in targetConvexTransform, in capsuleToCast, in castStart, 0f, out _))
            {
                result = default;
                return false;
            }

            var targetConvexTransformInverse = math.inverse(targetConvexTransform);
            var casterInTargetSpace          = math.mul(targetConvexTransformInverse, castStart);

            var  startA = math.transform(casterInTargetSpace, capsuleToCast.pointA);
            var  rayA   = new Ray(startA, startA + math.rotate(targetConvexTransformInverse, castEnd - castStart.pos));
            bool hitA   = PointRayConvex.RaycastRoundedConvex(in rayA, in targetConvex, capsuleToCast.radius, out var fractionA);
            fractionA   = math.select(2f, fractionA, hitA);
            var  startB = math.transform(casterInTargetSpace, capsuleToCast.pointB);
            var  rayB   = new Ray(startB, startB + math.rotate(targetConvexTransformInverse, castEnd - castStart.pos));
            bool hitB   = PointRayConvex.RaycastRoundedConvex(in rayB, in targetConvex, capsuleToCast.radius, out var fractionB);
            fractionB   = math.select(2f, fractionB, hitB);

            var        ray           = new Ray(0f, math.rotate(targetConvexTransformInverse, castStart.pos - castEnd));
            simdFloat3 startSimd     = new simdFloat3(startA, startA, startB, startB);
            int        bestEdgeIndex = -1;
            float      bestFraction  = 2f;
            ref var    blob          = ref targetConvex.convexColliderBlob.Value;

            var capEdge         = startB - startA;
            var capEdgeCrossRay = math.normalizesafe(math.cross(capEdge, ray.displacement), float3.zero);
            if (capEdgeCrossRay.Equals(float3.zero))
            {
                // The capsule aligns with the ray. We already have this case tested.
            }
            else
            {
                // We need four culling planes around the capsule
                var    correctedCapEdge = math.normalize(math.cross(ray.displacement, capEdgeCrossRay));
                var    ta               = math.select(startB, startA, math.dot(capEdgeCrossRay, startA) >= math.dot(capEdgeCrossRay, startB));
                var    tb               = math.select(startB, startA, math.dot(-capEdgeCrossRay, startA) >= math.dot(-capEdgeCrossRay, startB));
                var    tc               = math.select(startB, startA, math.dot(correctedCapEdge, startA) >= math.dot(correctedCapEdge, startB));
                var    td               = math.select(startB, startA, math.dot(-correctedCapEdge, startA) >= math.dot(-correctedCapEdge, startB));
                Plane  planeA           = new Plane(capEdgeCrossRay, -math.dot(capEdgeCrossRay, ta + capEdgeCrossRay * capsuleToCast.radius));
                Plane  planeB           = new Plane(-capEdgeCrossRay, -math.dot(-capEdgeCrossRay, tb - capEdgeCrossRay * capsuleToCast.radius));
                Plane  planeC           = new Plane(correctedCapEdge, -math.dot(correctedCapEdge, tc + correctedCapEdge * capsuleToCast.radius));
                Plane  planeD           = new Plane(-correctedCapEdge, -math.dot(-correctedCapEdge, td - correctedCapEdge * capsuleToCast.radius));
                float4 obbX             = new float4(planeA.normal.x, planeB.normal.x, planeC.normal.x, planeD.normal.x);
                float4 obbY             = new float4(planeA.normal.y, planeB.normal.y, planeC.normal.y, planeD.normal.y);
                float4 obbZ             = new float4(planeA.normal.z, planeB.normal.z, planeC.normal.z, planeD.normal.z);
                float4 obbD             = new float4(planeA.distanceFromOrigin, planeB.distanceFromOrigin, planeC.distanceFromOrigin, planeD.distanceFromOrigin);

                for (int i = 0; i < blob.vertexIndicesInEdges.Length; i++)
                {
                    var indices = blob.vertexIndicesInEdges[i];
                    var ax      = blob.verticesX[indices.x] * targetConvex.scale.x;
                    var ay      = blob.verticesY[indices.x] * targetConvex.scale.y;
                    var az      = blob.verticesZ[indices.x] * targetConvex.scale.z;
                    var bx      = blob.verticesX[indices.y] * targetConvex.scale.x;
                    var by      = blob.verticesY[indices.y] * targetConvex.scale.y;
                    var bz      = blob.verticesZ[indices.y] * targetConvex.scale.z;

                    bool4 isAOutside = obbX * ax + obbY * ay + obbZ * az + obbD > 0f;
                    bool4 isBOutside = obbX * bx + obbY * by + obbZ * bz + obbD > 0f;
                    if (math.any(isAOutside & isBOutside))
                        continue;

                    var edgePoints = new simdFloat3(new float4(ax, bx, bx, ax), new float4(ay, by, by, ay), new float4(az, bz, bz, az));
                    var cso        = startSimd - edgePoints;
                    if (PointRayTriangle.RaycastRoundedQuad(in ray, in cso, capsuleToCast.radius, out var edgeFraction, out _))
                    {
                        if (edgeFraction < bestFraction)
                        {
                            bestFraction  = edgeFraction;
                            bestEdgeIndex = i;
                        }
                    }
                }
            }

            bool  hit      = (bestEdgeIndex >= 0) | hitA | hitB;
            float fraction = math.min(math.min(fractionA, fractionB), bestFraction);
            if (hit)
            {
                var hitTransform = castStart;
                hitTransform.pos = math.lerp(castStart.pos, castEnd, fraction);
                DistanceBetween(in targetConvex, in targetConvexTransform, in capsuleToCast, in hitTransform, 1f, out var distanceResult);
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

        public static bool ColliderCast(in ConvexCollider convexToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in CapsuleCollider targetCapsule,
                                        in RigidTransform targetCapsuleTransform,
                                        out ColliderCastResult result)
        {
            if (DistanceBetween(in convexToCast, in castStart, in targetCapsule, in targetCapsuleTransform, 0f, out _))
            {
                result = default;
                return false;
            }

            var castStartInverse    = math.inverse(castStart);
            var targetInCasterSpace = math.mul(castStartInverse, targetCapsuleTransform);

            var  targetA = math.transform(targetInCasterSpace, targetCapsule.pointA);
            var  rayA    = new Ray(targetA, targetA - math.rotate(castStartInverse, castEnd - castStart.pos));
            bool hitA    = PointRayConvex.RaycastRoundedConvex(in rayA, in convexToCast, targetCapsule.radius, out var fractionA);
            fractionA    = math.select(2f, fractionA, hitA);
            var  targetB = math.transform(targetInCasterSpace, targetCapsule.pointB);
            var  rayB    = new Ray(targetB, targetB - math.rotate(castStartInverse, castEnd - castStart.pos));
            bool hitB    = PointRayConvex.RaycastRoundedConvex(in rayB, in convexToCast, targetCapsule.radius, out var fractionB);
            fractionB    = math.select(2f, fractionB, hitB);

            var        ray           = new Ray(0f, math.rotate(castStartInverse, castStart.pos - castEnd));
            simdFloat3 targetSimd    = new simdFloat3(targetA, targetB, targetB, targetA);
            int        bestEdgeIndex = -1;
            float      bestFraction  = 2f;
            ref var    blob          = ref convexToCast.convexColliderBlob.Value;

            var capEdge         = targetB - targetA;
            var capEdgeCrossRay = math.normalizesafe(math.cross(capEdge, ray.displacement), float3.zero);
            if (capEdgeCrossRay.Equals(float3.zero))
            {
                // The capsule aligns with the ray. We already have this case tested.
            }
            else
            {
                // We need four culling planes around the capsule
                var    correctedCapEdge = math.normalize(math.cross(ray.displacement, capEdgeCrossRay));
                var    ta               = math.select(targetB, targetA, math.dot(capEdgeCrossRay, targetA) >= math.dot(capEdgeCrossRay, targetB));
                var    tb               = math.select(targetB, targetA, math.dot(-capEdgeCrossRay, targetA) >= math.dot(-capEdgeCrossRay, targetB));
                var    tc               = math.select(targetB, targetA, math.dot(correctedCapEdge, targetA) >= math.dot(correctedCapEdge, targetB));
                var    td               = math.select(targetB, targetA, math.dot(-correctedCapEdge, targetA) >= math.dot(-correctedCapEdge, targetB));
                Plane  planeA           = new Plane(capEdgeCrossRay, -math.dot(capEdgeCrossRay, ta + capEdgeCrossRay * targetCapsule.radius));
                Plane  planeB           = new Plane(-capEdgeCrossRay, -math.dot(-capEdgeCrossRay, tb - capEdgeCrossRay * targetCapsule.radius));
                Plane  planeC           = new Plane(correctedCapEdge, -math.dot(correctedCapEdge, tc + correctedCapEdge * targetCapsule.radius));
                Plane  planeD           = new Plane(-correctedCapEdge, -math.dot(-correctedCapEdge, td - correctedCapEdge * targetCapsule.radius));
                float4 obbX             = new float4(planeA.normal.x, planeB.normal.x, planeC.normal.x, planeD.normal.x);
                float4 obbY             = new float4(planeA.normal.y, planeB.normal.y, planeC.normal.y, planeD.normal.y);
                float4 obbZ             = new float4(planeA.normal.z, planeB.normal.z, planeC.normal.z, planeD.normal.z);
                float4 obbD             = new float4(planeA.distanceFromOrigin, planeB.distanceFromOrigin, planeC.distanceFromOrigin, planeD.distanceFromOrigin);

                for (int i = 0; i < blob.vertexIndicesInEdges.Length; i++)
                {
                    var indices = blob.vertexIndicesInEdges[i];
                    var ax      = blob.verticesX[indices.x] * convexToCast.scale.x;
                    var ay      = blob.verticesY[indices.x] * convexToCast.scale.y;
                    var az      = blob.verticesZ[indices.x] * convexToCast.scale.z;
                    var bx      = blob.verticesX[indices.y] * convexToCast.scale.x;
                    var by      = blob.verticesY[indices.y] * convexToCast.scale.y;
                    var bz      = blob.verticesZ[indices.y] * convexToCast.scale.z;

                    bool4 isAOutside = obbX * ax + obbY * ay + obbZ * az + obbD > 0f;
                    bool4 isBOutside = obbX * bx + obbY * by + obbZ * bz + obbD > 0f;
                    if (math.any(isAOutside & isBOutside))
                        continue;

                    var edgePoints = new simdFloat3(new float4(ax, ax, bx, bx), new float4(ay, ay, by, by), new float4(az, az, bz, bz));
                    var cso        = edgePoints - targetSimd;
                    if (PointRayTriangle.RaycastRoundedQuad(in ray, in cso, targetCapsule.radius, out var edgeFraction, out _))
                    {
                        if (edgeFraction < bestFraction)
                        {
                            bestFraction  = edgeFraction;
                            bestEdgeIndex = i;
                        }
                    }
                }
            }

            bool  hit      = (bestEdgeIndex > 0) | hitA | hitB;
            float fraction = math.min(math.min(fractionA, fractionB), bestFraction);
            if (hit)
            {
                var hitTransform = castStart;
                hitTransform.pos = math.lerp(castStart.pos, castEnd, fraction);
                DistanceBetween(in convexToCast, in hitTransform, in targetCapsule, in targetCapsuleTransform, 1f, out var distanceResult);
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
    }
}

