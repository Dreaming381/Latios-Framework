using Unity.Mathematics;

// This file contains Minkowski Portal Refinement algorithms primarily purposed for shapecasting.
// The implementations here were not derived from any particular reference code.

namespace Latios.Psyshock
{
    internal static class Mpr
    {
        public static bool MprCastNoRoundness(in Collider caster,
                                              in Collider target,
                                              in RigidTransform targetInCasterSpace,
                                              float3 normalizedCastDirectionInCasterSpace,
                                              float maxCastDistance,
                                              out float distanceOfImpact,
                                              out bool somethingWentWrong)
        {
            somethingWentWrong = false;

            // First, test if the caster can even reach the target
            var   portalA     = MinkowskiSupports.GetSupport(in caster, in target, normalizedCastDirectionInCasterSpace, in targetInCasterSpace);
            float minDistance =
                math.dot(MinkowskiSupports.GetSupport(in caster, in target, -normalizedCastDirectionInCasterSpace, in targetInCasterSpace).pos,
                         normalizedCastDirectionInCasterSpace);
            float maxDistance = math.dot(portalA.pos, normalizedCastDirectionInCasterSpace) + maxCastDistance;
            if (minDistance > 0f || maxDistance < 0f)
            {
                distanceOfImpact = k_missDistance;
                return false;
            }

            // Second, check if the caster's AABB reaches the target's AABB laterally
            var initialCsoAabb = MinkowskiSupports.GetCsoAabb(in caster, in target, targetInCasterSpace);
            if (math.all(initialCsoAabb.max < 0f | initialCsoAabb.min > 0f))
            {
                // The origin is not in the Aabb. Cast the ray in both directions
                float castMagnitude = math.max(maxDistance, maxCastDistance) - minDistance;
                bool  hitForward    = PointRayBox.RaycastAabb(new Ray(0f, normalizedCastDirectionInCasterSpace * castMagnitude), initialCsoAabb, out _);
                bool  hitBackward   = PointRayBox.RaycastAabb(new Ray(0f, -normalizedCastDirectionInCasterSpace * castMagnitude), initialCsoAabb, out _);
                if (!(hitForward | hitBackward))
                {
                    distanceOfImpact = k_missDistance;
                    return false;
                }
            }

            // Third, check if the caster reaches the target laterally using planar MPR
            if (!DoPlanarMpr(in caster, in target, in targetInCasterSpace, normalizedCastDirectionInCasterSpace, portalA.pos, out var portalB, out var portalC,
                             ref somethingWentWrong))
            {
                distanceOfImpact = k_missDistance;
                return false;
            }

            // At this point, wen know that the extents results in a collision. So it is safe to do the full 3D MPR.
            // But first, we need to slide the caster so that the origin is behind the CSO.
            float          slideDistance            = -minDistance;
            RigidTransform slidTargetInCasterSpace  = targetInCasterSpace;
            slidTargetInCasterSpace.pos            -= slideDistance * normalizedCastDirectionInCasterSpace;
            // Because our space has been slid, we need to recover our supports using the IDs and the new space

            portalA = MinkowskiSupports.Get3DSupportFromPlanar(in caster, in target, slidTargetInCasterSpace, portalA);
            portalB = MinkowskiSupports.Get3DSupportFromPlanar(in caster, in target, slidTargetInCasterSpace, portalB);
            portalC = MinkowskiSupports.Get3DSupportFromPlanar(in caster, in target, slidTargetInCasterSpace, portalC);

            // Our planar supports might be interior supports due to the axis reduction.
            //portalA = GetSupport(caster, target, portalA.pos, slidTargetInCasterSpace);
            //portalB = GetSupport(caster, target, portalB.pos, slidTargetInCasterSpace);
            //portalC = GetSupport(caster, target, portalC.pos, slidTargetInCasterSpace);

            // Catch the case where the ray misses the portal triangle. It is frustrating that it happens, but this should catch it.
            DoMprPortalSearch(in caster, in target, in slidTargetInCasterSpace, normalizedCastDirectionInCasterSpace, ref portalA, ref portalB, ref portalC,
                              ref somethingWentWrong);

            float mprDistance = DoMprRefine3D(in caster,
                                              in target,
                                              in slidTargetInCasterSpace,
                                              normalizedCastDirectionInCasterSpace,
                                              portalA,
                                              portalB,
                                              portalC,
                                              ref somethingWentWrong);

            distanceOfImpact = slideDistance - mprDistance;
            return distanceOfImpact <= maxCastDistance;
        }

