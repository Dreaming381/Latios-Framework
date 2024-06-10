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
            var bInATransform = math.mul(math.inverse(aTransform), bTransform);
            var gjkResult     = GjkEpa.DoGjkEpa(boxA, boxB, in bInATransform);
            var featureCodeA  = PointRayBox.FeatureCodeFromGjk(gjkResult.simplexAVertexCount, gjkResult.simplexAVertexA, gjkResult.simplexAVertexB, gjkResult.simplexAVertexC);
            var featureCodeB  = PointRayBox.FeatureCodeFromGjk(gjkResult.simplexBVertexCount, gjkResult.simplexBVertexA, gjkResult.simplexBVertexB, gjkResult.simplexBVertexC);
            result            = InternalQueryTypeUtilities.BinAResultToWorld(new ColliderDistanceResultInternal
            {
                distance     = gjkResult.distance,
                hitpointA    = gjkResult.hitpointOnAInASpace,
                hitpointB    = gjkResult.hitpointOnBInASpace,
                normalA      = PointRayBox.BoxNormalFromFeatureCode(featureCodeA),
                normalB      = math.rotate(bInATransform.rot, PointRayBox.BoxNormalFromFeatureCode(featureCodeB)),
                featureCodeA = featureCodeA,
                featureCodeB = featureCodeB
            }, aTransform);
            return result.distance <= maxDistance;
            //var aWorldToLocal      = math.inverse(aTransform);
            //var bWorldToLocal      = math.inverse(bTransform);
            //var bInASpaceTransform = math.mul(aWorldToLocal, bTransform);
            //var aInBSpaceTransform = math.mul(bWorldToLocal, aTransform);
            //var hit                = BoxBoxDistance(in boxA,
            //                                        in boxB,
            //                                        in bInASpaceTransform,
            //                                        in aInBSpaceTransform,
            //                                        maxDistance,
            //                                        out ColliderDistanceResultInternal localResult);
            //result = InternalQueryTypeUtilities.BinAResultToWorld(in localResult, in aTransform);
            //return hit;
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
            UnitySim.ContactsBetweenResult result = default;

            var bInATransform = math.mul(math.inverse(aTransform), bTransform);

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
                            result.Add(math.transform(aTransform, clippedSegmentB + aLocalContactNormal * bDistance), bDistance);
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
                        result.Add(math.transform(aTransform, vertex), distance);
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

        private const float k_boxBoxEpsilon = math.EPSILON;

        private const float k_boxBoxAccuracy = 2e-5f;  // Record: 1.120567e-5f

        private static bool BoxBoxDistance(in BoxCollider boxA,
                                           in BoxCollider boxB,
                                           in RigidTransform bInASpace,
                                           in RigidTransform aInBSpace,
                                           float maxDistance,
                                           out ColliderDistanceResultInternal result)
        {
            //Step 1: Points vs faces
            simdFloat3 bTopPoints     = default;
            simdFloat3 bBottomPoints  = default;
            bTopPoints.x              = math.select(-boxB.halfSize.x, boxB.halfSize.x, new bool4(true, true, false, false));
            bBottomPoints.x           = bTopPoints.x;
            bBottomPoints.y           = -boxB.halfSize.y;
            bTopPoints.y              = boxB.halfSize.y;
            bTopPoints.z              = math.select(-boxB.halfSize.z, boxB.halfSize.z, new bool4(true, false, true, false));
            bBottomPoints.z           = bTopPoints.z;
            bTopPoints               += boxB.center;
            bBottomPoints            += boxB.center;
            var bTopPointsInAOS       = simd.transform(bInASpace, bTopPoints) - boxA.center;  //OS = origin space
            var bBottomPointsInAOS    = simd.transform(bInASpace, bBottomPoints) - boxA.center;

            OriginAabb8PointsWithEspilonFudge(boxA.halfSize,
                                              in bTopPointsInAOS,
                                              in bBottomPointsInAOS,
                                              out float3 pointsClosestAInA,
                                              out float3 pointsClosestBInA,
                                              out float pointsAxisDistanceInA);
            bool4 bTopMatch           = bTopPointsInAOS == pointsClosestBInA;
            bool4 bBottomMatch        = bBottomPointsInAOS == pointsClosestBInA;
            int   bInABIndex          = math.tzcnt((math.bitmask(bBottomMatch) << 4) | math.bitmask(bTopMatch));
            float pointsDistanceSqInA = math.distancesq(pointsClosestAInA, pointsClosestBInA);
            bool  isInvalidInA        = (pointsAxisDistanceInA < 0f) & math.distance(pointsAxisDistanceInA * pointsAxisDistanceInA, pointsDistanceSqInA) > k_boxBoxEpsilon;
            pointsAxisDistanceInA     = math.select(pointsAxisDistanceInA, float.MinValue, isInvalidInA);
            pointsDistanceSqInA       = math.select(pointsDistanceSqInA, float.MaxValue, isInvalidInA);

            simdFloat3 aTopPoints     = default;
            simdFloat3 aBottomPoints  = default;
            aTopPoints.x              = math.select(-boxA.halfSize.x, boxA.halfSize.x, new bool4(true, true, false, false));
            aBottomPoints.x           = aTopPoints.x;
            aBottomPoints.y           = -boxA.halfSize.y;
            aTopPoints.y              = boxA.halfSize.y;
            aTopPoints.z              = math.select(-boxA.halfSize.z, boxA.halfSize.z, new bool4(true, false, true, false));
            aBottomPoints.z           = aTopPoints.z;
            aTopPoints               += boxA.center;
            aBottomPoints            += boxA.center;
            var aTopPointsInBOS       = simd.transform(aInBSpace, aTopPoints) - boxB.center;
            var aBottomPointsInBOS    = simd.transform(aInBSpace, aBottomPoints) - boxB.center;

            OriginAabb8PointsWithEspilonFudge(boxB.halfSize,
                                              in aTopPointsInBOS,
                                              in aBottomPointsInBOS,
                                              out float3 pointsClosestBInB,
                                              out float3 pointsClosestAInB,
                                              out float pointsAxisDistanceInB);
            bool4 aTopMatch           = aTopPointsInBOS == pointsClosestAInB;
            bool4 aBottomMatch        = aBottomPointsInBOS == pointsClosestAInB;
            int   aInBAIndex          = math.tzcnt((math.bitmask(aBottomMatch) << 4) | math.bitmask(aTopMatch));
            float pointsDistanceSqInB = math.distancesq(pointsClosestAInB, pointsClosestBInB);
            bool  isInvalidInB        = (pointsAxisDistanceInB < 0f) & math.distance(pointsAxisDistanceInB * pointsAxisDistanceInB, pointsDistanceSqInB) > k_boxBoxEpsilon;
            pointsAxisDistanceInB     = math.select(pointsAxisDistanceInB, float.MinValue, isInvalidInB);
            pointsDistanceSqInB       = math.select(pointsDistanceSqInB, float.MaxValue, isInvalidInB);

            //Step 2: Edges vs edges

            //For any pair of normals, if the normals are colinear, then there must also exist a point-face pair that is equidistant.
            //However, for a pair of normals, up to two edges from each box can be valid.
            //For box A, assemble the points and edges procedurally using the box dimensions.
            //For box B, use a simd dot product and mask it against the best result. The first 1 index and last 1 index are taken. In most cases, these are the same, which is fine.
            //It is also worth noting that unlike a true SAT, directionality matters here, so we want to find the separating axis directionally oriented from a to b to get the correct closest features.
            //That's the max dot for a and the min dot for b.
            float3     bCenterInASpace  = math.transform(bInASpace, boxB.center) - boxA.center;
            simdFloat3 faceNormalsBoxA  = new simdFloat3(new float3(1f, 0f, 0f), new float3(0f, 1f, 0f), new float3(0f, 0f, 1f), new float3(1f, 0f, 0f));
            simdFloat3 faceNormalsBoxB  = simd.mul(bInASpace.rot, faceNormalsBoxA);
            simdFloat3 edgeAxes03       = simd.cross(faceNormalsBoxA.aaab, faceNormalsBoxB);  //normalsB is already .abca
            simdFloat3 edgeAxes47       = simd.cross(faceNormalsBoxA.bbcc, faceNormalsBoxB.bcab);
            float3     edgeAxes8        = math.cross(faceNormalsBoxA.c, faceNormalsBoxB.c);
            edgeAxes03                  = simd.select(-edgeAxes03, edgeAxes03, simd.dot(edgeAxes03, bCenterInASpace) >= 0f);
            edgeAxes47                  = simd.select(-edgeAxes47, edgeAxes47, simd.dot(edgeAxes47, bCenterInASpace) >= 0f);
            edgeAxes8                   = math.select(-edgeAxes8, edgeAxes8, math.dot(edgeAxes8, bCenterInASpace) >= 0f);
            bool4      edgeInvalids03   = edgeAxes03 == 0f;
            bool4      edgeInvalids47   = edgeAxes47 == 0f;
            bool       edgeInvalids8    = edgeAxes8.Equals(float3.zero);
            simdFloat3 bLeftPointsInAOS = simd.shuffle(bTopPointsInAOS,
                                                       bBottomPointsInAOS,
                                                       math.ShuffleComponent.LeftZ,
                                                       math.ShuffleComponent.LeftW,
                                                       math.ShuffleComponent.RightZ,
                                                       math.ShuffleComponent.RightW);
            simdFloat3 bRightPointsInAOS = simd.shuffle(bTopPointsInAOS,
                                                        bBottomPointsInAOS,
                                                        math.ShuffleComponent.LeftX,
                                                        math.ShuffleComponent.LeftY,
                                                        math.ShuffleComponent.RightX,
                                                        math.ShuffleComponent.RightY);
            simdFloat3 bFrontPointsInAOS = simd.shuffle(bTopPointsInAOS,
                                                        bBottomPointsInAOS,
                                                        math.ShuffleComponent.LeftY,
                                                        math.ShuffleComponent.LeftW,
                                                        math.ShuffleComponent.RightY,
                                                        math.ShuffleComponent.RightW);
            simdFloat3 bBackPointsInAOS = simd.shuffle(bTopPointsInAOS,
                                                       bBottomPointsInAOS,
                                                       math.ShuffleComponent.LeftX,
                                                       math.ShuffleComponent.LeftZ,
                                                       math.ShuffleComponent.RightX,
                                                       math.ShuffleComponent.RightZ);
            simdFloat3 bNormalsX = new simdFloat3(new float3(0f, math.SQRT2 / 2f, math.SQRT2 / 2f),
                                                  new float3(0f, math.SQRT2 / 2f, -math.SQRT2 / 2f),
                                                  new float3(0f, -math.SQRT2 / 2f, math.SQRT2 / 2f),
                                                  new float3(0f, -math.SQRT2 / 2f, -math.SQRT2 / 2f));
            simdFloat3 bNormalsY = new simdFloat3(new float3(math.SQRT2 / 2f, 0f, math.SQRT2 / 2f),
                                                  new float3(math.SQRT2 / 2f, 0f, -math.SQRT2 / 2f),
                                                  new float3(-math.SQRT2 / 2f, 0f, math.SQRT2 / 2f),
                                                  new float3(-math.SQRT2 / 2f, 0f, -math.SQRT2 / 2f));
            simdFloat3 bNormalsZ = new simdFloat3(new float3(math.SQRT2 / 2f, math.SQRT2 / 2f, 0f),
                                                  new float3(-math.SQRT2 / 2f, math.SQRT2 / 2f, 0f),
                                                  new float3(math.SQRT2 / 2f, -math.SQRT2 / 2f, 0f),
                                                  new float3(-math.SQRT2 / 2f, -math.SQRT2 / 2f, 0f));
            float3 bCenterPlaneDistancesInA = simd.dot(bCenterInASpace, faceNormalsBoxB).xyz;

            //x vs x
            float3 axisXX        = edgeAxes03.a;
            float3 aXX           = math.select(-boxA.halfSize, boxA.halfSize, axisXX > 0f);
            float3 aExtraXX      = math.select(aXX, boxA.halfSize, axisXX == 0f);
            aExtraXX.x           = -boxA.halfSize.x;
            simdFloat3 aPointsXX = new simdFloat3(aXX, aXX, aExtraXX, aExtraXX);
            simdFloat3 aEdgesXX  = new simdFloat3(new float3(2f * boxA.halfSize.x, 0f, 0f));

            var                   dotsXX        = simd.dot(bLeftPointsInAOS, axisXX);
            float                 bestDotXX     = math.cmin(dotsXX);
            int                   dotsMaskXX    = math.bitmask(dotsXX == bestDotXX);
            math.ShuffleComponent bIndexXX      = (math.ShuffleComponent)math.clamp(math.tzcnt(dotsMaskXX), 0, 3);
            math.ShuffleComponent bExtraIndexXX = (math.ShuffleComponent)math.clamp(3 - (math.lzcnt(dotsMaskXX) - 28), 0, 3);
            simdFloat3            bPointsXX     = simd.shuffle(bLeftPointsInAOS, bLeftPointsInAOS, bIndexXX, bExtraIndexXX, bIndexXX, bExtraIndexXX);
            simdFloat3            bEdgesXX      = simd.shuffle(bRightPointsInAOS, bRightPointsInAOS, bIndexXX, bExtraIndexXX, bIndexXX, bExtraIndexXX) - bPointsXX;
            simdFloat3            bNormalsXX    = simd.shuffle(bNormalsX, bNormalsX, bIndexXX, bExtraIndexXX, bIndexXX, bExtraIndexXX);
            var                   isInvalidXX   = !CapsuleCapsule.SegmentSegmentInvalidateEndpoints(aPointsXX,
                                                                                  aEdgesXX,
                                                                                  bPointsXX,
                                                                                  bEdgesXX,
                                                                                  out simdFloat3 closestAsXX,
                                                                                  out simdFloat3 closestBsXX);
            var projectionXX             = simd.project(closestBsXX - closestAsXX, axisXX);
            isInvalidXX                 |= edgeInvalids03.x | !simd.isfiniteallxyz(projectionXX);
            var signedAxisDistancesSqXX  = simd.lengthsq(projectionXX) * math.sign(simd.dot(projectionXX, axisXX));
            var distancesSqXX            = simd.distancesq(closestAsXX, closestBsXX);
            isInvalidXX                 |= (signedAxisDistancesSqXX < 0f) & math.distance(math.abs(signedAxisDistancesSqXX), distancesSqXX) > k_boxBoxEpsilon;
            signedAxisDistancesSqXX      = math.select(signedAxisDistancesSqXX, float.MinValue, isInvalidXX);
            distancesSqXX                = math.select(distancesSqXX, float.MaxValue, isInvalidXX);

            //x vs y
            float3 axisXY        = edgeAxes03.b;
            float3 aXY           = math.select(-boxA.halfSize, boxA.halfSize, axisXY > 0f);
            float3 aExtraXY      = math.select(aXY, boxA.halfSize, axisXY == 0f);
            aExtraXY.x           = -boxA.halfSize.x;
            simdFloat3 aPointsXY = new simdFloat3(aXY, aXY, aExtraXY, aExtraXY);
            simdFloat3 aEdgesXY  = new simdFloat3(new float3(2f * boxA.halfSize.x, 0f, 0f));

            var                   dotsXY        = simd.dot(bBottomPointsInAOS, axisXY);
            float                 bestDotXY     = math.cmin(dotsXY);
            int                   dotsMaskXY    = math.bitmask(dotsXY == bestDotXY);
            math.ShuffleComponent bIndexXY      = (math.ShuffleComponent)math.clamp(math.tzcnt(dotsMaskXY), 0, 3);
            math.ShuffleComponent bExtraIndexXY = (math.ShuffleComponent)math.clamp(3 - (math.lzcnt(dotsMaskXY) - 28), 0, 3);
            simdFloat3            bPointsXY     = simd.shuffle(bBottomPointsInAOS, bBottomPointsInAOS, bIndexXY, bExtraIndexXY, bIndexXY, bExtraIndexXY);
            simdFloat3            bEdgesXY      = simd.shuffle(bTopPointsInAOS, bTopPointsInAOS, bIndexXY, bExtraIndexXY, bIndexXY, bExtraIndexXY) - bPointsXY;
            simdFloat3            bNormalsXY    = simd.shuffle(bNormalsY, bNormalsY, bIndexXY, bExtraIndexXY, bIndexXY, bExtraIndexXY);
            var                   isInvalidXY   = !CapsuleCapsule.SegmentSegmentInvalidateEndpoints(aPointsXY,
                                                                                  aEdgesXY,
                                                                                  bPointsXY,
                                                                                  bEdgesXY,
                                                                                  out simdFloat3 closestAsXY,
                                                                                  out simdFloat3 closestBsXY);
            var projectionXY             = simd.project(closestBsXY - closestAsXY, axisXY);
            isInvalidXY                 |= edgeInvalids03.y | !simd.isfiniteallxyz(projectionXY);
            var signedAxisDistancesSqXY  = simd.lengthsq(projectionXY) * math.sign(simd.dot(projectionXY, axisXY));
            var distancesSqXY            = simd.distancesq(closestAsXY, closestBsXY);
            isInvalidXY                 |= (signedAxisDistancesSqXY < 0f) & math.distance(math.abs(signedAxisDistancesSqXY), distancesSqXY) > k_boxBoxEpsilon;
            signedAxisDistancesSqXY      = math.select(signedAxisDistancesSqXY, float.MinValue, isInvalidXY);
            distancesSqXY                = math.select(distancesSqXY, float.MaxValue, isInvalidXY);

            //x vs z
            float3 axisXZ        = edgeAxes03.c;
            float3 aXZ           = math.select(-boxA.halfSize, boxA.halfSize, axisXZ > 0f);
            float3 aExtraXZ      = math.select(aXZ, boxA.halfSize, axisXZ == 0f);
            aExtraXZ.x           = -boxA.halfSize.x;
            simdFloat3 aPointsXZ = new simdFloat3(aXZ, aXZ, aExtraXZ, aExtraXZ);
            simdFloat3 aEdgesXZ  = new simdFloat3(new float3(2f * boxA.halfSize.x, 0f, 0f));

            var                   dotsXZ        = simd.dot(bFrontPointsInAOS, axisXZ);
            float                 bestDotXZ     = math.cmin(dotsXZ);
            int                   dotsMaskXZ    = math.bitmask(dotsXZ == bestDotXZ);
            math.ShuffleComponent bIndexXZ      = (math.ShuffleComponent)math.clamp(math.tzcnt(dotsMaskXZ), 0, 3);
            math.ShuffleComponent bExtraIndexXZ = (math.ShuffleComponent)math.clamp(3 - (math.lzcnt(dotsMaskXZ) - 28), 0, 3);
            simdFloat3            bPointsXZ     = simd.shuffle(bFrontPointsInAOS, bFrontPointsInAOS, bIndexXZ, bExtraIndexXZ, bIndexXZ, bExtraIndexXZ);
            simdFloat3            bEdgesXZ      = simd.shuffle(bBackPointsInAOS, bBackPointsInAOS, bIndexXZ, bExtraIndexXZ, bIndexXZ, bExtraIndexXZ) - bPointsXZ;
            simdFloat3            bNormalsXZ    = simd.shuffle(bNormalsZ, bNormalsZ, bIndexXZ, bExtraIndexXZ, bIndexXZ, bExtraIndexXZ);
            var                   isInvalidXZ   = !CapsuleCapsule.SegmentSegmentInvalidateEndpoints(aPointsXZ,
                                                                                  aEdgesXZ,
                                                                                  bPointsXZ,
                                                                                  bEdgesXZ,
                                                                                  out simdFloat3 closestAsXZ,
                                                                                  out simdFloat3 closestBsXZ);
            var projectionXZ             = simd.project(closestBsXZ - closestAsXZ, axisXZ);
            isInvalidXZ                 |= edgeInvalids03.z | !simd.isfiniteallxyz(projectionXZ);
            var signedAxisDistancesSqXZ  = simd.lengthsq(projectionXZ) * math.sign(simd.dot(projectionXZ, axisXZ));
            var distancesSqXZ            = simd.distancesq(closestAsXZ, closestBsXZ);
            isInvalidXZ                 |= (signedAxisDistancesSqXZ < 0f) & math.distance(math.abs(signedAxisDistancesSqXZ), distancesSqXZ) > k_boxBoxEpsilon;
            signedAxisDistancesSqXZ      = math.select(signedAxisDistancesSqXZ, float.MinValue, isInvalidXZ);
            distancesSqXZ                = math.select(distancesSqXZ, float.MaxValue, isInvalidXZ);

            //y
            //y vs x
            float3 axisYX        = edgeAxes03.d;
            float3 aYX           = math.select(-boxA.halfSize, boxA.halfSize, axisYX > 0f);
            float3 aExtraYX      = math.select(aYX, boxA.halfSize, axisYX == 0f);
            aExtraYX.y           = -boxA.halfSize.y;
            simdFloat3 aPointsYX = new simdFloat3(aYX, aYX, aExtraYX, aExtraYX);
            simdFloat3 aEdgesYX  = new simdFloat3(new float3(0f, 2f * boxA.halfSize.y, 0f));

            var                   dotsYX        = simd.dot(bLeftPointsInAOS, axisYX);
            float                 bestDotYX     = math.cmin(dotsYX);
            int                   dotsMaskYX    = math.bitmask(dotsYX == bestDotYX);
            math.ShuffleComponent bIndexYX      = (math.ShuffleComponent)math.clamp(math.tzcnt(dotsMaskYX), 0, 3);
            math.ShuffleComponent bExtraIndexYX = (math.ShuffleComponent)math.clamp(3 - (math.lzcnt(dotsMaskYX) - 28), 0, 3);
            simdFloat3            bPointsYX     = simd.shuffle(bLeftPointsInAOS, bLeftPointsInAOS, bIndexYX, bExtraIndexYX, bIndexYX, bExtraIndexYX);
            simdFloat3            bEdgesYX      = simd.shuffle(bRightPointsInAOS, bRightPointsInAOS, bIndexYX, bExtraIndexYX, bIndexYX, bExtraIndexYX) - bPointsYX;
            simdFloat3            bNormalsYX    = simd.shuffle(bNormalsX, bNormalsX, bIndexYX, bExtraIndexYX, bIndexYX, bExtraIndexYX);
            var                   isInvalidYX   = !CapsuleCapsule.SegmentSegmentInvalidateEndpoints(aPointsYX,
                                                                                  aEdgesYX,
                                                                                  bPointsYX,
                                                                                  bEdgesYX,
                                                                                  out simdFloat3 closestAsYX,
                                                                                  out simdFloat3 closestBsYX);
            var projectionYX             = simd.project(closestBsYX - closestAsYX, axisYX);
            isInvalidYX                 |= edgeInvalids03.w | !simd.isfiniteallxyz(projectionYX);
            var signedAxisDistancesSqYX  = simd.lengthsq(projectionYX) * math.sign(simd.dot(projectionYX, axisYX));
            var distancesSqYX            = simd.distancesq(closestAsYX, closestBsYX);
            isInvalidYX                 |= (signedAxisDistancesSqYX < 0f) & math.distance(math.abs(signedAxisDistancesSqYX), distancesSqYX) > k_boxBoxEpsilon;
            signedAxisDistancesSqYX      = math.select(signedAxisDistancesSqYX, float.MinValue, isInvalidYX);
            distancesSqYX                = math.select(distancesSqYX, float.MaxValue, isInvalidYX);

            //y vs y
            float3 axisYY        = edgeAxes47.a;
            float3 aYY           = math.select(-boxA.halfSize, boxA.halfSize, axisYY > 0f);
            float3 aExtraYY      = math.select(aYY, boxA.halfSize, axisYY == 0f);
            aExtraYY.y           = -boxA.halfSize.y;
            simdFloat3 aPointsYY = new simdFloat3(aYY, aYY, aExtraYY, aExtraYY);
            simdFloat3 aEdgesYY  = new simdFloat3(new float3(0f, 2f * boxA.halfSize.y, 0f));

            var                   dotsYY        = simd.dot(bBottomPointsInAOS, axisYY);
            float                 bestDotYY     = math.cmin(dotsYY);
            int                   dotsMaskYY    = math.bitmask(dotsYY == bestDotYY);
            math.ShuffleComponent bIndexYY      = (math.ShuffleComponent)math.clamp(math.tzcnt(dotsMaskYY), 0, 3);
            math.ShuffleComponent bExtraIndexYY = (math.ShuffleComponent)math.clamp(3 - (math.lzcnt(dotsMaskYY) - 28), 0, 3);
            simdFloat3            bPointsYY     = simd.shuffle(bBottomPointsInAOS, bBottomPointsInAOS, bIndexYY, bExtraIndexYY, bIndexYY, bExtraIndexYY);
            simdFloat3            bEdgesYY      = simd.shuffle(bTopPointsInAOS, bTopPointsInAOS, bIndexYY, bExtraIndexYY, bIndexYY, bExtraIndexYY) - bPointsYY;
            simdFloat3            bNormalsYY    = simd.shuffle(bNormalsY, bNormalsY, bIndexYY, bExtraIndexYY, bIndexYY, bExtraIndexYY);
            var                   isInvalidYY   = !CapsuleCapsule.SegmentSegmentInvalidateEndpoints(aPointsYY,
                                                                                  aEdgesYY,
                                                                                  bPointsYY,
                                                                                  bEdgesYY,
                                                                                  out simdFloat3 closestAsYY,
                                                                                  out simdFloat3 closestBsYY);
            var projectionYY             = simd.project(closestBsYY - closestAsYY, axisYY);
            isInvalidYY                 |= edgeInvalids47.x | !simd.isfiniteallxyz(projectionYY);
            var signedAxisDistancesSqYY  = simd.lengthsq(projectionYY) * math.sign(simd.dot(projectionYY, axisYY));
            var distancesSqYY            = simd.distancesq(closestAsYY, closestBsYY);
            isInvalidYY                 |= (signedAxisDistancesSqYY < 0f) & math.distance(math.abs(signedAxisDistancesSqYY), distancesSqYY) > k_boxBoxEpsilon;
            signedAxisDistancesSqYY      = math.select(signedAxisDistancesSqYY, float.MinValue, isInvalidYY);
            distancesSqYY                = math.select(distancesSqYY, float.MaxValue, isInvalidYY);

            //y vs z
            float3 axisYZ        = edgeAxes47.b;
            float3 aYZ           = math.select(-boxA.halfSize, boxA.halfSize, axisYZ > 0f);
            float3 aExtraYZ      = math.select(aYZ, boxA.halfSize, axisYZ == 0f);
            aExtraYZ.y           = -boxA.halfSize.y;
            simdFloat3 aPointsYZ = new simdFloat3(aYZ, aYZ, aExtraYZ, aExtraYZ);
            simdFloat3 aEdgesYZ  = new simdFloat3(new float3(0f, 2f * boxA.halfSize.y, 0f));

            var                   dotsYZ        = simd.dot(bFrontPointsInAOS, axisYZ);
            float                 bestDotYZ     = math.cmin(dotsYZ);
            int                   dotsMaskYZ    = math.bitmask(dotsYZ == bestDotYZ);
            math.ShuffleComponent bIndexYZ      = (math.ShuffleComponent)math.clamp(math.tzcnt(dotsMaskYZ), 0, 3);
            math.ShuffleComponent bExtraIndexYZ = (math.ShuffleComponent)math.clamp(3 - (math.lzcnt(dotsMaskYZ) - 28), 0, 3);
            simdFloat3            bPointsYZ     = simd.shuffle(bFrontPointsInAOS, bFrontPointsInAOS, bIndexYZ, bExtraIndexYZ, bIndexYZ, bExtraIndexYZ);
            simdFloat3            bEdgesYZ      = simd.shuffle(bBackPointsInAOS, bBackPointsInAOS, bIndexYZ, bExtraIndexYZ, bIndexYZ, bExtraIndexYZ) - bPointsYZ;
            simdFloat3            bNormalsYZ    = simd.shuffle(bNormalsZ, bNormalsZ, bIndexYZ, bExtraIndexYZ, bIndexYZ, bExtraIndexYZ);
            var                   isInvalidYZ   = !CapsuleCapsule.SegmentSegmentInvalidateEndpoints(aPointsYZ,
                                                                                  aEdgesYZ,
                                                                                  bPointsYZ,
                                                                                  bEdgesYZ,
                                                                                  out simdFloat3 closestAsYZ,
                                                                                  out simdFloat3 closestBsYZ);
            var projectionYZ             = simd.project(closestBsYZ - closestAsYZ, axisYZ);
            isInvalidYZ                 |= edgeInvalids47.y | !simd.isfiniteallxyz(projectionYZ);
            var signedAxisDistancesSqYZ  = simd.lengthsq(projectionYZ) * math.sign(simd.dot(projectionYZ, axisYZ));
            var distancesSqYZ            = simd.distancesq(closestAsYZ, closestBsYZ);
            isInvalidYZ                 |= (signedAxisDistancesSqYZ < 0f) & math.distance(math.abs(signedAxisDistancesSqYZ), distancesSqYZ) > k_boxBoxEpsilon;
            signedAxisDistancesSqYZ      = math.select(signedAxisDistancesSqYZ, float.MinValue, isInvalidYZ);
            distancesSqYZ                = math.select(distancesSqYZ, float.MaxValue, isInvalidYZ);

            //z
            //z vs x
            float3 axisZX        = edgeAxes47.c;
            float3 aZX           = math.select(-boxA.halfSize, boxA.halfSize, axisZX > 0f);
            float3 aExtraZX      = math.select(aZX, boxA.halfSize, axisZX == 0f);
            aExtraZX.z           = -boxA.halfSize.z;
            simdFloat3 aPointsZX = new simdFloat3(aZX, aZX, aExtraZX, aExtraZX);
            simdFloat3 aEdgesZX  = new simdFloat3(new float3(0f, 0f, 2f * boxA.halfSize.z));

            var                   dotsZX        = simd.dot(bLeftPointsInAOS, axisZX);
            float                 bestDotZX     = math.cmin(dotsZX);
            int                   dotsMaskZX    = math.bitmask(dotsZX == bestDotZX);
            math.ShuffleComponent bIndexZX      = (math.ShuffleComponent)math.clamp(math.tzcnt(dotsMaskZX), 0, 3);
            math.ShuffleComponent bExtraIndexZX = (math.ShuffleComponent)math.clamp(3 - (math.lzcnt(dotsMaskZX) - 28), 0, 3);
            simdFloat3            bPointsZX     = simd.shuffle(bLeftPointsInAOS, bLeftPointsInAOS, bIndexZX, bExtraIndexZX, bIndexZX, bExtraIndexZX);
            simdFloat3            bEdgesZX      = simd.shuffle(bRightPointsInAOS, bRightPointsInAOS, bIndexZX, bExtraIndexZX, bIndexZX, bExtraIndexZX) - bPointsZX;
            simdFloat3            bNormalsZX    = simd.shuffle(bNormalsX, bNormalsX, bIndexZX, bExtraIndexZX, bIndexZX, bExtraIndexZX);
            var                   isInvalidZX   = !CapsuleCapsule.SegmentSegmentInvalidateEndpoints(aPointsZX,
                                                                                  aEdgesZX,
                                                                                  bPointsZX,
                                                                                  bEdgesZX,
                                                                                  out simdFloat3 closestAsZX,
                                                                                  out simdFloat3 closestBsZX);
            var projectionZX             = simd.project(closestBsZX - closestAsZX, axisZX);
            isInvalidZX                 |= edgeInvalids47.z | !simd.isfiniteallxyz(projectionZX);
            var signedAxisDistancesSqZX  = simd.lengthsq(projectionZX) * math.sign(simd.dot(projectionZX, axisZX));
            var distancesSqZX            = simd.distancesq(closestAsZX, closestBsZX);
            isInvalidZX                 |= (signedAxisDistancesSqZX < 0f) & math.distance(math.abs(signedAxisDistancesSqZX), distancesSqZX) > k_boxBoxEpsilon;
            signedAxisDistancesSqZX      = math.select(signedAxisDistancesSqZX, float.MinValue, isInvalidZX);
            distancesSqZX                = math.select(distancesSqZX, float.MaxValue, isInvalidZX);

            //z vs y
            float3 axisZY        = edgeAxes47.d;
            float3 aZY           = math.select(-boxA.halfSize, boxA.halfSize, axisZY > 0f);
            float3 aExtraZY      = math.select(aZY, boxA.halfSize, axisZY == 0f);
            aExtraZY.z           = -boxA.halfSize.z;
            simdFloat3 aPointsZY = new simdFloat3(aZY, aZY, aExtraZY, aExtraZY);
            simdFloat3 aEdgesZY  = new simdFloat3(new float3(0f, 0f, 2f * boxA.halfSize.z));

            var                   dotsZY        = simd.dot(bBottomPointsInAOS, axisZY);
            float                 bestDotZY     = math.cmin(dotsZY);
            int                   dotsMaskZY    = math.bitmask(dotsZY == bestDotZY);
            math.ShuffleComponent bIndexZY      = (math.ShuffleComponent)math.clamp(math.tzcnt(dotsMaskZY), 0, 3);
            math.ShuffleComponent bExtraIndexZY = (math.ShuffleComponent)math.clamp(3 - (math.lzcnt(dotsMaskZY) - 28), 0, 3);
            simdFloat3            bPointsZY     = simd.shuffle(bBottomPointsInAOS, bBottomPointsInAOS, bIndexZY, bExtraIndexZY, bIndexZY, bExtraIndexZY);
            simdFloat3            bEdgesZY      = simd.shuffle(bTopPointsInAOS, bTopPointsInAOS, bIndexZY, bExtraIndexZY, bIndexZY, bExtraIndexZY) - bPointsZY;
            simdFloat3            bNormalsZY    = simd.shuffle(bNormalsY, bNormalsY, bIndexZY, bExtraIndexZY, bIndexZY, bExtraIndexZY);
            var                   isInvalidZY   = !CapsuleCapsule.SegmentSegmentInvalidateEndpoints(aPointsZY,
                                                                                  aEdgesZY,
                                                                                  bPointsZY,
                                                                                  bEdgesZY,
                                                                                  out simdFloat3 closestAsZY,
                                                                                  out simdFloat3 closestBsZY);
            var projectionZY             = simd.project(closestBsZY - closestAsZY, axisZY);
            isInvalidZY                 |= edgeInvalids47.w | !simd.isfiniteallxyz(projectionZY);
            var signedAxisDistancesSqZY  = simd.lengthsq(projectionZY) * math.sign(simd.dot(projectionZY, axisZY));
            var distancesSqZY            = simd.distancesq(closestAsZY, closestBsZY);
            isInvalidZY                 |= (signedAxisDistancesSqZY < 0f) & math.distance(math.abs(signedAxisDistancesSqZY), distancesSqZY) > k_boxBoxEpsilon;
            signedAxisDistancesSqZY      = math.select(signedAxisDistancesSqZY, float.MinValue, isInvalidZY);
            distancesSqZY                = math.select(distancesSqZY, float.MaxValue, isInvalidZY);

            //z vs z
            float3 axisZZ        = edgeAxes8;
            float3 aZZ           = math.select(-boxA.halfSize, boxA.halfSize, axisZZ > 0f);
            float3 aExtraZZ      = math.select(aZZ, boxA.halfSize, axisZZ == 0f);
            aExtraZZ.z           = -boxA.halfSize.z;
            simdFloat3 aPointsZZ = new simdFloat3(aZZ, aZZ, aExtraZZ, aExtraZZ);
            simdFloat3 aEdgesZZ  = new simdFloat3(new float3(0f, 0f, 2f * boxA.halfSize.z));

            var                   dotsZZ        = simd.dot(bFrontPointsInAOS, axisZZ);
            float                 bestDotZZ     = math.cmin(dotsZZ);
            int                   dotsMaskZZ    = math.bitmask(dotsZZ == bestDotZZ);
            math.ShuffleComponent bIndexZZ      = (math.ShuffleComponent)math.clamp(math.tzcnt(dotsMaskZZ), 0, 3);
            math.ShuffleComponent bExtraIndexZZ = (math.ShuffleComponent)math.clamp(3 - (math.lzcnt(dotsMaskZZ) - 28), 0, 3);
            simdFloat3            bPointsZZ     = simd.shuffle(bFrontPointsInAOS, bFrontPointsInAOS, bIndexZZ, bExtraIndexZZ, bIndexZZ, bExtraIndexZZ);
            simdFloat3            bEdgesZZ      = simd.shuffle(bBackPointsInAOS, bBackPointsInAOS, bIndexZZ, bExtraIndexZZ, bIndexZZ, bExtraIndexZZ) - bPointsZZ;
            simdFloat3            bNormalsZZ    = simd.shuffle(bNormalsZ, bNormalsZ, bIndexZZ, bExtraIndexZZ, bIndexZZ, bExtraIndexZZ);
            var                   isInvalidZZ   = !CapsuleCapsule.SegmentSegmentInvalidateEndpoints(aPointsZZ,
                                                                                  aEdgesZZ,
                                                                                  bPointsZZ,
                                                                                  bEdgesZZ,
                                                                                  out simdFloat3 closestAsZZ,
                                                                                  out simdFloat3 closestBsZZ);
            var projectionZZ             = simd.project(closestBsZZ - closestAsZZ, axisZZ);
            isInvalidZZ                 |= edgeInvalids8 | !simd.isfiniteallxyz(projectionZZ);
            var signedAxisDistancesSqZZ  = simd.lengthsq(projectionZZ) * math.sign(simd.dot(projectionZZ, axisZZ));
            var distancesSqZZ            = simd.distancesq(closestAsZZ, closestBsZZ);
            isInvalidZZ                 |= (signedAxisDistancesSqZZ < 0f) & math.distance(math.abs(signedAxisDistancesSqZZ), distancesSqZZ) > k_boxBoxEpsilon;
            signedAxisDistancesSqZZ      = math.select(signedAxisDistancesSqZZ, float.MinValue, isInvalidZZ);
            distancesSqZZ                = math.select(distancesSqZZ, float.MaxValue, isInvalidZZ);

            //Step 3: Find the best result.
            float4     bestEdgeSignedAxisDistancesSq = signedAxisDistancesSqXX;
            float4     bestEdgeDistancesSq           = distancesSqXX;
            simdFloat3 bestEdgeClosestAs             = closestAsXX;
            simdFloat3 bestEdgeClosestBs             = closestBsXX;
            simdFloat3 bestNormalBs                  = bNormalsXX;

            bool4 newEdgeIsBetters = math.select(signedAxisDistancesSqXY > bestEdgeSignedAxisDistancesSq,
                                                 distancesSqXY < bestEdgeDistancesSq,
                                                 signedAxisDistancesSqXY >= 0f & bestEdgeDistancesSq >= 0f);
            bestEdgeSignedAxisDistancesSq = math.select(bestEdgeSignedAxisDistancesSq, signedAxisDistancesSqXY, newEdgeIsBetters);
            bestEdgeDistancesSq           = math.select(bestEdgeDistancesSq, distancesSqXY, newEdgeIsBetters);
            bestEdgeClosestAs             = simd.select(bestEdgeClosestAs, closestAsXY, newEdgeIsBetters);
            bestEdgeClosestBs             = simd.select(bestEdgeClosestBs, closestBsXY, newEdgeIsBetters);
            bestNormalBs                  = simd.select(bestNormalBs, bNormalsXY, newEdgeIsBetters);

            newEdgeIsBetters = math.select(signedAxisDistancesSqXZ > bestEdgeSignedAxisDistancesSq,
                                           distancesSqXZ < bestEdgeDistancesSq,
                                           signedAxisDistancesSqXZ >= 0f & bestEdgeDistancesSq >= 0f);
            bestEdgeSignedAxisDistancesSq = math.select(bestEdgeSignedAxisDistancesSq, signedAxisDistancesSqXZ, newEdgeIsBetters);
            bestEdgeDistancesSq           = math.select(bestEdgeDistancesSq, distancesSqXZ, newEdgeIsBetters);
            bestEdgeClosestAs             = simd.select(bestEdgeClosestAs, closestAsXZ, newEdgeIsBetters);
            bestEdgeClosestBs             = simd.select(bestEdgeClosestBs, closestBsXZ, newEdgeIsBetters);
            bestNormalBs                  = simd.select(bestNormalBs, bNormalsXZ, newEdgeIsBetters);

            newEdgeIsBetters = math.select(signedAxisDistancesSqYX > bestEdgeSignedAxisDistancesSq,
                                           distancesSqYX < bestEdgeDistancesSq,
                                           signedAxisDistancesSqYX >= 0f & bestEdgeDistancesSq >= 0f);
            bestEdgeSignedAxisDistancesSq = math.select(bestEdgeSignedAxisDistancesSq, signedAxisDistancesSqYX, newEdgeIsBetters);
            bestEdgeDistancesSq           = math.select(bestEdgeDistancesSq, distancesSqYX, newEdgeIsBetters);
            bestEdgeClosestAs             = simd.select(bestEdgeClosestAs, closestAsYX, newEdgeIsBetters);
            bestEdgeClosestBs             = simd.select(bestEdgeClosestBs, closestBsYX, newEdgeIsBetters);
            bestNormalBs                  = simd.select(bestNormalBs, bNormalsYX, newEdgeIsBetters);

            newEdgeIsBetters = math.select(signedAxisDistancesSqYY > bestEdgeSignedAxisDistancesSq,
                                           distancesSqYY < bestEdgeDistancesSq,
                                           signedAxisDistancesSqYY >= 0f & bestEdgeDistancesSq >= 0f);
            bestEdgeSignedAxisDistancesSq = math.select(bestEdgeSignedAxisDistancesSq, signedAxisDistancesSqYY, newEdgeIsBetters);
            bestEdgeDistancesSq           = math.select(bestEdgeDistancesSq, distancesSqYY, newEdgeIsBetters);
            bestEdgeClosestAs             = simd.select(bestEdgeClosestAs, closestAsYY, newEdgeIsBetters);
            bestEdgeClosestBs             = simd.select(bestEdgeClosestBs, closestBsYY, newEdgeIsBetters);
            bestNormalBs                  = simd.select(bestNormalBs, bNormalsYY, newEdgeIsBetters);

            newEdgeIsBetters = math.select(signedAxisDistancesSqYZ > bestEdgeSignedAxisDistancesSq,
                                           distancesSqYZ < bestEdgeDistancesSq,
                                           signedAxisDistancesSqYZ >= 0f & bestEdgeDistancesSq >= 0f);
            bestEdgeSignedAxisDistancesSq = math.select(bestEdgeSignedAxisDistancesSq, signedAxisDistancesSqYZ, newEdgeIsBetters);
            bestEdgeDistancesSq           = math.select(bestEdgeDistancesSq, distancesSqYZ, newEdgeIsBetters);
            bestEdgeClosestAs             = simd.select(bestEdgeClosestAs, closestAsYZ, newEdgeIsBetters);
            bestEdgeClosestBs             = simd.select(bestEdgeClosestBs, closestBsYZ, newEdgeIsBetters);
            bestNormalBs                  = simd.select(bestNormalBs, bNormalsYZ, newEdgeIsBetters);

            newEdgeIsBetters = math.select(signedAxisDistancesSqZX > bestEdgeSignedAxisDistancesSq,
                                           distancesSqZX < bestEdgeDistancesSq,
                                           signedAxisDistancesSqZX >= 0f & bestEdgeDistancesSq >= 0f);
            bestEdgeSignedAxisDistancesSq = math.select(bestEdgeSignedAxisDistancesSq, signedAxisDistancesSqZX, newEdgeIsBetters);
            bestEdgeDistancesSq           = math.select(bestEdgeDistancesSq, distancesSqZX, newEdgeIsBetters);
            bestEdgeClosestAs             = simd.select(bestEdgeClosestAs, closestAsZX, newEdgeIsBetters);
            bestEdgeClosestBs             = simd.select(bestEdgeClosestBs, closestBsZX, newEdgeIsBetters);
            bestNormalBs                  = simd.select(bestNormalBs, bNormalsZX, newEdgeIsBetters);

            newEdgeIsBetters = math.select(signedAxisDistancesSqZY > bestEdgeSignedAxisDistancesSq,
                                           distancesSqZY < bestEdgeDistancesSq,
                                           signedAxisDistancesSqZY >= 0f & bestEdgeDistancesSq >= 0f);
            bestEdgeSignedAxisDistancesSq = math.select(bestEdgeSignedAxisDistancesSq, signedAxisDistancesSqZY, newEdgeIsBetters);
            bestEdgeDistancesSq           = math.select(bestEdgeDistancesSq, distancesSqZY, newEdgeIsBetters);
            bestEdgeClosestAs             = simd.select(bestEdgeClosestAs, closestAsZY, newEdgeIsBetters);
            bestEdgeClosestBs             = simd.select(bestEdgeClosestBs, closestBsZY, newEdgeIsBetters);
            bestNormalBs                  = simd.select(bestNormalBs, bNormalsZY, newEdgeIsBetters);

            newEdgeIsBetters = math.select(signedAxisDistancesSqZZ > bestEdgeSignedAxisDistancesSq,
                                           distancesSqZZ < bestEdgeDistancesSq,
                                           signedAxisDistancesSqZZ >= 0f & bestEdgeDistancesSq >= 0f);
            bestEdgeSignedAxisDistancesSq = math.select(bestEdgeSignedAxisDistancesSq, signedAxisDistancesSqZZ, newEdgeIsBetters);
            bestEdgeDistancesSq           = math.select(bestEdgeDistancesSq, distancesSqZZ, newEdgeIsBetters);
            bestEdgeClosestAs             = simd.select(bestEdgeClosestAs, closestAsZZ, newEdgeIsBetters);
            bestEdgeClosestBs             = simd.select(bestEdgeClosestBs, closestBsZZ, newEdgeIsBetters);
            bestNormalBs                  = simd.select(bestNormalBs, bNormalsZZ, newEdgeIsBetters);

            var    absoluteDistanceIsValid    = bestEdgeSignedAxisDistancesSq >= 0f;
            bool   anyAbsoluteDistanceIsValid = math.any(absoluteDistanceIsValid);
            float4 maskedAbsoluteDistances    = math.select(float.MaxValue, bestEdgeSignedAxisDistancesSq, absoluteDistanceIsValid);
            bool4  isTheBestEdge              =
                math.select(bestEdgeSignedAxisDistancesSq == math.cmax(bestEdgeSignedAxisDistancesSq),
                            maskedAbsoluteDistances == math.cmin(maskedAbsoluteDistances),
                            anyAbsoluteDistanceIsValid);
            int    bestEdgeIndex                = math.tzcnt(math.bitmask(isTheBestEdge));
            float  bestEdgeSignedAxisDistanceSq = bestEdgeSignedAxisDistancesSq[bestEdgeIndex];
            float  bestEdgeDistanceSq           = bestEdgeDistancesSq[bestEdgeIndex];
            float3 bestEdgeClosestA             = bestEdgeClosestAs[bestEdgeIndex];
            float3 bestEdgeClosestB             = bestEdgeClosestBs[bestEdgeIndex];
            float3 bestEdgeNormalB              = bestNormalBs[bestEdgeIndex];

            simdFloat3 topUnnormals    = default;
            simdFloat3 bottomUnnormals = default;
            topUnnormals.x             = math.select(-1f, 1f, new bool4(true, true, false, false));
            bottomUnnormals.x          = topUnnormals.x;
            bottomUnnormals.y          = -1f;
            topUnnormals.y             = 1f;
            topUnnormals.z             = math.select(-1f, 1f, new bool4(true, false, true, false));
            bottomUnnormals.z          = topUnnormals.z;

            float3 pointsNormalBFromBInA = simd.shuffle(topUnnormals, bottomUnnormals, (math.ShuffleComponent)bInABIndex);
            float3 pointsNormalAFromAInB = simd.shuffle(topUnnormals, bottomUnnormals, (math.ShuffleComponent)aInBAIndex);
            pointsNormalBFromBInA        = math.normalize(math.rotate(bInASpace, pointsNormalBFromBInA));
            pointsNormalAFromAInB        = math.normalize(pointsNormalAFromAInB);
            float3 pointsNormalBFromAInB = math.select(0f, 1f, pointsClosestBInB == boxB.halfSize) + math.select(0f, -1f, pointsClosestBInB == -boxB.halfSize);
            pointsNormalBFromAInB        = math.normalize(math.rotate(bInASpace, pointsNormalBFromAInB));
            float3 pointsNormalAFromBInA =
                math.normalize(math.select(0f, 1f, pointsClosestAInA == boxA.halfSize) + math.select(0f, -1f, pointsClosestAInA == -boxA.halfSize));
            float3 bestEdgeNormalA             = math.normalize(math.select(0f, 1f, bestEdgeClosestA == boxA.halfSize) + math.select(0f, -1f, bestEdgeClosestA == -boxA.halfSize));
            int    matchedBIndexFromEdgeTop    = math.tzcnt(math.bitmask(bestEdgeClosestB == bTopPointsInAOS));
            int    matchedBIndexFromEdgeBottom = math.tzcnt(math.bitmask((bestEdgeClosestB == bBottomPointsInAOS))) + 4;
            int    matchedIndexBFromEdge       = math.select(matchedBIndexFromEdgeTop, matchedBIndexFromEdgeBottom, matchedBIndexFromEdgeBottom < 8);
            float3 edgeNormalBAsCorner         = simd.shuffle(topUnnormals, bottomUnnormals, (math.ShuffleComponent)math.clamp(matchedIndexBFromEdge, 0, 7));
            edgeNormalBAsCorner                = math.normalize(edgeNormalBAsCorner);
            bestEdgeNormalB                    = math.select(bestEdgeNormalB, edgeNormalBAsCorner, matchedIndexBFromEdge < 8);

            bool bInAIsBetter = math.select(pointsAxisDistanceInA >= pointsAxisDistanceInB,
                                            pointsDistanceSqInA <= pointsDistanceSqInB,
                                            pointsAxisDistanceInA >= 0f && pointsAxisDistanceInB >= 0f);
            float  pointsAxisDistance = math.select(pointsAxisDistanceInB, pointsAxisDistanceInA, bInAIsBetter);
            float  pointsDistanceSq   = math.select(pointsDistanceSqInB, pointsDistanceSqInA, bInAIsBetter);
            float3 pointsClosestA     = math.select(math.transform(bInASpace, pointsClosestAInB + boxB.center), pointsClosestAInA + boxA.center, bInAIsBetter);
            float3 pointsClosestB     = math.select(math.transform(bInASpace, pointsClosestBInB + boxB.center), pointsClosestBInA + boxA.center, bInAIsBetter);
            float3 pointsNormalA      = math.select(pointsNormalAFromAInB, pointsNormalAFromBInA, bInAIsBetter);
            float3 pointsNormalB      = math.select(pointsNormalBFromAInB, pointsNormalBFromBInA, bInAIsBetter);

            bool pointsIsBetter = math.select(pointsAxisDistance * pointsAxisDistance * math.sign(pointsAxisDistance) >= bestEdgeSignedAxisDistanceSq,
                                              pointsDistanceSq <= bestEdgeDistanceSq,
                                              pointsAxisDistance >= 0f && bestEdgeSignedAxisDistanceSq >= 0f);
            float  bestAxisDistance = math.select(math.sign(bestEdgeSignedAxisDistanceSq) * math.sqrt(math.abs(bestEdgeSignedAxisDistanceSq)), pointsAxisDistance, pointsIsBetter);
            float  bestDistanceSq   = math.select(bestEdgeDistanceSq, pointsDistanceSq, pointsIsBetter);
            float3 bestClosestA     = math.select(bestEdgeClosestA + boxA.center, pointsClosestA, pointsIsBetter);
            float3 bestClosestB     = math.select(bestEdgeClosestB + boxA.center, pointsClosestB, pointsIsBetter);
            float3 bestNormalA      = math.select(bestEdgeNormalA, pointsNormalA, pointsIsBetter);
            float3 bestNormalB      = math.select(math.rotate(bInASpace, bestEdgeNormalB), pointsNormalB, pointsIsBetter);

            //Step 4: Build result
            result = new ColliderDistanceResultInternal
            {
                hitpointA    = bestClosestA,
                hitpointB    = bestClosestB,
                normalA      = bestNormalA,
                normalB      = bestNormalB,
                distance     = math.sign(bestAxisDistance) * math.sqrt(math.abs(bestDistanceSq)),
                featureCodeA = PointRayBox.FeatureCodeFromBoxNormal(bestNormalA),
                featureCodeB = PointRayBox.FeatureCodeFromBoxNormal(math.rotate(aInBSpace, bestNormalB))
            };
            return result.distance <= maxDistance;
        }

        private static bool BoxBoxDistanceDebug(in BoxCollider boxA,
                                                in BoxCollider boxB,
                                                in RigidTransform bInASpace,
                                                in RigidTransform aInBSpace,
                                                float maxDistance,
                                                out ColliderDistanceResultInternal result)
        {
            //Step 1: Points vs faces
            simdFloat3 bTopPoints     = default;
            simdFloat3 bBottomPoints  = default;
            bTopPoints.x              = math.select(-boxB.halfSize.x, boxB.halfSize.x, new bool4(true, true, false, false));
            bBottomPoints.x           = bTopPoints.x;
            bBottomPoints.y           = -boxB.halfSize.y;
            bTopPoints.y              = boxB.halfSize.y;
            bTopPoints.z              = math.select(-boxB.halfSize.z, boxB.halfSize.z, new bool4(true, false, true, false));
            bBottomPoints.z           = bTopPoints.z;
            bTopPoints               += boxB.center;
            bBottomPoints            += boxB.center;
            var bTopPointsInAOS       = simd.transform(bInASpace, bTopPoints) - boxA.center;  //OS = origin space
            var bBottomPointsInAOS    = simd.transform(bInASpace, bBottomPoints) - boxA.center;

            OriginAabb8PointsWithEspilonFudgeDebug(boxA.halfSize,
                                                   in bTopPointsInAOS,
                                                   in bBottomPointsInAOS,
                                                   out float3 pointsClosestAInA,
                                                   out float3 pointsClosestBInA,
                                                   out float pointsAxisDistanceInA);
            bool4 bTopMatch           = bTopPointsInAOS == pointsClosestBInA;
            bool4 bBottomMatch        = bBottomPointsInAOS == pointsClosestBInA;
            int   bInABIndex          = math.tzcnt((math.bitmask(bBottomMatch) << 4) | math.bitmask(bTopMatch));
            float pointsDistanceSqInA = math.distancesq(pointsClosestAInA, pointsClosestBInA);
            UnityEngine.Debug.Log($"Points B in A: axisDistance: {pointsAxisDistanceInA}, distanceSq: {pointsDistanceSqInA}, bInABIndex: {bInABIndex}");

            simdFloat3 aTopPoints     = default;
            simdFloat3 aBottomPoints  = default;
            aTopPoints.x              = math.select(-boxA.halfSize.x, boxA.halfSize.x, new bool4(true, true, false, false));
            aBottomPoints.x           = aTopPoints.x;
            aBottomPoints.y           = -boxA.halfSize.y;
            aTopPoints.y              = boxA.halfSize.y;
            aTopPoints.z              = math.select(-boxA.halfSize.z, boxA.halfSize.z, new bool4(true, false, true, false));
            aBottomPoints.z           = aTopPoints.z;
            aTopPoints               += boxA.center;
            aBottomPoints            += boxA.center;
            var aTopPointsInBOS       = simd.transform(aInBSpace, aTopPoints) - boxB.center;
            var aBottomPointsInBOS    = simd.transform(aInBSpace, aBottomPoints) - boxB.center;
            if (math.any(math.isnan(aTopPointsInBOS.x)))
            {
                UnityEngine.Debug.Log(
                    $"Something went wrong transforming to B-space. aTopPoints.a: {aTopPoints.a}, boxB.center: {boxB.center}, aInBSpace: {aInBSpace.rot}, {aInBSpace.pos}");
            }

            OriginAabb8PointsWithEspilonFudgeDebug(boxB.halfSize,
                                                   in aTopPointsInBOS,
                                                   in aBottomPointsInBOS,
                                                   out float3 pointsClosestBInB,
                                                   out float3 pointsClosestAInB,
                                                   out float pointsAxisDistanceInB);
            bool4 aTopMatch           = aTopPointsInBOS == pointsClosestAInB;
            bool4 aBottomMatch        = aBottomPointsInBOS == pointsClosestAInB;
            int   aInBAIndex          = math.tzcnt((math.bitmask(aBottomMatch) << 4) | math.bitmask(aTopMatch));
            float pointsDistanceSqInB = math.distancesq(pointsClosestAInB, pointsClosestBInB);

            UnityEngine.Debug.Log($"Points A in B: axisDistance: {pointsAxisDistanceInB}, distanceSq: {pointsDistanceSqInB}, aInBAIndex: {aInBAIndex}");

            //Step 2: Edges vs edges

            //For any pair of normals, if the normals are colinear, then there must also exist a point-face pair that is equidistant.
            //However, for a pair of normals, up to two edges from each box can be valid.
            //For box A, assemble the points and edges procedurally using the box dimensions.
            //For box B, use a simd dot product and mask it against the best result. The first 1 index and last 1 index are taken. In most cases, these are the same, which is fine.
            //It is also worth noting that unlike a true SAT, directionality matters here, so we want to find the separating axis directionally oriented from a to b to get the correct closest features.
            //That's the max dot for a and the min dot for b.
            float3     bCenterInASpace  = math.transform(bInASpace, boxB.center) - boxA.center;
            simdFloat3 faceNormalsBoxA  = new simdFloat3(new float3(1f, 0f, 0f), new float3(0f, 1f, 0f), new float3(0f, 0f, 1f), new float3(1f, 0f, 0f));
            simdFloat3 faceNormalsBoxB  = simd.mul(bInASpace.rot, faceNormalsBoxA);
            simdFloat3 edgeAxes03       = simd.cross(faceNormalsBoxA.aaab, faceNormalsBoxB);  //normalsB is already .abca
            simdFloat3 edgeAxes47       = simd.cross(faceNormalsBoxA.bbcc, faceNormalsBoxB.bcab);
            float3     edgeAxes8        = math.cross(faceNormalsBoxA.c, faceNormalsBoxB.c);
            edgeAxes03                  = simd.select(-edgeAxes03, edgeAxes03, simd.dot(edgeAxes03, bCenterInASpace) >= 0f);
            edgeAxes47                  = simd.select(-edgeAxes47, edgeAxes47, simd.dot(edgeAxes47, bCenterInASpace) >= 0f);
            edgeAxes8                   = math.select(-edgeAxes8, edgeAxes8, math.dot(edgeAxes8, bCenterInASpace) >= 0f);
            bool4      edgeInvalids03   = edgeAxes03 == 0f;
            bool4      edgeInvalids47   = edgeAxes47 == 0f;
            bool       edgeInvalids8    = edgeAxes8.Equals(float3.zero);
            simdFloat3 bLeftPointsInAOS = simd.shuffle(bTopPointsInAOS,
                                                       bBottomPointsInAOS,
                                                       math.ShuffleComponent.LeftZ,
                                                       math.ShuffleComponent.LeftW,
                                                       math.ShuffleComponent.RightZ,
                                                       math.ShuffleComponent.RightW);
            simdFloat3 bRightPointsInAOS = simd.shuffle(bTopPointsInAOS,
                                                        bBottomPointsInAOS,
                                                        math.ShuffleComponent.LeftX,
                                                        math.ShuffleComponent.LeftY,
                                                        math.ShuffleComponent.RightX,
                                                        math.ShuffleComponent.RightY);
            simdFloat3 bFrontPointsInAOS = simd.shuffle(bTopPointsInAOS,
                                                        bBottomPointsInAOS,
                                                        math.ShuffleComponent.LeftY,
                                                        math.ShuffleComponent.LeftW,
                                                        math.ShuffleComponent.RightY,
                                                        math.ShuffleComponent.RightW);
            simdFloat3 bBackPointsInAOS = simd.shuffle(bTopPointsInAOS,
                                                       bBottomPointsInAOS,
                                                       math.ShuffleComponent.LeftX,
                                                       math.ShuffleComponent.LeftZ,
                                                       math.ShuffleComponent.RightX,
                                                       math.ShuffleComponent.RightZ);
            simdFloat3 bNormalsX = new simdFloat3(new float3(0f, math.SQRT2 / 2f, math.SQRT2 / 2f),
                                                  new float3(0f, math.SQRT2 / 2f, -math.SQRT2 / 2f),
                                                  new float3(0f, -math.SQRT2 / 2f, math.SQRT2 / 2f),
                                                  new float3(0f, -math.SQRT2 / 2f, -math.SQRT2 / 2f));
            simdFloat3 bNormalsY = new simdFloat3(new float3(math.SQRT2 / 2f, 0f, math.SQRT2 / 2f),
                                                  new float3(math.SQRT2 / 2f, 0f, -math.SQRT2 / 2f),
                                                  new float3(-math.SQRT2 / 2f, 0f, math.SQRT2 / 2f),
                                                  new float3(-math.SQRT2 / 2f, 0f, -math.SQRT2 / 2f));
            simdFloat3 bNormalsZ = new simdFloat3(new float3(math.SQRT2 / 2f, math.SQRT2 / 2f, 0f),
                                                  new float3(-math.SQRT2 / 2f, math.SQRT2 / 2f, 0f),
                                                  new float3(math.SQRT2 / 2f, -math.SQRT2 / 2f, 0f),
                                                  new float3(-math.SQRT2 / 2f, -math.SQRT2 / 2f, 0f));
            float3 bCenterPlaneDistancesInA = simd.dot(bCenterInASpace, faceNormalsBoxB).xyz;

            //x vs x
            float3 axisXX        = edgeAxes03.a;
            float3 aXX           = math.select(-boxA.halfSize, boxA.halfSize, axisXX > 0f);
            float3 aExtraXX      = math.select(aXX, boxA.halfSize, axisXX == 0f);
            aExtraXX.x           = -boxA.halfSize.x;
            simdFloat3 aPointsXX = new simdFloat3(aXX, aXX, aExtraXX, aExtraXX);
            simdFloat3 aEdgesXX  = new simdFloat3(new float3(2f * boxA.halfSize.x, 0f, 0f));

            var                   dotsXX        = simd.dot(bLeftPointsInAOS, axisXX);
            float                 bestDotXX     = math.cmin(dotsXX);
            int                   dotsMaskXX    = math.bitmask(dotsXX == bestDotXX);
            math.ShuffleComponent bIndexXX      = (math.ShuffleComponent)math.clamp(math.tzcnt(dotsMaskXX), 0, 3);
            math.ShuffleComponent bExtraIndexXX = (math.ShuffleComponent)math.clamp(3 - (math.lzcnt(dotsMaskXX) - 28), 0, 3);
            simdFloat3            bPointsXX     = simd.shuffle(bLeftPointsInAOS, bLeftPointsInAOS, bIndexXX, bExtraIndexXX, bIndexXX, bExtraIndexXX);
            simdFloat3            bEdgesXX      = simd.shuffle(bRightPointsInAOS, bRightPointsInAOS, bIndexXX, bExtraIndexXX, bIndexXX, bExtraIndexXX) - bPointsXX;
            simdFloat3            bNormalsXX    = simd.shuffle(bNormalsX, bNormalsX, bIndexXX, bExtraIndexXX, bIndexXX, bExtraIndexXX);
            var                   isInvalidXX   = !CapsuleCapsule.SegmentSegmentInvalidateEndpoints(aPointsXX,
                                                                                  aEdgesXX,
                                                                                  bPointsXX,
                                                                                  bEdgesXX,
                                                                                  out simdFloat3 closestAsXX,
                                                                                  out simdFloat3 closestBsXX);
            var projectionXX             = simd.project(closestBsXX - closestAsXX, axisXX);
            isInvalidXX                 |= edgeInvalids03.x | !simd.isfiniteallxyz(projectionXX);
            var signedAxisDistancesSqXX  = simd.lengthsq(projectionXX) * math.sign(simd.dot(projectionXX, axisXX));
            var distancesSqXX            = simd.distancesq(closestAsXX, closestBsXX);
            isInvalidXX                 |= (signedAxisDistancesSqXX < 0f) & math.distance(math.abs(signedAxisDistancesSqXX), distancesSqXX) > k_boxBoxEpsilon;
            UnityEngine.Debug.Log($"XX: axis: {axisXX}, signedAxisDistancesSq: {signedAxisDistancesSqXX}, distancesSq: {distancesSqXX}, isInvalid: {isInvalidXX}");
            signedAxisDistancesSqXX = math.select(signedAxisDistancesSqXX, float.MinValue, isInvalidXX);
            distancesSqXX           = math.select(distancesSqXX, float.MaxValue, isInvalidXX);
            UnityEngine.Debug.Log($"edgeInvalids03: {edgeInvalids03}, eps: {k_boxBoxEpsilon}");

            //x vs y
            float3 axisXY        = edgeAxes03.b;
            float3 aXY           = math.select(-boxA.halfSize, boxA.halfSize, axisXY > 0f);
            float3 aExtraXY      = math.select(aXY, boxA.halfSize, axisXY == 0f);
            aExtraXY.x           = -boxA.halfSize.x;
            simdFloat3 aPointsXY = new simdFloat3(aXY, aXY, aExtraXY, aExtraXY);
            simdFloat3 aEdgesXY  = new simdFloat3(new float3(2f * boxA.halfSize.x, 0f, 0f));

            var                   dotsXY        = simd.dot(bBottomPointsInAOS, axisXY);
            float                 bestDotXY     = math.cmin(dotsXY);
            int                   dotsMaskXY    = math.bitmask(dotsXY == bestDotXY);
            math.ShuffleComponent bIndexXY      = (math.ShuffleComponent)math.clamp(math.tzcnt(dotsMaskXY), 0, 3);
            math.ShuffleComponent bExtraIndexXY = (math.ShuffleComponent)math.clamp(3 - (math.lzcnt(dotsMaskXY) - 28), 0, 3);
            simdFloat3            bPointsXY     = simd.shuffle(bBottomPointsInAOS, bBottomPointsInAOS, bIndexXY, bExtraIndexXY, bIndexXY, bExtraIndexXY);
            simdFloat3            bEdgesXY      = simd.shuffle(bTopPointsInAOS, bTopPointsInAOS, bIndexXY, bExtraIndexXY, bIndexXY, bExtraIndexXY) - bPointsXY;
            simdFloat3            bNormalsXY    = simd.shuffle(bNormalsY, bNormalsY, bIndexXY, bExtraIndexXY, bIndexXY, bExtraIndexXY);
            var                   isInvalidXY   = !CapsuleCapsule.SegmentSegmentInvalidateEndpoints(aPointsXY,
                                                                                  aEdgesXY,
                                                                                  bPointsXY,
                                                                                  bEdgesXY,
                                                                                  out simdFloat3 closestAsXY,
                                                                                  out simdFloat3 closestBsXY);
            var projectionXY             = simd.project(closestBsXY - closestAsXY, axisXY);
            isInvalidXY                 |= edgeInvalids03.y | !simd.isfiniteallxyz(projectionXY);
            var signedAxisDistancesSqXY  = simd.lengthsq(projectionXY) * math.sign(simd.dot(projectionXY, axisXY));
            var distancesSqXY            = simd.distancesq(closestAsXY, closestBsXY);
            isInvalidXY                 |= (signedAxisDistancesSqXY < 0f) & math.distance(math.abs(signedAxisDistancesSqXY), distancesSqXY) > k_boxBoxEpsilon;
            UnityEngine.Debug.Log($"XY: axis: {axisXY}, signedAxisDistancesSq: {signedAxisDistancesSqXY}, distancesSq: {distancesSqXY}, isInvalid: {isInvalidXY}");
            signedAxisDistancesSqXY = math.select(signedAxisDistancesSqXY, float.MinValue, isInvalidXY);
            distancesSqXY           = math.select(distancesSqXY, float.MaxValue, isInvalidXY);

            //x vs z
            float3 axisXZ        = edgeAxes03.c;
            float3 aXZ           = math.select(-boxA.halfSize, boxA.halfSize, axisXZ > 0f);
            float3 aExtraXZ      = math.select(aXZ, boxA.halfSize, axisXZ == 0f);
            aExtraXZ.x           = -boxA.halfSize.x;
            simdFloat3 aPointsXZ = new simdFloat3(aXZ, aXZ, aExtraXZ, aExtraXZ);
            simdFloat3 aEdgesXZ  = new simdFloat3(new float3(2f * boxA.halfSize.x, 0f, 0f));

            var                   dotsXZ        = simd.dot(bFrontPointsInAOS, axisXZ);
            float                 bestDotXZ     = math.cmin(dotsXZ);
            int                   dotsMaskXZ    = math.bitmask(dotsXZ == bestDotXZ);
            math.ShuffleComponent bIndexXZ      = (math.ShuffleComponent)math.clamp(math.tzcnt(dotsMaskXZ), 0, 3);
            math.ShuffleComponent bExtraIndexXZ = (math.ShuffleComponent)math.clamp(3 - (math.lzcnt(dotsMaskXZ) - 28), 0, 3);
            simdFloat3            bPointsXZ     = simd.shuffle(bFrontPointsInAOS, bFrontPointsInAOS, bIndexXZ, bExtraIndexXZ, bIndexXZ, bExtraIndexXZ);
            simdFloat3            bEdgesXZ      = simd.shuffle(bBackPointsInAOS, bBackPointsInAOS, bIndexXZ, bExtraIndexXZ, bIndexXZ, bExtraIndexXZ) - bPointsXZ;
            simdFloat3            bNormalsXZ    = simd.shuffle(bNormalsZ, bNormalsZ, bIndexXZ, bExtraIndexXZ, bIndexXZ, bExtraIndexXZ);
            var                   isInvalidXZ   = !CapsuleCapsule.SegmentSegmentInvalidateEndpoints(aPointsXZ,
                                                                                  aEdgesXZ,
                                                                                  bPointsXZ,
                                                                                  bEdgesXZ,
                                                                                  out simdFloat3 closestAsXZ,
                                                                                  out simdFloat3 closestBsXZ);
            var projectionXZ             = simd.project(closestBsXZ - closestAsXZ, axisXZ);
            isInvalidXZ                 |= edgeInvalids03.z | !simd.isfiniteallxyz(projectionXZ);
            var signedAxisDistancesSqXZ  = simd.lengthsq(projectionXZ) * math.sign(simd.dot(projectionXZ, axisXZ));
            var distancesSqXZ            = simd.distancesq(closestAsXZ, closestBsXZ);
            isInvalidXZ                 |= (signedAxisDistancesSqXZ < 0f) & math.distance(math.abs(signedAxisDistancesSqXZ), distancesSqXZ) > k_boxBoxEpsilon;
            UnityEngine.Debug.Log($"XZ: axis: {axisXZ}, signedAxisDistancesSq: {signedAxisDistancesSqXZ}, distancesSq: {distancesSqXZ}, isInvalid: {isInvalidXZ}");
            signedAxisDistancesSqXZ = math.select(signedAxisDistancesSqXZ, float.MinValue, isInvalidXZ);
            distancesSqXZ           = math.select(distancesSqXZ, float.MaxValue, isInvalidXZ);

            //y
            //y vs x
            float3 axisYX        = edgeAxes03.d;
            float3 aYX           = math.select(-boxA.halfSize, boxA.halfSize, axisYX > 0f);
            float3 aExtraYX      = math.select(aYX, boxA.halfSize, axisYX == 0f);
            aExtraYX.y           = -boxA.halfSize.y;
            simdFloat3 aPointsYX = new simdFloat3(aYX, aYX, aExtraYX, aExtraYX);
            simdFloat3 aEdgesYX  = new simdFloat3(new float3(0f, 2f * boxA.halfSize.y, 0f));

            var                   dotsYX        = simd.dot(bLeftPointsInAOS, axisYX);
            float                 bestDotYX     = math.cmin(dotsYX);
            int                   dotsMaskYX    = math.bitmask(dotsYX == bestDotYX);
            math.ShuffleComponent bIndexYX      = (math.ShuffleComponent)math.clamp(math.tzcnt(dotsMaskYX), 0, 3);
            math.ShuffleComponent bExtraIndexYX = (math.ShuffleComponent)math.clamp(3 - (math.lzcnt(dotsMaskYX) - 28), 0, 3);
            simdFloat3            bPointsYX     = simd.shuffle(bLeftPointsInAOS, bLeftPointsInAOS, bIndexYX, bExtraIndexYX, bIndexYX, bExtraIndexYX);
            simdFloat3            bEdgesYX      = simd.shuffle(bRightPointsInAOS, bRightPointsInAOS, bIndexYX, bExtraIndexYX, bIndexYX, bExtraIndexYX) - bPointsYX;
            simdFloat3            bNormalsYX    = simd.shuffle(bNormalsX, bNormalsX, bIndexYX, bExtraIndexYX, bIndexYX, bExtraIndexYX);
            var                   isInvalidYX   = !CapsuleCapsule.SegmentSegmentInvalidateEndpoints(aPointsYX,
                                                                                  aEdgesYX,
                                                                                  bPointsYX,
                                                                                  bEdgesYX,
                                                                                  out simdFloat3 closestAsYX,
                                                                                  out simdFloat3 closestBsYX);
            var projectionYX             = simd.project(closestBsYX - closestAsYX, axisYX);
            isInvalidYX                 |= edgeInvalids03.w | !simd.isfiniteallxyz(projectionYX);
            var signedAxisDistancesSqYX  = simd.lengthsq(projectionYX) * math.sign(simd.dot(projectionYX, axisYX));
            var distancesSqYX            = simd.distancesq(closestAsYX, closestBsYX);
            isInvalidYX                 |= (signedAxisDistancesSqYX < 0f) & math.distance(math.abs(signedAxisDistancesSqYX), distancesSqYX) > k_boxBoxEpsilon;
            UnityEngine.Debug.Log($"YX: axis: {axisYX}, signedAxisDistancesSq: {signedAxisDistancesSqYX}, distancesSq: {distancesSqYX}, isInvalid: {isInvalidYX}");
            signedAxisDistancesSqYX = math.select(signedAxisDistancesSqYX, float.MinValue, isInvalidYX);
            distancesSqYX           = math.select(distancesSqYX, float.MaxValue, isInvalidYX);

            //y vs y
            float3 axisYY        = edgeAxes47.a;
            float3 aYY           = math.select(-boxA.halfSize, boxA.halfSize, axisYY > 0f);
            float3 aExtraYY      = math.select(aYY, boxA.halfSize, axisYY == 0f);
            aExtraYY.y           = -boxA.halfSize.y;
            simdFloat3 aPointsYY = new simdFloat3(aYY, aYY, aExtraYY, aExtraYY);
            simdFloat3 aEdgesYY  = new simdFloat3(new float3(0f, 2f * boxA.halfSize.y, 0f));

            var                   dotsYY        = simd.dot(bBottomPointsInAOS, axisYY);
            float                 bestDotYY     = math.cmin(dotsYY);
            int                   dotsMaskYY    = math.bitmask(dotsYY == bestDotYY);
            math.ShuffleComponent bIndexYY      = (math.ShuffleComponent)math.clamp(math.tzcnt(dotsMaskYY), 0, 3);
            math.ShuffleComponent bExtraIndexYY = (math.ShuffleComponent)math.clamp(3 - (math.lzcnt(dotsMaskYY) - 28), 0, 3);
            simdFloat3            bPointsYY     = simd.shuffle(bBottomPointsInAOS, bBottomPointsInAOS, bIndexYY, bExtraIndexYY, bIndexYY, bExtraIndexYY);
            simdFloat3            bEdgesYY      = simd.shuffle(bTopPointsInAOS, bTopPointsInAOS, bIndexYY, bExtraIndexYY, bIndexYY, bExtraIndexYY) - bPointsYY;
            simdFloat3            bNormalsYY    = simd.shuffle(bNormalsY, bNormalsY, bIndexYY, bExtraIndexYY, bIndexYY, bExtraIndexYY);
            var                   isInvalidYY   = !CapsuleCapsule.SegmentSegmentInvalidateEndpoints(aPointsYY,
                                                                                  aEdgesYY,
                                                                                  bPointsYY,
                                                                                  bEdgesYY,
                                                                                  out simdFloat3 closestAsYY,
                                                                                  out simdFloat3 closestBsYY);
            var projectionYY             = simd.project(closestBsYY - closestAsYY, axisYY);
            isInvalidYY                 |= edgeInvalids47.x | !simd.isfiniteallxyz(projectionYY);
            var signedAxisDistancesSqYY  = simd.lengthsq(projectionYY) * math.sign(simd.dot(projectionYY, axisYY));
            var distancesSqYY            = simd.distancesq(closestAsYY, closestBsYY);
            isInvalidYY                 |= (signedAxisDistancesSqYY < 0f) & math.distance(math.abs(signedAxisDistancesSqYY), distancesSqYY) > k_boxBoxEpsilon;
            UnityEngine.Debug.Log($"YY: axis: {axisYY}, signedAxisDistancesSq: {signedAxisDistancesSqYY}, distancesSq: {distancesSqYY}, isInvalid: {isInvalidYY}");
            signedAxisDistancesSqYY = math.select(signedAxisDistancesSqYY, float.MinValue, isInvalidYY);
            distancesSqYY           = math.select(distancesSqYY, float.MaxValue, isInvalidYY);

            //y vs z
            float3 axisYZ        = edgeAxes47.b;
            float3 aYZ           = math.select(-boxA.halfSize, boxA.halfSize, axisYZ > 0f);
            float3 aExtraYZ      = math.select(aYZ, boxA.halfSize, axisYZ == 0f);
            aExtraYZ.y           = -boxA.halfSize.y;
            simdFloat3 aPointsYZ = new simdFloat3(aYZ, aYZ, aExtraYZ, aExtraYZ);
            simdFloat3 aEdgesYZ  = new simdFloat3(new float3(0f, 2f * boxA.halfSize.y, 0f));

            var                   dotsYZ        = simd.dot(bFrontPointsInAOS, axisYZ);
            float                 bestDotYZ     = math.cmin(dotsYZ);
            int                   dotsMaskYZ    = math.bitmask(dotsYZ == bestDotYZ);
            math.ShuffleComponent bIndexYZ      = (math.ShuffleComponent)math.clamp(math.tzcnt(dotsMaskYZ), 0, 3);
            math.ShuffleComponent bExtraIndexYZ = (math.ShuffleComponent)math.clamp(3 - (math.lzcnt(dotsMaskYZ) - 28), 0, 3);
            simdFloat3            bPointsYZ     = simd.shuffle(bFrontPointsInAOS, bFrontPointsInAOS, bIndexYZ, bExtraIndexYZ, bIndexYZ, bExtraIndexYZ);
            simdFloat3            bEdgesYZ      = simd.shuffle(bBackPointsInAOS, bBackPointsInAOS, bIndexYZ, bExtraIndexYZ, bIndexYZ, bExtraIndexYZ) - bPointsYZ;
            simdFloat3            bNormalsYZ    = simd.shuffle(bNormalsZ, bNormalsZ, bIndexYZ, bExtraIndexYZ, bIndexYZ, bExtraIndexYZ);
            var                   isInvalidYZ   = !CapsuleCapsule.SegmentSegmentInvalidateEndpoints(aPointsYZ,
                                                                                  aEdgesYZ,
                                                                                  bPointsYZ,
                                                                                  bEdgesYZ,
                                                                                  out simdFloat3 closestAsYZ,
                                                                                  out simdFloat3 closestBsYZ);
            var projectionYZ             = simd.project(closestBsYZ - closestAsYZ, axisYZ);
            isInvalidYZ                 |= edgeInvalids47.y | !simd.isfiniteallxyz(projectionYZ);
            var signedAxisDistancesSqYZ  = simd.lengthsq(projectionYZ) * math.sign(simd.dot(projectionYZ, axisYZ));
            var distancesSqYZ            = simd.distancesq(closestAsYZ, closestBsYZ);
            isInvalidYZ                 |= (signedAxisDistancesSqYZ < 0f) & math.distance(math.abs(signedAxisDistancesSqYZ), distancesSqYZ) > k_boxBoxEpsilon;
            UnityEngine.Debug.Log($"YZ: axis: {axisYZ}, signedAxisDistancesSq: {signedAxisDistancesSqYZ}, distancesSq: {distancesSqYZ}, isInvalid: {isInvalidYZ}");
            signedAxisDistancesSqYZ = math.select(signedAxisDistancesSqYZ, float.MinValue, isInvalidYZ);
            distancesSqYZ           = math.select(distancesSqYZ, float.MaxValue, isInvalidYZ);

            //z
            //z vs x
            float3 axisZX        = edgeAxes47.c;
            float3 aZX           = math.select(-boxA.halfSize, boxA.halfSize, axisZX > 0f);
            float3 aExtraZX      = math.select(aZX, boxA.halfSize, axisZX == 0f);
            aExtraZX.z           = -boxA.halfSize.z;
            simdFloat3 aPointsZX = new simdFloat3(aZX, aZX, aExtraZX, aExtraZX);
            simdFloat3 aEdgesZX  = new simdFloat3(new float3(0f, 0f, 2f * boxA.halfSize.z));

            var                   dotsZX        = simd.dot(bLeftPointsInAOS, axisZX);
            float                 bestDotZX     = math.cmin(dotsZX);
            int                   dotsMaskZX    = math.bitmask(dotsZX == bestDotZX);
            math.ShuffleComponent bIndexZX      = (math.ShuffleComponent)math.clamp(math.tzcnt(dotsMaskZX), 0, 3);
            math.ShuffleComponent bExtraIndexZX = (math.ShuffleComponent)math.clamp(3 - (math.lzcnt(dotsMaskZX) - 28), 0, 3);
            simdFloat3            bPointsZX     = simd.shuffle(bLeftPointsInAOS, bLeftPointsInAOS, bIndexZX, bExtraIndexZX, bIndexZX, bExtraIndexZX);
            simdFloat3            bEdgesZX      = simd.shuffle(bRightPointsInAOS, bRightPointsInAOS, bIndexZX, bExtraIndexZX, bIndexZX, bExtraIndexZX) - bPointsZX;
            simdFloat3            bNormalsZX    = simd.shuffle(bNormalsX, bNormalsX, bIndexZX, bExtraIndexZX, bIndexZX, bExtraIndexZX);
            var                   isInvalidZX   = !CapsuleCapsule.SegmentSegmentInvalidateEndpoints(aPointsZX,
                                                                                  aEdgesZX,
                                                                                  bPointsZX,
                                                                                  bEdgesZX,
                                                                                  out simdFloat3 closestAsZX,
                                                                                  out simdFloat3 closestBsZX);
            var projectionZX             = simd.project(closestBsZX - closestAsZX, axisZX);
            isInvalidZX                 |= edgeInvalids47.z | !simd.isfiniteallxyz(projectionZX);
            var signedAxisDistancesSqZX  = simd.lengthsq(projectionZX) * math.sign(simd.dot(projectionZX, axisZX));
            var distancesSqZX            = simd.distancesq(closestAsZX, closestBsZX);
            isInvalidZX                 |= (signedAxisDistancesSqZX < 0f) & math.distance(math.abs(signedAxisDistancesSqZX), distancesSqZX) > k_boxBoxEpsilon;
            UnityEngine.Debug.Log($"ZX: axis: {axisZX}, signedAxisDistancesSq: {signedAxisDistancesSqZX}, distancesSq: {distancesSqZX}, isInvalid: {isInvalidZX}");
            signedAxisDistancesSqZX = math.select(signedAxisDistancesSqZX, float.MinValue, isInvalidZX);
            distancesSqZX           = math.select(distancesSqZX, float.MaxValue, isInvalidZX);

            //z vs y
            float3 axisZY        = edgeAxes47.d;
            float3 aZY           = math.select(-boxA.halfSize, boxA.halfSize, axisZY > 0f);
            float3 aExtraZY      = math.select(aZY, boxA.halfSize, axisZY == 0f);
            aExtraZY.z           = -boxA.halfSize.z;
            simdFloat3 aPointsZY = new simdFloat3(aZY, aZY, aExtraZY, aExtraZY);
            simdFloat3 aEdgesZY  = new simdFloat3(new float3(0f, 0f, 2f * boxA.halfSize.z));

            var                   dotsZY        = simd.dot(bBottomPointsInAOS, axisZY);
            float                 bestDotZY     = math.cmin(dotsZY);
            int                   dotsMaskZY    = math.bitmask(dotsZY == bestDotZY);
            math.ShuffleComponent bIndexZY      = (math.ShuffleComponent)math.clamp(math.tzcnt(dotsMaskZY), 0, 3);
            math.ShuffleComponent bExtraIndexZY = (math.ShuffleComponent)math.clamp(3 - (math.lzcnt(dotsMaskZY) - 28), 0, 3);
            simdFloat3            bPointsZY     = simd.shuffle(bBottomPointsInAOS, bBottomPointsInAOS, bIndexZY, bExtraIndexZY, bIndexZY, bExtraIndexZY);
            simdFloat3            bEdgesZY      = simd.shuffle(bTopPointsInAOS, bTopPointsInAOS, bIndexZY, bExtraIndexZY, bIndexZY, bExtraIndexZY) - bPointsZY;
            simdFloat3            bNormalsZY    = simd.shuffle(bNormalsY, bNormalsY, bIndexZY, bExtraIndexZY, bIndexZY, bExtraIndexZY);
            var                   isInvalidZY   = !CapsuleCapsule.SegmentSegmentInvalidateEndpoints(aPointsZY,
                                                                                  aEdgesZY,
                                                                                  bPointsZY,
                                                                                  bEdgesZY,
                                                                                  out simdFloat3 closestAsZY,
                                                                                  out simdFloat3 closestBsZY);
            var projectionZY             = simd.project(closestBsZY - closestAsZY, axisZY);
            isInvalidZY                 |= edgeInvalids47.w | !simd.isfiniteallxyz(projectionZY);
            var signedAxisDistancesSqZY  = simd.lengthsq(projectionZY) * math.sign(simd.dot(projectionZY, axisZY));
            var distancesSqZY            = simd.distancesq(closestAsZY, closestBsZY);
            isInvalidZY                 |= (signedAxisDistancesSqZY < 0f) & math.distance(math.abs(signedAxisDistancesSqZY), distancesSqZY) > k_boxBoxEpsilon;
            UnityEngine.Debug.Log($"ZY: axis: {axisZY}, signedAxisDistancesSq: {signedAxisDistancesSqZY}, distancesSq: {distancesSqZY}, isInvalid: {isInvalidZY}");
            signedAxisDistancesSqZY = math.select(signedAxisDistancesSqZY, float.MinValue, isInvalidZY);
            distancesSqZY           = math.select(distancesSqZY, float.MaxValue, isInvalidZY);

            //z vs z
            float3 axisZZ        = edgeAxes8;
            float3 aZZ           = math.select(-boxA.halfSize, boxA.halfSize, axisZZ > 0f);
            float3 aExtraZZ      = math.select(aZZ, boxA.halfSize, axisZZ == 0f);
            aExtraZZ.z           = -boxA.halfSize.z;
            simdFloat3 aPointsZZ = new simdFloat3(aZZ, aZZ, aExtraZZ, aExtraZZ);
            simdFloat3 aEdgesZZ  = new simdFloat3(new float3(0f, 0f, 2f * boxA.halfSize.z));

            var                   dotsZZ        = simd.dot(bFrontPointsInAOS, axisZZ);
            float                 bestDotZZ     = math.cmin(dotsZZ);
            int                   dotsMaskZZ    = math.bitmask(dotsZZ == bestDotZZ);
            math.ShuffleComponent bIndexZZ      = (math.ShuffleComponent)math.clamp(math.tzcnt(dotsMaskZZ), 0, 3);
            math.ShuffleComponent bExtraIndexZZ = (math.ShuffleComponent)math.clamp(3 - (math.lzcnt(dotsMaskZZ) - 28), 0, 3);
            simdFloat3            bPointsZZ     = simd.shuffle(bFrontPointsInAOS, bFrontPointsInAOS, bIndexZZ, bExtraIndexZZ, bIndexZZ, bExtraIndexZZ);
            simdFloat3            bEdgesZZ      = simd.shuffle(bBackPointsInAOS, bBackPointsInAOS, bIndexZZ, bExtraIndexZZ, bIndexZZ, bExtraIndexZZ) - bPointsZZ;
            simdFloat3            bNormalsZZ    = simd.shuffle(bNormalsZ, bNormalsZ, bIndexZZ, bExtraIndexZZ, bIndexZZ, bExtraIndexZZ);
            var                   isInvalidZZ   = !CapsuleCapsule.SegmentSegmentInvalidateEndpoints(aPointsZZ,
                                                                                  aEdgesZZ,
                                                                                  bPointsZZ,
                                                                                  bEdgesZZ,
                                                                                  out simdFloat3 closestAsZZ,
                                                                                  out simdFloat3 closestBsZZ);
            var projectionZZ             = simd.project(closestBsZZ - closestAsZZ, axisZZ);
            isInvalidZZ                 |= edgeInvalids8 | !simd.isfiniteallxyz(projectionZZ);
            var signedAxisDistancesSqZZ  = simd.lengthsq(projectionZZ) * math.sign(simd.dot(projectionZZ, axisZZ));
            var distancesSqZZ            = simd.distancesq(closestAsZZ, closestBsZZ);
            isInvalidZZ                 |= (signedAxisDistancesSqZZ < 0f) & math.distance(math.abs(signedAxisDistancesSqZZ), distancesSqZZ) > k_boxBoxEpsilon;
            UnityEngine.Debug.Log($"ZZ: axis: {axisZZ}, signedAxisDistancesSq: {signedAxisDistancesSqZZ}, distancesSq: {distancesSqZZ}, isInvalid: {isInvalidZZ}");
            UnityEngine.Debug.Log($"projectionZZ: {projectionZZ.a}, {projectionZZ.b}, {projectionZZ.c}, {projectionZZ.d}");
            signedAxisDistancesSqZZ = math.select(signedAxisDistancesSqZZ, float.MinValue, isInvalidZZ);
            distancesSqZZ           = math.select(distancesSqZZ, float.MaxValue, isInvalidZZ);

            //Step 3: Find the best result.
            float4     bestEdgeSignedAxisDistancesSq = signedAxisDistancesSqXX;
            float4     bestEdgeDistancesSq           = distancesSqXX;
            simdFloat3 bestEdgeClosestAs             = closestAsXX;
            simdFloat3 bestEdgeClosestBs             = closestBsXX;
            simdFloat3 bestNormalBs                  = bNormalsXX;
            int4       bestEdgeIds                   = 0;

            bool4 newEdgeIsBetters = math.select(signedAxisDistancesSqXY > bestEdgeSignedAxisDistancesSq,
                                                 distancesSqXY < bestEdgeDistancesSq,
                                                 signedAxisDistancesSqXY >= 0f & bestEdgeDistancesSq >= 0f);
            bestEdgeSignedAxisDistancesSq = math.select(bestEdgeSignedAxisDistancesSq, signedAxisDistancesSqXY, newEdgeIsBetters);
            bestEdgeDistancesSq           = math.select(bestEdgeDistancesSq, distancesSqXY, newEdgeIsBetters);
            bestEdgeClosestAs             = simd.select(bestEdgeClosestAs, closestAsXY, newEdgeIsBetters);
            bestEdgeClosestBs             = simd.select(bestEdgeClosestBs, closestBsXY, newEdgeIsBetters);
            bestNormalBs                  = simd.select(bestNormalBs, bNormalsXY, newEdgeIsBetters);
            bestEdgeIds                   = math.select(bestEdgeIds, 1, newEdgeIsBetters);

            newEdgeIsBetters = math.select(signedAxisDistancesSqXZ > bestEdgeSignedAxisDistancesSq,
                                           distancesSqXZ < bestEdgeDistancesSq,
                                           signedAxisDistancesSqXZ >= 0f & bestEdgeDistancesSq >= 0f);
            bestEdgeSignedAxisDistancesSq = math.select(bestEdgeSignedAxisDistancesSq, signedAxisDistancesSqXZ, newEdgeIsBetters);
            bestEdgeDistancesSq           = math.select(bestEdgeDistancesSq, distancesSqXZ, newEdgeIsBetters);
            bestEdgeClosestAs             = simd.select(bestEdgeClosestAs, closestAsXZ, newEdgeIsBetters);
            bestEdgeClosestBs             = simd.select(bestEdgeClosestBs, closestBsXZ, newEdgeIsBetters);
            bestNormalBs                  = simd.select(bestNormalBs, bNormalsXZ, newEdgeIsBetters);
            bestEdgeIds                   = math.select(bestEdgeIds, 2, newEdgeIsBetters);

            newEdgeIsBetters = math.select(signedAxisDistancesSqYX > bestEdgeSignedAxisDistancesSq,
                                           distancesSqYX < bestEdgeDistancesSq,
                                           signedAxisDistancesSqYX >= 0f & bestEdgeDistancesSq >= 0f);
            bestEdgeSignedAxisDistancesSq = math.select(bestEdgeSignedAxisDistancesSq, signedAxisDistancesSqYX, newEdgeIsBetters);
            bestEdgeDistancesSq           = math.select(bestEdgeDistancesSq, distancesSqYX, newEdgeIsBetters);
            bestEdgeClosestAs             = simd.select(bestEdgeClosestAs, closestAsYX, newEdgeIsBetters);
            bestEdgeClosestBs             = simd.select(bestEdgeClosestBs, closestBsYX, newEdgeIsBetters);
            bestNormalBs                  = simd.select(bestNormalBs, bNormalsYX, newEdgeIsBetters);
            bestEdgeIds                   = math.select(bestEdgeIds, 3, newEdgeIsBetters);

            newEdgeIsBetters = math.select(signedAxisDistancesSqYY > bestEdgeSignedAxisDistancesSq,
                                           distancesSqYY < bestEdgeDistancesSq,
                                           signedAxisDistancesSqYY >= 0f & bestEdgeDistancesSq >= 0f);
            bestEdgeSignedAxisDistancesSq = math.select(bestEdgeSignedAxisDistancesSq, signedAxisDistancesSqYY, newEdgeIsBetters);
            bestEdgeDistancesSq           = math.select(bestEdgeDistancesSq, distancesSqYY, newEdgeIsBetters);
            bestEdgeClosestAs             = simd.select(bestEdgeClosestAs, closestAsYY, newEdgeIsBetters);
            bestEdgeClosestBs             = simd.select(bestEdgeClosestBs, closestBsYY, newEdgeIsBetters);
            bestNormalBs                  = simd.select(bestNormalBs, bNormalsYY, newEdgeIsBetters);
            bestEdgeIds                   = math.select(bestEdgeIds, 4, newEdgeIsBetters);

            newEdgeIsBetters = math.select(signedAxisDistancesSqYZ > bestEdgeSignedAxisDistancesSq,
                                           distancesSqYZ < bestEdgeDistancesSq,
                                           signedAxisDistancesSqYZ >= 0f & bestEdgeDistancesSq >= 0f);
            bestEdgeSignedAxisDistancesSq = math.select(bestEdgeSignedAxisDistancesSq, signedAxisDistancesSqYZ, newEdgeIsBetters);
            bestEdgeDistancesSq           = math.select(bestEdgeDistancesSq, distancesSqYZ, newEdgeIsBetters);
            bestEdgeClosestAs             = simd.select(bestEdgeClosestAs, closestAsYZ, newEdgeIsBetters);
            bestEdgeClosestBs             = simd.select(bestEdgeClosestBs, closestBsYZ, newEdgeIsBetters);
            bestNormalBs                  = simd.select(bestNormalBs, bNormalsYZ, newEdgeIsBetters);
            bestEdgeIds                   = math.select(bestEdgeIds, 5, newEdgeIsBetters);

            newEdgeIsBetters = math.select(signedAxisDistancesSqZX > bestEdgeSignedAxisDistancesSq,
                                           distancesSqZX < bestEdgeDistancesSq,
                                           signedAxisDistancesSqZX >= 0f & bestEdgeDistancesSq >= 0f);
            bestEdgeSignedAxisDistancesSq = math.select(bestEdgeSignedAxisDistancesSq, signedAxisDistancesSqZX, newEdgeIsBetters);
            bestEdgeDistancesSq           = math.select(bestEdgeDistancesSq, distancesSqZX, newEdgeIsBetters);
            bestEdgeClosestAs             = simd.select(bestEdgeClosestAs, closestAsZX, newEdgeIsBetters);
            bestEdgeClosestBs             = simd.select(bestEdgeClosestBs, closestBsZX, newEdgeIsBetters);
            bestNormalBs                  = simd.select(bestNormalBs, bNormalsZX, newEdgeIsBetters);
            bestEdgeIds                   = math.select(bestEdgeIds, 6, newEdgeIsBetters);

            newEdgeIsBetters = math.select(signedAxisDistancesSqZY > bestEdgeSignedAxisDistancesSq,
                                           distancesSqZY < bestEdgeDistancesSq,
                                           signedAxisDistancesSqZY >= 0f & bestEdgeDistancesSq >= 0f);
            bestEdgeSignedAxisDistancesSq = math.select(bestEdgeSignedAxisDistancesSq, signedAxisDistancesSqZY, newEdgeIsBetters);
            bestEdgeDistancesSq           = math.select(bestEdgeDistancesSq, distancesSqZY, newEdgeIsBetters);
            bestEdgeClosestAs             = simd.select(bestEdgeClosestAs, closestAsZY, newEdgeIsBetters);
            bestEdgeClosestBs             = simd.select(bestEdgeClosestBs, closestBsZY, newEdgeIsBetters);
            bestNormalBs                  = simd.select(bestNormalBs, bNormalsZY, newEdgeIsBetters);
            bestEdgeIds                   = math.select(bestEdgeIds, 7, newEdgeIsBetters);

            newEdgeIsBetters = math.select(signedAxisDistancesSqZZ > bestEdgeSignedAxisDistancesSq,
                                           distancesSqZZ < bestEdgeDistancesSq,
                                           signedAxisDistancesSqZZ >= 0f & bestEdgeDistancesSq >= 0f);
            bestEdgeSignedAxisDistancesSq = math.select(bestEdgeSignedAxisDistancesSq, signedAxisDistancesSqZZ, newEdgeIsBetters);
            bestEdgeDistancesSq           = math.select(bestEdgeDistancesSq, distancesSqZZ, newEdgeIsBetters);
            bestEdgeClosestAs             = simd.select(bestEdgeClosestAs, closestAsZZ, newEdgeIsBetters);
            bestEdgeClosestBs             = simd.select(bestEdgeClosestBs, closestBsZZ, newEdgeIsBetters);
            bestNormalBs                  = simd.select(bestNormalBs, bNormalsZZ, newEdgeIsBetters);
            bestEdgeIds                   = math.select(bestEdgeIds, 8, newEdgeIsBetters);
            UnityEngine.Debug.Log($"Best edge ids so far: {bestEdgeIds}");

            //float bestEdgeSignedDistanceSq = math.cmin(bestEdgeSignedDistancesSq);
            var    absoluteDistanceIsValid    = bestEdgeSignedAxisDistancesSq >= 0f;
            bool   anyAbsoluteDistanceIsValid = math.any(absoluteDistanceIsValid);
            float4 maskedAbsoluteDistances    = math.select(float.MaxValue, bestEdgeSignedAxisDistancesSq, absoluteDistanceIsValid);
            bool4  isTheBestEdge              =
                math.select(bestEdgeSignedAxisDistancesSq == math.cmax(bestEdgeSignedAxisDistancesSq),
                            maskedAbsoluteDistances == math.cmin(maskedAbsoluteDistances),
                            anyAbsoluteDistanceIsValid);
            int bestEdgeIndex = math.tzcnt(math.bitmask(isTheBestEdge));
            if (bestEdgeIndex > 3)
            {
                UnityEngine.Debug.LogError(
                    $"WTF? best: {bestEdgeSignedAxisDistancesSq}, masked: {maskedAbsoluteDistances}, any: {anyAbsoluteDistanceIsValid}, isTheBestEdge: {isTheBestEdge}");
            }
            float  bestEdgeSignedAxisDistanceSq = bestEdgeSignedAxisDistancesSq[bestEdgeIndex];
            float  bestEdgeDistanceSq           = bestEdgeDistancesSq[bestEdgeIndex];
            float3 bestEdgeClosestA             = bestEdgeClosestAs[bestEdgeIndex];
            float3 bestEdgeClosestB             = bestEdgeClosestBs[bestEdgeIndex];
            float3 bestEdgeNormalB              = bestNormalBs[bestEdgeIndex];
            int    bestId                       = bestEdgeIds[bestEdgeIndex];
            UnityEngine.Debug.Log($"Best Edge Index: {bestEdgeIndex}");

            simdFloat3 topUnnormals    = default;
            simdFloat3 bottomUnnormals = default;
            topUnnormals.x             = math.select(-1f, 1f, new bool4(true, true, false, false));
            bottomUnnormals.x          = topUnnormals.x;
            bottomUnnormals.y          = -1f;
            topUnnormals.y             = 1f;
            topUnnormals.z             = math.select(-1f, 1f, new bool4(true, false, true, false));
            bottomUnnormals.z          = topUnnormals.z;

            float3 pointsNormalBFromBInA = simd.shuffle(topUnnormals, bottomUnnormals, (math.ShuffleComponent)bInABIndex);
            float3 pointsNormalAFromAInB = simd.shuffle(topUnnormals, bottomUnnormals, (math.ShuffleComponent)aInBAIndex);
            pointsNormalBFromBInA        = math.normalize(math.rotate(bInASpace, pointsNormalBFromBInA));
            pointsNormalAFromAInB        = math.normalize(pointsNormalAFromAInB);
            float3 pointsNormalBFromAInB = math.select(0f, 1f, pointsClosestBInB == boxB.halfSize) + math.select(0f, -1f, pointsClosestBInB == -boxB.halfSize);
            pointsNormalBFromAInB        = math.normalize(math.rotate(bInASpace, pointsNormalBFromAInB));
            float3 pointsNormalAFromBInA =
                math.normalize(math.select(0f, 1f, pointsClosestAInA == boxA.halfSize) + math.select(0f, -1f, pointsClosestAInA == -boxA.halfSize));
            float3 bestEdgeNormalA             = math.normalize(math.select(0f, 1f, bestEdgeClosestA == boxA.halfSize) + math.select(0f, -1f, bestEdgeClosestA == -boxA.halfSize));
            int    matchedBIndexFromEdgeTop    = math.tzcnt(math.bitmask(bestEdgeClosestB == bTopPointsInAOS));
            int    matchedBIndexFromEdgeBottom = math.tzcnt(math.bitmask((bestEdgeClosestB == bBottomPointsInAOS))) + 4;
            int    matchedIndexBFromEdge       = math.select(matchedBIndexFromEdgeTop, matchedBIndexFromEdgeBottom, matchedBIndexFromEdgeBottom < 8);
            float3 edgeNormalBAsCorner         = simd.shuffle(topUnnormals, bottomUnnormals, (math.ShuffleComponent)math.clamp(matchedIndexBFromEdge, 0, 7));
            edgeNormalBAsCorner                = math.normalize(edgeNormalBAsCorner);
            bestEdgeNormalB                    = math.select(bestEdgeNormalB, edgeNormalBAsCorner, matchedIndexBFromEdge < 8);

            bool bInAIsBetter = math.select(pointsAxisDistanceInA >= pointsAxisDistanceInB,
                                            pointsDistanceSqInA <= pointsDistanceSqInB,
                                            pointsAxisDistanceInA >= 0f && pointsAxisDistanceInB >= 0f);
            float  pointsAxisDistance = math.select(pointsAxisDistanceInB, pointsAxisDistanceInA, bInAIsBetter);
            float  pointsDistanceSq   = math.select(pointsDistanceSqInB, pointsDistanceSqInA, bInAIsBetter);
            float3 pointsClosestA     = math.select(math.transform(bInASpace, pointsClosestAInB + boxB.center), pointsClosestAInA + boxA.center, bInAIsBetter);
            float3 pointsClosestB     = math.select(math.transform(bInASpace, pointsClosestBInB + boxB.center), pointsClosestBInA + boxA.center, bInAIsBetter);
            float3 pointsNormalA      = math.select(pointsNormalAFromAInB, pointsNormalAFromBInA, bInAIsBetter);
            float3 pointsNormalB      = math.select(pointsNormalBFromAInB, pointsNormalBFromBInA, bInAIsBetter);
            int    pointsBestId       = math.select(10, 9, bInAIsBetter);

            bool pointsIsBetter = math.select(pointsAxisDistance * pointsAxisDistance * math.sign(pointsAxisDistance) >= bestEdgeSignedAxisDistanceSq,
                                              pointsDistanceSq <= bestEdgeDistanceSq,
                                              pointsAxisDistance >= 0f && bestEdgeSignedAxisDistanceSq >= 0f);
            float  bestAxisDistance = math.select(math.sign(bestEdgeSignedAxisDistanceSq) * math.sqrt(math.abs(bestEdgeSignedAxisDistanceSq)), pointsAxisDistance, pointsIsBetter);
            float  bestDistanceSq   = math.select(bestEdgeDistanceSq, pointsDistanceSq, pointsIsBetter);
            float3 bestClosestA     = math.select(bestEdgeClosestA + boxA.center, pointsClosestA, pointsIsBetter);
            float3 bestClosestB     = math.select(bestEdgeClosestB + boxA.center, pointsClosestB, pointsIsBetter);
            float3 bestNormalA      = math.select(bestEdgeNormalA, pointsNormalA, pointsIsBetter);
            float3 bestNormalB      = math.select(math.rotate(bInASpace, bestEdgeNormalB), pointsNormalB, pointsIsBetter);
            bestId                  = math.select(bestId, pointsBestId, pointsIsBetter);
            UnityEngine.Debug.Log(
                $"bestId: {bestId}, bestAxisDistance: {bestAxisDistance}, bestDistanceSq: {bestDistanceSq}, bestClosestA: {bestClosestA}, bestClosestB: {bestClosestB}");

            //Step 4: Build result
            result = new ColliderDistanceResultInternal
            {
                hitpointA = bestClosestA,
                hitpointB = bestClosestB,
                normalA   = bestNormalA,
                normalB   = bestNormalB,
                distance  = math.sign(bestAxisDistance) * math.sqrt(math.abs(bestDistanceSq))
            };
            return result.distance <= maxDistance;
        }

        internal static void OriginAabb8PointsWithEspilonFudge(float3 aabb,
                                                               in simdFloat3 points03,
                                                               in simdFloat3 points47,
                                                               out float3 closestAOut,
                                                               out float3 closestBOut,
                                                               out float axisDistanceOut)
        {
            //Step 1: Find the minimum axis distance
            bool4 minXMask0347 = points03.x <= points47.x;
            bool4 minYMask0347 = points03.y <= points47.y;
            bool4 minZMask0347 = points03.z <= points47.z;

            var minX0347 = simd.select(points47, points03, minXMask0347);
            var maxX0347 = simd.select(points03, points47, minXMask0347);
            var minY0347 = simd.select(points47, points03, minYMask0347);
            var maxY0347 = simd.select(points03, points47, minYMask0347);
            var minZ0347 = simd.select(points47, points03, minZMask0347);
            var maxZ0347 = simd.select(points03, points47, minZMask0347);

            float minXValue = math.cmin(minX0347.x);
            float maxXValue = math.cmax(maxX0347.x);
            float minYValue = math.cmin(minY0347.y);
            float maxYValue = math.cmax(maxY0347.y);
            float minZValue = math.cmin(minZ0347.z);
            float maxZValue = math.cmax(maxZ0347.z);

            float3 minValues = new float3(minXValue, minYValue, minZValue);
            float3 maxValues = new float3(maxXValue, maxYValue, maxZValue);

            float3 distancesToMin = maxValues + aabb;
            float3 distancesToMax = aabb - minValues;
            float3 bestDistances  = math.min(distancesToMin, distancesToMax);
            float  bestDistance   = math.cmin(bestDistances);
            bool3  bestAxisMask   = bestDistance == bestDistances;
            // Prioritize y first, then z, then x if multiple distances perfectly match.
            // Todo: Should this be configurabe?
            bestAxisMask.xz &= !bestAxisMask.y;
            bestAxisMask.x  &= !bestAxisMask.z;

            //Step 2: Find the point that matches the bestDistance for the bestDistanceMask and has the least deviation when clamped to the AABB
            simdFloat3 distancesToMin03 = points03 + aabb;
            simdFloat3 distancesToMax03 = aabb - points03;
            simdFloat3 distancesToMin47 = points47 + aabb;
            simdFloat3 distancesToMax47 = aabb - points47;

            // Because we bias away from the edge-edge case, nearly parallel edge-faces might report the wrong point.
            // So we add an epsilon fudge and capture the absolute distance
            bool4 matchesMinX03 = (math.abs(bestDistance - distancesToMin03.x) < math.EPSILON) & bestAxisMask.x;
            bool4 matchesMinY03 = (math.abs(bestDistance - distancesToMin03.y) < math.EPSILON) & bestAxisMask.y;
            bool4 matchesMinZ03 = (math.abs(bestDistance - distancesToMin03.z) < math.EPSILON) & bestAxisMask.z;
            bool4 matchesX03    = matchesMinX03 | (math.abs(bestDistance - distancesToMax03.x) < math.EPSILON) & bestAxisMask.x;
            bool4 matchesY03    = matchesMinY03 | (math.abs(bestDistance - distancesToMax03.y) < math.EPSILON) & bestAxisMask.y;
            bool4 matchesZ03    = matchesMinZ03 | (math.abs(bestDistance - distancesToMax03.z) < math.EPSILON) & bestAxisMask.z;

            bool4 matchesMinX47 = (math.abs(bestDistance - distancesToMin47.x) < math.EPSILON) & bestAxisMask.x;
            bool4 matchesMinY47 = (math.abs(bestDistance - distancesToMin47.y) < math.EPSILON) & bestAxisMask.y;
            bool4 matchesMinZ47 = (math.abs(bestDistance - distancesToMin47.z) < math.EPSILON) & bestAxisMask.z;
            bool4 matchesX47    = matchesMinX47 | (math.abs(bestDistance - distancesToMax47.x) < math.EPSILON) & bestAxisMask.x;
            bool4 matchesY47    = matchesMinY47 | (math.abs(bestDistance - distancesToMax47.y) < math.EPSILON) & bestAxisMask.y;
            bool4 matchesZ47    = matchesMinZ47 | (math.abs(bestDistance - distancesToMax47.z) < math.EPSILON) & bestAxisMask.z;

            float4 diffXValues03 = points03.x - math.clamp(points03.x, -aabb.x, aabb.x);
            float4 diffYValues03 = points03.y - math.clamp(points03.y, -aabb.y, aabb.y);
            float4 diffZValues03 = points03.z - math.clamp(points03.z, -aabb.z, aabb.z);
            float4 diffXValues47 = points47.x - math.clamp(points47.x, -aabb.x, aabb.x);
            float4 diffYValues47 = points47.y - math.clamp(points47.y, -aabb.y, aabb.y);
            float4 diffZValues47 = points47.z - math.clamp(points47.z, -aabb.z, aabb.z);

            float4 distSqYZ03 = math.select(float.MaxValue, diffYValues03 * diffYValues03 + diffZValues03 * diffZValues03, matchesX03);
            float4 distSqXZ03 = math.select(float.MaxValue, diffXValues03 * diffXValues03 + diffZValues03 * diffZValues03, matchesY03);
            float4 distSqXY03 = math.select(float.MaxValue, diffXValues03 * diffXValues03 + diffYValues03 * diffYValues03, matchesZ03);
            float4 distSqYZ47 = math.select(float.MaxValue, diffYValues47 * diffYValues47 + diffZValues47 * diffZValues47, matchesX47);
            float4 distSqXZ47 = math.select(float.MaxValue, diffXValues47 * diffXValues47 + diffZValues47 * diffZValues47, matchesY47);
            float4 distSqXY47 = math.select(float.MaxValue, diffXValues47 * diffXValues47 + diffYValues47 * diffYValues47, matchesZ47);

            bool4  useY03          = distSqXZ03 < distSqYZ03;
            float4 bestDistSq03    = math.select(distSqYZ03, distSqXZ03, useY03);
            bool4  matchesMin03    = math.select(matchesMinX03, matchesMinY03, useY03);
            bool4  useZ03          = distSqXY03 < bestDistSq03;
            bestDistSq03           = math.select(bestDistSq03, distSqXY03, useZ03);
            matchesMin03           = math.select(matchesMin03, matchesMinZ03, useZ03);
            float bestDistSqFrom03 = math.cmin(bestDistSq03);
            int   index03          = math.clamp(math.tzcnt(math.bitmask(bestDistSqFrom03 == bestDistSq03)), 0, 3);

            bool4  useY47          = distSqXZ47 < distSqYZ47;
            float4 bestDistSq47    = math.select(distSqYZ47, distSqXZ47, useY47);
            bool4  matchesMin47    = math.select(matchesMinX47, matchesMinY47, useY47);
            bool4  useZ47          = distSqXY47 < bestDistSq47;
            bestDistSq47           = math.select(bestDistSq47, distSqXY47, useZ47);
            matchesMin47           = math.select(matchesMin47, matchesMinZ47, useZ47);
            float bestDistSqFrom47 = math.cmin(bestDistSq47);
            int   index47          = math.clamp(math.tzcnt(math.bitmask(bestDistSqFrom47 == bestDistSq47)), 0, 3) + 4;

            bool                  use47      = bestDistSqFrom47 < bestDistSqFrom03;
            math.ShuffleComponent bestIndex  = (math.ShuffleComponent)math.select(index03, index47, use47);
            bool4                 matchesMin = math.select(matchesMin03, matchesMin47, use47);
            bool                  useMin     = matchesMin[((int)bestIndex) & 3];

            closestBOut     = simd.shuffle(points03, points47, bestIndex);
            closestAOut     = math.select(closestBOut, math.select(aabb, -aabb, useMin), bestAxisMask);
            closestAOut     = math.clamp(closestAOut, -aabb, aabb);
            axisDistanceOut = -bestDistance;
        }

        internal static void OriginAabb8PointsWithEspilonFudgeDebug(float3 aabb,
                                                                    in simdFloat3 points03,
                                                                    in simdFloat3 points47,
                                                                    out float3 closestAOut,
                                                                    out float3 closestBOut,
                                                                    out float axisDistanceOut)
        {
            UnityEngine.Debug.Log(
                $"Begin OriginAabb8PointsWithEpsilonFudge. aabb: {aabb}, points: {points03.a}, {points03.b}, {points03.c}, {points03.d}, {points47.a}, {points47.b}, {points47.c}, {points47.d}");

            //Step 1: Find the minimum axis distance
            bool4 minXMask0347 = points03.x <= points47.x;
            bool4 minYMask0347 = points03.y <= points47.y;
            bool4 minZMask0347 = points03.z <= points47.z;
            UnityEngine.Debug.Log($"minXMask0347: {minXMask0347}, minYMask0347: {minYMask0347}, minZMask0347: {minZMask0347}");

            var minX0347 = simd.select(points47, points03, minXMask0347);
            var maxX0347 = simd.select(points03, points47, minXMask0347);
            var minY0347 = simd.select(points47, points03, minYMask0347);
            var maxY0347 = simd.select(points03, points47, minYMask0347);
            var minZ0347 = simd.select(points47, points03, minZMask0347);
            var maxZ0347 = simd.select(points03, points47, minZMask0347);

            float minXValue = math.cmin(minX0347.x);
            float maxXValue = math.cmax(maxX0347.x);
            float minYValue = math.cmin(minY0347.y);
            float maxYValue = math.cmax(maxY0347.y);
            float minZValue = math.cmin(minZ0347.z);
            float maxZValue = math.cmax(maxZ0347.z);

            float3 minValues = new float3(minXValue, minYValue, minZValue);
            float3 maxValues = new float3(maxXValue, maxYValue, maxZValue);

            float3 distancesToMin = maxValues + aabb;
            float3 distancesToMax = aabb - minValues;
            float3 bestDistances  = math.min(distancesToMin, distancesToMax);
            float  bestDistance   = math.cmin(bestDistances);
            bool3  bestAxisMask   = bestDistance == bestDistances;
            UnityEngine.Debug.Log(
                $"minValues: {minValues}, maxValues: {maxValues}, distancesToMin: {distancesToMin}, distancesToMax: {distancesToMax}, bestDistance: {bestDistance}, bestAxisMask: {bestAxisMask}");

            //Step 2: Find the point that matches the bestDistance for the bestDistanceMask and has the least deviation when clamped to the AABB
            simdFloat3 distancesToMin03 = points03 + aabb;
            simdFloat3 distancesToMax03 = aabb - points03;
            simdFloat3 distancesToMin47 = points47 + aabb;
            simdFloat3 distancesToMax47 = aabb - points47;

            // Because we bias away from the edge-edge case, nearly parallel edge-faces might report the wrong point.
            // So we add an epsilon fudge and capture the absolute distance
            bool4 matchesMinX03 = (math.abs(bestDistance - distancesToMin03.x) < math.EPSILON) & bestAxisMask.x;
            bool4 matchesMinY03 = (math.abs(bestDistance - distancesToMin03.y) < math.EPSILON) & bestAxisMask.y;
            bool4 matchesMinZ03 = (math.abs(bestDistance - distancesToMin03.z) < math.EPSILON) & bestAxisMask.z;
            bool4 matchesX03    = matchesMinX03 | (math.abs(bestDistance - distancesToMax03.x) < math.EPSILON) & bestAxisMask.x;
            bool4 matchesY03    = matchesMinY03 | (math.abs(bestDistance - distancesToMax03.y) < math.EPSILON) & bestAxisMask.y;
            bool4 matchesZ03    = matchesMinZ03 | (math.abs(bestDistance - distancesToMax03.z) < math.EPSILON) & bestAxisMask.z;

            bool4 matchesMinX47 = (math.abs(bestDistance - distancesToMin47.x) < math.EPSILON) & bestAxisMask.x;
            bool4 matchesMinY47 = (math.abs(bestDistance - distancesToMin47.y) < math.EPSILON) & bestAxisMask.y;
            bool4 matchesMinZ47 = (math.abs(bestDistance - distancesToMin47.z) < math.EPSILON) & bestAxisMask.z;
            bool4 matchesX47    = matchesMinX47 | (math.abs(bestDistance - distancesToMax47.x) < math.EPSILON) & bestAxisMask.x;
            bool4 matchesY47    = matchesMinY47 | (math.abs(bestDistance - distancesToMax47.y) < math.EPSILON) & bestAxisMask.y;
            bool4 matchesZ47    = matchesMinZ47 | (math.abs(bestDistance - distancesToMax47.z) < math.EPSILON) & bestAxisMask.z;
            UnityEngine.Debug.Log(
                $"matchesX03: {matchesX03}, matchesY03: {matchesY03}, matchesZ03: {matchesZ03}, matchesX47: {matchesX47}, matchesY47: {matchesY47}, matchesZ47: {matchesZ47}");

            float4 diffXValues03 = points03.x - math.clamp(points03.x, -aabb.x, aabb.x);
            float4 diffYValues03 = points03.y - math.clamp(points03.y, -aabb.y, aabb.y);
            float4 diffZValues03 = points03.z - math.clamp(points03.z, -aabb.z, aabb.z);
            float4 diffXValues47 = points47.x - math.clamp(points47.x, -aabb.x, aabb.x);
            float4 diffYValues47 = points47.y - math.clamp(points47.y, -aabb.y, aabb.y);
            float4 diffZValues47 = points47.z - math.clamp(points47.z, -aabb.z, aabb.z);
            UnityEngine.Debug.Log(
                $"diffXValues03: {diffXValues03}, diffYValues03: {diffYValues03}, diffZValues03: {diffZValues03}, diffXValues47: {diffXValues47}, diffYValues47: {diffYValues47}, diffZValues47: {diffZValues47}");

            float4 distSqYZ03 = math.select(float.MaxValue, diffYValues03 * diffYValues03 + diffZValues03 * diffZValues03, matchesX03);
            float4 distSqXZ03 = math.select(float.MaxValue, diffXValues03 * diffXValues03 + diffZValues03 * diffZValues03, matchesY03);
            float4 distSqXY03 = math.select(float.MaxValue, diffXValues03 * diffXValues03 + diffYValues03 * diffYValues03, matchesZ03);
            float4 distSqYZ47 = math.select(float.MaxValue, diffYValues47 * diffYValues47 + diffZValues47 * diffZValues47, matchesX47);
            float4 distSqXZ47 = math.select(float.MaxValue, diffXValues47 * diffXValues47 + diffZValues47 * diffZValues47, matchesY47);
            float4 distSqXY47 = math.select(float.MaxValue, diffXValues47 * diffXValues47 + diffYValues47 * diffYValues47, matchesZ47);
            UnityEngine.Debug.Log(
                $"distSqYZ03: {distSqYZ03}, distSqXZ03: {distSqXZ03}, distSqXY03: {distSqXY03}, distSqYZ47: {distSqYZ47}, distSqXZ47: {distSqXZ47}, distSqXY47: {distSqXY47}");

            bool4  useY03          = distSqXZ03 < distSqYZ03;
            float4 bestDistSq03    = math.select(distSqYZ03, distSqXZ03, useY03);
            bool4  matchesMin03    = math.select(matchesMinX03, matchesMinY03, useY03);
            bool4  useZ03          = distSqXY03 < bestDistSq03;
            bestDistSq03           = math.select(bestDistSq03, distSqXY03, useZ03);
            matchesMin03           = math.select(matchesMin03, matchesMinZ03, useZ03);
            float bestDistSqFrom03 = math.cmin(bestDistSq03);
            int   index03          = math.clamp(math.tzcnt(math.bitmask(bestDistSqFrom03 == bestDistSq03)), 0, 3);
            UnityEngine.Debug.Log(
                $"useY03: {useY03}, useZ03: {useZ03}, bestDistSq03: {bestDistSq03}, matchesMin03: {matchesMin03}, bestDistSqFrom03: {bestDistSqFrom03}, index03: {index03}");

            bool4  useY47          = distSqXZ47 < distSqYZ47;
            float4 bestDistSq47    = math.select(distSqYZ47, distSqXZ47, useY47);
            bool4  matchesMin47    = math.select(matchesMinX47, matchesMinY47, useY47);
            bool4  useZ47          = distSqXY47 < bestDistSq47;
            bestDistSq47           = math.select(bestDistSq47, distSqXY47, useZ47);
            matchesMin47           = math.select(matchesMin47, matchesMinZ47, useZ47);
            float bestDistSqFrom47 = math.cmin(bestDistSq47);
            int   index47          = math.clamp(math.tzcnt(math.bitmask(bestDistSqFrom47 == bestDistSq47)), 0, 3) + 4;

            UnityEngine.Debug.Log(
                $"useY47: {useY47}, useZ47: {useZ47}, bestDistSq47: {bestDistSq47}, matchesMin47: {matchesMin47}, bestDistSqFrom47: {bestDistSqFrom47}, index47: {index47}");

            bool                  use47      = bestDistSqFrom47 < bestDistSqFrom03;
            math.ShuffleComponent bestIndex  = (math.ShuffleComponent)math.select(index03, index47, use47);
            bool4                 matchesMin = math.select(matchesMin03, matchesMin47, use47);
            bool                  useMin     = matchesMin[((int)bestIndex) & 3];
            UnityEngine.Debug.Log($"use47: {use47}, bestIndex: {bestIndex}, matchesMin: {matchesMin}, useMin: {useMin}");

            closestBOut     = simd.shuffle(points03, points47, bestIndex);
            closestAOut     = math.select(closestBOut, math.select(aabb, -aabb, useMin), bestAxisMask);
            closestAOut     = math.clamp(closestAOut, -aabb, aabb);
            axisDistanceOut = -bestDistance;
        }

        /*internal static void OriginAabb8BoxPoints(float3 aabbExtents, float3 boxCenter, simdFloat3 points03, simdFloat3 points47, out int closestAIndexOut, out float3 closestBOut, out float signedDistanceOut)
           {
            // This is basically the point vs box algorithm brute-forced and vectorized.
            bool4 isNegativeX03 = points03.x < 0f;
            bool4 isNegativeY03 = points03.y < 0f;
            bool4 isNegativeZ03 = points03.z < 0f;
            bool4 isNegativeX47 = points47.x < 0f;
            bool4 isNegativeY47 = points47.y < 0f;
            bool4 isNegativeZ47 = points47.z < 0f;
            simdFloat3 ospPoints03 = simd.select(points03, -points03, isNegativeX03, isNegativeY03, isNegativeZ03);
            simdFloat3 ospPoints47 = simd.select(points47, -points47, isNegativeX47, isNegativeY47, isNegativeZ47);
            int4 region03 = math.select(4, int4.zero, ospPoints03.x < aabbExtents.x);
            int4 region47 = math.select(4, int4.zero, ospPoints47.x < aabbExtents.x);
            region03 += math.select(2, int4.zero, ospPoints03.y < aabbExtents.y);
            region47 += math.select(2, int4.zero, ospPoints47.y < aabbExtents.y);
            region03 += math.select(1, int4.zero, ospPoints03.z < aabbExtents.z);
            region47 += math.select(1, int4.zero, ospPoints47.z < aabbExtents.z);

            // Region 0: Inside the box. Closest feature is face.
            simdFloat3 delta03 = aabbExtents - ospPoints03;
            simdFloat3 delta47 = aabbExtents - ospPoints47;
            float4 min03 = simd.cminxyz(delta03);
            float4 min47 = simd.cminxyz(delta47);
            bool4 minMaskX03 = min03 == delta03.x;
            bool4 minMaskX47 = min47 == delta47.x;
            bool4 minMaskY03 = min03 == delta03.y;
            bool4 minMaskY47 = min47 == delta47.y;
            bool4 minMaskZ03 = min03 == delta03.z;
            bool4 minMaskZ47 = min47 == delta47.z;
            simdFloat3 closestB03 = simd.select(ospPoints03, new simdFloat3(aabbExtents), minMaskX03, minMaskY03, minMaskZ03);
            simdFloat3 closestB47 = simd.select(ospPoints47, new simdFloat3(aabbExtents), minMaskX47, minMaskY47, minMaskZ47);
            float4 signedDistance03 = -min03;
            float4 signedDistance47 = -min47;

            // Region 1: xy in box, z outside. Closest feature is the z-face.
            signedDistance03 = math.select(signedDistance03, ospPoints03.z - aabbExtents.z, region03 == 1);
            signedDistance47 = math.select(signedDistance47, ospPoints47.z - aabbExtents.z, region47 == 1);
            closestB03 = simd.select(closestB03, new simdFloat3(closestB03.x, closestB03.y, aabbExtents.z), region03 == 1);
            closestB47 = simd.select(closestB47, new simdFloat3(closestB47.x, closestB47.y, aabbExtents.z), region47 == 1);

            // Region 2: xz in box, y outside. Closest feature is the y-face.
            signedDistance03 = math.select(signedDistance03, ospPoints03.y - aabbExtents.y, region03 == 2);
            signedDistance47 = math.select(signedDistance47, ospPoints47.y - aabbExtents.y, region47 == 2);
            closestB03 = simd.select(closestB03, new simdFloat3(closestB03.x, aabbExtents.y, closestB03.z), region03 == 2);
            closestB47 = simd.select(closestB47, new simdFloat3(closestB47.x, aabbExtents.y, closestB47.z), region47 == 2);

            // Region 3: x in box, yz outside. Closest feature is the x-axis edge.
            simdFloat3 diffSq03 = ospPoints03 - aabbExtents;
            simdFloat3 diffSq47 = ospPoints47 - aabbExtents;
            diffSq03 *= diffSq03;
            diffSq47 *= diffSq47;
            signedDistance03 = math.select(signedDistance03, math.sqrt(diffSq03.y + diffSq03.z), region03 == 3);
            signedDistance47 = math.select(signedDistance47, math.sqrt(diffSq47.y + diffSq47.z), region47 == 3);
            closestB03 = simd.select(closestB03, new simdFloat3(closestB03.x, aabbExtents.y, aabbExtents.z), region03 == 3);
            closestB47 = simd.select(closestB47, new simdFloat3(closestB47.x, aabbExtents.y, aabbExtents.z), region47 == 3);

            // Region 4: yz in box, x outside. Closest feature is the x-face.
            signedDistance03 = math.select(signedDistance03, ospPoints03.x - aabbExtents.x, region03 == 4);
            signedDistance47 = math.select(signedDistance47, ospPoints47.x - aabbExtents.x, region47 == 4);
            closestB03 = simd.select(closestB03, new simdFloat3(aabbExtents.x, closestB03.y, closestB03.z), region03 == 4);
            closestB47 = simd.select(closestB47, new simdFloat3(aabbExtents.x, closestB47.y, closestB47.z), region47 == 4);

            // Region 5: y in box, xz outside. Closest feature is the y-axis edge.
            signedDistance03 = math.select(signedDistance03, math.sqrt(diffSq03.x + diffSq03.z), region03 == 5);
            signedDistance47 = math.select(signedDistance47, math.sqrt(diffSq47.x + diffSq47.z), region47 == 5);
            closestB03 = simd.select(closestB03, new simdFloat3(aabbExtents.x, closestB03.y, aabbExtents.z), region03 == 5);
            closestB47 = simd.select(closestB47, new simdFloat3(aabbExtents.x, closestB47.y, aabbExtents.z), region47 == 5);

            // Region 6: z in box, xy outside. Closest feature is the z-axis edge.
            signedDistance03 = math.select(signedDistance03, math.sqrt(diffSq03.x + diffSq03.y), region03 == 6);
            signedDistance47 = math.select(signedDistance47, math.sqrt(diffSq47.x + diffSq47.y), region47 == 6);
            closestB03 = simd.select(closestB03, new simdFloat3(aabbExtents.x, aabbExtents.y, closestB03.z), region03 == 6);
            closestB47 = simd.select(closestB47, new simdFloat3(aabbExtents.x, aabbExtents.y, closestB47.z), region47 == 6);

            // Region 7: xyz outside box. Closest feature is the corner.
            signedDistance03 = math.select(signedDistance03, simd.length(diffSq03), region03 == 7);
            signedDistance47 = math.select(signedDistance47, simd.length(diffSq47), region47 == 7);
            closestB03 = simd.select(closestB03, new simdFloat3(aabbExtents), region03 == 7);
            closestB47 = simd.select(closestB47, new simdFloat3(aabbExtents), region47 == 7);

            // Expand into the full octant space
            closestB03 = simd.select(closestB03, -closestB03, isNegativeX03, isNegativeY03, isNegativeZ03);
            closestB47 = simd.select(closestB47, -closestB47, isNegativeX47, isNegativeY47, isNegativeZ47);

            // We now need to account for punchthrough, which
           }
         */

        public static bool4 ArePointsInsideObbPlusEpsilon(simdFloat3 points, simdFloat3 obbNormals, float3 distances, float3 halfWidths)
        {
            float3 positives  = distances + halfWidths + math.EPSILON;
            float3 negatives  = distances - halfWidths - math.EPSILON;
            var    dots       = simd.dot(points, obbNormals.a);
            bool4  resultsX   = (dots <= positives.x) & (dots >= negatives.x);
            resultsX         |= (-dots <= positives.x) & (-dots >= negatives.x);
            dots              = simd.dot(points, obbNormals.b);
            bool4 resultsY    = (dots <= positives.y) & (dots >= negatives.y);
            resultsY         |= (-dots <= positives.y) & (-dots >= negatives.y);
            dots              = simd.dot(points, obbNormals.c);
            bool4 resultsZ    = (dots <= positives.z) & (dots >= negatives.z);
            resultsZ         |= (-dots <= positives.z) & (-dots >= negatives.z);
            return resultsX & resultsY & resultsZ;
        }
    }
}

