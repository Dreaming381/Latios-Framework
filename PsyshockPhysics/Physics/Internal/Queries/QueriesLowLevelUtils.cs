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

        internal static bool4 SegmentSegmentInvalidateEndpoints(simdFloat3 pointA,
                                                                simdFloat3 edgeA,
                                                                simdFloat3 pointB,
                                                                simdFloat3 edgeB,
                                                                out simdFloat3 closestAOut,
                                                                out simdFloat3 closestBOut)
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

            return fracA != 0f & fracA != 1f & fracB != 0f & fracB != 1f;
        }

        // Returns true for each segment pair whose result does not include an endpoint on either segment of the pair.
        internal static void OriginAabb8PointsWithEspilonFudge(float3 aabb,
                                                               simdFloat3 points03,
                                                               simdFloat3 points47,
                                                               out float3 closestAOut,
                                                               out float3 closestBOut,
                                                               out float axisDistanceOut)
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

            // Because we bias away from the edge-edge case, nearly parallel edge-faces might report the wrong point.
            // So we add an epsilon fudge and capture the absolute distance
            bool4 matchesMinX03 = (math.abs(bestDistance - distancesToMin03.x) < math.EPSILON) & bestAxisMask.x;
            bool4 matchesMinY03 = (math.abs(bestDistance - distancesToMin03.y) < math.EPSILON) & bestAxisMask.y;
            bool4 matchesMinZ03 = (math.abs(bestDistance - distancesToMin03.z) < math.EPSILON) & bestAxisMask.z;
            bool4 matchesX03    = matchesMinX03 | (math.abs(bestDistance - distancesToMax03.x) < math.EPSILON) & bestAxisMask.x;
            bool4 matchesY03    = matchesMinY03 | (math.abs(bestDistance - distancesToMax03.y) < math.EPSILON) & bestAxisMask.y;
            bool4 matchesZ03    = matchesMinZ03 | (math.abs(bestDistance - distancesToMax03.z) < math.EPSILON) & bestAxisMask.z;

            bool4 matchesMinX47 = (math.abs(bestDistance - distancesToMin47.x) < math.EPSILON) & bestAxisMask.x;
            bool4 matchesMinY47 = (math.abs(bestDistance - distancesToMin47.y) < math.EPSILON) & bestAxisMask.y;
            bool4 matchesMinZ47 = (math.abs(bestDistance - distancesToMin47.z) < math.EPSILON) & bestAxisMask.z;
            bool4 matchesX47    = matchesMinX47 | (math.abs(bestDistance - distancesToMax47.x) < math.EPSILON) & bestAxisMask.x;
            bool4 matchesY47    = matchesMinY47 | (math.abs(bestDistance - distancesToMax47.y) < math.EPSILON) & bestAxisMask.y;
            bool4 matchesZ47    = matchesMinZ47 | (math.abs(bestDistance - distancesToMax47.z) < math.EPSILON) & bestAxisMask.z;

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

        internal static void OriginAabb8PointsWithEspilonFudgeDebug(float3 aabb,
                                                                    simdFloat3 points03,
                                                                    simdFloat3 points47,
                                                                    out float3 closestAOut,
                                                                    out float3 closestBOut,
                                                                    out float axisDistanceOut)
        {
            UnityEngine.Debug.Log(
                $"Begin OriginAabb8PointsWithEpsilonFudge. aabb: {aabb}, points: {points03.a}, {points03.b}, {points03.c}, {points03.d}, {points47.a}, {points47.b}, {points47.c}, {points47.d}");

            //Step 1: Find the minimum axis distance
            bool4 minXMask0347 = points03.x <= points47.x;
            bool4 minYMask0347 = points03.y <= points47.y;
            bool4 minZMask0347 = points03.z <= points47.z;
            UnityEngine.Debug.Log($"minXMask0347: {minXMask0347}, minYMask0347: {minYMask0347}, minZMask0347: {minZMask0347}");

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
            UnityEngine.Debug.Log(
                $"minValues: {minValues}, maxValues: {maxValues}, distancesToMin: {distancesToMin}, distancesToMax: {distancesToMax}, bestDistance: {bestDistance}, bestAxisMask: {bestAxisMask}");

            //Step 2: Find the point that matches the bestDistance for the bestDistanceMask and has the least deviation when clamped to the AABB
            simdFloat3 distancesToMin03 = points03 + aabb;
            simdFloat3 distancesToMax03 = aabb - points03;
            simdFloat3 distancesToMin47 = points47 + aabb;
            simdFloat3 distancesToMax47 = aabb - points47;

            // Because we bias away from the edge-edge case, nearly parallel edge-faces might report the wrong point.
            // So we add an epsilon fudge and capture the absolute distance
            bool4 matchesMinX03 = (math.abs(bestDistance - distancesToMin03.x) < math.EPSILON) & bestAxisMask.x;
            bool4 matchesMinY03 = (math.abs(bestDistance - distancesToMin03.y) < math.EPSILON) & bestAxisMask.y;
            bool4 matchesMinZ03 = (math.abs(bestDistance - distancesToMin03.z) < math.EPSILON) & bestAxisMask.z;
            bool4 matchesX03    = matchesMinX03 | (math.abs(bestDistance - distancesToMax03.x) < math.EPSILON) & bestAxisMask.x;
            bool4 matchesY03    = matchesMinY03 | (math.abs(bestDistance - distancesToMax03.y) < math.EPSILON) & bestAxisMask.y;
            bool4 matchesZ03    = matchesMinZ03 | (math.abs(bestDistance - distancesToMax03.z) < math.EPSILON) & bestAxisMask.z;

            bool4 matchesMinX47 = (math.abs(bestDistance - distancesToMin47.x) < math.EPSILON) & bestAxisMask.x;
            bool4 matchesMinY47 = (math.abs(bestDistance - distancesToMin47.y) < math.EPSILON) & bestAxisMask.y;
            bool4 matchesMinZ47 = (math.abs(bestDistance - distancesToMin47.z) < math.EPSILON) & bestAxisMask.z;
            bool4 matchesX47    = matchesMinX47 | (math.abs(bestDistance - distancesToMax47.x) < math.EPSILON) & bestAxisMask.x;
            bool4 matchesY47    = matchesMinY47 | (math.abs(bestDistance - distancesToMax47.y) < math.EPSILON) & bestAxisMask.y;
            bool4 matchesZ47    = matchesMinZ47 | (math.abs(bestDistance - distancesToMax47.z) < math.EPSILON) & bestAxisMask.z;
            UnityEngine.Debug.Log(
                $"matchesX03: {matchesX03}, matchesY03: {matchesY03}, matchesZ03: {matchesZ03}, matchesX47: {matchesX47}, matchesY47: {matchesY47}, matchesZ47: {matchesZ47}");

            float4 diffXValues03 = points03.x - math.clamp(points03.x, -aabb.x, aabb.x);
            float4 diffYValues03 = points03.y - math.clamp(points03.y, -aabb.y, aabb.y);
            float4 diffZValues03 = points03.z - math.clamp(points03.z, -aabb.z, aabb.z);
            float4 diffXValues47 = points47.x - math.clamp(points47.x, -aabb.x, aabb.x);
            float4 diffYValues47 = points47.y - math.clamp(points47.y, -aabb.y, aabb.y);
            float4 diffZValues47 = points47.z - math.clamp(points47.z, -aabb.z, aabb.z);
            UnityEngine.Debug.Log(
                $"diffXValues03: {diffXValues03}, diffYValues03: {diffYValues03}, diffZValues03: {diffZValues03}, diffXValues47: {diffXValues47}, diffYValues47: {diffYValues47}, diffZValues47: {diffZValues47}");

            float4 distSqYZ03 = math.select(float.MaxValue, diffYValues03 * diffYValues03 + diffZValues03 * diffZValues03, matchesX03);
            float4 distSqXZ03 = math.select(float.MaxValue, diffXValues03 * diffXValues03 + diffZValues03 * diffZValues03, matchesY03);
            float4 distSqXY03 = math.select(float.MaxValue, diffXValues03 * diffXValues03 + diffYValues03 * diffYValues03, matchesZ03);
            float4 distSqYZ47 = math.select(float.MaxValue, diffYValues47 * diffYValues47 + diffZValues47 * diffZValues47, matchesX47);
            float4 distSqXZ47 = math.select(float.MaxValue, diffXValues47 * diffXValues47 + diffZValues47 * diffZValues47, matchesY47);
            float4 distSqXY47 = math.select(float.MaxValue, diffXValues47 * diffXValues47 + diffYValues47 * diffYValues47, matchesZ47);
            UnityEngine.Debug.Log(
                $"distSqYZ03: {distSqYZ03}, distSqXZ03: {distSqXZ03}, distSqXY03: {distSqXY03}, distSqYZ47: {distSqYZ47}, distSqXZ47: {distSqXZ47}, distSqXY47: {distSqXY47}");

            bool4  useY03          = distSqXZ03 < distSqYZ03;
            float4 bestDistSq03    = math.select(distSqYZ03, distSqXZ03, useY03);
            bool4  matchesMin03    = math.select(matchesMinX03, matchesMinY03, useY03);
            bool4  useZ03          = distSqXY03 < bestDistSq03;
            bestDistSq03           = math.select(bestDistSq03, distSqXY03, useZ03);
            matchesMin03           = math.select(matchesMin03, matchesMinZ03, useZ03);
            float bestDistSqFrom03 = math.cmin(bestDistSq03);
            int   index03          = math.clamp(math.tzcnt(math.bitmask(bestDistSqFrom03 == bestDistSq03)), 0, 3);
            UnityEngine.Debug.Log(
                $"useY03: {useY03}, useZ03: {useZ03}, bestDistSq03: {bestDistSq03}, matchesMin03: {matchesMin03}, bestDistSqFrom03: {bestDistSqFrom03}, index03: {index03}");

            bool4  useY47          = distSqXZ47 < distSqYZ47;
            float4 bestDistSq47    = math.select(distSqYZ47, distSqXZ47, useY47);
            bool4  matchesMin47    = math.select(matchesMinX47, matchesMinY47, useY47);
            bool4  useZ47          = distSqXY47 < bestDistSq47;
            bestDistSq47           = math.select(bestDistSq47, distSqXY47, useZ47);
            matchesMin47           = math.select(matchesMin47, matchesMinZ47, useZ47);
            float bestDistSqFrom47 = math.cmin(bestDistSq47);
            int   index47          = math.clamp(math.tzcnt(math.bitmask(bestDistSqFrom47 == bestDistSq47)), 0, 3) + 4;

            UnityEngine.Debug.Log(
                $"useY47: {useY47}, useZ47: {useZ47}, bestDistSq47: {bestDistSq47}, matchesMin47: {matchesMin47}, bestDistSqFrom47: {bestDistSqFrom47}, index47: {index47}");

            bool                  use47      = bestDistSqFrom47 < bestDistSqFrom03;
            math.ShuffleComponent bestIndex  = (math.ShuffleComponent)math.select(index03, index47, use47);
            bool4                 matchesMin = math.select(matchesMin03, matchesMin47, use47);
            bool                  useMin     = matchesMin[((int)bestIndex) & 3];
            UnityEngine.Debug.Log($"use47: {use47}, bestIndex: {bestIndex}, matchesMin: {matchesMin}, useMin: {useMin}");

            closestBOut     = simd.shuffle(points03, points47, bestIndex);
            closestAOut     = math.select(closestBOut, math.select(aabb, -aabb, useMin), bestAxisMask);
            closestAOut     = math.clamp(closestAOut, -aabb, aabb);
            axisDistanceOut = -bestDistance;
        }

        /*internal static void OriginAabb8BoxPoints(float3 aabbExtents, float3 boxCenter, simdFloat3 points03, simdFloat3 points47, out int closestAIndexOut, out float3 closestBOut, out float signedDistanceOut)
           {
            // This is basically the point vs box algorithm brute-forced and vectorized.
            bool4 isNegativeX03 = points03.x < 0f;
            bool4 isNegativeY03 = points03.y < 0f;
            bool4 isNegativeZ03 = points03.z < 0f;
            bool4 isNegativeX47 = points47.x < 0f;
            bool4 isNegativeY47 = points47.y < 0f;
            bool4 isNegativeZ47 = points47.z < 0f;
            simdFloat3 ospPoints03 = simd.select(points03, -points03, isNegativeX03, isNegativeY03, isNegativeZ03);
            simdFloat3 ospPoints47 = simd.select(points47, -points47, isNegativeX47, isNegativeY47, isNegativeZ47);
            int4 region03 = math.select(4, int4.zero, ospPoints03.x < aabbExtents.x);
            int4 region47 = math.select(4, int4.zero, ospPoints47.x < aabbExtents.x);
            region03 += math.select(2, int4.zero, ospPoints03.y < aabbExtents.y);
            region47 += math.select(2, int4.zero, ospPoints47.y < aabbExtents.y);
            region03 += math.select(1, int4.zero, ospPoints03.z < aabbExtents.z);
            region47 += math.select(1, int4.zero, ospPoints47.z < aabbExtents.z);

            // Region 0: Inside the box. Closest feature is face.
            simdFloat3 delta03 = aabbExtents - ospPoints03;
            simdFloat3 delta47 = aabbExtents - ospPoints47;
            float4 min03 = simd.cminxyz(delta03);
            float4 min47 = simd.cminxyz(delta47);
            bool4 minMaskX03 = min03 == delta03.x;
            bool4 minMaskX47 = min47 == delta47.x;
            bool4 minMaskY03 = min03 == delta03.y;
            bool4 minMaskY47 = min47 == delta47.y;
            bool4 minMaskZ03 = min03 == delta03.z;
            bool4 minMaskZ47 = min47 == delta47.z;
            simdFloat3 closestB03 = simd.select(ospPoints03, new simdFloat3(aabbExtents), minMaskX03, minMaskY03, minMaskZ03);
            simdFloat3 closestB47 = simd.select(ospPoints47, new simdFloat3(aabbExtents), minMaskX47, minMaskY47, minMaskZ47);
            float4 signedDistance03 = -min03;
            float4 signedDistance47 = -min47;

            // Region 1: xy in box, z outside. Closest feature is the z-face.
            signedDistance03 = math.select(signedDistance03, ospPoints03.z - aabbExtents.z, region03 == 1);
            signedDistance47 = math.select(signedDistance47, ospPoints47.z - aabbExtents.z, region47 == 1);
            closestB03 = simd.select(closestB03, new simdFloat3(closestB03.x, closestB03.y, aabbExtents.z), region03 == 1);
            closestB47 = simd.select(closestB47, new simdFloat3(closestB47.x, closestB47.y, aabbExtents.z), region47 == 1);

            // Region 2: xz in box, y outside. Closest feature is the y-face.
            signedDistance03 = math.select(signedDistance03, ospPoints03.y - aabbExtents.y, region03 == 2);
            signedDistance47 = math.select(signedDistance47, ospPoints47.y - aabbExtents.y, region47 == 2);
            closestB03 = simd.select(closestB03, new simdFloat3(closestB03.x, aabbExtents.y, closestB03.z), region03 == 2);
            closestB47 = simd.select(closestB47, new simdFloat3(closestB47.x, aabbExtents.y, closestB47.z), region47 == 2);

            // Region 3: x in box, yz outside. Closest feature is the x-axis edge.
            simdFloat3 diffSq03 = ospPoints03 - aabbExtents;
            simdFloat3 diffSq47 = ospPoints47 - aabbExtents;
            diffSq03 *= diffSq03;
            diffSq47 *= diffSq47;
            signedDistance03 = math.select(signedDistance03, math.sqrt(diffSq03.y + diffSq03.z), region03 == 3);
            signedDistance47 = math.select(signedDistance47, math.sqrt(diffSq47.y + diffSq47.z), region47 == 3);
            closestB03 = simd.select(closestB03, new simdFloat3(closestB03.x, aabbExtents.y, aabbExtents.z), region03 == 3);
            closestB47 = simd.select(closestB47, new simdFloat3(closestB47.x, aabbExtents.y, aabbExtents.z), region47 == 3);

            // Region 4: yz in box, x outside. Closest feature is the x-face.
            signedDistance03 = math.select(signedDistance03, ospPoints03.x - aabbExtents.x, region03 == 4);
            signedDistance47 = math.select(signedDistance47, ospPoints47.x - aabbExtents.x, region47 == 4);
            closestB03 = simd.select(closestB03, new simdFloat3(aabbExtents.x, closestB03.y, closestB03.z), region03 == 4);
            closestB47 = simd.select(closestB47, new simdFloat3(aabbExtents.x, closestB47.y, closestB47.z), region47 == 4);

            // Region 5: y in box, xz outside. Closest feature is the y-axis edge.
            signedDistance03 = math.select(signedDistance03, math.sqrt(diffSq03.x + diffSq03.z), region03 == 5);
            signedDistance47 = math.select(signedDistance47, math.sqrt(diffSq47.x + diffSq47.z), region47 == 5);
            closestB03 = simd.select(closestB03, new simdFloat3(aabbExtents.x, closestB03.y, aabbExtents.z), region03 == 5);
            closestB47 = simd.select(closestB47, new simdFloat3(aabbExtents.x, closestB47.y, aabbExtents.z), region47 == 5);

            // Region 6: z in box, xy outside. Closest feature is the z-axis edge.
            signedDistance03 = math.select(signedDistance03, math.sqrt(diffSq03.x + diffSq03.y), region03 == 6);
            signedDistance47 = math.select(signedDistance47, math.sqrt(diffSq47.x + diffSq47.y), region47 == 6);
            closestB03 = simd.select(closestB03, new simdFloat3(aabbExtents.x, aabbExtents.y, closestB03.z), region03 == 6);
            closestB47 = simd.select(closestB47, new simdFloat3(aabbExtents.x, aabbExtents.y, closestB47.z), region47 == 6);

            // Region 7: xyz outside box. Closest feature is the corner.
            signedDistance03 = math.select(signedDistance03, simd.length(diffSq03), region03 == 7);
            signedDistance47 = math.select(signedDistance47, simd.length(diffSq47), region47 == 7);
            closestB03 = simd.select(closestB03, new simdFloat3(aabbExtents), region03 == 7);
            closestB47 = simd.select(closestB47, new simdFloat3(aabbExtents), region47 == 7);

            // Expand into the full octant space
            closestB03 = simd.select(closestB03, -closestB03, isNegativeX03, isNegativeY03, isNegativeZ03);
            closestB47 = simd.select(closestB47, -closestB47, isNegativeX47, isNegativeY47, isNegativeZ47);

            // We now need to account for punchthrough, which
           }
         */

        public static bool4 ArePointsInsideObbPlusEpsilon(simdFloat3 points, simdFloat3 obbNormals, float3 distances, float3 halfWidths)
        {
            float3 positives  = distances + halfWidths + math.EPSILON;
            float3 negatives  = distances - halfWidths - math.EPSILON;
            var    dots       = simd.dot(points, obbNormals.a);
            bool4  resultsX   = (dots <= positives.x) & (dots >= negatives.x);
            resultsX         |= (-dots <= positives.x) & (-dots >= negatives.x);
            dots              = simd.dot(points, obbNormals.b);
            bool4 resultsY    = (dots <= positives.y) & (dots >= negatives.y);
            resultsY         |= (-dots <= positives.y) & (-dots >= negatives.y);
            dots              = simd.dot(points, obbNormals.c);
            bool4 resultsZ    = (dots <= positives.z) & (dots >= negatives.z);
            resultsZ         |= (-dots <= positives.z) & (-dots >= negatives.z);
            return resultsX & resultsY & resultsZ;
        }
    }
}