        // If distanceOfImpact is negative, the caller should test if the two objects overlap at the initial position. The two objects projected OBBs intersect.
        public static bool MprCastNoRoundnessDebug(Collider caster,
                                                   Collider target,
                                                   RigidTransform targetInCasterSpace,
                                                   float3 normalizedCastDirectionInCasterSpace,
                                                   float maxCastDistance,
                                                   out float distanceOfImpact,
                                                   out bool somethingWentWrong)
        {
            somethingWentWrong = false;

            // First, test if the caster can even reach the target
            var   portalA     = MinkowskiSupports.GetSupport(in caster, in target, normalizedCastDirectionInCasterSpace, in targetInCasterSpace);
            float minDistance =
                math.dot(MinkowskiSupports.GetSupport(in caster, in target, -normalizedCastDirectionInCasterSpace, in targetInCasterSpace).pos,
                         normalizedCastDirectionInCasterSpace);
            float maxDistance = math.dot(portalA.pos, normalizedCastDirectionInCasterSpace) + maxCastDistance;
            if (minDistance > 0f || maxDistance < 0f)
            {
                distanceOfImpact = k_missDistance;
                return false;
            }

            // Second, check if the caster's AABB reaches the target's AABB laterally
            var initialCsoAabb = MinkowskiSupports.GetCsoAabb(in caster, in target, in targetInCasterSpace);
            if (math.all(initialCsoAabb.max < 0f | initialCsoAabb.min > 0f))
            {
                // The origin is not in the Aabb. Cast the ray in both directions
                float castMagnitude = math.max(maxDistance, maxCastDistance) - minDistance;
                bool  hitForward    = PointRayBox.RaycastAabb(new Ray(0f, normalizedCastDirectionInCasterSpace * castMagnitude), in initialCsoAabb, out _);
                bool  hitBackward   = PointRayBox.RaycastAabb(new Ray(0f, -normalizedCastDirectionInCasterSpace * castMagnitude), in initialCsoAabb, out _);
                if (!(hitForward | hitBackward))
                {
                    distanceOfImpact = k_missDistance;
                    return false;
                }
            }

            // Third, check if the caster reaches the target laterally using planar MPR
            if (!DoPlanarMprDebug(in caster, in target, in targetInCasterSpace, normalizedCastDirectionInCasterSpace, portalA.pos, out var portalB, out var portalC,
                                  ref somethingWentWrong))
            {
                distanceOfImpact = k_missDistance;
                return false;
            }

            // At this point, wen know that the extents results in a collision. So it is safe to do the full 3D MPR.
            // But first, we need to slide the caster so that the origin is behind the CSO.
            float slideDistance = -minDistance;
            UnityEngine.Debug.Log($"Running Mpr3D, sliding: {slideDistance}");
            RigidTransform slidTargetInCasterSpace  = targetInCasterSpace;
            slidTargetInCasterSpace.pos            -= slideDistance * normalizedCastDirectionInCasterSpace;
            // Because our space has been slid, we need to recover our supports using the IDs and the new space

            UnityEngine.Debug.Log($"Post-planar portal: A: {portalA.pos}, {portalA.id}, B: {portalB.pos}, {portalB.id}, C: {portalC.pos}, {portalC.id}");

            portalA = MinkowskiSupports.Get3DSupportFromPlanar(in caster, in target, in slidTargetInCasterSpace, in portalA);
            portalB = MinkowskiSupports.Get3DSupportFromPlanar(in caster, in target, in slidTargetInCasterSpace, in portalB);
            portalC = MinkowskiSupports.Get3DSupportFromPlanar(in caster, in target, in slidTargetInCasterSpace, in portalC);

            UnityEngine.Debug.Log($"3D portal: A: {portalA.pos}, {portalA.id}, B: {portalB.pos}, {portalB.id}, C: {portalC.pos}, {portalC.id}");

            // Our planar supports might be interior supports due to the axis reduction.
            //portalA = GetSupport(caster, target, portalA.pos, slidTargetInCasterSpace);
            //portalB = GetSupport(caster, target, portalB.pos, slidTargetInCasterSpace);
            //portalC = GetSupport(caster, target, portalC.pos, slidTargetInCasterSpace);

            //UnityEngine.Debug.Log($"Corrected portal: A: {portalA.pos}, {portalA.id}, B: {portalB.pos}, {portalB.id}, C: {portalC.pos}, {portalC.id}");

            // Catch the case where the ray misses the portal triangle. It is frustrating that it happens, but this should catch it.
            DoMprPortalSearchDebug(in caster, in target, in slidTargetInCasterSpace, normalizedCastDirectionInCasterSpace, ref portalA, ref portalB, ref portalC,
                                   ref somethingWentWrong);

            UnityEngine.Debug.Log($"Post-search portal: A: {portalA.pos}, {portalA.id}, B: {portalB.pos}, {portalB.id}, C: {portalC.pos}, {portalC.id}");

            float mprDistance = DoMprRefine3DDebug(in caster,
                                                   in target,
                                                   in slidTargetInCasterSpace,
                                                   normalizedCastDirectionInCasterSpace,
                                                   portalA,
                                                   portalB,
                                                   portalC,
                                                   ref somethingWentWrong);

            UnityEngine.Debug.Log($"mprDistance: {mprDistance}");

            distanceOfImpact = slideDistance - mprDistance;
            return distanceOfImpact <= maxCastDistance;
        }

        private const float k_missDistance = 2f;

        private static void DoMprPortalSearch(in Collider colliderA,
                                              in Collider colliderB,
                                              in RigidTransform bInASpace,
                                              float3 normalizedSearchDirectionInASpace,
                                              ref SupportPoint portalA,
                                              ref SupportPoint portalB,
                                              ref SupportPoint portalC,
                                              ref bool somethingWentWrong)
        {
            int iters = 100;
            while (iters > 0)
            {
                iters--;

                simdFloat3 portalVerts = new simdFloat3(portalA.pos, portalB.pos, portalC.pos, portalA.pos);
                simdFloat3 normals     = simd.cross(portalVerts, portalVerts.bcad);
                normals                = simd.select(normals, -normals, simd.dot(normals, portalVerts.cabd) < 0f);
                bool4 isWrongSide      = simd.dot(normals, normalizedSearchDirectionInASpace) < 0f;
                if (isWrongSide.x)
                {
                    portalC = MinkowskiSupports.GetSupport(in colliderA, in colliderB, -normals.a, in bInASpace);
                }
                else if (isWrongSide.y)
                {
                    portalA = MinkowskiSupports.GetSupport(in colliderA, in colliderB, -normals.b, in bInASpace);
                }
                else if (isWrongSide.z)
                {
                    portalB = MinkowskiSupports.GetSupport(in colliderA, in colliderB, -normals.c, in bInASpace);
                }
                else
                {
                    return;
                }
                if (portalA.id == portalB.id || portalA.id == portalC.id || portalB.id == portalC.id)
                {
                    break;
                }
            }

            somethingWentWrong |= true;
        }

