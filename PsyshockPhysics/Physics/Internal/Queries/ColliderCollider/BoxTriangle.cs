using Latios.Calci;
using Unity.Burst;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class BoxTriangle
    {
        public static bool DistanceBetween(in TriangleCollider triangle,
                                           in RigidTransform triangleTransform,
                                           in BoxCollider box,
                                           in RigidTransform boxTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            // Do a quick SAT test against the box faces to rule out misses.
            var aInBTransform          = math.mul(math.inverse(boxTransform), triangleTransform);
            var triangleInBoxSpaceAabb = Physics.AabbFrom(triangle, aInBTransform);
            var localBoxAabb           = new Aabb(box.center - (box.halfSize + maxDistance), box.center + (box.halfSize + maxDistance));
            if (math.any(triangleInBoxSpaceAabb.max < localBoxAabb.min) || math.any(localBoxAabb.max < triangleInBoxSpaceAabb.min))
            {
                result = default;
                return false;
            }

            // Todo: SAT algorithm similar to box vs box.
            var bInATransform = math.mul(math.inverse(triangleTransform), boxTransform);
            var gjkResult     = GjkEpa.DoGjkEpa(triangle, box, in bInATransform);
            var featureCodeA  = PointRayTriangle.FeatureCodeFromGjk(gjkResult.simplexAVertexCount, gjkResult.simplexAVertexA, gjkResult.simplexAVertexB);
            var featureCodeB  = PointRayBox.FeatureCodeFromGjk(gjkResult.simplexBVertexCount, gjkResult.simplexBVertexA, gjkResult.simplexBVertexB, gjkResult.simplexBVertexC);
            result            = InternalQueryTypeUtilities.BinAResultToWorld(new ColliderDistanceResultInternal
            {
                distance     = gjkResult.distance,
                hitpointA    = gjkResult.hitpointOnAInASpace,
                hitpointB    = gjkResult.hitpointOnBInASpace,
                normalA      = PointRayTriangle.TriangleNormalFromFeatureCode(featureCodeA, in triangle, -gjkResult.normalizedOriginToClosestCsoPoint),
                normalB      = math.rotate(bInATransform.rot, PointRayBox.BoxNormalFromFeatureCode(featureCodeB)),
                featureCodeA = featureCodeA,
                featureCodeB = featureCodeB
            }, triangleTransform);
            var  featureTypeA = featureCodeA >> 14;
            var  featureTypeB = featureCodeB >> 14;
            bool refinedHit   = true;
            switch ((featureTypeA, featureTypeB))
            {
                case (2, 1):
                {
                    // A box edge was reported closest to the triangle face. Construct a capsule from the box edge and test.
                    // Use the new results for all triangle data as well as the box hitpoint. Then only correct the
                    // box data if the new capsule hitpoint was an endpoint.
                    PointRayBox.EdgeEndpointsFromEdgeFeatureCode(in box, featureCodeB, out var boxEdgeA, out var boxEdgeB);
                    var edgeCapsule = new CapsuleCollider(boxEdgeA, boxEdgeB, 0f);
                    refinedHit      = CapsuleTriangle.DistanceBetween(in triangle, in triangleTransform, in edgeCapsule, in boxTransform, maxDistance, out var edgeResult);
                    if (edgeResult.distance > 0f && result.distance < 0f)
                        edgeResult.distance = -edgeResult.distance;
                    if (edgeResult.featureCodeA == 0x8000 && math.dot(edgeResult.normalA, result.normalA) < 0.5f)
                        edgeResult.normalA = -edgeResult.normalA;
                    result.distance        = edgeResult.distance;
                    result.hitpointA       = edgeResult.hitpointA;
                    result.normalA         = edgeResult.normalA;
                    result.featureCodeA    = edgeResult.featureCodeA;
                    result.hitpointB       = edgeResult.hitpointB;
                    if (edgeResult.featureCodeB == 0)
                    {
                        result.featureCodeB = (ushort)math.bitmask(new bool4(boxEdgeA >= 0f, false));
                        result.normalB      = math.rotate(boxTransform.rot, PointRayBox.BoxNormalFromFeatureCode(result.featureCodeB));
                    }
                    else if (edgeResult.featureCodeB == 1)
                    {
                        result.featureCodeB = (ushort)math.bitmask(new bool4(boxEdgeB >= 0f, false));
                        result.normalB      = math.rotate(boxTransform.rot, PointRayBox.BoxNormalFromFeatureCode(result.featureCodeB));
                    }
                    break;
                }
                case (1, 2):
                {
                    // A triangle edge was reported closest to a box face. Same concept as above, except the box and triangle roles are flipped.
                    PointRayTriangle.EdgeEndpointsFromEdgeFeatureCode(in triangle, featureCodeA, out var triEdgeA, out var triEdgeB);
                    var edgeCapsule = new CapsuleCollider(triEdgeA, triEdgeB, 0f);
                    refinedHit      = CapsuleBox.DistanceBetween(in box, in boxTransform, in edgeCapsule, in triangleTransform, maxDistance, out var edgeResult);
                    if (edgeResult.distance > 0f && result.distance < 0f)
                        edgeResult.distance = -edgeResult.distance;
                    result.distance         = edgeResult.distance;
                    result.hitpointB        = edgeResult.hitpointA;
                    result.normalB          = edgeResult.normalA;
                    result.featureCodeB     = edgeResult.featureCodeA;
                    result.hitpointA        = edgeResult.hitpointB;
                    var edgeFeatureType     = edgeResult.featureCodeB >> 14;
                    if (edgeFeatureType < 2)
                    {
                        float3 ab          = triangle.pointB - triangle.pointA;
                        float3 bc          = triangle.pointC - triangle.pointB;
                        float3 ca          = triangle.pointA - triangle.pointC;
                        float3 planeNormal = math.normalizesafe(math.cross(ab, ca));
                        float3 abUnnormal  = math.cross(ab, planeNormal);
                        float3 bcUnnormal  = math.cross(bc, planeNormal);
                        float3 caUnnormal  = math.cross(ca, planeNormal);
                        var    vertexIndex = (edgeFeatureType + (featureCodeA & 0x3)) % 3;
                        switch (vertexIndex)
                        {
                            case 0:
                                result.featureCodeA = 0;
                                result.normalA      = math.rotate(triangleTransform.rot, math.normalize(-math.normalize(abUnnormal) - math.normalize(caUnnormal)));
                                break;
                            case 1:
                                result.featureCodeA = 1;
                                result.normalA      = math.rotate(triangleTransform.rot, math.normalize(-math.normalize(abUnnormal) - math.normalize(bcUnnormal)));
                                break;
                            case 2:
                                result.featureCodeA = 2;
                                result.normalA      = math.rotate(triangleTransform.rot, math.normalize(-math.normalize(caUnnormal) - math.normalize(bcUnnormal)));
                                break;
                        }
                    }
                    break;
                }
            }
            GjkEpa.ValidateGjkEpa(triangle, in triangleTransform, box, in boxTransform, in result, result.distance <= maxDistance);
            return refinedHit && result.distance <= maxDistance;
        }

        public static bool ColliderCast(in BoxCollider boxToCast,
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
            bool hit                          = Mpr.MprCastNoRoundness(boxToCast,
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
            DistanceBetween(in targetTriangle, in targetTriangleTransform, in boxToCast, in casterHitTransform, float.MaxValue, out var distanceResult);

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

        public static bool ColliderCast(in TriangleCollider triangleToCast,
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
            bool hit                          = Mpr.MprCastNoRoundness(triangleToCast,
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
            DistanceBetween(in triangleToCast, in casterHitTransform, in targetBox, in targetBoxTransform, float.MaxValue, out var distanceResult);

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

        public static UnitySim.ContactsBetweenResult UnityContactsBetween(in TriangleCollider triangle,
                                                                          in RigidTransform triangleTransform,
                                                                          in BoxCollider box,
                                                                          in RigidTransform boxTransform,
                                                                          in ColliderDistanceResult distanceResult)
        {
            UnitySim.ContactsBetweenResult result = default;

            var aInBTransform = math.mul(math.inverse(boxTransform), triangleTransform);
            var triangleInB   =
                new TriangleCollider(math.transform(aInBTransform, triangle.pointA), math.transform(aInBTransform, triangle.pointB),
                                     math.transform(aInBTransform, triangle.pointC));

            // Unity Physics prefers to use the SAT axes for the contact normal if it can.
            // We attempt to recover the best SAT axis here.
            var featureTypes  = (distanceResult.featureCodeA >> 12) & 0x0c;
            featureTypes     |= distanceResult.featureCodeB >> 14;
            float3 aContactNormalInBSpace;
            bool   usesContactDir = false;
            switch (featureTypes)
            {
                case 0:  // A point and B point
                case 1:  // A point and B edge
                case 4:  // A edge and B point
                {
                    // In the case of a point and an edge, I believe only one of three things will happen:
                    // 1) The SAT axis is between two edges, and the planes won't project onto each other at all.
                    // 2) The SAT axis will be a face axis, in which the closest point will be clipped off.
                    // 3) The SAT axis is identical to the direction between the closest points.
                    //
                    // The first two options in Unity Physics will trigger the FaceFace() method to fail.
                    // The failure will result in Unity completely nuking its in-progress manifold and using
                    // the general-purpose algorithm that relies on GJK, which uses a contact normal based on
                    // the axis between the closest points. The last option is effectively the same result.
                    aContactNormalInBSpace =
                        math.normalizesafe((distanceResult.hitpointB - distanceResult.hitpointA) * math.select(1f, -1f, distanceResult.distance < 0f), float3.zero);
                    // Yes, normalizesafe has produced "valid" normals on floating point errors before. The result can get pretty noisy, so the tolerance is high.
                    aContactNormalInBSpace = math.select(aContactNormalInBSpace, float3.zero, math.abs(distanceResult.distance) < 1e-3f);
                    if (aContactNormalInBSpace.Equals(float3.zero))
                    {
                        aContactNormalInBSpace = math.normalize(distanceResult.normalA - distanceResult.normalB);
                    }
                    aContactNormalInBSpace = math.InverseRotateFast(boxTransform.rot, aContactNormalInBSpace);
                    usesContactDir         = true;
                    break;
                }
                case 5:  // A edge and B edge
                {
                    var edgeDirectionIndexA = (distanceResult.featureCodeA) & 0xff;
                    var edgeDirectionA      = edgeDirectionIndexA switch
                    {
                        0 => math.normalize(triangleInB.pointB - triangleInB.pointA),
                        1 => math.normalize(triangleInB.pointC - triangleInB.pointB),
                        2 => math.normalize(triangleInB.pointA - triangleInB.pointC),
                        _ => default
                    };
                    var edgeDirectionIndexB = (distanceResult.featureCodeB >> 2) & 0xff;
                    var edgeDirectionB      = edgeDirectionIndexB switch
                    {
                        0 => new float3(1f, 0f, 0f),
                        1 => new float3(0f, 1f, 0f),
                        2 => new float3(0f, 0f, 1f),
                        _ => default
                    };
                    aContactNormalInBSpace = math.normalize(math.cross(edgeDirectionA, edgeDirectionB));
                    aContactNormalInBSpace = math.select(aContactNormalInBSpace,
                                                         -aContactNormalInBSpace,
                                                         math.dot(math.rotate(boxTransform.rot, aContactNormalInBSpace), distanceResult.normalB) > 0f);
                    break;
                }
                case 2:  // A point and B face
                case 6:  // A edge and B face
                case 10:  // A face and B face
                {
                    // For A edge and face, this can only happen due to some bizarre precision issues.
                    // But we'll handle it anyways by just using the face normal of B.
                    var faceIndex          = distanceResult.featureCodeB & 0xff;
                    aContactNormalInBSpace = faceIndex switch
                    {
                        0 => new float3(1f, 0f, 0f),
                        1 => new float3(0f, 1f, 0f),
                        2 => new float3(0f, 0f, 1f),
                        3 => new float3(-1f, 0f, 0f),
                        4 => new float3(0f, -1f, 0f),
                        5 => new float3(0f, 0f, -1f),
                        _ => default
                    };
                    aContactNormalInBSpace = -aContactNormalInBSpace;
                    break;
                }
                case 8:  // A face and B point
                case 9:  // A face and B edge
                {
                    // For B edge, this can only happen due to some bizarre precision issues.
                    // But we'll handle it anyways by just using the face normal of A.
                    aContactNormalInBSpace = math.normalize(math.cross(triangleInB.pointB - triangleInB.pointA, triangleInB.pointC - triangleInB.pointA));
                    aContactNormalInBSpace = math.select(aContactNormalInBSpace,
                                                         -aContactNormalInBSpace,
                                                         math.dot(math.rotate(boxTransform.rot, aContactNormalInBSpace), distanceResult.normalA) < 0f);
                    break;
                }
                default:
                    aContactNormalInBSpace = default;
                    break;
            }

            for (int iteration = math.select(0, 1, usesContactDir); iteration < 2; iteration++)
            {
                result.contactNormal = math.rotate(boxTransform, -aContactNormalInBSpace);

                var bLocalContactNormal = -aContactNormalInBSpace;
                PointRayTriangle.BestFacePlanesAndVertices(in triangleInB,
                                                           aContactNormalInBSpace,
                                                           out var aEdgePlaneNormals,
                                                           out var aEdgePlaneDistances,
                                                           out var aPlane,
                                                           out var aVertices);
                PointRayBox.BestFacePlanesAndVertices(in box, bLocalContactNormal, out var bEdgePlaneNormals, out var bEdgePlaneDistances, out var bPlane, out var bVertices);
                bool needsClosestPoint                 = true;
                var  distanceScalarAlongContactNormalB = math.rcp(math.dot(-aContactNormalInBSpace, bPlane.normal));

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
                        result.Add(math.transform(boxTransform, clippedSegmentA + aContactNormalInBSpace * aDistance), aDistance);
                        needsClosestPoint &= aDistance > distanceResult.distance + 1e-4f;
                        if (fractionB < 1f)  // Avoid duplication when vertex is not clipped
                        {
                            var clippedSegmentB = rayStart + fractionB * rayDisplacement;
                            var bDistance       = mathex.SignedDistance(bPlane, clippedSegmentB) * distanceScalarAlongContactNormalB;
                            result.Add(math.transform(boxTransform, clippedSegmentB + aContactNormalInBSpace * bDistance), bDistance);
                            needsClosestPoint &= bDistance > distanceResult.distance + 1e-4f;
                        }
                    }
                    aVertices = aVertices.bcaa;
                }

                // Project vertices of B onto the face of A
                if (math.abs(math.dot(aPlane.normal, aContactNormalInBSpace)) > 0.05f)
                {
                    var distanceScalarAlongContactNormalA = math.rcp(math.dot(aContactNormalInBSpace, aPlane.normal));
                    for (int i = 0; i < 4; i++)
                    {
                        var vertex = bVertices[i];
                        if (math.all(simd.dot(aEdgePlaneNormals, vertex) < aEdgePlaneDistances))
                        {
                            var distance = mathex.SignedDistance(aPlane, vertex) * distanceScalarAlongContactNormalA;
                            result.Add(math.transform(boxTransform, vertex), distance);
                            needsClosestPoint &= distance > distanceResult.distance + 1e-4f;
                        }
                    }
                }

                if (!needsClosestPoint)
                {
                    return result;
                }
                else if (iteration == 0)
                {
                    // We missed using the SAT axis. Unity falls back to a more generalized algorithm,
                    // but it effectively simplifies to the same loop except using a contact normal based
                    // on the axis of the closest points.
                    aContactNormalInBSpace =
                        math.normalizesafe((distanceResult.hitpointB - distanceResult.hitpointA) * math.select(1f, -1f, distanceResult.distance < 0f), float3.zero);
                    if (aContactNormalInBSpace.Equals(float3.zero))
                    {
                        aContactNormalInBSpace = math.normalize(distanceResult.normalA - distanceResult.normalB);
                    }
                    aContactNormalInBSpace = math.select(aContactNormalInBSpace, -aContactNormalInBSpace, math.dot(aContactNormalInBSpace, distanceResult.normalB) > 0f);
                    aContactNormalInBSpace = math.InverseRotateFast(boxTransform.rot, aContactNormalInBSpace);
                    result                 = default;
                }
            }

            result.Add(distanceResult.hitpointB, distanceResult.distance);
            return result;
        }
    }
}

