using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class ConvexConvex
    {
        public static bool DistanceBetween(in ConvexCollider convexA,
                                           in RigidTransform aTransform,
                                           in ConvexCollider convexB,
                                           in RigidTransform bTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            var bInATransform = math.mul(math.inverse(aTransform), bTransform);
            var gjkResult     = GjkEpa.DoGjkEpa(convexA, convexB, in bInATransform);
            var featureCodeA  = PointRayConvex.FeatureCodeFromGjk(gjkResult.simplexAVertexCount,
                                                                  gjkResult.simplexAVertexA,
                                                                  gjkResult.simplexAVertexB,
                                                                  gjkResult.simplexAVertexC,
                                                                  in convexA);
            var featureCodeB = PointRayConvex.FeatureCodeFromGjk(gjkResult.simplexBVertexCount,
                                                                 gjkResult.simplexBVertexA,
                                                                 gjkResult.simplexBVertexB,
                                                                 gjkResult.simplexBVertexC,
                                                                 in convexB);
            result = InternalQueryTypeUtilities.BinAResultToWorld(new ColliderDistanceResultInternal
            {
                distance  = gjkResult.distance,
                hitpointA = gjkResult.hitpointOnAInASpace,
                hitpointB = gjkResult.hitpointOnBInASpace,
                normalA   = PointRayConvex.ConvexNormalFromFeatureCode(featureCodeA, in convexA, -gjkResult.normalizedOriginToClosestCsoPoint),
                normalB   = math.rotate(bInATransform.rot, PointRayConvex.ConvexNormalFromFeatureCode(featureCodeB, in convexB,
                                                                                                      gjkResult.normalizedOriginToClosestCsoPoint)),
                featureCodeA = featureCodeA,
                featureCodeB = featureCodeB
            }, aTransform);
            return result.distance <= maxDistance;
        }

        public static bool ColliderCast(in ConvexCollider convexToCast,
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
            bool hit                          = Mpr.MprCastNoRoundness(convexToCast,
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
            DistanceBetween(in convexToCast, in casterHitTransform, in targetConvex, in targetConvexTransform, float.MaxValue, out var distanceResult);

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

        public static UnitySim.ContactsBetweenResult UnityContactsBetween(in ConvexCollider convexA,
                                                                          in RigidTransform aTransform,
                                                                          in ConvexCollider convexB,
                                                                          in RigidTransform bTransform,
                                                                          in ColliderDistanceResult distanceResult)
        {
            var contactNormal = math.normalizesafe((distanceResult.hitpointB - distanceResult.hitpointA) * math.select(1f, -1f, distanceResult.distance < 0f), float3.zero);
            if (contactNormal.Equals(float3.zero))
            {
                contactNormal = math.normalize(distanceResult.normalA - distanceResult.normalB);
            }
            var aLocalContactNormal = math.InverseRotateFast(aTransform.rot, contactNormal);
            contactNormal           = -contactNormal;

            ref var blobA       = ref convexA.convexColliderBlob.Value;
            float3  invScaleA   = math.rcp(convexA.scale);
            var     bitmaskA    = math.bitmask(new bool4(math.isfinite(invScaleA), false));
            var     dimensionsA = math.countbits(bitmaskA);

            ref var blobB       = ref convexB.convexColliderBlob.Value;
            float3  invScaleB   = math.rcp(convexB.scale);
            var     bitmaskB    = math.bitmask(new bool4(math.isfinite(invScaleB), false));
            var     dimensionsB = math.countbits(bitmaskB);

            if (dimensionsA >= 2 && dimensionsB >= 2)
            {
                Plane facePlaneA = default;
                int   faceIndexA = 0;
                int   edgeCountA = 0;
                Plane facePlaneB = default;
                int   faceIndexB = 0;
                int   edgeCountB = 0;

                if (dimensionsA == 3)
                {
                    PointRayConvex.BestFacePlane(ref blobA, aLocalContactNormal * invScaleA, distanceResult.featureCodeA, out facePlaneA, out faceIndexA, out edgeCountA);
                }
                else
                {
                    edgeCountA = blobA.yz2DVertexIndices.Length;  // bitmask = 6
                    facePlaneA = new Plane(new float3(1f, 0f, 0f), 0f);
                    if (bitmaskA == 5)
                    {
                        edgeCountA = blobA.xz2DVertexIndices.Length;
                        facePlaneA = new Plane(new float3(0f, 1f, 0f), 0f);
                    }
                    else if (bitmaskA == 3)
                    {
                        edgeCountA = blobA.xy2DVertexIndices.Length;
                        facePlaneA = new Plane(new float3(0f, 0f, 1f), 0f);
                    }
                    if (math.dot(facePlaneA.normal, aLocalContactNormal) < 0f)
                        facePlaneA = mathex.Flip(facePlaneA);
                }
                var              aStackCount = CollectionHelper.Align(edgeCountA, 4) / 4;
                Span<simdFloat3> verticesA   = stackalloc simdFloat3[aStackCount];
                Span<SimdPlane>  edgePlanesA = stackalloc SimdPlane[aStackCount];
                if (dimensionsA == 3)
                {
                    var edgeIndicesStartAndCount = blobA.edgeIndicesInFacesStartsAndCounts[faceIndexA];
                    for (int batch = 0; batch < aStackCount; batch++)
                    {
                        var wrappedEdgeIndices  = edgeIndicesStartAndCount.start + ((4 * batch + new int4(0, 1, 2, 3)) % edgeIndicesStartAndCount.count);
                        var edgeIndexInFace     = blobA.edgeIndicesInFaces[wrappedEdgeIndices.x];
                        var edge                = blobA.vertexIndicesInEdges[edgeIndexInFace.index];
                        var vertexIndex         = edgeIndexInFace.flipped ? edge.y : edge.x;
                        verticesA[batch].a      = new float3(blobA.verticesX[vertexIndex], blobA.verticesY[vertexIndex], blobA.verticesZ[vertexIndex]);
                        edgeIndexInFace         = blobA.edgeIndicesInFaces[wrappedEdgeIndices.y];
                        edge                    = blobA.vertexIndicesInEdges[edgeIndexInFace.index];
                        vertexIndex             = edgeIndexInFace.flipped ? edge.y : edge.x;
                        verticesA[batch].b      = new float3(blobA.verticesX[vertexIndex], blobA.verticesY[vertexIndex], blobA.verticesZ[vertexIndex]);
                        edgeIndexInFace         = blobA.edgeIndicesInFaces[wrappedEdgeIndices.z];
                        edge                    = blobA.vertexIndicesInEdges[edgeIndexInFace.index];
                        vertexIndex             = edgeIndexInFace.flipped ? edge.y : edge.x;
                        verticesA[batch].c      = new float3(blobA.verticesX[vertexIndex], blobA.verticesY[vertexIndex], blobA.verticesZ[vertexIndex]);
                        edgeIndexInFace         = blobA.edgeIndicesInFaces[wrappedEdgeIndices.w];
                        edge                    = blobA.vertexIndicesInEdges[edgeIndexInFace.index];
                        vertexIndex             = edgeIndexInFace.flipped ? edge.y : edge.x;
                        verticesA[batch].d      = new float3(blobA.verticesX[vertexIndex], blobA.verticesY[vertexIndex], blobA.verticesZ[vertexIndex]);
                        verticesA[batch]       *= convexA.scale;
                        var tails               = verticesA[batch].bcda;
                        vertexIndex             = edgeIndexInFace.flipped ? edge.x : edge.y;
                        tails.a                 = new float3(blobA.verticesX[vertexIndex], blobA.verticesY[vertexIndex], blobA.verticesZ[vertexIndex]) * convexA.scale;

                        // The average of 4 vertices should be a point inside the face
                        // to help get the correct outward edge planes.
                        var midpoint               = simd.csumabcd(verticesA[batch]) / 4f;
                        var edgeDisplacements      = tails - verticesA[batch];
                        edgePlanesA[batch].normals = simd.cross(edgeDisplacements, aLocalContactNormal);
                        edgePlanesA[batch].normals =
                            simd.select(-edgePlanesA[batch].normals, edgePlanesA[batch].normals, simd.dot(edgePlanesA[batch].normals, midpoint - verticesA[batch]) > 0f);
                        edgePlanesA[batch].distancesFromOrigin = simd.dot(edgePlanesA[batch].normals, tails);
                    }
                }
                else
                {
                    ref var indices2D = ref blobA.yz2DVertexIndices;  // bitmask = 6
                    if (bitmaskA == 5)
                        indices2D = ref blobA.xz2DVertexIndices;
                    else if (bitmaskA == 3)
                        indices2D = ref blobA.xy2DVertexIndices;
                    for (int batch = 0; batch < aStackCount; batch++)
                    {
                        var  i                   = batch * 4;
                        int4 indices             = i + new int4(0, 1, 2, 3);
                        indices                  = math.select(indices, indices - indices2D.Length, indices >= indices2D.Length);
                        var        extraIndex    = math.select(i + 4, i + 4 - indices2D.Length, i + 4 >= indices2D.Length);
                        simdFloat3 edgeVerticesA = default;
                        simdFloat3 edgeVerticesB = default;
                        if (bitmaskA == 6)
                        {
                            edgeVerticesA = new simdFloat3(float4.zero,
                                                           new float4(blobA.verticesY[indices.x], blobA.verticesY[indices.y], blobA.verticesY[indices.z],
                                                                      blobA.verticesY[indices.w]) * convexA.scale.y,
                                                           new float4(blobA.verticesZ[indices.x], blobA.verticesZ[indices.y], blobA.verticesZ[indices.z],
                                                                      blobA.verticesZ[indices.w]) * convexA.scale.z);
                            edgeVerticesB = new simdFloat3(float4.zero,
                                                           new float4(blobA.verticesY[indices.y], blobA.verticesY[indices.z], blobA.verticesY[indices.w],
                                                                      blobA.verticesY[extraIndex]) * convexA.scale.y,
                                                           new float4(blobA.verticesZ[indices.y], blobA.verticesZ[indices.z], blobA.verticesZ[indices.w],
                                                                      blobA.verticesZ[extraIndex]) * convexA.scale.z);
                        }
                        else if (bitmaskA == 5)
                        {
                            edgeVerticesA = new simdFloat3(new float4(blobA.verticesX[indices.x], blobA.verticesX[indices.y], blobA.verticesX[indices.z],
                                                                      blobA.verticesX[indices.w]) * convexA.scale.x,
                                                           float4.zero,
                                                           new float4(blobA.verticesZ[indices.x], blobA.verticesZ[indices.y], blobA.verticesZ[indices.z],
                                                                      blobA.verticesZ[indices.w]) * convexA.scale.z);
                            edgeVerticesB = new simdFloat3(new float4(blobA.verticesX[indices.y], blobA.verticesX[indices.z], blobA.verticesX[indices.w],
                                                                      blobA.verticesX[extraIndex]) * convexA.scale.x,
                                                           float4.zero,
                                                           new float4(blobA.verticesZ[indices.y], blobA.verticesZ[indices.z], blobA.verticesZ[indices.w],
                                                                      blobA.verticesZ[extraIndex]) * convexA.scale.z);
                        }
                        else  // bitmask == 3
                        {
                            edgeVerticesA = new simdFloat3(new float4(blobA.verticesX[indices.x], blobA.verticesX[indices.y], blobA.verticesX[indices.z],
                                                                      blobA.verticesX[indices.w]) * convexA.scale.x,
                                                           new float4(blobA.verticesY[indices.x], blobA.verticesY[indices.y], blobA.verticesY[indices.z],
                                                                      blobA.verticesY[indices.w]) * convexA.scale.y,
                                                           float4.zero);
                            edgeVerticesB = new simdFloat3(new float4(blobA.verticesX[indices.y], blobA.verticesX[indices.z], blobA.verticesX[indices.w],
                                                                      blobA.verticesX[extraIndex]) * convexA.scale.x,
                                                           new float4(blobA.verticesY[indices.y], blobA.verticesY[indices.z], blobA.verticesY[indices.w],
                                                                      blobA.verticesY[extraIndex]) * convexA.scale.y,
                                                           float4.zero);
                        }

                        // The average of all 8 vertices is the average of all the edge midpoints, which should be a point inside the face
                        // to help get the correct outward edge planes.
                        var midpoint           = simd.csumabcd(edgeVerticesA + edgeVerticesB) / 8f;
                        var edgeDisplacements  = edgeVerticesB - edgeVerticesA;
                        var edgePlaneNormals   = simd.cross(edgeDisplacements, aLocalContactNormal);
                        edgePlaneNormals       = simd.select(-edgePlaneNormals, edgePlaneNormals, simd.dot(edgePlaneNormals, midpoint - edgeVerticesA) > 0f);
                        var edgePlaneDistances = simd.dot(edgePlaneNormals, edgeVerticesB);

                        verticesA[batch]                       = edgeVerticesA;
                        edgePlanesA[batch].normals             = edgePlaneNormals;
                        edgePlanesA[batch].distancesFromOrigin = edgePlaneDistances;
                    }
                }

                var bInATransform = math.mul(math.inverse(aTransform), bTransform);
                if (dimensionsB == 3)
                {
                    var bLocalContactNormal = math.InverseRotateFast(bInATransform.rot, -aLocalContactNormal);
                    PointRayConvex.BestFacePlane(ref blobB, bLocalContactNormal * invScaleB, distanceResult.featureCodeB, out facePlaneB, out faceIndexB, out edgeCountB);
                }
                else
                {
                    edgeCountB = blobB.yz2DVertexIndices.Length;  // bitmask = 6
                    facePlaneB = new Plane(new float3(1f, 0f, 0f), 0f);
                    if (bitmaskB == 5)
                    {
                        edgeCountB = blobB.xz2DVertexIndices.Length;
                        facePlaneB = new Plane(new float3(0f, 1f, 0f), 0f);
                    }
                    else if (bitmaskB == 3)
                    {
                        edgeCountB = blobB.xy2DVertexIndices.Length;
                        facePlaneB = new Plane(new float3(0f, 0f, 1f), 0f);
                    }
                    if (math.dot(facePlaneB.normal, aLocalContactNormal) < 0f)
                        facePlaneB = mathex.Flip(facePlaneB);
                }
                var              bStackCount = CollectionHelper.Align(edgeCountB, 4) / 4;
                Span<simdFloat3> verticesB   = stackalloc simdFloat3[bStackCount];
                Span<SimdPlane>  edgePlanesB = stackalloc SimdPlane[bStackCount];
                if (dimensionsB == 3)
                {
                    var edgeIndicesStartBndCount = blobB.edgeIndicesInFacesStartsAndCounts[faceIndexB];
                    for (int batch = 0; batch < aStackCount; batch++)
                    {
                        var wrappedEdgeIndices = edgeIndicesStartBndCount.start + ((4 * batch + new int4(0, 1, 2, 3)) % edgeIndicesStartBndCount.count);
                        var edgeIndexInFace    = blobB.edgeIndicesInFaces[wrappedEdgeIndices.x];
                        var edge               = blobB.vertexIndicesInEdges[edgeIndexInFace.index];
                        var vertexIndex        = edgeIndexInFace.flipped ? edge.y : edge.x;
                        verticesB[batch].a     = new float3(blobB.verticesX[vertexIndex], blobB.verticesY[vertexIndex], blobB.verticesZ[vertexIndex]);
                        edgeIndexInFace        = blobB.edgeIndicesInFaces[wrappedEdgeIndices.y];
                        edge                   = blobB.vertexIndicesInEdges[edgeIndexInFace.index];
                        vertexIndex            = edgeIndexInFace.flipped ? edge.y : edge.x;
                        verticesB[batch].b     = new float3(blobB.verticesX[vertexIndex], blobB.verticesY[vertexIndex], blobB.verticesZ[vertexIndex]);
                        edgeIndexInFace        = blobB.edgeIndicesInFaces[wrappedEdgeIndices.z];
                        edge                   = blobB.vertexIndicesInEdges[edgeIndexInFace.index];
                        vertexIndex            = edgeIndexInFace.flipped ? edge.y : edge.x;
                        verticesB[batch].c     = new float3(blobB.verticesX[vertexIndex], blobB.verticesY[vertexIndex], blobB.verticesZ[vertexIndex]);
                        edgeIndexInFace        = blobB.edgeIndicesInFaces[wrappedEdgeIndices.w];
                        edge                   = blobB.vertexIndicesInEdges[edgeIndexInFace.index];
                        vertexIndex            = edgeIndexInFace.flipped ? edge.y : edge.x;
                        verticesB[batch].d     = new float3(blobB.verticesX[vertexIndex], blobB.verticesY[vertexIndex], blobB.verticesZ[vertexIndex]);
                        verticesB[batch]       = simd.transform(bInATransform, verticesB[batch] * convexB.scale);
                        var tails              = verticesB[batch].bcda;
                        vertexIndex            = edgeIndexInFace.flipped ? edge.x : edge.y;
                        tails.a                = math.transform(bInATransform,
                                                                new float3(blobB.verticesX[vertexIndex], blobB.verticesY[vertexIndex],
                                                                           blobB.verticesZ[vertexIndex]) * convexB.scale);

                        // The average of 4 vertices should be a point inside the face
                        // to help get the correct outward edge planes.
                        var midpoint               = simd.csumabcd(verticesB[batch]) / 4f;
                        var edgeDisplacements      = tails - verticesB[batch];
                        edgePlanesB[batch].normals = simd.cross(edgeDisplacements, aLocalContactNormal);
                        edgePlanesB[batch].normals =
                            simd.select(-edgePlanesB[batch].normals, edgePlanesB[batch].normals, simd.dot(edgePlanesB[batch].normals, midpoint - verticesB[batch]) > 0f);
                        edgePlanesB[batch].distancesFromOrigin = simd.dot(edgePlanesB[batch].normals, tails);
                    }
                }
                else
                {
                    ref var indices2D = ref blobB.yz2DVertexIndices;  // bitmask = 6
                    if (bitmaskB == 5)
                        indices2D = ref blobB.xz2DVertexIndices;
                    else if (bitmaskB == 3)
                        indices2D = ref blobB.xy2DVertexIndices;
                    for (int batch = 0; batch < aStackCount; batch++)
                    {
                        var  i                   = batch * 4;
                        int4 indices             = i + new int4(0, 1, 2, 3);
                        indices                  = math.select(indices, indices - indices2D.Length, indices >= indices2D.Length);
                        var        extraIndex    = math.select(i + 4, i + 4 - indices2D.Length, i + 4 >= indices2D.Length);
                        simdFloat3 edgeVerticesA = default;
                        simdFloat3 edgeVerticesB = default;
                        if (bitmaskB == 6)
                        {
                            edgeVerticesA = new simdFloat3(float4.zero,
                                                           new float4(blobB.verticesY[indices.x], blobB.verticesY[indices.y], blobB.verticesY[indices.z],
                                                                      blobB.verticesY[indices.w]) * convexB.scale.y,
                                                           new float4(blobB.verticesZ[indices.x], blobB.verticesZ[indices.y], blobB.verticesZ[indices.z],
                                                                      blobB.verticesZ[indices.w]) * convexB.scale.z);
                            edgeVerticesB = new simdFloat3(float4.zero,
                                                           new float4(blobB.verticesY[indices.y], blobB.verticesY[indices.z], blobB.verticesY[indices.w],
                                                                      blobB.verticesY[extraIndex]) * convexB.scale.y,
                                                           new float4(blobB.verticesZ[indices.y], blobB.verticesZ[indices.z], blobB.verticesZ[indices.w],
                                                                      blobB.verticesZ[extraIndex]) * convexB.scale.z);
                        }
                        else if (bitmaskB == 5)
                        {
                            edgeVerticesA = new simdFloat3(new float4(blobB.verticesX[indices.x], blobB.verticesX[indices.y], blobB.verticesX[indices.z],
                                                                      blobB.verticesX[indices.w]) * convexB.scale.x,
                                                           float4.zero,
                                                           new float4(blobB.verticesZ[indices.x], blobB.verticesZ[indices.y], blobB.verticesZ[indices.z],
                                                                      blobB.verticesZ[indices.w]) * convexB.scale.z);
                            edgeVerticesB = new simdFloat3(new float4(blobB.verticesX[indices.y], blobB.verticesX[indices.z], blobB.verticesX[indices.w],
                                                                      blobB.verticesX[extraIndex]) * convexB.scale.x,
                                                           float4.zero,
                                                           new float4(blobB.verticesZ[indices.y], blobB.verticesZ[indices.z], blobB.verticesZ[indices.w],
                                                                      blobB.verticesZ[extraIndex]) * convexB.scale.z);
                        }
                        else  // bitmask == 3
                        {
                            edgeVerticesA = new simdFloat3(new float4(blobB.verticesX[indices.x], blobB.verticesX[indices.y], blobB.verticesX[indices.z],
                                                                      blobB.verticesX[indices.w]) * convexB.scale.x,
                                                           new float4(blobB.verticesY[indices.x], blobB.verticesY[indices.y], blobB.verticesY[indices.z],
                                                                      blobB.verticesY[indices.w]) * convexB.scale.y,
                                                           float4.zero);
                            edgeVerticesB = new simdFloat3(new float4(blobB.verticesX[indices.y], blobB.verticesX[indices.z], blobB.verticesX[indices.w],
                                                                      blobB.verticesX[extraIndex]) * convexB.scale.x,
                                                           new float4(blobB.verticesY[indices.y], blobB.verticesY[indices.z], blobB.verticesY[indices.w],
                                                                      blobB.verticesY[extraIndex]) * convexB.scale.y,
                                                           float4.zero);
                        }

                        // The average of all 8 vertices is the average of all the edge midpoints, which should be a point inside the face
                        // to help get the correct outward edge planes.
                        var midpoint           = simd.csumabcd(edgeVerticesA + edgeVerticesB) / 8f;
                        var edgeDisplacements  = edgeVerticesB - edgeVerticesA;
                        var edgePlaneNormals   = simd.cross(edgeDisplacements, aLocalContactNormal);
                        edgePlaneNormals       = simd.select(-edgePlaneNormals, edgePlaneNormals, simd.dot(edgePlaneNormals, midpoint - edgeVerticesA) > 0f);
                        var edgePlaneDistances = simd.dot(edgePlaneNormals, edgeVerticesB);

                        verticesB[batch]                       = edgeVerticesA;
                        edgePlanesB[batch].normals             = edgePlaneNormals;
                        edgePlanesB[batch].distancesFromOrigin = edgePlaneDistances;
                    }
                }

                bool                      needsClosestPoint = true;
                UnityContactManifoldExtra result            = default;
                result.baseStorage.contactNormal            = contactNormal;
                if (math.abs(math.dot(facePlaneB.normal, aLocalContactNormal)) > 0.05f)
                {
                    var distanceScalarAlongContactNormalB = math.rcp(math.dot(aLocalContactNormal, facePlaneB.normal));

                    for (int edgeIndex = 0; edgeIndex < edgeCountA; edgeIndex++)
                    {
                        var rayStart        = verticesA[edgeIndex / 4][edgeIndex % 4];
                        var tailIndex       = edgeIndex + 1;
                        tailIndex           = math.select(tailIndex, 0, tailIndex == aStackCount * 4);
                        var rayDisplacement = verticesA[tailIndex / 4][tailIndex % 4] - rayStart;

                        float4 enterFractions = float4.zero;
                        float4 exitFractions  = 1f;
                        bool4  projectsOnFace = true;

                        for (int i = 0; i < bStackCount; i++)
                        {
                            var edgePlane          = edgePlanesB[i];
                            var rayRelativeStarts  = simd.dot(rayStart, edgePlane.normals) - edgePlane.distancesFromOrigin;
                            var relativeDiffs      = simd.dot(rayDisplacement, edgePlane.normals);
                            var rayRelativeEnds    = rayRelativeStarts + relativeDiffs;
                            var rayFractions       = math.select(-rayRelativeStarts / relativeDiffs, float4.zero, relativeDiffs == float4.zero);
                            var startsInside       = rayRelativeStarts <= 0f;
                            var endsInside         = rayRelativeEnds <= 0f;
                            projectsOnFace        &= startsInside | endsInside;
                            enterFractions         = math.select(float4.zero, rayFractions, !startsInside & rayFractions > enterFractions);
                            exitFractions          = math.select(1f, rayFractions, !endsInside & rayFractions < exitFractions);
                        }
                        var fractionA = math.cmax(enterFractions);
                        var fractionB = math.cmin(exitFractions);

                        if (math.all(projectsOnFace) && fractionA < fractionB)
                        {
                            // Add the two contacts from the possibly clipped segment
                            var clippedSegmentA = rayStart + fractionA * rayDisplacement;
                            var aDistance       = mathex.SignedDistance(facePlaneB, clippedSegmentA) * distanceScalarAlongContactNormalB;
                            result.Add(math.transform(aTransform, clippedSegmentA + aLocalContactNormal * aDistance), aDistance);
                            needsClosestPoint &= aDistance > distanceResult.distance + 1e-4f;
                            if (fractionB < 1f)  // Avoid duplication when vertex is not clipped
                            {
                                var clippedSegmentB = rayStart + fractionB * rayDisplacement;
                                var bDistance       = mathex.SignedDistance(facePlaneB, clippedSegmentB) * distanceScalarAlongContactNormalB;
                                result.Add(math.transform(aTransform, clippedSegmentB + aLocalContactNormal * bDistance), bDistance);
                                needsClosestPoint &= bDistance > distanceResult.distance + 1e-4f;
                            }
                        }
                    }

                    if (math.abs(math.dot(facePlaneA.normal, aLocalContactNormal)) < 0.05f)
                    {
                        var distanceScalarAlongContactNormalA = math.rcp(math.dot(aLocalContactNormal, facePlaneA.normal));
                        for (int vertexIndex = 0; vertexIndex < edgeCountB; vertexIndex++)
                        {
                            var   vertex      = verticesB[vertexIndex / 4][vertexIndex % 4];
                            bool4 projectsOnA = true;
                            for (int i = 0; i < bStackCount; i++)
                            {
                                var edgePlane  = edgePlanesA[i];
                                projectsOnA   &= simd.dot(edgePlane.normals, vertex) < edgePlane.distancesFromOrigin;
                            }
                            if (math.all(projectsOnA))
                            {
                                var distance = mathex.SignedDistance(facePlaneA, vertex) * distanceScalarAlongContactNormalA;
                                result.Add(math.transform(aTransform, vertex), distance);
                                needsClosestPoint &= distance > distanceResult.distance + 1e-4f;
                            }
                        }
                    }
                }
                else if (math.abs(math.dot(facePlaneA.normal, aLocalContactNormal)) > 0.05f)
                {
                    var distanceScalarAlongContactNormalA = math.rcp(math.dot(aLocalContactNormal, facePlaneA.normal));

                    for (int edgeIndex = 0; edgeIndex < edgeCountB; edgeIndex++)
                    {
                        var rayStart        = verticesA[edgeIndex / 4][edgeIndex % 4];
                        var tailIndex       = edgeIndex + 1;
                        tailIndex           = math.select(tailIndex, 0, tailIndex == aStackCount * 4);
                        var rayDisplacement = verticesA[tailIndex / 4][tailIndex % 4] - rayStart;

                        float4 enterFractions = float4.zero;
                        float4 exitFractions  = 1f;
                        bool4  projectsOnFace = true;

                        for (int i = 0; i < aStackCount; i++)
                        {
                            var edgePlane          = edgePlanesA[i];
                            var rayRelativeStarts  = simd.dot(rayStart, edgePlane.normals) - edgePlane.distancesFromOrigin;
                            var relativeDiffs      = simd.dot(rayDisplacement, edgePlane.normals);
                            var rayRelativeEnds    = rayRelativeStarts + relativeDiffs;
                            var rayFractions       = math.select(-rayRelativeStarts / relativeDiffs, float4.zero, relativeDiffs == float4.zero);
                            var startsInside       = rayRelativeStarts <= 0f;
                            var endsInside         = rayRelativeEnds <= 0f;
                            projectsOnFace        &= startsInside | endsInside;
                            enterFractions         = math.select(float4.zero, rayFractions, !startsInside & rayFractions > enterFractions);
                            exitFractions          = math.select(1f, rayFractions, !endsInside & rayFractions < exitFractions);
                        }
                        var fractionA = math.cmax(enterFractions);
                        var fractionB = math.cmin(exitFractions);

                        if (math.all(projectsOnFace) && fractionA < fractionB)
                        {
                            // Add the two contacts from the possibly clipped segment
                            var clippedSegmentA = rayStart + fractionA * rayDisplacement;
                            var aDistance       = mathex.SignedDistance(facePlaneA, clippedSegmentA) * distanceScalarAlongContactNormalA;
                            result.Add(math.transform(aTransform, clippedSegmentA), aDistance);
                            needsClosestPoint &= aDistance > distanceResult.distance + 1e-4f;
                            if (fractionB < 1f)  // Avoid duplication when vertex is not clipped
                            {
                                var clippedSegmentB = rayStart + fractionB * rayDisplacement;
                                var bDistance       = mathex.SignedDistance(facePlaneA, clippedSegmentB) * distanceScalarAlongContactNormalA;
                                result.Add(math.transform(aTransform, clippedSegmentA), bDistance);
                                needsClosestPoint &= bDistance > distanceResult.distance + 1e-4f;
                            }
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
                {
                    Span<byte>   indices           = stackalloc byte[requiredContacts];
                    Span<float3> projectedContacts = stackalloc float3[result.baseStorage.contactCount];
                    for (int i = 0; i < result.baseStorage.contactCount; i++)
                    {
                        projectedContacts[i] = result[i].location;
                    }
                    var                            finalVertexCount = ExpandingPolygonBuilder2D.Build(ref indices, projectedContacts, facePlaneB.normal);
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
            else if (dimensionsA == 0 || dimensionsB == 0)
            {
                return ContactManifoldHelpers.GetSingleContactManifold(in distanceResult);
            }
            else if (dimensionsB == 1)
            {
                var bAsCapsule = new CapsuleCollider(blobB.localAabb.min * convexB.scale, blobB.localAabb.max * convexB.scale, 0f);
                return CapsuleConvex.UnityContactsBetween(in convexA, in aTransform, in bAsCapsule, in bTransform, in distanceResult);
            }
            else  // if (dimensionsA == 1)
            {
                var aAsCapsule = new CapsuleCollider(blobA.localAabb.min * convexA.scale,
                                                     blobA.localAabb.max * convexA.scale,
                                                     0f);
                var result = CapsuleConvex.UnityContactsBetween(in convexB,
                                                                in bTransform,
                                                                in aAsCapsule,
                                                                in aTransform,
                                                                distanceResult.ToFlipped());
                result.FlipInPlace();
                return result;
            }
        }

        struct SimdPlane
        {
            public simdFloat3 normals;
            public float4     distancesFromOrigin;
        }

        unsafe struct UnityContactManifoldExtra
        {
            public UnitySim.ContactsBetweenResult baseStorage;
            public fixed float                    extraContactsData[4 * (512 - 32)];

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