        private static float DoMprRefine3D(in Collider colliderA,
                                           in Collider colliderB,
                                           in RigidTransform bInASpace,
                                           float3 normalizedSearchDirectionInASpace,
                                           SupportPoint portalA,
                                           SupportPoint portalB,
                                           SupportPoint portalC,
                                           ref bool somethingWentWrong)
        {
            const float k_smallNormal  = 1e-4f;
            const float k_normalScaler = 1000f;

            // Triangles OAB, OBC, and OCA should surround the ray.
            // Triangle ABC is a portal that the ray passes through.
            // The triangle ABC normal should point aligned with the ray, facing the opposite of the origin
            float3 portalUnscaledNormal = math.cross(portalB.pos - portalA.pos, portalC.pos - portalB.pos);
            if (math.all(portalUnscaledNormal == 0f))
                portalUnscaledNormal = normalizedSearchDirectionInASpace;
            portalUnscaledNormal     = math.select(portalUnscaledNormal, -portalUnscaledNormal, math.dot(portalUnscaledNormal, normalizedSearchDirectionInASpace) < 0f);
            // If our normal gets small, scale it up.
            portalUnscaledNormal = math.select(portalUnscaledNormal, portalUnscaledNormal * k_normalScaler, math.all(math.abs(portalUnscaledNormal) < k_smallNormal));

            var replacedPoint = portalA;
            int iters         = 100;
            while (iters > 0)
            {
                iters--;

                // Find a new support out through the portal.
                var newSupport = MinkowskiSupports.GetSupport(in colliderA, in colliderB, portalUnscaledNormal, in bInASpace);

                // If the new support is actually one of our portal supports, then terminate.
                uint3 ids = new uint3(portalA.id, portalB.id, portalC.id);
                if (math.any(newSupport.id == ids))
                {
                    break;
                }

                // This new support creates three new triangles connecting to the origin and each of the portal vertices.
                simdFloat3 portalVerts = new simdFloat3(portalA.pos, portalB.pos, portalC.pos, portalA.pos);
                simdFloat3 newPlanes   = simd.cross(portalVerts, newSupport.pos);
                newPlanes              = simd.select(newPlanes, newPlanes * k_normalScaler, simd.cmaxxyz(simd.abs(newPlanes)) < k_smallNormal);
                // Align the new planes to point towards the next portal vertex
                newPlanes = simd.select(newPlanes, -newPlanes, simd.dot(newPlanes, portalVerts.bcaa) < 0f);
                // Find which side of each plane the ray is on
                bool4 pointsTowardsRay = simd.dot(newPlanes, normalizedSearchDirectionInASpace) >= 0f;
                // It is possible that our new support could be outside the portal. So do the backward case as well.
                newPlanes                       = simd.select(newPlanes, -newPlanes, simd.dot(newPlanes, portalVerts.caba) < 0f);
                bool4 pointsTowardsRayBackwards = simd.dot(newPlanes, normalizedSearchDirectionInASpace) >= 0f;
                // Split up exclusion zones such that those in front of A and behind B exclude C, and likewise forward
                bool4 isInZone = (pointsTowardsRay & !pointsTowardsRay.yzxw) | (pointsTowardsRayBackwards.yzxw & !pointsTowardsRayBackwards);

                // Prevent ping-ponging due to the ray being on the border of the portal.
                bool terminateAfterAssign = newSupport.id == replacedPoint.id;
                // Update our portal triangle
                if (isInZone.x)
                {
                    replacedPoint = portalC;
                    portalC       = newSupport;
                }
                else if (isInZone.y)
                {
                    replacedPoint = portalA;
                    portalA       = newSupport;
                }
                else if (isInZone.z)
                {
                    replacedPoint = portalB;
                    portalB       = newSupport;
                }
                else
                {
                    // Our portal has degenerated or we have a new support really close to the ray
                    // We either got three planes pointing towards the ray or three planes pointing against it.
                    // In either case, replace with the point least aligned to the ray
                    float3 alignment = simd.dot(portalVerts, normalizedSearchDirectionInASpace).xyz;
                    float  min       = math.cmin(alignment);
                    if (alignment.x == min)
                    {
                        replacedPoint = portalA;
                        portalA       = newSupport;
                    }
                    else if (alignment.y == min)
                    {
                        replacedPoint = portalB;
                        portalB       = newSupport;
                    }
                    else
                    {
                        replacedPoint = portalC;
                        portalC       = newSupport;
                    }
                }

                if (terminateAfterAssign)
                {
                    break;
                }

                // Check that we didn't make backward progress
                /*if (math.dot(newSupport.pos, normalizedSearchDirectionInASpace) < math.dot(replacedPoint.pos, normalizedSearchDirectionInASpace))
                   {
                    float oldTrianglePerimeter = math.csum(simd.length(portalVerts - portalVerts.bcad));
                    float newTrianglePerimeter = math.distance(portalA.pos, portalB.pos) + math.distance(portalB.pos, portalC.pos) + math.distance(portalC.pos, portalA.pos);
                    if (newTrianglePerimeter > oldTrianglePerimeter)
                    {
                        // The simplex went the wrong way. Undo and exit
                        if (isInZone.x)
                        {
                            portalC = replacedPoint;
                        }
                        else if (isInZone.y)
                        {
                            portalA = replacedPoint;
                        }
                        else if (isInZone.z)
                        {
                            portalB = replacedPoint;
                        }
                        else
                        {
                            if (degenerateRestoreTarget == 0)
                                portalA = replacedPoint;
                            else if (degenerateRestoreTarget == 1)
                                portalB = replacedPoint;
                            else
                                portalC = replacedPoint;
                        }
                        break;
                    }
                   }*/

                // Update the portal's normal
                var pendingNormal    = math.cross(portalB.pos - portalA.pos, portalC.pos - portalB.pos);
                portalUnscaledNormal = math.select(pendingNormal, portalUnscaledNormal, math.all(pendingNormal == 0f));
                portalUnscaledNormal = math.select(portalUnscaledNormal, -portalUnscaledNormal, math.dot(portalUnscaledNormal, normalizedSearchDirectionInASpace) < 0f);
                // If our normal gets small, scale it up.
                portalUnscaledNormal = math.select(portalUnscaledNormal, portalUnscaledNormal * k_normalScaler, math.all(math.abs(portalUnscaledNormal) < k_smallNormal));
            }
            if (iters <= 0)
            {
                somethingWentWrong |= true;
            }

            // Our portal is now the surface triangle of the CSO our ray passes through.
            // Find the distance to the plane of the portal and return.
            // We don't use triangle raycast here because precision issues could cause our ray to miss.
            Plane plane = mathex.PlaneFrom(portalA.pos, portalB.pos - portalA.pos, portalC.pos - portalA.pos);
            float denom = math.dot(plane.normal, normalizedSearchDirectionInASpace);
            if (math.abs(denom) < math.EPSILON)
            {
                // The triangle is coplanar with the ray.

                if (math.all(plane.normal == 0f))
                {
                    // Our triangle is a line segment. Find the extreme points.
                    float distAB   = math.distancesq(portalA.pos, portalB.pos);
                    float distBC   = math.distancesq(portalB.pos, portalC.pos);
                    float distCA   = math.distancesq(portalC.pos, portalA.pos);
                    int   winner   = math.select(0, 1, distBC > distAB);
                    float bestDist = math.max(distAB, distBC);
                    winner         = math.select(winner, 2, distCA > bestDist);
                    float3 targetA = default;
                    float3 targetB = default;
                    switch (winner)
                    {
                        case 0:
                            targetA = portalA.pos;
                            targetB = portalB.pos;
                            break;
                        case 1:
                            targetA = portalB.pos;
                            targetB = portalC.pos;
                            break;
                        case 2:
                            targetA = portalC.pos;
                            targetB = portalA.pos;
                            break;
                    }
                    float3 farthestAway = math.select(targetA, targetB, math.lengthsq(targetB) > math.lengthsq(targetA));
                    float3 rayExtents   = normalizedSearchDirectionInASpace * math.dot(farthestAway, normalizedSearchDirectionInASpace) * 2f;
                    CapsuleCapsule.SegmentSegment(0f, rayExtents, targetA, targetB - targetA, out var closestA, out _, out _);
                    return math.length(closestA);
                }
                // We have a real triangle with a real normal
                simdFloat3 triangle  = new simdFloat3(portalA.pos, portalB.pos, portalC.pos, portalC.pos);
                simdFloat3 posPoints = triangle + plane.normal;
                simdFloat3 negPoints = triangle - plane.normal;
                float      rayLength = math.cmax(simd.length(triangle)) * 2f;
                float3     rayStart  = rayLength * normalizedSearchDirectionInASpace;
                Ray        ray       = new Ray(rayStart, 0f);
                simdFloat3 quadAB    = simd.shuffle(negPoints,
                                                    posPoints,
                                                    math.ShuffleComponent.LeftX,
                                                    math.ShuffleComponent.RightX,
                                                    math.ShuffleComponent.RightY,
                                                    math.ShuffleComponent.LeftY);
                simdFloat3 quadBC = simd.shuffle(negPoints,
                                                 posPoints,
                                                 math.ShuffleComponent.LeftY,
                                                 math.ShuffleComponent.RightY,
                                                 math.ShuffleComponent.RightZ,
                                                 math.ShuffleComponent.LeftZ);
                simdFloat3 quadCA = simd.shuffle(negPoints,
                                                 posPoints,
                                                 math.ShuffleComponent.LeftZ,
                                                 math.ShuffleComponent.RightZ,
                                                 math.ShuffleComponent.LeftX,
                                                 math.ShuffleComponent.RightX);
                bool3  hit       = default;
                float3 fractions = default;
                hit.x            = PointRayTriangle.RaycastQuad(in ray, in quadAB, out fractions.x);
                hit.y            = PointRayTriangle.RaycastQuad(in ray, in quadBC, out fractions.y);
                hit.z            = PointRayTriangle.RaycastQuad(in ray, in quadCA, out fractions.z);
                fractions        = math.select(float.MaxValue, fractions, hit);
                return (1f - math.cmin(fractions)) * rayLength;
            }
            return math.abs(plane.distanceToOrigin / denom);
        }

