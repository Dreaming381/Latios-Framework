using Unity.Burst;
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
                hitpoint = math.transform(boxTransform, localResult.hitpoint),
                normal   = math.rotate(boxTransform, localResult.normal),
                distance = localResult.distance
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

        public static bool PointBoxDistance(float3 point, in BoxCollider box, float maxDistance, out PointDistanceResultInternal result)
        {
            // Idea: The positive octant of the box contains 7 feature regions: 3 faces, 3 edges, and inside.
            // The other octants are identical except with flipped signs. So if we unflip signs,
            // calculate the distance for these 7 regions, and then flip signs again, we get a valid result.
            // We use feature regions rather than feature types to avoid swizzling since that would require a second branch.
            float3 osPoint    = point - box.center;  //os = origin space
            bool3  isNegative = osPoint < 0f;
            float3 ospPoint   = math.select(osPoint, -osPoint, isNegative);  //osp = origin space positive
            int    region     = math.csum(math.select(new int3(4, 2, 1), int3.zero, ospPoint < box.halfSize));
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
                    minMask.xz      &= !minMask.y;
                    minMask.x       &= !minMask.z;
                    result.hitpoint  = math.select(ospPoint, box.halfSize, minMask);
                    result.distance  = -min;
                    result.normal    = math.select(0f, 1f, minMask);
                    break;
                }
                case 1:
                {
                    // xy in box, z outside
                    // Closest feature is the z-face
                    result.distance = ospPoint.z - box.halfSize.z;
                    result.hitpoint = new float3(ospPoint.xy, box.halfSize.z);
                    result.normal   = new float3(0f, 0f, 1f);
                    break;
                }
                case 2:
                {
                    // xz in box, y outside
                    // Closest feature is the y-face
                    result.distance = ospPoint.y - box.halfSize.y;
                    result.hitpoint = new float3(ospPoint.x, box.halfSize.y, ospPoint.z);
                    result.normal   = new float3(0f, 1f, 0f);
                    break;
                }
                case 3:
                {
                    // x in box, yz outside
                    // Closest feature is the x-axis edge
                    result.distance = math.distance(ospPoint.yz, box.halfSize.yz);
                    result.hitpoint = new float3(ospPoint.x, box.halfSize.yz);
                    result.normal   = new float3(0f, math.SQRT2 / 2f, math.SQRT2 / 2f);
                    break;
                }
                case 4:
                {
                    // yz in box, x outside
                    // Closest feature is the x-face
                    result.distance = ospPoint.x - box.halfSize.x;
                    result.hitpoint = new float3(box.halfSize.x, ospPoint.yz);
                    result.normal   = new float3(1f, 0f, 0f);
                    break;
                }
                case 5:
                {
                    // y in box, xz outside
                    // Closest feature is the y-axis edge
                    result.distance = math.distance(ospPoint.xz, box.halfSize.xz);
                    result.hitpoint = new float3(box.halfSize.x, ospPoint.y, box.halfSize.y);
                    result.normal   = new float3(math.SQRT2 / 2f, 0f, math.SQRT2 / 2f);
                    break;
                }
                case 6:
                {
                    // z in box, xy outside
                    // Closest feature is the z-axis edge
                    result.distance = math.distance(ospPoint.xy, box.halfSize.xy);
                    result.hitpoint = new float3(box.halfSize.xy, ospPoint.z);
                    result.normal   = new float3(math.SQRT2 / 2f, math.SQRT2 / 2f, 0f);
                    break;
                }
                default:
                {
                    // xyz outside box
                    // Closest feature is the osp corner
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

        public static bool RaycastAabb(in Ray ray, in Aabb aabb, out float fraction)
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

        public static bool RaycastRoundedBox(in Ray ray, in BoxCollider box, float radius, out float fraction, out float3 normal)
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
    }
}

