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
            return math.distancesq(closestAOut, closestBOut) <= math.square(capsuleA.radius + capsuleB.radius + maxDistance);
        }

        public static bool DistanceBetween(in CapsuleCollider capsuleA,
                                           in RigidTransform aTransform,
                                           in CapsuleCollider capsuleB,
                                           in RigidTransform bTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            var             bInASpaceTransform = math.InverseTransformFast(in aTransform, in bTransform);
            CapsuleCollider bInASpace          = new CapsuleCollider(math.transform(bInASpaceTransform, capsuleB.pointA),
                                                                     math.transform(bInASpaceTransform, capsuleB.pointB),
                                                                     capsuleB.radius);
            bool hit = CapsuleCapsuleDistance(in capsuleA, in bInASpace, maxDistance, out ColliderDistanceResultInternal localResult);
            result   = InternalQueryTypeUtilities.BinAResultToWorld(in localResult, in aTransform);
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
            var csoRadius           = capsuleToCast.radius + targetCapsule.radius;
            var casterAInWorldSpace = math.transform(castStart, capsuleToCast.pointA);
            var casterBInWorldSpace = math.transform(castStart, capsuleToCast.pointB);
            var targetAInWorldSpace = math.transform(targetCapsuleTransform, targetCapsule.pointA);
            var targetBInWorldSpace = math.transform(targetCapsuleTransform, targetCapsule.pointB);
            var cso                 = new simdFloat3(casterAInWorldSpace - targetAInWorldSpace,
                                                     casterAInWorldSpace - targetBInWorldSpace,
                                                     casterBInWorldSpace - targetBInWorldSpace,
                                                     casterBInWorldSpace - targetAInWorldSpace);
            var  ray = new Ray(0f, castStart.pos - castEnd);
            bool hit = PointRayTriangle.RaycastRoundedQuad(in ray, in cso, csoRadius, out float fraction, out float3 normal);
            if (hit)
            {
                var hitTransform = castStart;
                hitTransform.pos = math.lerp(castStart.pos, castEnd, fraction);
                DistanceBetween(in capsuleToCast, in hitTransform, in targetCapsule, in targetCapsuleTransform, 1f, out var distanceResult);
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
            result.featureCodeA    = (ushort)math.select(result.featureCodeA, 0, isStartEndAB.x);
            result.featureCodeA    = (ushort)math.select(result.featureCodeA, 1, isStartEndAB.y);
            result.featureCodeB    = 0x4000;
            result.featureCodeB    = (ushort)math.select(result.featureCodeB, 0, isStartEndAB.z);
            result.featureCodeB    = (ushort)math.select(result.featureCodeB, 1, isStartEndAB.w);
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
                result.hitpointB = closestB - capsuleB.radius * capsuleNormal;
                result.hitpointA = closestA + capsuleA.radius * capsuleNormal;
                return hit;
            }
            if (bIsSphere)
            {
                mathex.GetDualPerpendicularNormalized(edgeA, out var capsuleNormal, out _);
                result.normalA   = capsuleNormal;
                result.normalB   = -capsuleNormal;
                result.hitpointA = closestA - capsuleA.radius * capsuleNormal;
                result.hitpointB = closestB + capsuleB.radius * capsuleNormal;
                return hit;
            }

            {
                var capsuleNormal = math.normalizesafe(math.cross(edgeA, edgeB));
                if (capsuleNormal.Equals(float3.zero))
                    mathex.GetDualPerpendicularNormalized(math.select(edgeA, edgeB, math.lengthsq(edgeB) > math.lengthsq(edgeA)), out capsuleNormal, out _);
                result.normalA   = capsuleNormal;
                result.normalB   = -capsuleNormal;
                result.hitpointA = closestA - capsuleA.radius * capsuleNormal;
                result.hitpointB = closestB + capsuleB.radius * capsuleNormal;
                return hit;
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

