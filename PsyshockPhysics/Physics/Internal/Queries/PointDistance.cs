using System;
using Unity.Mathematics;

//A lot of this code has been commented out as I wrote it before I got distance query operations working in a real project.
//Consequently, I want to vet them before I uncomment them.

namespace Latios.Psyshock
{
    internal static partial class SpatialInternal
    {
        public struct PointDistanceResultInternal
        {
            public float3 hitpoint;
            public float  distance;
            public float3 normal;
        }

        //All algorithms return a negative distance if inside the collider.
        public static bool PointSphereDistance(float3 point, SphereCollider sphere, float maxDistance, out PointDistanceResultInternal result)
        {
            float3 delta          = sphere.center - point;
            float  pcDistanceSq   = math.lengthsq(delta);  //point center distance
            bool   distanceIsZero = pcDistanceSq == 0.0f;
            float  invPCDistance  = math.select(math.rsqrt(pcDistanceSq), 0.0f, distanceIsZero);
            float3 inNormal       = math.select(delta * invPCDistance, new float3(0, 1, 0), distanceIsZero);  // choose an arbitrary normal when the distance is zero
            float  distance       = pcDistanceSq * invPCDistance - sphere.radius;
            result                = new PointDistanceResultInternal
            {
                hitpoint = point + inNormal * distance,
                distance = distance,
                normal   = -inNormal,
            };
            return distance <= maxDistance;
        }

        public static bool PointCapsuleDistance(float3 point, CapsuleCollider capsule, float maxDistance, out PointDistanceResultInternal result)
        {
            //Strategy: Project p onto the capsule's line clamped to the segment. Then inflate point on line as sphere
            float3 edge                   = capsule.pointB - capsule.pointA;
            float3 ap                     = point - capsule.pointA;
            float  dot                    = math.dot(ap, edge);
            float  edgeLengthSq           = math.lengthsq(edge);
            dot                           = math.clamp(dot, 0f, edgeLengthSq);
            float3         pointOnSegment = capsule.pointA + edge * dot / edgeLengthSq;
            SphereCollider sphere         = new SphereCollider(pointOnSegment, capsule.radius);
            return PointSphereDistance(point, sphere, maxDistance, out result);
        }

        /*public static bool PointCylinderDistance(float3 point, CylinderCollider cylinder, float maxDistance, out PointDistanceResultInternal result)
           {
            //Strategy: Project p onto the capsule's line.
            //If on the segment, do point vs sphere.
            //Otherwise do point vs disk
            float3 edge = cylinder.pointB - cylinder.pointA;
            float3 ap = point - cylinder.pointA;
            float3 unitEdge = math.normalize(edge);
            float dot = math.dot(ap, unitEdge);  //dot is distance of projected point from pointA
            float3 pointOnLine = cylinder.pointA + unitEdge * dot;
            if (dot < 0f)
            {
                //Todo: Optimize math
                float3 pointOnLineToPoint = point - pointOnLine;  //This gives us our direction from the center of the cap to the edge towards the query point.
                if (math.lengthsq(pointOnLineToPoint) > cylinder.radius * cylinder.radius)
                {
                    float3 roundNormal = math.normalize(pointOnLineToPoint);
                    float3 capNormal = -unitEdge;
                    result.normal = (roundNormal + capNormal) / math.SQRT2;  //Summing orthogonal unit vectors has a length of sqrt2
                    result.hitpoint = cylinder.pointA + roundNormal * cylinder.radius;
                    result.distance = math.distance(point, result.hitpoint);
                }
                else
                {
                    result.normal = -unitEdge;
                    result.distance = -dot;
                    result.hitpoint = point + unitEdge * result.distance;
                }
                return result.distance <= maxDistance;
            }
            if (dot * dot > math.lengthsq(edge))
            {
                //Todo: Optimize math
                float3 pointOnLineToPoint = point - pointOnLine;  //This gives us our direction from the center of the cap to the edge towards the query point.
                if (math.lengthsq(pointOnLineToPoint) > cylinder.radius * cylinder.radius)
                {
                    float3 roundNormal = math.normalize(pointOnLineToPoint);
                    float3 capNormal = unitEdge;
                    result.normal = (roundNormal + capNormal) / math.SQRT2;  //Summing orthogonal unit vectors has a length of sqrt2
                    result.hitpoint = cylinder.pointB + roundNormal * cylinder.radius;
                    result.distance = math.distance(point, result.hitpoint);
                }
                else
                {
                    result.normal = unitEdge;
                    result.distance = math.distance(pointOnLine, cylinder.pointB);
                    result.hitpoint = point - unitEdge * result.distance;
                }
                return result.distance <= maxDistance;
            }
            else
            {
                SphereCollider sphere = new SphereCollider(pointOnLine, cylinder.radius);
                return DistanceBetween(point, sphere, maxDistance, out result);
            }
           }*/

