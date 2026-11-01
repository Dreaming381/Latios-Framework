using System.Runtime.CompilerServices;
using Latios.Calci;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class CapsuleCapsule
    {
        public static bool AreOverlapping(in CapsuleCollider capsuleA,
                                          in RigidTransform aTransform,
                                          in CapsuleCollider capsuleB,
                                          in RigidTransform bTransform)
        {
            return WithinDistance(in capsuleA, in aTransform, in capsuleB, in bTransform, 0f);
        }

        public static bool WithinDistance(in CapsuleCollider capsuleA,
                                          in RigidTransform aTransform,
                                          in CapsuleCollider capsuleB,
                                          in RigidTransform bTransform,
                                          float maxDistance)
        {
            var BinASpaceTransform = math.InverseTransformFast(in aTransform, in bTransform);
            var bInASpaceA         = math.transform(BinASpaceTransform, capsuleB.pointA);
            var bInASpaceB         = math.transform(BinASpaceTransform, capsuleB.pointB);

            SegmentSegment(capsuleA.pointA, capsuleA.pointB, bInASpaceA, bInASpaceB, out _, out _, out _, out var closestAToBPrecise);
            var threshold = capsuleA.radius + capsuleB.radius + maxDistance;
            return math.lengthsq(closestAToBPrecise) <= threshold * threshold && threshold >= 0f;
        }

        public static bool DistanceBetween(in CapsuleCollider capsuleA,
                                           in RigidTransform aTransform,
                                           in CapsuleCollider capsuleB,
                                           in RigidTransform bTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            // Transform everything into world-space relative to A
            var transformedA = new CapsuleCollider(math.rotate(aTransform.rot, capsuleA.pointA), math.rotate(aTransform.rot, capsuleA.pointB), capsuleA.radius);
            var transformedB = new CapsuleCollider(bTransform.pos - aTransform.pos + math.rotate(bTransform.rot, capsuleB.pointA),
                                                   bTransform.pos - aTransform.pos + math.rotate(bTransform.rot, capsuleB.pointB),
                                                   capsuleB.radius);
            var hit = CapsuleCapsuleDistance(in transformedA, in transformedB, maxDistance, out var localResult);
            result  = new ColliderDistanceResult
            {
                hitpointA         = localResult.hitpointA + aTransform.pos,
                hitpointB         = localResult.hitpointB + aTransform.pos,
                normalA           = localResult.normalA,
                normalB           = localResult.normalB,
                distance          = localResult.distance,
                subColliderIndexA = 0,
                subColliderIndexB = 0,
                featureCodeA      = localResult.featureCodeA,
                featureCodeB      = localResult.featureCodeB,
            };
            return hit;
        }

        // The current implementation constructs a CSO of the two capsules and raycasts against it.
        // This becomes a raycast vs quad and a raycast against four edge capsules. For isolated queries,
        // this seems fairly optimal. However, when multiple capsules can be processed in batch,
        // this link provides an alternative method which handles the swept line versus cylinder case:
        // https://www.gamedev.net/forums/topic/288963-swept-capsule-capsule-intersection-3d/
        // It is likely that the linked solution vectorizes better.
        public static bool ColliderCast(in CapsuleCollider capsuleToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in CapsuleCollider targetCapsule,
                                        in RigidTransform targetCapsuleTransform,
                                        out ColliderCastResult result)
        {
            var csoRadius    = capsuleToCast.radius + targetCapsule.radius;
            var casterA      = castStart.pos - targetCapsuleTransform.pos + math.rotate(castStart.rot, capsuleToCast.pointA);
            var casterB      = castStart.pos - targetCapsuleTransform.pos + math.rotate(castStart.rot, capsuleToCast.pointB);
            var targetA      = math.rotate(targetCapsuleTransform.rot, targetCapsule.pointA);
            var targetB      = math.rotate(targetCapsuleTransform.rot, targetCapsule.pointB);
            var cso          = new simdFloat3(casterA - targetA, casterA - targetB, casterB - targetB, casterB - targetA);
            var displacement = castEnd - castStart.pos;
            // If the target is ahead of the caster, than the Minkowski difference puts the CSO behind the origin, so our ray is reversed
            var ray = new Ray(0f, -displacement);

            bool hit = PointRayTriangle.RaycastRoundedQuad(in ray, in cso, csoRadius, out float fraction, out float3 normal);
            if (hit)
            {
                var hitDisplacement = fraction * displacement;
                SegmentSegment(casterA + hitDisplacement,
                               casterB + hitDisplacement,
                               targetA,
                               targetB,
                               out _,
                               out var closestOnTarget,
                               out _,
                               out var casterToTargetPrecise);
                // Taking the difference in segment points is more accurate than using the raycast normal, except for when the radii are both 0.
                normal = math.normalizesafe(casterToTargetPrecise, normal);
                result = new ColliderCastResult
                {
                    hitpoint                 = closestOnTarget - normal * targetCapsule.radius + targetCapsuleTransform.pos,
                    normalOnCaster           = normal,
                    normalOnTarget           = -normal,
                    subColliderIndexOnCaster = 0,
                    subColliderIndexOnTarget = 0,
                    distance                 = fraction * math.length(displacement)
                };
                return true;
            }
            result = default;
            return false;
        }

        public static UnitySim.ContactsBetweenResult UnityContactsBetween(in CapsuleCollider capsuleA,
                                                                          in RigidTransform aTransform,
                                                                          in CapsuleCollider capsuleB,
                                                                          in RigidTransform bTransform,
                                                                          in ColliderDistanceResult distanceResult)
        {
            // As of Unity Physics 1.0.14, only a single contact is generated for this case,
            // and there is a todo for making it multi-contact.
            return ContactManifoldHelpers.GetSingleContactManifold(in distanceResult);
        }

        internal static bool CapsuleCapsuleDistance(in CapsuleCollider capsuleA, in CapsuleCollider capsuleB, float maxDistance, out ColliderDistanceResultInternal result)
        {
            SegmentSegment(capsuleA.pointA,
                           capsuleA.pointB,
                           capsuleB.pointA,
                           capsuleB.pointB,
                           out float3 closestA,
                           out float3 closestB,
                           out var isStartEndAB,
                           out var closestAToBPrecise);
            SphereCollider sphereA  = new SphereCollider(float3.zero, capsuleA.radius);
            SphereCollider sphereB  = new SphereCollider(closestAToBPrecise, capsuleB.radius);
            var            hit      = SphereSphere.SphereSphereDistance(in sphereA, in sphereB, maxDistance, out result, out bool degenerate);
            result.hitpointA       += closestA;
            result.hitpointB       += closestA;
            result.featureCodeA     = 0x4000;
            result.featureCodeA     = (ushort)math.select(result.featureCodeA, 1, isStartEndAB.y);
            result.featureCodeA     = (ushort)math.select(result.featureCodeA, 0, isStartEndAB.x);
            result.featureCodeB     = 0x4000;
            result.featureCodeB     = (ushort)math.select(result.featureCodeB, 1, isStartEndAB.w);
            result.featureCodeB     = (ushort)math.select(result.featureCodeB, 0, isStartEndAB.z);
            if (Hint.Likely(!degenerate))
                return hit;

            var aIsSphere = capsuleA.pointA.Equals(capsuleA.pointB);
            var bIsSphere = capsuleB.pointA.Equals(capsuleB.pointB);
            if (aIsSphere && bIsSphere)
                return hit;

            float3 edgeA = capsuleA.pointB - capsuleA.pointA;
            float3 edgeB = capsuleB.pointB - capsuleB.pointA;
            if (aIsSphere)
            {
                mathex.GetDualPerpendicularNormalized(edgeB, out var capsuleNormal, out _);
                result.normalB   = capsuleNormal;
                result.normalA   = -capsuleNormal;
                result.hitpointB = closestB + capsuleB.radius * capsuleNormal;
                result.hitpointA = closestA - capsuleA.radius * capsuleNormal;
                return hit;
            }
            if (bIsSphere)
            {
                mathex.GetDualPerpendicularNormalized(edgeA, out var capsuleNormal, out _);
                result.normalA   = capsuleNormal;
                result.normalB   = -capsuleNormal;
                result.hitpointA = closestA + capsuleA.radius * capsuleNormal;
                result.hitpointB = closestB - capsuleB.radius * capsuleNormal;
                return hit;
            }

            {
                var capsuleNormal = math.normalizesafe(math.cross(edgeA, edgeB));
                if (capsuleNormal.Equals(float3.zero))
                    mathex.GetDualPerpendicularNormalized(math.select(edgeA, edgeB, math.lengthsq(edgeB) > math.lengthsq(edgeA)), out capsuleNormal, out _);
                result.normalA   = capsuleNormal;
                result.normalB   = -capsuleNormal;
                result.hitpointA = closestA + capsuleA.radius * capsuleNormal;
                result.hitpointB = closestB - capsuleB.radius * capsuleNormal;
                return hit;
            }
        }

        // Currently 12 ULP per component, which when measured as a distance is ~21 ULP. We round up to 32 (16f).
        // If the SegmentSegment kernel is ever updated, this constant should also be updated.
        internal const float kSegmentSegmentPreciseMaxUlpErrorTolerance = 16f * math.EPSILON;

        internal static void SegmentSegment(float3 startSegmentA,
                                            float3 endSegmentA,
                                            float3 startSegmentB,
                                            float3 endSegmentB,
                                            out float3 closestAOut,
                                            out float3 closestBOut,
                                            out bool4 isStartEndAB,
                                            out float3 closestAToBPrecise)
        {
            float3 edgeA                     = endSegmentA - startSegmentA;
            float3 edgeB                     = endSegmentB - startSegmentB;
            float3 perpendicularUnnormalized = math.cross(edgeA, edgeB);
            float3 startAToStartB            = startSegmentB - startSegmentA;
            // This is the derivation for the optimized implementation
            // edgeB x perpendicular creates a plane. The closest points on the infinite edges is based on where edgeA crosses that plane.
            //float3 bPlaneUnnormal = math.cross(edgeB, perpendicularUnnormalized);
            // Project startSegmentA onto the plane normal to get the distance of startSegmentA to the plane.
            // We use startSegmentB as a reference point.
            //float  startADistanceToPlane = math.dot(startAToStartB, bPlaneUnnormal);
            // Project edgeA onto the plane normal to determine how much distance it travels relative to the plane normal.
            //float edgeAProjectionDistance = math.dot(edgeA, bPlaneUnnormal);
            // We never had to normalize bPlaneUnnormal because is magnitude is canceled out by this division.
            //float fractionAlongA = startADistanceToPlane / edgeAProjectionDistance;
            // Find the clamped point along segment A using the original distances rather than the fraction to avoid precision loss from the division.
            //float3 pointOnA = startSegmentA + edgeA * fractionAlongA;
            //pointOnA        = math.select(pointOnA, endSegmentA, startADistanceToPlane >= edgeAProjectionDistance);
            // Note: For parallel edges, this picks startSegmentA, and for degenerate edges, it prefers startSegmentA over endSegmentA.
            //pointOnA = math.select(startSegmentA, pointOnA, startADistanceToPlane > 0f);

            // The issue with the above is that we don't detect coincidence of endpoints cleanly across the segments.
            // However, we can recognize that startADistanceToPlane is really: dot(startAToStartB, cross(edgeB, perpendicularUnnormalized)).
            // Using the triple product rule, we can rearrange that as dot(perpendicularUnnormalized, cross(startAToStartB, edgeB)).
            // The benefit of this approach is that the cross product is exactly zero if startSegmentA is either of B's endpoints.
            // We can then apply the trick for endSegmentA. The difference of these signed distances results in the total edgeAProjectionDistance.
            float3 endAToStartB           = startSegmentB - endSegmentA;
            float  startADistanceToPlane  = math.dot(perpendicularUnnormalized, math.cross(startAToStartB, edgeB));
            float  endADistanceToPlane    = math.dot(perpendicularUnnormalized, math.cross(endAToStartB, edgeB));
            float  edgeProjectionDistance = startADistanceToPlane - endADistanceToPlane;
            float  fractionAlongA         = startADistanceToPlane / edgeProjectionDistance;
            float3 pointOnA               = startSegmentA + edgeA * fractionAlongA;
            // For there to be an intersection, the signed distance from the start should be positive and from the end should be negative.
            // If either is violated, we clamp.
            pointOnA = math.select(pointOnA, endSegmentA, endADistanceToPlane >= 0f);
            pointOnA = math.select(pointOnA, startSegmentA, startADistanceToPlane <= 0f);

            // Find the closest point on segment B by projecting pointOnA onto edgeB.
            float  edgeBLengthSq  = math.lengthsq(edgeB);
            float  aAlongB        = math.dot(pointOnA - startSegmentB, edgeB);
            float  fractionAlongB = aAlongB / edgeBLengthSq;
            float3 pointOnB       = startSegmentB + edgeB * fractionAlongB;
            bool   isStartB       = aAlongB <= 0f;
            bool   isEndB         = aAlongB >= edgeBLengthSq;
            pointOnB              = math.select(pointOnB, endSegmentB, isEndB);
            pointOnB              = math.select(startSegmentB, pointOnB, !isStartB);

            // Re-find the closest point on segment A.
            float edgeALengthSq = math.lengthsq(edgeA);
            float bAlongA       = math.dot(pointOnB - startSegmentA, edgeA);
            fractionAlongA      = bAlongA / edgeALengthSq;
            pointOnA            = startSegmentA + edgeA * fractionAlongA;
            bool isStartA       = bAlongA <= 0f;
            bool isEndA         = bAlongA >= edgeALengthSq;
            pointOnA            = math.select(pointOnA, endSegmentA, isEndA);
            pointOnA            = math.select(startSegmentA, pointOnA, !isStartA);
            isStartEndAB        = new bool4(isStartA, isEndA, isStartB, isEndB);

            closestAOut        = pointOnA;
            closestBOut        = pointOnB;
            closestAToBPrecise = closestBOut - closestAOut;

            // Refine results by projecting the errors
            bool aNotEndpoint = !(isStartA | isEndA);
            bool bNotEndpoint = !(isStartB | isEndB);
            if (!aNotEndpoint && !bNotEndpoint)
                return;

            // See PointRayCapsule.PointCapsuleDistance() for explanation of what we are doing here.
            float distanceSq = math.lengthsq(closestAToBPrecise);
            if (!bNotEndpoint) // only aNotEndpoint is true
                closestAToBPrecise -= edgeA * (math.dot(closestAToBPrecise, edgeA) / edgeALengthSq);
            else if (!aNotEndpoint) // only bNotEndpoint is true
                closestAToBPrecise -= edgeB * (math.dot(closestAToBPrecise, edgeB) / edgeBLengthSq);
            else  // both aNotEndpoint and bNotEndpoint are true
            {
                float perpendicularSq = math.lengthsq(perpendicularUnnormalized);
                // The precision of the perpendicular is determined by the splay. We use a conservative metric here.
                if (perpendicularSq > 2f * distanceSq * (edgeALengthSq + edgeBLengthSq))
                {
                    // Project closest onto the perpendicular
                    closestAToBPrecise = perpendicularUnnormalized * (math.dot(closestAToBPrecise, perpendicularUnnormalized) / perpendicularSq);
                }
                else
                {
                    // The perpendicular itself is too noisy, likely because the segments are nearly parallel. Clean based on A
                    closestAToBPrecise -= edgeA * (math.dot(closestAToBPrecise, edgeA) / edgeALengthSq);
                }
            }
        }

        internal static void SegmentSegment(in simdFloat3 startSegmentA,
                                            in simdFloat3 endSegmentA,
                                            in simdFloat3 startSegmentB,
                                            in simdFloat3 endSegmentB,
                                            out simdFloat3 closestAOut,
                                            out simdFloat3 closestBOut,
                                            out int isStartEndABx4,  // 16-bit bitfield
                                            out simdFloat3 closestAToBPrecise)
        {
            simdFloat3 edgeA                     = endSegmentA - startSegmentA;
            simdFloat3 edgeB                     = endSegmentB - startSegmentB;
            simdFloat3 perpendicularUnnormalized = simd.cross(edgeA, edgeB);
            simdFloat3 startAToStartB            = startSegmentB - startSegmentA;
            // This is the derivation for the optimized implementation
            // edgeB x perpendicular creates a plane. The closest points on the infinite edges is based on where edgeA crosses that plane.
            //float3 bPlaneUnnormal = math.cross(edgeB, perpendicularUnnormalized);
            // Project startSegmentA onto the plane normal to get the distance of startSegmentA to the plane.
            // We use startSegmentB as a reference point.
            //float  startADistanceToPlane = math.dot(startAToStartB, bPlaneUnnormal);
            // Project edgeA onto the plane normal to determine how much distance it travels relative to the plane normal.
            //float edgeAProjectionDistance = math.dot(edgeA, bPlaneUnnormal);
            // We never had to normalize bPlaneUnnormal because is magnitude is canceled out by this division.
            //float fractionAlongA = startADistanceToPlane / edgeAProjectionDistance;
            // Find the clamped point along segment A using the original distances rather than the fraction to avoid precision loss from the division.
            //float3 pointOnA = startSegmentA + edgeA * fractionAlongA;
            //pointOnA        = math.select(pointOnA, endSegmentA, startADistanceToPlane >= edgeAProjectionDistance);
            // Note: For parallel edges, this picks startSegmentA, and for degenerate edges, it prefers startSegmentA over endSegmentA.
            //pointOnA = math.select(startSegmentA, pointOnA, startADistanceToPlane > 0f);

            // The issue with the above is that we don't detect coincidence of endpoints cleanly across the segments.
            // However, we can recognize that startADistanceToPlane is really: dot(startAToStartB, cross(edgeB, perpendicularUnnormalized)).
            // Using the triple product rule, we can rearrange that as dot(perpendicularUnnormalized, cross(startAToStartB, edgeB)).
            // The benefit of this approach is that the cross product is exactly zero if startSegmentA is either of B's endpoints.
            // We can then apply the trick for endSegmentA. The difference of these signed distances results in the total edgeAProjectionDistance.
            simdFloat3 endAToStartB           = startSegmentB - endSegmentA;
            float4     startADistanceToPlane  = simd.dot(perpendicularUnnormalized, simd.cross(startAToStartB, edgeB));
            float4     endADistanceToPlane    = simd.dot(perpendicularUnnormalized, simd.cross(endAToStartB, edgeB));
            float4     edgeProjectionDistance = startADistanceToPlane - endADistanceToPlane;
            float4     fractionAlongA         = startADistanceToPlane / edgeProjectionDistance;
            simdFloat3 pointOnA               = startSegmentA + edgeA * fractionAlongA;
            // For there to be an intersection, the signed distance from the start should be positive and from the end should be negative.
            // If either is violated, we clamp.
            pointOnA = simd.select(pointOnA, endSegmentA, endADistanceToPlane >= 0f);
            pointOnA = simd.select(pointOnA, startSegmentA, startADistanceToPlane <= 0f);

            // Find the closest point on segment B by projecting pointOnA onto edgeB.
            float4     edgeBLengthSq  = simd.lengthsq(edgeB);
            float4     aAlongB        = simd.dot(pointOnA - startSegmentB, edgeB);
            float4     fractionAlongB = aAlongB / edgeBLengthSq;
            simdFloat3 pointOnB       = startSegmentB + edgeB * fractionAlongB;
            bool4      isStartB       = aAlongB <= 0f;
            bool4      isEndB         = aAlongB >= edgeBLengthSq;
            pointOnB                  = simd.select(pointOnB, endSegmentB, isEndB);
            pointOnB                  = simd.select(startSegmentB, pointOnB, !isStartB);

            // Re-find the closest point on segment A.
            float4 edgeALengthSq  = simd.lengthsq(edgeA);
            float4 bAlongA        = simd.dot(pointOnB - startSegmentA, edgeA);
            fractionAlongA        = bAlongA / edgeALengthSq;
            pointOnA              = startSegmentA + edgeA * fractionAlongA;
            bool4 isStartA        = bAlongA <= 0f;
            bool4 isEndA          = bAlongA >= edgeALengthSq;
            pointOnA              = simd.select(pointOnA, endSegmentA, isEndA);
            pointOnA              = simd.select(startSegmentA, pointOnA, !isStartA);
            int4 bitmask          = math.select(0, new int4(0x1, 0x10, 0x100, 0x1000), isStartA);
            bitmask              += math.select(0, new int4(0x2, 0x20, 0x200, 0x2000), isEndA);
            bitmask              += math.select(0, new int4(0x4, 0x40, 0x400, 0x4000), isStartB);
            bitmask              += math.select(0, new int4(0x8, 0x80, 0x800, 0x8000), isEndB);
            isStartEndABx4        = math.csum(bitmask);

            closestAOut        = pointOnA;
            closestBOut        = pointOnB;
            closestAToBPrecise = closestBOut - closestAOut;

            // Refine results by projecting the errors
            bool4 aNotEndpoint = !(isStartA | isEndA);
            bool4 bNotEndpoint = !(isStartB | isEndB);

            // See PointRayCapsule.PointCapsuleDistance() for explanation of what we are doing here.
            float4 distanceSq = simd.lengthsq(closestAToBPrecise);

            simdFloat3 zero = default;
            // only bNotEndpoint is true
            bool4 bNotEndpointOnly  = !aNotEndpoint & bNotEndpoint;
            closestAToBPrecise     -= simd.select(zero, edgeB * (simd.dot(closestAToBPrecise, edgeB) / edgeBLengthSq), bNotEndpointOnly);
            // both aNotEndpoint and bNotEndpoint are true
            bool4  aNorBEndpoint   = aNotEndpoint & bNotEndpoint;
            float4 perpendicularSq = simd.lengthsq(perpendicularUnnormalized);
            // The precision of the perpendicular is determined by the splay. We use a conservative metric here.
            bool4 safePerpendicular = perpendicularSq > 2f * distanceSq * (edgeALengthSq + edgeBLengthSq);
            // Project closest onto the perpendicular
            closestAToBPrecise = simd.select(closestAToBPrecise,
                                             perpendicularUnnormalized * (simd.dot(closestAToBPrecise, perpendicularUnnormalized) / perpendicularSq),
                                             safePerpendicular & aNorBEndpoint);
            // only aNotEndpoint is true or ...
            bool4 aNotEndpointOnly = aNotEndpoint & !bNotEndpoint;
            // The perpendicular itself is too noisy, likely because the segments are nearly parallel. Clean based on A
            closestAToBPrecise -= simd.select(zero, edgeA * (simd.dot(closestAToBPrecise, edgeA) / edgeALengthSq), aNotEndpointOnly | (!safePerpendicular & aNorBEndpoint));
        }

        internal static void SegmentSegment(in simdFloat3 startSegmentA,
                                            in simdFloat3 endSegmentA,
                                            in simdFloat3 startSegmentB,
                                            in simdFloat3 endSegmentB,
                                            out simdFloat3 closestAOut,
                                            out simdFloat3 closestBOut)
        {
            simdFloat3 edgeA = endSegmentA - startSegmentA;
            simdFloat3 edgeB = endSegmentB - startSegmentB;
            simdFloat3 diff  = startSegmentB - startSegmentA;

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

            closestAOut = simd.select(startSegmentA + fracA * edgeA, endSegmentA, fracA == 1f);
            closestBOut = simd.select(startSegmentB + fracB * edgeB, endSegmentB, fracB == 1f);
        }

        internal static void SegmentSegmentOld(in simdFloat3 pointA,
                                               in simdFloat3 edgeA,
                                               in simdFloat3 pointB,
                                               in simdFloat3 edgeB,
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
        }

        // Returns true for each segment pair whose result does not include an endpoint on either segment of the pair.
        // Unlike the above, this implementation assumes that closestAOut and closestBOut will be disregarded at endpoints.
        // Thus, it skips patching those values with endpoints, and instead accepts points and edges as inputs.
        public static bool4 SegmentSegmentInvalidateEndpointsPointEdge(simdFloat3 pointA,
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
    }
}

