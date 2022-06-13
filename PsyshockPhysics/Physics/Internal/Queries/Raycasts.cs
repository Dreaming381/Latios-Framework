using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static partial class SpatialInternal
    {
        public static bool RaycastAabb(Ray ray, Aabb aabb, out float fraction)
        {
            //slab clipping method
            float3 l     = aabb.min - ray.start;
            float3 h     = aabb.max - ray.start;
            float3 nearT = l * ray.reciprocalDisplacement;
            float3 farT  = h * ray.reciprocalDisplacement;

            float3 near = math.min(nearT, farT);
            float3 far  = math.max(nearT, farT);

            float nearMax = math.cmax(math.float4(near, 0f));
            float farMin  = math.cmin(math.float4(far, 1f));

            fraction = nearMax;

            return (nearMax <= farMin) & (l.x <= h.x);
        }

        public static bool RaycastSphere(Ray ray, SphereCollider sphere, out float fraction, out float3 normal)
        {
            float3 delta           = ray.start - sphere.center;
            float  a               = math.dot(ray.displacement, ray.displacement);
            float  b               = 2f * math.dot(ray.displacement, delta);
            float  c               = math.dot(delta, delta) - sphere.radius * sphere.radius;
            float  discriminant    = b * b - 4f * a * c;
            bool   hit             = discriminant >= 0f & c >= 0f;  //Unlike Unity.Physics, we ignore inside hits.
            discriminant           = math.abs(discriminant);
            float sqrtDiscriminant = math.sqrt(discriminant);
            float root1            = (-b - sqrtDiscriminant) / (2f * a);
            float root2            = (-b + sqrtDiscriminant) / (2f * a);
            float rootmin          = math.min(root1, root2);
            float rootmax          = math.max(root1, root2);
            bool  rootminValid     = rootmin >= 0f & rootmin <= 1f;
            bool  rootmaxValid     = rootmax >= 0f & rootmax <= 1f;
            fraction               = math.select(rootmax, rootmin, rootminValid);
            normal                 = (delta + ray.displacement * fraction) / sphere.radius;  //hit point to center divided by radius = normalize normal of sphere at hit
            bool aRootIsValid      = rootminValid | rootmaxValid;
            return hit & aRootIsValid;
        }

        public static bool4 Raycast4Spheres(simdFloat3 rayStart, simdFloat3 rayDisplacement, simdFloat3 center, float4 radius, out float4 fraction, out simdFloat3 normal)
        {
            simdFloat3 delta        = rayStart - center;
            float4     a            = simd.dot(rayDisplacement, rayDisplacement);
            float4     b            = 2f * simd.dot(rayDisplacement, delta);
            float4     c            = simd.dot(delta, delta) - radius * radius;
            float4     discriminant = b * b - 4f * a * c;
            bool4      hit          = discriminant >= 0f & c >= 0f;  //Unlike Unity.Physics, we ignore inside hits.
            discriminant            = math.abs(discriminant);
            float4 sqrtDiscriminant = math.sqrt(discriminant);
            float4 root1            = (-b - sqrtDiscriminant) / (2f * a);
            float4 root2            = (-b + sqrtDiscriminant) / (2f * a);
            float4 rootmin          = math.min(root1, root2);
            float4 rootmax          = math.max(root1, root2);
            bool4  rootminValid     = rootmin >= 0f & rootmin <= 1f;
            bool4  rootmaxValid     = rootmax >= 0f & rootmax <= 1f;
            fraction                = math.select(rootmax, rootmin, rootminValid);
            normal                  = (delta + rayDisplacement * fraction) / radius;
            bool4 aRootIsValid      = rootminValid | rootmaxValid;
            return hit & aRootIsValid;
        }

        public static bool RaycastCapsule(Ray ray, CapsuleCollider capsule, out float fraction, out float3 normal)
        {
            float          axisLength = mathex.getLengthAndNormal(capsule.pointB - capsule.pointA, out float3 axis);
            SphereCollider sphere1    = new SphereCollider(capsule.pointA, capsule.radius);

            // Ray vs infinite cylinder
            {
                float  directionDotAxis  = math.dot(ray.displacement, axis);
                float  originDotAxis     = math.dot(ray.start - capsule.pointA, axis);
                float3 rayDisplacement2D = ray.displacement - axis * directionDotAxis;
                float3 rayOrigin2D       = ray.start - axis * originDotAxis;
                Ray    rayIn2d           = new Ray(rayOrigin2D, rayOrigin2D + rayDisplacement2D);

                if (RaycastSphere(rayIn2d, sphere1, out float cylinderFraction, out normal))
                {
                    float t = originDotAxis + cylinderFraction * directionDotAxis;  // distance of the hit from Vertex0 along axis
                    if (t >= 0.0f && t <= axisLength)
                    {
                        fraction = cylinderFraction;
                        return true;
                    }
                }
            }

            //Ray vs caps
            SphereCollider sphere2 = new SphereCollider(capsule.pointB, capsule.radius);
            bool           hit1    = RaycastSphere(ray, sphere1, out float fraction1, out float3 normal1);
            bool           hit2    = RaycastSphere(ray, sphere2, out float fraction2, out float3 normal2);
            fraction1              = hit1 ? fraction1 : fraction1 + 1f;
            fraction2              = hit2 ? fraction2 : fraction2 + 1f;
            fraction               = math.select(fraction2, fraction1, fraction1 < fraction2);
            normal                 = math.select(normal2, normal1, fraction1 < fraction2);
            return hit1 | hit2;
        }

        public static bool4 Raycast4Capsules(Ray ray, simdFloat3 capA, simdFloat3 capB, float4 capRadius, out float4 fraction, out simdFloat3 normal)
        {
            float4 axisLength = mathex.getLengthAndNormal(capB - capA, out simdFloat3 axis);
            // Ray vs infinite cylinder
            float4     directionDotAxis   = simd.dot(ray.displacement, axis);
            float4     originDotAxis      = simd.dot(ray.start - capA, axis);
            simdFloat3 rayDisplacement2D  = ray.displacement - axis * directionDotAxis;
            simdFloat3 rayOrigin2D        = ray.start - axis * originDotAxis;
            bool4      hitCylinder        = Raycast4Spheres(rayOrigin2D, rayDisplacement2D, capA, capRadius, out float4 cylinderFraction, out simdFloat3 cylinderNormal);
            float4     t                  = originDotAxis + cylinderFraction * directionDotAxis;
            hitCylinder                  &= t >= 0f & t <= axisLength;

            // Ray vs caps
            bool4 hitCapA = Raycast4Spheres(new simdFloat3(ray.start), new simdFloat3(ray.displacement), capA, capRadius, out float4 capAFraction, out simdFloat3 capANormal);
            bool4 hitCapB = Raycast4Spheres(new simdFloat3(ray.start), new simdFloat3(ray.displacement), capB, capRadius, out float4 capBFraction, out simdFloat3 capBNormal);

            // Find best result
            cylinderFraction = math.select(2f, cylinderFraction, hitCylinder);
            capAFraction     = math.select(2f, capAFraction, hitCapA);
            capBFraction     = math.select(2f, capBFraction, hitCapB);

            normal   = simd.select(cylinderNormal, capANormal, capAFraction < cylinderFraction);
            fraction = math.select(cylinderFraction, capAFraction, capAFraction < cylinderFraction);
            normal   = simd.select(normal, capBNormal, capBFraction < fraction);
            fraction = math.select(fraction, capBFraction, capBFraction < fraction);
            return fraction <= 1f;
        }

        //Note: Unity.Physics does not have an equivalent for this. It raycasts against the convex polygon.
        public static bool RaycastBox(Ray ray, BoxCollider box, out float fraction, out float3 normal)
        {
            Aabb aabb = new Aabb(box.center - box.halfSize, box.center + box.halfSize);
            if (RaycastAabb(ray, aabb, out fraction))
            {
                //Idea: Calculate the distance from the hitpoint to each plane of the AABB.
                //The smallest distance is what we consider the plane we actually hit.
                //Also, mask out planes whose normal does not face against the ray.
                //Todo: Is that last step necessary?
                float3 hitpoint            = ray.start + ray.displacement * fraction;
                bool3  signPositive        = ray.displacement > 0f;
                bool3  signNegative        = ray.displacement < 0f;
                float3 alignedFaces        = math.select(aabb.min, aabb.max, signNegative);
                float3 faceDistances       = math.abs(alignedFaces - hitpoint) + math.select(float.MaxValue, 0f, signNegative | signPositive);  //mask out faces the ray is parallel with
                float  closestFaceDistance = math.cmin(faceDistances);
                normal                     = math.select(float3.zero, new float3(1f), closestFaceDistance == faceDistances) * math.select(-1f, 1f, signNegative);  //The normal should be opposite to the ray direction
                return true;
            }
            else
            {
                normal = float3.zero;
                return false;
            }
        }

        public static bool RaycastRoundedBox(Ray ray, BoxCollider box, float radius, out float fraction, out float3 normal)
        {
            // Early out if inside hit
            if (PointBoxDistance(ray.start, box, radius, out _))
            {
                fraction = default;
                normal   = default;
                return false;
            }

            var outerBox       = box;
            outerBox.halfSize += radius;
            bool hitOuter      = RaycastBox(ray, outerBox, out fraction, out normal);
            var  hitPoint      = math.lerp(ray.start, ray.end, fraction);

            if (hitOuter && math.all(math.abs(normal) > 0.9f | (hitPoint >= box.center - box.halfSize & hitPoint <= box.center + box.halfSize)))
            {
                // We hit a flat surface of the box. We have our result already.
                return true;
            }
            else if (!hitOuter && !math.all(ray.start >= outerBox.center - outerBox.halfSize & ray.start <= outerBox.center + outerBox.halfSize))
            {
                // Our ray missed the outer box.
                return false;
            }

            // Our ray either hit near an edge of the outer box or started inside the box. From this point it must hit a capsule surrounding an edge.
            simdFloat3 bTopPoints     = default;
            simdFloat3 bBottomPoints  = default;
            bTopPoints.x              = math.select(-box.halfSize.x, box.halfSize.x, new bool4(true, true, false, false));
            bBottomPoints.x           = bTopPoints.x;
            bBottomPoints.y           = -box.halfSize.y;
            bTopPoints.y              = box.halfSize.y;
            bTopPoints.z              = math.select(-box.halfSize.z, box.halfSize.z, new bool4(true, false, true, false));
            bBottomPoints.z           = bTopPoints.z;
            bTopPoints               += box.center;
            bBottomPoints            += box.center;

            simdFloat3 bLeftPoints = simd.shuffle(bTopPoints,
                                                  bBottomPoints,
                                                  math.ShuffleComponent.LeftZ,
                                                  math.ShuffleComponent.LeftW,
                                                  math.ShuffleComponent.RightZ,
                                                  math.ShuffleComponent.RightW);
            simdFloat3 bRightPoints = simd.shuffle(bTopPoints,
                                                   bBottomPoints,
                                                   math.ShuffleComponent.LeftX,
                                                   math.ShuffleComponent.LeftY,
                                                   math.ShuffleComponent.RightX,
                                                   math.ShuffleComponent.RightY);
            simdFloat3 bFrontPoints = simd.shuffle(bTopPoints,
                                                   bBottomPoints,
                                                   math.ShuffleComponent.LeftY,
                                                   math.ShuffleComponent.LeftW,
                                                   math.ShuffleComponent.RightY,
                                                   math.ShuffleComponent.RightW);
            simdFloat3 bBackPoints = simd.shuffle(bTopPoints,
                                                  bBottomPoints,
                                                  math.ShuffleComponent.LeftX,
                                                  math.ShuffleComponent.LeftZ,
                                                  math.ShuffleComponent.RightX,
                                                  math.ShuffleComponent.RightZ);

            var topBottomHits = Raycast4Capsules(ray, bTopPoints, bBottomPoints, radius, out float4 topBottomFractions, out simdFloat3 topBottomNormals);
            var leftRightHits = Raycast4Capsules(ray, bLeftPoints, bRightPoints, radius, out float4 leftRightFractions, out simdFloat3 leftRightNormals);
            var frontBackHits = Raycast4Capsules(ray, bFrontPoints, bBackPoints, radius, out float4 frontBackFractions, out simdFloat3 frontBackNormals);

            topBottomFractions = math.select(2f, topBottomFractions, topBottomHits);
            leftRightFractions = math.select(2f, leftRightFractions, leftRightHits);
            frontBackFractions = math.select(2f, frontBackFractions, frontBackHits);

            simdFloat3 bestNormals   = simd.select(topBottomNormals, leftRightNormals, leftRightFractions < topBottomFractions);
            float4     bestFractions = math.select(topBottomFractions, leftRightFractions, leftRightFractions < topBottomFractions);
            bestNormals              = simd.select(bestNormals, frontBackNormals, frontBackFractions < bestFractions);
            bestFractions            = math.select(bestFractions, frontBackFractions, frontBackFractions < bestFractions);
            bestNormals              = simd.select(bestNormals, bestNormals.badc, bestFractions.yxwz < bestFractions);
            bestFractions            = math.select(bestFractions, bestFractions.yxwz, bestFractions.yxwz < bestFractions);
            normal                   = math.select(bestNormals.a, bestNormals.c, bestFractions.z < bestFractions.x);
            fraction                 = math.select(bestFractions.x, bestFractions.z, bestFractions.z < bestFractions.x);
            return fraction <= 1f;
        }

        // Mostly from Unity.Physics but handles more edge cases
        // Todo: Reduce branches
        public static bool RaycastTriangle(Ray ray, simdFloat3 triPoints, out float fraction, out float3 outNormal)
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

                // Todo: This is a rare case, so we are going to do something crazy to avoid trying to solve
                // line intersections in 3D space.
                // Instead, inflate the plane along the normal and raycast against the planes
                // In the case that the ray passes through one of the plane edges, this recursion will reach
                // three levels deep, and then a full plane will be constructed against the ray.
                var    negPoints = triPoints - normal;
                var    posPoints = triPoints + normal;
                var    quadA     = new simdFloat3(negPoints.a, posPoints.a, posPoints.b, negPoints.b);
                var    quadB     = new simdFloat3(negPoints.b, posPoints.b, posPoints.c, negPoints.c);
                var    quadC     = new simdFloat3(negPoints.c, posPoints.c, posPoints.a, negPoints.a);
                bool3  hits      = default;
                float3 fractions = default;
                hits.x           = RaycastQuad(ray, quadA, out fractions.x);
                hits.y           = RaycastQuad(ray, quadB, out fractions.y);
                hits.z           = RaycastQuad(ray, quadC, out fractions.z);
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

        public static bool RaycastRoundedTriangle(Ray ray, simdFloat3 triPoints, float radius, out float fraction, out float3 normal)
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
                triFaceHit           = RaycastTriangle(ray, triPoints + math.normalize(triNormal) * radius, out triFraction, out _);
            triFraction              = math.select(2f, triFraction, triFaceHit);
            bool4 capsuleHits        = Raycast4Capsules(ray, triPoints, triPoints.bcaa, radius, out float4 capsuleFractions, out simdFloat3 capsuleNormals);
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
        public static bool RaycastQuad(Ray ray, simdFloat3 quadPoints, out float fraction)
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
                var    negPoints = quadPoints - normal;
                var    posPoints = quadPoints + normal;
                var    quadA     = new simdFloat3(negPoints.a, posPoints.a, posPoints.b, negPoints.b);
                var    quadB     = new simdFloat3(negPoints.b, posPoints.b, posPoints.c, negPoints.c);
                var    quadC     = new simdFloat3(negPoints.c, posPoints.c, posPoints.d, negPoints.d);
                var    quadD     = new simdFloat3(negPoints.d, posPoints.d, posPoints.a, negPoints.a);
                bool4  hits      = default;
                float4 fractions = default;
                hits.x           = RaycastQuad(ray, quadA, out fractions.x);
                hits.y           = RaycastQuad(ray, quadB, out fractions.y);
                hits.z           = RaycastQuad(ray, quadC, out fractions.z);
                hits.w           = RaycastQuad(ray, quadD, out fractions.w);
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

        public static bool RaycastRoundedQuad(Ray ray, simdFloat3 quadPoints, float radius, out float fraction, out float3 normal)
        {
            // Make sure the ray doesn't start inside.
            if (PointQuadDistance(ray.start, quadPoints, radius, out _))
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
                quadFaceHit          = RaycastQuad(ray, quadPoints + math.normalize(quadNormal) * radius, out quadFraction);
            quadFraction             = math.select(2f, quadFraction, quadFaceHit);
            bool4 capsuleHits        = Raycast4Capsules(ray, quadPoints, quadPoints.bcda, radius, out float4 capsuleFractions, out simdFloat3 capsuleNormals);
            capsuleFractions         = math.select(2f, capsuleFractions, capsuleHits);
            simdFloat3 bestNormals   = simd.select(capsuleNormals, capsuleNormals.badc, capsuleFractions.yxwz < capsuleFractions);
            float4     bestFractions = math.select(capsuleFractions, capsuleFractions.yxwz, capsuleFractions.yxwz < capsuleFractions);
            normal                   = math.select(bestNormals.a, bestNormals.c, bestFractions.z < bestFractions.x);
            fraction                 = math.select(bestFractions.x, bestFractions.z, bestFractions.z < bestFractions.x);
            normal                   = math.select(normal, quadNormal, quadFraction < fraction);
            fraction                 = math.select(fraction, quadFraction, quadFraction < fraction);
            return fraction <= 1f;
        }

        public static bool RaycastConvex(Ray ray, ConvexCollider convex, out float fraction, out float3 normal)
        {
            ref var blob       = ref convex.convexColliderBlob.Value;
            var     scaledAabb = new Aabb(blob.localAabb.min * convex.scale, blob.localAabb.max * convex.scale);

            if (!RaycastAabb(ray, scaledAabb, out float aabbFraction))
            {
                if (!math.all(ray.start >= scaledAabb.min & ray.end <= scaledAabb.max))
                {
                    fraction = 2f;
                    normal   = default;
                    return false;
                }
            }

            float3 invScale   = math.rcp(convex.scale);
            var    dimensions = math.countbits(math.bitmask(new bool4(math.isfinite(invScale), false)));

            if (dimensions == 3)
            {
                fraction           = -2f;
                float exitFraction = 2f;
                int   bestIndex    = 0;
                bool  inside       = true;
                var   scaledRay    = new Ray(ray.start * invScale, ray.end * invScale);

                for (int i = 0; i < blob.facePlaneX.Length; i++)
                {
                    // These are signed distances to the plane from start/end points respectively
                    float startDot = scaledRay.start.x * blob.facePlaneX[i] + scaledRay.start.y * blob.facePlaneY[i] + scaledRay.start.z * blob.facePlaneZ[i] +
                                     blob.facePlaneDist[i];
                    float endDot = scaledRay.end.x * blob.facePlaneX[i] + scaledRay.end.y * blob.facePlaneY[i] + scaledRay.end.z * blob.facePlaneZ[i] + blob.facePlaneDist[i];

                    // If the ray is completely outside the plane or starts on the plane and moves away, then it misses.
                    if (startDot >= 0f && endDot >= 0f)
                    {
                        // If the ray is coplaner, just skip
                        if (startDot == 0f && endDot == 0f)
                            continue;

                        normal   = default;
                        fraction = 2f;
                        return false;
                    }

                    // This is the distance of the ray start to the plane divided by the length of the ray projected onto the plane's normal.
                    float newFraction = startDot / (startDot - endDot);

                    if (newFraction > fraction && startDot > 0f)
                    {
                        fraction  = newFraction;
                        bestIndex = i;
                    }
                    else if (newFraction < exitFraction && endDot > 0f)
                    {
                        exitFraction = newFraction;
                    }
                    inside &= startDot < 0f;
                }
                if (inside || exitFraction < fraction)
                {
                    normal   = default;
                    fraction = 2f;
                    return false;
                }

                normal = new float3(blob.facePlaneX[bestIndex], blob.facePlaneY[bestIndex], blob.facePlaneZ[bestIndex]);
                return true;
            }
            else if (dimensions == 0)
            {
                SphereCollider sphere = new SphereCollider(0f, 0f);
                return RaycastSphere(ray, sphere, out fraction, out normal);
            }
            else if (dimensions == 1)
            {
                CapsuleCollider capsule = new CapsuleCollider(scaledAabb.min, scaledAabb.max, 0f);
                return RaycastCapsule(ray, capsule, out fraction, out normal);
            }
            else if (dimensions == 2)
            {
                // From the AABB check we know the ray crosses the plane. So now we just need to figure out if the ray hits
                // the geometry.
                var hitPoint = ray.start + ray.displacement * aabbFraction;

                var mask      = math.select(1f, 0f, math.isfinite(invScale));
                var diff      = blob.localAabb.max - blob.localAabb.min;
                diff         *= mask;
                var rayStart  = hitPoint - diff + blob.localAabb.min * mask;

                var inflateRay      = new Ray(rayStart, rayStart + diff * 3f);
                var inflateConvex   = convex;
                inflateConvex.scale = math.select(1f, convex.scale, math.isfinite(invScale));
                if (RaycastConvex(inflateRay, inflateConvex, out _, out _))
                {
                    fraction = aabbFraction;
                    normal   = math.normalize(mask * ray.displacement);
                    return true;
                }
            }
            fraction = 2f;
            normal   = default;
            return false;
        }

        // Scale is applied before radius
        public static bool RaycastRoundedConvex(Ray ray, ConvexCollider convex, float radius, out float fraction)
        {
            ref var blob       = ref convex.convexColliderBlob.Value;
            var     scale      = convex.scale;
            var     scaledAabb = new Aabb(blob.localAabb.min * scale - radius, blob.localAabb.max * scale + radius);

            if (!RaycastAabb(ray, scaledAabb, out _))
            {
                if (!math.all(ray.start >= scaledAabb.min & ray.end <= scaledAabb.max))
                {
                    fraction = 2f;
                    return false;
                }
            }

            float3 invScale   = math.rcp(scale);
            var    dimensions = math.countbits(math.bitmask(new bool4(math.isfinite(invScale), false)));

            if (dimensions == 3)
            {
                fraction           = -2f;
                float exitFraction = 2f;
                int   bestIndex    = 0;
                bool  inside       = true;
                var   scaledRay    = new Ray(ray.start * invScale, ray.end * invScale);

                for (int i = 0; i < blob.facePlaneX.Length; i++)
                {
                    float startDot = scaledRay.start.x * blob.facePlaneX[i] + scaledRay.start.y * blob.facePlaneY[i] + scaledRay.start.z * blob.facePlaneZ[i] +
                                     blob.facePlaneDist[i] - radius;
                    float endDot = scaledRay.end.x * blob.facePlaneX[i] + scaledRay.end.y * blob.facePlaneY[i] + scaledRay.end.z * blob.facePlaneZ[i] + blob.facePlaneDist[i] -
                                   radius;
                    // If the ray is completely outside the plane or starts on the plane and moves away, then it misses.
                    if (startDot >= 0f && endDot >= 0f)
                    {
                        // If the ray is coplaner, just skip
                        if (startDot == 0f && endDot == 0f)
                            continue;

                        fraction = 2f;
                        return false;
                    }

                    // This is the distance of the ray start to the plane divided by the length of the ray projected onto the plane's normal.
                    float newFraction = startDot / (startDot - endDot);

                    if (newFraction > fraction && startDot > 0f)
                    {
                        fraction  = newFraction;
                        bestIndex = i;
                    }
                    else if (newFraction < exitFraction && endDot > 0f)
                    {
                        exitFraction = newFraction;
                    }
                    inside &= startDot < 0f;
                }

                if (inside || exitFraction < fraction)
                {
                    fraction = 2f;
                    return false;
                }

                // We know the inflated hit face, but we don't know if it hit the rounded part or not yet.
                float3 scaledPoint = scaledRay.start + scaledRay.displacement * fraction;
                var    edgeRange   = blob.edgeIndicesInFacesStartsAndCounts[bestIndex];
                bool   hitEdge     = false;
                bool   nearsEdge   = false;
                for (int i = 0; i < edgeRange.y; i++)
                {
                    float dot = math.dot(scaledPoint.xyz1(), blob.faceEdgeOutwardPlanes[i + edgeRange.x]);
                    if (dot > 0f)
                    {
                        nearsEdge   = true;
                        var indices = blob.vertexIndicesInEdges[blob.edgeIndicesInFaces[i + edgeRange.x]];
                        var cap     = new CapsuleCollider(new float3(blob.verticesX[indices.x], blob.verticesY[indices.x], blob.verticesZ[indices.x]),
                                                          new float3(blob.verticesX[indices.y], blob.verticesY[indices.y], blob.verticesZ[indices.y]), radius);
                        if (RaycastCapsule(scaledRay, cap, out float newFraction, out _))
                        {
                            if (!hitEdge)
                            {
                                fraction = newFraction;
                                hitEdge  = true;
                            }
                            fraction = math.min(fraction, newFraction);
                        }
                    }
                }

                return nearsEdge == hitEdge;
            }
            else if (dimensions == 0)
            {
                SphereCollider sphere = new SphereCollider(0f, radius);
                return RaycastSphere(ray, sphere, out fraction, out _);
            }
            else if (dimensions == 1)
            {
                CapsuleCollider capsule = new CapsuleCollider(scaledAabb.min + radius, scaledAabb.max - radius, 0f);
                return RaycastCapsule(ray, capsule, out fraction, out _);
            }
            else if (dimensions == 2)
            {
                // We need to identify if the ray hits one of the planar surfaces.
                var   mask     = math.select(1f, 0f, math.isfinite(invScale));
                float maxStart = math.dot(ray.start.xyz1(), new float4(mask, -radius));
                float maxEnd   = math.dot(ray.end.xyz1(), new float4(mask, -radius));
                float minStart = math.dot(ray.start.xyz1(), new float4(-mask, radius));
                float minEnd   = math.dot(ray.end.xyz1(), new float4(-mask, radius));
                if (maxStart > 0f && maxEnd <= 0f)
                {
                    // We might have a planar hit on the max side of the AABB, so find the plane hit and raycast the original object.
                    float  planarFraction = maxStart / (maxStart - maxEnd);
                    float3 hitPoint       = ray.start + ray.displacement * planarFraction;

                    var diff      = blob.localAabb.max - blob.localAabb.min + 2f * radius;
                    diff         *= mask;
                    var rayStart  = hitPoint - diff + blob.localAabb.min * mask;

                    var inflateRay      = new Ray(rayStart, rayStart + diff * 3f);
                    var inflateConvex   = convex;
                    inflateConvex.scale = math.select(1f, convex.scale, math.isfinite(invScale));
                    if (RaycastConvex(inflateRay, inflateConvex, out _, out _))
                    {
                        fraction = planarFraction;
                        return true;
                    }
                }
                else if (minStart > 0f && minEnd <= 0f)
                {
                    // We might have a planar hit on the max side of the AABB, so find the plane hit and raycast the original object.
                    float  planarFraction = minStart / (minStart - minEnd);
                    float3 hitPoint       = ray.start + ray.displacement * planarFraction;

                    var diff      = blob.localAabb.max - blob.localAabb.min + 2f * radius;
                    diff         *= mask;
                    var rayStart  = hitPoint - diff + blob.localAabb.min * mask;

                    var inflateRay      = new Ray(rayStart, rayStart + diff * 3f);
                    var inflateConvex   = convex;
                    inflateConvex.scale = math.select(1f, convex.scale, math.isfinite(invScale));
                    if (RaycastConvex(inflateRay, inflateConvex, out _, out _))
                    {
                        fraction = planarFraction;
                        return true;
                    }
                }

                fraction = 2f;
                bool hit = false;
                for (int i = 0; i < blob.vertexIndicesInEdges.Length; i++)
                {
                    var indices = blob.vertexIndicesInEdges[i];
                    var cap     = new CapsuleCollider(new float3(blob.verticesX[indices.x], blob.verticesY[indices.x], blob.verticesZ[indices.x]) * scale,
                                                      new float3(blob.verticesX[indices.y], blob.verticesY[indices.y], blob.verticesZ[indices.y]) * scale, radius);
                    if (RaycastCapsule(ray, cap, out float newFraction, out _))
                    {
                        hit      = true;
                        fraction = math.min(fraction, newFraction);
                    }
                }
                return hit;
            }
            fraction = 2f;
            return false;
        }
    }
}

