using Latios.Calci;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class PointRayBox
    {
        public static bool AreOverlapping(float3 point, in BoxCollider box, in RigidTransform boxTransform)
        {
            var pointInBoxSpace = math.InverseTransformFast(in boxTransform, point);
            var clampedPoint    = math.clamp(pointInBoxSpace, box.center - box.halfSize, box.center + box.halfSize);
            return pointInBoxSpace.Equals(clampedPoint);
        }

        public static bool WithinDistance(float3 point, in BoxCollider box, in RigidTransform boxTransform, float maxDistance)
        {
            var pointInBoxSpace = math.InverseTransformFast(in boxTransform, point);
            return PointBoxWithin(pointInBoxSpace, in box, maxDistance);
        }

        public static bool DistanceBetween(float3 point, in BoxCollider box, in RigidTransform boxTransform, float maxDistance, out PointDistanceResult result)
        {
            var  pointInBoxSpace = math.InverseTransformFast(in boxTransform, point);
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
            var  rayInBoxSpace      = Ray.InverseTransformRay(in boxTransform, ray);
            bool hit                = RaycastBox(in rayInBoxSpace, in box, out float fraction, out float3 normal);
            result.position         = ray.start + ray.displacement * fraction;
            result.normal           = math.rotate(boxTransform, normal);
            result.distance         = fraction * math.length(ray.displacement);
            result.subColliderIndex = 0;
            return hit;
        }

        internal static bool PointBoxWithin(float3 point, in BoxCollider box, float maxDistance)
        {
            var clampedPoint = math.clamp(point, box.center - box.halfSize, box.center + box.halfSize);
            if (maxDistance >= 0f)
                return math.distancesq(point, clampedPoint) <= maxDistance * maxDistance;
            return math.cmin(box.halfSize - math.abs(point - box.center)) >= math.abs(maxDistance);
        }

        internal static bool PointBoxDistance(float3 point, in BoxCollider box, float maxDistance, out PointDistanceResultInternal result)
        {
            // Idea: The positive octant of the box contains 8 feature regions: inside, 3 faces, 3 edges, and a corner.
            // The other octants are identical except with flipped signs. So if we unflip signs, calculate the result
            // in the positive octant, and then flip signs again, we get a valid result.
            float3 osPoint      = point - box.center;  //os = origin space
            bool3  isNegative   = osPoint < 0f;
            float3 ospPoint     = math.select(osPoint, -osPoint, isNegative);  //osp = origin space positive
            bool3  outside      = ospPoint >= box.halfSize;
            int    outsideMask  = math.bitmask(new bool4(outside, false));
            int    outsideCount = math.countbits(outsideMask);
            bool3  axesMask;
            int    axesBitmask;
            int    axesCount;
            float3 hitpoint;
            if (outsideCount == 0)
            {
                // Inside the box. Get the closest wall.
                float3 delta   = box.halfSize - ospPoint;
                float  min     = math.cmin(delta);
                bool3  minMask = min == delta;
                // Prioritize y first, then z, then x if multiple distances perfectly match.
                minMask.xz      &= !minMask.y;
                minMask.x       &= !minMask.z;
                axesMask         = minMask;
                axesBitmask      = math.bitmask(new bool4(minMask, false));
                axesCount        = 1;
                hitpoint         = math.select(ospPoint, box.halfSize, minMask);
                result.distance  = -min;
            }
            else
            {
                // The closest feature is the face, edge, or corner based on which axes are outside the box.
                axesMask        = outside;
                axesBitmask     = outsideMask;
                axesCount       = outsideCount;
                hitpoint        = math.select(ospPoint, box.halfSize, outside);
                result.distance = math.length(ospPoint - hitpoint);
            }
            result.normal      = BoxNormalFromSurfaceAxes(axesCount, isNegative, axesMask);
            result.featureCode = FeatureCodeFromSurfaceAxes(axesBitmask, axesCount, isNegative, axesMask);
            result.hitpoint    = math.select(hitpoint, -hitpoint, isNegative) + box.center;
            return result.distance <= maxDistance;
        }

        // Note: Returns true for inside hit
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
            if (RaycastAabb(in ray, in aabb, out fraction) && fraction > 0f)
            {
                // The ray entered through one or more faces that resulted in the hit fraction.
                // That means that `near` inside RaycastAabb() must be the fraction for every face hit.
                // Burst will reuse the following entryFractions from the inlined RaycastAabb().
                float3 entryFractions = math.min((aabb.min - ray.start) * ray.reciprocalDisplacement, (aabb.max - ray.start) * ray.reciprocalDisplacement);
                var    signs          = math.select(1f, -1f, ray.displacement > 0f);
                normal                = math.normalize(math.select(0f, signs, entryFractions == fraction));
                return true;
            }
            else
            {
                normal = float3.zero;
                return false;
            }
        }

        internal static bool RaycastRoundedBox(in Ray ray, in BoxCollider box, float radius, out float fraction, out float3 roundedNormal, out float3 featureNormal)
        {
            // Shift the ray and box so that the box center is at the origin
            float3 rayStart = ray.start - box.center;
            // Early out if the ray starts inside
            if (math.lengthsq(math.max(math.abs(rayStart) - box.halfSize, 0f)) <= radius * radius)
            {
                fraction      = default;
                roundedNormal = default;
                featureNormal = default;
                return false;
            }

            // Figure out where the ray enters the AABB of the rounded box. If it misses, early out.
            var boxAabb = new Aabb(box.center - box.halfSize - radius, box.center + box.halfSize + radius);
            if (!RaycastAabb(in ray, in boxAabb, out float aabbFraction))
            {
                fraction      = default;
                roundedNormal = default;
                featureNormal = default;
                return false;
            }
            float3 aabbHitPosition = rayStart + ray.displacement * aabbFraction;

            // If two axes are within the real box extents, then we hit a flat part.
            bool3 withinHalfSize      = math.abs(aabbHitPosition) <= box.halfSize;
            int   withinHalfSizeCount = math.countbits(math.bitmask(new bool4(withinHalfSize, false)));
            if (withinHalfSizeCount == 2)
            {
                fraction      = aabbFraction;
                var signs     = math.select(1f, -1f, aabbHitPosition < 0f);
                roundedNormal = math.select(signs, float3.zero, withinHalfSize);
                featureNormal = roundedNormal;
                return aabbFraction > 0f;
            }
            else if (Hint.Unlikely(withinHalfSizeCount == 3))
            {
                fraction              = aabbFraction;
                float3 entryFractions = math.min((boxAabb.min - ray.start) * ray.reciprocalDisplacement, (boxAabb.max - ray.start) * ray.reciprocalDisplacement);
                var    signs          = math.select(1f, -1f, ray.displacement > 0f);
                roundedNormal         = math.normalize(math.select(0f, signs, entryFractions == aabbFraction));
                featureNormal         = roundedNormal;
                return aabbFraction > 0f;
            }

            // Take any given point along the ray, and compare it to the interval of each axis covered by the interior box.
            // If the point is within 3 intervals, it is inside the interior box. If it is within 2 intervals, it is outside the interior
            // but inside the rounded box near a face. If the point is within 1 interval, it is near an interior edge. And if the point
            // is outside all intervals, it is nearest to a corner.
            float3 negativeIntervalFractions = (-box.halfSize - rayStart) * ray.reciprocalDisplacement;
            float3 positiveIntervalFractions = (box.halfSize - rayStart) * ray.reciprocalDisplacement;
            // Note: The enter and exit fractions are clamped to the AABB entrypoint, which is effectively clamping the intervals to where
            // the ray starts.
            float3 intervalEnterFractions = math.max(math.min(negativeIntervalFractions, positiveIntervalFractions), aabbFraction);
            float3 intervalExitFractions  = math.max(math.max(negativeIntervalFractions, positiveIntervalFractions), aabbFraction);
            // Fetch the sphere in the octant where the ray hit the AABB, along with the sphere in the octant where the ray exits each interval.
            // Replicate each ray and shift it so that each sphere is at the origin.
            simdFloat3 cornerRayStarts         = default;
            float4     cornerIntervalFractions = new float4(aabbFraction, intervalExitFractions);
            cornerRayStarts.x                  = rayStart.x - math.select(box.halfSize.x, -box.halfSize.x, rayStart.x + ray.displacement.x * cornerIntervalFractions < 0f);
            cornerRayStarts.y                  = rayStart.y - math.select(box.halfSize.y, -box.halfSize.y, rayStart.y + ray.displacement.y * cornerIntervalFractions < 0f);
            cornerRayStarts.z                  = rayStart.z - math.select(box.halfSize.z, -box.halfSize.z, rayStart.z + ray.displacement.z * cornerIntervalFractions < 0f);
            simdFloat3 cornerRayDisplacements  = new simdFloat3(ray.displacement);
            bool4      cornerHits              = PointRaySphere.Raycast4OriginSpheres(in cornerRayStarts, in cornerRayDisplacements, radius, out float4 cornerFractions);
            cornerFractions                    = math.select(2f, cornerFractions, cornerHits);

            // Fetch the sphere in the octant when the ray enters each interval. D duplicates A.
            // Replicate each ray and shift it so that each sphere is at the origin.
            simdFloat3 edgeRayStarts         = default;
            float4     edgeIntervalFractions = intervalEnterFractions.xyzx;
            edgeRayStarts.x                  = rayStart.x - math.select(box.halfSize.x, -box.halfSize.x, rayStart.x + ray.displacement.x * edgeIntervalFractions < 0f);
            edgeRayStarts.y                  = rayStart.y - math.select(box.halfSize.y, -box.halfSize.y, rayStart.y + ray.displacement.y * edgeIntervalFractions < 0f);
            edgeRayStarts.z                  = rayStart.z - math.select(box.halfSize.z, -box.halfSize.z, rayStart.z + ray.displacement.z * edgeIntervalFractions < 0f);
            // Zero out the interval's axis.
            edgeRayStarts.x = new float4(0f, edgeRayStarts.x.yz, 0f);
            edgeRayStarts.y = new float4(edgeRayStarts.y.x, 0f, edgeRayStarts.y.zw);
            edgeRayStarts.z = new float4(edgeRayStarts.z.xy, 0f, edgeRayStarts.z.w);
            // Zero out the interval axes for displacements too.
            simdFloat3 edgeRayDisplacements = cornerRayDisplacements;
            edgeRayDisplacements.x          = new float4(0f, edgeRayDisplacements.x.yz, 0f);
            edgeRayDisplacements.y          = new float4(edgeRayDisplacements.y.x, 0f, edgeRayDisplacements.y.zw);
            edgeRayDisplacements.z          = new float4(edgeRayDisplacements.z.xy, 0f, edgeRayDisplacements.z.w);
            bool4 edgeHits                  = PointRaySphere.Raycast4OriginSpheres(in edgeRayStarts, in edgeRayDisplacements, radius, out float4 edgeFractions);
            // Only count hits if the hitpoint on the axis is within the interval (the ray hit the cylinder).
            var hitAxesLocations  = rayStart.xyzx + ray.displacement.xyzx * edgeFractions;
            edgeHits             &= math.abs(hitAxesLocations) <= box.halfSize.xyzx;
            edgeFractions         = math.select(2f, edgeFractions, edgeHits);

            // Reduce
            fraction                 = math.cmin(math.min(cornerFractions, edgeFractions));
            float3 rayHitpoint       = rayStart + ray.displacement * fraction;
            float3 innerBoxHitpoint  = math.clamp(rayHitpoint, -box.halfSize, box.halfSize);
            roundedNormal            = math.normalizesafe(rayHitpoint - innerBoxHitpoint, -math.normalizesafe(ray.displacement));
            featureNormal            = math.select(0f, 1f, innerBoxHitpoint == box.halfSize);
            featureNormal           += math.select(0f, -1f, innerBoxHitpoint == -box.halfSize);
            featureNormal            = math.normalizesafe(featureNormal, roundedNormal);
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

        static ushort FeatureCodeFromSurfaceAxes(int axesBitmask, int axesCount, bool3 isNegative, bool3 axesMask)
        {
            int negativeBits = math.bitmask(new bool4(isNegative, false));
            int faceCode     = 0x8000 + math.tzcnt(axesBitmask) + math.select(0, 3, math.any(axesMask & isNegative));
            // An edge runs along the one axis not in the mask, and its code holds the signs of the other two axes in order.
            int edgeAxis      = math.tzcnt(~axesBitmask & 7);
            int belowAxisBits = negativeBits & ((1 << edgeAxis) - 1);
            int aboveAxisBits = (negativeBits >> (edgeAxis + 1)) << edgeAxis;
            int edgeCode      = 0x4000 + edgeAxis * 4 + (belowAxisBits | aboveAxisBits);
            return (ushort)math.select(math.select(faceCode, edgeCode, axesCount == 2), negativeBits, axesCount == 3);
        }

        static float3 BoxNormalFromSurfaceAxes(int axesCount, bool3 isNegative, bool3 axesMask)
        {
            float  componentScale = math.select(math.select(1f, math.SQRT2 / 2f, axesCount == 2), math.normalize(math.float3(1f)).x, axesCount == 3);
            float3 normal         = math.select(0f, componentScale, axesMask);
            return math.select(normal, -normal, isNegative);
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

        internal static void EdgeEndpointsFromEdgeFeatureCode(in BoxCollider box, ushort featureCode, out float3 endpointA, out float3 endpointB)
        {
            var offAxisSigns = math.select(1f, -1f, (featureCode & new int2(1, 2)) != 0);
            switch (featureCode & 0xc)
            {
                case 0x0:
                    endpointA = new float3(-1f, offAxisSigns);
                    endpointB = new float3(1f, offAxisSigns);
                    break;
                case 0x4:
                    endpointA = new float3(offAxisSigns.x, -1f, offAxisSigns.y);
                    endpointB = new float3(offAxisSigns.x, 1f, offAxisSigns.y);
                    break;
                case 0x8:
                    endpointA = new float3(offAxisSigns, -1f);
                    endpointB = new float3(offAxisSigns, 1f);
                    break;
                default:
                    endpointA = endpointB = 0f;
                    break;
            }
            endpointA = box.center + box.halfSize * endpointA;
            endpointB = box.center + box.halfSize * endpointB;
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
            float4 secondCompPos       = new float4(1f, 1f, -1f, -1f);  // CCW so that edge X plane_normal => outward
            switch (faceIndex)
            {
                case 0:  // positive X
                    plane    = new Plane(new float3(1f, 0f, 0f), -box.halfSize.x - box.center.x);
                    vertices = new simdFloat3(ones, secondCompPos, firstComponent);
                    break;
                case 1:  // positive Y
                    plane    = new Plane(new float3(0f, 1f, 0f), -box.halfSize.y - box.center.y);
                    vertices = new simdFloat3(firstComponent, ones, secondCompPos);
                    break;
                case 2:  // positive Z
                    plane    = new Plane(new float3(0f, 0f, 1f), -box.halfSize.z - box.center.z);
                    vertices = new simdFloat3(secondCompPos, firstComponent, ones);
                    break;
                case 3:  // negative X
                    plane    = new Plane(new float3(-1f, 0f, 0f), -box.halfSize.x + box.center.x);
                    vertices = new simdFloat3(-ones, secondCompPos, -firstComponent);
                    break;
                case 4:  // negative Y
                    plane    = new Plane(new float3(0f, -1f, 0f), -box.halfSize.y + box.center.y);
                    vertices = new simdFloat3(firstComponent, -ones, -secondCompPos);
                    break;
                case 5:  // negative Z
                    plane    = new Plane(new float3(0f, 0f, -1f), -box.halfSize.z + box.center.z);
                    vertices = new simdFloat3(secondCompPos, -firstComponent, -ones);
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

