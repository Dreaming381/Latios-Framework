using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class CapsuleBox
    {
        // Box is first because it is cheaper to transform a capsule into A-space
        public static bool DistanceBetween(in BoxCollider box,
                                           in RigidTransform boxTransform,
                                           in CapsuleCollider capsule,
                                           in RigidTransform capsuleTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            var boxWorldToLocal        = math.inverse(boxTransform);
            var capInBoxSpaceTransform = math.mul(boxWorldToLocal, capsuleTransform);
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
            if (DistanceBetween(in targetBox, in targetBoxTransform, in capsuleToCast, in castStart, 0f, out _))
            {
                result = default;
                return false;
            }

            var targetBoxTransformInverse = math.inverse(targetBoxTransform);
            var casterInTargetSpace       = math.mul(targetBoxTransformInverse, castStart);
            var inflatedBoxAabb           = new Aabb(targetBox.center - targetBox.halfSize - capsuleToCast.radius, targetBox.center + targetBox.halfSize + capsuleToCast.radius);

            var  startA    = math.transform(casterInTargetSpace, capsuleToCast.pointA);
            var  rayA      = new Ray(startA, startA + math.rotate(targetBoxTransformInverse, castEnd - castStart.pos));
            bool hitA      = PointRayBox.RaycastAabb(in rayA, in inflatedBoxAabb, out var fractionA);
            var  hitpointA = math.lerp(rayA.start, rayA.end, fractionA) - targetBox.center;
            // The hit point is somewhere on the inflated box, which means that whatever side of the inflated box it hits,
            // that axis should be beyond the half-size by about a radius. For a flat side hit,
            // the remaining two axes should be less than or equal to the half size.
            // Warning: This may not work correctly for extremely small or zero radius.
            hitA           &= math.countbits(math.bitmask(new bool4(math.abs(hitpointA) > targetBox.halfSize, false))) == 1;
            fractionA       = math.select(2f, fractionA, hitA);
            var  startB     = math.transform(casterInTargetSpace, capsuleToCast.pointB);
            var  rayB       = new Ray(startB, startB + math.rotate(targetBoxTransformInverse, castEnd - castStart.pos));
            bool hitB       = PointRayBox.RaycastAabb(in rayB, in inflatedBoxAabb, out var fractionB);
            var  hitpointB  = math.lerp(rayB.start, rayB.end, fractionB) - targetBox.center;
            hitB           &= math.countbits(math.bitmask(new bool4(math.abs(hitpointB) > targetBox.halfSize, false))) == 1;
            fractionB       = math.select(2f, fractionB, hitB);

            var        ray = new Ray(0f, math.rotate(targetBoxTransformInverse, castStart.pos - castEnd));
            bool4      hitX;
            float4     fractionsX;
            float3     targetA = targetBox.center - targetBox.halfSize;
            float3     targetB = targetBox.center + new float3(targetBox.halfSize.x, -targetBox.halfSize.y, -targetBox.halfSize.z);
            simdFloat3 cso     = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitX.x             = PointRayTriangle.RaycastRoundedQuad(in ray, in cso, capsuleToCast.radius, out fractionsX.x, out _);
            targetA            = targetBox.center + new float3(-targetBox.halfSize.x, -targetBox.halfSize.y, targetBox.halfSize.z);
            targetB            = targetBox.center + new float3(targetBox.halfSize.x, -targetBox.halfSize.y, targetBox.halfSize.z);
            cso                = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitX.y             = PointRayTriangle.RaycastRoundedQuad(in ray, in cso, capsuleToCast.radius, out fractionsX.y, out _);
            targetA            = targetBox.center + new float3(-targetBox.halfSize.x, targetBox.halfSize.y, -targetBox.halfSize.z);
            targetB            = targetBox.center + new float3(targetBox.halfSize.x, targetBox.halfSize.y, -targetBox.halfSize.z);
            cso                = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitX.z             = PointRayTriangle.RaycastRoundedQuad(in ray, in cso, capsuleToCast.radius, out fractionsX.z, out _);
            targetA            = targetBox.center + new float3(-targetBox.halfSize.x, targetBox.halfSize.y, targetBox.halfSize.z);
            targetB            = targetBox.center + targetBox.halfSize;
            cso                = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitX.w             = PointRayTriangle.RaycastRoundedQuad(in ray, in cso, capsuleToCast.radius, out fractionsX.w, out _);
            fractionsX         = math.select(2f, fractionsX, hitX);

            bool4  hitY;
            float4 fractionsY;
            targetA    = targetBox.center - targetBox.halfSize;
            targetB    = targetBox.center + new float3(-targetBox.halfSize.x, targetBox.halfSize.y, -targetBox.halfSize.z);
            cso        = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitY.x     = PointRayTriangle.RaycastRoundedQuad(in ray, in cso, capsuleToCast.radius, out fractionsY.x, out _);
            targetA    = targetBox.center + new float3(-targetBox.halfSize.x, -targetBox.halfSize.y, targetBox.halfSize.z);
            targetB    = targetBox.center + new float3(-targetBox.halfSize.x, targetBox.halfSize.y, targetBox.halfSize.z);
            cso        = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitY.y     = PointRayTriangle.RaycastRoundedQuad(in ray, in cso, capsuleToCast.radius, out fractionsY.y, out _);
            targetA    = targetBox.center + new float3(targetBox.halfSize.x, -targetBox.halfSize.y, -targetBox.halfSize.z);
            targetB    = targetBox.center + new float3(targetBox.halfSize.x, targetBox.halfSize.y, -targetBox.halfSize.z);
            cso        = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitY.z     = PointRayTriangle.RaycastRoundedQuad(in ray, in cso, capsuleToCast.radius, out fractionsY.z, out _);
            targetA    = targetBox.center + new float3(targetBox.halfSize.x, -targetBox.halfSize.y, targetBox.halfSize.z);
            targetB    = targetBox.center + targetBox.halfSize;
            cso        = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitY.w     = PointRayTriangle.RaycastRoundedQuad(in ray, in cso, capsuleToCast.radius, out fractionsY.w, out _);
            fractionsY = math.select(2f, fractionsY, hitY);

            bool4  hitZ;
            float4 fractionsZ;
            targetA    = targetBox.center - targetBox.halfSize;
            targetB    = targetBox.center + new float3(-targetBox.halfSize.x, -targetBox.halfSize.y, targetBox.halfSize.z);
            cso        = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitZ.x     = PointRayTriangle.RaycastRoundedQuad(in ray, in cso, capsuleToCast.radius, out fractionsZ.x, out _);
            targetA    = targetBox.center + new float3(-targetBox.halfSize.x, targetBox.halfSize.y, -targetBox.halfSize.z);
            targetB    = targetBox.center + new float3(-targetBox.halfSize.x, targetBox.halfSize.y, targetBox.halfSize.z);
            cso        = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitZ.y     = PointRayTriangle.RaycastRoundedQuad(in ray, in cso, capsuleToCast.radius, out fractionsZ.y, out _);
            targetA    = targetBox.center + new float3(targetBox.halfSize.x, -targetBox.halfSize.y, -targetBox.halfSize.z);
            targetB    = targetBox.center + new float3(targetBox.halfSize.x, -targetBox.halfSize.y, targetBox.halfSize.z);
            cso        = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitZ.z     = PointRayTriangle.RaycastRoundedQuad(in ray, in cso, capsuleToCast.radius, out fractionsZ.z, out _);
            targetA    = targetBox.center + new float3(targetBox.halfSize.x, targetBox.halfSize.y, -targetBox.halfSize.z);
            targetB    = targetBox.center + targetBox.halfSize;
            cso        = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitZ.w     = PointRayTriangle.RaycastRoundedQuad(in ray, in cso, capsuleToCast.radius, out fractionsZ.w, out _);
            fractionsZ = math.select(2f, fractionsZ, hitZ);

            bool  hit      = math.any(hitX | hitY | hitZ) | hitA | hitB;
            float fraction = math.min(math.min(fractionA, fractionB), math.cmin(math.min(fractionsX, math.min(fractionsY, fractionsZ))));
            if (hit)
            {
                var hitTransform = castStart;
                hitTransform.pos = math.lerp(castStart.pos, castEnd, fraction);
                DistanceBetween(in targetBox, in targetBoxTransform, in capsuleToCast, in hitTransform, 1f, out var distanceResult);
                result = new ColliderCastResult
                {
                    hitpoint                 = distanceResult.hitpointB,
                    normalOnCaster           = distanceResult.normalB,
                    normalOnTarget           = distanceResult.normalA,
                    subColliderIndexOnCaster = distanceResult.subColliderIndexB,
                    subColliderIndexOnTarget = distanceResult.subColliderIndexA,
                    distance                 = math.distance(hitTransform.pos, castStart.pos)
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
            if (DistanceBetween(in boxToCast, in castStart, in targetCapsule, in targetCapsuleTransform, 0f, out _))
            {
                result = default;
                return false;
            }

            var castStartInverse    = math.inverse(castStart);
            var targetInCasterSpace = math.mul(castStartInverse, targetCapsuleTransform);
            var inflatedBoxAabb     = new Aabb(boxToCast.center - boxToCast.halfSize - targetCapsule.radius, boxToCast.center + boxToCast.halfSize + targetCapsule.radius);

            var  targetA    = math.transform(targetInCasterSpace, targetCapsule.pointA);
            var  rayA       = new Ray(targetA, targetA - math.rotate(castStartInverse, castEnd - castStart.pos));
            bool hitA       = PointRayBox.RaycastAabb(in rayA, in inflatedBoxAabb, out var fractionA);
            var  hitpointA  = math.lerp(rayA.start, rayA.end, fractionA) - boxToCast.center;
            hitA           &= math.countbits(math.bitmask(new bool4(math.abs(hitpointA) > boxToCast.halfSize, false))) == 1;
            fractionA       = math.select(2f, fractionA, hitA);
            var  targetB    = math.transform(targetInCasterSpace, targetCapsule.pointB);
            var  rayB       = new Ray(targetB, targetB - math.rotate(castStartInverse, castEnd - castStart.pos));
            bool hitB       = PointRayBox.RaycastAabb(in rayB, in inflatedBoxAabb, out var fractionB);
            var  hitpointB  = math.lerp(rayB.start, rayB.end, fractionB) - boxToCast.center;
            hitB           &= math.countbits(math.bitmask(new bool4(math.abs(hitpointB) > boxToCast.halfSize, false))) == 1;
            fractionB       = math.select(2f, fractionB, hitB);

            var        ray = new Ray(0f, math.rotate(castStartInverse, castStart.pos - castEnd));
            bool4      hitX;
            float4     fractionsEdge;
            float3     startA = boxToCast.center - boxToCast.halfSize;
            float3     startB = boxToCast.center + new float3(boxToCast.halfSize.x, -boxToCast.halfSize.y, -boxToCast.halfSize.z);
            simdFloat3 cso    = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitX.x            = PointRayTriangle.RaycastRoundedQuad(in ray, in cso, targetCapsule.radius, out fractionsEdge.x, out _);
            startA            = boxToCast.center + new float3(-boxToCast.halfSize.x, -boxToCast.halfSize.y, boxToCast.halfSize.z);
            startB            = boxToCast.center + new float3(boxToCast.halfSize.x, -boxToCast.halfSize.y, boxToCast.halfSize.z);
            cso               = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitX.y            = PointRayTriangle.RaycastRoundedQuad(in ray, in cso, targetCapsule.radius, out fractionsEdge.y, out _);
            startA            = boxToCast.center + new float3(-boxToCast.halfSize.x, boxToCast.halfSize.y, -boxToCast.halfSize.z);
            startB            = boxToCast.center + new float3(boxToCast.halfSize.x, boxToCast.halfSize.y, -boxToCast.halfSize.z);
            cso               = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitX.z            = PointRayTriangle.RaycastRoundedQuad(in ray, in cso, targetCapsule.radius, out fractionsEdge.z, out _);
            startA            = boxToCast.center + new float3(-boxToCast.halfSize.x, boxToCast.halfSize.y, boxToCast.halfSize.z);
            startB            = boxToCast.center + boxToCast.halfSize;
            cso               = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitX.w            = PointRayTriangle.RaycastRoundedQuad(in ray, in cso, targetCapsule.radius, out fractionsEdge.w, out _);
            fractionsEdge     = math.select(2f, fractionsEdge, hitX);

            bool4  hitY;
            float4 fractionsY;
            startA     = boxToCast.center - boxToCast.halfSize;
            startB     = boxToCast.center + new float3(-boxToCast.halfSize.x, boxToCast.halfSize.y, -boxToCast.halfSize.z);
            cso        = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitY.x     = PointRayTriangle.RaycastRoundedQuad(in ray, in cso, targetCapsule.radius, out fractionsY.x, out _);
            startA     = boxToCast.center + new float3(-boxToCast.halfSize.x, -boxToCast.halfSize.y, boxToCast.halfSize.z);
            startB     = boxToCast.center + new float3(-boxToCast.halfSize.x, boxToCast.halfSize.y, boxToCast.halfSize.z);
            cso        = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitY.y     = PointRayTriangle.RaycastRoundedQuad(in ray, in cso, targetCapsule.radius, out fractionsY.y, out _);
            startA     = boxToCast.center + new float3(boxToCast.halfSize.x, -boxToCast.halfSize.y, -boxToCast.halfSize.z);
            startB     = boxToCast.center + new float3(boxToCast.halfSize.x, boxToCast.halfSize.y, -boxToCast.halfSize.z);
            cso        = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitY.z     = PointRayTriangle.RaycastRoundedQuad(in ray, in cso, targetCapsule.radius, out fractionsY.z, out _);
            startA     = boxToCast.center + new float3(boxToCast.halfSize.x, -boxToCast.halfSize.y, boxToCast.halfSize.z);
            startB     = boxToCast.center + boxToCast.halfSize;
            cso        = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitY.w     = PointRayTriangle.RaycastRoundedQuad(in ray, in cso, targetCapsule.radius, out fractionsY.w, out _);
            fractionsY = math.select(2f, fractionsY, hitY);

            bool4  hitZ;
            float4 fractionsZ;
            startA     = boxToCast.center - boxToCast.halfSize;
            startB     = boxToCast.center + new float3(-boxToCast.halfSize.x, -boxToCast.halfSize.y, boxToCast.halfSize.z);
            cso        = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitZ.x     = PointRayTriangle.RaycastRoundedQuad(in ray, in cso, targetCapsule.radius, out fractionsZ.x, out _);
            startA     = boxToCast.center + new float3(-boxToCast.halfSize.x, boxToCast.halfSize.y, -boxToCast.halfSize.z);
            startB     = boxToCast.center + new float3(-boxToCast.halfSize.x, boxToCast.halfSize.y, boxToCast.halfSize.z);
            cso        = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitZ.y     = PointRayTriangle.RaycastRoundedQuad(in ray, in cso, targetCapsule.radius, out fractionsZ.y, out _);
            startA     = boxToCast.center + new float3(boxToCast.halfSize.x, -boxToCast.halfSize.y, -boxToCast.halfSize.z);
            startB     = boxToCast.center + new float3(boxToCast.halfSize.x, -boxToCast.halfSize.y, boxToCast.halfSize.z);
            cso        = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitZ.z     = PointRayTriangle.RaycastRoundedQuad(in ray, in cso, targetCapsule.radius, out fractionsZ.z, out _);
            startA     = boxToCast.center + new float3(boxToCast.halfSize.x, boxToCast.halfSize.y, -boxToCast.halfSize.z);
            startB     = boxToCast.center + boxToCast.halfSize;
            cso        = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitZ.w     = PointRayTriangle.RaycastRoundedQuad(in ray, in cso, targetCapsule.radius, out fractionsZ.w, out _);
            fractionsZ = math.select(2f, fractionsZ, hitZ);

            bool  hit      = math.any(hitX | hitY | hitZ) | hitA | hitB;
            float fraction = math.min(math.min(fractionA, fractionB), math.cmin(math.min(fractionsEdge, math.min(fractionsY, fractionsZ))));
            if (hit)
            {
                var hitTransform = castStart;
                hitTransform.pos = math.lerp(castStart.pos, castEnd, fraction);
                DistanceBetween(in boxToCast, in hitTransform, in targetCapsule, in targetCapsuleTransform, 1f, out var distanceResult);
                result = new ColliderCastResult
                {
                    hitpoint                 = distanceResult.hitpointA,
                    normalOnCaster           = distanceResult.normalA,
                    normalOnTarget           = distanceResult.normalB,
                    subColliderIndexOnCaster = distanceResult.subColliderIndexA,
                    subColliderIndexOnTarget = distanceResult.subColliderIndexB,
                    distance                 = math.distance(hitTransform.pos, castStart.pos)
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
                result.Add(math.transform(boxTransform, clippedSegmentA), aDistance - capsule.radius);
                result.Add(math.transform(boxTransform, clippedSegmentB), bDistance - capsule.radius);
                needsClosestPoint = math.min(aDistance, bDistance) > distanceResult.distance + 1e-4f;  // Magic constant comes from Unity Physics
            }

            if (needsClosestPoint)
                result.Add(distanceResult.hitpointB, distanceResult.distance);
            return result;
        }

        private static bool BoxCapsuleDistance(in BoxCollider box, in CapsuleCollider capsule, float maxDistance, out ColliderDistanceResultInternal result)
        {
            float3 osPointA = capsule.pointA - box.center;  //os = origin space
            float3 osPointB = capsule.pointB - box.center;
            float3 pointsPointOnBox;
            float3 pointsPointOnSegment;
            float  axisDistance;
            // Step 1: Points vs Planes
            {
                float3 distancesToMin = math.max(osPointA, osPointB) + box.halfSize;
                float3 distancesToMax = box.halfSize - math.min(osPointA, osPointB);
                float3 bestDistances  = math.min(distancesToMin, distancesToMax);
                float  bestDistance   = math.cmin(bestDistances);
                bool3  bestAxisMask   = bestDistance == bestDistances;
                // Prioritize y first, then z, then x if multiple distances perfectly match.
                // Todo: Should this be configurabe?
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

            // Step 2: Edge vs Edges
            // Todo: We could inline the SegmentSegment invocations to simplify the initial dot products.
            float3     capsuleEdge     = osPointB - osPointA;
            simdFloat3 simdCapsuleEdge = new simdFloat3(capsuleEdge);
            simdFloat3 simdOsPointA    = new simdFloat3(osPointA);
            // x-axes
            simdFloat3 boxPointsX = new simdFloat3(new float3(-box.halfSize.x, box.halfSize.y, box.halfSize.z),
                                                   new float3(-box.halfSize.x, box.halfSize.y, -box.halfSize.z),
                                                   new float3(-box.halfSize.x, -box.halfSize.y, box.halfSize.z),
                                                   new float3(-box.halfSize.x, -box.halfSize.y, -box.halfSize.z));
            simdFloat3 boxEdgesX = new simdFloat3(new float3(2f * box.halfSize.x, 0f, 0f));
            CapsuleCapsule.SegmentSegment(simdOsPointA, simdCapsuleEdge, boxPointsX, boxEdgesX, out simdFloat3 edgesClosestAsX, out simdFloat3 edgesClosestBsX);
            simdFloat3 boxNormalsX = new simdFloat3(new float3(0f, math.SQRT2 / 2f, math.SQRT2 / 2f),
                                                    new float3(0f, math.SQRT2 / 2f, -math.SQRT2 / 2f),
                                                    new float3(0f, -math.SQRT2 / 2f, math.SQRT2 / 2f),
                                                    new float3(0f, -math.SQRT2 / 2f, -math.SQRT2 / 2f));
            // Imagine a line that goes perpendicular through a box's edge at the midpoint.
            // All orientations of that line which do not penetrate the box (tangency is not considered penetrating in this case) are validly resolved collisions.
            // Orientations of the line which do penetrate are not valid.
            // If we constrain the capsule edge to be perpendicular, normalize it, and then compute the dot product, we can compare that to the necessary 45 degree angle
            // where penetration occurs. Parallel lines are excluded because either we want to record a capsule point (step 1) or a perpendicular edge on the box.
            bool   notParallelX       = !capsuleEdge.yz.Equals(float2.zero);
            float4 alignmentsX        = simd.dot(math.normalize(new float3(0f, capsuleEdge.yz)), boxNormalsX);
            bool4  validsX            = (math.abs(alignmentsX) <= math.SQRT2 / 2f) & notParallelX;
            float4 signedDistancesSqX = simd.distancesq(edgesClosestAsX, edgesClosestBsX);
            bool4  insidesX           =
                (math.abs(edgesClosestAsX.x) <= box.halfSize.x) & (math.abs(edgesClosestAsX.y) <= box.halfSize.y) & (math.abs(edgesClosestAsX.z) <= box.halfSize.z);
            signedDistancesSqX = math.select(signedDistancesSqX, -signedDistancesSqX, insidesX);
            signedDistancesSqX = math.select(float.MaxValue, signedDistancesSqX, validsX);

            // y-axis
            simdFloat3 boxPointsY = new simdFloat3(new float3(box.halfSize.x, -box.halfSize.y, box.halfSize.z),
                                                   new float3(box.halfSize.x, -box.halfSize.y, -box.halfSize.z),
                                                   new float3(-box.halfSize.x, -box.halfSize.y, box.halfSize.z),
                                                   new float3(-box.halfSize.x, -box.halfSize.y, -box.halfSize.z));
            simdFloat3 boxEdgesY = new simdFloat3(new float3(0f, 2f * box.halfSize.y, 0f));
            CapsuleCapsule.SegmentSegment(simdOsPointA, simdCapsuleEdge, boxPointsY, boxEdgesY, out simdFloat3 edgesClosestAsY, out simdFloat3 edgesClosestBsY);
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

            // z-axis
            simdFloat3 boxPointsZ = new simdFloat3(new float3(box.halfSize.x, box.halfSize.y, -box.halfSize.z),
                                                   new float3(box.halfSize.x, -box.halfSize.y, -box.halfSize.z),
                                                   new float3(-box.halfSize.x, box.halfSize.y, -box.halfSize.z),
                                                   new float3(-box.halfSize.x, -box.halfSize.y, -box.halfSize.z));
            simdFloat3 boxEdgesZ = new simdFloat3(new float3(0f, 0f, 2f * box.halfSize.z));
            CapsuleCapsule.SegmentSegment(simdOsPointA, simdCapsuleEdge, boxPointsZ, boxEdgesZ, out simdFloat3 edgesClosestAsZ, out simdFloat3 edgesClosestBsZ);
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

            // Step 4: Create result
            float3 boxNormal         = math.normalize(math.select(0f, 1f, bestPointOnBox == box.halfSize) + math.select(0f, -1f, bestPointOnBox == -box.halfSize));
            float3 capsuleNormal     = math.normalizesafe(bestPointOnBox - bestPointOnSegment);
            bool   capsuleDegenerate = capsuleNormal.Equals(float3.zero);
            capsuleNormal            = math.select(capsuleNormal, -capsuleNormal, bestSignedDistanceSq < 0f);
            result                   = new ColliderDistanceResultInternal
            {
                hitpointA    = bestPointOnBox + box.center,
                hitpointB    = bestPointOnSegment + box.center + capsuleNormal * capsule.radius,
                normalA      = boxNormal,
                normalB      = capsuleNormal,
                distance     = math.sign(bestSignedDistanceSq) * math.sqrt(math.abs(bestSignedDistanceSq)) - capsule.radius,
                featureCodeA = PointRayBox.FeatureCodeFromBoxNormal(boxNormal),
                featureCodeB = (ushort)math.select(0x4000,
                                                   math.select(0, 1, math.all(bestPointOnSegment == osPointB)),
                                                   bestPointOnSegment.Equals(osPointA) || bestPointOnSegment.Equals(osPointB))
            };

            if (Hint.Likely(!capsuleDegenerate))
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

            result.normalB    = math.normalize(math.cross(math.cross(capsuleEdge, -boxNormal), capsuleEdge));
            result.hitpointB += result.normalB * capsule.radius;
            return result.distance <= maxDistance;
        }
    }
}

