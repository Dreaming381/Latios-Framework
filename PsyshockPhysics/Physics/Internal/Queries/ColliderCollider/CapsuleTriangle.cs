using Unity.Burst;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class CapsuleTriangle
    {
        // Triangle is first because it is cheaper to transform a capsule into A-space
        public static bool DistanceBetween(in TriangleCollider triangle,
                                           in RigidTransform triangleTransform,
                                           in CapsuleCollider capsule,
                                           in RigidTransform capsuleTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            var triangleWorldToLocal        = math.inverse(triangleTransform);
            var capInTriangleSpaceTransform = math.mul(triangleWorldToLocal, capsuleTransform);
            var capsuleInTriangleSpace      = new CapsuleCollider(math.transform(capInTriangleSpaceTransform, capsule.pointA),
                                                                  math.transform(capInTriangleSpaceTransform, capsule.pointB),
                                                                  capsule.radius);
            bool hit = TriangleCapsuleDistance(in triangle, in capsuleInTriangleSpace, maxDistance, out ColliderDistanceResultInternal localResult);
            result   = InternalQueryTypeUtilities.BinAResultToWorld(in localResult, in triangleTransform);

            return hit;
        }

        public static bool ColliderCast(in CapsuleCollider capsuleToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in TriangleCollider targetTriangle,
                                        in RigidTransform targetTriangleTransform,
                                        out ColliderCastResult result)
        {
            if (DistanceBetween(in targetTriangle, in targetTriangleTransform, in capsuleToCast, in castStart, 0f, out _))
            {
                result = default;
                return false;
            }

            var targetTriangleTransformInverse = math.inverse(targetTriangleTransform);
            var casterInTargetSpace            = math.mul(targetTriangleTransformInverse, castStart);
            var triPoints                      = targetTriangle.AsSimdFloat3();

            var  startA = math.transform(casterInTargetSpace, capsuleToCast.pointA);
            var  rayA   = new Ray(startA, startA + math.rotate(targetTriangleTransformInverse, castEnd - castStart.pos));
            bool hitA   = PointRayTriangle.RaycastRoundedTriangle(in rayA, in triPoints, capsuleToCast.radius, out var fractionA, out _);
            fractionA   = math.select(2f, fractionA, hitA);
            var  startB = math.transform(casterInTargetSpace, capsuleToCast.pointB);
            var  rayB   = new Ray(startB, startB + math.rotate(targetTriangleTransformInverse, castEnd - castStart.pos));
            bool hitB   = PointRayTriangle.RaycastRoundedTriangle(in rayB, in triPoints, capsuleToCast.radius, out var fractionB, out _);
            fractionB   = math.select(2f, fractionB, hitB);

            var        ray = new Ray(0f, math.rotate(targetTriangleTransformInverse, castStart.pos - castEnd));
            bool3      hitEdge;
            float3     fractionsEdge;
            simdFloat3 startSimd = new simdFloat3(startA, startA, startB, startB);
            simdFloat3 cso       = startSimd - triPoints.abba;
            hitEdge.x            = PointRayTriangle.RaycastRoundedQuad(in ray, in cso, capsuleToCast.radius, out fractionsEdge.x, out _);
            cso                  = startSimd - triPoints.bccb;
            hitEdge.y            = PointRayTriangle.RaycastRoundedQuad(in ray, in cso, capsuleToCast.radius, out fractionsEdge.y, out _);
            cso                  = startSimd - triPoints.caac;
            hitEdge.z            = PointRayTriangle.RaycastRoundedQuad(in ray, in cso, capsuleToCast.radius, out fractionsEdge.z, out _);
            fractionsEdge        = math.select(2f, fractionsEdge, hitEdge);

            bool  hit      = math.any(hitEdge) | hitA | hitB;
            float fraction = math.min(math.min(fractionA, fractionB), math.cmin(fractionsEdge));
            if (hit)
            {
                var hitTransform = castStart;
                hitTransform.pos = math.lerp(castStart.pos, castEnd, fraction);
                DistanceBetween(in targetTriangle, in targetTriangleTransform, in capsuleToCast, in hitTransform, 1f, out var distanceResult);
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
                                        in CapsuleCollider targetCapsule,
                                        in RigidTransform targetCapsuleTransform,
                                        out ColliderCastResult result)
        {
            if (DistanceBetween(in triangleToCast, in castStart, in targetCapsule, in targetCapsuleTransform, 0f, out _))
            {
                result = default;
                return false;
            }

            var castStartInverse    = math.inverse(castStart);
            var targetInCasterSpace = math.mul(castStartInverse, targetCapsuleTransform);
            var triPoints           = triangleToCast.AsSimdFloat3();

            var  targetA = math.transform(targetInCasterSpace, targetCapsule.pointA);
            var  rayA    = new Ray(targetA, targetA - math.rotate(castStartInverse, castEnd - castStart.pos));
            bool hitA    = PointRayTriangle.RaycastRoundedTriangle(in rayA, in triPoints, targetCapsule.radius, out var fractionA, out _);
            fractionA    = math.select(2f, fractionA, hitA);
            var  targetB = math.transform(targetInCasterSpace, targetCapsule.pointB);
            var  rayB    = new Ray(targetB, targetB - math.rotate(castStartInverse, castEnd - castStart.pos));
            bool hitB    = PointRayTriangle.RaycastRoundedTriangle(in rayB, in triPoints, targetCapsule.radius, out var fractionB, out _);
            fractionB    = math.select(2f, fractionB, hitB);

            var        ray = new Ray(0f, math.rotate(castStartInverse, castStart.pos - castEnd));
            bool3      hitEdge;
            float3     fractionsEdge;
            simdFloat3 targetSimd = new simdFloat3(targetA, targetB, targetA, targetB);
            simdFloat3 cso        = triPoints.aabb - targetSimd;
            hitEdge.x             = PointRayTriangle.RaycastRoundedQuad(in ray, in cso, targetCapsule.radius, out fractionsEdge.x, out _);
            cso                   = triPoints.bbcc - targetSimd;
            hitEdge.y             = PointRayTriangle.RaycastRoundedQuad(in ray, in cso, targetCapsule.radius, out fractionsEdge.y, out _);
            cso                   = triPoints.ccaa - targetSimd;
            hitEdge.z             = PointRayTriangle.RaycastRoundedQuad(in ray, in cso, targetCapsule.radius, out fractionsEdge.z, out _);
            fractionsEdge         = math.select(2f, fractionsEdge, hitEdge);

            bool  hit      = math.any(hitEdge) | hitA | hitB;
            float fraction = math.min(math.min(fractionA, fractionB), math.cmin(fractionsEdge));
            if (hit)
            {
                var hitTransform = castStart;
                hitTransform.pos = math.lerp(castStart.pos, castEnd, fraction);
                DistanceBetween(in triangleToCast, in hitTransform, in targetCapsule, in targetCapsuleTransform, 1f, out var distanceResult);
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

        internal static bool TriangleCapsuleDistance(in TriangleCollider triangle, in CapsuleCollider capsule, float maxDistance, out ColliderDistanceResultInternal result)
        {
            // The strategy for this is different from Unity Physics, but is inspired by the capsule-capsule algorithm
            // and this blog: https://wickedengine.net/2020/04/26/capsule-collision-detection/
            // The idea is to reorder the checks so that the axis intersection branch culls some more math.
            simdFloat3 triPoints = new simdFloat3(triangle.pointA, triangle.pointB, triangle.pointC, triangle.pointA);
            simdFloat3 triEdges  = triPoints.bcaa - triPoints;

            float3 capEdge = capsule.pointB - capsule.pointA;
            CapsuleCapsule.SegmentSegment(triPoints, triEdges, new simdFloat3(capsule.pointA), new simdFloat3(capEdge), out var closestTriEdges, out var closestCapsuleAxis);
            float3 segSegDists      = simd.distancesq(closestTriEdges, closestCapsuleAxis).xyz;
            bool   bIsBetter        = segSegDists.y < segSegDists.x;
            float3 closestEdgePoint = math.select(closestTriEdges.a, closestTriEdges.b, bIsBetter);
            float3 closestAxisPoint = math.select(closestCapsuleAxis.a, closestCapsuleAxis.b, bIsBetter);
            bool   cIsBetter        = segSegDists.z < math.cmin(segSegDists.xy);
            closestEdgePoint        = math.select(closestEdgePoint, closestTriEdges.c, cIsBetter);
            closestAxisPoint        = math.select(closestAxisPoint, closestCapsuleAxis.c, cIsBetter);

            if (PointRayTriangle.RaycastTriangle(new Ray(capsule.pointA, capsule.pointB), triPoints, out float fraction, out _))
            {
                float3 triNormal         = math.normalizesafe(math.cross(triEdges.a, triEdges.b), math.normalizesafe(capEdge, 0f));
                float  minFractionOffset = math.min(fraction, 1f - fraction);
                // This is how much we have to move the axis along the triangle through the axis to achieve separation.
                float dot = math.dot(triNormal, capEdge);

                float offsetDistance = minFractionOffset * math.abs(dot);

                if (offsetDistance * offsetDistance <= math.distancesq(closestEdgePoint, closestAxisPoint))
                {
                    bool useCapB                 = 1f - fraction < fraction;
                    triNormal                    = math.select(triNormal, -triNormal, (dot < 0f) ^ useCapB);
                    float3         capsuleOffset = triNormal * (offsetDistance + capsule.radius);
                    SphereCollider sphere        = new SphereCollider(math.select(capsule.pointA, capsule.pointB, useCapB) + capsuleOffset, capsule.radius);
                    SphereTriangle.TriangleSphereDistance(triangle, sphere, maxDistance, out result);
                    result.distance   = -offsetDistance;
                    result.hitpointB -= capsuleOffset;
                    return true;
                }
                else
                {
                    SphereCollider axisSphere = new SphereCollider(closestAxisPoint, capsule.radius);
                    SphereCollider edgeSphere = new SphereCollider(closestEdgePoint, 0f);
                    // This gives us the positive distance from the capsule to edge.
                    // The penetration point is the opposite side of the capsule.
                    SphereSphere.SphereSphereDistance(edgeSphere, axisSphere, float.MaxValue, out result);
                    result.distance   = -result.distance - capsule.radius;
                    result.normalB    = -result.normalB;
                    result.hitpointB += result.normalB * 2f * capsule.radius;
                    return true;
                }
            }
            else
            {
                SphereCollider axisSphere = new SphereCollider(closestAxisPoint, capsule.radius);
                bool           hitAxis    = SphereTriangle.TriangleSphereDistance(triangle, axisSphere, maxDistance, out var axisResult);
                SphereCollider aSphere    = new SphereCollider(capsule.pointA, capsule.radius);
                bool           hitA       = SphereTriangle.TriangleSphereDistance(triangle, aSphere, maxDistance, out var aResult);
                SphereCollider bSphere    = new SphereCollider(capsule.pointB, capsule.radius);
                bool           hitB       = SphereTriangle.TriangleSphereDistance(triangle, bSphere, maxDistance, out var bResult);
                if (!hitAxis && !hitA && !hitB)
                {
                    result = axisResult;
                    return false;
                }

                result          = default;
                result.distance = float.MaxValue;
                if (hitAxis)
                    result = axisResult;
                if (hitA && aResult.distance < result.distance)
                    result = aResult;
                if (hitB && bResult.distance < result.distance)
                    result = bResult;
                return true;
            }
        }
    }
}

