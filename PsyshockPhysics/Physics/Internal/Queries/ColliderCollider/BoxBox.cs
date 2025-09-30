using Latios.Calci;
using Unity.Burst;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class BoxBox
    {
        public static bool DistanceBetween(in BoxCollider boxA,
                                           in RigidTransform aTransform,
                                           in BoxCollider boxB,
                                           in RigidTransform bTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            // Todo: SAT algorithm like it used to be, except better.
            //var bInATransform = math.mul(math.inverse(aTransform), bTransform);
            //var gjkResult     = GjkEpa.DoGjkEpa(boxA, boxB, in bInATransform);
            //var featureCodeA  = PointRayBox.FeatureCodeFromGjk(gjkResult.simplexAVertexCount, gjkResult.simplexAVertexA, gjkResult.simplexAVertexB, gjkResult.simplexAVertexC);
            //var featureCodeB  = PointRayBox.FeatureCodeFromGjk(gjkResult.simplexBVertexCount, gjkResult.simplexBVertexA, gjkResult.simplexBVertexB, gjkResult.simplexBVertexC);
            //result            = InternalQueryTypeUtilities.BinAResultToWorld(new ColliderDistanceResultInternal
            //{
            //    distance     = gjkResult.distance,
            //    hitpointA    = gjkResult.hitpointOnAInASpace,
            //    hitpointB    = gjkResult.hitpointOnBInASpace,
            //    normalA      = PointRayBox.BoxNormalFromFeatureCode(featureCodeA),
            //    normalB      = math.rotate(bInATransform.rot, PointRayBox.BoxNormalFromFeatureCode(featureCodeB)),
            //    featureCodeA = featureCodeA,
            //    featureCodeB = featureCodeB
            //}, aTransform);
            //return result.distance <= maxDistance;

            var aOffsetTransform  = aTransform;
            aOffsetTransform.pos += math.rotate(aTransform.rot, boxA.center);
            var bOffsetTransform  = bTransform;
            bOffsetTransform.pos += math.rotate(bTransform.rot, boxB.center);
            var bInATransform     = math.mul(math.inverse(aOffsetTransform), bOffsetTransform);
            var aInBTransform     = math.mul(math.inverse(bOffsetTransform), aOffsetTransform);

            var hit                = BoxBoxDistance(boxA.halfSize, boxB.halfSize, in bInATransform, in aInBTransform, maxDistance, out var localResult);
            localResult.hitpointA += boxA.center;
            localResult.hitpointB += boxA.center;
            result                 = InternalQueryTypeUtilities.BinAResultToWorld(in localResult, in aTransform);
            return hit;
        }

        public static bool ColliderCast(in BoxCollider boxToCast, in RigidTransform castStart, float3 castEnd, in BoxCollider targetBox, in RigidTransform targetBoxTransform,
                                        out ColliderCastResult result)
        {
            var  castStartInverse             = math.inverse(castStart);
            var  targetInCasterSpaceTransform = math.mul(castStartInverse, targetBoxTransform);
            var  castDirection                = math.rotate(castStartInverse, castEnd - castStart.pos);
            var  normalizedCastDirection      = math.normalize(castDirection);
            bool hit                          = Mpr.MprCastNoRoundness(boxToCast,
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
            DistanceBetween(in boxToCast, in casterHitTransform, in targetBox, in targetBoxTransform, float.MaxValue, out var distanceResult);

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

        public static UnitySim.ContactsBetweenResult UnityContactsBetween(in BoxCollider boxA,
                                                                          in RigidTransform aTransform,
                                                                          in BoxCollider boxB,
                                                                          in RigidTransform bTransform,
                                                                          in ColliderDistanceResult distanceResult)
        {
            UnitySim.ContactsBetweenResult result        = default;
            var                            bInATransform = math.mul(math.inverse(aTransform), bTransform);

            // Unity Physics prefers to use the SAT axes for the contact normal if it can.
            // We attempt to recover the best SAT axis here.
            var featureTypes  = (distanceResult.featureCodeA >> 12) & 0x0c;
            featureTypes     |= distanceResult.featureCodeB >> 14;
            float3 aLocalContactNormal;
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
                    aLocalContactNormal =
                        math.normalizesafe((distanceResult.hitpointB - distanceResult.hitpointA) * math.select(1f, -1f, distanceResult.distance < 0f), float3.zero);
                    // Yes, normalizesafe has produced "valid" normals on floating point errors before. The result can get pretty noisy, so the tolerance is high.
                    aLocalContactNormal = math.select(aLocalContactNormal, float3.zero, math.abs(distanceResult.distance) < 1e-3f);
                    if (aLocalContactNormal.Equals(float3.zero))
                    {
                        aLocalContactNormal = math.normalize(distanceResult.normalA - distanceResult.normalB);
                    }
                    aLocalContactNormal = math.InverseRotateFast(aTransform.rot, aLocalContactNormal);
                    usesContactDir      = true;
                    break;
                }
                case 5:  // A edge and B edge
                {
                    var edgeDirectionIndexA = (distanceResult.featureCodeA >> 2) & 0xff;
                    var edgeDirectionA      = edgeDirectionIndexA switch
                    {
                        0 => new float3(1f, 0f, 0f),
                        1 => new float3(0f, 1f, 0f),
                        2 => new float3(0f, 0f, 1f),
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
                    edgeDirectionB      = math.rotate(bInATransform.rot, edgeDirectionB);
                    aLocalContactNormal = math.normalize(math.cross(edgeDirectionA, edgeDirectionB));
                    aLocalContactNormal = math.select(aLocalContactNormal,
                                                      -aLocalContactNormal,
                                                      math.dot(math.rotate(aTransform.rot, aLocalContactNormal), distanceResult.normalA) < 0f);
                    break;
                }
                case 2:  // A point and B face
                case 6:  // A edge and B face
                {
                    // For A edge, this can only happen due to some bizarre precision issues.
                    // But we'll handle it anyways by just using the face normal of B.
                    var faceIndex       = distanceResult.featureCodeB & 0xff;
                    aLocalContactNormal = faceIndex switch
                    {
                        0 => new float3(1f, 0f, 0f),
                        1 => new float3(0f, 1f, 0f),
                        2 => new float3(0f, 0f, 1f),
                        3 => new float3(-1f, 0f, 0f),
                        4 => new float3(0f, -1f, 0f),
                        5 => new float3(0f, 0f, -1f),
                        _ => default
                    };
                    aLocalContactNormal = -math.rotate(bInATransform.rot, aLocalContactNormal);
                    break;
                }
                case 8:  // A face and B point
                case 9:  // A face and B edge
                case 10:  // A face and B face
                {
                    // For B edge and face, this can only happen due to some bizarre precision issues.
                    // But we'll handle it anyways by just using the face normal of A.
                    var faceIndex       = distanceResult.featureCodeA & 0xff;
                    aLocalContactNormal = faceIndex switch
                    {
                        0 => new float3(1f, 0f, 0f),
                        1 => new float3(0f, 1f, 0f),
                        2 => new float3(0f, 0f, 1f),
                        3 => new float3(-1f, 0f, 0f),
                        4 => new float3(0f, -1f, 0f),
                        5 => new float3(0f, 0f, -1f),
                        _ => default
                    };
                    break;
                }
                default:
                    aLocalContactNormal = default;
                    break;
            }

            for (int iteration = math.select(0, 1, usesContactDir); iteration < 2; iteration++)
            {
                result.contactNormal = math.rotate(aTransform, -aLocalContactNormal);

                var bLocalContactNormal = math.InverseRotateFast(bInATransform.rot, -aLocalContactNormal);
                PointRayBox.BestFacePlanesAndVertices(in boxA, aLocalContactNormal, out var aEdgePlaneNormals, out var aEdgePlaneDistances, out var aPlane, out var aVertices);
                PointRayBox.BestFacePlanesAndVertices(in boxB, bLocalContactNormal, out var bEdgePlaneNormals, out _,                       out var bPlane, out var bVertices);
                bPlane                                 = mathex.TransformPlane(bInATransform, bPlane);
                bVertices                              = simd.transform(bInATransform, bVertices);
                bEdgePlaneNormals                      = simd.mul(bInATransform.rot, bEdgePlaneNormals);
                var  bEdgePlaneDistances               = simd.dot(bEdgePlaneNormals, bVertices.bcda);
                bool needsClosestPoint                 = true;
                var  distanceScalarAlongContactNormalB = math.rcp(math.dot(-aLocalContactNormal, bPlane.normal));

                // Project and clip edges of A onto the face of B.
                for (int edgeIndex = 0; edgeIndex < 4; edgeIndex++)
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
                            var bContact        = math.transform(aTransform, clippedSegmentB + aLocalContactNormal * bDistance);
                            result.Add(bContact, bDistance);
                            needsClosestPoint &= bDistance > distanceResult.distance + 1e-4f;
                        }
                    }
                    aVertices = aVertices.bcda;
                }

                // Project vertices of B onto the face of A
                var distanceScalarAlongContactNormalA = math.rcp(math.dot(aLocalContactNormal, aPlane.normal));
                for (int i = 0; i < 4; i++)
                {
                    var vertex = bVertices[i];
                    if (math.all(simd.dot(aEdgePlaneNormals, vertex) < aEdgePlaneDistances))
                    {
                        var distance = mathex.SignedDistance(aPlane, vertex) * distanceScalarAlongContactNormalA;
                        var bContact = math.transform(aTransform, vertex);
                        result.Add(bContact, distance);
                        needsClosestPoint &= distance > distanceResult.distance + 1e-4f;
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
                    aLocalContactNormal =
                        math.normalizesafe((distanceResult.hitpointB - distanceResult.hitpointA) * math.select(1f, -1f, distanceResult.distance < 0f), float3.zero);
                    if (aLocalContactNormal.Equals(float3.zero))
                    {
                        aLocalContactNormal = math.normalize(distanceResult.normalA - distanceResult.normalB);
                    }
                    aLocalContactNormal = math.InverseRotateFast(aTransform.rot, aLocalContactNormal);
                    result              = default;
                }
            }

            result.Add(distanceResult.hitpointB, distanceResult.distance);
            return result;
        }

        private static bool BoxBoxDistance(float3 halfSizeA,
                                           float3 halfSizeB,
                                           in RigidTransform bInASpace,
                                           in RigidTransform aInBSpace,
                                           float maxDistance,
                                           out ColliderDistanceResultInternal result)
        {
            var aFaceBPointDistances = FacePointAxesDistances(in halfSizeA, in halfSizeB, in bInASpace);
            var aPointBFaceDistances = FacePointAxesDistances(in halfSizeB, in halfSizeA, in aInBSpace);
            var alignedMaxDistance   = math.cmax(math.max(aFaceBPointDistances, aPointBFaceDistances));
            if (alignedMaxDistance > maxDistance)
            {
                result = default;
                return false;
            }

            var bInARotMat = new float3x3(bInASpace.rot);
            var bAxes      = new simdFloat3(bInARotMat.c0, bInARotMat.c1, bInARotMat.c2, bInARotMat.c2);
            var axCrossB   = new simdFloat3(0f, -bAxes.z, bAxes.y);
            var ayCrossB   = new simdFloat3(bAxes.z, 0f, -bAxes.x);
            var azCrossB   = new simdFloat3(-bAxes.y, bAxes.x, 0f);

            var normalizedAxCrossB = simd.normalizesafe(axCrossB, default);
            var normalizedAyCrossB = simd.normalizesafe(ayCrossB, default);
            var normalizedAzCrossB = simd.normalizesafe(azCrossB, default);
            // If the edges are parallel, we cannot have an edge-edge feature pair. Only a point-edge or edge-face.
            var axMasks = (normalizedAxCrossB.x != 0f) | (normalizedAxCrossB.y != 0f) | (normalizedAxCrossB.z != 0f);
            var ayMasks = (normalizedAyCrossB.x != 0f) | (normalizedAyCrossB.y != 0f) | (normalizedAyCrossB.z != 0f);
            var azMasks = (normalizedAzCrossB.x != 0f) | (normalizedAzCrossB.y != 0f) | (normalizedAzCrossB.z != 0f);

            // This SAT algorithm is borrowed from Unity Physics BoxBox Manifold algorithm, except full simdFloat3-ified.
            var supportA = new simdFloat3(math.chgsign(halfSizeA.x, normalizedAxCrossB.x),
                                          math.chgsign(halfSizeA.y, normalizedAxCrossB.y),
                                          math.chgsign(halfSizeA.z, normalizedAxCrossB.z));
            var maxA                = math.abs(simd.dot(supportA, normalizedAxCrossB));
            var minA                = -maxA;
            var axisInB             = simd.mul(aInBSpace.rot, normalizedAxCrossB);
            var supportBinB         = new simdFloat3(math.chgsign(halfSizeB.x, axisInB.x), math.chgsign(halfSizeB.y, axisInB.y), math.chgsign(halfSizeB.z, axisInB.z));
            var supportB            = simd.mul(bInASpace.rot, supportBinB);
            var offsetB             = math.abs(simd.dot(supportB, normalizedAxCrossB));
            var centerB             = simd.dot(bInASpace.pos, normalizedAxCrossB);
            var maxB                = centerB + offsetB;
            var minB                = centerB - offsetB;
            var axPositiveDistances = minB - maxA;
            var axNegativeDistances = minA - maxB;
            var axDistances         = math.select(float.MinValue, math.max(axPositiveDistances, axNegativeDistances), axMasks);
            normalizedAxCrossB      = simd.select(normalizedAxCrossB, -normalizedAxCrossB, axPositiveDistances < axNegativeDistances);

            supportA = new simdFloat3(math.chgsign(halfSizeA.x, normalizedAyCrossB.x),
                                      math.chgsign(halfSizeA.y, normalizedAyCrossB.y),
                                      math.chgsign(halfSizeA.z, normalizedAyCrossB.z));
            maxA                    = math.abs(simd.dot(supportA, normalizedAyCrossB));
            minA                    = -maxA;
            axisInB                 = simd.mul(aInBSpace.rot, normalizedAyCrossB);
            supportBinB             = new simdFloat3(math.chgsign(halfSizeB.x, axisInB.x), math.chgsign(halfSizeB.y, axisInB.y), math.chgsign(halfSizeB.z, axisInB.z));
            supportB                = simd.mul(bInASpace.rot, supportBinB);
            offsetB                 = math.abs(simd.dot(supportB, normalizedAyCrossB));
            centerB                 = simd.dot(bInASpace.pos, normalizedAyCrossB);
            maxB                    = centerB + offsetB;
            minB                    = centerB - offsetB;
            var ayPositiveDistances = minB - maxA;
            var ayNegativeDistances = minA - maxB;
            var ayDistances         = math.select(float.MinValue, math.max(ayPositiveDistances, ayNegativeDistances), ayMasks);
            normalizedAyCrossB      = simd.select(normalizedAyCrossB, -normalizedAyCrossB, ayPositiveDistances < ayNegativeDistances);

            supportA = new simdFloat3(math.chgsign(halfSizeA.x, normalizedAzCrossB.x),
                                      math.chgsign(halfSizeA.y, normalizedAzCrossB.y),
                                      math.chgsign(halfSizeA.z, normalizedAzCrossB.z));
            maxA                    = math.abs(simd.dot(supportA, normalizedAzCrossB));
            minA                    = -maxA;
            axisInB                 = simd.mul(aInBSpace.rot, normalizedAzCrossB);
            supportBinB             = new simdFloat3(math.chgsign(halfSizeB.x, axisInB.x), math.chgsign(halfSizeB.y, axisInB.y), math.chgsign(halfSizeB.z, axisInB.z));
            supportB                = simd.mul(bInASpace.rot, supportBinB);
            offsetB                 = math.abs(simd.dot(supportB, normalizedAzCrossB));
            centerB                 = simd.dot(bInASpace.pos, normalizedAzCrossB);
            maxB                    = centerB + offsetB;
            minB                    = centerB - offsetB;
            var azPositiveDistances = minB - maxA;
            var azNegativeDistances = minA - maxB;
            var azDistances         = math.select(float.MinValue, math.max(azPositiveDistances, azNegativeDistances), azMasks);
            normalizedAzCrossB      = simd.select(normalizedAzCrossB, -normalizedAzCrossB, azPositiveDistances < azNegativeDistances);

            var edgeMaxDistance    = math.cmax(math.max(math.max(axDistances, ayDistances), azDistances));
            var overallMaxDistance = math.max(alignedMaxDistance, edgeMaxDistance);
            if (overallMaxDistance > maxDistance)
            {
                result = default;
                return false;
            }

            var edgeAxisIndexBatch = math.select(int.MaxValue, new int4(0, 1, 2, 2), axDistances == edgeMaxDistance);
            edgeAxisIndexBatch     = math.min(edgeAxisIndexBatch, math.select(int.MaxValue, new int4(3, 4, 5, 5), ayDistances == edgeMaxDistance));
            edgeAxisIndexBatch     = math.min(edgeAxisIndexBatch, math.select(int.MaxValue, new int4(6, 7, 8, 8), azDistances == edgeMaxDistance));
            var edgeAxisIndex      = math.cmin(edgeAxisIndexBatch);

            if (overallMaxDistance <= 0f)
            {
                if (alignedMaxDistance >= edgeMaxDistance)
                {
                    // We have penetration, and it is between a point and a face.
                    var aFaceMask = alignedMaxDistance == aFaceBPointDistances;
                    if (math.any(aFaceMask))
                    {
                        // There's a vertex on B closest to face A.
                        // If multiple axes match, prioritize y, then z over x.
                        aFaceMask.xz       &= !aFaceMask.y;
                        aFaceMask.x        &= !aFaceMask.z;
                        var aFaceNormal     = math.select(0f, math.chgsign(1f, bInASpace.pos), aFaceMask);
                        var aFaceNormalInB  = math.rotate(aInBSpace.rot, aFaceNormal);
                        // Oppose the normal on each axis, or if zero, pick the sign towards A's center
                        var signs        = math.select(-aFaceNormalInB, aInBSpace.pos, aFaceNormalInB == 0f);
                        var faceSupportB = math.transform(bInASpace, math.chgsign(halfSizeB, signs));

                        // It is possible that A is fully inside B. If so, we want the bFace result instead. In such a case,
                        // the following clamp check will fail. Due to floating point precision though, we need a little bit
                        // of margin, so we use the difference of the max distance between aFaceBPoint and aPointBFace as a
                        // margin.
                        var clampedSupport = math.clamp(faceSupportB, -halfSizeA, halfSizeA);
                        var margin         = math.distance(math.cmax(aFaceBPointDistances), math.cmax(aPointBFaceDistances));
                        if (math.all(aFaceMask | (math.abs(faceSupportB - clampedSupport) <= margin)))
                        {
                            ushort featureCodeA = (ushort)(0x8000 | (math.tzcnt(math.bitmask(new bool4(aFaceMask, false))) + math.select(0, 3, math.any(aFaceNormal < -0.5f))));
                            ushort featureCodeB = (ushort)math.bitmask(new bool4(signs < 0f, false));
                            result              = new ColliderDistanceResultInternal
                            {
                                distance     = alignedMaxDistance,
                                hitpointA    = math.select(faceSupportB, halfSizeA * aFaceNormal, aFaceMask),
                                hitpointB    = faceSupportB,
                                normalA      = aFaceNormal,
                                normalB      = math.rotate(bInASpace.rot, PointRayBox.BoxNormalFromFeatureCode(featureCodeB)),
                                featureCodeA = featureCodeA,
                                featureCodeB = featureCodeB
                            };
                            return true;
                        }
                    }
                    var bFaceMask = alignedMaxDistance == aPointBFaceDistances;
                    // Catch the edge case where A was inside B but floating point imprecision caused the aFace axis to be larger.
                    bool bFaceValid = math.any(bFaceMask);
                    if (!bFaceValid)
                    {
                        bFaceMask  = math.abs(alignedMaxDistance - aPointBFaceDistances) <= 1e-5f;
                        bFaceValid = math.any(bFaceMask);
                    }
                    if (!bFaceValid)
                    {
                        // If edges are better, we should fall back to that.
                        bool bFaceBetterThanEdges = math.cmax(aPointBFaceDistances) <= edgeMaxDistance;
                        if (bFaceBetterThanEdges)
                        {
                            // Precision is bad. Just commit to this.
                            bFaceMask  = aPointBFaceDistances == math.cmax(aPointBFaceDistances);
                            bFaceValid = true;
                        }
                    }
                    if (bFaceValid)
                    {
                        // There's a vertex on A closet to face B.
                        // If multiple axes match, prioritize y, then z over x.
                        bFaceMask.xz       &= !bFaceMask.y;
                        bFaceMask.x        &= !bFaceMask.z;
                        var bFaceNormal     = math.select(0f, math.chgsign(1f, aInBSpace.pos), bFaceMask);
                        var bFaceNormalInA  = math.rotate(bInASpace.rot, bFaceNormal);
                        // Oppose the normal on each axis or if zero, pick the sign towards B's center
                        var signs        = math.select(-bFaceNormalInA, bInASpace.pos, bFaceNormalInA == 0f);
                        var faceSupportA = math.chgsign(halfSizeA, signs);

                        ushort featureCodeA = (ushort)math.bitmask(new bool4(signs < 0f, false));
                        ushort featureCodeB = (ushort)(0x8000 | (math.tzcnt(math.bitmask(new bool4(bFaceMask, false))) + math.select(0, 3, math.any(bFaceNormal < -0.5f))));
                        result              = new ColliderDistanceResultInternal
                        {
                            distance     = alignedMaxDistance,
                            hitpointA    = faceSupportA,
                            hitpointB    = math.transform(bInASpace, math.select(math.transform(aInBSpace, faceSupportA), halfSizeB * bFaceNormal, bFaceMask)),
                            normalA      = PointRayBox.BoxNormalFromFeatureCode(featureCodeA),
                            normalB      = bFaceNormalInA,
                            featureCodeA = featureCodeA,
                            featureCodeB = featureCodeB
                        };
                        return result.distance <= maxDistance;
                    }
                }

                // We have penetration, and it is between two edges.
                float3 axis  = default;
                bool3  maskA = default;
                bool3  maskB = default;
                switch (edgeAxisIndex)
                {
                    case 0:
                        axis  = normalizedAxCrossB.a;
                        maskA = new bool3(true, false, false);
                        maskB = new bool3(true, false, false);
                        break;
                    case 1:
                        axis  = normalizedAxCrossB.b;
                        maskA = new bool3(true, false, false);
                        maskB = new bool3(false, true, false);
                        break;
                    case 2:
                        axis  = normalizedAxCrossB.c;
                        maskA = new bool3(true, false, false);
                        maskB = new bool3(false, false, true);
                        break;
                    case 3:
                        axis  = normalizedAyCrossB.a;
                        maskA = new bool3(false, true, false);
                        maskB = new bool3(true, false, false);
                        break;
                    case 4:
                        axis  = normalizedAyCrossB.b;
                        maskA = new bool3(false, true, false);
                        maskB = new bool3(false, true, false);
                        break;
                    case 5:
                        axis  = normalizedAyCrossB.c;
                        maskA = new bool3(false, true, false);
                        maskB = new bool3(false, false, true);
                        break;
                    case 6:
                        axis  = normalizedAzCrossB.a;
                        maskA = new bool3(false, false, true);
                        maskB = new bool3(true, false, false);
                        break;
                    case 7:
                        axis  = normalizedAzCrossB.b;
                        maskA = new bool3(false, false, true);
                        maskB = new bool3(false, true, false);
                        break;
                    case 8:
                        axis  = normalizedAzCrossB.c;
                        maskA = new bool3(false, false, true);
                        maskB = new bool3(false, false, true);
                        break;
                }

                // first letter = box, second letter = p is point, e is edge
                var aSigns       = math.select(axis, 1f, maskA);
                var aSupportP    = math.chgsign(halfSizeA, aSigns);
                var aSupportE    = math.select(0f, -2f, maskA);
                var edgeAxisInB  = math.rotate(aInBSpace.rot, axis);
                var bSigns       = math.select(-edgeAxisInB, 1f, maskB);
                var bSupportPinB = math.chgsign(halfSizeB, bSigns);
                var bSupportEinB = math.select(0f, -2f, maskB);
                var bSupportP    = math.transform(bInASpace, bSupportPinB);
                var bSupportE    = math.rotate(bInASpace, bSupportEinB);
                CapsuleCapsule.SegmentSegment(aSupportP, aSupportE, bSupportP, bSupportE, out var closestA, out var closestB, out _);
                var closestAinB = math.transform(aInBSpace, closestA);
                // The two points should be on or inside each other's boxes, or else we picked up the wrong edges
                // (edges of parallel faces could have multiple valid support points)
                bool valid  = math.clamp(closestAinB, -halfSizeB, halfSizeB).Equals(closestAinB);
                valid      &= math.clamp(closestB, -halfSizeA, halfSizeA).Equals(closestB);
                if (!valid)
                {
                    // Look for the ordinate of the axis closest to zero. Flipping that should give us the next best support.
                    var absAxis            = math.abs(axis);
                    var aFlipMask          = absAxis == math.cmin(absAxis);
                    var aAlternateSupportP = math.select(aSupportP, -aSupportP, aFlipMask);

                    var absAxisInB         = math.abs(edgeAxisInB);
                    var bFlipMask          = absAxisInB == math.cmin(absAxisInB);
                    var bAlternateSupportP = math.transform(bInASpace, math.select(bSupportPinB, -bSupportPinB, bFlipMask));

                    var aStarts = new simdFloat3(aSupportP, aSupportP, aAlternateSupportP, aAlternateSupportP);
                    var bStarts = new simdFloat3(bSupportP, bAlternateSupportP, bSupportP, bAlternateSupportP);
                    CapsuleCapsule.SegmentSegment(in aStarts, new simdFloat3(aSupportE), in bStarts, new simdFloat3(bSupportE), out var closestAs, out var closestBs);
                    var closestAsInB       = simd.transform(aInBSpace, closestAs);
                    var clampedAs          = simd.clamp(closestAsInB, -halfSizeB, halfSizeB);
                    var clampedBs          = simd.clamp(closestBs, -halfSizeA, halfSizeA);
                    var clampedDistortions = simd.distance(closestAsInB, clampedAs) + simd.distance(closestBs, clampedBs);
                    var bestPairIndex      = math.tzcnt(math.bitmask(clampedDistortions == math.cmin(clampedDistortions)));
                    if ((bestPairIndex & 2) == 2)
                        aSigns = math.select(aSigns, -aSigns, aFlipMask);
                    if ((bestPairIndex & 1) == 1)
                        bSigns = math.select(bSigns, -bSigns, bFlipMask);
                    closestA   = closestAs[bestPairIndex];
                    closestB   = closestBs[bestPairIndex];
                }

                var normalA    = math.select(math.chgsign(1f / math.sqrt(2f), aSigns), 0f, maskA);
                var normalBinB = math.select(math.chgsign(1f / math.sqrt(2f), bSigns), 0f, maskB);
                result         = new ColliderDistanceResultInternal
                {
                    distance     = edgeMaxDistance,
                    hitpointA    = closestA,
                    hitpointB    = closestB,
                    normalA      = normalA,
                    normalB      = math.rotate(bInASpace.rot, normalBinB),
                    featureCodeA = PointRayBox.FeatureCodeFromBoxNormal(normalA),
                    featureCodeB = PointRayBox.FeatureCodeFromBoxNormal(normalBinB)
                };
                return result.distance <= maxDistance;
            }

            // The boxes are not penetrating. If edges have the greater separating axis, test those.
            // Otherwise, it can be guaranteed we do not have an edge-edge pair.
            bool foundClosestEdge = false;
            result                = default;
            //if (edgeMaxDistance + 1e-4f >= alignedMaxDistance)
            if (edgeMaxDistance > alignedMaxDistance)
            {
                float3 axis  = default;
                bool3  maskA = default;
                bool3  maskB = default;
                switch (edgeAxisIndex)
                {
                    case 0:
                        axis  = normalizedAxCrossB.a;
                        maskA = new bool3(true, false, false);
                        maskB = new bool3(true, false, false);
                        break;
                    case 1:
                        axis  = normalizedAxCrossB.b;
                        maskA = new bool3(true, false, false);
                        maskB = new bool3(false, true, false);
                        break;
                    case 2:
                        axis  = normalizedAxCrossB.c;
                        maskA = new bool3(true, false, false);
                        maskB = new bool3(false, false, true);
                        break;
                    case 3:
                        axis  = normalizedAyCrossB.a;
                        maskA = new bool3(false, true, false);
                        maskB = new bool3(true, false, false);
                        break;
                    case 4:
                        axis  = normalizedAyCrossB.b;
                        maskA = new bool3(false, true, false);
                        maskB = new bool3(false, true, false);
                        break;
                    case 5:
                        axis  = normalizedAyCrossB.c;
                        maskA = new bool3(false, true, false);
                        maskB = new bool3(false, false, true);
                        break;
                    case 6:
                        axis  = normalizedAzCrossB.a;
                        maskA = new bool3(false, false, true);
                        maskB = new bool3(true, false, false);
                        break;
                    case 7:
                        axis  = normalizedAzCrossB.b;
                        maskA = new bool3(false, false, true);
                        maskB = new bool3(false, true, false);
                        break;
                    case 8:
                        axis  = normalizedAzCrossB.c;
                        maskA = new bool3(false, false, true);
                        maskB = new bool3(false, false, true);
                        break;
                }
                var aSigns       = math.select(axis, 1f, maskA);
                var aSupportP    = math.chgsign(halfSizeA, aSigns);
                var aSupportE    = math.select(0f, -2f * halfSizeA, maskA);
                var edgeAxisInB  = math.rotate(aInBSpace.rot, axis);
                var bSigns       = math.select(-edgeAxisInB, 1f, maskB);
                var bSupportPinB = math.chgsign(halfSizeB, bSigns);
                var bSupportEinB = math.select(0f, -2f * halfSizeB, maskB);
                var bSupportP    = math.transform(bInASpace, bSupportPinB);
                var bSupportE    = math.rotate(bInASpace, bSupportEinB);

                // Look for the ordinate of the axis closest to zero. Flipping that should give us the next best support.
                var absAxis            = math.abs(axis);
                var aFlipMask          = absAxis == math.cmin(math.select(absAxis, float.MaxValue, maskA));
                var aAlternateSupportP = math.select(aSupportP, -aSupportP, aFlipMask);

                var absAxisInB         = math.abs(edgeAxisInB);
                var bFlipMask          = absAxisInB == math.cmin(math.select(absAxisInB, float.MaxValue, maskB));
                var bAlternateSupportP = math.transform(bInASpace, math.select(bSupportPinB, -bSupportPinB, bFlipMask));

                var aStarts = new simdFloat3(aSupportP, aSupportP, aAlternateSupportP, aAlternateSupportP);
                var bStarts = new simdFloat3(bSupportP, bAlternateSupportP, bSupportP, bAlternateSupportP);
                var valid   = CapsuleCapsule.SegmentSegmentInvalidateEndpoints(aStarts,
                                                                               new simdFloat3(aSupportE),
                                                                               bStarts,
                                                                               new simdFloat3(bSupportE),
                                                                               out var closestAs,
                                                                               out var closestBs);
                if (math.any(valid))
                {
                    var distSqs       = math.select(float.MaxValue, simd.distancesq(closestAs, closestBs), valid);
                    var bestPairIndex = math.tzcnt(math.bitmask(distSqs == math.cmin(distSqs)));
                    if ((bestPairIndex & 2) == 2)
                        aSigns = math.select(aSigns, -aSigns, aFlipMask);
                    if ((bestPairIndex & 1) == 1)
                        bSigns     = math.select(bSigns, -bSigns, bFlipMask);
                    var closestA   = closestAs[bestPairIndex];
                    var closestB   = closestBs[bestPairIndex];
                    var normalA    = math.select(math.chgsign(1f / math.sqrt(2f), aSigns), 0f, maskA);
                    var normalBinB = math.select(math.chgsign(1f / math.sqrt(2f), bSigns), 0f, maskB);
                    result         = new ColliderDistanceResultInternal
                    {
                        distance     = math.sqrt(distSqs[bestPairIndex]),
                        hitpointA    = closestA,
                        hitpointB    = closestB,
                        normalA      = normalA,
                        normalB      = math.rotate(bInASpace.rot, normalBinB),
                        featureCodeA = PointRayBox.FeatureCodeFromBoxNormal(normalA),
                        featureCodeB = PointRayBox.FeatureCodeFromBoxNormal(normalBinB)
                    };
                    if (alignedMaxDistance < 0f || result.distance < alignedMaxDistance)
                    {
                        return result.distance <= maxDistance;
                    }
                    foundClosestEdge = true;
                }
            }

            // Test each point on each box against the points, edges, and planes of the other box using SIMD clamped distances.
            var aPoints03        = new simdFloat3(new float4(halfSizeA.x, -halfSizeA.x, halfSizeA.x, -halfSizeA.x), new float4(halfSizeA.yy, -halfSizeA.yy), halfSizeA.z);
            var aPoints47        = new simdFloat3(aPoints03.x, aPoints03.y, -halfSizeA.z);
            var aPoints03inB     = simd.transform(aInBSpace, aPoints03);
            var aPoints47inB     = simd.transform(aInBSpace, aPoints47);
            var aPoints03Clamped = simd.clamp(aPoints03inB, -halfSizeB, halfSizeB);
            var aPoints47Clamped = simd.clamp(aPoints47inB, -halfSizeB, halfSizeB);
            var aDistSqs03       = simd.distancesq(aPoints03inB, aPoints03Clamped);
            var aDistSqs47       = simd.distancesq(aPoints47inB, aPoints47Clamped);

            var bPoints03        = new simdFloat3(new float4(halfSizeB.x, -halfSizeB.x, halfSizeB.x, -halfSizeB.x), new float4(halfSizeB.yy, -halfSizeB.yy), halfSizeB.z);
            var bPoints47        = new simdFloat3(bPoints03.x, bPoints03.y, -halfSizeB.z);
            var bPoints03inA     = simd.transform(bInASpace, bPoints03);
            var bPoints47inA     = simd.transform(bInASpace, bPoints47);
            var bPoints03Clamped = simd.clamp(bPoints03inA, -halfSizeA, halfSizeA);
            var bPoints47Clamped = simd.clamp(bPoints47inA, -halfSizeA, halfSizeA);
            var bDistSqs03       = simd.distancesq(bPoints03inA, bPoints03Clamped);
            var bDistSqs47       = simd.distancesq(bPoints47inA, bPoints47Clamped);

            var a47Better = aDistSqs47 < aDistSqs03;
            var bestAs    = math.min(aDistSqs03, aDistSqs47);
            var b47Better = bDistSqs47 < bDistSqs03;
            var bestBs    = math.min(bDistSqs03, bDistSqs47);
            var asBetter  = bestAs < bestBs;
            var bests     = math.min(bestAs, bestBs);
            var best      = math.cmin(bests);
            if (foundClosestEdge && best > result.distance * result.distance)
            {
                return result.distance <= maxDistance;
            }
            if (best > maxDistance * maxDistance)
            {
                return false;
            }
            var bestIndex = math.tzcnt(math.bitmask(best == bests));
            var bestId    = bestIndex + math.select(0, 4, math.select(b47Better, a47Better, asBetter)[bestIndex]);  // math.select(8, 0, asBetter[bestIndex]);
            if (asBetter[bestIndex])
            {
                float3 hitB = default;
                switch (bestId)
                {
                    case 0:
                        hitB             = aPoints03Clamped.a;
                        result.distance  = aDistSqs03.x;
                        result.hitpointA = aPoints03.a;
                        break;
                    case 1:
                        hitB             = aPoints03Clamped.b;
                        result.distance  = aDistSqs03.y;
                        result.hitpointA = aPoints03.b;
                        break;
                    case 2:
                        hitB             = aPoints03Clamped.c;
                        result.distance  = aDistSqs03.z;
                        result.hitpointA = aPoints03.c;
                        break;
                    case 3:
                        hitB             = aPoints03Clamped.d;
                        result.distance  = aDistSqs03.w;
                        result.hitpointA = aPoints03.d;
                        break;
                    case 4:
                        hitB             = aPoints47Clamped.a;
                        result.distance  = aDistSqs47.x;
                        result.hitpointA = aPoints47.a;
                        break;
                    case 5:
                        hitB             = aPoints47Clamped.b;
                        result.distance  = aDistSqs47.y;
                        result.hitpointA = aPoints47.b;
                        break;
                    case 6:
                        hitB             = aPoints47Clamped.c;
                        result.distance  = aDistSqs47.z;
                        result.hitpointA = aPoints47.c;
                        break;
                    case 7:
                        hitB             = aPoints47Clamped.d;
                        result.distance  = aDistSqs47.w;
                        result.hitpointA = aPoints47.d;
                        break;
                }
                result.distance   = math.sqrt(result.distance);
                var hitBInsideBox = halfSizeB - math.abs(hitB);
                // Sometimes due to floating point error, we actually have overlap here. So we need to push the hitpoint out to the surface.
                // First, try to correct very tiny floating point errors, so that we catch edges.
                var isVeryTinyError = hitBInsideBox < 1e-5f;
                hitB                = math.select(hitB, math.chgsign(halfSizeB, hitB), isVeryTinyError);
                hitBInsideBox       = halfSizeB - math.abs(hitB);
                if (math.all(hitBInsideBox > 0f))
                {
                    // The error is a little bigger. Pick the closest axis and push out to the face.
                    var boostAxis    = math.tzcnt(math.bitmask(new bool4(hitBInsideBox == math.cmin(hitBInsideBox), false)));
                    var boostAmount  = hitBInsideBox[boostAxis];
                    result.distance -= boostAmount;
                    hitB[boostAxis]  = math.chgsign(halfSizeB[boostAxis], hitB[boostAxis]);
                }
                result.hitpointB    = math.transform(bInASpace, hitB);
                result.featureCodeA = (ushort)bestId;
                result.normalA      = math.normalize(math.select(1f, -1f, (bestId & new int3(1, 2, 4)) != 0));
                result.normalB      = math.normalize(math.select(0f, math.chgsign(1f, hitB), hitB == math.chgsign(halfSizeB, hitB)));
                result.featureCodeB = PointRayBox.FeatureCodeFromBoxNormal(result.normalB);
                result.normalB      = math.rotate(bInASpace.rot, result.normalB);
            }
            else
            {
                float3 hitA = default;
                switch (bestId)
                {
                    case 0:
                        hitA             = bPoints03Clamped.a;
                        result.distance  = bDistSqs03.x;
                        result.hitpointB = bPoints03inA.a;
                        break;
                    case 1:
                        hitA             = bPoints03Clamped.b;
                        result.distance  = bDistSqs03.y;
                        result.hitpointB = bPoints03inA.b;
                        break;
                    case 2:
                        hitA             = bPoints03Clamped.c;
                        result.distance  = bDistSqs03.z;
                        result.hitpointB = bPoints03inA.c;
                        break;
                    case 3:
                        hitA             = bPoints03Clamped.d;
                        result.distance  = bDistSqs03.w;
                        result.hitpointB = bPoints03inA.d;
                        break;
                    case 4:
                        hitA             = bPoints47Clamped.a;
                        result.distance  = bDistSqs47.x;
                        result.hitpointB = bPoints47inA.a;
                        break;
                    case 5:
                        hitA             = bPoints47Clamped.b;
                        result.distance  = bDistSqs47.y;
                        result.hitpointB = bPoints47inA.b;
                        break;
                    case 6:
                        hitA             = bPoints47Clamped.c;
                        result.distance  = bDistSqs47.z;
                        result.hitpointB = bPoints47inA.c;
                        break;
                    case 7:
                        hitA             = bPoints47Clamped.d;
                        result.distance  = bDistSqs47.w;
                        result.hitpointB = bPoints47inA.d;
                        break;
                }
                result.distance   = math.sqrt(result.distance);
                var hitAInsideBox = halfSizeA - math.abs(hitA);
                // Sometimes due to floating point error, we actually have overlap here. So we need to push the hitpoint out to the surface.
                // First, try to correct very tiny floating point errors, so that we catch edges.
                var isVeryTinyError = hitAInsideBox < 1e-5f;
                hitA                = math.select(hitA, math.chgsign(halfSizeA, hitA), isVeryTinyError);
                hitAInsideBox       = halfSizeA - math.abs(hitA);
                if (math.all(hitAInsideBox > 0f))
                {
                    // The error is a little bigger. Pick the closest axis and push out to the face.
                    var boostAxis    = math.tzcnt(math.bitmask(new bool4(hitAInsideBox == math.cmin(hitAInsideBox), false)));
                    var boostAmount  = hitAInsideBox[boostAxis];
                    result.distance -= boostAmount;
                    hitA[boostAxis]  = math.chgsign(halfSizeA[boostAxis], hitA[boostAxis]);
                }
                result.hitpointA    = hitA;
                result.featureCodeB = (ushort)bestId;
                result.normalB      = math.rotate(bInASpace.rot, math.normalize(math.select(1f, -1f, (bestId & new int3(1, 2, 4)) != 0)));
                result.normalA      = math.normalize(math.select(0f, math.chgsign(1f, hitA), hitA == math.chgsign(halfSizeA, hitA)));
                result.featureCodeA = PointRayBox.FeatureCodeFromBoxNormal(result.normalA);
            }
            return result.distance <= maxDistance;
        }

        // This is a draft for a newer implementation which may not be as susceptible to floating point precision, but is more expensive to compute.
        // It was drafted, and then bugs were found and fixed in the original algorithm.
        private static bool BoxBoxDistance2(float3 halfSizeA,
                                           float3 halfSizeB,
                                           in RigidTransform bInASpace,
                                           in RigidTransform aInBSpace,
                                           float maxDistance,
                                           out ColliderDistanceResultInternal result)
        {
            var aFaceBPointDistances = FacePointAxesDistances(in halfSizeA, in halfSizeB, in bInASpace);
            var aPointBFaceDistances = FacePointAxesDistances(in halfSizeB, in halfSizeA, in aInBSpace);
            var alignedMaxDistance = math.cmax(math.max(aFaceBPointDistances, aPointBFaceDistances));
            if (alignedMaxDistance > maxDistance)
            {
                result = default;
                return false;
            }

            var bInARotMat = new float3x3(bInASpace.rot);
            var bAxes = new simdFloat3(bInARotMat.c0, bInARotMat.c1, bInARotMat.c2, bInARotMat.c2);
            var axCrossB = new simdFloat3(0f, -bAxes.z, bAxes.y);
            var ayCrossB = new simdFloat3(bAxes.z, 0f, -bAxes.x);
            var azCrossB = new simdFloat3(-bAxes.y, bAxes.x, 0f);

            var normalizedAxCrossB = simd.normalizesafe(axCrossB, default);
            var normalizedAyCrossB = simd.normalizesafe(ayCrossB, default);
            var normalizedAzCrossB = simd.normalizesafe(azCrossB, default);
            // If the edges are parallel, we cannot have an edge-edge feature pair. Only a point-edge or edge-face.
            var axMasks = (normalizedAxCrossB.x != 0f) | (normalizedAxCrossB.y != 0f) | (normalizedAxCrossB.z != 0f);
            var ayMasks = (normalizedAyCrossB.x != 0f) | (normalizedAyCrossB.y != 0f) | (normalizedAyCrossB.z != 0f);
            var azMasks = (normalizedAzCrossB.x != 0f) | (normalizedAzCrossB.y != 0f) | (normalizedAzCrossB.z != 0f);

            // This SAT algorithm is borrowed from Unity Physics BoxBox Manifold algorithm, except full simdFloat3-ified.
            var supportA = new simdFloat3(math.chgsign(halfSizeA.x, normalizedAxCrossB.x),
                                          math.chgsign(halfSizeA.y, normalizedAxCrossB.y),
                                          math.chgsign(halfSizeA.z, normalizedAxCrossB.z));
            var maxA = math.abs(simd.dot(supportA, normalizedAxCrossB));
            var minA = -maxA;
            var axisInB = simd.mul(aInBSpace.rot, normalizedAxCrossB);
            var supportBinB = new simdFloat3(math.chgsign(halfSizeB.x, axisInB.x), math.chgsign(halfSizeB.y, axisInB.y), math.chgsign(halfSizeB.z, axisInB.z));
            var supportB = simd.mul(bInASpace.rot, supportBinB);
            var offsetB = math.abs(simd.dot(supportB, normalizedAxCrossB));
            var centerB = simd.dot(bInASpace.pos, normalizedAxCrossB);
            var maxB = centerB + offsetB;
            var minB = centerB - offsetB;
            var axPositiveDistances = minB - maxA;
            var axNegativeDistances = minA - maxB;
            var axDistances = math.select(float.MinValue, math.max(axPositiveDistances, axNegativeDistances), axMasks);
            normalizedAxCrossB = simd.select(normalizedAxCrossB, -normalizedAxCrossB, axPositiveDistances < axNegativeDistances);

            supportA = new simdFloat3(math.chgsign(halfSizeA.x, normalizedAyCrossB.x),
                                      math.chgsign(halfSizeA.y, normalizedAyCrossB.y),
                                      math.chgsign(halfSizeA.z, normalizedAyCrossB.z));
            maxA = math.abs(simd.dot(supportA, normalizedAyCrossB));
            minA = -maxA;
            axisInB = simd.mul(aInBSpace.rot, normalizedAyCrossB);
            supportBinB = new simdFloat3(math.chgsign(halfSizeB.x, axisInB.x), math.chgsign(halfSizeB.y, axisInB.y), math.chgsign(halfSizeB.z, axisInB.z));
            supportB = simd.mul(bInASpace.rot, supportBinB);
            offsetB = math.abs(simd.dot(supportB, normalizedAyCrossB));
            centerB = simd.dot(bInASpace.pos, normalizedAyCrossB);
            maxB = centerB + offsetB;
            minB = centerB - offsetB;
            var ayPositiveDistances = minB - maxA;
            var ayNegativeDistances = minA - maxB;
            var ayDistances = math.select(float.MinValue, math.max(ayPositiveDistances, ayNegativeDistances), ayMasks);
            normalizedAyCrossB = simd.select(normalizedAyCrossB, -normalizedAyCrossB, ayPositiveDistances < ayNegativeDistances);

            supportA = new simdFloat3(math.chgsign(halfSizeA.x, normalizedAzCrossB.x),
                                      math.chgsign(halfSizeA.y, normalizedAzCrossB.y),
                                      math.chgsign(halfSizeA.z, normalizedAzCrossB.z));
            maxA = math.abs(simd.dot(supportA, normalizedAzCrossB));
            minA = -maxA;
            axisInB = simd.mul(aInBSpace.rot, normalizedAzCrossB);
            supportBinB = new simdFloat3(math.chgsign(halfSizeB.x, axisInB.x), math.chgsign(halfSizeB.y, axisInB.y), math.chgsign(halfSizeB.z, axisInB.z));
            supportB = simd.mul(bInASpace.rot, supportBinB);
            offsetB = math.abs(simd.dot(supportB, normalizedAzCrossB));
            centerB = simd.dot(bInASpace.pos, normalizedAzCrossB);
            maxB = centerB + offsetB;
            minB = centerB - offsetB;
            var azPositiveDistances = minB - maxA;
            var azNegativeDistances = minA - maxB;
            var azDistances = math.select(float.MinValue, math.max(azPositiveDistances, azNegativeDistances), azMasks);
            normalizedAzCrossB = simd.select(normalizedAzCrossB, -normalizedAzCrossB, azPositiveDistances < azNegativeDistances);

            var edgeMaxDistance = math.cmax(math.max(math.max(axDistances, ayDistances), azDistances));
            if (edgeMaxDistance > maxDistance)
            {
                result = default;
                return false;
            }

            ColliderDistanceResultInternal pointFaceResult = default;
            bool pointFaceResultValid = false;

            // Vertex feature checks
            if (alignedMaxDistance < 0f)
            {
                // We have penetration, potentially between a point and a face.
                var bestAFaceBPointDistance = math.cmax(aFaceBPointDistances);
                ColliderDistanceResultInternal aFaceBPointResult = default;
                bool aFaceBPointResultValid = false;
                if (bestAFaceBPointDistance < 0f)
                {
                    // There might be a vertex on B closest to face A.
                    var aFaceMask = bestAFaceBPointDistance == aFaceBPointDistances;
                    // If multiple axes match, prioritize y, then z over x.
                    aFaceMask.xz &= !aFaceMask.y;
                    aFaceMask.x &= !aFaceMask.z;
                    var aFaceNormal = math.select(0f, math.chgsign(1f, bInASpace.pos), aFaceMask);
                    var aFaceNormalInB = math.rotate(aInBSpace.rot, aFaceNormal);
                    // Oppose the normal on each axis, or if zero, pick the sign towards A's center
                    var signs = math.select(-aFaceNormalInB, aInBSpace.pos, aFaceNormalInB == 0f);
                    var faceSupportB = math.transform(bInASpace, math.chgsign(halfSizeB, signs));

                    // Force the oordinate along the penetration axis to 0, because if we have punch through
                    // we don't want to see a clamped value.
                    var faceSupportBOffAxis = math.select(faceSupportB, float3.zero, aFaceMask);
                    // Epsilon hopefully catches floating point error from transforming spaces when the penetration is very tiny.
                    var clampedSupportBOffAxis = math.clamp(faceSupportBOffAxis, -halfSizeA - math.EPSILON, halfSizeA + math.EPSILON);
                    if (faceSupportBOffAxis.Equals(clampedSupportBOffAxis))
                    {
                        ushort featureCodeA = (ushort)(0x8000 | (math.tzcnt(math.bitmask(new bool4(aFaceMask, false))) + math.select(0, 3, math.any(aFaceNormal < -0.5f))));
                        ushort featureCodeB = (ushort)math.bitmask(new bool4(signs < 0f, false));
                        aFaceBPointResult = new ColliderDistanceResultInternal
                        {
                            distance = bestAFaceBPointDistance,
                            hitpointA = math.select(faceSupportB, halfSizeA * aFaceNormal, aFaceMask),
                            hitpointB = faceSupportB,
                            normalA = aFaceNormal,
                            normalB = math.rotate(bInASpace.rot, PointRayBox.BoxNormalFromFeatureCode(featureCodeB)),
                            featureCodeA = featureCodeA,
                            featureCodeB = featureCodeB
                        };
                        aFaceBPointResultValid = true;
                    }
                }
                var bestAPointBFaceDistance = math.cmax(aPointBFaceDistances);
                ColliderDistanceResultInternal aPointBFaceResult = default;
                bool aPointBFaceResultValid = false;
                if (bestAFaceBPointDistance < 0f)
                {
                    // There might be a vertex on A closest to face B.
                    var bFaceMask = alignedMaxDistance == aPointBFaceDistances;
                    // If multiple axes match, prioritize y, then z over x.
                    bFaceMask.xz &= !bFaceMask.y;
                    bFaceMask.x &= !bFaceMask.z;
                    var bFaceNormal = math.select(0f, math.chgsign(1f, aInBSpace.pos), bFaceMask);
                    var bFaceNormalInA = math.rotate(bInASpace.rot, bFaceNormal);
                    // Oppose the normal on each axis, or if zero, pick the sign towards B's center
                    var signs = math.select(-bFaceNormalInA, bInASpace.pos, bFaceNormalInA == 0f);
                    var faceSupportAinA = math.chgsign(halfSizeA, signs);
                    var faceSupportA = math.transform(aInBSpace, faceSupportAinA);

                    // Force the oordinate along the penetration axis to 0, because if we have punch through
                    // we don't want to see a clamped value.
                    var faceSupportAOffAxis = math.select(faceSupportA, float3.zero, bFaceMask);
                    // Epsilon hopefully catches floating point error from transforming spaces when the penetration is very tiny.
                    var clampedSupportAOffAxis = math.clamp(faceSupportAOffAxis, -halfSizeB - math.EPSILON, halfSizeB + math.EPSILON);
                    if (faceSupportAOffAxis.Equals(clampedSupportAOffAxis))
                    {
                        ushort featureCodeA = (ushort)math.bitmask(new bool4(signs < 0f, false));
                        ushort featureCodeB = (ushort)(0x8000 | (math.tzcnt(math.bitmask(new bool4(bFaceMask, false))) + math.select(0, 3, math.any(bFaceNormal < -0.5f))));
                        aPointBFaceResult = new ColliderDistanceResultInternal
                        {
                            distance = bestAPointBFaceDistance,
                            hitpointA = faceSupportAinA,
                            hitpointB = math.transform(bInASpace, math.select(faceSupportA, halfSizeB * bFaceNormal, bFaceMask)),
                            normalB = bFaceNormalInA,
                            normalA = PointRayBox.BoxNormalFromFeatureCode(featureCodeA),
                            featureCodeA = featureCodeA,
                            featureCodeB = featureCodeB
                        };
                        aPointBFaceResultValid = true;
                    }
                }

                pointFaceResultValid = aFaceBPointResultValid | aPointBFaceResultValid;
                var useA = aFaceBPointResultValid && (aFaceBPointResult.distance > aPointBFaceResult.distance || !aPointBFaceResultValid);
                pointFaceResult = useA ? aFaceBPointResult : aPointBFaceResult;
            }
            else
            {
                // Test each point on each box against the points, edges, and planes of the other box using SIMD clamped distances.
                var aPoints03 = new simdFloat3(new float4(halfSizeA.x, -halfSizeA.x, halfSizeA.x, -halfSizeA.x), new float4(halfSizeA.yy, -halfSizeA.yy), halfSizeA.z);
                var aPoints47 = new simdFloat3(aPoints03.x, aPoints03.y, -halfSizeA.z);
                var aPoints03inB = simd.transform(aInBSpace, aPoints03);
                var aPoints47inB = simd.transform(aInBSpace, aPoints47);
                var aPoints03Clamped = simd.clamp(aPoints03inB, -halfSizeB, halfSizeB);
                var aPoints47Clamped = simd.clamp(aPoints47inB, -halfSizeB, halfSizeB);
                var aDistSqs03 = simd.distancesq(aPoints03inB, aPoints03Clamped);
                var aDistSqs47 = simd.distancesq(aPoints47inB, aPoints47Clamped);

                var bPoints03 = new simdFloat3(new float4(halfSizeB.x, -halfSizeB.x, halfSizeB.x, -halfSizeB.x), new float4(halfSizeB.yy, -halfSizeB.yy), halfSizeB.z);
                var bPoints47 = new simdFloat3(bPoints03.x, bPoints03.y, -halfSizeB.z);
                var bPoints03inA = simd.transform(bInASpace, bPoints03);
                var bPoints47inA = simd.transform(bInASpace, bPoints47);
                var bPoints03Clamped = simd.clamp(bPoints03inA, -halfSizeA, halfSizeA);
                var bPoints47Clamped = simd.clamp(bPoints47inA, -halfSizeA, halfSizeA);
                var bDistSqs03 = simd.distancesq(bPoints03inA, bPoints03Clamped);
                var bDistSqs47 = simd.distancesq(bPoints47inA, bPoints47Clamped);

                var a47Better = aDistSqs47 < aDistSqs03;
                var bestAs = math.min(aDistSqs03, aDistSqs47);
                var b47Better = bDistSqs47 < bDistSqs03;
                var bestBs = math.min(bDistSqs03, bDistSqs47);
                var asBetter = bestAs < bestBs;
                var bests = math.min(bestAs, bestBs);
                var best = math.cmin(bests);
                if (best <= maxDistance * maxDistance)
                {
                    var bestIndex = math.tzcnt(math.bitmask(best == bests));
                    var bestId = bestIndex + math.select(0, 4, math.select(b47Better, a47Better, asBetter)[bestIndex]);  // math.select(8, 0, asBetter[bestIndex]);
                    if (asBetter[bestIndex])
                    {
                        float3 hitB = default;
                        switch (bestId)
                        {
                            case 0:
                                hitB = aPoints03Clamped.a;
                                pointFaceResult.distance = aDistSqs03.x;
                                pointFaceResult.hitpointA = aPoints03.a;
                                break;
                            case 1:
                                hitB = aPoints03Clamped.b;
                                pointFaceResult.distance = aDistSqs03.y;
                                pointFaceResult.hitpointA = aPoints03.b;
                                break;
                            case 2:
                                hitB = aPoints03Clamped.c;
                                pointFaceResult.distance = aDistSqs03.z;
                                pointFaceResult.hitpointA = aPoints03.c;
                                break;
                            case 3:
                                hitB = aPoints03Clamped.d;
                                pointFaceResult.distance = aDistSqs03.w;
                                pointFaceResult.hitpointA = aPoints03.d;
                                break;
                            case 4:
                                hitB = aPoints47Clamped.a;
                                pointFaceResult.distance = aDistSqs47.x;
                                pointFaceResult.hitpointA = aPoints47.a;
                                break;
                            case 5:
                                hitB = aPoints47Clamped.b;
                                pointFaceResult.distance = aDistSqs47.y;
                                pointFaceResult.hitpointA = aPoints47.b;
                                break;
                            case 6:
                                hitB = aPoints47Clamped.c;
                                pointFaceResult.distance = aDistSqs47.z;
                                pointFaceResult.hitpointA = aPoints47.c;
                                break;
                            case 7:
                                hitB = aPoints47Clamped.d;
                                pointFaceResult.distance = aDistSqs47.w;
                                pointFaceResult.hitpointA = aPoints47.d;
                                break;
                        }
                        pointFaceResult.distance = math.sqrt(pointFaceResult.distance);
                        var hitBInsideBox = halfSizeB - math.abs(hitB);
                        // Sometimes due to floating point error, we actually have overlap here. So we need to push the hitpoint out to the surface.
                        // First, try to correct very tiny floating point errors, so that we catch edges.
                        var isVeryTinyError = hitBInsideBox < 1e-5f;
                        hitB = math.select(hitB, math.chgsign(halfSizeB, hitB), isVeryTinyError);
                        hitBInsideBox = halfSizeB - math.abs(hitB);
                        if (math.all(hitBInsideBox > 0f))
                        {
                            // The error is a little bigger. Pick the closest axis and push out to the face.
                            var boostAxis = math.tzcnt(math.bitmask(new bool4(hitBInsideBox == math.cmin(hitBInsideBox), false)));
                            var boostAmount = hitBInsideBox[boostAxis];
                            pointFaceResult.distance -= boostAmount;
                            hitB[boostAxis] = math.chgsign(halfSizeB[boostAxis], hitB[boostAxis]);
                        }
                        pointFaceResult.hitpointB = math.transform(bInASpace, hitB);
                        pointFaceResult.featureCodeA = (ushort)bestId;
                        pointFaceResult.normalA = math.normalize(math.select(1f, -1f, (bestId & new int3(1, 2, 4)) != 0));
                        pointFaceResult.normalB = math.normalize(math.select(0f, math.chgsign(1f, hitB), hitB == math.chgsign(halfSizeB, hitB)));
                        pointFaceResult.featureCodeB = PointRayBox.FeatureCodeFromBoxNormal(pointFaceResult.normalB);
                        pointFaceResult.normalB = math.rotate(bInASpace.rot, pointFaceResult.normalB);
                        pointFaceResultValid = true;
                    }
                    else
                    {
                        float3 hitA = default;
                        switch (bestId)
                        {
                            case 0:
                                hitA = bPoints03Clamped.a;
                                pointFaceResult.distance = bDistSqs03.x;
                                pointFaceResult.hitpointB = bPoints03inA.a;
                                break;
                            case 1:
                                hitA = bPoints03Clamped.b;
                                pointFaceResult.distance = bDistSqs03.y;
                                pointFaceResult.hitpointB = bPoints03inA.b;
                                break;
                            case 2:
                                hitA = bPoints03Clamped.c;
                                pointFaceResult.distance = bDistSqs03.z;
                                pointFaceResult.hitpointB = bPoints03inA.c;
                                break;
                            case 3:
                                hitA = bPoints03Clamped.d;
                                pointFaceResult.distance = bDistSqs03.w;
                                pointFaceResult.hitpointB = bPoints03inA.d;
                                break;
                            case 4:
                                hitA = bPoints47Clamped.a;
                                pointFaceResult.distance = bDistSqs47.x;
                                pointFaceResult.hitpointB = bPoints47inA.a;
                                break;
                            case 5:
                                hitA = bPoints47Clamped.b;
                                pointFaceResult.distance = bDistSqs47.y;
                                pointFaceResult.hitpointB = bPoints47inA.b;
                                break;
                            case 6:
                                hitA = bPoints47Clamped.c;
                                pointFaceResult.distance = bDistSqs47.z;
                                pointFaceResult.hitpointB = bPoints47inA.c;
                                break;
                            case 7:
                                hitA = bPoints47Clamped.d;
                                pointFaceResult.distance = bDistSqs47.w;
                                pointFaceResult.hitpointB = bPoints47inA.d;
                                break;
                        }
                        pointFaceResult.distance = math.sqrt(pointFaceResult.distance);
                        var hitAInsideBox = halfSizeA - math.abs(hitA);
                        // Sometimes due to floating point error, we actually have overlap here. So we need to push the hitpoint out to the surface.
                        // First, try to correct very tiny floating point errors, so that we catch edges.
                        var isVeryTinyError = hitAInsideBox < 1e-5f;
                        hitA = math.select(hitA, math.chgsign(halfSizeA, hitA), isVeryTinyError);
                        hitAInsideBox = halfSizeA - math.abs(hitA);
                        if (math.all(hitAInsideBox > 0f))
                        {
                            // The error is a little bigger. Pick the closest axis and push out to the face.
                            var boostAxis = math.tzcnt(math.bitmask(new bool4(hitAInsideBox == math.cmin(hitAInsideBox), false)));
                            var boostAmount = hitAInsideBox[boostAxis];
                            pointFaceResult.distance -= boostAmount;
                            hitA[boostAxis] = math.chgsign(halfSizeA[boostAxis], hitA[boostAxis]);
                        }
                        pointFaceResult.hitpointA = hitA;
                        pointFaceResult.featureCodeB = (ushort)bestId;
                        pointFaceResult.normalB = math.rotate(bInASpace.rot, math.normalize(math.select(1f, -1f, (bestId & new int3(1, 2, 4)) != 0)));
                        pointFaceResult.normalA = math.normalize(math.select(0f, math.chgsign(1f, hitA), hitA == math.chgsign(halfSizeA, hitA)));
                        pointFaceResult.featureCodeA = PointRayBox.FeatureCodeFromBoxNormal(pointFaceResult.normalA);
                        pointFaceResultValid = true;
                    }
                }
            }

            // Edge feature checks
            ColliderDistanceResultInternal edgeEdgeResult = default;
            bool edgeEdgeResultValid = false;
            {
                var edgeAxisIndexBatch = math.select(int.MaxValue, new int4(0, 1, 2, 2), axDistances == edgeMaxDistance);
                edgeAxisIndexBatch = math.min(edgeAxisIndexBatch, math.select(int.MaxValue, new int4(3, 4, 5, 5), ayDistances == edgeMaxDistance));
                edgeAxisIndexBatch = math.min(edgeAxisIndexBatch, math.select(int.MaxValue, new int4(6, 7, 8, 8), azDistances == edgeMaxDistance));
                var edgeAxisIndex = math.cmin(edgeAxisIndexBatch);

                float3 axis = default;
                bool3 maskA = default;
                bool3 maskB = default;
                switch (edgeAxisIndex)
                {
                    case 0:
                        axis = normalizedAxCrossB.a;
                        maskA = new bool3(true, false, false);
                        maskB = new bool3(true, false, false);
                        break;
                    case 1:
                        axis = normalizedAxCrossB.b;
                        maskA = new bool3(true, false, false);
                        maskB = new bool3(false, true, false);
                        break;
                    case 2:
                        axis = normalizedAxCrossB.c;
                        maskA = new bool3(true, false, false);
                        maskB = new bool3(false, false, true);
                        break;
                    case 3:
                        axis = normalizedAyCrossB.a;
                        maskA = new bool3(false, true, false);
                        maskB = new bool3(true, false, false);
                        break;
                    case 4:
                        axis = normalizedAyCrossB.b;
                        maskA = new bool3(false, true, false);
                        maskB = new bool3(false, true, false);
                        break;
                    case 5:
                        axis = normalizedAyCrossB.c;
                        maskA = new bool3(false, true, false);
                        maskB = new bool3(false, false, true);
                        break;
                    case 6:
                        axis = normalizedAzCrossB.a;
                        maskA = new bool3(false, false, true);
                        maskB = new bool3(true, false, false);
                        break;
                    case 7:
                        axis = normalizedAzCrossB.b;
                        maskA = new bool3(false, false, true);
                        maskB = new bool3(false, true, false);
                        break;
                    case 8:
                        axis = normalizedAzCrossB.c;
                        maskA = new bool3(false, false, true);
                        maskB = new bool3(false, false, true);
                        break;
                }
                var aSigns = math.select(axis, 1f, maskA);
                var aSupportP = math.chgsign(halfSizeA, aSigns);
                var aSupportE = math.select(0f, -2f * halfSizeA, maskA);
                var edgeAxisInB = math.rotate(aInBSpace.rot, axis);
                var bSigns = math.select(-edgeAxisInB, 1f, maskB);
                var bSupportPinB = math.chgsign(halfSizeB, bSigns);
                var bSupportEinB = math.select(0f, -2f * halfSizeB, maskB);
                var bSupportP = math.transform(bInASpace, bSupportPinB);
                var bSupportE = math.rotate(bInASpace, bSupportEinB);

                // Look for the ordinate of the axis closest to zero. Flipping that should give us the next best support.
                var absAxis = math.abs(axis);
                var aFlipMask = absAxis == math.cmin(math.select(absAxis, float.MaxValue, maskA));
                var aAlternateSupportP = math.select(aSupportP, -aSupportP, aFlipMask);

                var absAxisInB = math.abs(edgeAxisInB);
                var bFlipMask = absAxisInB == math.cmin(math.select(absAxisInB, float.MaxValue, maskB));
                var bAlternateSupportP = math.transform(bInASpace, math.select(bSupportPinB, -bSupportPinB, bFlipMask));

                var aStarts = new simdFloat3(aSupportP, aSupportP, aAlternateSupportP, aAlternateSupportP);
                var bStarts = new simdFloat3(bSupportP, bAlternateSupportP, bSupportP, bAlternateSupportP);
                var valid = CapsuleCapsule.SegmentSegmentInvalidateEndpoints(aStarts,
                                                                               new simdFloat3(aSupportE),
                                                                               bStarts,
                                                                               new simdFloat3(bSupportE),
                                                                               out var closestAs,
                                                                               out var closestBs);
                if (math.any(valid))
                {
                    var distSqs = math.select(float.MaxValue, simd.distancesq(closestAs, closestBs), valid);
                    var bestPairIndex = math.tzcnt(math.bitmask(distSqs == math.cmin(distSqs)));
                    if ((bestPairIndex & 2) == 2)
                        aSigns = math.select(aSigns, -aSigns, aFlipMask);
                    if ((bestPairIndex & 1) == 1)
                        bSigns = math.select(bSigns, -bSigns, bFlipMask);
                    var closestA = closestAs[bestPairIndex];
                    var closestB = closestBs[bestPairIndex];
                    var normalA = math.select(math.chgsign(1f / math.sqrt(2f), aSigns), 0f, maskA);
                    var normalBinB = math.select(math.chgsign(1f / math.sqrt(2f), bSigns), 0f, maskB);
                    var closestAinB = math.transform(aInBSpace, closestA);

                    bool isInside = math.clamp(closestAinB, -halfSizeB, halfSizeB).Equals(closestAinB);
                    isInside &= math.clamp(closestB, -halfSizeA, halfSizeA).Equals(closestB);

                    edgeEdgeResult = new ColliderDistanceResultInternal
                    {
                        distance = math.select(1f, -1f, isInside) * math.sqrt(distSqs[bestPairIndex]),
                        hitpointA = closestA,
                        hitpointB = closestB,
                        normalA = normalA,
                        normalB = math.rotate(bInASpace.rot, normalBinB),
                        featureCodeA = PointRayBox.FeatureCodeFromBoxNormal(normalA),
                        featureCodeB = PointRayBox.FeatureCodeFromBoxNormal(normalBinB)
                    };
                    edgeEdgeResultValid = true;
                }
            }

            if (pointFaceResultValid && pointFaceResult.distance < 0f && (!edgeEdgeResultValid || edgeEdgeResult.distance < pointFaceResult.distance))
            {
                result = pointFaceResult;
            }
            else if (edgeEdgeResultValid && edgeEdgeResult.distance < 0f && (!pointFaceResultValid || pointFaceResult.distance < edgeEdgeResult.distance))
            {
                result = edgeEdgeResult;
            }
            else if (edgeEdgeResultValid && edgeEdgeResult.distance >= 0f && (!pointFaceResultValid || pointFaceResult.distance < 0f || pointFaceResult.distance > edgeEdgeResult.distance))
            {
                result = edgeEdgeResult;
            }
            else if (pointFaceResultValid)
            {
                result = pointFaceResult;
            }
            else
            {
                result = default;
                result.distance = float.PositiveInfinity;
            }
            return result.distance <= maxDistance;
        }

        private static float3 FacePointAxesDistances(in float3 halfSizeA, in float3 halfSizeB, in RigidTransform bInASpace)
        {
            float3 x       = math.rotate(bInASpace.rot, new float3(halfSizeB.x, 0, 0));
            float3 y       = math.rotate(bInASpace.rot, new float3(0, halfSizeB.y, 0));
            float3 z       = math.rotate(bInASpace.rot, new float3(0, 0, halfSizeB.z));
            var    extents = math.abs(x) + math.abs(y) + math.abs(z);

            var distancesBetweenCenters = math.abs(bInASpace.pos);
            var sumExtents              = halfSizeA + extents;
            return distancesBetweenCenters - sumExtents;
        }
    }
}

