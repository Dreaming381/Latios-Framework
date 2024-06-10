using System;
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
            var featureCodeA  = PointRayConvex.FeatureCodeFromGjk(gjkResult.simplexAVertexCount,
                                                                  gjkResult.simplexAVertexA,
                                                                  gjkResult.simplexAVertexB,
                                                                  gjkResult.simplexAVertexC,
                                                                  in convex);
            var featureCodeB = PointRayBox.FeatureCodeFromGjk(gjkResult.simplexBVertexCount, gjkResult.simplexBVertexA, gjkResult.simplexBVertexB, gjkResult.simplexBVertexC);
            result           = InternalQueryTypeUtilities.BinAResultToWorld(new ColliderDistanceResultInternal
            {
                distance     = gjkResult.distance,
                hitpointA    = gjkResult.hitpointOnAInASpace,
                hitpointB    = gjkResult.hitpointOnBInASpace,
                normalA      = PointRayConvex.ConvexNormalFromFeatureCode(featureCodeA, in convex, -gjkResult.normalizedOriginToClosestCsoPoint),
                normalB      = math.rotate(bInATransform.rot, PointRayBox.BoxNormalFromFeatureCode(featureCodeB)),
                featureCodeA = featureCodeA,
                featureCodeB = featureCodeB
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

        public static UnitySim.ContactsBetweenResult UnityContactsBetween(in ConvexCollider convex,
                                                                          in RigidTransform convexTransform,
                                                                          in BoxCollider box,
                                                                          in RigidTransform boxTransform,
                                                                          in ColliderDistanceResult distanceResult)
        {
            var contactNormal = math.normalizesafe((distanceResult.hitpointB - distanceResult.hitpointA) * math.select(1f, -1f, distanceResult.distance < 0f), float3.zero);
            if (contactNormal.Equals(float3.zero))
            {
                contactNormal = math.normalize(distanceResult.normalA - distanceResult.normalB);
            }
            var aLocalContactNormal = math.InverseRotateFast(convexTransform.rot, contactNormal);
            contactNormal           = -contactNormal;

            ref var blob       = ref convex.convexColliderBlob.Value;
            float3  invScale   = math.rcp(convex.scale);
            var     bitmask    = math.bitmask(new bool4(math.isfinite(invScale), false));
            var     dimensions = math.countbits(bitmask);

            if (dimensions == 3)
            {
                var bInATransform       = math.mul(math.inverse(convexTransform), boxTransform);
                var bLocalContactNormal = math.InverseRotateFast(bInATransform.rot, -aLocalContactNormal);
                PointRayConvex.BestFacePlane(ref convex.convexColliderBlob.Value,
                                             aLocalContactNormal * invScale,
                                             distanceResult.featureCodeA,
                                             out var aPlane,
                                             out int faceIndex,
                                             out int edgeCount);
                PointRayBox.BestFacePlanesAndVertices(in box, bLocalContactNormal, out var bEdgePlaneNormals, out _, out var bPlane, out var bVertices);
                bPlane                                 = mathex.TransformPlane(bInATransform, bPlane);
                bVertices                              = simd.transform(bInATransform, bVertices);
                bEdgePlaneNormals                      = simd.mul(bInATransform.rot, bEdgePlaneNormals);
                var  bEdgePlaneDistances               = simd.dot(bEdgePlaneNormals, bVertices.bcda);
                bool needsClosestPoint                 = true;
                var  distanceScalarAlongContactNormalB = math.rcp(math.dot(-aLocalContactNormal, bPlane.normal));

                bool projectBOnA        = math.abs(math.dot(aPlane.normal, aLocalContactNormal)) < 0.05f;
                int4 positiveSideCounts = 0;
                int4 negativeSideCounts = 0;

                UnityContactManifoldExtra3D result = default;
                result.baseStorage.contactNormal   = contactNormal;
                var edgeIndicesBase                = blob.edgeIndicesInFacesStartsAndCounts[faceIndex].start;

                // Project and clip edges of A onto the face of B.
                for (int edgeIndex = 0; edgeIndex < edgeCount; edgeIndex++)
                {
                    var    edgeIndexInFace = blob.edgeIndicesInFaces[edgeIndicesBase + edgeIndex];
                    var    edge            = blob.vertexIndicesInEdges[edgeIndexInFace.index];
                    var    edgeStart       = edgeIndexInFace.flipped ? edge.y : edge.x;
                    var    edgeTail        = edgeIndexInFace.flipped ? edge.x : edge.y;
                    float3 rayStart        = new float3(blob.verticesX[edgeStart], blob.verticesY[edgeStart], blob.verticesZ[edgeStart]) * convex.scale;
                    float3 rayDisplacement = new float3(blob.verticesX[edgeTail], blob.verticesY[edgeTail], blob.verticesZ[edgeTail]) * convex.scale - rayStart;

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
                        result.Add(math.transform(convexTransform, clippedSegmentA + aLocalContactNormal * aDistance), aDistance);
                        needsClosestPoint &= aDistance > distanceResult.distance + 1e-4f;
                        if (fractionB < 1f)  // Avoid duplication when vertex is not clipped
                        {
                            var clippedSegmentB = rayStart + fractionB * rayDisplacement;
                            var bDistance       = mathex.SignedDistance(bPlane, clippedSegmentB) * distanceScalarAlongContactNormalB;
                            result.Add(math.transform(convexTransform, clippedSegmentB + aLocalContactNormal * bDistance), bDistance);
                            needsClosestPoint &= bDistance > distanceResult.distance + 1e-4f;
                        }
                    }

                    // Inside for each edge of A
                    if (projectBOnA)
                    {
                        var aEdgePlaneNormal   = math.cross(rayDisplacement, aLocalContactNormal);
                        var edgePlaneDistance  = math.dot(aEdgePlaneNormal, rayStart);
                        var projection         = simd.dot(bVertices, aEdgePlaneNormal);
                        positiveSideCounts    += math.select(int4.zero, 1, projection > edgePlaneDistance);
                        negativeSideCounts    += math.select(int4.zero, 1, projection < edgePlaneDistance);
                    }
                }
                if (projectBOnA)
                {
                    var distanceScalarAlongContactNormalA = math.rcp(math.dot(aLocalContactNormal, aPlane.normal));
                    var bProjectsOnA                      = math.min(positiveSideCounts, negativeSideCounts) == 0;
                    for (int i = 0; i < 4; i++)
                    {
                        var vertex = bVertices[i];
                        if (bProjectsOnA[i])
                        {
                            var distance = mathex.SignedDistance(aPlane, vertex) * distanceScalarAlongContactNormalA;
                            result.Add(math.transform(convexTransform, vertex), distance);
                            needsClosestPoint &= distance > distanceResult.distance + 1e-4f;
                        }
                    }
                }

                var requiredContacts = math.select(32, 31, needsClosestPoint);
                if (result.baseStorage.contactCount <= requiredContacts)
                {
                    if (needsClosestPoint)
                        result.baseStorage.Add(distanceResult.hitpointB, distanceResult.distance);
                    return result.baseStorage;
                }

                // Simplification required
                Span<byte>   indices           = stackalloc byte[requiredContacts];
                Span<float3> projectedContacts = stackalloc float3[result.baseStorage.contactCount];
                for (int i = 0; i < result.baseStorage.contactCount; i++)
                {
                    projectedContacts[i] = result[i].location;
                }
                var                            finalVertexCount = ExpandingPolygonBuilder2D.Build(ref indices, projectedContacts, bPlane.normal);
                UnitySim.ContactsBetweenResult finalResult      = default;
                finalResult.contactNormal                       = result.baseStorage.contactNormal;
                for (int i = 0; i < finalVertexCount; i++)
                {
                    finalResult.Add(result[indices[i]]);
                }
                if (needsClosestPoint)
                    finalResult.Add(distanceResult.hitpointB, distanceResult.distance);
                return finalResult;
            }
            else if (dimensions == 0)
            {
                return ContactManifoldHelpers.GetSingleContactManifold(in distanceResult);
            }
            else if (dimensions == 1)
            {
                var convexCapsule = new CapsuleCollider(blob.localAabb.min * convex.scale, blob.localAabb.max * convex.scale, 0f);
                var result        = CapsuleBox.UnityContactsBetween(in box,
                                                                    in boxTransform,
                                                                    in convexCapsule,
                                                                    in convexTransform,
                                                                    distanceResult.ToFlipped());
                result.FlipInPlace();
                return result;
            }
            else  //if (dimensions == 2)
            {
                ref var indices2D = ref blob.yz2DVertexIndices;  // bitmask = 6
                var     aPlane    = new Plane(new float3(1f, 0f, 0f), 0f);
                if (bitmask == 5)
                {
                    indices2D = ref blob.xz2DVertexIndices;
                    aPlane    = new Plane(new float3(0f, 1f, 0f), 0f);
                }
                else if (bitmask == 3)
                {
                    indices2D = ref blob.xy2DVertexIndices;
                    aPlane    = new Plane(new float3(0f, 0f, 1f), 0f);
                }
                if (math.dot(aPlane.normal, aLocalContactNormal) < 0f)
                    aPlane = mathex.Flip(aPlane);

                var bInATransform       = math.mul(math.inverse(convexTransform), boxTransform);
                var bLocalContactNormal = math.InverseRotateFast(bInATransform.rot, -aLocalContactNormal);
                PointRayBox.BestFacePlanesAndVertices(in box, bLocalContactNormal, out var bEdgePlaneNormals, out _, out var bPlane, out var bVertices);
                bPlane                                 = mathex.TransformPlane(bInATransform, bPlane);
                bVertices                              = simd.transform(bInATransform, bVertices);
                bEdgePlaneNormals                      = simd.mul(bInATransform.rot, bEdgePlaneNormals);
                var  bEdgePlaneDistances               = simd.dot(bEdgePlaneNormals, bVertices.bcda);
                bool needsClosestPoint                 = true;
                var  distanceScalarAlongContactNormalB = math.rcp(math.dot(-aLocalContactNormal, bPlane.normal));

                bool projectBOnA        = math.abs(math.dot(aPlane.normal, aLocalContactNormal)) < 0.05f;
                int4 positiveSideCounts = 0;
                int4 negativeSideCounts = 0;

                UnityContactManifoldExtra2D result = default;
                result.baseStorage.contactNormal   = contactNormal;

                // Project and clip edges of A onto the face of B.
                for (int edgeIndex = 0; edgeIndex < indices2D.Length; edgeIndex++)
                {
                    var edgeTail  = edgeIndex + 1;
                    edgeTail     -= math.select(0, indices2D.Length, edgeTail >= indices2D.Length);
                    float3 rayStart;
                    float3 rayDisplacement;
                    if (bitmask == 6)
                    {
                        rayStart        = new float3(0f, blob.verticesY[edgeIndex], blob.verticesZ[edgeIndex]) * convex.scale;
                        rayDisplacement = new float3(0f, blob.verticesY[edgeTail], blob.verticesZ[edgeTail]) * convex.scale - rayStart;
                    }
                    else if (bitmask == 5)
                    {
                        rayStart        = new float3(blob.verticesX[edgeIndex], 0f, blob.verticesZ[edgeIndex]) * convex.scale;
                        rayDisplacement = new float3(blob.verticesX[edgeTail], 0f, blob.verticesZ[edgeTail]) * convex.scale - rayStart;
                    }
                    else  // bitmask == 3
                    {
                        rayStart        = new float3(blob.verticesX[edgeIndex], blob.verticesY[edgeIndex], 0f) * convex.scale;
                        rayDisplacement = new float3(blob.verticesX[edgeTail], blob.verticesY[edgeTail], 0f) * convex.scale - rayStart;
                    }
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
                        result.Add(math.transform(convexTransform, clippedSegmentA + aLocalContactNormal * aDistance), aDistance);
                        needsClosestPoint &= aDistance > distanceResult.distance + 1e-4f;
                        if (fractionB < 1f)  // Avoid duplication when vertex is not clipped
                        {
                            var clippedSegmentB = rayStart + fractionB * rayDisplacement;
                            var bDistance       = mathex.SignedDistance(bPlane, clippedSegmentB) * distanceScalarAlongContactNormalB;
                            result.Add(math.transform(convexTransform, clippedSegmentB + aLocalContactNormal * bDistance), bDistance);
                            needsClosestPoint &= bDistance > distanceResult.distance + 1e-4f;
                        }
                    }

                    // Inside for each edge of A
                    if (projectBOnA)
                    {
                        var aEdgePlaneNormal   = math.cross(rayDisplacement, aLocalContactNormal);
                        var edgePlaneDistance  = math.dot(aEdgePlaneNormal, rayStart);
                        var projection         = simd.dot(bVertices, aEdgePlaneNormal);
                        positiveSideCounts    += math.select(int4.zero, 1, projection > edgePlaneDistance);
                        negativeSideCounts    += math.select(int4.zero, 1, projection < edgePlaneDistance);
                    }
                }
                if (projectBOnA)
                {
                    var distanceScalarAlongContactNormalA = math.rcp(math.dot(aLocalContactNormal, aPlane.normal));
                    var bProjectsOnA                      = math.min(positiveSideCounts, negativeSideCounts) == 0;
                    for (int i = 0; i < 4; i++)
                    {
                        var vertex = bVertices[i];
                        if (bProjectsOnA[i])
                        {
                            var distance = mathex.SignedDistance(aPlane, vertex) * distanceScalarAlongContactNormalA;
                            result.Add(math.transform(convexTransform, vertex), distance);
                            needsClosestPoint &= distance > distanceResult.distance + 1e-4f;
                        }
                    }
                }

                var requiredContacts = math.select(32, 31, needsClosestPoint);
                if (result.baseStorage.contactCount <= requiredContacts)
                {
                    if (needsClosestPoint)
                        result.baseStorage.Add(distanceResult.hitpointB, distanceResult.distance);
                    return result.baseStorage;
                }

                // Simplification required
                Span<byte>   indices           = stackalloc byte[requiredContacts];
                Span<float3> projectedContacts = stackalloc float3[result.baseStorage.contactCount];
                for (int i = 0; i < result.baseStorage.contactCount; i++)
                {
                    projectedContacts[i] = result[i].location;
                }
                var                            finalVertexCount = ExpandingPolygonBuilder2D.Build(ref indices, projectedContacts, bPlane.normal);
                UnitySim.ContactsBetweenResult finalResult      = default;
                finalResult.contactNormal                       = result.baseStorage.contactNormal;
                for (int i = 0; i < finalVertexCount; i++)
                {
                    finalResult.Add(result[indices[i]]);
                }
                if (needsClosestPoint)
                    finalResult.Add(distanceResult.hitpointB, distanceResult.distance);
                return finalResult;
            }
        }

        unsafe struct UnityContactManifoldExtra2D
        {
            public UnitySim.ContactsBetweenResult baseStorage;
            public fixed float                    extraContactsData[672];

            public ref UnitySim.ContactsBetweenResult.ContactOnB this[int index]
            {
                get
                {
                    if (index < 32)
                    {
                        fixed (void* ptr = baseStorage.contactsData)
                        return ref ((UnitySim.ContactsBetweenResult.ContactOnB*)ptr)[index];
                    }
                    else
                    {
                        fixed (void* ptr = extraContactsData)
                        return ref ((UnitySim.ContactsBetweenResult.ContactOnB*)ptr)[index - 32];
                    }
                }
            }

            public void Add(float3 locationOnB, float distanceToA)
            {
                this[baseStorage.contactCount] = new UnitySim.ContactsBetweenResult.ContactOnB { location = locationOnB, distanceToA = distanceToA };
                baseStorage.contactCount++;
            }
        }

        unsafe struct UnityContactManifoldExtra3D
        {
            public UnitySim.ContactsBetweenResult baseStorage;
            public fixed float                    extraContactsData[16];

            public ref UnitySim.ContactsBetweenResult.ContactOnB this[int index]
            {
                get
                {
                    if (index < 32)
                    {
                        fixed (void* ptr = baseStorage.contactsData)
                        return ref ((UnitySim.ContactsBetweenResult.ContactOnB*)ptr)[index];
                    }
                    else
                    {
                        fixed (void* ptr = extraContactsData)
                        return ref ((UnitySim.ContactsBetweenResult.ContactOnB*)ptr)[index - 32];
                    }
                }
            }

            public void Add(float3 locationOnB, float distanceToA)
            {
                this[baseStorage.contactCount] = new UnitySim.ContactsBetweenResult.ContactOnB { location = locationOnB, distanceToA = distanceToA };
                baseStorage.contactCount++;
            }
        }
    }
}