        private static bool DoPlanarMpr(in Collider colliderA,
                                        in Collider colliderB,
                                        in RigidTransform bInASpace,
                                        float3 planeNormal,
                                        float3 searchStart,
                                        out SupportPoint planarSupportIdA,
                                        out SupportPoint planarSupportIdB,
                                        ref bool somethingWentWrong)
        {
            const float k_smallNormal  = 1e-4f;
            const float k_normalScaler = 1000f;

            float3 center = searchStart - math.project(searchStart, planeNormal);
            float3 ray    = -center;

            var portalA = MinkowskiSupports.GetPlanarSupport(in colliderA, in colliderB, -center, in bInASpace, planeNormal);
            // We are sliding everything such that (0, 0, 0) is the ray start and the ray is the original origin
            portalA.pos              += ray;
            planarSupportIdA          = portalA;
            float3 normalizedPortalA  = math.normalizesafe(portalA.pos);
            float3 normalizedRay      = math.normalize(ray);
            float3 projectionPoint    = math.dot(normalizedPortalA, normalizedRay) * normalizedRay;
            float3 searchDirection    = projectionPoint - normalizedPortalA;
            // Our initial support could actually be aligned to the ray, in which case our ray is the zero vector, which gets normalized to NaN
            // We catch that here and force the next condition to be true to handle it.
            if (math.all(ray == 0f))
                searchDirection = 0f;

            // If we don't have a search direction, then our first support is the vertex aligned along the ray (rare).
            // But more common is that precision issues cause the search direction to be an imperfect scale of the ray.
            // That's the same issue but a lot harder to catch. So we compare it to the orthogonal to catch it, since
            // the searchDirection should always be orthogonal to the ray anyways.
            if (math.all(searchDirection == 0f) || math.abs(math.dot(searchDirection, ray)) >= math.abs(math.dot(searchDirection, math.cross(ray, planeNormal))))
            {
                if (math.lengthsq(portalA.pos) >= math.lengthsq(ray))
                {
                    if (math.all(searchDirection == 0f))
                    {
                        mathex.GetDualPerpendicularNormalized(planeNormal, out var dirA, out var dirB);
                        planarSupportIdA = MinkowskiSupports.GetPlanarSupport(in colliderA, in colliderB, dirA, in bInASpace, planeNormal);
                        planarSupportIdB = MinkowskiSupports.GetPlanarSupport(in colliderA, in colliderB, dirB, in bInASpace, planeNormal);
                    }
                    else
                    {
                        // We still need a second planar support, so just find a cross product and test both directions
                        searchDirection  = math.cross(searchDirection, planeNormal);
                        planarSupportIdB = MinkowskiSupports.GetPlanarSupport(in colliderA, in colliderB, searchDirection, in bInASpace, planeNormal);
                        if (planarSupportIdB.id == planarSupportIdA.id)
                        {
                            planarSupportIdB = MinkowskiSupports.GetPlanarSupport(in colliderA, in colliderB, -searchDirection, in bInASpace, planeNormal);
                            if (planarSupportIdB.id == planarSupportIdA.id)
                            {
                                somethingWentWrong |= true;
                            }
                        }
                    }
                    return true;
                }
                else
                {
                    planarSupportIdB = default;
                    return false;
                }
            }
            // If our search direction is really small, scale it up.
            searchDirection = math.select(searchDirection, searchDirection * k_normalScaler, math.all(math.abs(searchDirection) < k_smallNormal));
            // Find a new support point orthogonal to our ray away from the first support point
            var portalB       = MinkowskiSupports.GetPlanarSupport(in colliderA, in colliderB, searchDirection, in bInASpace, planeNormal);
            portalB.pos      += ray;
            planarSupportIdB  = portalB;
            // Get the portal normal facing away from the center
            float3 portalUnscaledNormal = math.cross(portalB.pos - portalA.pos, planeNormal);
            portalUnscaledNormal        = math.select(portalUnscaledNormal, portalUnscaledNormal * k_normalScaler, math.all(math.abs(portalUnscaledNormal) < k_smallNormal));
            portalUnscaledNormal        = math.select(portalUnscaledNormal, -portalUnscaledNormal, math.dot(portalUnscaledNormal, ray) < 0f);

            // If the segment from the ray endpoint to a portal endpoint is aligned with the portal normal, then the portal is beyond the ray endpoint.
            if (math.dot(portalA.pos - ray, portalUnscaledNormal) >= 0f)
                return true;

            // Todo: Set a max iterations and assertion to prevent freezes if NaNs happen.
            int iters = 100;
            while (iters > 0)
            {
                iters--;

                // Find a new support out through the portal.
                var newSupport  = MinkowskiSupports.GetSupport(in colliderA, in colliderB, portalUnscaledNormal, in bInASpace);
                newSupport.pos += ray;
                // If the new support is actually one of our portal supports, then terminate.
                if (newSupport.id == portalA.id || newSupport.id == portalB.id)
                    break;
                // Create the split plane
                float3 newPlane = math.cross(newSupport.pos, planeNormal);
                newPlane        = math.select(newPlane, newPlane * k_normalScaler, math.all(math.abs(newPlane) < k_smallNormal));
                // Point the plane towards B
                newPlane = math.select(newPlane, -newPlane, math.dot(newPlane, portalB.pos) < 0f);

                // Find which side of the plane the ray is on and replace the opposite portal point
                if (math.dot(ray, newPlane) > 0f)
                    portalA = newSupport;
                else
                    portalB = newSupport;

                // Update the portal's normal
                portalUnscaledNormal = math.cross(portalB.pos - portalA.pos, planeNormal);
                portalUnscaledNormal = math.select(portalUnscaledNormal, portalUnscaledNormal * k_normalScaler, math.all(math.abs(portalUnscaledNormal) < k_smallNormal));
                portalUnscaledNormal = math.select(portalUnscaledNormal, -portalUnscaledNormal, math.dot(portalUnscaledNormal, ray) < 0f);
                // If the segment from the ray endpoint to a portal endpoint is aligned with the portal normal, then the portal is beyond the ray endpoint.
                if (math.dot(portalA.pos - ray, portalUnscaledNormal) >= 0f)
                {
                    planarSupportIdA = portalA;
                    planarSupportIdB = portalB;
                    return true;
                }
            }
            if (iters <= 0)
            {
                somethingWentWrong |= true;
            }

            // By this point, we found the portal and our ray endpoint is outside it, which means no collision.
            return false;
        }

