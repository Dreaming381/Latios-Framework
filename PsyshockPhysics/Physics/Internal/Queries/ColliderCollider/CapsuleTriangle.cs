using Unity.Burst;
using Unity.Burst.CompilerServices;
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

        public static UnitySim.ContactsBetweenResult UnityContactsBetween(in TriangleCollider triangle,
                                                                          in RigidTransform triangleTransform,
                                                                          in CapsuleCollider capsule,
                                                                          in RigidTransform capsuleTransform,
                                                                          in ColliderDistanceResult distanceResult)
        {
            UnitySim.ContactsBetweenResult result = default;
            result.contactNormal                  = distanceResult.normalB;

            var triangleLocalContactNormal = math.InverseRotateFast(triangleTransform.rot, -distanceResult.normalB);
            PointRayTriangle.BestFacePlanesAndVertices(in triangle, triangleLocalContactNormal, out var edgePlaneNormals, out var edgePlaneDistances, out var plane, out _);

            bool needsClosestPoint = math.abs(math.dot(triangleLocalContactNormal, plane.normal)) < 0.05f;

            if (!needsClosestPoint)
            {
                var bInATransform     = math.mul(math.inverse(triangleTransform), capsuleTransform);
                var rayStart          = math.transform(bInATransform, capsule.pointA);
                var rayDisplacement   = math.transform(bInATransform, capsule.pointB) - rayStart;
                var rayRelativeStarts = simd.dot(rayStart, edgePlaneNormals) - edgePlaneDistances;
                var relativeDiffs     = simd.dot(rayDisplacement, edgePlaneNormals);
                var rayRelativeEnds   = rayRelativeStarts + relativeDiffs;
                var rayFractions      = math.select(-rayRelativeStarts / relativeDiffs, float4.zero, relativeDiffs == float4.zero);
                var startsInside      = rayRelativeStarts <= 0f;
                var endsInside        = rayRelativeEnds <= 0f;
                var projectsOnFace    = startsInside | endsInside;
                var enterFractions    = math.select(float4.zero, rayFractions, !startsInside & rayFractions > float4.zero);
                var exitFractions     = math.select(1f, rayFractions, !endsInside & rayFractions < 1f);
                var fractionA         = math.cmax(enterFractions);
                var fractionB         = math.cmin(exitFractions);
                needsClosestPoint     = true;
                if (math.all(projectsOnFace) && fractionA < fractionB)
                {
                    // Add the two contacts from the possibly clipped segment
                    var distanceScalarAlongContactNormal = math.rcp(math.dot(triangleLocalContactNormal, plane.normal));
                    var clippedSegmentA                  = rayStart + fractionA * rayDisplacement;
                    var clippedSegmentB                  = rayStart + fractionB * rayDisplacement;
                    var aDistance                        = mathex.SignedDistance(plane, clippedSegmentA) * distanceScalarAlongContactNormal;
                    var bDistance                        = mathex.SignedDistance(plane, clippedSegmentB) * distanceScalarAlongContactNormal;
                    result.Add(math.transform(triangleTransform, clippedSegmentA), aDistance - capsule.radius);
                    result.Add(math.transform(triangleTransform, clippedSegmentB), bDistance - capsule.radius);
                    needsClosestPoint = math.min(aDistance, bDistance) > distanceResult.distance + 1e-4f;  // Magic constant comes from Unity Physics
                }
            }

            if (needsClosestPoint)
                result.Add(distanceResult.hitpointB, distanceResult.distance);
            return result;
        }

        internal static bool TriangleCapsuleDistance(in TriangleCollider triangle, in CapsuleCollider capsule, float maxDistance, out ColliderDistanceResultInternal result)
        {
            // The strategy for this is different from Unity Physics, but is inspired by the capsule-capsule algorithm
            // and this blog: https://wickedengine.net/2020/04/26/capsule-collision-detection/
            // The idea is to reorder the checks so that the axis intersection branch culls some more math.
            simdFloat3 triPoints = new simdFloat3(triangle.pointA, triangle.pointB, triangle.pointC, triangle.pointA);
            simdFloat3 triEdges  = triPoints.bcaa - triPoints;

            float3 capEdge = capsule.pointB - capsule.pointA;
            CapsuleCapsule.SegmentSegment(in triPoints, in triEdges, new simdFloat3(capsule.pointA), new simdFloat3(capEdge), out var closestTriEdges, out var closestCapsuleAxis);
            float3 segSegDists      = simd.distancesq(closestTriEdges, closestCapsuleAxis).xyz;
            bool   bIsBetter        = segSegDists.y < segSegDists.x;
            float3 closestEdgePoint = math.select(closestTriEdges.a, closestTriEdges.b, bIsBetter);
            float3 closestAxisPoint = math.select(closestCapsuleAxis.a, closestCapsuleAxis.b, bIsBetter);
            bool   cIsBetter        = segSegDists.z < math.cmin(segSegDists.xy);
            closestEdgePoint        = math.select(closestEdgePoint, closestTriEdges.c, cIsBetter);
            closestAxisPoint        = math.select(closestAxisPoint, closestCapsuleAxis.c, cIsBetter);

            if (PointRayTriangle.RaycastTriangle(new Ray(capsule.pointA, capsule.pointB), in triPoints, out float fraction, out _) && fraction != 1f)
            {
                float3 triNormal         = math.normalizesafe(math.cross(triEdges.a, triEdges.b), math.normalizesafe(capEdge, 0f));
                float  minFractionOffset = math.min(fraction, 1f - fraction);
                // This is the length of the segment projected onto the triangle normal.
                float dot = math.dot(triNormal, capEdge);
                // This is how much we have to move the segment along the triangle normal to achieve separation.
                float offsetDistance = minFractionOffset * math.abs(dot);

                if (offsetDistance * offsetDistance < math.distancesq(closestEdgePoint, closestAxisPoint))
                {
                    bool useCapB = 1f - fraction < fraction;
                    triNormal    = math.select(triNormal, -triNormal, (dot < 0f) ^ useCapB);
                    result       = new ColliderDistanceResultInternal
                    {
                        distance     = -offsetDistance * capsule.radius,
                        hitpointA    = math.lerp(capsule.pointA, capsule.pointB, fraction),
                        hitpointB    = math.select(capsule.pointA, capsule.pointB, useCapB) - capsule.radius * triNormal,
                        normalA      = triNormal,
                        normalB      = -triNormal,
                        featureCodeA = 0x8000,
                        featureCodeB = 0x4000
                    };

                    return true;
                }
                else
                {
                    var bestEdge   = math.select(math.select(triEdges.a, triEdges.b, bIsBetter), triEdges.c, cIsBetter);
                    var edgeNormal = math.cross(math.normalizesafe(math.cross(triEdges.a, triEdges.c)), bestEdge);
                    if (Hint.Unlikely(edgeNormal.Equals(float3.zero)))
                    {
                        // The ray hit a degenerate triangle.
                        // Find the longest edge and do capsule vs capsule
                        var lengthSqs        = simd.lengthsq(triEdges);
                        var longestEdgeIndex = math.tzcnt(math.bitmask(new bool4(lengthSqs.xyz == math.cmax(lengthSqs.xyz), false)));
                        var edgeCap          = new CapsuleCollider
                        {
                            pointA = triPoints[longestEdgeIndex],
                            pointB = triPoints[(longestEdgeIndex + 1) % 3],
                            radius = 0f
                        };
                        CapsuleCapsule.CapsuleCapsuleDistance(in edgeCap, in capsule, float.MaxValue, out result);
                        result.featureCodeA  = (ushort)(result.featureCodeA + longestEdgeIndex);
                        result.featureCodeA -= (ushort)math.select(0, 3, (result.featureCodeA & 0xff) > 2);
                        return true;
                    }

                    // Check for the closest point on the triangle being a vertex
                    var closestIsVertex = (closestEdgePoint == triPoints).xyz;
                    if (Hint.Unlikely(math.any(closestIsVertex)))
                    {
                        // The capsule segment must have crossed right through the triangle vertex of a non-degenerate triangle
                        var    allEdgeNormals = simd.cross(math.cross(triEdges.a, triEdges.c), triEdges);
                        uint   index;
                        float3 bestNormal;
                        if (closestIsVertex.x)
                        {
                            index      = 0;
                            bestNormal = math.normalize(allEdgeNormals.a + allEdgeNormals.c);
                        }
                        else if (closestIsVertex.y)
                        {
                            index      = 1;
                            bestNormal = math.normalize(allEdgeNormals.a + allEdgeNormals.b);
                        }
                        else
                        {
                            index      = 2;
                            bestNormal = math.normalize(allEdgeNormals.b + allEdgeNormals.c);
                        }

                        // We know neither the capsule nor the edge is degenerate, so find the normal that points towards the triangle
                        var capNormal = math.normalize(math.cross(capEdge, math.cross(capEdge, bestNormal)));
                        // We also know that the closest point on the capsule is not an endpoint since the ray hit.
                        result = new ColliderDistanceResultInternal
                        {
                            distance     = -math.distance(closestAxisPoint, closestEdgePoint) - capsule.radius,
                            hitpointA    = closestEdgePoint,
                            hitpointB    = closestAxisPoint + capNormal * capsule.radius,
                            normalA      = bestNormal,
                            normalB      = capNormal,
                            featureCodeA = (ushort)index,
                            featureCodeB = 0x4000
                        };
                        return true;
                    }

                    {
                        // We know neither the capsule nor the edge is degenerate, so find the normal that points towards the triangle
                        var capNormal = math.normalize(math.cross(capEdge, math.cross(capEdge, edgeNormal)));
                        // We also know that the closest point on the capsule is not an endpoint since the ray hit.
                        // And we know that the closest point on the triangle is not a vertex as we already checked that.
                        result = new ColliderDistanceResultInternal
                        {
                            distance     = -math.distance(closestAxisPoint, closestEdgePoint) - capsule.radius,
                            hitpointA    = closestEdgePoint,
                            hitpointB    = closestAxisPoint + capNormal * capsule.radius,
                            normalA      = edgeNormal,
                            normalB      = capNormal,
                            featureCodeA = (ushort)(0x4000 + math.select(math.select(0, 1, bIsBetter), 2, cIsBetter)),
                            featureCodeB = 0x4000
                        };
                        return true;
                    }
                }
            }
            else
            {
                // Because the ray didn't hit, we should hopefully not have to worry about degenerate distance checks for the capsule segment
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
                {
                    result              = axisResult;
                    result.featureCodeB = 0x4000;
                }
                bool capDegenerate = capsule.pointA.Equals(capsule.pointB);
                if (hitA && aResult.distance < result.distance)
                {
                    result = aResult;
                    if (!capDegenerate && math.dot(result.normalB, capEdge) > 0f)
                        result.normalB = math.normalize(math.cross(math.cross(capEdge, result.normalB), capEdge));
                }
                if (hitB && !capDegenerate && bResult.distance < result.distance)
                {
                    result              = bResult;
                    result.featureCodeB = 1;
                }
                return true;
            }
        }
    }
}

