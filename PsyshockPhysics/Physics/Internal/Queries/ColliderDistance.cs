using System;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static partial class SpatialInternal
    {
        public struct ColliderDistanceResultInternal
        {
            public float3 hitpointA;
            public float3 hitpointB;
            public float3 normalA;
            public float3 normalB;
            public float  distance;
        }

        #region Sphere
        public static bool SphereSphereDistance(SphereCollider sphereA, SphereCollider sphereB, float maxDistance, out ColliderDistanceResultInternal result)
        {
            float3 delta          = sphereB.center - sphereA.center;
            float  ccDistanceSq   = math.lengthsq(delta);  //center center distance
            bool   distanceIsZero = ccDistanceSq == 0.0f;
            float  invCCDistance  = math.select(math.rsqrt(ccDistanceSq), 0.0f, distanceIsZero);
            float3 normalA        = math.select(delta * invCCDistance, new float3(0, 1, 0), distanceIsZero);  // choose an arbitrary normal when the distance is zero
            float  distance       = ccDistanceSq * invCCDistance - sphereA.radius - sphereB.radius;
            result                = new ColliderDistanceResultInternal
            {
                hitpointA = sphereA.center + normalA * sphereA.radius,
                hitpointB = sphereA.center + normalA * (sphereA.radius + distance),  //hitpoint A + A's normal * distance [expand distributive property]
                normalA   = normalA,
                normalB   = -normalA,
                distance  = distance,
            };
            return distance <= maxDistance;
        }
        #endregion Sphere

        #region Capsule
        public static bool CapsuleSphereDistance(CapsuleCollider capsule, SphereCollider sphere, float maxDistance, out ColliderDistanceResultInternal result)
        {
            //Strategy: Project p onto the capsule's line clamped to the segment. Then inflate point on line as sphere
            float3 edge                   = capsule.pointB - capsule.pointA;
            float3 ap                     = sphere.center - capsule.pointA;
            float  dot                    = math.dot(ap, edge);
            float  edgeLengthSq           = math.lengthsq(edge);
            dot                           = math.clamp(dot, 0f, edgeLengthSq);
            float3         pointOnSegment = capsule.pointA + edge * dot / edgeLengthSq;
            SphereCollider sphereA        = new SphereCollider(pointOnSegment, capsule.radius);
            return SphereSphereDistance(sphereA, sphere, maxDistance, out result);
        }

        public static bool CapsuleCapsuleDistance(CapsuleCollider capsuleA, CapsuleCollider capsuleB, float maxDistance, out ColliderDistanceResultInternal result)
        {
            float3 edgeA = capsuleA.pointB - capsuleA.pointA;
            float3 edgeB = capsuleB.pointB - capsuleB.pointA;

            QueriesLowLevelUtils.SegmentSegment(capsuleA.pointA, edgeA, capsuleB.pointA, edgeB, out float3 closestA, out float3 closestB);
            //Todo: There may be some precision issues at close distances. Figure this out later.
            SphereCollider sphereA = new SphereCollider(closestA, capsuleA.radius);
            SphereCollider sphereB = new SphereCollider(closestB, capsuleB.radius);
            return SphereSphereDistance(sphereA, sphereB, maxDistance, out result);
        }
        #endregion Capsule

        #region Box
        public static bool BoxSphereDistance(BoxCollider box, SphereCollider sphere, float maxDistance, out ColliderDistanceResultInternal result)
        {
            bool   hit     = PointBoxDistance(sphere.center, box, maxDistance + sphere.radius, out PointDistanceResultInternal pointDistanceResult);
            float3 normalB = math.normalizesafe(pointDistanceResult.hitpoint - sphere.center, -pointDistanceResult.normal);
            result         = new ColliderDistanceResultInternal
            {
                distance  = pointDistanceResult.distance - sphere.radius,
                hitpointA = pointDistanceResult.hitpoint,
                hitpointB = sphere.center + normalB * sphere.radius,
                normalA   = pointDistanceResult.normal,
                normalB   = normalB,
            };
            return hit;
        }

        //The following is defined in DistanceQueries.Collider.BoxCapsule
        //public static bool DistanceBetween(BoxCollider box, CapsuleCollider capsule, float maxDistance, out ColliderDistanceResultInternal result)

        //The following is defined in DistanceQueries.Collider.BoxBox
        //public static bool DistanceBetween(BoxCollider boxA,
        //                                   BoxCollider boxB,
        //                                   RigidTransform bInASpace,
        //                                   RigidTransform aInBSpace,
        //                                   float maxDistance,
        //                                   out ColliderDistanceResultInternal result)
        #endregion Box

        #region Triangle
        public static bool TriangleSphereDistance(TriangleCollider triangle, SphereCollider sphere, float maxDistance, out ColliderDistanceResultInternal result)
        {
            bool   hit     = PointTriangleDistance(sphere.center, triangle, maxDistance + sphere.radius, out PointDistanceResultInternal pointDistanceResult);
            float3 normalB = math.normalizesafe(pointDistanceResult.hitpoint - sphere.center, -pointDistanceResult.normal);
            result         = new ColliderDistanceResultInternal
            {
                distance  = pointDistanceResult.distance - sphere.radius,
                hitpointA = pointDistanceResult.hitpoint,
                hitpointB = sphere.center + normalB * sphere.radius,
                normalA   = pointDistanceResult.normal,
                normalB   = normalB,
            };
            return hit;
        }

        public static bool TriangleCapsuleDistance(TriangleCollider triangle, CapsuleCollider capsule, float maxDistance, out ColliderDistanceResultInternal result)
        {
            // The strategy for this is different from Unity Physics, but is inspired by the capsule-capsule algorithm
            // and this blog: https://wickedengine.net/2020/04/26/capsule-collision-detection/
            // The idea is to reorder the checks so that the axis intersection branch culls some more math.
            simdFloat3 triPoints = new simdFloat3(triangle.pointA, triangle.pointB, triangle.pointC, triangle.pointA);
            simdFloat3 triEdges  = triPoints.bcaa - triPoints;

            float3 capEdge = capsule.pointB - capsule.pointA;
            QueriesLowLevelUtils.SegmentSegment(triPoints, triEdges, new simdFloat3(capsule.pointA), new simdFloat3(capEdge), out var closestTriEdges, out var closestCapsuleAxis);
            float3 segSegDists      = simd.distancesq(closestTriEdges, closestCapsuleAxis).xyz;
            bool   bIsBetter        = segSegDists.y < segSegDists.x;
            float3 closestEdgePoint = math.select(closestTriEdges.a, closestTriEdges.b, bIsBetter);
            float3 closestAxisPoint = math.select(closestCapsuleAxis.a, closestCapsuleAxis.b, bIsBetter);
            bool   cIsBetter        = segSegDists.z < math.cmin(segSegDists.xy);
            closestEdgePoint        = math.select(closestEdgePoint, closestTriEdges.c, cIsBetter);
            closestAxisPoint        = math.select(closestAxisPoint, closestCapsuleAxis.c, cIsBetter);

            if (RaycastTriangle(new Ray(capsule.pointA, capsule.pointB), triPoints, out float fraction, out _))
            {
                float3 triNormal         = math.normalizesafe(math.cross(triEdges.a, triEdges.b), math.normalizesafe(capEdge, 0f));
                float  minFractionOffset = math.min(fraction, 1f - fraction);
                // This is how much we have to move the axis along the triangle through the axis to achieve separation.
                float dot = math.dot(triNormal, capEdge);

                float offsetDistance = minFractionOffset * math.abs(dot);

                if (offsetDistance * offsetDistance <= math.distancesq(closestEdgePoint, closestAxisPoint))
                {
                    bool useCapB                 = 1f - fraction < fraction;
                    triNormal                    = math.select(triNormal, -triNormal, (dot < 0f) ^ useCapB);
                    float3         capsuleOffset = triNormal * (offsetDistance + capsule.radius);
                    SphereCollider sphere        = new SphereCollider(math.select(capsule.pointA, capsule.pointB, useCapB) + capsuleOffset, capsule.radius);
                    TriangleSphereDistance(triangle, sphere, maxDistance, out result);
                    result.distance   = -offsetDistance;
                    result.hitpointB -= capsuleOffset;
                    return true;
                }
                else
                {
                    SphereCollider axisSphere = new SphereCollider(closestAxisPoint, capsule.radius);
                    SphereCollider edgeSphere = new SphereCollider(closestEdgePoint, 0f);
                    // This gives us the positive distance from the capsule to edge.
                    // The penetration point is the opposite side of the capsule.
                    SphereSphereDistance(edgeSphere, axisSphere, float.MaxValue, out result);
                    result.distance   = -result.distance - capsule.radius;
                    result.normalB    = -result.normalB;
                    result.hitpointB += result.normalB * 2f * capsule.radius;
                    return true;
                }
            }
            else
            {
                SphereCollider axisSphere = new SphereCollider(closestAxisPoint, capsule.radius);
                bool           hitAxis    = TriangleSphereDistance(triangle, axisSphere, maxDistance, out var axisResult);
                SphereCollider aSphere    = new SphereCollider(capsule.pointA, capsule.radius);
                bool           hitA       = TriangleSphereDistance(triangle, aSphere, maxDistance, out var aResult);
                SphereCollider bSphere    = new SphereCollider(capsule.pointB, capsule.radius);
                bool           hitB       = TriangleSphereDistance(triangle, bSphere, maxDistance, out var bResult);
                if (!hitAxis && !hitA && !hitB)
                {
                    result = axisResult;
                    return false;
                }

                result          = default;
                result.distance = float.MaxValue;
                if (hitAxis)
                    result = axisResult;
                if (hitA && aResult.distance < result.distance)
                    result = aResult;
                if (hitB && bResult.distance < result.distance)
                    result = bResult;
                return true;
            }
        }
        #endregion

        #region Convex
        public static bool ConvexSphereDistance(ConvexCollider convex, SphereCollider sphere, float maxDistance, out ColliderDistanceResultInternal result)
        {
            bool   hit     = PointConvexDistance(sphere.center, convex, maxDistance + sphere.radius, out PointDistanceResultInternal pointDistanceResult);
            float3 normalB = math.normalizesafe(pointDistanceResult.hitpoint - sphere.center, -pointDistanceResult.normal);
            result         = new ColliderDistanceResultInternal
            {
                distance  = pointDistanceResult.distance - sphere.radius,
                hitpointA = pointDistanceResult.hitpoint,
                hitpointB = sphere.center + normalB * sphere.radius,
                normalA   = pointDistanceResult.normal,
                normalB   = normalB,
            };
            return hit;
        }
        #endregion
    }
}

