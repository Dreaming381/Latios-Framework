using Latios.Calci;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class CapsuleBox
    {
        public static bool AreOverlapping(in BoxCollider box,
                                          in RigidTransform boxTransform,
                                          in CapsuleCollider capsule,
                                          in RigidTransform capsuleTransform)
        {
            return WithinDistance(in box, in boxTransform, in capsule, in capsuleTransform, 0f);
        }

        public static bool WithinDistance(in BoxCollider box,
                                          in RigidTransform boxTransform,
                                          in CapsuleCollider capsule,
                                          in RigidTransform capsuleTransform,
                                          float maxDistance)
        {
            var capInBoxSpaceTransform = math.InverseTransformFast(in boxTransform, in capsuleTransform);
            var capsuleInBoxSpace      = new CapsuleCollider(math.transform(capInBoxSpaceTransform, capsule.pointA),
                                                             math.transform(capInBoxSpaceTransform, capsule.pointB),
                                                             capsule.radius);
            return BoxCapsuleWithin(in box, in capsuleInBoxSpace, maxDistance);
        }

        public static bool DistanceBetween(in BoxCollider box,
                                           in RigidTransform boxTransform,
                                           in CapsuleCollider capsule,
                                           in RigidTransform capsuleTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            var capInBoxSpaceTransform = math.InverseTransformFast(in boxTransform, in capsuleTransform);
            var capsuleInBoxSpace      = new CapsuleCollider(math.transform(capInBoxSpaceTransform, capsule.pointA),
                                                             math.transform(capInBoxSpaceTransform, capsule.pointB),
                                                             capsule.radius);
            bool hit = BoxCapsuleDistance(in box, in capsuleInBoxSpace, maxDistance, out ColliderDistanceResultInternal localResult);
            result   = InternalQueryTypeUtilities.BinAResultToWorld(in localResult, in boxTransform);

            return hit;
        }

        public static bool ColliderCast(in CapsuleCollider capsuleToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in BoxCollider targetBox,
                                        in RigidTransform targetBoxTransform,
                                        out ColliderCastResult result)
        {
            if (AreOverlapping(in targetBox, in targetBoxTransform, in capsuleToCast, in castStart))
            {
                result = default;
                return false;
            }

            var casterInTargetSpace         = math.InverseTransformFast(in targetBoxTransform, castStart);
            var inflatedBoxAabb             = new Aabb(targetBox.center - targetBox.halfSize - capsuleToCast.radius, targetBox.center + targetBox.halfSize + capsuleToCast.radius);
            var castDisplacement            = castEnd - castStart.pos;
            var transformedCastDisplacement = math.InverseRotateFast(targetBoxTransform.rot, castDisplacement);

            var  startA    = math.transform(casterInTargetSpace, capsuleToCast.pointA);
            var  rayA      = new Ray(startA, startA + transformedCastDisplacement);
            bool hitA      = PointRayBox.RaycastAabb(in rayA, in inflatedBoxAabb, out var fractionA);
            var  hitpointA = rayA.start + transformedCastDisplacement * fractionA - targetBox.center;
            // The hit point is somewhere on the inflated box, which means that whatever side of the inflated box it hits,
            // that axis should be beyond the half-size by about a radius. For a flat side hit,
            // the remaining two axes should be less than or equal to the half size.
            // We check for the two remaining axes, because they remain accurate even at 0 radius.
            hitA           &= math.countbits(math.bitmask(new bool4(math.abs(hitpointA) <= targetBox.halfSize, false))) >= 2;
            fractionA       = math.select(2f, fractionA, hitA);
            var  startB     = math.transform(casterInTargetSpace, capsuleToCast.pointB);
            var  rayB       = new Ray(startB, startB + transformedCastDisplacement);
            bool hitB       = PointRayBox.RaycastAabb(in rayB, in inflatedBoxAabb, out var fractionB);
            var  hitpointB  = rayB.start + transformedCastDisplacement * fractionB - targetBox.center;
            hitB           &= math.countbits(math.bitmask(new bool4(math.abs(hitpointB) <= targetBox.halfSize, false))) >= 2;
            fractionB       = math.select(2f, fractionB, hitB);
            float fraction  = math.min(fractionA, fractionB);

            var castCapsuleEdgeCross = math.normalizesafe(math.cross(startB - startA, transformedCastDisplacement));
            if (castCapsuleEdgeCross.Equals(float3.zero))
                mathex.GetDualPerpendicularNormalized(transformedCastDisplacement, out castCapsuleEdgeCross, out _);
            var planeA               = mathex.PlaneFrom(startA, castCapsuleEdgeCross);
            var planeB               = mathex.Flip(planeA);
            planeA.distanceToOrigin -= capsuleToCast.radius;
            planeB.distanceToOrigin -= capsuleToCast.radius;
            var otherCross           = math.normalize(math.cross(transformedCastDisplacement, castCapsuleEdgeCross));
            var planeC               = mathex.PlaneFrom(startA, otherCross);
            if (math.dot(planeC.normal, startB - startA) > 0f)
                planeC               = mathex.Flip(planeC);
            planeC.distanceToOrigin -= capsuleToCast.radius;
            var planeD               = mathex.PlaneFrom(startB, -planeC.normal);
            planeD.distanceToOrigin -= capsuleToCast.radius;

            var ray        = new Ray(0f, -transformedCastDisplacement);
            var castInputs = new CastAgainstEdgeInputs
            {
                box                    = targetBox,
                capsule                = new CapsuleCollider(startA, startB, capsuleToCast.radius),
                ray                    = ray,
                rayDisplacementLength  = math.length(ray.displacement),
                rayDirection           = math.normalize(ray.displacement),
                planeNormals           = new simdFloat3(planeA.normal, planeB.normal, planeC.normal, planeD.normal),
                planeDistancesToOrigin = new float4(planeA.distanceToOrigin, planeB.distanceToOrigin, planeC.distanceToOrigin, planeD.distanceToOrigin),
            };
            CastAgainstEdge(in castInputs, new bool3(true, false, false), new bool3(false, true, true),   ref fraction);
            CastAgainstEdge(in castInputs, new bool3(true, false, false), new bool3(false, true, false),  ref fraction);
            CastAgainstEdge(in castInputs, new bool3(true, false, false), new bool3(false, false, true),  ref fraction);
            CastAgainstEdge(in castInputs, new bool3(true, false, false), new bool3(false, false, false), ref fraction);

            CastAgainstEdge(in castInputs, new bool3(false, true, false), new bool3(true, false, true),   ref fraction);
            CastAgainstEdge(in castInputs, new bool3(false, true, false), new bool3(true, false, false),  ref fraction);
            CastAgainstEdge(in castInputs, new bool3(false, true, false), new bool3(false, false, true),  ref fraction);
            CastAgainstEdge(in castInputs, new bool3(false, true, false), new bool3(false, false, false), ref fraction);

            CastAgainstEdge(in castInputs, new bool3(false, false, true), new bool3(true, true, false),   ref fraction);
            CastAgainstEdge(in castInputs, new bool3(false, false, true), new bool3(true, false, false),  ref fraction);
            CastAgainstEdge(in castInputs, new bool3(false, false, true), new bool3(false, true, false),  ref fraction);
            CastAgainstEdge(in castInputs, new bool3(false, false, true), new bool3(false, false, false), ref fraction);

            bool hit = fraction < 2f;
            if (hit)
            {
                var hitTransform = castStart;

                hitTransform.pos = castStart.pos + castDisplacement * fraction;
                DistanceBetween(in targetBox, in targetBoxTransform, in capsuleToCast, in hitTransform, 1f, out var distanceResult);
                result = new ColliderCastResult
                {
                    hitpoint                 = distanceResult.hitpointB,
                    normalOnCaster           = distanceResult.normalB,
                    normalOnTarget           = distanceResult.normalA,
                    subColliderIndexOnCaster = distanceResult.subColliderIndexB,
                    subColliderIndexOnTarget = distanceResult.subColliderIndexA,
                    distance                 = math.length(castDisplacement) * fraction
                };
                return true;
            }
            result = default;
            return false;
        }

        public static bool ColliderCast(in BoxCollider boxToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in CapsuleCollider targetCapsule,
                                        in RigidTransform targetCapsuleTransform,
                                        out ColliderCastResult result)
        {
            if (AreOverlapping(in boxToCast, in castStart, in targetCapsule, in targetCapsuleTransform))
            {
                result = default;
                return false;
            }

            var targetInCasterSpace         = math.InverseTransformFast(castStart, targetCapsuleTransform);
            var inflatedBoxAabb             = new Aabb(boxToCast.center - boxToCast.halfSize - targetCapsule.radius, boxToCast.center + boxToCast.halfSize + targetCapsule.radius);
            var castDisplacement            = castEnd - castStart.pos;
            var transformedCastDisplacement = math.InverseRotateFast(castStart.rot, castDisplacement);

            var  targetA    = math.transform(targetInCasterSpace, targetCapsule.pointA);
            var  rayA       = new Ray(targetA, targetA - transformedCastDisplacement);
            bool hitA       = PointRayBox.RaycastAabb(in rayA, in inflatedBoxAabb, out var fractionA);
            var  hitpointA  = rayA.start - transformedCastDisplacement * fractionA - boxToCast.center;
            hitA           &= math.countbits(math.bitmask(new bool4(math.abs(hitpointA) <= boxToCast.halfSize, false))) >= 2;
            fractionA       = math.select(2f, fractionA, hitA);
            var  targetB    = math.transform(targetInCasterSpace, targetCapsule.pointB);
            var  rayB       = new Ray(targetB, targetB - transformedCastDisplacement);
            bool hitB       = PointRayBox.RaycastAabb(in rayB, in inflatedBoxAabb, out var fractionB);
            var  hitpointB  = rayB.start - transformedCastDisplacement * fractionB - boxToCast.center;
            hitB           &= math.countbits(math.bitmask(new bool4(math.abs(hitpointB) <= boxToCast.halfSize, false))) >= 2;
            fractionB       = math.select(2f, fractionB, hitB);
            float fraction  = math.min(fractionA, fractionB);

            var castCapsuleEdgeCross = math.normalizesafe(math.cross(targetB - targetA, transformedCastDisplacement));
            if (castCapsuleEdgeCross.Equals(float3.zero))
                mathex.GetDualPerpendicularNormalized(transformedCastDisplacement, out castCapsuleEdgeCross, out _);
            var planeA               = mathex.PlaneFrom(targetA, castCapsuleEdgeCross);
            var planeB               = mathex.Flip(planeA);
            planeA.distanceToOrigin -= targetCapsule.radius;
            planeB.distanceToOrigin -= targetCapsule.radius;
            var otherCross           = math.normalize(math.cross(transformedCastDisplacement, castCapsuleEdgeCross));
            var planeC               = mathex.PlaneFrom(targetA, otherCross);
            if (math.dot(planeC.normal, targetB - targetA) > 0f)
                planeC               = mathex.Flip(planeC);
            planeC.distanceToOrigin -= targetCapsule.radius;
            var planeD               = mathex.PlaneFrom(targetB, -planeC.normal);
            planeD.distanceToOrigin -= targetCapsule.radius;

            var ray        = new Ray(0f, transformedCastDisplacement);
            var castInputs = new CastAgainstEdgeInputs
            {
                box                    = boxToCast,
                capsule                = new CapsuleCollider(targetA, targetB, targetCapsule.radius),
                ray                    = ray,
                rayDisplacementLength  = math.length(ray.displacement),
                rayDirection           = math.normalize(ray.displacement),
                planeNormals           = new simdFloat3(planeA.normal, planeB.normal, planeC.normal, planeD.normal),
                planeDistancesToOrigin = new float4(planeA.distanceToOrigin, planeB.distanceToOrigin, planeC.distanceToOrigin, planeD.distanceToOrigin),
            };
            CastAgainstEdge(in castInputs, new bool3(true, false, false), new bool3(false, true, true),   ref fraction);
            CastAgainstEdge(in castInputs, new bool3(true, false, false), new bool3(false, true, false),  ref fraction);
            CastAgainstEdge(in castInputs, new bool3(true, false, false), new bool3(false, false, true),  ref fraction);
            CastAgainstEdge(in castInputs, new bool3(true, false, false), new bool3(false, false, false), ref fraction);

            CastAgainstEdge(in castInputs, new bool3(false, true, false), new bool3(true, false, true),   ref fraction);
            CastAgainstEdge(in castInputs, new bool3(false, true, false), new bool3(true, false, false),  ref fraction);
            CastAgainstEdge(in castInputs, new bool3(false, true, false), new bool3(false, false, true),  ref fraction);
            CastAgainstEdge(in castInputs, new bool3(false, true, false), new bool3(false, false, false), ref fraction);

            CastAgainstEdge(in castInputs, new bool3(false, false, true), new bool3(true, true, false),   ref fraction);
            CastAgainstEdge(in castInputs, new bool3(false, false, true), new bool3(true, false, false),  ref fraction);
            CastAgainstEdge(in castInputs, new bool3(false, false, true), new bool3(false, true, false),  ref fraction);
            CastAgainstEdge(in castInputs, new bool3(false, false, true), new bool3(false, false, false), ref fraction);

            bool hit = fraction < 2f;
            if (hit)
            {
                var hitTransform = castStart;
                hitTransform.pos = castStart.pos + castDisplacement * fraction;
                DistanceBetween(in boxToCast, in hitTransform, in targetCapsule, in targetCapsuleTransform, 1f, out var distanceResult);
                result = new ColliderCastResult
                {
                    hitpoint                 = distanceResult.hitpointA,
                    normalOnCaster           = distanceResult.normalA,
                    normalOnTarget           = distanceResult.normalB,
                    subColliderIndexOnCaster = distanceResult.subColliderIndexA,
                    subColliderIndexOnTarget = distanceResult.subColliderIndexB,
                    distance                 = math.length(castDisplacement) * fraction
                };
                return true;
            }
            result = default;
            return false;
        }

        public static UnitySim.ContactsBetweenResult UnityContactsBetween(in BoxCollider box,
                                                                          in RigidTransform boxTransform,
                                                                          in CapsuleCollider capsule,
                                                                          in RigidTransform capsuleTransform,
                                                                          in ColliderDistanceResult distanceResult)
        {
            UnitySim.ContactsBetweenResult result = default;
            result.contactNormal                  = distanceResult.normalB;

            var boxLocalContactNormal = math.InverseRotateFast(boxTransform.rot, -distanceResult.normalB);
            PointRayBox.BestFacePlanesAndVertices(in box, boxLocalContactNormal, out var edgePlaneNormals, out var edgePlaneDistances, out var plane, out _);

            var  bInATransform     = math.mul(math.inverse(boxTransform), capsuleTransform);
            var  rayStart          = math.transform(bInATransform, capsule.pointA);
            var  rayDisplacement   = math.transform(bInATransform, capsule.pointB) - rayStart;
            var  rayRelativeStarts = simd.dot(rayStart, edgePlaneNormals) - edgePlaneDistances;
            var  relativeDiffs     = simd.dot(rayDisplacement, edgePlaneNormals);
            var  rayRelativeEnds   = rayRelativeStarts + relativeDiffs;
            var  rayFractions      = math.select(-rayRelativeStarts / relativeDiffs, float4.zero, relativeDiffs == float4.zero);
            var  startsInside      = rayRelativeStarts <= 0f;
            var  endsInside        = rayRelativeEnds <= 0f;
            var  projectsOnFace    = startsInside | endsInside;
            var  enterFractions    = math.select(float4.zero, rayFractions, !startsInside & rayFractions > float4.zero);
            var  exitFractions     = math.select(1f, rayFractions, !endsInside & rayFractions < 1f);
            var  fractionA         = math.cmax(enterFractions);
            var  fractionB         = math.cmin(exitFractions);
            bool needsClosestPoint = true;
            if (math.all(projectsOnFace) && fractionA < fractionB)
            {
                // Add the two contacts from the possibly clipped segment
                var distanceScalarAlongContactNormal = math.rcp(math.dot(boxLocalContactNormal, plane.normal));
                var clippedSegmentA                  = rayStart + fractionA * rayDisplacement;
                var clippedSegmentB                  = rayStart + fractionB * rayDisplacement;
                var aDistance                        = mathex.SignedDistance(plane, clippedSegmentA) * distanceScalarAlongContactNormal;
                var bDistance                        = mathex.SignedDistance(plane, clippedSegmentB) * distanceScalarAlongContactNormal;

                // Unity Physics puts the contact points inside the capsule if the contact is close to the segment endpoint.
                // Our version corrects this by finding the surface point along the contact vector.
                var contactOnBoxPlusRadiusA = clippedSegmentA + (aDistance + capsule.radius) * distanceResult.normalB;
                var contactOnBoxPlusRadiusB = clippedSegmentB + (bDistance + capsule.radius) * distanceResult.normalB;
                var capsuleInBox            = new CapsuleCollider(rayStart, rayStart + rayDisplacement, capsule.radius);
                PointRayCapsule.RaycastCapsule(new Ray(contactOnBoxPlusRadiusA, clippedSegmentA), in capsuleInBox, out var contactFractionA, out _);
                PointRayCapsule.RaycastCapsule(new Ray(contactOnBoxPlusRadiusB, clippedSegmentB), in capsuleInBox, out var contactFractionB, out _);
                clippedSegmentA = math.lerp(contactOnBoxPlusRadiusA, clippedSegmentA, contactFractionA);
                clippedSegmentB = math.lerp(contactOnBoxPlusRadiusB, clippedSegmentB, contactFractionB);

                result.Add(math.transform(boxTransform, clippedSegmentA), math.distance(clippedSegmentA, contactOnBoxPlusRadiusA));
                result.Add(math.transform(boxTransform, clippedSegmentB), math.distance(clippedSegmentB, contactOnBoxPlusRadiusB));
                needsClosestPoint = math.min(aDistance, bDistance) > distanceResult.distance + 1e-4f;  // Magic constant comes from Unity Physics
            }

            if (needsClosestPoint)
                result.Add(distanceResult.hitpointB, distanceResult.distance);
            return result;
        }

        private static bool BoxCapsuleWithin(in BoxCollider box, in CapsuleCollider capsule, float maxDistance)
        {
            float3 osPointA    = capsule.pointA - box.center;  // os = origin space
            float3 osPointB    = capsule.pointB - box.center;
            float3 capsuleEdge = osPointB - osPointA;

            // Step 1: SAT the capsule axis with the box to determine penetration. Knowing this saves a bunch of work.
            float3 pointDistancesToBoxMin = math.max(osPointA, osPointB) + box.halfSize;
            float3 pointDistancesToBoxMax = box.halfSize - math.min(osPointA, osPointB);
            float3 bestPointDistances     = math.min(pointDistancesToBoxMin, pointDistancesToBoxMax);  // positive is penetrating

            simdFloat3 crosses = simd.crossRightUpForwardRight(capsuleEdge);
            // The capsule edge is perpendicular to the crosses, so pointA and pointB have equal projections. We use pointA.
            float4 capsuleSupportProjections = simd.dot(osPointA, crosses);
            // The box support point has the same signs as the direction vector, which cancels during projection, so we can just use absolute values.
            float4 boxSupportProjections = simd.dot(simd.abs(crosses), box.halfSize);
            // Similarly, the box is symmetric, so whether the capsule support is on the positive or negative doesn't matter. We just want the largest projection magnitude.
            float4 edgeScaledDistances   = boxSupportProjections - math.abs(capsuleSupportProjections);  // positive is penetrating
            bool   segmentIsIntersecting = math.all(new bool4(bestPointDistances >= 0f, true) & (edgeScaledDistances >= 0f));

            // Step 2: Find closest points and distance.
            float bestSignedDistanceSq;
            if (segmentIsIntersecting)
            {
                // The internal capsule segment is penetrating the box.
                float  bestPointDepth          = math.cmin(bestPointDistances);
                float4 crossEdgeLengths        = simd.length(crosses);
                bool4  capsuleIsParallelToFace = (capsuleEdge.yzxy == 0f) | (capsuleEdge.zxyz == 0f);
                float4 edgeDepths              = math.select(edgeScaledDistances / crossEdgeLengths, float.MaxValue, capsuleIsParallelToFace);
                float  bestEdgeDepth           = math.cmin(edgeDepths);
                if (bestPointDepth <= bestEdgeDepth)
                {
                    // The hitpoint is either on the face or across the face if the capsule is parallel.
                    bestSignedDistanceSq = -bestPointDepth * bestPointDepth;
                }
                else
                {
                    // The hitpoint is between the segment and an edge.
                    bestSignedDistanceSq = -bestEdgeDepth * bestEdgeDepth;
                }
            }
            else
            {
                // No core penetration.
                // Points vs Box
                float3 pointOnBoxA      = math.clamp(osPointA, -box.halfSize, box.halfSize);
                float3 pointOnBoxB      = math.clamp(osPointB, -box.halfSize, box.halfSize);
                float  distanceSqA      = math.distancesq(osPointA, pointOnBoxA);
                float  distanceSqB      = math.distancesq(osPointB, pointOnBoxB);
                bool   useB             = distanceSqB < distanceSqA;
                float  pointsDistanceSq = math.select(distanceSqA, distanceSqB, useB);

                // Edge vs Edges
                // x-axes
                simdFloat3 boxPointsAx = new simdFloat3(new float3(-box.halfSize.x, box.halfSize.y, box.halfSize.z),
                                                        new float3(-box.halfSize.x, box.halfSize.y, -box.halfSize.z),
                                                        new float3(-box.halfSize.x, -box.halfSize.y, box.halfSize.z),
                                                        new float3(-box.halfSize.x, -box.halfSize.y, -box.halfSize.z));
                Segment4Edges(osPointA, osPointB, boxPointsAx, box.halfSize.x, new bool3(true, false, false), out simdFloat3 edgesClosestAsX, out simdFloat3 edgesClosestBsX);
                // A capsule edge parallel to the box edges finds its distance from an endpoint or a perpendicular box edge instead.
                bool   notParallelX = !capsuleEdge.yz.Equals(float2.zero);
                float4 distancesSqX = math.select(float.MaxValue, simd.distancesq(edgesClosestAsX, edgesClosestBsX), notParallelX);

                // y-axis
                simdFloat3 boxPointsAy = new simdFloat3(new float3(box.halfSize.x, -box.halfSize.y, box.halfSize.z),
                                                        new float3(box.halfSize.x, -box.halfSize.y, -box.halfSize.z),
                                                        new float3(-box.halfSize.x, -box.halfSize.y, box.halfSize.z),
                                                        new float3(-box.halfSize.x, -box.halfSize.y, -box.halfSize.z));
                Segment4Edges(osPointA, osPointB, boxPointsAy, box.halfSize.y, new bool3(false, true, false), out simdFloat3 edgesClosestAsY, out simdFloat3 edgesClosestBsY);
                bool   notParallelY = !capsuleEdge.xz.Equals(float2.zero);
                float4 distancesSqY = math.select(float.MaxValue, simd.distancesq(edgesClosestAsY, edgesClosestBsY), notParallelY);

                // z-axis
                simdFloat3 boxPointsAz = new simdFloat3(new float3(box.halfSize.x, box.halfSize.y, -box.halfSize.z),
                                                        new float3(box.halfSize.x, -box.halfSize.y, -box.halfSize.z),
                                                        new float3(-box.halfSize.x, box.halfSize.y, -box.halfSize.z),
                                                        new float3(-box.halfSize.x, -box.halfSize.y, -box.halfSize.z));
                Segment4Edges(osPointA, osPointB, boxPointsAz, box.halfSize.z, new bool3(false, false, true), out simdFloat3 edgesClosestAsZ, out simdFloat3 edgesClosestBsZ);
                bool   notParallelZ = !capsuleEdge.xy.Equals(float2.zero);
                float4 distancesSqZ = math.select(float.MaxValue, simd.distancesq(edgesClosestAsZ, edgesClosestBsZ), notParallelZ);

                // Reduce
                float4 bestAxisDistancesSq = distancesSqX;
                bool4  yWins               = distancesSqY < bestAxisDistancesSq;
                bestAxisDistancesSq        = math.select(bestAxisDistancesSq, distancesSqY, yWins);
                bool4 zWins                = distancesSqZ < bestAxisDistancesSq;
                bestAxisDistancesSq        = math.select(bestAxisDistancesSq, distancesSqZ, zWins);
                bool  bBeatsA              = bestAxisDistancesSq.y < bestAxisDistancesSq.x;
                bool  dBeatsC              = bestAxisDistancesSq.w < bestAxisDistancesSq.z;
                float bestAbDistanceSq     = math.select(bestAxisDistancesSq.x, bestAxisDistancesSq.y, bBeatsA);
                float bestCdDistanceSq     = math.select(bestAxisDistancesSq.z, bestAxisDistancesSq.w, dBeatsC);
                bool  cdBeatsAb            = bestCdDistanceSq < bestAbDistanceSq;
                float edgesDistanceSq      = math.select(bestAbDistanceSq, bestCdDistanceSq, cdBeatsAb);
                bool  pointsBeatEdges      = pointsDistanceSq <= edgesDistanceSq;
                bestSignedDistanceSq       = math.select(edgesDistanceSq, pointsDistanceSq, pointsBeatEdges);
            }

            // Step 3: Create the result.
            float resultDistance = math.sign(bestSignedDistanceSq) * math.sqrt(math.abs(bestSignedDistanceSq)) - capsule.radius;

            return resultDistance <= maxDistance;
        }

        private static bool BoxCapsuleDistance(in BoxCollider box, in CapsuleCollider capsule, float maxDistance, out ColliderDistanceResultInternal result)
        {
            float3 osPointA    = capsule.pointA - box.center;  // os = origin space
            float3 osPointB    = capsule.pointB - box.center;
            float3 capsuleEdge = osPointB - osPointA;

            // Step 1: SAT the capsule axis with the box to determine penetration. Knowing this saves a bunch of work.
            float3 pointDistancesToBoxMin = math.max(osPointA, osPointB) + box.halfSize;
            float3 pointDistancesToBoxMax = box.halfSize - math.min(osPointA, osPointB);
            float3 bestPointDistances     = math.min(pointDistancesToBoxMin, pointDistancesToBoxMax);  // positive is penetrating

            simdFloat3 crosses = simd.crossRightUpForwardRight(capsuleEdge);
            // The capsule edge is perpendicular to the crosses, so pointA and pointB have equal projections. We use pointA.
            float4 capsuleSupportProjections = simd.dot(osPointA, crosses);
            // The box support point has the same signs as the direction vector, which cancels during projection, so we can just use absolute values.
            float4 boxSupportProjections = simd.dot(simd.abs(crosses), box.halfSize);
            // Similarly, the box is symmetric, so whether the capsule support is on the positive or negative doesn't matter. We just want the largest projection magnitude.
            float4 edgeScaledDistances   = boxSupportProjections - math.abs(capsuleSupportProjections);  // positive is penetrating
            bool   segmentIsIntersecting = math.all(new bool4(bestPointDistances >= 0f, true) & (edgeScaledDistances >= 0f));

            // Step 2: Find closest points and distance.
            float3 closestPointOnSegment;
            float3 closestPointOnBox;
            float  bestSignedDistanceSq;
            float3 segmentPenetrationNormal = default;
            float3 segmentToBoxPrecise;
            bool   segmentIntersectingOverhangsFaceEdge = false;
            if (segmentIsIntersecting)
            {
                // The internal capsule segment is penetrating the box.
                float  bestPointDepth               = math.cmin(bestPointDistances);
                float4 crossEdgeLengths             = simd.length(crosses);
                bool4  capsuleIsParallelToFace      = (capsuleEdge.yzxy == 0f) | (capsuleEdge.zxyz == 0f);
                float4 edgeDepths                   = math.select(edgeScaledDistances / crossEdgeLengths, float.MaxValue, capsuleIsParallelToFace);
                float  bestEdgeDepth                = math.cmin(edgeDepths);
                float3 separatingAxisTowardsCapsule = default;
                closestPointOnSegment               = default;
                closestPointOnBox                   = default;
                bestSignedDistanceSq                = default;
                bool faceIsClosest                  = bestPointDepth <= bestEdgeDepth;
                if (!faceIsClosest)
                {
                    // The hitpoint is between the segment and an edge.
                    int    edgeAxisYz         = math.select(2, 1, edgeDepths.y == bestEdgeDepth);
                    int    edgeAxis           = math.select(edgeAxisYz, 0, edgeDepths.x == bestEdgeDepth);
                    float3 crossTowardCapsule = math.chgsign(crosses[edgeAxis], capsuleSupportProjections[edgeAxis]);
                    float3 boxEdgeStart       = math.chgsign(box.halfSize, crossTowardCapsule);
                    float3 boxEdgeEnd         = boxEdgeStart;
                    boxEdgeStart[edgeAxis]    = -box.halfSize[edgeAxis];
                    boxEdgeEnd[edgeAxis]      = box.halfSize[edgeAxis];
                    CapsuleCapsule.SegmentSegment(osPointA, osPointB, boxEdgeStart, boxEdgeEnd, out closestPointOnSegment, out closestPointOnBox, out _, out _);
                    bestSignedDistanceSq         = -bestEdgeDepth * bestEdgeDepth;
                    separatingAxisTowardsCapsule = crossTowardCapsule / crossEdgeLengths[edgeAxis];
                    // If the cross direction is nearly axis-aligned, we might have picked the wrong edge. Fall back to the face algorithm if this happens, since that handles overhangs.
                    faceIsClosest = math.distance(closestPointOnSegment, closestPointOnBox) > bestPointDepth;
                }

                if (faceIsClosest)
                {
                    // The hitpoint is either on the face or across the face if the capsule is parallel.
                    bool3 bestAxisMask = bestPointDepth == bestPointDistances;
                    // For ties, prefer y, then z.
                    bestAxisMask.xz       &= !bestAxisMask.y;
                    bestAxisMask.x        &= !bestAxisMask.z;
                    bool3 useMin           = bestPointDistances == pointDistancesToBoxMin;
                    bool3 aIsGreater       = osPointA > osPointB;
                    bool  useB             = math.any((useMin ^ aIsGreater) & bestAxisMask);
                    closestPointOnSegment  = math.select(osPointA, osPointB, useB);
                    int overhangEdgeAxis   = 0;
                    if (math.any((osPointA == osPointB) & bestAxisMask))
                    {
                        // The capsule is parallel to the face along our axis.
                        bool3 aOverhangs = (math.abs(osPointA) > box.halfSize) & !bestAxisMask;
                        bool3 bOverhangs = (math.abs(osPointB) > box.halfSize) & !bestAxisMask;
                        if (!math.any(aOverhangs))
                            closestPointOnSegment = osPointA;
                        else if (!math.any(bOverhangs))
                            closestPointOnSegment = osPointB;
                        else
                        {
                            // Find where the segment crosses the edge.
                            float3 octantBoundary      = math.chgsign(box.halfSize, osPointA);
                            float3 aToFace2D           = math.select(float3.zero, octantBoundary - osPointA, aOverhangs);
                            float3 aToFaceFractions    = aToFace2D / math.select(1f, capsuleEdge, aOverhangs);
                            float  bestAToFaceFraction = math.cmax(aToFaceFractions);
                            bool3  isOverhangAxes      = aOverhangs & (aToFaceFractions == bestAToFaceFraction);
                            closestPointOnSegment      = math.select(osPointA + capsuleEdge * bestAToFaceFraction, octantBoundary, isOverhangAxes);
                        }
                    }
                    else
                    {
                        bool3 closestOverhangs = (math.abs(closestPointOnSegment) > box.halfSize) & !bestAxisMask;
                        if (math.any(closestOverhangs))
                        {
                            // We ended up in this face algorithm, but the closest endpoint does not project onto the box face. So we look for where the segment crosses the edge.
                            var    capsuleEdgeFromClosest    = math.select(capsuleEdge, -capsuleEdge, useB);
                            float3 octantBoundary            = math.chgsign(box.halfSize, closestPointOnSegment);
                            float3 closestToFace2D           = math.select(float3.zero, octantBoundary - closestPointOnSegment, closestOverhangs);
                            float3 closestToFaceFractions    = closestToFace2D / math.select(1f, capsuleEdgeFromClosest, closestOverhangs);
                            float  bestClosestToFaceFraction = math.saturate(math.cmax(closestToFaceFractions));
                            bool3  isOverhangAxes            = closestOverhangs & (closestToFaceFractions == bestClosestToFaceFraction);
                            float3 segmentEdgeCrossingPoint  = math.select(closestPointOnSegment + capsuleEdgeFromClosest * bestClosestToFaceFraction,
                                                                           octantBoundary,
                                                                           isOverhangAxes);
                            // However, this new point might not be any better. So make sure it is first.
                            float3 boxSignedExtents          = math.select(box.halfSize, -box.halfSize, useMin);
                            float3 boxPointClosestToCrossing =
                                math.clamp(math.select(segmentEdgeCrossingPoint, boxSignedExtents, bestAxisMask), -box.halfSize, box.halfSize);
                            float3 boxPointClosestToCapsuleEndpoint = math.clamp(math.select(closestPointOnSegment, boxSignedExtents, bestAxisMask), -box.halfSize, box.halfSize);
                            bool   crossingIsBetter                 = math.distancesq(segmentEdgeCrossingPoint, boxPointClosestToCrossing) < math.distancesq(closestPointOnSegment,
                                                                                                                                                           boxPointClosestToCapsuleEndpoint);
                            closestPointOnSegment                = math.select(closestPointOnSegment, segmentEdgeCrossingPoint, crossingIsBetter);
                            segmentIntersectingOverhangsFaceEdge = crossingIsBetter && math.countbits(math.bitmask(new bool4(isOverhangAxes, false))) == 1;
                            overhangEdgeAxis                     = math.tzcnt(math.bitmask(new bool4(!(isOverhangAxes | bestAxisMask), false)));
                        }
                    }
                    closestPointOnBox            = math.select(closestPointOnSegment, math.select(box.halfSize, -box.halfSize, useMin), bestAxisMask);
                    closestPointOnBox            = math.clamp(closestPointOnBox, -box.halfSize, box.halfSize);
                    float faceDepth              = math.csum(math.select(0f, math.abs(closestPointOnBox - closestPointOnSegment), bestAxisMask));
                    bestSignedDistanceSq         = -faceDepth * faceDepth;
                    separatingAxisTowardsCapsule = math.select(0f, math.select(1f, -1f, useMin), bestAxisMask);
                    if (segmentIntersectingOverhangsFaceEdge)
                    {
                        separatingAxisTowardsCapsule = math.chgsign(crosses[overhangEdgeAxis], capsuleSupportProjections[overhangEdgeAxis]) / crossEdgeLengths[overhangEdgeAxis];
                    }
                }
                segmentPenetrationNormal = -separatingAxisTowardsCapsule;
                segmentToBoxPrecise      = closestPointOnBox - closestPointOnSegment;
            }
            else
            {
                // No core penetration.
                // Points vs Box
                float3 pointOnBoxA          = math.clamp(osPointA, -box.halfSize, box.halfSize);
                float3 pointOnBoxB          = math.clamp(osPointB, -box.halfSize, box.halfSize);
                float  distanceSqA          = math.distancesq(osPointA, pointOnBoxA);
                float  distanceSqB          = math.distancesq(osPointB, pointOnBoxB);
                bool   useB                 = distanceSqB < distanceSqA;
                float3 pointsPointOnSegment = math.select(osPointA, osPointB, useB);
                float3 pointsPointOnBox     = math.select(pointOnBoxA, pointOnBoxB, useB);
                float  pointsDistanceSq     = math.select(distanceSqA, distanceSqB, useB);
                float3 pointsPrecise        = pointsPointOnBox - pointsPointOnSegment;

                // Edge vs Edges
                // x-axes
                simdFloat3 boxPointsAx = new simdFloat3(new float3(-box.halfSize.x, box.halfSize.y, box.halfSize.z),
                                                        new float3(-box.halfSize.x, box.halfSize.y, -box.halfSize.z),
                                                        new float3(-box.halfSize.x, -box.halfSize.y, box.halfSize.z),
                                                        new float3(-box.halfSize.x, -box.halfSize.y, -box.halfSize.z));
                SegmentOutside4EdgesExcludeSegmentEndpoints(osPointA, osPointB, boxPointsAx, box.halfSize.x, new bool3(true, false, false),
                                                            out float4 fractionsX, out float4 distancesSqX, out simdFloat3 closestOnEdgesX, out simdFloat3 segmentToEdgesPreciseX);

                // y-axis
                simdFloat3 boxPointsAy = new simdFloat3(new float3(box.halfSize.x, -box.halfSize.y, box.halfSize.z),
                                                        new float3(box.halfSize.x, -box.halfSize.y, -box.halfSize.z),
                                                        new float3(-box.halfSize.x, -box.halfSize.y, box.halfSize.z),
                                                        new float3(-box.halfSize.x, -box.halfSize.y, -box.halfSize.z));
                SegmentOutside4EdgesExcludeSegmentEndpoints(osPointA, osPointB, boxPointsAy, box.halfSize.y, new bool3(false, true, false),
                                                            out float4 fractionsY, out float4 distancesSqY, out simdFloat3 closestOnEdgesY, out simdFloat3 segmentToEdgesPreciseY);

                // z-axis
                simdFloat3 boxPointsAz = new simdFloat3(new float3(box.halfSize.x, box.halfSize.y, -box.halfSize.z),
                                                        new float3(box.halfSize.x, -box.halfSize.y, -box.halfSize.z),
                                                        new float3(-box.halfSize.x, box.halfSize.y, -box.halfSize.z),
                                                        new float3(-box.halfSize.x, -box.halfSize.y, -box.halfSize.z));
                SegmentOutside4EdgesExcludeSegmentEndpoints(osPointA, osPointB, boxPointsAz, box.halfSize.z, new bool3(false, false, true),
                                                            out float4 fractionsZ, out float4 distancesSqZ, out simdFloat3 closestOnEdgesZ, out simdFloat3 segmentToEdgesPreciseZ);

                // Reduce
                float4     bestAxisDistancesSq = distancesSqX;
                float4     bestAxisFractions   = fractionsX;
                simdFloat3 bestAxisPointsOnBox = closestOnEdgesX;
                simdFloat3 bestAxisPrecise     = segmentToEdgesPreciseX;
                bool4      yWins               = distancesSqY < bestAxisDistancesSq;
                bestAxisDistancesSq            = math.select(bestAxisDistancesSq, distancesSqY, yWins);
                bestAxisFractions              = math.select(bestAxisFractions, fractionsY, yWins);
                bestAxisPointsOnBox            = simd.select(bestAxisPointsOnBox, closestOnEdgesY, yWins);
                bestAxisPrecise                = simd.select(bestAxisPrecise, segmentToEdgesPreciseY, yWins);
                bool4 zWins                    = distancesSqZ < bestAxisDistancesSq;
                bestAxisDistancesSq            = math.select(bestAxisDistancesSq, distancesSqZ, zWins);
                bestAxisFractions              = math.select(bestAxisFractions, fractionsZ, zWins);
                bestAxisPointsOnBox            = simd.select(bestAxisPointsOnBox, closestOnEdgesZ, zWins);
                bestAxisPrecise                = simd.select(bestAxisPrecise, segmentToEdgesPreciseZ, zWins);

                bool   bBeatsA          = bestAxisDistancesSq.y < bestAxisDistancesSq.x;
                bool   dBeatsC          = bestAxisDistancesSq.w < bestAxisDistancesSq.z;
                float  bestAbDistanceSq = math.select(bestAxisDistancesSq.x, bestAxisDistancesSq.y, bBeatsA);
                float  bestCdDistanceSq = math.select(bestAxisDistancesSq.z, bestAxisDistancesSq.w, dBeatsC);
                float  bestAbFraction   = math.select(bestAxisFractions.x, bestAxisFractions.y, bBeatsA);
                float  bestCdFraction   = math.select(bestAxisFractions.z, bestAxisFractions.w, dBeatsC);
                float3 bestAbPointOnBox = math.select(bestAxisPointsOnBox.a, bestAxisPointsOnBox.b, bBeatsA);
                float3 bestCdPointOnBox = math.select(bestAxisPointsOnBox.c, bestAxisPointsOnBox.d, dBeatsC);
                float3 bestAbPrecise    = math.select(bestAxisPrecise.a, bestAxisPrecise.b, bBeatsA);
                float3 bestCdPrecise    = math.select(bestAxisPrecise.c, bestAxisPrecise.d, dBeatsC);
                bool   cdBeatsAb        = bestCdDistanceSq < bestAbDistanceSq;
                float  edgesDistanceSq  = math.select(bestAbDistanceSq, bestCdDistanceSq, cdBeatsAb);
                float  edgesFraction    = math.select(bestAbFraction, bestCdFraction, cdBeatsAb);
                float3 edgesPointOnBox  = math.select(bestAbPointOnBox, bestCdPointOnBox, cdBeatsAb);
                float3 edgesPrecise     = math.select(bestAbPrecise, bestCdPrecise, cdBeatsAb);
                bool   pointsBeatEdges  = pointsDistanceSq <= edgesDistanceSq;
                bestSignedDistanceSq    = math.select(edgesDistanceSq, pointsDistanceSq, pointsBeatEdges);
                closestPointOnSegment   = math.select(osPointA + capsuleEdge * edgesFraction, pointsPointOnSegment, pointsBeatEdges);
                closestPointOnBox       = math.select(edgesPointOnBox, pointsPointOnBox, pointsBeatEdges);
                segmentToBoxPrecise     = math.select(edgesPrecise, pointsPrecise, pointsBeatEdges);
            }

            // Step 3: Create the result.
            float3 boxNormal         = math.normalize(math.select(0f, 1f, closestPointOnBox == box.halfSize) + math.select(0f, -1f, closestPointOnBox == -box.halfSize));
            float3 capsuleNormal     = math.normalizesafe(segmentToBoxPrecise);
            bool   capsuleDegenerate = capsuleNormal.Equals(float3.zero);
            capsuleNormal            = math.select(capsuleNormal, -capsuleNormal, bestSignedDistanceSq < 0f);
            capsuleNormal            = math.select(capsuleNormal, segmentPenetrationNormal, segmentIsIntersecting && (!capsuleDegenerate || segmentIntersectingOverhangsFaceEdge));
            result                   = new ColliderDistanceResultInternal
            {
                hitpointA    = closestPointOnBox + box.center,
                hitpointB    = closestPointOnSegment + box.center + capsuleNormal * capsule.radius,
                normalA      = boxNormal,
                normalB      = capsuleNormal,
                distance     = math.sign(bestSignedDistanceSq) * math.sqrt(math.abs(bestSignedDistanceSq)) - capsule.radius,
                featureCodeA = PointRayBox.FeatureCodeFromBoxNormal(boxNormal),
                featureCodeB = PointRayCapsule.FeatureCodeFromSegmentHitpoint(closestPointOnSegment, osPointA, osPointB)
            };

            if (Hint.Likely(!capsuleDegenerate || segmentIntersectingOverhangsFaceEdge))
                return result.distance <= maxDistance;

            if (capsuleEdge.Equals(float3.zero))
            {
                result.hitpointB -= boxNormal * capsule.radius;
                result.normalB    = -boxNormal;
                return result.distance <= maxDistance;
            }

            var edgeNormalized = math.normalize(capsuleEdge);
            edgeNormalized     = math.select(edgeNormalized, -edgeNormalized, result.featureCodeB == 1);
            if (result.featureCodeB < 2 && math.dot(result.normalA, edgeNormalized) >= 0f)
            {
                result.hitpointB -= boxNormal * capsule.radius;
                result.normalB    = -boxNormal;
                return result.distance <= maxDistance;
            }

            {
                // The segment touches the box surface. The normal is the SAT axis.
                float  bestPointDepth          = math.cmin(bestPointDistances);
                float4 crossEdgeLengths        = simd.length(crosses);
                bool4  capsuleIsParallelToFace = (capsuleEdge.yzxy == 0f) | (capsuleEdge.zxyz == 0f);
                float4 edgeDepths              = math.select(edgeScaledDistances / crossEdgeLengths, float.MaxValue, capsuleIsParallelToFace);
                float  bestEdgeDepth           = math.cmin(edgeDepths);
                float3 separatingAxisTowardsCapsule;
                if (bestPointDepth <= bestEdgeDepth)
                {
                    bool3 bestAxisMask            = bestPointDepth == bestPointDistances;
                    bestAxisMask.xz              &= !bestAxisMask.y;
                    bestAxisMask.x               &= !bestAxisMask.z;
                    bool3 useMin                  = bestPointDistances == pointDistancesToBoxMin;
                    separatingAxisTowardsCapsule  = math.select(0f, math.select(1f, -1f, useMin), bestAxisMask);
                }
                else
                {
                    int edgeAxisYz               = math.select(2, 1, edgeDepths.y == bestEdgeDepth);
                    int edgeAxis                 = math.select(edgeAxisYz, 0, edgeDepths.x == bestEdgeDepth);
                    separatingAxisTowardsCapsule = math.chgsign(crosses[edgeAxis], capsuleSupportProjections[edgeAxis]) / crossEdgeLengths[edgeAxis];
                }
                result.normalB    = -separatingAxisTowardsCapsule;
                result.hitpointB += result.normalB * capsule.radius;
            }

            return result.distance <= maxDistance;
        }

        // This is similar to CapsuleCapsule.SegmentSegment, except it assumes that the 4 edges are parallel and aligned to a single axis, such as for a box.
        // This method is significantly faster because of that assumption.
        static void Segment4Edges(float3 segmentA,
                                  float3 segmentB,
                                  simdFloat3 edgeStarts,
                                  float axisHalfSize,
                                  bool3 axisMask,
                                  out simdFloat3 closestOnSegment,
                                  out simdFloat3 closestOnEdges)
        {
            float3 segment = segmentB - segmentA;
            // Remove the axis component from the segment, then project any point of the edge onto that segment and clamp. That gives us the closest point on the segment.
            float3     projectedSegment        = math.select(segment, float3.zero, axisMask);
            simdFloat3 segmentAToBoxEdgeStarts = edgeStarts - segmentA;
            float4     fractions               = math.saturate(simd.dot(segmentAToBoxEdgeStarts, projectedSegment) / math.lengthsq(projectedSegment));
            closestOnSegment                   = simd.select(new simdFloat3(segmentA) + new simdFloat3(segment) * fractions, new simdFloat3(segmentB), fractions == 1f);
            // The box point copies the segment hitpoint on the axis component, clamped.
            closestOnEdges = simd.select(edgeStarts, simd.clamp(closestOnSegment, -axisHalfSize, axisHalfSize), axisMask);
            // If the box point is an endpoint, then we need to find the segment hitpoint again. We check if the closest on the segment exceeds the axis half-size so that perfect
            // endpoint hits don't do this extra step.
            float4 closestOnSegmentAxisComponent = math.select(math.select(closestOnSegment.x, closestOnSegment.y, axisMask.y), closestOnSegment.z, axisMask.z);
            bool4  isClampedEdge                 = math.abs(closestOnSegmentAxisComponent) > axisHalfSize;
            fractions                            = math.saturate(simd.dot(closestOnEdges - segmentA, segment) / math.lengthsq(segment));
            simdFloat3 clampedClosestOnSegment   = simd.select(new simdFloat3(segmentA) + new simdFloat3(segment) * fractions, new simdFloat3(segmentB), fractions == 1f);
            closestOnSegment                     = simd.select(closestOnSegment, clampedClosestOnSegment, isClampedEdge);
        }

        // This method assumes the segment does not intersect the box, and that there's already a segment-endpoints-vs-box test.
        // If the segment is parallel to the box edges, this method classifies all four edges as being segment endpoint hits, which are discarded.
        static void SegmentOutside4EdgesExcludeSegmentEndpoints(float3 segmentA,
                                                                float3 segmentB,
                                                                simdFloat3 edgeStarts,
                                                                float axisHalfSize,
                                                                bool3 axisMask,
                                                                out float4 fractions,
                                                                out float4 distancesSq,
                                                                out simdFloat3 closestOnEdges,
                                                                out simdFloat3 segmentToEdgesPrecise)
        {
            static float3 ToEdgeAxisFrame(float3 v, bool3 axisMask) => math.select(math.select(v.zxy, v.yzx, axisMask.y), v, axisMask.x);
            static simdFloat3 FromEdgeAxisFrame(float4 x, float4 y, float4 z, bool3 axisMask)
            {
                return new simdFloat3(math.select(math.select(y, z, axisMask.y), x, axisMask.x),
                                      math.select(math.select(z, x, axisMask.y), y, axisMask.x),
                                      math.select(math.select(x, y, axisMask.y), z, axisMask.x));
            }

            // Convert to frame where edges are along x. This keeps things tidy and helps Burst.
            segmentA      = ToEdgeAxisFrame(segmentA, axisMask);
            segmentB      = ToEdgeAxisFrame(segmentB, axisMask);
            float4 edgesY = math.select(math.select(edgeStarts.x, edgeStarts.z, axisMask.y), edgeStarts.y, axisMask.x);  // Picks first non-axis after the axis (with wrap around)
            float4 edgesZ = math.select(math.select(edgeStarts.y, edgeStarts.x, axisMask.y), edgeStarts.z, axisMask.x);

            float3 segment = segmentB - segmentA;
            // The following have zero rounding when the segment is very close to the edge.
            float4 segmentAToEdgesY = edgesY - segmentA.y;
            float4 segmentBToEdgesY = edgesY - segmentB.y;
            float4 segmentAToEdgesZ = edgesZ - segmentA.z;
            float4 segmentBToEdgesZ = edgesZ - segmentB.z;

            // Project the box edges onto the segment with the axis component removed. Each box edge looks like a point projected on
            // a perpendicular segmentYZ. We project relative to both endpoints for accuracy near the endpoints.
            float4 scaledProjectionsOnSegmentYZFromA = segmentAToEdgesY * segment.y + segmentAToEdgesZ * segment.z;
            float4 scaledProjectionsOnSegmentYZFromB = segmentBToEdgesY * segment.y + segmentBToEdgesZ * segment.z;
            bool4  infiniteEdgesClosestIsA           = scaledProjectionsOnSegmentYZFromA <= 0f;
            bool4  infiniteEdgesClosestIsB           = scaledProjectionsOnSegmentYZFromB >= 0f;
            bool4  infiniteEdgesClosestIsEndpoint    = infiniteEdgesClosestIsA | infiniteEdgesClosestIsB;
            bool4  infiniteEdgesCloserToB            = scaledProjectionsOnSegmentYZFromA > -scaledProjectionsOnSegmentYZFromB;

            // Prefer the segment endpoint we are closer to when doing measurements, to preserve accuracy.
            float4 infiniteEdgesPreferredEndpointX       = math.select(segmentA.x, segmentB.x, infiniteEdgesCloserToB);
            float4 preferredScaledProjectionsOnSegmentYZ = math.select(scaledProjectionsOnSegmentYZFromA, scaledProjectionsOnSegmentYZFromB, infiniteEdgesCloserToB);
            float4 clampedScaledProjectionsOnSegmentYZ   = segment.x * math.select(preferredScaledProjectionsOnSegmentYZ, float4.zero, infiniteEdgesClosestIsEndpoint);
            // The nearest point along the infinite edge is infiniteEdgesPreferredEndpointX + segment.x * (preferredScaledProjectionsOnSegmentYZ / segmentYZLengthSq).
            // We want to test if that point is beyond axisHalfSize in either direction. To avoid both division and comparing multiplied values, we move the axisHalfSize
            // across the comparison and then multiply both sides by segmentYZLengthSq.
            float segmentYZLengthSq     = math.lengthsq(segment.yz);
            bool4 isClampedEdgePositive = (infiniteEdgesPreferredEndpointX - axisHalfSize) * segmentYZLengthSq + clampedScaledProjectionsOnSegmentYZ > 0f;
            bool4 isClampedEdgeNegative = (infiniteEdgesPreferredEndpointX + axisHalfSize) * segmentYZLengthSq + clampedScaledProjectionsOnSegmentYZ < 0f;
            bool4 isClampedEdge         = isClampedEdgePositive | isClampedEdgeNegative;

            // If we clamped the edge, we need to reproject the edge vertex onto the full segment. We reuse a partial dot product for this.
            // If we didn't clamp to the edge, we keep the YZ projection. Thus we need the appropriate projection length to divide by later.
            float  segmentLengthSq                 = segmentYZLengthSq + segment.x * segment.x;
            float4 projectionOnSegmentLengthSq     = math.select(segmentYZLengthSq, segmentLengthSq, isClampedEdge);
            float4 edgeVerticesX                   = math.select(-axisHalfSize, axisHalfSize, isClampedEdgePositive);
            float4 scaledProjectionsOnSegmentFromA = math.select(scaledProjectionsOnSegmentYZFromA,
                                                                 scaledProjectionsOnSegmentYZFromA + (edgeVerticesX - segmentA.x) * segment.x,
                                                                 isClampedEdge);
            float4 scaledProjectionsOnSegmentFromB = math.select(scaledProjectionsOnSegmentYZFromB,
                                                                 scaledProjectionsOnSegmentYZFromB + (edgeVerticesX - segmentB.x) * segment.x,
                                                                 isClampedEdge);
            bool4 segmentClosestIsInterior = math.select(!infiniteEdgesClosestIsEndpoint,
                                                         (scaledProjectionsOnSegmentFromA > 0f) & (scaledProjectionsOnSegmentFromB < 0f),
                                                         isClampedEdge);
            bool4 segmentClosestIsCloserToB = math.select(infiniteEdgesCloserToB, scaledProjectionsOnSegmentFromA > -scaledProjectionsOnSegmentFromB, isClampedEdge);

            fractions              = math.saturate(scaledProjectionsOnSegmentFromA / projectionOnSegmentLengthSq);
            float4 closestOnEdgesX = math.select(math.clamp(segmentA.x + segment.x * fractions, -axisHalfSize, axisHalfSize), edgeVerticesX, isClampedEdge);
            closestOnEdges         = FromEdgeAxisFrame(closestOnEdgesX, edgesY, edgesZ, axisMask);

            // We need the precise vector between the segment and the edges.
            // First, let's start with a base using the vector from the nearest segment endpoint towards the edge's closest point. This is a precise value.
            float4     preferredEndpointX       = math.select(segmentA.x, segmentB.x, segmentClosestIsCloserToB);
            simdFloat3 preferredEndpointToEdges = new simdFloat3(closestOnEdgesX - preferredEndpointX,
                                                                 math.select(segmentAToEdgesY, segmentBToEdgesY, segmentClosestIsCloserToB),
                                                                 math.select(segmentAToEdgesZ, segmentBToEdgesZ, segmentClosestIsCloserToB));
            // We will assume right now that the closest segment endpoint is not clamped. With this assumption, our precise vector is perpendicular to the segment.
            // If we did not clamp the edge, then this perpendicular is shared. It is a cross product that simplifies to (0f, segment.z, -segment.y).
            float4 sharedPerpendicularEndpointToEdgesProjections = preferredEndpointToEdges.y * segment.z - preferredEndpointToEdges.z * segment.y;  // dot(preferredEndpointsToEdges, sharedPerpendicular)
            // If we did clamp the edge, then we want to slide the endpoint along the segment based on the projection of the edge vertex relative to the endpoint onto the segment.
            // We already computed this projection.
            float4 preferredScaledProjectionsOnSegment = math.select(scaledProjectionsOnSegmentFromA, scaledProjectionsOnSegmentFromB, segmentClosestIsCloserToB);
            // This means different things depending on if we clamped or not, but we take advantage of a single divide instruction for both paths.
            float4 correctionFractions =
                math.select(sharedPerpendicularEndpointToEdgesProjections, preferredScaledProjectionsOnSegment, isClampedEdge) / projectionOnSegmentLengthSq;
            // If we did not clamp the edge, then our fraction is relative to the perpendicular
            simdFloat3 segmentToEdgesInterior = new simdFloat3(0f, segment.z * correctionFractions, -segment.y * correctionFractions);
            // If we did clamp, then our fraction is the slide fraction along the segment
            simdFloat3 segmentToEdgesClamped = preferredEndpointToEdges - new simdFloat3(segment) * correctionFractions;
            segmentToEdgesPrecise            = simd.select(segmentToEdgesInterior, segmentToEdgesClamped, isClampedEdge);
            // If the closest point on the segment is an endpoint, discard it. We already have it from a point-box test. This avoids us having to calculate metadata for that point again.
            distancesSq           = math.select(float.MaxValue, simd.lengthsq(segmentToEdgesPrecise), segmentClosestIsInterior);
            segmentToEdgesPrecise = FromEdgeAxisFrame(segmentToEdgesPrecise.x, segmentToEdgesPrecise.y, segmentToEdgesPrecise.z, axisMask);
        }

        struct CastAgainstEdgeInputs
        {
            public Ray             ray;
            public CapsuleCollider capsule;
            public BoxCollider     box;
            public float           rayDisplacementLength;
            public float3          rayDirection;
            public simdFloat3      planeNormals;
            public float4          planeDistancesToOrigin;
        }

        static void CastAgainstEdge(in CastAgainstEdgeInputs inputs, bool3 axisMask, bool3 negatedAxes, ref float fraction)
        {
            // If both adjacent faces to the edge point in the same direction of the ray, then this is a backfacing edge and can be skipped.
            float3 faceDirections = math.select(1f, -1f, negatedAxes);
            float3 faceNormalA    = math.select(float3.zero, faceDirections, axisMask.yzx);
            float3 faceNormalB    = math.select(float3.zero, faceDirections, axisMask.zxy);
            if (math.dot(faceNormalA, inputs.ray.displacement) < 0f && math.dot(faceNormalB, inputs.ray.displacement) < 0f)
                return;

            // Cull anything that missed sideways
            float3 targetA                      = inputs.box.center + math.select(inputs.box.halfSize, -inputs.box.halfSize, axisMask | negatedAxes);
            float3 targetB                      = inputs.box.center + math.select(inputs.box.halfSize, -inputs.box.halfSize, negatedAxes);
            float4 edgeASignedDistancesToPlanes = simd.dot(inputs.planeNormals, targetA) + inputs.planeDistancesToOrigin;
            float4 edgeBSignedDistancesToPlanes = simd.dot(inputs.planeNormals, targetB) + inputs.planeDistancesToOrigin;
            if (math.any((edgeASignedDistancesToPlanes > 0f) & edgeBSignedDistancesToPlanes > 0f))
                return;

            simdFloat3 cso = new simdFloat3(inputs.capsule.pointA - targetA, inputs.capsule.pointA - targetB, inputs.capsule.pointB - targetB, inputs.capsule.pointB - targetA);

            // Check if we already hit something before the ray can even reach this edge.
            var validRayDistance     = inputs.rayDisplacementLength * math.min(fraction, 1f);
            var csoPointsAgainstRay  = cso - inputs.rayDirection * inputs.capsule.radius - inputs.ray.start;
            var csoDistancesAlongRay = simd.dot(inputs.rayDirection, csoPointsAgainstRay);
            if (math.all(csoDistancesAlongRay > validRayDistance))
                return;

            bool hit = PointRayTriangle.RaycastRoundedQuad(in inputs.ray, in cso, inputs.capsule.radius, out var hitFraction, out _);
            if (hit)
                fraction = math.min(fraction, hitFraction);
        }
    }
}

