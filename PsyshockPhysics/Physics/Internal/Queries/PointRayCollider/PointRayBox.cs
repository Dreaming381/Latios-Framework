using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    [BurstCompile]
    internal static class PointRayBox
    {
        public static bool DistanceBetween(float3 point, in BoxCollider box, in RigidTransform boxTransform, float maxDistance, out PointDistanceResult result)
        {
            var  pointInBoxSpace = math.transform(math.inverse(boxTransform), point);
            bool hit             = PointBoxDistance(pointInBoxSpace, in box, maxDistance, out var localResult);
            result               = new PointDistanceResult
            {
                hitpoint         = math.transform(boxTransform, localResult.hitpoint),
                normal           = math.rotate(boxTransform, localResult.normal),
                distance         = localResult.distance,
                subColliderIndex = 0
            };
            return hit;
        }

        public static bool Raycast(in Ray ray, in BoxCollider box, in RigidTransform boxTransform, out RaycastResult result)
        {
            var  rayInBoxSpace      = Ray.TransformRay(math.inverse(boxTransform), ray);
            bool hit                = RaycastBox(in rayInBoxSpace, in box, out float fraction, out float3 normal);
            result.position         = math.lerp(ray.start, ray.end, fraction);
            result.normal           = math.rotate(boxTransform, normal);
            result.distance         = math.distance(ray.start, result.position);
            result.subColliderIndex = 0;
            return hit;
        }

        internal static bool PointBoxDistance(float3 point, in BoxCollider box, float maxDistance, out PointDistanceResultInternal result)
        {
            // Idea: The positive octant of the box contains 7 feature regions: 3 faces, 3 edges, and inside.
            // The other octants are identical except with flipped signs. So if we unflip signs,
            // calculate the distance for these 7 regions, and then flip signs again, we get a valid result.
            // We use feature regions rather than feature types to avoid swizzling since that would require a second branch.
            float3 osPoint    = point - box.center;  //os = origin space
            bool3  isNegative = osPoint < 0f;
            float3 ospPoint   = math.select(osPoint, -osPoint, isNegative);  //osp = origin space positive
            int    region     = math.bitmask(new bool4(ospPoint >= box.halfSize, false).zyxw);
            switch (region)
            {
                case 0:
                {
                    // Inside the box. Get the closest wall.
                    float3 delta   = box.halfSize - ospPoint;
                    float  min     = math.cmin(delta);
                    bool3  minMask = min == delta;
                    // Prioritize y first, then z, then x if multiple distances perfectly match.
                    // Todo: Should this be configurabe?
                    minMask.xz         &= !minMask.y;
                    minMask.x          &= !minMask.z;
                    result.hitpoint     = math.select(ospPoint, box.halfSize, minMask);
                    result.distance     = -min;
                    result.normal       = math.select(0f, 1f, minMask);
                    result.featureCode  = (ushort)(0x8000 | (math.tzcnt(math.bitmask(new bool4(minMask, false))) + math.select(0, 3, math.any(minMask & isNegative))));
                    break;
                }
                case 1:
                {
                    // xy in box, z outside
                    // Closest feature is the z-face
                    result.distance    = ospPoint.z - box.halfSize.z;
                    result.hitpoint    = new float3(ospPoint.xy, box.halfSize.z);
                    result.normal      = new float3(0f, 0f, 1f);
                    result.featureCode = (ushort)(0x8002 + math.select(0, 3, isNegative.z));
                    break;
                }
                case 2:
                {
                    // xz in box, y outside
                    // Closest feature is the y-face
                    result.distance    = ospPoint.y - box.halfSize.y;
                    result.hitpoint    = new float3(ospPoint.x, box.halfSize.y, ospPoint.z);
                    result.normal      = new float3(0f, 1f, 0f);
                    result.featureCode = (ushort)(0x8001 + math.select(0, 3, isNegative.y));
                    break;
                }
                case 3:
                {
                    // x in box, yz outside
                    // Closest feature is the x-axis edge
                    result.distance    = math.distance(ospPoint.yz, box.halfSize.yz);
                    result.hitpoint    = new float3(ospPoint.x, box.halfSize.yz);
                    result.normal      = new float3(0f, math.SQRT2 / 2f, math.SQRT2 / 2f);
                    result.featureCode = (ushort)(0x4000 + math.bitmask(new bool4(isNegative.yz, false, false)));
                    break;
                }
                case 4:
                {
                    // yz in box, x outside
                    // Closest feature is the x-face
                    result.distance    = ospPoint.x - box.halfSize.x;
                    result.hitpoint    = new float3(box.halfSize.x, ospPoint.yz);
                    result.normal      = new float3(1f, 0f, 0f);
                    result.featureCode = (ushort)(0x8000 + math.select(0, 3, isNegative.x));
                    break;
                }
                case 5:
                {
                    // y in box, xz outside
                    // Closest feature is the y-axis edge
                    result.distance    = math.distance(ospPoint.xz, box.halfSize.xz);
                    result.hitpoint    = new float3(box.halfSize.x, ospPoint.y, box.halfSize.y);
                    result.normal      = new float3(math.SQRT2 / 2f, 0f, math.SQRT2 / 2f);
                    result.featureCode = (ushort)(0x4004 + math.bitmask(new bool4(isNegative.xz, false, false)));
                    break;
                }
                case 6:
                {
                    // z in box, xy outside
                    // Closest feature is the z-axis edge
                    result.distance    = math.distance(ospPoint.xy, box.halfSize.xy);
                    result.hitpoint    = new float3(box.halfSize.xy, ospPoint.z);
                    result.normal      = new float3(math.SQRT2 / 2f, math.SQRT2 / 2f, 0f);
                    result.featureCode = (ushort)(0x4008 + math.bitmask(new bool4(isNegative.xy, false, false)));
                    break;
                }
                default:
                {
                    // xyz outside box
                    // Closest feature is the osp corner
                    result.distance    = math.distance(ospPoint, box.halfSize);
                    result.hitpoint    = box.halfSize;
                    result.normal      = math.normalize(math.float3(1f));
                    result.featureCode = (ushort)math.bitmask(new bool4(isNegative, false));
                    break;
                }
            }
            result.hitpoint = math.select(result.hitpoint, -result.hitpoint, isNegative) + box.center;
            result.normal   = math.select(result.normal, -result.normal, isNegative);
            return result.distance <= maxDistance;
        }

        internal static bool RaycastAabb(in Ray ray, in Aabb aabb, out float fraction)
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

        //Note: Unity.Physics does not have an equivalent for this. It raycasts against the convex polygon.
        private static bool RaycastBox(in Ray ray, in BoxCollider box, out float fraction, out float3 normal)
        {
            Aabb aabb = new Aabb(box.center - box.halfSize, box.center + box.halfSize);
            if (RaycastAabb(in ray, in aabb, out fraction))
            {
                // Idea: Calculate the distance from the hitpoint to each plane of the AABB.
                // The smallest distance is what we consider the plane we actually hit.
                // Also, mask out planes whose normal does not face against the ray.
                // Todo: Is that last step necessary?
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

        internal static bool RaycastRoundedBox(in Ray ray, in BoxCollider box, float radius, out float fraction, out float3 normal)
        {
            // Early out if inside hit
            if (PointBoxDistance(ray.start, in box, radius, out _))
            {
                fraction = default;
                normal   = default;
                return false;
            }

            var outerBox       = box;
            outerBox.halfSize += radius;
            bool hitOuter      = RaycastBox(in ray, in outerBox, out fraction, out normal);
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

            var topBottomHits = PointRayCapsule.Raycast4Capsules(in ray, in bTopPoints, in bBottomPoints, radius, out float4 topBottomFractions, out simdFloat3 topBottomNormals);
            var leftRightHits = PointRayCapsule.Raycast4Capsules(in ray, in bLeftPoints, in bRightPoints, radius, out float4 leftRightFractions, out simdFloat3 leftRightNormals);
            var frontBackHits = PointRayCapsule.Raycast4Capsules(in ray, in bFrontPoints, in bBackPoints, radius, out float4 frontBackFractions, out simdFloat3 frontBackNormals);

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

        internal static ushort FeatureCodeFromBoxNormal(float3 normalInBoxSpace)
        {
            bool3 isNegative             = normalInBoxSpace < 0f;
            var   normalsNotZero         = math.abs(normalInBoxSpace) > 0.1f;
            var   normalsNotZeroBitmask  = math.bitmask(new bool4(normalsNotZero, false));
            var   normalsNotZeroCount    = math.countbits(normalsNotZeroBitmask);
            var   featureCodeFace        = 0x8000 | (math.tzcnt(normalsNotZeroBitmask) + math.select(0, 3, math.any(normalsNotZero & isNegative)));
            var   edgeDirectionIndex     = math.tzcnt(~normalsNotZeroBitmask);
            var   featureCodeEdge        = 0x4000 | (edgeDirectionIndex * 4);
            featureCodeEdge             += math.select(0, math.bitmask(new bool4(isNegative.yz, false, false)), edgeDirectionIndex == 0);
            featureCodeEdge             += math.select(0, math.bitmask(new bool4(isNegative.xz, false, false)), edgeDirectionIndex == 1);
            featureCodeEdge             += math.select(0, math.bitmask(new bool4(isNegative.xy, false, false)), edgeDirectionIndex == 2);
            var featureCodeVertex        = math.bitmask(new bool4(isNegative, false));
            return (ushort)math.select(featureCodeFace, math.select(featureCodeEdge, featureCodeVertex, normalsNotZeroCount == 3), normalsNotZeroCount > 1);
        }

        internal static float3 BoxNormalFromFeatureCode(ushort featureCode)
        {
            float root2 = 1f / math.sqrt(2f);
            float root3 = 1f / math.sqrt(3f);
            return featureCode switch
                   {
                       0x0 => new float3(root3, root3, root3),
                       0x1 => new float3(-root3, root3, root3),
                       0x2 => new float3(root3, -root3, root3),
                       0x3 => new float3(-root3, -root3, root3),
                       0x4 => new float3(root3, root3, -root3),
                       0x5 => new float3(-root3, root3, -root3),
                       0x6 => new float3(root3, -root3, -root3),
                       0x7 => new float3(-root3, -root3, -root3),
                       0x4000 => new float3(0f, root2, root2),
                       0x4001 => new float3(0f, -root2, root2),
                       0x4002 => new float3(0f, root2, -root2),
                       0x4003 => new float3(0f, -root2, -root2),
                       0x4004 => new float3(root2, 0f, root2),
                       0x4005 => new float3(-root2, 0f, root2),
                       0x4006 => new float3(root2, 0f, -root2),
                       0x4007 => new float3(-root2, 0f, -root2),
                       0x4008 => new float3(root2, root2, 0f),
                       0x4009 => new float3(-root2, root2, 0f),
                       0x400a => new float3(root2, -root2, 0f),
                       0x400b => new float3(-root2, -root2, 0f),
                       0x8000 => new float3(1f, 0f, 0f),
                       0x8001 => new float3(0f, 1f, 0f),
                       0x8002 => new float3(0f, 0f, 1f),
                       0x8003 => new float3(-1f, 0f, 0f),
                       0x8004 => new float3(0f, -1f, 0f),
                       0x8005 => new float3(0f, 0f, -1f),
                       _ => new float3(0f, 1f, 0f)
                   };
        }

        internal static ushort FeatureCodeFromGjk(byte count, byte a, byte b, byte c)
        {
            switch (count)
            {
                case 1:
                {
                    return a;
                }
                case 2:
                {
                    var xor = a ^ b;
                    return xor switch
                           {
                               1 => (ushort)(0x4000 + (a >> 1)),  // Hit x-edge
                               2 => (ushort)(0x4004 + (a & 1) + ((a >> 1) & 2)),  // Hit y-edge
                               3 => (ushort)(0x8002 + math.select(0, 3, (a & 4) != 0)),  // Hit z face diagonal (rare)
                               4 => (ushort)(0x4008 + (a & 3)),  // Hit z-edge
                               5 => (ushort)(0x8001 + math.select(0, 3, (a & 2) != 0)),  // Hit y face diagonal (rare)
                               6 => (ushort)(0x8000 + math.select(0, 3, (a & 1) != 0)),  // Hit x face diagonal (rare)
                               _ => a,  // We hit an interior edge somehow or returned a vertex twice. This shouldn't happen.
                           };
                }
                case 3:
                {
                    var and = a & b & c;
                    if (and != 0)
                    {
                        return and switch
                               {
                                   1 => 0x8003,  // Hit negative x face
                                   2 => 0x8004,  // Hit negative y face
                                   4 => 0x8005,  // Hit negative z face
                                   _ => a,  // Points got duplicated or something? This shouldn't happen.
                               };
                    }
                    var or = a | b | c;
                    if (or != 7)
                    {
                        return or switch
                               {
                                   3 => 0x8002,  // Hit positive z face
                                   5 => 0x8001,  // Hit positive y face
                                   6 => 0x8000,  // Hit positive x face
                                   _ => a,  // Points got duplicated or something? This shouldn't happen.
                               };
                    }
                    // At this point, we hit an interior triangle somehow. This shouldn't happen.
                    return a;
                }
                default: return a;  // Max is 3.
            }
        }

        internal static void BestFacePlanesAndVertices(in BoxCollider box,
                                                       float3 localDirectionToAlign,
                                                       out simdFloat3 edgePlaneOutwardNormals,
                                                       out float4 edgePlaneDistances,
                                                       out Plane plane,
                                                       out simdFloat3 vertices)
        {
            var axisMagnitudes = math.abs(localDirectionToAlign);
            var bestAxis       = math.cmax(axisMagnitudes) == axisMagnitudes;
            // Prioritize y first, then z, then x if multiple distances perfectly match.
            // Todo: Should this be configurabe?
            bestAxis.xz               &= !bestAxis.y;
            bestAxis.x                &= !bestAxis.z;
            bool   bestAxisIsNegative  = math.any(bestAxis & (localDirectionToAlign < 0f));
            var    faceIndex           = math.tzcnt(math.bitmask(new bool4(bestAxis, false))) + math.select(0, 3, bestAxisIsNegative);
            float4 ones                = 1f;
            float4 firstComponent      = new float4(-1f, 1f, 1f, -1f);
            float4 secondCompPos       = new float4(1f, 1f, -1f, -1f);  // CW so that edge X plane_normal => outward
            switch (faceIndex)
            {
                case 0:  // positive X
                    plane    = new Plane(new float3(1f, 0f, 0f), -box.halfSize.x - box.center.x);
                    vertices = new simdFloat3(ones, firstComponent, secondCompPos);
                    break;
                case 1:  // positive Y
                    plane    = new Plane(new float3(0f, 1f, 0f), -box.halfSize.y - box.center.y);
                    vertices = new simdFloat3(firstComponent, ones, secondCompPos);
                    break;
                case 2:  // positive Z
                    plane    = new Plane(new float3(0f, 0f, 1f), -box.halfSize.z - box.center.z);
                    vertices = new simdFloat3(firstComponent, secondCompPos, ones);
                    break;
                case 3:  // negative X
                    plane    = new Plane(new float3(-1f, 0f, 0f), -box.halfSize.x + box.center.x);
                    vertices = new simdFloat3(-ones, firstComponent, -secondCompPos);
                    break;
                case 4:  // negative Y
                    plane    = new Plane(new float3(0f, -1f, 0f), -box.halfSize.y + box.center.y);
                    vertices = new simdFloat3(firstComponent, -ones, -secondCompPos);
                    break;
                case 5:  // negative Z
                    plane    = new Plane(new float3(0f, 0f, -1f), -box.halfSize.z + box.center.z);
                    vertices = new simdFloat3(firstComponent, -secondCompPos, -ones);
                    break;
                default:  // Should not happen
                    plane    = default;
                    vertices = default;
                    break;
            }
            vertices                *= box.halfSize;
            vertices                += box.center;
            edgePlaneOutwardNormals  = simd.cross(vertices.bcda - vertices, localDirectionToAlign);  // These normals are perpendicular to the contact normal, not the plane.
            edgePlaneDistances       = simd.dot(edgePlaneOutwardNormals, vertices.bcda);
        }
    }
}