        #region DebugCast

        private static void DoMprPortalSearchDebug(in Collider colliderA,
                                                   in Collider colliderB,
                                                   in RigidTransform bInASpace,
                                                   float3 normalizedSearchDirectionInASpace,
                                                   ref SupportPoint portalA,
                                                   ref SupportPoint portalB,
                                                   ref SupportPoint portalC,
                                                   ref bool somethingWentWrong)
        {
            UnityEngine.Debug.Log(
                $"Enter DoMprPortalSearch, normalizedSearchDirectionInASpace: {normalizedSearchDirectionInASpace} A: {portalA.pos}, {portalA.id}, B: {portalB.pos}, {portalB.id}, C: {portalC.pos}, {portalC.id}");
            int iters = 100;
            while (iters > 0)
            {
                iters--;

                simdFloat3 portalVerts = new simdFloat3(portalA.pos, portalB.pos, portalC.pos, portalA.pos);
                simdFloat3 normals     = simd.cross(portalVerts, portalVerts.bcad);
                normals                = simd.select(normals, -normals, simd.dot(normals, portalVerts.cabd) < 0f);
                bool4 isWrongSide      = simd.dot(normals, normalizedSearchDirectionInASpace) < 0f;
                if (isWrongSide.x)
                {
                    portalC = MinkowskiSupports.GetSupport(in colliderA, in colliderB, -normals.a, in bInASpace);
                }
                else if (isWrongSide.y)
                {
                    portalA = MinkowskiSupports.GetSupport(in colliderA, in colliderB, -normals.b, in bInASpace);
                }
                else if (isWrongSide.z)
                {
                    portalB = MinkowskiSupports.GetSupport(in colliderA, in colliderB, -normals.c, in bInASpace);
                }
                else
                {
                    UnityEngine.Debug.Log($"normals: {normals.a}, {normals.b}, {normals.c}, dots: {simd.dot(normals, normalizedSearchDirectionInASpace)}");
                    UnityEngine.Debug.Log("DoMprPortalSearch exited normally.");
                    return;
                }
                UnityEngine.Debug.Log(
                    $"iters: {iters}, isWrongSide: {isWrongSide}, A: {portalA.pos}, {portalA.id}, B: {portalB.pos}, {portalB.id}, C: {portalC.pos}, {portalC.id}");
                if (portalA.id == portalB.id || portalA.id == portalC.id || portalB.id == portalC.id)
                {
                    UnityEngine.Debug.Log("Portal degenerated.");
                    break;
                }
            }
            UnityEngine.Debug.Log("DoMprPortalSearch exited from too many iterations  or portal degeneration.");
            somethingWentWrong |= true;
        }

