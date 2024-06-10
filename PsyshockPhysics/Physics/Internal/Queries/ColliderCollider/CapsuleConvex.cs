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
            var featureCodeA  = PointRayConvex.FeatureCodeFromGjk(gjkResult.simplexAVertexCount,
                                                                  gjkResult.simplexAVertexA,
                                                                  gjkResult.simplexAVertexB,
                                                                  gjkResult.simplexAVertexC,
                                                                  in convex);
            var featureCodeB = PointRayCapsule.FeatureCodeFromGjk(gjkResult.simplexBVertexCount, gjkResult.simplexBVertexA);
            result           = InternalQueryTypeUtilities.BinAResultToWorld(new ColliderDistanceResultInternal
            {
                distance     = gjkResult.distance,
                hitpointA    = gjkResult.hitpointOnAInASpace,
                hitpointB    = gjkResult.hitpointOnBInASpace,
                normalA      = PointRayConvex.ConvexNormalFromFeatureCode(featureCodeA, in convex, -gjkResult.normalizedOriginToClosestCsoPoint),
                normalB      = math.rotate(bInATransform.rot, PointRayCapsule.CapsuleNormalFromFeatureCode(featureCodeB, in capsule, gjkResult.normalizedOriginToClosestCsoPoint)),
                featureCodeA = featureCodeA,
                featureCodeB = featureCodeB
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
                float4 obbD             = new float4(planeA.distanceToOrigin, planeB.distanceToOrigin, planeC.distanceToOrigin, planeD.distanceToOrigin);

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
                float4 obbD             = new float4(planeA.distanceToOrigin, planeB.distanceToOrigin, planeC.distanceToOrigin, planeD.distanceToOrigin);

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

        public static UnitySim.ContactsBetweenResult UnityContactsBetween(in ConvexCollider convex,
                                                                          in RigidTransform convexTransform,
                                                                          in CapsuleCollider capsule,
                                                                          in RigidTransform capsuleTransform,
                                                                          in ColliderDistanceResult distanceResult)
        {
            UnitySim.ContactsBetweenResult result = default;
            result.contactNormal                  = distanceResult.normalB;

            ref var blob       = ref convex.convexColliderBlob.Value;
            float3  invScale   = math.rcp(convex.scale);
            var     bitmask    = math.bitmask(new bool4(math.isfinite(invScale), false));
            var     dimensions = math.countbits(bitmask);
            if (dimensions == 3)
            {
                var convexLocalContactNormal = math.InverseRotateFast(convexTransform.rot, -distanceResult.normalB);
                PointRayConvex.BestFacePlane(ref convex.convexColliderBlob.Value,
                                             convexLocalContactNormal * invScale,
                                             distanceResult.featureCodeA,
                                             out var plane,
                                             out int faceIndex,
                                             out int edgeCount);

                bool needsClosestPoint = math.abs(math.dot(convexLocalContactNormal, plane.normal)) < 0.05f;

                if (!needsClosestPoint)
                {
                    var bInATransform   = math.mul(math.inverse(convexTransform), capsuleTransform);
                    var rayStart        = math.transform(bInATransform, capsule.pointA);
                    var rayDisplacement = math.transform(bInATransform, capsule.pointB) - rayStart;

                    var    edgeIndicesBase = blob.edgeIndicesInFacesStartsAndCounts[faceIndex].start;
                    float4 enterFractions  = float4.zero;
                    float4 exitFractions   = 1f;
                    bool4  projectsOnFace  = true;
                    for (int i = 0; i < edgeCount; i += 4)
                    {
                        int4 indices       = i + new int4(0, 1, 2, 3);
                        indices            = math.select(indices, indices - edgeCount, indices >= edgeCount);
                        indices           += edgeIndicesBase;
                        var segmentA       = blob.vertexIndicesInEdges[blob.edgeIndicesInFaces[indices.x].index];
                        var segmentB       = blob.vertexIndicesInEdges[blob.edgeIndicesInFaces[indices.y].index];
                        var segmentC       = blob.vertexIndicesInEdges[blob.edgeIndicesInFaces[indices.z].index];
                        var segmentD       = blob.vertexIndicesInEdges[blob.edgeIndicesInFaces[indices.w].index];
                        var edgeVerticesA  = new simdFloat3(new float4(blob.verticesX[segmentA.x], blob.verticesX[segmentB.x], blob.verticesX[segmentC.x],
                                                                       blob.verticesX[segmentD.x]),
                                                            new float4(blob.verticesY[segmentA.x], blob.verticesY[segmentB.x], blob.verticesY[segmentC.x],
                                                                       blob.verticesY[segmentD.x]),
                                                            new float4(blob.verticesZ[segmentA.x], blob.verticesZ[segmentB.x], blob.verticesZ[segmentC.x],
                                                                       blob.verticesZ[segmentD.x]));
                        var edgeVerticesB = new simdFloat3(new float4(blob.verticesX[segmentA.y], blob.verticesX[segmentB.y], blob.verticesX[segmentC.y],
                                                                      blob.verticesX[segmentD.y]),
                                                           new float4(blob.verticesY[segmentA.y], blob.verticesY[segmentB.y], blob.verticesY[segmentC.y],
                                                                      blob.verticesY[segmentD.y]),
                                                           new float4(blob.verticesZ[segmentA.y], blob.verticesZ[segmentB.y], blob.verticesZ[segmentC.y],
                                                                      blob.verticesZ[segmentD.y]));
                        edgeVerticesA *= convex.scale;
                        edgeVerticesB *= convex.scale;

                        // The average of all 8 vertices is the average of all the edge midpoints, which should be a point inside the face
                        // to help get the correct outward edge planes.
                        var midpoint           = simd.csumabcd(edgeVerticesA + edgeVerticesB) / 8f;
                        var edgeDisplacements  = edgeVerticesB - edgeVerticesA;
                        var edgePlaneNormals   = simd.cross(edgeDisplacements, convexLocalContactNormal);
                        edgePlaneNormals       = simd.select(-edgePlaneNormals, edgePlaneNormals, simd.dot(edgePlaneNormals, midpoint - edgeVerticesA) > 0f);
                        var edgePlaneDistances = simd.dot(edgePlaneNormals, edgeVerticesB);

                        var rayRelativeStarts  = simd.dot(rayStart, edgePlaneNormals) - edgePlaneDistances;
                        var relativeDiffs      = simd.dot(rayDisplacement, edgePlaneNormals);
                        var rayRelativeEnds    = rayRelativeStarts + relativeDiffs;
                        var rayFractions       = math.select(-rayRelativeStarts / relativeDiffs, float4.zero, relativeDiffs == float4.zero);
                        var startsInside       = rayRelativeStarts <= 0f;
                        var endsInside         = rayRelativeEnds <= 0f;
                        projectsOnFace        &= startsInside | endsInside;
                        enterFractions         = math.select(float4.zero, rayFractions, !startsInside & rayFractions > enterFractions);
                        exitFractions          = math.select(1f, rayFractions, !endsInside & rayFractions < exitFractions);
                    }

                    var fractionA     = math.cmax(enterFractions);
                    var fractionB     = math.cmin(exitFractions);
                    needsClosestPoint = true;
                    if (math.all(projectsOnFace) && fractionA < fractionB)
                    {
                        // Add the two contacts from the possibly clipped segment
                        var distanceScalarAlongContactNormal = math.rcp(math.dot(convexLocalContactNormal, plane.normal));
                        var clippedSegmentA                  = rayStart + fractionA * rayDisplacement;
                        var clippedSegmentB                  = rayStart + fractionB * rayDisplacement;
                        var aDistance                        = mathex.SignedDistance(plane, clippedSegmentA) * distanceScalarAlongContactNormal;
                        var bDistance                        = mathex.SignedDistance(plane, clippedSegmentB) * distanceScalarAlongContactNormal;
                        result.Add(math.transform(convexTransform, clippedSegmentA), aDistance - capsule.radius);
                        result.Add(math.transform(convexTransform, clippedSegmentB), bDistance - capsule.radius);
                        needsClosestPoint = math.min(aDistance, bDistance) > distanceResult.distance + 1e-4f;  // Magic constant comes from Unity Physics
                    }
                }

                if (needsClosestPoint)
                    result.Add(distanceResult.hitpointB, distanceResult.distance);
                return result;
            }
            else if (dimensions == 0)
            {
                result.Add(distanceResult.hitpointB, distanceResult.distance);
                return result;
            }
            else if (dimensions == 1)
            {
                var convexCapsule = new CapsuleCollider(blob.localAabb.min * convex.scale, blob.localAabb.max * convex.scale, 0f);
                return CapsuleCapsule.UnityContactsBetween(in convexCapsule, in convexTransform, in capsule, in capsuleTransform, in distanceResult);
            }
            else  //if (dimensions == 2)
            {
                var convexLocalContactNormal = math.InverseRotateFast(convexTransform.rot, -distanceResult.normalB);

                ref var indices2D = ref blob.yz2DVertexIndices;  // bitmask = 6
                var     plane     = new Plane(new float3(1f, 0f, 0f), 0f);
                if (bitmask == 5)
                {
                    indices2D = ref blob.xz2DVertexIndices;
                    plane     = new Plane(new float3(0f, 1f, 0f), 0f);
                }
                else if (bitmask == 3)
                {
                    indices2D = ref blob.xy2DVertexIndices;
                    plane     = new Plane(new float3(0f, 0f, 1f), 0f);
                }
                if (math.dot(plane.normal, convexLocalContactNormal) < 0f)
                    plane = mathex.Flip(plane);

                bool needsClosestPoint = math.abs(math.dot(convexLocalContactNormal, plane.normal)) < 0.05f;

                if (!needsClosestPoint)
                {
                    var bInATransform   = math.mul(math.inverse(convexTransform), capsuleTransform);
                    var rayStart        = math.transform(bInATransform, capsule.pointA);
                    var rayDisplacement = math.transform(bInATransform, capsule.pointB) - rayStart;

                    float4 enterFractions = float4.zero;
                    float4 exitFractions  = 1f;
                    bool4  projectsOnFace = true;
                    for (int i = 0; i < indices2D.Length; i += 4)
                    {
                        int4 indices             = i + new int4(0, 1, 2, 3);
                        indices                  = math.select(indices, indices - indices2D.Length, indices >= indices2D.Length);
                        var        extraIndex    = math.select(i + 4, i + 4 - indices2D.Length, i + 4 >= indices2D.Length);
                        simdFloat3 edgeVerticesA = default;
                        simdFloat3 edgeVerticesB = default;
                        if (bitmask == 6)
                        {
                            edgeVerticesA = new simdFloat3(float4.zero,
                                                           new float4(blob.verticesY[indices.x], blob.verticesY[indices.y], blob.verticesY[indices.z],
                                                                      blob.verticesY[indices.w]) * convex.scale.y,
                                                           new float4(blob.verticesZ[indices.x], blob.verticesZ[indices.y], blob.verticesZ[indices.z],
                                                                      blob.verticesZ[indices.w]) * convex.scale.z);
                            edgeVerticesB = new simdFloat3(float4.zero,
                                                           new float4(blob.verticesY[indices.y], blob.verticesY[indices.z], blob.verticesY[indices.w],
                                                                      blob.verticesY[extraIndex]) * convex.scale.y,
                                                           new float4(blob.verticesZ[indices.y], blob.verticesZ[indices.z], blob.verticesZ[indices.w],
                                                                      blob.verticesZ[extraIndex]) * convex.scale.z);
                        }
                        else if (bitmask == 5)
                        {
                            edgeVerticesA = new simdFloat3(new float4(blob.verticesX[indices.x], blob.verticesX[indices.y], blob.verticesX[indices.z],
                                                                      blob.verticesX[indices.w]) * convex.scale.x,
                                                           float4.zero,
                                                           new float4(blob.verticesZ[indices.x], blob.verticesZ[indices.y], blob.verticesZ[indices.z],
                                                                      blob.verticesZ[indices.w]) * convex.scale.z);
                            edgeVerticesB = new simdFloat3(new float4(blob.verticesX[indices.y], blob.verticesX[indices.z], blob.verticesX[indices.w],
                                                                      blob.verticesX[extraIndex]) * convex.scale.x,
                                                           float4.zero,
                                                           new float4(blob.verticesZ[indices.y], blob.verticesZ[indices.z], blob.verticesZ[indices.w],
                                                                      blob.verticesZ[extraIndex]) * convex.scale.z);
                        }
                        else  // bitmask == 3
                        {
                            edgeVerticesA = new simdFloat3(new float4(blob.verticesX[indices.x], blob.verticesX[indices.y], blob.verticesX[indices.z],
                                                                      blob.verticesX[indices.w]) * convex.scale.x,
                                                           new float4(blob.verticesY[indices.x], blob.verticesY[indices.y], blob.verticesY[indices.z],
                                                                      blob.verticesY[indices.w]) * convex.scale.y,
                                                           float4.zero);
                            edgeVerticesB = new simdFloat3(new float4(blob.verticesX[indices.y], blob.verticesX[indices.z], blob.verticesX[indices.w],
                                                                      blob.verticesX[extraIndex]) * convex.scale.x,
                                                           new float4(blob.verticesY[indices.y], blob.verticesY[indices.z], blob.verticesY[indices.w],
                                                                      blob.verticesY[extraIndex]) * convex.scale.y,
                                                           float4.zero);
                        }

                        // The average of all 8 vertices is the average of all the edge midpoints, which should be a point inside the face
                        // to help get the correct outward edge planes.
                        var midpoint           = simd.csumabcd(edgeVerticesA + edgeVerticesB) / 8f;
                        var edgeDisplacements  = edgeVerticesB - edgeVerticesA;
                        var edgePlaneNormals   = simd.cross(edgeDisplacements, convexLocalContactNormal);
                        edgePlaneNormals       = simd.select(-edgePlaneNormals, edgePlaneNormals, simd.dot(edgePlaneNormals, midpoint - edgeVerticesA) > 0f);
                        var edgePlaneDistances = simd.dot(edgePlaneNormals, edgeVerticesB);

                        var rayRelativeStarts  = simd.dot(rayStart, edgePlaneNormals) - edgePlaneDistances;
                        var relativeDiffs      = simd.dot(rayDisplacement, edgePlaneNormals);
                        var rayRelativeEnds    = rayRelativeStarts + relativeDiffs;
                        var rayFractions       = math.select(-rayRelativeStarts / relativeDiffs, float4.zero, relativeDiffs == float4.zero);
                        var startsInside       = rayRelativeStarts <= 0f;
                        var endsInside         = rayRelativeEnds <= 0f;
                        projectsOnFace        &= startsInside | endsInside;
                        enterFractions         = math.select(float4.zero, rayFractions, !startsInside & rayFractions > enterFractions);
                        exitFractions          = math.select(1f, rayFractions, !endsInside & rayFractions < exitFractions);
                    }

                    var fractionA     = math.cmax(enterFractions);
                    var fractionB     = math.cmin(exitFractions);
                    needsClosestPoint = true;
                    if (math.all(projectsOnFace) && fractionA < fractionB)
                    {
                        // Add the two contacts from the possibly clipped segment
                        var distanceScalarAlongContactNormal = math.rcp(math.dot(convexLocalContactNormal, plane.normal));
                        var clippedSegmentA                  = rayStart + fractionA * rayDisplacement;
                        var clippedSegmentB                  = rayStart + fractionB * rayDisplacement;
                        var aDistance                        = mathex.SignedDistance(plane, clippedSegmentA) * distanceScalarAlongContactNormal;
                        var bDistance                        = mathex.SignedDistance(plane, clippedSegmentB) * distanceScalarAlongContactNormal;
                        result.Add(math.transform(convexTransform, clippedSegmentA), aDistance - capsule.radius);
                        result.Add(math.transform(convexTransform, clippedSegmentB), bDistance - capsule.radius);
                        needsClosestPoint = math.min(aDistance, bDistance) > distanceResult.distance + 1e-4f;  // Magic constant comes from Unity Physics
                    }
                }

                if (needsClosestPoint)
                    result.Add(distanceResult.hitpointB, distanceResult.distance);
                return result;
            }
        }
    }
}

