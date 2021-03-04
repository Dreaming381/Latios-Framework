using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class QueriesLowLevelUtils
    {
        //Todo: Copied from Unity.Physics. I still don't fully understand this, but it is working correctly for degenerate segments somehow.
        //I tested with parallel segments, segments with 0-length edges and a few other weird things. It holds up with pretty good accuracy.
        //I'm not sure where the NaNs or infinities disappear. But they do.
        // Find the closest points on a pair of line segments
        internal static void SegmentSegment(float3 pointA, float3 edgeA, float3 pointB, float3 edgeB, out float3 closestAOut, out float3 closestBOut)
        {
            // Find the closest point on edge A to the line containing edge B
            float3 diff = pointB - pointA;

            float r         = math.dot(edgeA, edgeB);
            float s1        = math.dot(edgeA, diff);
            float s2        = math.dot(edgeB, diff);
            float lengthASq = math.lengthsq(edgeA);
            float lengthBSq = math.lengthsq(edgeB);

            float invDenom, invLengthASq, invLengthBSq;
            {
                float  denom = lengthASq * lengthBSq - r * r;
                float3 inv   = 1.0f / new float3(denom, lengthASq, lengthBSq);
                invDenom     = inv.x;
                invLengthASq = inv.y;
                invLengthBSq = inv.z;
            }

            float fracA = (s1 * lengthBSq - s2 * r) * invDenom;
            fracA       = math.clamp(fracA, 0.0f, 1.0f);

            // Find the closest point on edge B to the point on A just found
            float fracB = fracA * (invLengthBSq * r) - invLengthBSq * s2;
            fracB       = math.clamp(fracB, 0.0f, 1.0f);

            // If the point on B was clamped then there may be a closer point on A to the edge
            fracA = fracB * (invLengthASq * r) + invLengthASq * s1;
            fracA = math.clamp(fracA, 0.0f, 1.0f);

            closestAOut = pointA + fracA * edgeA;
            closestBOut = pointB + fracB * edgeB;
        }

        internal static void SegmentSegment(simdFloat3 pointA, simdFloat3 edgeA, simdFloat3 pointB, simdFloat3 edgeB, out simdFloat3 closestAOut, out simdFloat3 closestBOut)
        {
            simdFloat3 diff = pointB - pointA;

            float4 r         = simd.dot(edgeA, edgeB);
            float4 s1        = simd.dot(edgeA, diff);
            float4 s2        = simd.dot(edgeB, diff);
            float4 lengthASq = simd.lengthsq(edgeA);
            float4 lengthBSq = simd.lengthsq(edgeB);

            float4 invDenom, invLengthASq, invLengthBSq;
            {
                float4 denom = lengthASq * lengthBSq - r * r;
                invDenom     = 1.0f / denom;
                invLengthASq = 1.0f / lengthASq;
                invLengthBSq = 1.0f / lengthBSq;
            }

            float4 fracA = (s1 * lengthBSq - s2 * r) * invDenom;
            fracA        = math.clamp(fracA, 0.0f, 1.0f);

            float4 fracB = fracA * (invLengthBSq * r) - invLengthBSq * s2;
            fracB        = math.clamp(fracB, 0.0f, 1.0f);

            fracA = fracB * invLengthASq * r + invLengthASq * s1;
            fracA = math.clamp(fracA, 0.0f, 1.0f);

            closestAOut = pointA + fracA * edgeA;
            closestBOut = pointB + fracB * edgeB;
        }

        internal static void OriginAabb8Points(float3 aabb, simdFloat3 points03, simdFloat3 points47, out float3 closestAOut, out float3 closestBOut, out float axisDistanceOut)
        {
            //Step 1: Find the minimum axis distance
            bool4 minXMask0347 = points03.x <= points47.x;
            bool4 minYMask0347 = points03.y <= points47.y;
            bool4 minZMask0347 = points03.z <= points47.z;

            var minX0347 = simd.select(points47, points03, minXMask0347);
            var maxX0347 = simd.select(points03, points47, minXMask0347);
            var minY0347 = simd.select(points47, points03, minYMask0347);
            var maxY0347 = simd.select(points03, points47, minYMask0347);
            var minZ0347 = simd.select(points47, points03, minZMask0347);
            var maxZ0347 = simd.select(points03, points47, minZMask0347);

            float minXValue = math.cmin(minX0347.x);
            float maxXValue = math.cmax(maxX0347.x);
            float minYValue = math.cmin(minY0347.y);
            float maxYValue = math.cmax(maxY0347.y);
            float minZValue = math.cmin(minZ0347.z);
            float maxZValue = math.cmax(maxZ0347.z);

            float3 minValues = new float3(minXValue, minYValue, minZValue);
            float3 maxValues = new float3(maxXValue, maxYValue, maxZValue);

            float3 distancesToMin = maxValues + aabb;
            float3 distancesToMax = aabb - minValues;
            float3 bestDistances  = math.min(distancesToMin, distancesToMax);
            float  bestDistance   = math.cmin(bestDistances);
            bool3  bestAxisMask   = bestDistance == bestDistances;

            //Step 2: Find the point that matches the bestDistance for the bestDistanceMask and has the least deviation when clamped to the AABB
            simdFloat3 distancesToMin03 = points03 + aabb;
            simdFloat3 distancesToMax03 = aabb - points03;
            simdFloat3 distancesToMin47 = points47 + aabb;
            simdFloat3 distancesToMax47 = aabb - points47;

            bool4 matchesMinX03 = (bestDistance == distancesToMin03.x) & bestAxisMask.x;
            bool4 matchesMinY03 = (bestDistance == distancesToMin03.y) & bestAxisMask.y;
            bool4 matchesMinZ03 = (bestDistance == distancesToMin03.z) & bestAxisMask.z;
            bool4 matchesX03    = matchesMinX03 | (bestDistance == distancesToMax03.x) & bestAxisMask.x;
            bool4 matchesY03    = matchesMinY03 | (bestDistance == distancesToMax03.y) & bestAxisMask.y;
            bool4 matchesZ03    = matchesMinZ03 | (bestDistance == distancesToMax03.z) & bestAxisMask.z;

            bool4 matchesMinX47 = (bestDistance == distancesToMin47.x) & bestAxisMask.x;
            bool4 matchesMinY47 = (bestDistance == distancesToMin47.y) & bestAxisMask.y;
            bool4 matchesMinZ47 = (bestDistance == distancesToMin47.z) & bestAxisMask.z;
            bool4 matchesX47    = matchesMinX47 | (bestDistance == distancesToMax47.x) & bestAxisMask.x;
            bool4 matchesY47    = matchesMinY47 | (bestDistance == distancesToMax47.y) & bestAxisMask.y;
            bool4 matchesZ47    = matchesMinZ47 | (bestDistance == distancesToMax47.z) & bestAxisMask.z;

            float4 diffXValues03 = points03.x - math.clamp(points03.x, -aabb.x, aabb.x);
            float4 diffYValues03 = points03.y - math.clamp(points03.y, -aabb.y, aabb.y);
            float4 diffZValues03 = points03.z - math.clamp(points03.z, -aabb.z, aabb.z);
            float4 diffXValues47 = points47.x - math.clamp(points47.x, -aabb.x, aabb.x);
            float4 diffYValues47 = points47.y - math.clamp(points47.y, -aabb.y, aabb.y);
            float4 diffZValues47 = points47.z - math.clamp(points47.z, -aabb.z, aabb.z);

            float4 distSqYZ03 = math.select(float.MaxValue, diffYValues03 * diffYValues03 + diffZValues03 * diffZValues03, matchesX03);
            float4 distSqXZ03 = math.select(float.MaxValue, diffXValues03 * diffXValues03 + diffZValues03 * diffZValues03, matchesY03);
            float4 distSqXY03 = math.select(float.MaxValue, diffXValues03 * diffXValues03 + diffYValues03 * diffYValues03, matchesZ03);
            float4 distSqYZ47 = math.select(float.MaxValue, diffYValues47 * diffYValues47 + diffZValues47 * diffZValues47, matchesX47);
            float4 distSqXZ47 = math.select(float.MaxValue, diffXValues47 * diffXValues47 + diffZValues47 * diffZValues47, matchesY47);
            float4 distSqXY47 = math.select(float.MaxValue, diffXValues47 * diffXValues47 + diffYValues47 * diffYValues47, matchesZ47);

            bool4  useY03          = distSqXZ03 < distSqYZ03;
            float4 bestDistSq03    = math.select(distSqYZ03, distSqXZ03, useY03);
            bool4  matchesMin03    = math.select(matchesMinX03, matchesMinY03, useY03);
            bool4  useZ03          = distSqXY03 < bestDistSq03;
            bestDistSq03           = math.select(bestDistSq03, distSqXY03, useZ03);
            matchesMin03           = math.select(matchesMin03, matchesMinZ03, useZ03);
            float bestDistSqFrom03 = math.cmin(bestDistSq03);
            int   index03          = math.clamp(math.tzcnt(math.bitmask(bestDistSqFrom03 == bestDistSq03)), 0, 3);

            bool4  useY47          = distSqXZ47 < distSqYZ47;
            float4 bestDistSq47    = math.select(distSqYZ47, distSqXZ47, useY47);
            bool4  matchesMin47    = math.select(matchesMinX47, matchesMinY47, useY47);
            bool4  useZ47          = distSqXY47 < bestDistSq47;
            bestDistSq47           = math.select(bestDistSq47, distSqXY47, useZ47);
            matchesMin47           = math.select(matchesMin47, matchesMinZ47, useZ47);
            float bestDistSqFrom47 = math.cmin(bestDistSq47);
            int   index47          = math.clamp(math.tzcnt(math.bitmask(bestDistSqFrom47 == bestDistSq47)), 0, 3) + 4;

            bool                  use47      = bestDistSqFrom47 < bestDistSqFrom03;
            math.ShuffleComponent bestIndex  = (math.ShuffleComponent)math.select(index03, index47, use47);
            bool4                 matchesMin = math.select(matchesMin03, matchesMin47, use47);
            bool                  useMin     = matchesMin[((int)bestIndex) & 3];

            closestBOut     = simd.shuffle(points03, points47, bestIndex);
            closestAOut     = math.select(closestBOut, math.select(aabb, -aabb, useMin), bestAxisMask);
            closestAOut     = math.clamp(closestAOut, -aabb, aabb);
            axisDistanceOut = -bestDistance;
        }

        public static bool4 ArePointsInsideObb(simdFloat3 points, simdFloat3 obbNormals, float3 distances, float3 halfWidths)
        {
            float3 positives  = distances + halfWidths;
            float3 negatives  = distances - halfWidths;
            var    dots       = simd.dot(points, obbNormals.aaaa);
            bool4  results    = dots <= positives.x;
            results          &= dots >= negatives.x;
            dots              = simd.dot(points, obbNormals.bbbb);
            results          &= dots <= positives.y;
            results          &= dots >= negatives.y;
            dots              = simd.dot(points, obbNormals.cccc);
            results          &= dots <= positives.z;
            results          &= dots >= negatives.z;
            return results;
        }
    }
}