        public static bool PointBoxDistance(float3 point, BoxCollider box, float maxDistance, out PointDistanceResultInternal result)
        {
            //Idea: The positive octant of the box contains 7 feature regions: 3 faces, 3 edges, and inside.
            //The other octants are identical except with flipped signs. So if we unflip signs,
            //calculate the distance for these 7 regions, and then flip signs again, we get a valid result.
            //We use feature regions rather than feature types to avoid swizzling since that would require a second branch.
            float3 osPoint    = point - box.center;  //os = origin space
            bool3  isNegative = osPoint < 0f;
            float3 ospPoint   = math.select(osPoint, -osPoint, isNegative);  //osp = origin space positive
            int    region     = math.csum(math.select(new int3(4, 2, 1), int3.zero, ospPoint < box.halfSize));
            switch (region)
            {
                case 0:
                {
                    //Inside the box. Get the closest wall.
                    float3 delta   = box.halfSize - ospPoint;
                    float  min     = math.cmin(delta);
                    bool3  minMask = min == delta;
                    //Prioritize y first, then z, then x if multiple distances perfectly match.
                    //Todo: Should this be configurabe?
                    minMask.xz      &= !minMask.y;
                    minMask.x       &= !minMask.z;
                    result.hitpoint  = math.select(ospPoint, box.halfSize, minMask);
                    result.distance  = -min;
                    result.normal    = math.select(0f, 1f, minMask);
                    break;
                }
                case 1:
                {
                    //xy in box, z outside
                    //Closest feature is the z-face
                    result.distance = ospPoint.z - box.halfSize.z;
                    result.hitpoint = new float3(ospPoint.xy, box.halfSize.z);
                    result.normal   = new float3(0f, 0f, 1f);
                    break;
                }
                case 2:
                {
                    //xz in box, y outside
                    //Closest feature is the y-face
                    result.distance = ospPoint.y - box.halfSize.y;
                    result.hitpoint = new float3(ospPoint.x, box.halfSize.y, ospPoint.z);
                    result.normal   = new float3(0f, 1f, 0f);
                    break;
                }
                case 3:
                {
                    //x in box, yz outside
                    //Closest feature is the x-axis edge
                    result.distance = math.distance(ospPoint.yz, box.halfSize.yz);
                    result.hitpoint = new float3(ospPoint.x, box.halfSize.yz);
                    result.normal   = new float3(0f, math.SQRT2 / 2f, math.SQRT2 / 2f);
                    break;
                }
                case 4:
                {
                    //yz in box, x outside
                    //Closest feature is the x-face
                    result.distance = ospPoint.x - box.halfSize.x;
                    result.hitpoint = new float3(box.halfSize.x, ospPoint.yz);
                    result.normal   = new float3(1f, 0f, 0f);
                    break;
                }
                case 5:
                {
                    //y in box, xz outside
                    //Closest feature is the y-axis edge
                    result.distance = math.distance(ospPoint.xz, box.halfSize.xz);
                    result.hitpoint = new float3(box.halfSize.x, ospPoint.y, box.halfSize.y);
                    result.normal   = new float3(math.SQRT2 / 2f, 0f, math.SQRT2 / 2f);
                    break;
                }
                case 6:
                {
                    //z in box, xy outside
                    //Closest feature is the z-axis edge
                    result.distance = math.distance(ospPoint.xy, box.halfSize.xy);
                    result.hitpoint = new float3(box.halfSize.xy, ospPoint.z);
                    result.normal   = new float3(math.SQRT2 / 2f, math.SQRT2 / 2f, 0f);
                    break;
                }
                default:
                {
                    //xyz outside box
                    ////Closest feature is the osp corner
                    result.distance = math.distance(ospPoint, box.halfSize);
                    result.hitpoint = box.halfSize;
                    result.normal   = math.normalize(math.float3(1f));
                    break;
                }
            }
            result.hitpoint = math.select(result.hitpoint, -result.hitpoint, isNegative) + box.center;
            result.normal   = math.select(result.normal, -result.normal, isNegative);
            return result.distance <= maxDistance;
        }

