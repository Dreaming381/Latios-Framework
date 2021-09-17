using System;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static partial class SpatialInternal
    {
        public static bool BoxCapsuleDistance(BoxCollider box, CapsuleCollider capsule, float maxDistance, out ColliderDistanceResultInternal result)
        {
            float3 osPointA = capsule.pointA - box.center;  //os = origin space
            float3 osPointB = capsule.pointB - box.center;
            float3 pointsPointOnBox;
            float3 pointsPointOnSegment;
            float  axisDistance;
            //Step 1: Points vs Planes
            {
                float3 distancesToMin = math.max(osPointA, osPointB) + box.halfSize;
                float3 distancesToMax = box.halfSize - math.min(osPointA, osPointB);
                float3 bestDistances  = math.min(distancesToMin, distancesToMax);
                float  bestDistance   = math.cmin(bestDistances);
                bool3  bestAxisMask   = bestDistance == bestDistances;
                //Prioritize y first, then z, then x if multiple distances perfectly match.
                //Todo: Should this be configurabe?
                bestAxisMask.xz      &= !bestAxisMask.y;
                bestAxisMask.x       &= !bestAxisMask.z;
                float3 zeroMask       = math.select(0f, 1f, bestAxisMask);
                bool   useMin         = (bestDistances * zeroMask).Equals(distancesToMin * zeroMask);
                float  aOnAxis        = math.dot(osPointA, zeroMask);
                float  bOnAxis        = math.dot(osPointB, zeroMask);
                bool   aIsGreater     = aOnAxis > bOnAxis;
                pointsPointOnSegment  = math.select(osPointA, osPointB, useMin ^ aIsGreater);
                pointsPointOnBox      = math.select(pointsPointOnSegment, math.select(box.halfSize, -box.halfSize, useMin), bestAxisMask);
                pointsPointOnBox      = math.clamp(pointsPointOnBox, -box.halfSize, box.halfSize);
                axisDistance          = -bestDistance;
            }
            float signedDistanceSq = math.distancesq(pointsPointOnSegment, pointsPointOnBox);
            signedDistanceSq       = math.select(signedDistanceSq, -signedDistanceSq, axisDistance <= 0f);

            //Step 2: Edge vs Edges
            //Todo: We could inline the SegmentSegment invocations to simplify the initial dot products.
            float3     capsuleEdge     = osPointB - osPointA;
            simdFloat3 simdCapsuleEdge = new simdFloat3(capsuleEdge);
            simdFloat3 simdOsPointA    = new simdFloat3(osPointA);
            //x-axes
            simdFloat3 boxPointsX = new simdFloat3(new float3(-box.halfSize.x, box.halfSize.y, box.halfSize.z),
                                                   new float3(-box.halfSize.x, box.halfSize.y, -box.halfSize.z),
                                                   new float3(-box.halfSize.x, -box.halfSize.y, box.halfSize.z),
                                                   new float3(-box.halfSize.x, -box.halfSize.y, -box.halfSize.z));
            simdFloat3 boxEdgesX = new simdFloat3(new float3(2f * box.halfSize.x, 0f, 0f));
            QueriesLowLevelUtils.SegmentSegment(simdOsPointA, simdCapsuleEdge, boxPointsX, boxEdgesX, out simdFloat3 edgesClosestAsX, out simdFloat3 edgesClosestBsX);
            simdFloat3 boxNormalsX = new simdFloat3(new float3(0f, math.SQRT2 / 2f, math.SQRT2 / 2f),
                                                    new float3(0f, math.SQRT2 / 2f, -math.SQRT2 / 2f),
                                                    new float3(0f, -math.SQRT2 / 2f, math.SQRT2 / 2f),
                                                    new float3(0f, -math.SQRT2 / 2f, -math.SQRT2 / 2f));
            //Imagine a line that goes perpendicular through a box's edge at the midpoint.
            //All orientations of that line which do not penetrate the box (tangency is not considered penetrating in this case) are validly resolved collisions.
            //Orientations of the line which do penetrate are not valid.
            //If we constrain the capsule edge to be perpendicular, normalize it, and then compute the dot product, we can compare that to the necessary 45 degree angle
            //where penetration occurs. Parallel lines are excluded because either we want to record a capsule point (step 1) or a perpendicular edge on the box.
            bool   notParallelX       = !capsuleEdge.yz.Equals(float2.zero);
            float4 alignmentsX        = simd.dot(math.normalize(new float3(0f, capsuleEdge.yz)), boxNormalsX);
            bool4  validsX            = (math.abs(alignmentsX) <= math.SQRT2 / 2f) & notParallelX;
            float4 signedDistancesSqX = simd.distancesq(edgesClosestAsX, edgesClosestBsX);
            bool4  insidesX           =
                (math.abs(edgesClosestAsX.x) <= box.halfSize.x) & (math.abs(edgesClosestAsX.y) <= box.halfSize.y) & (math.abs(edgesClosestAsX.z) <= box.halfSize.z);
            signedDistancesSqX = math.select(signedDistancesSqX, -signedDistancesSqX, insidesX);
            signedDistancesSqX = math.select(float.MaxValue, signedDistancesSqX, validsX);

            //y-axis
            simdFloat3 boxPointsY = new simdFloat3(new float3(box.halfSize.x, -box.halfSize.y, box.halfSize.z),
                                                   new float3(box.halfSize.x, -box.halfSize.y, -box.halfSize.z),
                                                   new float3(-box.halfSize.x, -box.halfSize.y, box.halfSize.z),
                                                   new float3(-box.halfSize.x, -box.halfSize.y, -box.halfSize.z));
            simdFloat3 boxEdgesY = new simdFloat3(new float3(0f, 2f * box.halfSize.y, 0f));
            QueriesLowLevelUtils.SegmentSegment(simdOsPointA, simdCapsuleEdge, boxPointsY, boxEdgesY, out simdFloat3 edgesClosestAsY, out simdFloat3 edgesClosestBsY);
            simdFloat3 boxNormalsY = new simdFloat3(new float3(math.SQRT2 / 2f, 0f, math.SQRT2 / 2f),
                                                    new float3(math.SQRT2 / 2f, 0f, -math.SQRT2 / 2f),
                                                    new float3(-math.SQRT2 / 2f, 0f, math.SQRT2 / 2f),
                                                    new float3(-math.SQRT2 / 2f, 0f, -math.SQRT2 / 2f));
            bool   notParallelY       = !capsuleEdge.xz.Equals(float2.zero);
            float4 alignmentsY        = simd.dot(math.normalize(new float3(capsuleEdge.x, 0f, capsuleEdge.z)), boxNormalsY);
            bool4  validsY            = (math.abs(alignmentsY) <= math.SQRT2 / 2f) & notParallelY;
            float4 signedDistancesSqY = simd.distancesq(edgesClosestAsY, edgesClosestBsY);
            bool4  insidesY           =
                (math.abs(edgesClosestAsY.x) <= box.halfSize.x) & (math.abs(edgesClosestAsY.y) <= box.halfSize.y) & (math.abs(edgesClosestAsY.z) <= box.halfSize.z);
            signedDistancesSqY = math.select(signedDistancesSqY, -signedDistancesSqY, insidesY);
            signedDistancesSqY = math.select(float.MaxValue, signedDistancesSqY, validsY);

            //z-axis
            simdFloat3 boxPointsZ = new simdFloat3(new float3(box.halfSize.x, box.halfSize.y, -box.halfSize.z),
                                                   new float3(box.halfSize.x, -box.halfSize.y, -box.halfSize.z),
                                                   new float3(-box.halfSize.x, box.halfSize.y, -box.halfSize.z),
                                                   new float3(-box.halfSize.x, -box.halfSize.y, -box.halfSize.z));
            simdFloat3 boxEdgesZ = new simdFloat3(new float3(0f, 0f, 2f * box.halfSize.z));
            QueriesLowLevelUtils.SegmentSegment(simdOsPointA, simdCapsuleEdge, boxPointsZ, boxEdgesZ, out simdFloat3 edgesClosestAsZ, out simdFloat3 edgesClosestBsZ);
            simdFloat3 boxNormalsZ = new simdFloat3(new float3(math.SQRT2 / 2f, math.SQRT2 / 2f, 0f),
                                                    new float3(math.SQRT2 / 2f, -math.SQRT2 / 2f, 0f),
                                                    new float3(-math.SQRT2 / 2f, math.SQRT2 / 2f, 0f),
                                                    new float3(-math.SQRT2 / 2f, -math.SQRT2 / 2f, 0f));
            bool   notParallelZ       = !capsuleEdge.xy.Equals(float2.zero);
            float4 alignmentsZ        = simd.dot(math.normalize(new float3(capsuleEdge.xy, 0f)), boxNormalsZ);
            bool4  validsZ            = (math.abs(alignmentsZ) <= math.SQRT2 / 2f) & notParallelZ;
            float4 signedDistancesSqZ = simd.distancesq(edgesClosestAsZ, edgesClosestBsZ);
            bool4  insidesZ           =
                (math.abs(edgesClosestAsZ.x) <= box.halfSize.x) & (math.abs(edgesClosestAsZ.y) <= box.halfSize.y) & (math.abs(edgesClosestAsZ.z) <= box.halfSize.z);
            signedDistancesSqZ = math.select(signedDistancesSqZ, -signedDistancesSqZ, insidesZ);
            signedDistancesSqZ = math.select(float.MaxValue, signedDistancesSqZ, validsZ);

            //Step 3: Find best result
            float4     bestAxisSignedDistancesSq = signedDistancesSqX;
            simdFloat3 bestAxisPointsOnSegment   = edgesClosestAsX;
            simdFloat3 bestAxisPointsOnBox       = edgesClosestBsX;
            bool4      yWins                     = (signedDistancesSqY < bestAxisSignedDistancesSq) ^ ((bestAxisSignedDistancesSq < 0f) & (signedDistancesSqY < 0f));
            bestAxisSignedDistancesSq            = math.select(bestAxisSignedDistancesSq, signedDistancesSqY, yWins);
            bestAxisPointsOnSegment              = simd.select(bestAxisPointsOnSegment, edgesClosestAsY, yWins);
            bestAxisPointsOnBox                  = simd.select(bestAxisPointsOnBox, edgesClosestBsY, yWins);
            bool4 zWins                          = (signedDistancesSqZ < bestAxisSignedDistancesSq) ^ ((bestAxisSignedDistancesSq < 0f) & (signedDistancesSqZ < 0f));
            bestAxisSignedDistancesSq            = math.select(bestAxisSignedDistancesSq, signedDistancesSqZ, zWins);
            bestAxisPointsOnSegment              = simd.select(bestAxisPointsOnSegment, edgesClosestAsZ, zWins);
            bestAxisPointsOnBox                  = simd.select(bestAxisPointsOnBox, edgesClosestBsZ, zWins);
            bool   bBeatsA                       = (bestAxisSignedDistancesSq.y < bestAxisSignedDistancesSq.x) ^ (math.all(bestAxisSignedDistancesSq.xy < 0f));
            bool   dBeatsC                       = (bestAxisSignedDistancesSq.w < bestAxisSignedDistancesSq.z) ^ (math.all(bestAxisSignedDistancesSq.zw < 0f));
            float  bestAbSignedDistanceSq        = math.select(bestAxisSignedDistancesSq.x, bestAxisSignedDistancesSq.y, bBeatsA);
            float  bestCdSignedDistanceSq        = math.select(bestAxisSignedDistancesSq.z, bestAxisSignedDistancesSq.w, dBeatsC);
            float3 bestAbPointOnSegment          = math.select(bestAxisPointsOnSegment.a, bestAxisPointsOnSegment.b, bBeatsA);
            float3 bestCdPointOnSegment          = math.select(bestAxisPointsOnSegment.c, bestAxisPointsOnSegment.d, dBeatsC);
            float3 bestAbPointOnBox              = math.select(bestAxisPointsOnBox.a, bestAxisPointsOnBox.b, bBeatsA);
            float3 bestCdPointOnBox              = math.select(bestAxisPointsOnBox.c, bestAxisPointsOnBox.d, dBeatsC);
            bool   cdBeatsAb                     = (bestCdSignedDistanceSq < bestAbSignedDistanceSq) ^ ((bestCdSignedDistanceSq < 0f) & (bestAbSignedDistanceSq < 0f));
            float  bestSignedDistanceSq          = math.select(bestAbSignedDistanceSq, bestCdSignedDistanceSq, cdBeatsAb);
            float3 bestPointOnSegment            = math.select(bestAbPointOnSegment, bestCdPointOnSegment, cdBeatsAb);
            float3 bestPointOnBox                = math.select(bestAbPointOnBox, bestCdPointOnBox, cdBeatsAb);
            bool   pointsBeatEdges               = (signedDistanceSq <= bestSignedDistanceSq) ^ ((signedDistanceSq < 0f) & (bestSignedDistanceSq < 0f));
            bestSignedDistanceSq                 = math.select(bestSignedDistanceSq, signedDistanceSq, pointsBeatEdges);
            bestPointOnSegment                   = math.select(bestPointOnSegment, pointsPointOnSegment, pointsBeatEdges);
            bestPointOnBox                       = math.select(bestPointOnBox, pointsPointOnBox, pointsBeatEdges);

            //Step 4: Build result
            float3 boxNormal     = math.normalize(math.select(0f, 1f, bestPointOnBox == box.halfSize) + math.select(0f, -1f, bestPointOnBox == -box.halfSize));
            float3 capsuleNormal = math.normalizesafe(bestPointOnBox - bestPointOnSegment, -boxNormal);
            capsuleNormal        = math.select(capsuleNormal, -capsuleNormal, bestSignedDistanceSq < 0f);
            result               = new ColliderDistanceResultInternal
            {
                hitpointA = bestPointOnBox + box.center,
                hitpointB = bestPointOnSegment + box.center + capsuleNormal * capsule.radius,
                normalA   = boxNormal,
                normalB   = capsuleNormal,
                distance  = math.sign(bestSignedDistanceSq) * math.sqrt(math.abs(bestSignedDistanceSq)) - capsule.radius
            };

            return result.distance <= maxDistance;
        }
    }
}