        private static float DoMprRefine3DDebug(in Collider colliderA,
                                                in Collider colliderB,
                                                in RigidTransform bInASpace,
                                                float3 normalizedSearchDirectionInASpace,
                                                SupportPoint portalA,
                                                SupportPoint portalB,
                                                SupportPoint portalC,
                                                ref bool somethingWentWrong)
        {
            const float k_smallNormal  = 1e-4f;
            const float k_normalScaler = 1000f;

            UnityEngine.Debug.Log(
                $"Entering DoMprRefine3D. normalizedSearchDirectionInASpace: {normalizedSearchDirectionInASpace}, somethingWentWrong: {somethingWentWrong}");

            // Triangles OAB, OBC, and OCA should surround the ray.
            // Triangle ABC is a portal that the ray passes through.
            // The triangle ABC normal should point aligned with the ray, facing the opposite of the origin
            float3 portalUnscaledNormal = math.cross(portalB.pos - portalA.pos, portalC.pos - portalB.pos);
            if (math.all(portalUnscaledNormal == 0f))
                portalUnscaledNormal = normalizedSearchDirectionInASpace;
            portalUnscaledNormal     = math.select(portalUnscaledNormal, -portalUnscaledNormal, math.dot(portalUnscaledNormal, normalizedSearchDirectionInASpace) < 0f);
            // If our normal gets small, scale it up.
            portalUnscaledNormal = math.select(portalUnscaledNormal, portalUnscaledNormal * k_normalScaler, math.all(math.abs(portalUnscaledNormal) < k_smallNormal));

            var replacedPoint = portalA;
            int iters         = 100;
            while (iters > 0)
            {
                iters--;

                // Find a new support out through the portal.
                var newSupport = MinkowskiSupports.GetSupport(in colliderA, in colliderB, portalUnscaledNormal, in bInASpace);

                UnityEngine.Debug.Log($"iter: {iters}, portalUnscaledNormal: {portalUnscaledNormal}");
                UnityEngine.Debug.Log(
                    $"newSupport: {newSupport.pos} - {newSupport.id}, A: {portalA.pos}, {portalA.id}, B: {portalB.pos}, {portalB.id}, C: {portalC.pos}, {portalC.id}");

                // If the new support is actually one of our portal supports, then terminate.
                uint3 ids = new uint3(portalA.id, portalB.id, portalC.id);
                if (math.any(newSupport.id == ids))
                {
                    UnityEngine.Debug.Log("New support matches portal support. Exiting loop.");
                    break;
                }

                // This new support creates three new triangles connecting to the origin and each of the portal vertices.
                simdFloat3 portalVerts = new simdFloat3(portalA.pos, portalB.pos, portalC.pos, portalA.pos);
                simdFloat3 newPlanes   = simd.cross(portalVerts, newSupport.pos);
                newPlanes              = simd.select(newPlanes, newPlanes * k_normalScaler, simd.cmaxxyz(simd.abs(newPlanes)) < k_smallNormal);
                // Align the new planes to point towards the next portal vertex
                newPlanes = simd.select(newPlanes, -newPlanes, simd.dot(newPlanes, portalVerts.bcaa) < 0f);
                // Find which side of each plane the ray is on
                bool4 pointsTowardsRay = simd.dot(newPlanes, normalizedSearchDirectionInASpace) >= 0f;
                // It is possible that our new support could be outside the portal. So do the backward case as well.
                newPlanes                       = simd.select(newPlanes, -newPlanes, simd.dot(newPlanes, portalVerts.caba) < 0f);
                bool4 pointsTowardsRayBackwards = simd.dot(newPlanes, normalizedSearchDirectionInASpace) >= 0f;
                // Split up exclusion zones such that those in front of A and behind B exclude C, and likewise forward
                bool4 isInZone = (pointsTowardsRay & !pointsTowardsRay.yzxw) | (pointsTowardsRayBackwards.yzxw & !pointsTowardsRayBackwards);

                // Prevent ping-ponging due to the ray being on the border of the portal.
                bool terminateAfterAssign = newSupport.id == replacedPoint.id;
                // Update our portal triangle
                if (isInZone.x)
                {
                    replacedPoint = portalC;
                    portalC       = newSupport;
                }
                else if (isInZone.y)
                {
                    replacedPoint = portalA;
                    portalA       = newSupport;
                }
                else if (isInZone.z)
                {
                    replacedPoint = portalB;
                    portalB       = newSupport;
                }
                else
                {
                    // Our portal has degenerated or we have a new support really close to the ray
                    // We either got three planes pointing towards the ray or three planes pointing against it.
                    // In either case, just pick an arbitrary point to replace.
                    UnityEngine.Debug.Log($"Degenerated portal. pointsTowardsRay: {pointsTowardsRay}, isInZone: {isInZone}");

                    // Replace with the point least aligned to the ray
                    float3 alignment = simd.dot(portalVerts, normalizedSearchDirectionInASpace).xyz;
                    float  min       = math.cmin(alignment);
                    if (alignment.x == min)
                    {
                        replacedPoint = portalA;
                        portalA       = newSupport;
                    }
                    else if (alignment.y == min)
                    {
                        replacedPoint = portalB;
                        portalB       = newSupport;
                    }
                    else
                    {
                        replacedPoint = portalC;
                        portalC       = newSupport;
                    }
                }

                if (terminateAfterAssign)
                {
                    UnityEngine.Debug.Log("Exiting loop due to ping-pong");
                    break;
                }

                // Check that we didn't make backward progress
                /*if (math.dot(newSupport.pos, normalizedSearchDirectionInASpace) < math.dot(replacedPoint.pos, normalizedSearchDirectionInASpace))
                   {
                    float oldTrianglePerimeter = math.csum(simd.length(portalVerts - portalVerts.bcad));
                    float newTrianglePerimeter = math.distance(portalA.pos, portalB.pos) + math.distance(portalB.pos, portalC.pos) + math.distance(portalC.pos, portalA.pos);
                    if (newTrianglePerimeter > oldTrianglePerimeter)
                    {
                        // The simplex went the wrong way. Undo and exit
                        UnityEngine.Debug.Log("Inverted simplex. Restoring point and exiting loop.");
                        UnityEngine.Debug.Log($"isInZone: {isInZone}, oldTrianglePerimeter: {oldTrianglePerimeter}, newTrianglePerimeter: {newTrianglePerimeter}");
                        if (isInZone.x)
                        {
                            portalC = replacedPoint;
                        }
                        else if (isInZone.y)
                        {
                            portalA = replacedPoint;
                        }
                        else if (isInZone.z)
                        {
                            portalB = replacedPoint;
                        }
                        else
                        {
                            if (degenerateRestoreTarget == 0)
                                portalA = replacedPoint;
                            else if (degenerateRestoreTarget == 1)
                                portalB = replacedPoint;
                            else
                                portalC = replacedPoint;
                        }
                        break;
                    }
                   }*/

                // Update the portal's normal
                var pendingNormal    = math.cross(portalB.pos - portalA.pos, portalC.pos - portalB.pos);
                portalUnscaledNormal = math.select(pendingNormal, portalUnscaledNormal, math.all(pendingNormal == 0f));
                portalUnscaledNormal = math.select(portalUnscaledNormal, -portalUnscaledNormal, math.dot(portalUnscaledNormal, normalizedSearchDirectionInASpace) < 0f);
                // If our normal gets small, scale it up.
                portalUnscaledNormal = math.select(portalUnscaledNormal, portalUnscaledNormal * k_normalScaler, math.all(math.abs(portalUnscaledNormal) < k_smallNormal));
            }
            if (iters <= 0)
            {
                UnityEngine.Debug.Log("Exhausted iterations in 3D refining portal");
                somethingWentWrong |= true;
            }

            UnityEngine.Debug.Log($"Portal after loop. A: {portalA.pos}, {portalA.id}, B: {portalB.pos}, {portalB.id}, C: {portalC.pos}, {portalC.id}");

            // Our portal is now the surface triangle of the CSO our ray passes through.
            // Find the distance to the plane of the portal and return.
            // We don't use triangle raycast here because precision issues could cause our ray to miss.
            Plane plane = mathex.PlaneFrom(portalA.pos, portalB.pos - portalA.pos, portalC.pos - portalA.pos);
            float denom = math.dot(plane.normal, normalizedSearchDirectionInASpace);
            UnityEngine.Debug.Log($"plane: {plane.normal}, {plane.distanceToOrigin}, denom: {denom}");
            if (math.abs(denom) < math.EPSILON)
            {
                // The triangle is coplanar with the ray.

                if (math.all(plane.normal == 0f))
                {
                    // Our triangle is a line segment. Find the extreme points.
                    float distAB   = math.distancesq(portalA.pos, portalB.pos);
                    float distBC   = math.distancesq(portalB.pos, portalC.pos);
                    float distCA   = math.distancesq(portalC.pos, portalA.pos);
                    int   winner   = math.select(0, 1, distBC > distAB);
                    float bestDist = math.max(distAB, distBC);
                    winner         = math.select(winner, 2, distCA > bestDist);
                    float3 targetA = default;
                    float3 targetB = default;
                    switch (winner)
                    {
                        case 0:
                            targetA = portalA.pos;
                            targetB = portalB.pos;
                            break;
                        case 1:
                            targetA = portalB.pos;
                            targetB = portalC.pos;
                            break;
                        case 2:
                            targetA = portalC.pos;
                            targetB = portalA.pos;
                            break;
                    }
                    float3 farthestAway = math.select(targetA, targetB, math.lengthsq(targetB) > math.lengthsq(targetA));
                    float3 rayExtents   = normalizedSearchDirectionInASpace * math.dot(farthestAway, normalizedSearchDirectionInASpace) * 2f;
                    CapsuleCapsule.SegmentSegment(0f, rayExtents, targetA, targetB - targetA, out var closestA, out _, out _);
                    UnityEngine.Debug.Log($"Coplanar portal is line. winner: {winner}, closestA: {closestA}");
                    return math.length(closestA);
                }
                // We have a real triangle with a real normal
                simdFloat3 triangle  = new simdFloat3(portalA.pos, portalB.pos, portalC.pos, portalC.pos);
                simdFloat3 posPoints = triangle + plane.normal;
                simdFloat3 negPoints = triangle - plane.normal;
                float      rayLength = math.cmax(simd.length(triangle)) * 2f;
                float3     rayStart  = rayLength * normalizedSearchDirectionInASpace;
                Ray        ray       = new Ray(rayStart, 0f);
                simdFloat3 quadAB    = simd.shuffle(negPoints,
                                                    posPoints,
                                                    math.ShuffleComponent.LeftX,
                                                    math.ShuffleComponent.RightX,
                                                    math.ShuffleComponent.RightY,
                                                    math.ShuffleComponent.LeftY);
                simdFloat3 quadBC = simd.shuffle(negPoints,
                                                 posPoints,
                                                 math.ShuffleComponent.LeftY,
                                                 math.ShuffleComponent.RightY,
                                                 math.ShuffleComponent.RightZ,
                                                 math.ShuffleComponent.LeftZ);
                simdFloat3 quadCA = simd.shuffle(negPoints,
                                                 posPoints,
                                                 math.ShuffleComponent.LeftZ,
                                                 math.ShuffleComponent.RightZ,
                                                 math.ShuffleComponent.LeftX,
                                                 math.ShuffleComponent.RightX);
                bool3  hit       = default;
                float3 fractions = default;
                hit.x            = PointRayTriangle.RaycastQuad(in ray, in quadAB, out fractions.x);
                hit.y            = PointRayTriangle.RaycastQuad(in ray, in quadBC, out fractions.y);
                hit.z            = PointRayTriangle.RaycastQuad(in ray, in quadCA, out fractions.z);
                fractions        = math.select(float.MaxValue, fractions, hit);
                UnityEngine.Debug.Log("Coplanar portal is triangle. hit: {hit}");
                return (1f - math.cmin(fractions)) * rayLength;
            }
            return math.abs(plane.distanceToOrigin / denom);
        }

