using Latios.Calci;
using Unity.Burst;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class PointRayTriangle
    {
        public static bool AreOverlapping(float3 point, in TriangleCollider triangle, in RigidTransform triangleTransform)
        {
            var pointInTriangleSpace = math.InverseTransformFast(in triangleTransform, point);
            return PointTriangleOverlapping(pointInTriangleSpace, in triangle);
        }

        public static bool WithinDistance(float3 point, in TriangleCollider triangle, in RigidTransform triangleTransform, float maxDistance)
        {
            var pointInTriangleSpace = math.InverseTransformFast(in triangleTransform, point);
            return PointTriangleWithin(pointInTriangleSpace, in triangle, maxDistance);
        }

        public static bool DistanceBetween(float3 point, in TriangleCollider triangle, in RigidTransform triangleTransform, float maxDistance, out PointDistanceResult result)
        {
            var  pointInTriangleSpace = math.InverseTransformFast(in triangleTransform, point);
            bool hit                  = PointTriangleDistance(pointInTriangleSpace, in triangle, maxDistance, out var localResult);
            result                    = new PointDistanceResult
            {
                hitpoint = math.transform(triangleTransform, localResult.hitpoint),
                normal   = math.rotate(triangleTransform, localResult.normal),
                distance = localResult.distance
            };
            return hit;
        }

        public static bool Raycast(in Ray ray, in TriangleCollider triangle, in RigidTransform triangleTransform, out RaycastResult result)
        {
            var  rayInTriangleSpace = Ray.InverseTransformRay(in triangleTransform, ray);
            bool hit                = RaycastTriangle(in rayInTriangleSpace,
                                                      new simdFloat3(triangle.pointA, triangle.pointB, triangle.pointC, triangle.pointA),
                                                      out float fraction,
                                                      out float3 normal);
            result.position         = ray.start + ray.displacement * fraction;
            result.normal           = math.rotate(triangleTransform, normal);
            result.distance         = fraction * math.length(ray.displacement);
            result.subColliderIndex = 0;
            return hit;
        }

        internal static bool PointTriangleOverlapping(float3 point, in TriangleCollider triangle)
        {
            if (point.Equals(triangle.pointA) || point.Equals(triangle.pointB) || point.Equals(triangle.pointC))
                return true;
            var planeNormal = math.normalizesafe(math.cross(triangle.pointB - triangle.pointA, triangle.pointA - triangle.pointC));
            if (!planeNormal.Equals(float3.zero))
            {
                if (math.dot(point - triangle.pointA, planeNormal) == 0f)
                {
                    var vertices      = triangle.AsSimdFloat3();
                    var edges         = vertices.bcaa - vertices;
                    var vp            = point - vertices;
                    var edgeUnnormals = simd.cross(edges, planeNormal);
                    var dots          = simd.dot(edgeUnnormals, vp);
                    if (math.all(dots >= 0f))
                        return true;
                }
                return false;
            }
            // Triangle is degenerate. Find the two extreme points to do point-capsule.
            var             abDistSq = math.distancesq(triangle.pointA, triangle.pointB);
            var             bcDistSq = math.distancesq(triangle.pointB, triangle.pointC);
            var             caDistSq = math.distancesq(triangle.pointC, triangle.pointA);
            CapsuleCollider capsule;
            if (abDistSq >= bcDistSq && abDistSq >= caDistSq)
                capsule = new CapsuleCollider(triangle.pointA, triangle.pointB, 0f);
            else if (bcDistSq >= abDistSq && bcDistSq >= caDistSq)
                capsule = new CapsuleCollider(triangle.pointB, triangle.pointC, 0f);
            else
                capsule = new CapsuleCollider(triangle.pointC, triangle.pointA, 0f);
            return PointRayCapsule.PointCapsuleWithin(point, in capsule, 0f);
        }

        internal static bool PointTriangleWithin(float3 point, in TriangleCollider triangle, float maxDistance)
        {
            if (maxDistance < 0f)
                return false;

            simdFloat3 vertices = triangle.AsSimdFloat3();
            simdFloat3 edges    = vertices.bcaa - vertices;
            simdFloat3 vp       = point - vertices;

            // project point onto plane
            // if counter-clockwise, normal faces "up"
            float3 planeNormal = math.normalizesafe(math.cross(edges.a, edges.c));
            // calculate edge planes aligned with triangle normal without the normalization (since it isn't required)
            // normals face "inward"
            simdFloat3 edgeUnnormals = simd.cross(edges, planeNormal);
            float4     dots          = simd.dot(edgeUnnormals, vp);
            if (math.all(dots.xyz >= 0f) && !planeNormal.Equals(float3.zero))
            {
                // all inside, hit plane
                float projectionDot = math.dot(planeNormal, point - triangle.pointA);
                return math.abs(projectionDot) <= maxDistance;
            }

            // The closest point is on one of the edges. This also covers degenerate triangles.
            float4     edgeLengthsSq = simd.lengthsq(edges);
            float4     edgeDots      = math.clamp(simd.dot(vp, edges), 0f, edgeLengthsSq);
            simdFloat3 edgeHitpoints = vertices + edges * (edgeDots / edgeLengthsSq);
            edgeHitpoints            = simd.select(edgeHitpoints, vertices.bcaa, edgeDots == edgeLengthsSq);
            float distanceSq         = math.cmin(simd.distancesq(point, edgeHitpoints).xyz);
            return distanceSq <= maxDistance * maxDistance;
        }

        // Distance is unsigned, triangle is "double-sided"
        internal static bool PointTriangleDistance(float3 point, in TriangleCollider triangle, float maxDistance, out PointDistanceResultInternal result)
        {
            simdFloat3 vertices = triangle.AsSimdFloat3();
            simdFloat3 edges    = vertices.bcaa - vertices;
            simdFloat3 vp       = point - vertices;

            // project point onto plane
            // if counter-clockwise, normal faces "up"
            float3 planeNormal = math.normalizesafe(math.cross(edges.a, edges.c));
            // calculate edge planes aligned with triangle normal without the normalization (since it isn't required)
            // normals face "inward"
            simdFloat3 edgeUnnormals = simd.cross(edges, planeNormal);
            float4     dots          = simd.dot(edgeUnnormals, vp);

            if (planeNormal.Equals(float3.zero))
            {
                // On all three edges at once because the cross product was 0.
                // We can't use the typical edge-hit test because we don't have valid edge normals.
                CapsuleCollider capAB = new CapsuleCollider(triangle.pointA, triangle.pointB, 0f);
                bool            hitAB = PointRayCapsule.PointCapsuleDistance(point, in capAB, maxDistance, out var resultAB);
                CapsuleCollider capBC = new CapsuleCollider(triangle.pointB, triangle.pointC, 0f);
                bool            hitBC = PointRayCapsule.PointCapsuleDistance(point, in capBC, maxDistance, out var resultBC);
                resultBC.featureCode++;
                CapsuleCollider capCA  = new CapsuleCollider(triangle.pointC, triangle.pointA, 0f);
                bool            hitCA  = PointRayCapsule.PointCapsuleDistance(point, in capCA, maxDistance, out var resultCA);
                resultCA.featureCode  += (ushort)math.select(2, -1, (resultCA.featureCode & 0xff) == 1);
                if (!hitAB && !hitBC && !hitCA)
                {
                    result = resultCA;
                    return false;
                }

                result          = default;
                result.distance = float.MaxValue;

                if (hitAB)
                    result = resultAB;
                if (hitBC && resultBC.distance < result.distance)
                    result = resultBC;
                if (hitCA && resultCA.distance < result.distance)
                    result = resultCA;
            }
            else if (math.all(dots.xyz > 0f))
            {
                // all inside, hit plane
                float  projectionDot  = math.dot(planeNormal, point - triangle.pointA);
                float3 projectedPoint = point - projectionDot * planeNormal;
                result.hitpoint       = projectedPoint;
                result.distance       = math.abs(projectionDot);
                result.normal         = math.select(planeNormal, -planeNormal, math.dot(point - result.hitpoint, planeNormal) < 0);
                result.featureCode    = 0x8000;
            }
            else
            {
                // The closest point is on an edge. It may be at the edge endpoint, in which we classify as a vertex. We test all three edges.
                float4     edgeLengthsSq   = simd.lengthsq(edges);
                float4     edgeDots        = math.clamp(simd.dot(vp, edges), 0f, edgeLengthsSq);
                bool4      clampedToStarts = edgeDots == 0f;
                bool4      clampedToEnds   = edgeDots == edgeLengthsSq;
                simdFloat3 edgeHitpoints   = vertices + edges * (edgeDots / edgeLengthsSq);
                edgeHitpoints              = simd.select(edgeHitpoints, vertices.bcaa, edgeDots == edgeLengthsSq);
                float4 distancesSq         = simd.distancesq(point, edgeHitpoints);
                // Only a point within (and not on the boundary of) an edge's voronoi region can hold the closest point.
                // But we can run into scenarios where floating-point math doesn't distinguish distances, especially if the point is far away.
                // So we throw out bad distances and prioritize a voronoi interior.
                bool4  pointOutsideEdges       = new bool4((dots <= 0f).xyz, false);
                bool4  pointWithinEdgesVoronoi = pointOutsideEdges & !clampedToStarts & !clampedToEnds;
                float4 edgeScores              = math.select(float.MaxValue, distancesSq, pointOutsideEdges);
                edgeScores                     = math.select(edgeScores, -1f, pointWithinEdgesVoronoi);
                int   bestEdgeIndex            = math.tzcnt(math.bitmask(edgeScores == math.cmin(edgeScores)));
                float minDistanceSq            = distancesSq[bestEdgeIndex];
                bool  clampedToStart           = clampedToStarts[bestEdgeIndex];
                bool  clampedToEnd             = clampedToEnds[bestEdgeIndex];
                int   nextEdgeIndex            = math.select(bestEdgeIndex + 1, 0, bestEdgeIndex == 2);
                int   previousEdgeIndex        = math.select(bestEdgeIndex - 1, 2, bestEdgeIndex == 0);
                int   sharedEdgeIndex          = math.select(bestEdgeIndex, previousEdgeIndex, clampedToStart);
                sharedEdgeIndex                = math.select(sharedEdgeIndex, nextEdgeIndex, clampedToEnd);
                simdFloat3 edgeNormals         = simd.normalize(edgeUnnormals);
                result.hitpoint                = edgeHitpoints[bestEdgeIndex];
                result.distance                = math.sqrt(minDistanceSq);
                result.normal                  = math.normalize(-edgeNormals[bestEdgeIndex] - edgeNormals[sharedEdgeIndex]);
                int featureCode                = bestEdgeIndex + math.select(0x4000, 0, clampedToStart);
                featureCode                    = math.select(featureCode, nextEdgeIndex, clampedToEnd);
                result.featureCode             = (ushort)featureCode;
            }
            return result.distance <= maxDistance;
        }

        private static bool PointQuadWithin(float3 point, in simdFloat3 quadPoints, float maxDistance)
        {
            simdFloat3 edges = quadPoints.bcda - quadPoints;
            simdFloat3 vp    = point - quadPoints;

            // if clockwise, normal faces "up"
            float3 planeNormal = math.normalizesafe(math.cross(edges.a, edges.d));
            // normals face "inward"
            simdFloat3 edgeUnnormals = simd.cross(edges, planeNormal);
            float4     dots          = simd.dot(edgeUnnormals, vp);
            if (math.all(dots > 0f)) // Not equal, because we want to fall back to edge tests when the quad is degenerate
                return math.abs(math.dot(planeNormal, vp.a)) <= maxDistance;

            float4     edgeLengthsSq = simd.lengthsq(edges);
            float4     edgeDots      = math.clamp(simd.dot(vp, edges), 0f, edgeLengthsSq);
            simdFloat3 edgeHitpoints = quadPoints + edges * (edgeDots / edgeLengthsSq);
            edgeHitpoints            = simd.select(edgeHitpoints, quadPoints.bcda, edgeDots == edgeLengthsSq);
            float distanceSq         = math.cmin(simd.distancesq(point, edgeHitpoints));
            return distanceSq <= maxDistance * maxDistance;
        }

        // Mostly from Unity.Physics but handles more edge cases
        // Todo: Reduce branches
        internal static bool RaycastTriangle(in Ray ray, in simdFloat3 triPoints, out float fraction, out float3 outNormal)
        {
            simdFloat3 abbcca = triPoints.bcaa - triPoints;
            float3     ab     = abbcca.a;
            float3     ca     = triPoints.a - triPoints.c;
            float3     normal = math.cross(ab, ca);
            float3     aStart = ray.start - triPoints.a;
            float3     aEnd   = ray.end - triPoints.a;

            float nDotAStart    = math.dot(normal, aStart);
            float nDotAEnd      = math.dot(normal, aEnd);
            float productOfDots = nDotAStart * nDotAEnd;

            if (productOfDots < 0f)
            {
                // The start and end are on opposite sides of the infinite plane.
                fraction = nDotAStart / (nDotAStart - nDotAEnd);

                // These edge normals are relative to the ray, not the plane normal.
                simdFloat3 edgeNormals = simd.cross(abbcca, ray.displacement);

                // This is the midpoint of the segment to the start point, avoiding the divide by two.
                float3     doubleStart = ray.start + ray.start;
                simdFloat3 r           = doubleStart - (triPoints + triPoints.bcaa);
                float3     dots        = simd.dot(r, edgeNormals).xyz;
                outNormal              = math.select(normal, -normal, math.dot(normal, ray.displacement) >= 0f);
                outNormal              = math.normalize(outNormal);
                return math.all(dots <= 0f) || math.all(dots >= 0f);
            }
            else if (nDotAStart == 0f && nDotAEnd == 0f)
            {
                // The start and end are both on the infinite plane or the tri is degenerate.

                // Check for the degenerate case
                if (math.all(normal == 0f))
                {
                    normal = math.cross(triPoints.a - ray.start, ab);
                    if (math.dot(normal, ray.displacement) != 0f)
                    {
                        fraction  = 2f;
                        outNormal = default;
                        return false;
                    }
                }

                // Make sure the start isn't on the tri.
                simdFloat3 edgeNormals = simd.cross(abbcca, normal);
                float3     doubleStart = ray.start + ray.start;
                simdFloat3 r           = doubleStart - (triPoints + triPoints.bcaa);
                float3     dots        = simd.dot(r, edgeNormals).xyz;
                if (math.all(dots <= 0f) || math.all(dots >= 0f))
                {
                    fraction  = 2f;
                    outNormal = default;
                    return false;
                }

                // This is a rare case, so we are going to do something crazy to avoid trying to solve
                // line intersections in 3D space.
                // Instead, inflate the plane along the normal and raycast against the planes
                // In the case that the ray passes through one of the plane edges, this recursion will reach
                // three levels deep, and then a full plane will be constructed against the ray.
                // Todo: Would raycasting against capsules of 0 radius be sufficient?
                var    negPoints = triPoints - normal;
                var    posPoints = triPoints + normal;
                var    quadA     = new simdFloat3(negPoints.a, posPoints.a, posPoints.b, negPoints.b);
                var    quadB     = new simdFloat3(negPoints.b, posPoints.b, posPoints.c, negPoints.c);
                var    quadC     = new simdFloat3(negPoints.c, posPoints.c, posPoints.a, negPoints.a);
                bool3  hits      = default;
                float3 fractions = default;
                hits.x           = RaycastQuad(in ray, in quadA, out fractions.x);
                hits.y           = RaycastQuad(in ray, in quadB, out fractions.y);
                hits.z           = RaycastQuad(in ray, in quadC, out fractions.z);
                fractions        = math.select(2f, fractions, hits);
                fraction         = math.cmin(fractions);

                float3 bestEdge = abbcca[math.min(2, math.csum(math.select(0, new int3(0, 1, 2), fraction == fractions)))];
                outNormal       = math.cross(bestEdge, normal);
                outNormal       = math.select(outNormal, -outNormal, math.dot(outNormal, ray.displacement) >= 0f);
                outNormal       = math.normalizesafe(outNormal);

                return math.any(hits);
            }
            else if (nDotAStart == 0f)
            {
                // The start of the ray is on the infinite plane
                // And since we ignore inside hits, we ignore this too.
                fraction  = 2f;
                outNormal = default;
                return false;
            }
            else if (nDotAEnd == 0f)
            {
                // The end of the ray is on the infinite plane
                fraction               = 1f;
                simdFloat3 edgeNormals = simd.cross(abbcca, normal);
                float3     doubleEnd   = ray.end + ray.end;
                simdFloat3 r           = doubleEnd - (triPoints + triPoints.bcaa);
                float3     dots        = simd.dot(r, edgeNormals).xyz;
                outNormal              = math.select(normal, -normal, math.dot(normal, ray.displacement) >= 0f);
                outNormal              = math.normalizesafe(outNormal);
                return math.all(dots <= 0f) || math.all(dots >= 0f);
            }
            else
            {
                fraction  = 2f;
                outNormal = default;
                return false;
            }
        }

        internal static bool RaycastRoundedTriangle(in Ray ray, in simdFloat3 triPoints, float radius, out float fraction, out float3 normal)
        {
            // Make sure the ray doesn't start inside.
            if (PointTriangleWithin(ray.start, new TriangleCollider(triPoints.a, triPoints.b, triPoints.c), radius))
            {
                fraction = 2f;
                normal   = default;
                return false;
            }

            float3 ab            = triPoints.b - triPoints.a;
            float3 ca            = triPoints.a - triPoints.c;
            float3 planeUnnormal = math.cross(ab, ca);
            float3 triNormal     = math.select(planeUnnormal, -planeUnnormal, math.dot(planeUnnormal, ray.displacement) > 0f);

            // Catch degenerate tri here
            bool  triFaceHit  = math.any(triNormal);
            float triFraction = 2f;
            if (triFaceHit)
            {
                triNormal  = math.normalize(triNormal);
                triFaceHit = RaycastTriangle(in ray, triPoints + triNormal * radius, out triFraction, out _);
            }
            triFraction              = math.select(2f, triFraction, triFaceHit);
            bool4 capsuleHits        = PointRayCapsule.Raycast4Capsules(in ray, in triPoints, triPoints.bcaa, radius, out float4 capsuleFractions, out simdFloat3 capsuleNormals);
            capsuleFractions         = math.select(2f, capsuleFractions, capsuleHits);
            simdFloat3 bestNormals   = simd.select(capsuleNormals, capsuleNormals.bacc, capsuleFractions.yxzz < capsuleFractions);
            float4     bestFractions = math.select(capsuleFractions, capsuleFractions.yxzz, capsuleFractions.yxzz < capsuleFractions);
            normal                   = math.select(bestNormals.a, bestNormals.c, bestFractions.z < bestFractions.x);
            fraction                 = math.select(bestFractions.x, bestFractions.z, bestFractions.z < bestFractions.x);
            normal                   = math.select(normal, triNormal, triFraction < fraction);
            fraction                 = math.select(fraction, triFraction, triFraction < fraction);
            if (fraction > 1f)
                return false;

            normal = NormalFromRoundedRaycastHit(ray.start + ray.displacement * fraction,
                                                 in triPoints,
                                                 triPoints.bcaa,
                                                 new bool4(true, true, true, false),
                                                 planeUnnormal,
                                                 triNormal,
                                                 normal);
            return true;
        }

        // Mostly from Unity.Physics but handles more edge cases
        // Todo: Reduce branches
        internal static bool RaycastQuad(in Ray ray, in simdFloat3 quadPoints, out float fraction)
        {
            simdFloat3 abbccdda = quadPoints.bcda - quadPoints;
            float3     ab       = abbccdda.a;
            float3     ca       = quadPoints.a - quadPoints.c;
            float3     normal   = math.cross(ab, ca);
            float3     aStart   = ray.start - quadPoints.a;
            float3     aEnd     = ray.end - quadPoints.a;

            float nDotAStart    = math.dot(normal, aStart);
            float nDotAEnd      = math.dot(normal, aEnd);
            float productOfDots = nDotAStart * nDotAEnd;

            if (productOfDots < 0f)
            {
                // The start and end are on opposite sides of the infinite plane.
                fraction = nDotAStart / (nDotAStart - nDotAEnd);

                // These edge normals are relative to the ray, not the plane normal.
                simdFloat3 edgeNormals = simd.cross(abbccdda, ray.displacement);

                // This is the midpoint of the segment to the start point, avoiding the divide by two.
                float3     doubleStart = ray.start + ray.start;
                simdFloat3 r           = doubleStart - (quadPoints + quadPoints.bcda);
                float4     dots        = simd.dot(r, edgeNormals);
                return math.all(dots <= 0f) || math.all(dots >= 0f);
            }
            else if (nDotAStart == 0f && nDotAEnd == 0f)
            {
                // The start and end are both on the infinite plane or the quad is degenerate.

                // Check for the degenerate case
                if (math.all(normal == 0f))
                {
                    normal = math.cross(quadPoints.a - ray.start, ab);
                    if (math.dot(normal, ray.displacement) != 0f)
                    {
                        fraction = 2f;
                        return false;
                    }
                }

                // Make sure the start isn't on the quad.
                simdFloat3 edgeNormals = simd.cross(abbccdda, normal);
                float3     doubleStart = ray.start + ray.start;
                simdFloat3 r           = doubleStart - (quadPoints + quadPoints.bcda);
                float4     dots        = simd.dot(r, edgeNormals);
                if (math.all(dots <= 0f) || math.all(dots >= 0f))
                {
                    fraction = 2f;
                    return false;
                }

                // Todo: This is a rare case, so we are going to do something crazy to avoid trying to solve
                // line intersections in 3D space.
                // Instead, inflate the plane along the normal and raycast against the planes
                // In the case that the ray passes through one of the plane edges, this recursion will reach
                // three levels deep, and then a full plane will be constructed against the ray.
                // Todo: Would raycasting against capsules of 0 radius be sufficient?
                var    negPoints = quadPoints - normal;
                var    posPoints = quadPoints + normal;
                var    quadA     = new simdFloat3(negPoints.a, posPoints.a, posPoints.b, negPoints.b);
                var    quadB     = new simdFloat3(negPoints.b, posPoints.b, posPoints.c, negPoints.c);
                var    quadC     = new simdFloat3(negPoints.c, posPoints.c, posPoints.d, negPoints.d);
                var    quadD     = new simdFloat3(negPoints.d, posPoints.d, posPoints.a, negPoints.a);
                bool4  hits      = default;
                float4 fractions = default;
                hits.x           = RaycastQuad(in ray, in quadA, out fractions.x);
                hits.y           = RaycastQuad(in ray, in quadB, out fractions.y);
                hits.z           = RaycastQuad(in ray, in quadC, out fractions.z);
                hits.w           = RaycastQuad(in ray, in quadD, out fractions.w);
                fractions        = math.select(2f, fractions, hits);
                fraction         = math.cmin(fractions);
                return math.any(hits);
            }
            else if (nDotAStart == 0f)
            {
                // The start of the ray is on the infinite plane
                // And since we ignore inside hits, we ignore this too.
                fraction = 2f;
                return false;
            }
            else if (nDotAEnd == 0f)
            {
                // The end of the ray is on the infinite plane
                fraction               = 1f;
                simdFloat3 edgeNormals = simd.cross(abbccdda, normal);
                float3     doubleEnd   = ray.end + ray.end;
                simdFloat3 r           = doubleEnd - (quadPoints + quadPoints.bcda);
                float4     dots        = simd.dot(r, edgeNormals);
                return math.all(dots <= 0f) || math.all(dots >= 0f);
            }
            else
            {
                fraction = 2f;
                return false;
            }
        }

        internal static bool RaycastRoundedQuad(in Ray ray, in simdFloat3 quadPoints, float radius, out float fraction, out float3 normal)
        {
            // Make sure the ray doesn't start inside.
            if (PointQuadWithin(ray.start, in quadPoints, radius))
            {
                fraction = 2f;
                normal   = default;
                return false;
            }

            float3 ab            = quadPoints.b - quadPoints.a;
            float3 ca            = quadPoints.a - quadPoints.c;
            float3 planeUnnormal = math.cross(ab, ca);
            float3 quadNormal    = math.select(planeUnnormal, -planeUnnormal, math.dot(planeUnnormal, ray.displacement) > 0f);

            // Catch degenerate quad here
            bool  quadFaceHit  = math.any(quadNormal);
            float quadFraction = 2f;
            if (quadFaceHit)
            {
                quadNormal  = math.normalize(quadNormal);
                quadFaceHit = RaycastQuad(in ray, quadPoints + quadNormal * radius, out quadFraction);
            }
            quadFraction             = math.select(2f, quadFraction, quadFaceHit);
            bool4 capsuleHits        = PointRayCapsule.Raycast4Capsules(in ray, in quadPoints, quadPoints.bcda, radius, out float4 capsuleFractions, out simdFloat3 capsuleNormals);
            capsuleFractions         = math.select(2f, capsuleFractions, capsuleHits);
            simdFloat3 bestNormals   = simd.select(capsuleNormals, capsuleNormals.badc, capsuleFractions.yxwz < capsuleFractions);
            float4     bestFractions = math.select(capsuleFractions, capsuleFractions.yxwz, capsuleFractions.yxwz < capsuleFractions);
            normal                   = math.select(bestNormals.a, bestNormals.c, bestFractions.z < bestFractions.x);
            fraction                 = math.select(bestFractions.x, bestFractions.z, bestFractions.z < bestFractions.x);
            normal                   = math.select(normal, quadNormal, quadFraction < fraction);
            fraction                 = math.select(fraction, quadFraction, quadFraction < fraction);
            if (fraction > 1f)
                return false;

            normal = NormalFromRoundedRaycastHit(ray.start + ray.displacement * fraction, in quadPoints, quadPoints.bcda, true, planeUnnormal, quadNormal, normal);
            return true;
        }

        // This cleans up normal errors that can happen at seams near the voronoi boundaries of the interior shape, after computing hitpoints using the ray-vs-sphere algorithm
        // on edges and vertices. Otherwise, normals can bend slightly along an edge or towards the center of the triangle or quad.
        static float3 NormalFromRoundedRaycastHit(float3 hitSphereCenter,
                                                  in simdFloat3 edgeStarts,
                                                  in simdFloat3 edgeEnds,
                                                  bool4 validEdges,
                                                  float3 planeUnnormal,
                                                  float3 faceNormal,
                                                  float3 fallbackNormal)
        {
            simdFloat3 edges   = edgeEnds - edgeStarts;
            simdFloat3 vp      = hitSphereCenter - edgeStarts;
            bool       hasFace = math.any(planeUnnormal);

            // If the hit center projects onto the interior face, then we have a face normal.
            float4 edgePlaneDots = simd.dot(simd.cross(edges, planeUnnormal), vp);
            if (hasFace && math.all(edgePlaneDots >= 0f | !validEdges))
                return faceNormal;

            // Otherwise, the center is closest to an interior edge. We find which edge is the closest and use that normal, similar to PointTriangleDistance.
            float4     edgeLengthsSq   = simd.lengthsq(edges);
            float4     edgeDots        = math.clamp(simd.dot(vp, edges), 0f, edgeLengthsSq);
            bool4      clampedToStarts = edgeDots == 0f;
            bool4      clampedToEnds   = edgeDots == edgeLengthsSq;
            simdFloat3 edgeHitpoints   = edgeStarts + edges * (edgeDots / edgeLengthsSq);
            edgeHitpoints              = simd.select(edgeHitpoints, edgeEnds, edgeDots == edgeLengthsSq);
            float4 distancesSq         = simd.distancesq(hitSphereCenter, edgeHitpoints);
            // Only a point within (and not on the boundary of) an edge's voronoi region can hold the closest point.
            // But we can run into scenarios where floating-point math doesn't distinguish distances, especially if the point is far away.
            // So we throw out bad distances and prioritize a voronoi interior.
            bool4  pointOutsideEdges       = (edgePlaneDots < 0f) & validEdges;
            bool4  pointWithinEdgesVoronoi = pointOutsideEdges & !clampedToStarts & !clampedToEnds;
            float4 edgeScores              = math.select(float.MaxValue, distancesSq, pointOutsideEdges);
            edgeScores                     = math.select(edgeScores, -1f, pointWithinEdgesVoronoi);
            float4 validDistancesSq        = math.select(float.MaxValue, distancesSq, validEdges);
            edgeScores                     = math.select(validDistancesSq, edgeScores, hasFace);
            int bestEdgeIndex              = math.tzcnt(math.bitmask(edgeScores == math.cmin(edgeScores)));
            return math.normalizesafe(hitSphereCenter - edgeHitpoints[bestEdgeIndex], fallbackNormal);
        }

        internal static float3 TriangleNormalFromFeatureCode(ushort featureCode, in TriangleCollider triangle, float3 csoOutwardDir)
        {
            var simdTri       = triangle.AsSimdFloat3();
            var displacements = simdTri.bcad - simdTri;
            var planeNormal   = math.normalizesafe(math.cross(displacements.a, displacements.b));
            if (featureCode == 0x8000)
            {
                if (planeNormal.Equals(float3.zero))
                {
                    mathex.GetDualPerpendicularNormalized(planeNormal, out var result, out _);
                    return result;
                }
                return math.select(planeNormal, -planeNormal, math.dot(planeNormal, csoOutwardDir) < 0f);
            }
            if (featureCode < 0x4000)
            {
                var normalizedDisplacements = simd.normalizesafe(displacements);
                var normalize               = simd.normalize(normalizedDisplacements.cabb - normalizedDisplacements);
                return normalize[featureCode];
            }
            var outwardNormals = simd.normalizesafe(simd.cross(displacements, planeNormal));
            return outwardNormals[featureCode & 0xff];
        }

        internal static ushort FeatureCodeFromGjk(byte count, byte a, byte b)
        {
            switch (count)
            {
                case 1:
                {
                    return a;
                }
                case 2:
                {
                    return (a, b) switch
                           {
                               (0, 1) => 0x4000,  // Hit ab-edge
                               (1, 0) => 0x4000,
                               (1, 2) => 0x4001,  // Hit bc-edge
                               (2, 1) => 0x4001,
                               (2, 0) => 0x4002,  // Hit ca-edge
                               (0, 2) => 0x4002,
                               _ => a
                           };
                }
                case 3:
                {
                    return 0x8000;
                }
                default: return a;  // Max is 3.
            }
        }

        internal static void EdgeEndpointsFromEdgeFeatureCode(in TriangleCollider triangle, ushort featureCode, out float3 endpointA, out float3 endpointB)
        {
            switch (featureCode & 0x3)
            {
                case 0:
                    endpointA = triangle.pointA;
                    endpointB = triangle.pointB;
                    break;
                case 1:
                    endpointA = triangle.pointB;
                    endpointB = triangle.pointC;
                    break;
                case 2:
                    endpointA = triangle.pointC;
                    endpointB = triangle.pointA;
                    break;
                default:
                    endpointA = endpointB = triangle.pointA;
                    break;
            }
        }

        internal static void BestFacePlanesAndVertices(in TriangleCollider triangle,
                                                       float3 localDirectionToAlign,
                                                       out simdFloat3 edgePlaneOutwardNormals,
                                                       out float4 edgePlaneDistances,
                                                       out Plane plane,
                                                       out simdFloat3 vertices)
        {
            vertices = triangle.AsSimdFloat3();
            plane    = mathex.PlaneFrom(triangle.pointA, triangle.pointB - triangle.pointA, triangle.pointC - triangle.pointA);
            if (math.dot(plane.normal, localDirectionToAlign) < 0f)
                plane = mathex.Flip(plane);

            edgePlaneOutwardNormals = simd.cross(vertices.bcab - vertices, localDirectionToAlign);  // These normals are perpendicular to the contact normal, not the plane.
            if (math.dot(edgePlaneOutwardNormals.a, vertices.c - vertices.a) > 0f)
                edgePlaneOutwardNormals = -edgePlaneOutwardNormals;
            edgePlaneDistances          = simd.dot(edgePlaneOutwardNormals, vertices.bcab);
        }
    }
}