        //Distance is unsigned, triangle is "double-sided"
        /*public static bool PointTriangleDistance(float3 point, TriangleCollider triangle, float maxDistance, out PointDistanceResultInternal result)
           {
            float3 ab = triangle.pointB - triangle.pointA;
            float3 bc = triangle.pointC - triangle.pointB;
            float3 ca = triangle.pointA - triangle.pointC;
            float3 ap = point - triangle.pointA;
            float3 bp = point - triangle.pointB;
            float3 cp = point - triangle.pointC;

            //project point onto plane
            //if clockwise, normal faces "up"
            float3 planeNormal = math.normalize(math.cross(ab, ca));
            float projectionDot = math.dot(planeNormal, point - triangle.pointA);
            float3 projectedPoint = point - projectionDot * planeNormal;

            //calculate edge planes aligned with triangle normal without the normalization (since it isn't required)
            //normals face "inward"
            float3 abUnnormal = math.cross(ab, planeNormal);
            float3 bcUnnormal = math.cross(bc, planeNormal);
            float3 caUnnormal = math.cross(ca, planeNormal);

            float3 dots = new float3(math.dot(abUnnormal, ap),
                                     math.dot(bcUnnormal, bp),
                                     math.dot(caUnnormal, cp));
            int region = math.csum(math.select(new int3(1, 2, 4), int3.zero, dots >= 0f));  //Todo: bitmask?
            switch (region)
            {
                case 0:
                    {
                        //all inside, hit plane
                        result.hitpoint = projectedPoint;
                        result.distance = math.abs(projectionDot);
                        break;
                    }
                case 1:
                    {
                        //outside ab plane
                        float abLengthSq = math.lengthsq(ab);
                        float dot = math.clamp(math.dot(ap, ab), 0f, abLengthSq);
                        result.hitpoint = triangle.pointA + ab * dot / abLengthSq;
                        result.distance = math.distance(point, result.hitpoint);
                        break;
                    }
                case 2:
                    {
                        //outside bc plane
                        float bcLengthSq = math.lengthsq(bc);
                        float dot = math.clamp(math.dot(bp, bc), 0f, bcLengthSq);
                        result.hitpoint = triangle.pointB + bc * dot / bcLengthSq;
                        result.distance = math.distance(point, result.hitpoint);
                        break;
                    }
                case 3:
                    {
                        //outside ab and bc so closest to point b
                        result.hitpoint = triangle.pointB;
                        result.distance = math.distance(point, triangle.pointB);
                        break;
                    }
                case 4:
                    {
                        //outside ca plane
                        float caLengthSq = math.lengthsq(ca);
                        float dot = math.clamp(math.dot(cp, ca), 0f, caLengthSq);
                        result.hitpoint = triangle.pointC + ca * dot / caLengthSq;
                        result.distance = math.distance(point, result.hitpoint);
                        break;
                    }
                case 5:
                    {
                        //outside ab and ca so closest to point a
                        result.hitpoint = triangle.pointA;
                        result.distance = math.distance(point, triangle.pointA);
                        break;
                    }
                case 6:
                    {
                        //outside bc and ca so closest to point c
                        result.hitpoint = triangle.pointC;
                        result.distance = math.distance(point, triangle.pointC);
                        break;
                    }
                default:
                    {
                        //How the heck did we get here?
                        throw new InvalidOperationException();
                        result.hitpoint = projectedPoint;
                        result.distance = 2f * maxDistance;
                        break;
                    }
            }
            result.normal = math.select(planeNormal, -planeNormal, math.dot(result.hitpoint - point, planeNormal) < 0);
            return result.distance <= maxDistance;
           }*/

        //Distance is unsigned, quad is "double-sided"
        public static bool PointQuadDistance(float3 point, simdFloat3 quadPoints, float maxDistance, out PointDistanceResultInternal result)
        {
            simdFloat3 abcd     = new simdFloat3(quadPoints.a, quadPoints.b, quadPoints.c, quadPoints.d);
            simdFloat3 abbccdda = abcd.bcda - abcd.abcd;
            simdFloat3 abcdp    = point - abcd;

            //project point onto plane
            //if clockwise, normal faces "up"
            float3 planeNormal    = math.normalize(math.cross(abbccdda.a, abbccdda.d));  //ab, da
            float  projectionDot  = math.dot(planeNormal, abcdp.a);
            float3 projectedPoint = point - projectionDot * planeNormal;

            //calculate edge planes aligned with quad normal without the normalization (since it isn't required)
            //normals face "inward"
            simdFloat3 abbccddaUnnormal = simd.cross(abbccdda, planeNormal);
            float4     dotsEdges        = simd.dot(abbccddaUnnormal, abcdp);

            if (math.bitmask(dotsEdges < 0f) == 0)  //if (math.all(dotsEdges >= 0))
            {
                result.hitpoint = projectedPoint;
                result.distance = math.abs(projectionDot);
            }
            else
            {
                float3 acUnnormal = math.cross(quadPoints.c - quadPoints.a, planeNormal);  //faces D
                float3 bdUnnormal = math.cross(quadPoints.d - quadPoints.b, planeNormal);  //faces A
                float2 dotsDiags  = new float2(math.dot(acUnnormal, abcdp.a), math.dot(bdUnnormal, abcdp.b));
                int    region     = math.csum(math.select(new int2(1, 2), int2.zero, dotsDiags >= 0));  //Todo: bitmask?
                switch (region)
                {
                    case 0:
                    {
                        //closest to da
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
                        //closest to ab
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
                        //closest to bc
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
                        //closest to cd
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
                        //How the heck did we get here?
                        //throw new InvalidOperationException();
                        result.hitpoint = projectedPoint;
                        result.distance = maxDistance * 2f;
                        break;
                    }
                }
            }

            result.normal = math.select(planeNormal, -planeNormal, math.dot(result.hitpoint - point, planeNormal) < 0);
            return result.distance <= maxDistance;
        }
    }
}

