using Unity.Burst;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class PointRayTriangle
    {
        public static bool DistanceBetween(float3 point, in TriangleCollider triangle, in RigidTransform triangleTransform, float maxDistance, out PointDistanceResult result)
        {
            var  pointInTriangleSpace = math.transform(math.inverse(triangleTransform), point);
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
            var  rayInTriangleSpace = Ray.TransformRay(math.inverse(triangleTransform), ray);
            bool hit                = RaycastTriangle(in rayInTriangleSpace,
                                                      new simdFloat3(triangle.pointA, triangle.pointB, triangle.pointC, triangle.pointC),
                                                      out float fraction,
                                                      out float3 normal);
            result.position         = math.lerp(ray.start, ray.end, fraction);
            result.normal           = math.rotate(triangleTransform, normal);
            result.distance         = math.distance(ray.start, result.position);
            result.subColliderIndex = 0;
            return hit;
        }

        // Distance is unsigned, triangle is "double-sided"
        internal static bool PointTriangleDistance(float3 point, in TriangleCollider triangle, float maxDistance, out PointDistanceResultInternal result)
        {
            float3 ab = triangle.pointB - triangle.pointA;
            float3 bc = triangle.pointC - triangle.pointB;
            float3 ca = triangle.pointA - triangle.pointC;
            float3 ap = point - triangle.pointA;
            float3 bp = point - triangle.pointB;
            float3 cp = point - triangle.pointC;

            // project point onto plane
            // if counter-clockwise, normal faces "up"
            float3 planeNormal    = math.normalizesafe(math.cross(ab, ca));
            float  projectionDot  = math.dot(planeNormal, point - triangle.pointA);
            float3 projectedPoint = point - projectionDot * planeNormal;

            // calculate edge planes aligned with triangle normal without the normalization (since it isn't required)
            // normals face "inward"
            float3 abUnnormal = math.cross(ab, planeNormal);
            float3 bcUnnormal = math.cross(bc, planeNormal);
            float3 caUnnormal = math.cross(ca, planeNormal);

            float3 dots = new float3(math.dot(abUnnormal, ap),
                                     math.dot(bcUnnormal, bp),
                                     math.dot(caUnnormal, cp));
            int region = math.bitmask(new bool4(dots <= 0f, false));
            switch (region)
            {
                case 0:
                {
                    // all inside, hit plane
                    result.hitpoint    = projectedPoint;
                    result.distance    = math.abs(projectionDot);
                    result.normal      = math.select(planeNormal, -planeNormal, math.dot(result.hitpoint - point, planeNormal) < 0);
                    result.featureCode = 0x8000;
                    break;
                }
                case 1:
                {
                    // outside ab plane
                    float abLengthSq   = math.lengthsq(ab);
                    float dot          = math.clamp(math.dot(ap, ab), 0f, abLengthSq);
                    result.hitpoint    = triangle.pointA + ab * dot / abLengthSq;
                    result.distance    = math.distance(point, result.hitpoint);
                    result.normal      = -math.normalize(abUnnormal);
                    result.featureCode = 0x4000;
                    break;
                }
                case 2:
                {
                    // outside bc plane
                    float bcLengthSq   = math.lengthsq(bc);
                    float dot          = math.clamp(math.dot(bp, bc), 0f, bcLengthSq);
                    result.hitpoint    = triangle.pointB + bc * dot / bcLengthSq;
                    result.distance    = math.distance(point, result.hitpoint);
                    result.normal      = -math.normalize(bcUnnormal);
                    result.featureCode = 0x4001;
                    break;
                }
                case 3:
                {
                    // outside ab and bc so closest to point b
                    result.hitpoint    = triangle.pointB;
                    result.distance    = math.distance(point, triangle.pointB);
                    result.normal      = math.normalize(-math.normalize(abUnnormal) - math.normalize(bcUnnormal));
                    result.featureCode = 1;
                    break;
                }
                case 4:
                {
                    // outside ca plane
                    float caLengthSq   = math.lengthsq(ca);
                    float dot          = math.clamp(math.dot(cp, ca), 0f, caLengthSq);
                    result.hitpoint    = triangle.pointC + ca * dot / caLengthSq;
                    result.distance    = math.distance(point, result.hitpoint);
                    result.normal      = -math.normalize(caUnnormal);
                    result.featureCode = 0x4002;
                    break;
                }
                case 5:
                {
                    // outside ab and ca so closest to point a
                    result.hitpoint    = triangle.pointA;
                    result.distance    = math.distance(point, triangle.pointA);
                    result.normal      = math.normalize(-math.normalize(abUnnormal) - math.normalize(caUnnormal));
                    result.featureCode = 0;
                    break;
                }
                case 6:
                {
                    // outside bc and ca so closest to point c
                    result.hitpoint    = triangle.pointC;
                    result.distance    = math.distance(point, triangle.pointC);
                    result.normal      = math.normalize(-math.normalize(caUnnormal) - math.normalize(bcUnnormal));
                    result.featureCode = 2;
                    break;
                }
                case 7:
                {
                    // on all three edges at once because the cross product was 0
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
                    break;
                }
                default:
                {
                    //How the heck did we get here?
                    //throw new InvalidOperationException();
                    result.hitpoint    = projectedPoint;
                    result.distance    = 2f * maxDistance;
                    result.normal      = new float3(0f, 1f, 0f);
                    result.featureCode = 0;
                    break;
                }
            }
            return result.distance <= maxDistance;
        }

        // Distance is unsigned, quad is "double-sided"
        private static bool PointQuadDistance(float3 point, in simdFloat3 quadPoints, float maxDistance, out PointDistanceResultInternal result)
        {
            simdFloat3 abcd     = new simdFloat3(quadPoints.a, quadPoints.b, quadPoints.c, quadPoints.d);
            simdFloat3 abbccdda = abcd.bcda - abcd.abcd;
            simdFloat3 abcdp    = point - abcd;

            // project point onto plane
            // if clockwise, normal faces "up"
            float3 planeNormal    = math.normalize(math.cross(abbccdda.a, abbccdda.d));  //ab, da
            float  projectionDot  = math.dot(planeNormal, abcdp.a);
            float3 projectedPoint = point - projectionDot * planeNormal;

            // calculate edge planes aligned with quad normal without the normalization (since it isn't required)
            // normals face "inward"
            simdFloat3 abbccddaUnnormal = simd.cross(abbccdda, planeNormal);
            float4     dotsEdges        = simd.dot(abbccddaUnnormal, abcdp);

            if (math.bitmask(dotsEdges < 0f) == 0)  //if (math.all(dotsEdges >= 0))
            {
                result.hitpoint = projectedPoint;
                result.distance = math.abs(projectionDot);
            }
            else
            {
                float3 acUnnormal = math.cross(quadPoints.c - quadPoints.a, planeNormal);  // faces D
                float3 bdUnnormal = math.cross(quadPoints.d - quadPoints.b, planeNormal);  // faces A
                float2 dotsDiags  = new float2(math.dot(acUnnormal, abcdp.a), math.dot(bdUnnormal, abcdp.b));
                int    region     = math.csum(math.select(new int2(1, 2), int2.zero, dotsDiags >= 0));  //Todo: bitmask?
                switch (region)
                {
                    case 0:
                    {
                        // closest to da
                        var   da         = abbccdda.d;
                        var   dp         = abcdp.d;
                        float daLengthSq = math.lengthsq(da);
                        float dot        = math.clamp(math.dot(dp, da), 0f, daLengthSq);
                        result.hitpoint  = quadPoints.d + da * dot / daLengthSq;
                        result.distance  = math.distance(point, result.hitpoint);
                        break;
                    }
                    case 1:
                    {
                        // closest to ab
                        var   ab         = abbccdda.a;
                        var   ap         = abcdp.a;
                        float abLengthSq = math.lengthsq(ab);
                        float dot        = math.clamp(math.dot(ap, ab), 0f, abLengthSq);
                        result.hitpoint  = quadPoints.a + ab * dot / abLengthSq;
                        result.distance  = math.distance(point, result.hitpoint);
                        break;
                    }
                    case 2:
                    {
                        // closest to bc
                        var   bc         = abbccdda.b;
                        var   bp         = abcdp.b;
                        float bcLengthSq = math.lengthsq(bc);
                        float dot        = math.clamp(math.dot(bp, bc), 0f, bcLengthSq);
                        result.hitpoint  = quadPoints.b + bc * dot / bcLengthSq;
                        result.distance  = math.distance(point, result.hitpoint);
                        break;
                    }
                    case 3:
                    {
                        // closest to cd
                        var   cd         = abbccdda.c;
                        var   cp         = abcdp.c;
                        float cdLengthSq = math.lengthsq(cd);
                        float dot        = math.clamp(math.dot(cp, cd), 0f, cdLengthSq);
                        result.hitpoint  = quadPoints.c + cd * dot / cdLengthSq;
                        result.distance  = math.distance(point, result.hitpoint);
                        break;
                    }
                    default:
                    {
                        // How the heck did we get here?
                        // throw new InvalidOperationException();
                        result.hitpoint = projectedPoint;
                        result.distance = maxDistance * 2f;
                        break;
                    }
                }
            }

            result.normal      = math.select(planeNormal, -planeNormal, math.dot(result.hitpoint - point, planeNormal) < 0);
            result.featureCode = 0;  // Unusable for quads
            return result.distance <= maxDistance;
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
                outNormal              = math.select(normal, -normal, nDotAStart >= 0f);
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
                simdFloat3 r           = doubleEnd - (triPoints + triPoints.bcda);
                float3     dots        = simd.dot(r, edgeNormals).xyz;
                outNormal              = math.select(normal, -normal, nDotAStart >= 0f);
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
            if (PointTriangleDistance(ray.start, new TriangleCollider(triPoints.a, triPoints.b, triPoints.c), radius, out _))
            {
                fraction = 2f;
                normal   = default;
                return false;
            }

            float3 ab        = triPoints.b - triPoints.a;
            float3 ca        = triPoints.a - triPoints.c;
            float3 triNormal = math.cross(ab, ca);
            triNormal        = math.select(triNormal, -triNormal, math.dot(triNormal, ray.displacement) > 0f);

            // Catch degenerate tri here
            bool  triFaceHit  = math.any(triNormal);
            float triFraction = 2f;
            if (triFaceHit)
                triFaceHit           = RaycastTriangle(in ray, triPoints + math.normalize(triNormal) * radius, out triFraction, out _);
            triFraction              = math.select(2f, triFraction, triFaceHit);
            bool4 capsuleHits        = PointRayCapsule.Raycast4Capsules(in ray, in triPoints, triPoints.bcaa, radius, out float4 capsuleFractions, out simdFloat3 capsuleNormals);
            capsuleFractions         = math.select(2f, capsuleFractions, capsuleHits);
            simdFloat3 bestNormals   = simd.select(capsuleNormals, capsuleNormals.bacc, capsuleFractions.yxzz < capsuleFractions);
            float4     bestFractions = math.select(capsuleFractions, capsuleFractions.yxzz, capsuleFractions.yxzz < capsuleFractions);
            normal                   = math.select(bestNormals.a, bestNormals.c, bestFractions.z < bestFractions.x);
            fraction                 = math.select(bestFractions.x, bestFractions.z, bestFractions.z < bestFractions.x);
            normal                   = math.select(normal, triNormal, triFraction < fraction);
            fraction                 = math.select(fraction, triFraction, triFraction < fraction);
            return fraction <= 1f;
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
            if (PointQuadDistance(ray.start, in quadPoints, radius, out _))
            {
                fraction = 2f;
                normal   = default;
                return false;
            }

            float3 ab         = quadPoints.b - quadPoints.a;
            float3 ca         = quadPoints.a - quadPoints.c;
            float3 quadNormal = math.cross(ab, ca);
            quadNormal        = math.select(quadNormal, -quadNormal, math.dot(quadNormal, ray.displacement) > 0f);

            // Catch degenerate quad here
            bool  quadFaceHit  = math.any(quadNormal);
            float quadFraction = 2f;
            if (quadFaceHit)
                quadFaceHit          = RaycastQuad(in ray, quadPoints + math.normalize(quadNormal) * radius, out quadFraction);
            quadFraction             = math.select(2f, quadFraction, quadFaceHit);
            bool4 capsuleHits        = PointRayCapsule.Raycast4Capsules(in ray, in quadPoints, quadPoints.bcda, radius, out float4 capsuleFractions, out simdFloat3 capsuleNormals);
            capsuleFractions         = math.select(2f, capsuleFractions, capsuleHits);
            simdFloat3 bestNormals   = simd.select(capsuleNormals, capsuleNormals.badc, capsuleFractions.yxwz < capsuleFractions);
            float4     bestFractions = math.select(capsuleFractions, capsuleFractions.yxwz, capsuleFractions.yxwz < capsuleFractions);
            normal                   = math.select(bestNormals.a, bestNormals.c, bestFractions.z < bestFractions.x);
            fraction                 = math.select(bestFractions.x, bestFractions.z, bestFractions.z < bestFractions.x);
            normal                   = math.select(normal, quadNormal, quadFraction < fraction);
            fraction                 = math.select(fraction, quadFraction, quadFraction < fraction);
            return fraction <= 1f;
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

