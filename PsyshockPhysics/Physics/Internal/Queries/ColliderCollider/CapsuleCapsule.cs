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

            SegmentSegment(capsuleA.pointA, capsuleA.pointB, bInASpaceA, bInASpaceB, out var closestAOut, out var closestBOut, out _);
            var threshold = capsuleA.radius + capsuleB.radius + maxDistance;
            return math.distancesq(closestAOut, closestBOut) <= threshold * threshold && threshold >= 0f;
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
                SegmentSegment(casterA + hitDisplacement, casterB + hitDisplacement, targetA, targetB, out var closestOnCaster, out var closestOnTarget, out _);
                // Taking the difference in segment points is more accurate than using the raycast normal, except for when the radii are both 0.
                normal = math.normalizesafe(closestOnTarget - closestOnCaster, normal);
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
            SegmentSegment(capsuleA.pointA, capsuleA.pointB, capsuleB.pointA, capsuleB.pointB, out float3 closestA, out float3 closestB, out var isStartEndAB);
            SphereCollider sphereA = new SphereCollider(closestA, capsuleA.radius);
            SphereCollider sphereB = new SphereCollider(closestB, capsuleB.radius);
            var            hit     = SphereSphere.SphereSphereDistance(in sphereA, in sphereB, maxDistance, out result, out bool degenerate);
            result.featureCodeA    = 0x4000;
            result.featureCodeA    = (ushort)math.select(result.featureCodeA, 1, isStartEndAB.y);
            result.featureCodeA    = (ushort)math.select(result.featureCodeA, 0, isStartEndAB.x);
            result.featureCodeB    = 0x4000;
            result.featureCodeB    = (ushort)math.select(result.featureCodeB, 1, isStartEndAB.w);
            result.featureCodeB    = (ushort)math.select(result.featureCodeB, 0, isStartEndAB.z);
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

        internal static void SegmentSegmentNew(float3 startSegmentA,
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

        internal static void SegmentSegment(float3 startSegmentA,
                                            float3 endSegmentA,
                                            float3 startSegmentB,
                                            float3 endSegmentB,
                                            out float3 closestAOut,
                                            out float3 closestBOut,
                                            out bool4 isStartEndAB)
        {
            float3 edgeA = endSegmentA - startSegmentA;
            float3 edgeB = endSegmentB - startSegmentB;

            // The following algorithm is copied from Unity Physics, and is quite robust at handling various degenerate cases.
            // Todo: I don't fully understand it yet, though I've analyzed all the endpoint cases which are the most critical.
            // If startSegmentA = startSegmentB, then diff, s1, and s2 all become zero, which results in fracA and fracB becoming exactly 0.
            // If endSegmentA = startSegmentB, then diff = edgeA, and therefore r = s2 and s1 = lengthASq. Therefore fracA numerator = denominator,
            // and fracA becomes exactly 1f, while fracB becomes exactly 0f. (r - s2 = 0).
            // If startSegmentA = endSegmentB, then diff = -edgeB, and therefore r = -s1 and s2 = -lengthBSq. Therefore, fracA numerator is 0.
            // fracB is s2 / (-lengthBSq) which is 1f exactly.
            // We only need to watch out for when endSegmentA = endSegmentB, as there is no perfect floating point in that scenario.
            float3 diff      = startSegmentB - startSegmentA;
            float  r         = math.dot(edgeA, edgeB);
            float  s1        = math.dot(edgeA, diff);
            float  s2        = math.dot(edgeB, diff);
            float  lengthASq = math.lengthsq(edgeA);
            float  lengthBSq = math.lengthsq(edgeB);

            float invDenom, invLengthASq, invLengthBSq;
            {
                float  denom = lengthASq * lengthBSq - r * r;
                float3 inv   = 1.0f / new float3(denom, lengthASq, lengthBSq);
                invDenom     = inv.x;
                invLengthASq = inv.y;
                invLengthBSq = inv.z;
            }

            // Find the closest point on edge A to the line containing edge B
            float fracA = (s1 * lengthBSq - s2 * r) * invDenom;
            fracA       = math.clamp(fracA, 0.0f, 1.0f);

            // Find the closest point on edge B to the point on A just found
            float fracB = fracA * (invLengthBSq * r) - invLengthBSq * s2;
            fracB       = math.clamp(fracB, 0.0f, 1.0f);

            // If the point on B was clamped then there may be a closer point on A to the edge
            fracA = fracB * (invLengthASq * r) + invLengthASq * s1;
            fracA = math.clamp(fracA, 0.0f, 1.0f);

            isStartEndAB = new float4(fracA, fracA, fracB, fracB) == new float4(0f, 1f, 0f, 1f);
            closestAOut  = math.select(startSegmentA + fracA * edgeA, endSegmentA, isStartEndAB.y);
            closestBOut  = math.select(startSegmentB + fracB * edgeB, endSegmentB, isStartEndAB.w);
        }

        // Todo: Copied from Unity.Physics. I still don't fully understand this, but it is working correctly for degenerate segments somehow.
        // I tested with parallel segments, segments with 0-length edges and a few other weird things. It holds up with pretty good accuracy.
        // I'm not sure where the NaNs or infinities disappear. But they do.
        // Find the closest points on a pair of line segments
        internal static void SegmentSegmentOld(float3 pointA, float3 edgeA, float3 pointB, float3 edgeB, out float3 closestAOut, out float3 closestBOut, out bool4 isStartEndAB)
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

            isStartEndAB = new float4(fracA, fracA, fracB, fracB) == new float4(0f, 1f, 0f, 1f);
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
        public static bool4 SegmentSegmentInvalidateEndpoints(in simdFloat3 startSegmentA,
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

            return fracA != 0f & fracA != 1f & fracB != 0f & fracB != 1f;
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

