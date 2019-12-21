using System;
using Unity.Mathematics;

namespace Latios.PhysicsEngine
{
    internal static partial class DistanceQueries
    {
        public struct PointDistanceResultInternal
        {
            public float3 hitpoint;
            public float  distance;
            public float3 normal;
        }

        //All algorithms return a negative distance if inside the collider.
        public static bool DistanceBetween(float3 point, SphereCollider sphere, float maxDistance, out PointDistanceResultInternal result)
        {
            float3 delta          = sphere.center - point;
            float  pcDistanceSq   = math.lengthsq(delta);  //point center distance
            bool   distanceIsZero = pcDistanceSq == 0.0f;
            float  invPCDistance  = math.select(math.rsqrt(pcDistanceSq), 0.0f, distanceIsZero);
            float3 inNormal       = math.select(delta * invPCDistance, new float3(0, 1, 0), distanceIsZero);  // choose an arbitrary normal when the distance is zero
            float  distance       = pcDistanceSq * invPCDistance - sphere.radius;
            result                = new PointDistanceResultInternal
            {
                hitpoint = point + inNormal * distance,
                distance = distance,
                normal   = -inNormal,
            };
            return distance <= maxDistance;
        }

        public static bool DistanceBetween(float3 point, CapsuleCollider capsule, float maxDistance, out PointDistanceResultInternal result)
        {
            //Strategy: Project p onto the capsule's line clamped to the segment. Then inflate point on line as sphere
            float3 edge                   = capsule.pointB - capsule.pointA;
            float3 ap                     = point - capsule.pointA;
            float  dot                    = math.dot(ap, edge);
            float  edgeLengthSq           = math.lengthsq(edge);
            dot                           = math.clamp(dot, 0f, edgeLengthSq);
            float3         pointOnSegment = capsule.pointA + edge * dot / edgeLengthSq;
            SphereCollider sphere         = new SphereCollider(pointOnSegment, capsule.radius);
            return DistanceBetween(point, sphere, maxDistance, out result);
        }
    }
}

