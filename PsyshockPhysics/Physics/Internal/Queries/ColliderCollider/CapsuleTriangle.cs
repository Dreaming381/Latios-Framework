using Latios.Calci;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class CapsuleTriangle
    {
        public static bool AreOverlapping(in TriangleCollider triangle,
                                          in RigidTransform triangleTransform,
                                          in CapsuleCollider capsule,
                                          in RigidTransform capsuleTransform)
        {
            return WithinDistance(in triangle, in triangleTransform, in capsule, in capsuleTransform, 0f);
        }

        public static bool WithinDistance(in TriangleCollider triangle,
                                          in RigidTransform triangleTransform,
                                          in CapsuleCollider capsule,
                                          in RigidTransform capsuleTransform,
                                          float maxDistance)
        {
            // Transform everything into world-space relative to the triangle
            var triPointsLocal       = triangle.AsSimdFloat3();
            var transformedTriPoints = simd.mul(triangleTransform.rot, triPointsLocal);
            var transformedCapsule   = new CapsuleCollider(capsuleTransform.pos - triangleTransform.pos + math.rotate(capsuleTransform.rot, capsule.pointA),
                                                           capsuleTransform.pos - triangleTransform.pos + math.rotate(capsuleTransform.rot, capsule.pointB),
                                                           capsule.radius);
            return TriangleCapsuleWithin(in transformedTriPoints, in transformedCapsule, maxDistance);
        }

        public static bool DistanceBetween(in TriangleCollider triangle,
                                           in RigidTransform triangleTransform,
                                           in CapsuleCollider capsule,
                                           in RigidTransform capsuleTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            // Transform everything into world-space relative to the triangle
            var triPointsLocal       = triangle.AsSimdFloat3();
            var transformedTriPoints = simd.mul(triangleTransform.rot, triPointsLocal);
            var transformedCapsule   = new CapsuleCollider(    capsuleTransform.pos - triangleTransform.pos + math.rotate(capsuleTransform.rot, capsule.pointA),
                                                               capsuleTransform.pos - triangleTransform.pos + math.rotate(capsuleTransform.rot, capsule.pointB),
                                                               capsule.radius);
            bool hit = TriangleCapsuleDistance(in transformedTriPoints, in transformedCapsule, maxDistance, out ColliderDistanceResultInternal localResult);
            result   = new ColliderDistanceResult
            {
                hitpointA         = localResult.hitpointA + triangleTransform.pos,
                hitpointB         = localResult.hitpointB + triangleTransform.pos,
                normalA           = localResult.normalA,
                normalB           = localResult.normalB,
                distance          = localResult.distance,
                subColliderIndexA = 0,
                subColliderIndexB = 0,
                featureCodeA      = localResult.featureCodeA,
                featureCodeB      = localResult.featureCodeB,
            };
            return hit;
        }

        public static bool ColliderCast(in CapsuleCollider capsuleToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in TriangleCollider targetTriangle,
                                        in RigidTransform targetTriangleTransform,
                                        out ColliderCastResult result)
        {
            if (AreOverlapping(in targetTriangle, in targetTriangleTransform, in capsuleToCast, in castStart))
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
            if (AreOverlapping(in triangleToCast, in castStart, in targetCapsule, in targetCapsuleTransform))
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
            simdFloat3 targetSimd = new simdFloat3(targetA, targetB, targetB, targetA);
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

                    // Unity Physics puts the contact points inside the capsule if the contact is close to the segment endpoint.
                    // Our version corrects this by finding the surface point along the contact vector.
                    var contactOnTrianglePlusRadiusA = clippedSegmentA + (aDistance + capsule.radius) * distanceResult.normalB;
                    var contactOnTrianglePlusRadiusB = clippedSegmentB + (bDistance + capsule.radius) * distanceResult.normalB;
                    var capsuleInTriangle            = new CapsuleCollider(rayStart, rayStart + rayDisplacement, capsule.radius);
                    PointRayCapsule.RaycastCapsule(new Ray(contactOnTrianglePlusRadiusA, clippedSegmentA), in capsuleInTriangle, out var contactFractionA, out _);
                    PointRayCapsule.RaycastCapsule(new Ray(contactOnTrianglePlusRadiusB, clippedSegmentB), in capsuleInTriangle, out var contactFractionB, out _);
                    clippedSegmentA = math.lerp(contactOnTrianglePlusRadiusA, clippedSegmentA, contactFractionA);
                    clippedSegmentB = math.lerp(contactOnTrianglePlusRadiusB, clippedSegmentB, contactFractionB);

                    result.Add(math.transform(triangleTransform, clippedSegmentA), math.distance(clippedSegmentA, contactOnTrianglePlusRadiusA));
                    result.Add(math.transform(triangleTransform, clippedSegmentB), math.distance(clippedSegmentB, contactOnTrianglePlusRadiusB));
                    needsClosestPoint = math.min(aDistance, bDistance) > distanceResult.distance + 1e-4f;  // Magic constant comes from Unity Physics
                }
            }

            if (needsClosestPoint)
                result.Add(distanceResult.hitpointB, distanceResult.distance);
            return result;
        }

        // Expects triPoints to be ABCA, which is what AsSimdFloat3() provides.
        internal static bool TriangleCapsuleWithin(in simdFloat3 triPoints, in CapsuleCollider capsule, float maxDistance)
        {
            simdFloat3 triPointsBcab = triPoints.bcab;
            CapsuleCapsule.SegmentSegment(in triPoints,
                                          triPointsBcab,
                                          new simdFloat3(capsule.pointA),
                                          new simdFloat3(capsule.pointB),
                                          out _,
                                          out _,
                                          out int isStartEndABx4,
                                          out var closestAToBPrecise);

            simdFloat3 edges = triPointsBcab - triPoints;

            // if counter-clockwise, normal faces "up"
            float3 planeNormal = math.normalizesafe(math.cross(edges.a, edges.c));
            if (Hint.Unlikely(planeNormal.Equals(float3.zero)))
            {
                // Triangle is degenerate. Find the two endpoints.
                float4 edgeLengthsSq           = simd.lengthsq(edges);
                int    bestDegenerateEdgeIndex = math.tzcnt(math.bitmask(math.cmax(edgeLengthsSq) == edgeLengthsSq));
                // Todo: Inline the Within() variant.
                var  triCapsule    = new CapsuleCollider(triPoints[bestDegenerateEdgeIndex], triPointsBcab[bestDegenerateEdgeIndex], 0f);
                bool degenerateHit = CapsuleCapsule.CapsuleCapsuleDistance(in triCapsule, in capsule, maxDistance, out _);
                return degenerateHit;
            }

            // calculate edge planes aligned with triangle normal without the normalization (since it isn't required)
            // normals face "inward"
            simdFloat3 edgeUnnormals = simd.cross(edges, planeNormal);

            simdFloat3 vpa        = capsule.pointA - triPoints;
            simdFloat3 vpb        = capsule.pointB - triPoints;
            float4     aDots      = simd.dot(edgeUnnormals, vpa);
            float4     bDots      = simd.dot(edgeUnnormals, vpb);
            bool       aFaceValid = math.all(aDots > 0f);
            bool       bFaceValid = math.all(bDots > 0f);

            // project each point onto plane
            float signedDistanceA = math.dot(vpa.a, planeNormal);
            float signedDistanceB = math.dot(vpb.a, planeNormal);
            bool  oppositeOfPlane = math.min(signedDistanceA, signedDistanceB) < 0f && math.max(signedDistanceA, signedDistanceB) > 0f;
            float pierceFraction  = signedDistanceA / (signedDistanceA - signedDistanceB);
            // Instead of lerping to find the capsule axis and testing new dot products, we can just lerp the dot products.
            bool  pierced            = oppositeOfPlane && math.all(aDots + (bDots - aDots) * pierceFraction >= 0f);
            float faceAxisDistance   = float.MaxValue;
            float faceResultDistance = float.MaxValue;
            if (aFaceValid || bFaceValid)
            {
                bool isFaceHitValid = pierced || !oppositeOfPlane;
                if (isFaceHitValid)
                {
                    bool  useCapB        = bFaceValid && (!aFaceValid || math.abs(signedDistanceB) < math.abs(signedDistanceA));
                    float signedDistance = math.select(signedDistanceA, signedDistanceB, useCapB);
                    faceAxisDistance     = math.abs(signedDistance);
                    faceResultDistance   = math.select(math.abs(signedDistance), -math.abs(signedDistance), oppositeOfPlane) - capsule.radius;

                    if (aFaceValid && bFaceValid && !pierced)
                    {
                        // No edge can be closer than this. Return early.
                        return faceResultDistance <= maxDistance;
                    }
                }
                else
                {
                    aFaceValid = false;
                    bFaceValid = false;
                }
            }

            int4  endpointFlagsSplit = new int4(isStartEndABx4, isStartEndABx4 >> 4, isStartEndABx4 >> 8, isStartEndABx4 >> 12) & 0xf;
            bool4 isEdgeStart        = (endpointFlagsSplit & 1) != 0;
            bool4 isEdgeEnd          = (endpointFlagsSplit & 2) != 0;
            bool4 isAxisStart        = (endpointFlagsSplit & 4) != 0;
            bool4 isAxisEnd          = (endpointFlagsSplit & 8) != 0;

            float4 edgeDistances = simd.length(closestAToBPrecise);
            if (!pierced && math.all(new float4(faceAxisDistance, edgeDistances.yzw) - capsule.radius > maxDistance))
            {
                return false;
            }

            float4 edgePreciseDotEdgeNormals = simd.dot(edgeUnnormals, closestAToBPrecise);
            // A triangle vertex is only closest when both its edges end up getting clamped to it when computing the nearest point.
            bool4 vertexValids = (isEdgeStart & isEdgeEnd.zxyz) | (isEdgeEnd & isEdgeStart.yzxy);
            // Edge hitpoints when the capsule axis is outside are only valid if it hits a valid vertex or is outside or on the edge not on a vertex.
            bool4 unpiercedEdgesValids = math.select(edgePreciseDotEdgeNormals <= 0f, vertexValids, isEdgeStart | isEdgeEnd);
            // Edge hitpoints when the capsule axis pierces are only valid if the hitpoint is not on an edge endpoint.
            bool4 piercedEdgesValids = !(isEdgeStart | isEdgeEnd | isAxisStart | isAxisEnd);
            bool4 edgeValids         = math.select(unpiercedEdgesValids, piercedEdgesValids, pierced);

            bool faceValid = aFaceValid || bFaceValid;
            // If rounding causes us to rule out all candidates, make all edges candidates
            edgeValids |= !math.any(edgeValids) & !faceValid;

            float4 edgeScores    = math.select(float.MaxValue, edgeDistances, edgeValids);
            float  bestEdgeScore = math.cmin(edgeScores);
            if (faceAxisDistance <= bestEdgeScore)
            {
                return faceResultDistance <= maxDistance;
            }

            int   bestEdgeIndex       = math.tzcnt(math.bitmask(edgeScores == bestEdgeScore));
            float closestEdgeDistance = edgeDistances[bestEdgeIndex];
            float resultDistance      = math.select(closestEdgeDistance, -closestEdgeDistance, pierced) - capsule.radius;
            return resultDistance <= maxDistance;
        }

        // Expects triPoints to be ABCA, which is what AsSimdFloat3() provides.
        internal static bool TriangleCapsuleDistance(in simdFloat3 triPoints, in CapsuleCollider capsule, float maxDistance, out ColliderDistanceResultInternal result)
        {
            simdFloat3 triPointsBcab = triPoints.bcab;
            CapsuleCapsule.SegmentSegment(in triPoints,
                                          triPointsBcab,
                                          new simdFloat3(capsule.pointA),
                                          new simdFloat3(capsule.pointB),
                                          out var closestTriEdges,
                                          out var closestAxisToEdges,
                                          out int isStartEndABx4,
                                          out var closestAToBPrecise);

            simdFloat3 edges = triPointsBcab - triPoints;

            // if counter-clockwise, normal faces "up"
            float3 planeNormal = math.normalizesafe(math.cross(edges.a, edges.c));
            if (Hint.Unlikely(planeNormal.Equals(float3.zero)))
            {
                // Triangle is degenerate. Find the two endpoints.
                float4 edgeLengthsSq           = simd.lengthsq(edges);
                int    bestDegenerateEdgeIndex = math.tzcnt(math.bitmask(math.cmax(edgeLengthsSq) == edgeLengthsSq));
                var    triCapsule              = new CapsuleCollider(triPoints[bestDegenerateEdgeIndex], triPointsBcab[bestDegenerateEdgeIndex], 0f);
                bool   degenerateHit           = CapsuleCapsule.CapsuleCapsuleDistance(in triCapsule, in capsule, maxDistance, out result);
                if (result.featureCodeA == 0x4000)
                    result.featureCodeA += (ushort)bestDegenerateEdgeIndex;
                else
                    result.featureCodeA = (ushort)((result.featureCodeA + bestDegenerateEdgeIndex) % 3);
                return degenerateHit;
            }

            // calculate edge planes aligned with triangle normal without the normalization (since it isn't required)
            // normals face "inward"
            simdFloat3 edgeUnnormals = simd.cross(edges, planeNormal);

            simdFloat3 vpa        = capsule.pointA - triPoints;
            simdFloat3 vpb        = capsule.pointB - triPoints;
            float4     aDots      = simd.dot(edgeUnnormals, vpa);
            float4     bDots      = simd.dot(edgeUnnormals, vpb);
            bool       aFaceValid = math.all(aDots > 0f);
            bool       bFaceValid = math.all(bDots > 0f);

            // project each point onto plane
            float signedDistanceA = math.dot(vpa.a, planeNormal);
            float signedDistanceB = math.dot(vpb.a, planeNormal);
            bool  oppositeOfPlane = math.min(signedDistanceA, signedDistanceB) < 0f && math.max(signedDistanceA, signedDistanceB) > 0f;
            float pierceFraction  = signedDistanceA / (signedDistanceA - signedDistanceB);
            // Instead of lerping to find the capsule axis and testing new dot products, we can just lerp the dot products.
            bool                           pierced          = oppositeOfPlane && math.all(aDots + (bDots - aDots) * pierceFraction >= 0f);
            float                          faceAxisDistance = float.MaxValue;
            ColliderDistanceResultInternal faceResult       = default;
            if (aFaceValid || bFaceValid)
            {
                bool isFaceHitValid = pierced || !oppositeOfPlane;
                if (isFaceHitValid)
                {
                    bool  useCapB        = bFaceValid && (!aFaceValid || math.abs(signedDistanceB) < math.abs(signedDistanceA));
                    float signedDistance = math.select(signedDistanceA, signedDistanceB, useCapB);
                    faceAxisDistance     = math.abs(signedDistance);
                    float3 capPoint      = math.select(capsule.pointA, capsule.pointB, useCapB);
                    // If one cap point is on the triangle surface, we sum both distances to get the sign.
                    // Otherwise, we separate the negative signed distance, which happens to be the signe of the other endpoint's signed distance
                    float separationSignRelativeToPlaneNormal = math.select(signedDistanceA + signedDistanceB,
                                                                            math.select(signedDistanceB, signedDistanceA, useCapB),
                                                                            oppositeOfPlane);
                    float3 separationDirection = math.chgsign(planeNormal, separationSignRelativeToPlaneNormal);
                    faceResult                 = new ColliderDistanceResultInternal
                    {
                        distance     = math.select(math.abs(signedDistance), -math.abs(signedDistance), oppositeOfPlane) - capsule.radius,
                        hitpointA    = capPoint - signedDistance * planeNormal,
                        hitpointB    = capPoint - capsule.radius * separationDirection,
                        normalA      = separationDirection,
                        normalB      = -separationDirection,
                        featureCodeA = 0x8000,
                        featureCodeB = (ushort)math.select(0, 1, useCapB)
                    };

                    if (aFaceValid && bFaceValid && !pierced)
                    {
                        // No edge can be closer than this. Return early.
                        result = faceResult;
                        return result.distance <= maxDistance;
                    }
                }
                else
                {
                    aFaceValid = false;
                    bFaceValid = false;
                }
            }

            int4  endpointFlagsSplit = new int4(isStartEndABx4, isStartEndABx4 >> 4, isStartEndABx4 >> 8, isStartEndABx4 >> 12) & 0xf;
            bool4 isEdgeStart        = (endpointFlagsSplit & 1) != 0;
            bool4 isEdgeEnd          = (endpointFlagsSplit & 2) != 0;
            bool4 isAxisStart        = (endpointFlagsSplit & 4) != 0;
            bool4 isAxisEnd          = (endpointFlagsSplit & 8) != 0;

            float4 edgeDistances = simd.length(closestAToBPrecise);
            if (!pierced && math.all(new float4(faceAxisDistance, edgeDistances.yzw) - capsule.radius > maxDistance))
            {
                result = default;
                return false;
            }

            float4 edgePreciseDotEdgeNormals = simd.dot(edgeUnnormals, closestAToBPrecise);
            // A triangle vertex is only closest when both its edges end up getting clamped to it when computing the nearest point.
            bool4 vertexValids = (isEdgeStart & isEdgeEnd.zxyz) | (isEdgeEnd & isEdgeStart.yzxy);
            // Edge hitpoints when the capsule axis is outside are only valid if it hits a valid vertex or is outside or on the edge not on a vertex.
            bool4 unpiercedEdgesValids = math.select(edgePreciseDotEdgeNormals <= 0f, vertexValids, isEdgeStart | isEdgeEnd);
            // Edge hitpoints when the capsule axis pierces are only valid if the hitpoint is not on an edge endpoint.
            bool4 piercedEdgesValids = !(isEdgeStart | isEdgeEnd | isAxisStart | isAxisEnd);
            bool4 edgeValids         = math.select(unpiercedEdgesValids, piercedEdgesValids, pierced);

            bool faceValid = aFaceValid || bFaceValid;
            // If rounding causes us to rule out all candidates, make all edges candidates
            edgeValids |= !math.any(edgeValids) & !faceValid;

            float4 edgeScores    = math.select(float.MaxValue, edgeDistances, edgeValids);
            float  bestEdgeScore = math.cmin(edgeScores);
            if (faceAxisDistance <= bestEdgeScore)
            {
                result = faceResult;
                return result.distance <= maxDistance;
            }

            int    bestEdgeIndex       = math.tzcnt(math.bitmask(edgeScores == bestEdgeScore));
            float3 closestOnAxis       = closestAxisToEdges[bestEdgeIndex];
            float3 closestOnEdge       = closestTriEdges[bestEdgeIndex];
            float  closestEdgeDistance = edgeDistances[bestEdgeIndex];
            bool   closestEdgeIsStart  = isEdgeStart[bestEdgeIndex];
            bool   closestEdgeIsEnd    = isEdgeEnd[bestEdgeIndex];
            float3 closestEdgeUnnormal = edgeUnnormals[bestEdgeIndex];
            float3 ulpScaleCap         = math.max(math.abs(capsule.pointA), math.abs(capsule.pointB));
            float3 ulpScaleTri         = math.max(math.abs(triPoints.x), math.max(math.abs(triPoints.y), math.abs(triPoints.z))).xyz;
            float  ulpScale            = math.cmax(math.max(ulpScaleCap, ulpScaleTri));

            float3 edgeSeparationDirection;
            if (closestEdgeDistance <= CapsuleCapsule.kSegmentSegmentPreciseMaxUlpErrorTolerance * ulpScale)
            {
                // Too close to get a valid normal for the capsule. Use the cross product of the two segments instead.
                float3 edge          = edges[bestEdgeIndex];
                float3 axis          = capsule.pointB - capsule.pointA;
                float3 perpendicular = math.cross(edge, axis);
                // A cross product's direction can have rounding error less than 4ulp (2f) divided by the sin of the angle between the input vectors.
                // The cross product's length is |edge|*|axis|*sin(angle). A little algebra results in the following metric:
                bool usePerpendicular   = math.lengthsq(perpendicular) > 2f * math.EPSILON * math.lengthsq(edge) * math.lengthsq(axis);
                edgeSeparationDirection = math.normalize(math.select(closestEdgeUnnormal, perpendicular, usePerpendicular));
                edgeSeparationDirection = math.chgsign(edgeSeparationDirection, -math.dot(edgeSeparationDirection, closestEdgeUnnormal));
            }
            else
            {
                edgeSeparationDirection = closestAToBPrecise[bestEdgeIndex] / math.select(closestEdgeDistance, -closestEdgeDistance, pierced);
            }
            float3 normalA      = -math.normalize(closestEdgeUnnormal);
            int    featureCodeA = 0x4000 + bestEdgeIndex;
            if (closestEdgeIsStart | closestEdgeIsEnd)
            {
                // The bestEdgeIndex + 1 doesn't actually need to be % 3 because edgeUnnormals[3] == edgeUnnormals[0].
                int otherEdge = math.select(bestEdgeIndex + 1, (bestEdgeIndex + 2) % 3, closestEdgeIsStart);
                normalA       = math.normalize(normalA - math.normalize(edgeUnnormals[otherEdge]));
                featureCodeA  = math.select((bestEdgeIndex + 1) % 3, bestEdgeIndex, closestEdgeIsStart);
            }
            result = new ColliderDistanceResultInternal
            {
                distance     = math.select(closestEdgeDistance, -closestEdgeDistance, pierced) - capsule.radius,
                hitpointA    = closestOnEdge,
                hitpointB    = closestOnAxis - edgeSeparationDirection * capsule.radius,
                normalA      = normalA,
                normalB      = -edgeSeparationDirection,
                featureCodeA = (ushort)featureCodeA,
                featureCodeB = (ushort)math.select(math.select(0x4000, 1, isAxisEnd[bestEdgeIndex]), 0, isAxisStart[bestEdgeIndex])
            };
            return result.distance <= maxDistance;
        }
    }
}

