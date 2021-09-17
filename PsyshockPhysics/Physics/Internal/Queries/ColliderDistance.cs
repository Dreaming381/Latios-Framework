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
    }
}

