using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class CapsuleCapsule
    {
        public static bool DistanceBetween(in CapsuleCollider capsuleA,
                                           in RigidTransform aTransform,
                                           in CapsuleCollider capsuleB,
                                           in RigidTransform bTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            var             aWorldToLocal      = math.inverse(aTransform);
            var             BinASpaceTransform = math.mul(aWorldToLocal, bTransform);
            CapsuleCollider BinASpace          = new CapsuleCollider(math.transform(BinASpaceTransform, capsuleB.pointA),
                                                                     math.transform(BinASpaceTransform, capsuleB.pointB),
                                                                     capsuleB.radius);
            bool hit = CapsuleCapsuleDistance(in capsuleA, in BinASpace, maxDistance, out ColliderDistanceResultInternal localResult);
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
            float3 edgeA = capsuleA.pointB - capsuleA.pointA;
            float3 edgeB = capsuleB.pointB - capsuleB.pointA;

            SegmentSegment(capsuleA.pointA, edgeA, capsuleB.pointA, edgeB, out float3 closestA, out float3 closestB, out var isStartEndAB);
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

            if (math.all((edgeA == 0f) & (edgeB == 0f)))
                return hit;

            if (math.all(edgeA == 0f))
            {
                mathex.GetDualPerpendicularNormalized(edgeB, out var capsuleNormal, out _);
                result.normalB   = capsuleNormal;
                result.normalA   = -capsuleNormal;
                result.hitpointB = closestB - capsuleB.radius * capsuleNormal;
                result.hitpointA = closestA + capsuleA.radius * capsuleNormal;
                return hit;
            }
            if (math.all(edgeB == 0f))
            {
                mathex.GetDualPerpendicularNormalized(edgeA, out var capsuleNormal, out _);
                result.normalA   = capsuleNormal;
                result.normalB   = -capsuleNormal;
                result.hitpointA = closestA - capsuleA.radius * capsuleNormal;
                result.hitpointB = closestB + capsuleB.radius * capsuleNormal;
                return hit;
            }

            {
                var capsuleNormal = math.normalize(math.cross(edgeA, edgeB));
                result.normalA    = capsuleNormal;
                result.normalB    = -capsuleNormal;
                result.hitpointA  = closestA - capsuleA.radius * capsuleNormal;
                result.hitpointB  = closestB + capsuleB.radius * capsuleNormal;
                return hit;
            }
        }

        // Todo: Copied from Unity.Physics. I still don't fully understand this, but it is working correctly for degenerate segments somehow.
        // I tested with parallel segments, segments with 0-length edges and a few other weird things. It holds up with pretty good accuracy.
        // I'm not sure where the NaNs or infinities disappear. But they do.
        // Find the closest points on a pair of line segments
        internal static void SegmentSegment(float3 pointA, float3 edgeA, float3 pointB, float3 edgeB, out float3 closestAOut, out float3 closestBOut, out bool4 isStartEndAB)
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

        internal static void SegmentSegment(in simdFloat3 pointA,
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
        public static bool4 SegmentSegmentInvalidateEndpoints(simdFloat3 pointA,
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