        private static bool DoPlanarMprDebug(in Collider colliderA,
                                             in Collider colliderB,
                                             in RigidTransform bInASpace,
                                             float3 planeNormal,
                                             float3 searchStart,
                                             out SupportPoint planarSupportIdA,
                                             out SupportPoint planarSupportIdB,
                                             ref bool somethingWentWrong)
        {
            const float k_smallNormal  = 1e-4f;
            const float k_normalScaler = 1000f;

            float3 center = searchStart - math.project(searchStart, planeNormal);
            float3 ray    = -center;

            var portalA = MinkowskiSupports.GetPlanarSupport(in colliderA, in colliderB, -center, in bInASpace, planeNormal);
            // We are sliding everything such that (0, 0, 0) is the ray start and the ray is the original origin
            portalA.pos              += ray;
            planarSupportIdA          = portalA;
            float3 normalizedPortalA  = math.normalizesafe(portalA.pos);
            float3 normalizedRay      = math.normalize(ray);
            float3 projectionPoint    = math.dot(normalizedPortalA, normalizedRay) * normalizedRay;
            float3 searchDirection    = projectionPoint - normalizedPortalA;
            // Our initial support could actually be aligned to the ray, in which case our ray is the zero vector, which gets normalized to NaN
            // We catch that here and force the next condition to be true to handle it.
            if (math.all(ray == 0f))
                searchDirection = 0f;

            UnityEngine.Debug.Log(
                $"Entering DoPlanarMpr. planeNormal: {planeNormal}, searchStart: {searchStart}, ray: {ray}, normalizedRay: {normalizedRay}, portalA: {portalA.pos}, {portalA.id}, searchDirection: {searchDirection}");
            // If we don't have a search direction, then our first support is the vertex aligned along the ray (rare).
            // But more common is that precision issues cause the search direction to be an imperfect scale of the ray.
            // That's the same issue but a lot harder to catch. So we compare it to the orthogonal to catch it, since
            // the searchDirection should always be orthogonal to the ray anyways.
            if (math.all(searchDirection == 0f) || math.abs(math.dot(searchDirection, ray)) >= math.abs(math.dot(searchDirection, math.cross(ray, planeNormal))))
            {
                if (math.lengthsq(portalA.pos) >= math.lengthsq(ray))
                {
                    if (math.all(searchDirection == 0f))
                    {
                        UnityEngine.Debug.Log("Search direction is 0. Getting both planar supports and exiting.");
                        mathex.GetDualPerpendicularNormalized(planeNormal, out var dirA, out var dirB);
                        planarSupportIdA = MinkowskiSupports.GetPlanarSupport(colliderA, colliderB, dirA, in bInASpace, planeNormal);
                        planarSupportIdB = MinkowskiSupports.GetPlanarSupport(colliderA, colliderB, dirB, in bInASpace, planeNormal);
                    }
                    else
                    {
                        UnityEngine.Debug.Log("Support aligned with ray and is beyond the ray. Getting second planar support and exiting.");
                        // We still need a second planar support, so just find a cross product and test both directions
                        searchDirection  = math.cross(searchDirection, planeNormal);
                        planarSupportIdB = MinkowskiSupports.GetPlanarSupport(colliderA, colliderB, searchDirection, in bInASpace, planeNormal);
                        if (planarSupportIdB.id == planarSupportIdA.id)
                        {
                            planarSupportIdB = MinkowskiSupports.GetPlanarSupport(colliderA, colliderB, -searchDirection, in bInASpace, planeNormal);
                            if (planarSupportIdB.id == planarSupportIdA.id)
                            {
                                UnityEngine.Debug.Log("Can't find orthogonal support.");
                                somethingWentWrong |= true;
                            }
                        }
                    }
                    return true;
                }
                else
                {
                    planarSupportIdB = default;
                    UnityEngine.Debug.Log("Support aligned with ray and ray extends beyond it. Returning a miss.");
                    return false;
                }
            }
            // If our search direction is really small, scale it up.
            searchDirection = math.select(searchDirection, searchDirection * k_normalScaler, math.all(math.abs(searchDirection) < k_smallNormal));
            // Find a new support point orthogonal to our ray away from the first support point
            var portalB       = MinkowskiSupports.GetPlanarSupport(in colliderA, in colliderB, searchDirection, in bInASpace, planeNormal);
            portalB.pos      += ray;
            planarSupportIdB  = portalB;
            // Get the portal normal facing away from the center
            float3 portalUnscaledNormal = math.cross(portalB.pos - portalA.pos, planeNormal);
            portalUnscaledNormal        = math.select(portalUnscaledNormal, portalUnscaledNormal * k_normalScaler, math.all(math.abs(portalUnscaledNormal) < k_smallNormal));
            portalUnscaledNormal        = math.select(portalUnscaledNormal, -portalUnscaledNormal, math.dot(portalUnscaledNormal, ray) < 0f);

            UnityEngine.Debug.Log($"ray: {ray}, searchDirection: {searchDirection}, planarA: {portalA.pos}, {portalA.id}, planarB: {portalB.pos}, {portalB.id}");

            // If the segment from the ray endpoint to a portal endpoint is aligned with the portal normal, then the portal is beyond the ray endpoint.
            if (math.dot(portalA.pos - ray, portalUnscaledNormal) >= 0f)
                return true;

            // Todo: Set a max iterations and assertion to prevent freezes if NaNs happen.
            int iters = 100;
            while (iters > 0)
            {
                iters--;

                // Find a new support out through the portal.
                var newSupport  = MinkowskiSupports.GetSupport(in colliderA, in colliderB, portalUnscaledNormal, in bInASpace);
                newSupport.pos += ray;
                // If the new support is actually one of our portal supports, then terminate.
                if (newSupport.id == portalA.id || newSupport.id == portalB.id)
                    break;
                // Create the split plane
                float3 newPlane = math.cross(newSupport.pos, planeNormal);
                newPlane        = math.select(newPlane, newPlane * k_normalScaler, math.all(math.abs(newPlane) < k_smallNormal));
                // Point the plane towards B
                newPlane = math.select(newPlane, -newPlane, math.dot(newPlane, portalB.pos) < 0f);

                UnityEngine.Debug.Log(
                    $"iter: {iters}, newSupport: {newSupport.pos}, {newSupport.id}, planarA: {portalA.pos}, {portalA.id}, planarB: {portalB.pos}, {portalB.id}, newPlane: {newPlane}");

                // Find which side of the plane the ray is on and replace the opposite portal point
                if (math.dot(ray, newPlane) > 0f)
                    portalA = newSupport;
                else
                    portalB = newSupport;

                // Update the portal's normal
                portalUnscaledNormal = math.cross(portalB.pos - portalA.pos, planeNormal);
                portalUnscaledNormal = math.select(portalUnscaledNormal, portalUnscaledNormal * k_normalScaler, math.all(math.abs(portalUnscaledNormal) < k_smallNormal));
                portalUnscaledNormal = math.select(portalUnscaledNormal, -portalUnscaledNormal, math.dot(portalUnscaledNormal, ray) < 0f);
                // If the segment from the ray endpoint to a portal endpoint is aligned with the portal normal, then the portal is beyond the ray endpoint.
                if (math.dot(portalA.pos - ray, portalUnscaledNormal) >= 0f)
                {
                    planarSupportIdA = portalA;
                    planarSupportIdB = portalB;
                    UnityEngine.Debug.Log("Found hit. Returning.");
                    return true;
                }
            }
            if (iters <= 0)
            {
                UnityEngine.Debug.Log("Exhausted iterations in 2D");
                somethingWentWrong |= true;
            }

            // By this point, we found the portal and our ray endpoint is outside it, which means no collision.
            UnityEngine.Debug.Log("No hit found in 2D");
            return false;
        }
        #endregion
    }
}

