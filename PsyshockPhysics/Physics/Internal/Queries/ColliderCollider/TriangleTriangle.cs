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
            var featureCodeA  = PointRayTriangle.FeatureCodeFromGjk(gjkResult.simplexAVertexCount, gjkResult.simplexAVertexA, gjkResult.simplexAVertexB);
            var featureCodeB  = PointRayTriangle.FeatureCodeFromGjk(gjkResult.simplexBVertexCount, gjkResult.simplexBVertexA, gjkResult.simplexBVertexB);
            result            = InternalQueryTypeUtilities.BinAResultToWorld(new ColliderDistanceResultInternal
            {
                distance  = gjkResult.distance,
                hitpointA = gjkResult.hitpointOnAInASpace,
                hitpointB = gjkResult.hitpointOnBInASpace,
                normalA   = PointRayTriangle.TriangleNormalFromFeatureCode(featureCodeA, in triangleA, -gjkResult.normalizedOriginToClosestCsoPoint),
                normalB   =
                    math.rotate(bInATransform.rot, PointRayTriangle.TriangleNormalFromFeatureCode(featureCodeB, in triangleB, gjkResult.normalizedOriginToClosestCsoPoint)),
                featureCodeA = featureCodeA,
                featureCodeB = featureCodeB
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

        public static UnitySim.ContactsBetweenResult UnityContactsBetween(in TriangleCollider triangleA,
                                                                          in RigidTransform aTransform,
                                                                          in TriangleCollider triangleB,
                                                                          in RigidTransform bTransform,
                                                                          in ColliderDistanceResult distanceResult)
        {
            UnitySim.ContactsBetweenResult result        = default;
            var                            bInATransform = math.mul(math.inverse(aTransform), bTransform);
            var                            triangleBinA  = new TriangleCollider(math.transform(bInATransform, triangleB.pointA), math.transform(bInATransform, triangleB.pointB),
                                                     math.transform(bInATransform, triangleB.pointC));

            // We attempt to identify point-face pairs for deriving the contact normal for improved robustness.
            var featureTypes  = (distanceResult.featureCodeA >> 12) & 0x0c;
            featureTypes     |= distanceResult.featureCodeB >> 14;
            float3 aLocalContactNormal;
            switch (featureTypes)
            {
                case 0:  // A point and B point
                case 1:  // A point and B edge
                case 4:  // A edge and B point
                case 5:  // A edge and B edge
                {
                    // Compute the contact normal based on contact points from GJK.
                    aLocalContactNormal =
                        math.normalizesafe((distanceResult.hitpointB - distanceResult.hitpointA) * math.select(1f, -1f, distanceResult.distance < 0f), float3.zero);
                    if (aLocalContactNormal.Equals(float3.zero))
                    {
                        aLocalContactNormal = math.normalize(distanceResult.normalA - distanceResult.normalB);
                    }
                    aLocalContactNormal = math.InverseRotateFast(aTransform.rot, aLocalContactNormal);
                    break;
                }
                case 2:  // A point and B face
                case 6:  // A edge and B face
                {
                    // For A edge, this can only happen due to some bizarre precision issues.
                    // But we'll handle it anyways by just using the face normal of B.
                    aLocalContactNormal = math.normalize(math.cross(triangleBinA.pointB - triangleBinA.pointA, triangleBinA.pointC - triangleBinA.pointA));
                    aLocalContactNormal = math.select(-aLocalContactNormal,
                                                      aLocalContactNormal,
                                                      math.dot(math.rotate(aTransform.rot, aLocalContactNormal), distanceResult.normalB) > 0f);
                    break;
                }
                case 8:  // A face and B point
                case 9:  // A face and B edge
                case 10:  // A face and B face
                {
                    // For B edge and face, this can only happen due to some bizarre precision issues.
                    // But we'll handle it anyways by just using the face normal of A.
                    aLocalContactNormal = math.normalize(math.cross(triangleA.pointB - triangleA.pointA, triangleA.pointC - triangleA.pointA));
                    aLocalContactNormal = math.select(aLocalContactNormal,
                                                      -aLocalContactNormal,
                                                      math.dot(math.rotate(aTransform.rot, aLocalContactNormal), distanceResult.normalA) < 0f);
                    break;
                }
                default:
                    aLocalContactNormal = default;
                    break;
            }

            {
                result.contactNormal = math.rotate(aTransform, -aLocalContactNormal);

                var bLocalContactNormal = math.InverseRotateFast(bInATransform.rot, -aLocalContactNormal);
                PointRayTriangle.BestFacePlanesAndVertices(in triangleA,
                                                           aLocalContactNormal,
                                                           out var aEdgePlaneNormals,
                                                           out var aEdgePlaneDistances,
                                                           out var aPlane,
                                                           out var aVertices);
                PointRayTriangle.BestFacePlanesAndVertices(in triangleBinA,
                                                           bLocalContactNormal,
                                                           out var bEdgePlaneNormals,
                                                           out var bEdgePlaneDistances,
                                                           out var bPlane,
                                                           out var bVertices);
                bool needsClosestPoint = true;

                if (math.abs(math.dot(bPlane.normal, aLocalContactNormal)) > 0.05f)
                {
                    var distanceScalarAlongContactNormalB = math.rcp(math.dot(-aLocalContactNormal, bPlane.normal));

                    // Project and clip edges of A onto the face of B.
                    for (int edgeIndex = 0; edgeIndex < 3; edgeIndex++)
                    {
                        var rayStart          = aVertices.a;
                        var rayDisplacement   = aVertices.b - rayStart;
                        var rayRelativeStarts = simd.dot(rayStart, bEdgePlaneNormals) - bEdgePlaneDistances;
                        var relativeDiffs     = simd.dot(rayDisplacement, bEdgePlaneNormals);
                        var rayRelativeEnds   = rayRelativeStarts + relativeDiffs;
                        var rayFractions      = math.select(-rayRelativeStarts / relativeDiffs, float4.zero, relativeDiffs == float4.zero);
                        var startsInside      = rayRelativeStarts <= 0f;
                        var endsInside        = rayRelativeEnds <= 0f;
                        var projectsOnFace    = startsInside | endsInside;
                        var enterFractions    = math.select(float4.zero, rayFractions, !startsInside & rayFractions > float4.zero);
                        var exitFractions     = math.select(1f, rayFractions, !endsInside & rayFractions < 1f);
                        var fractionA         = math.cmax(enterFractions);
                        var fractionB         = math.cmin(exitFractions);

                        if (math.all(projectsOnFace) && fractionA < fractionB)
                        {
                            // Add the two contacts from the possibly clipped segment
                            var clippedSegmentA = rayStart + fractionA * rayDisplacement;
                            var aDistance       = mathex.SignedDistance(bPlane, clippedSegmentA) * distanceScalarAlongContactNormalB;
                            result.Add(math.transform(aTransform, clippedSegmentA + aLocalContactNormal * aDistance), aDistance);
                            needsClosestPoint &= aDistance > distanceResult.distance + 1e-4f;
                            if (fractionB < 1f)  // Avoid duplication when vertex is not clipped
                            {
                                var clippedSegmentB = rayStart + fractionB * rayDisplacement;
                                var bDistance       = mathex.SignedDistance(bPlane, clippedSegmentB) * distanceScalarAlongContactNormalB;
                                result.Add(math.transform(aTransform, clippedSegmentB + aLocalContactNormal * bDistance), bDistance);
                                needsClosestPoint &= bDistance > distanceResult.distance + 1e-4f;
                            }
                        }
                        aVertices = aVertices.bcaa;
                    }

                    // Project vertices of B onto the face of A
                    var distanceScalarAlongContactNormalA = math.rcp(math.dot(aLocalContactNormal, aPlane.normal));
                    for (int i = 0; i < 3; i++)
                    {
                        var vertex = bVertices[i];
                        if (math.all(simd.dot(aEdgePlaneNormals, vertex) < aEdgePlaneDistances))
                        {
                            var distance = mathex.SignedDistance(aPlane, vertex) * distanceScalarAlongContactNormalA;
                            result.Add(math.transform(aTransform, vertex), distance);
                            needsClosestPoint &= distance > distanceResult.distance + 1e-4f;
                        }
                    }
                }
                else if (math.abs(math.dot(aPlane.normal, aLocalContactNormal)) > 0.05f)
                {
                    var distanceScalarAlongContactNormalA = math.rcp(math.dot(aLocalContactNormal, aPlane.normal));

                    // Project and clip edges of B onto the face of A.
                    for (int edgeIndex = 0; edgeIndex < 3; edgeIndex++)
                    {
                        var rayStart          = bVertices.a;
                        var rayDisplacement   = bVertices.b - rayStart;
                        var rayRelativeStarts = simd.dot(rayStart, aEdgePlaneNormals) - aEdgePlaneDistances;
                        var relativeDiffs     = simd.dot(rayDisplacement, aEdgePlaneNormals);
                        var rayRelativeEnds   = rayRelativeStarts + relativeDiffs;
                        var rayFractions      = math.select(-rayRelativeStarts / relativeDiffs, float4.zero, relativeDiffs == float4.zero);
                        var startsInside      = rayRelativeStarts <= 0f;
                        var endsInside        = rayRelativeEnds <= 0f;
                        var projectsOnFace    = startsInside | endsInside;
                        var enterFractions    = math.select(float4.zero, rayFractions, !startsInside & rayFractions > float4.zero);
                        var exitFractions     = math.select(1f, rayFractions, !endsInside & rayFractions < 1f);
                        var fractionA         = math.cmax(enterFractions);
                        var fractionB         = math.cmin(exitFractions);

                        if (math.all(projectsOnFace) && fractionA < fractionB)
                        {
                            // Add the two contacts from the possibly clipped segment
                            var clippedSegmentA = rayStart + fractionA * rayDisplacement;
                            var aDistance       = mathex.SignedDistance(aPlane, clippedSegmentA) * distanceScalarAlongContactNormalA;
                            result.Add(math.transform(aTransform, clippedSegmentA), aDistance);
                            needsClosestPoint &= aDistance > distanceResult.distance + 1e-4f;
                            if (fractionB < 1f)  // Avoid duplication when vertex is not clipped
                            {
                                var clippedSegmentB = rayStart + fractionB * rayDisplacement;
                                var bDistance       = mathex.SignedDistance(aPlane, clippedSegmentB) * distanceScalarAlongContactNormalA;
                                result.Add(math.transform(aTransform, clippedSegmentB), bDistance);
                                needsClosestPoint &= bDistance > distanceResult.distance + 1e-4f;
                            }
                        }
                        bVertices = bVertices.bcaa;
                    }
                }

                if (!needsClosestPoint)
                {
                    return result;
                }
            }

            result.Add(distanceResult.hitpointB, distanceResult.distance);
            return result;
        }
    }
}

