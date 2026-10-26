using Latios.Calci;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class PointRayCapsule
    {
        public static bool AreOverlapping(float3 point, in CapsuleCollider capsule, in RigidTransform capsuleTransform)
        {
            return WithinDistance(point, in capsule, in capsuleTransform, 0f);
        }

        public static bool WithinDistance(float3 point, in CapsuleCollider capsule, in RigidTransform capsuleTransform, float maxDistance)
        {
            var pointInCapSpace = math.InverseTransformFast(in capsuleTransform, point);
            return PointCapsuleWithin(pointInCapSpace, in capsule, maxDistance);
        }

        public static bool DistanceBetween(float3 point, in CapsuleCollider capsule, in RigidTransform capsuleTransform, float maxDistance, out PointDistanceResult result)
        {
            var capsuleInWorldPositionRelativeSpace = new CapsuleCollider(math.rotate(capsuleTransform.rot, capsule.pointA),
                                                                          math.rotate(capsuleTransform.rot, capsule.pointB),
                                                                          capsule.radius);
            bool hit = PointCapsuleDistance(point - capsuleTransform.pos, in capsuleInWorldPositionRelativeSpace, maxDistance, out var localResult);
            result   = new PointDistanceResult
            {
                hitpoint = localResult.hitpoint + capsuleTransform.pos,
                normal   = localResult.normal,
                distance = localResult.distance
            };
            return hit;
        }

        public static bool Raycast(in Ray ray, in CapsuleCollider capsule, in RigidTransform capsuleTransform, out RaycastResult result)
        {
            // Faster to transform the capsule than transform the ray.
            var capsuleInWorldPositionRelativeSpace = new CapsuleCollider(math.rotate(capsuleTransform.rot, capsule.pointA),
                                                                          math.rotate(capsuleTransform.rot, capsule.pointB),
                                                                          capsule.radius);
            bool hit = RaycastCapsule(ray.start - capsuleTransform.pos,
                                      ray.displacement,
                                      capsuleInWorldPositionRelativeSpace,
                                      out float fraction,
                                      out float3 normal);
            result.position         = ray.start + ray.displacement * fraction;
            result.normal           = normal;
            result.distance         = fraction * math.length(ray.displacement);
            result.subColliderIndex = 0;
            return hit;
        }

        internal static bool PointCapsuleWithin(float3 point, in CapsuleCollider capsule, float maxDistance)
        {
            // Strategy: Project p onto the capsule's line clamped to the segment. Then inflate point on line as sphere
            float3 ab         = capsule.pointB - capsule.pointA;
            float3 ap         = point - capsule.pointA;
            float  dot        = math.dot(ap, ab);
            float  abLengthSq = math.lengthsq(ab);
            dot               = math.clamp(dot, 0f, abLengthSq);
            var factor        = dot / abLengthSq;
            // If the division fails, that means we somehow weren't closest to an endpoint, but the numbers are too small to calculate where inbetween, so we just use the exact middle.
            factor             = math.select(0.5f, factor, math.isfinite(factor));
            var pointOnSegment = math.select(capsule.pointA + ab * factor, capsule.pointB, dot == abLengthSq);
            var threshold      = capsule.radius + maxDistance;
            return math.distancesq(point, pointOnSegment) <= threshold * threshold && threshold >= 0f;
        }

        internal static bool PointCapsuleDistance(float3 point, in CapsuleCollider capsule, float maxDistance, out PointDistanceResultInternal result)
        {
            // Strategy: Project p onto the capsule's line clamped to the segment. Then inflate point on line as sphere
            float3 ab         = capsule.pointB - capsule.pointA;
            float3 ap         = point - capsule.pointA;
            float  dot        = math.dot(ap, ab);
            float  abLengthSq = math.lengthsq(ab);
            dot               = math.clamp(dot, 0f, abLengthSq);
            ushort featureCode;
            float3 segmentToPoint;
            if (dot == 0f)
            {
                featureCode    = 0;
                segmentToPoint = ap;
            }
            else if (dot == abLengthSq)
            {
                featureCode    = 1;
                segmentToPoint = point - capsule.pointB;
            }
            else
            {
                featureCode    = 0x4000;
                var factor     = dot / abLengthSq;  // Guaranteed to be valid due to branches above
                segmentToPoint = ap - ab * factor;
                // The above isn't sufficiently accurate at computing where we are on the segment, which matters a lot when the point is very close to the segment.
                // We reduce the error by projecting our new segmentToPoint onto the segment again and subtracting that error.
                var errorAlongAxis  = math.dot(segmentToPoint, ab) / abLengthSq;
                errorAlongAxis      = math.select(0f, errorAlongAxis, math.isfinite(errorAlongAxis));
                segmentToPoint     -= ab * errorAlongAxis;
            }
            var segmentToPointDistanceSq = math.lengthsq(segmentToPoint);
            var segmentToPointDistance   = math.sqrt(segmentToPointDistanceSq);
            var normal                   = segmentToPoint / segmentToPointDistance;
            if (Hint.Unlikely(segmentToPointDistanceSq < PointRaySphere.kUnmeasurableLengthSqThreshold))
            {
                if (ab.Equals(float3.zero))
                    normal = new float3(0f, 1f, 0f);
                else
                    mathex.GetDualPerpendicularNormalized(ab, out normal, out _);
            }
            result = new PointDistanceResultInternal
            {
                hitpoint    = point - segmentToPoint + normal * capsule.radius,
                distance    = segmentToPointDistance - capsule.radius,
                normal      = normal,
                featureCode = featureCode,
            };
            return result.distance <= maxDistance;
        }

        // Todo: Phase out.
        internal static bool RaycastCapsule(in Ray ray, in CapsuleCollider capsule, out float fraction, out float3 normal)
        {
            return RaycastCapsule(ray.start, ray.displacement, in capsule, out fraction, out normal);
        }

        internal static bool RaycastCapsule(float3 rayStart, float3 rayDisplacement, in CapsuleCollider capsule, out float fraction, out float3 normal)
        {
            // Todo: We rely on axis being either a zero vector or NaN here when the capsule is degenerate.
            float          axisLength = mathex.GetLengthAndNormal(capsule.pointB - capsule.pointA, out float3 axis);
            SphereCollider cap        = new SphereCollider(capsule.pointA, capsule.radius);

            // Ray vs infinite cylinder
            float  directionDotAxis  = math.dot(rayDisplacement, axis);
            float  originDotAxis     = math.dot(rayStart - capsule.pointA, axis);
            float3 rayDisplacement2D = rayDisplacement - axis * directionDotAxis;
            float3 rayOrigin2D       = rayStart - axis * originDotAxis;
            bool   hitCylinder       = PointRaySphere.RaycastSphere(rayOrigin2D, rayDisplacement2D, in cap, out float cylinderFraction, out normal);
            // Distance from either the start of the ray or the hitpoint projected onto the axis from pointA.
            // If this value is between [0, axisLength], then either we hit the cylinder or missed the whole capsule.
            float t       = math.select(originDotAxis, originDotAxis + cylinderFraction * directionDotAxis, hitCylinder);
            bool  useCapB = t > axisLength;
            if (t >= 0f && !useCapB)
            {
                fraction = math.select(2f, cylinderFraction, hitCylinder);
                return hitCylinder;
            }

            // Ray vs caps
            cap.center = math.select(capsule.pointA, capsule.pointB, useCapB);
            return PointRaySphere.RaycastSphere(rayStart, rayDisplacement, in cap, out fraction, out normal);
        }

        // Todo: Phase out.
        internal static bool4 Raycast4Capsules(in Ray ray, in simdFloat3 capA, in simdFloat3 capB, float4 capRadius, out float4 fraction, out simdFloat3 normal)
        {
            return Raycast4Capsules(ray.start, ray.displacement, in capA, in capB, capRadius, out fraction, out normal);
        }

        internal static bool4 Raycast4Capsules(float3 rayStart,
                                               float3 rayDisplacement,
                                               in simdFloat3 capA,
                                               in simdFloat3 capB,
                                               float4 capRadius,
                                               out float4 fraction,
                                               out simdFloat3 normal)
        {
            float4 axisLength = mathex.GetLengthAndNormal(capB - capA, out simdFloat3 axis);
            // Ray vs infinite cylinder
            float4     directionDotAxis  = simd.dot(rayDisplacement, axis);
            float4     originDotAxis     = simd.dot(rayStart - capA, axis);
            simdFloat3 rayDisplacement2D = rayDisplacement - axis * directionDotAxis;
            simdFloat3 rayOrigin2D       = rayStart - axis * originDotAxis;
            bool4      hitCylinder       = PointRaySphere.Raycast4Spheres(in rayOrigin2D,
                                                                     in rayDisplacement2D,
                                                                     in capA,
                                                                     capRadius,
                                                                     out float4 cylinderFraction,
                                                                     out simdFloat3 cylinderNormal);
            float4 t              = math.select(originDotAxis, originDotAxis + cylinderFraction * directionDotAxis, hitCylinder);
            bool4  useCapB        = t > axisLength;
            bool4  useCylinderHit = (t >= 0f) & (!useCapB);

            // Ray vs caps
            var   cap    = simd.select(capA, capB, useCapB);
            bool4 hitCap = PointRaySphere.Raycast4Spheres(new simdFloat3(rayStart),
                                                          new simdFloat3(rayDisplacement),
                                                          in cap,
                                                          capRadius,
                                                          out float4 capFraction,
                                                          out simdFloat3 capNormal);

            // Find best result
            fraction = math.select(capFraction, cylinderFraction, useCylinderHit);
            normal   = simd.select(capNormal, cylinderNormal, useCylinderHit);
            return math.select(hitCap, hitCylinder, useCylinderHit);
        }

        internal static float3 CapsuleNormalFromFeatureCode(ushort featureCode, in CapsuleCollider capsule, float3 csoOutwardDir)
        {
            if (featureCode < 0x4000)
                return math.normalize(csoOutwardDir);

            var axis = capsule.pointB - capsule.pointA;
            return math.mul(quaternion.LookRotationSafe(axis, csoOutwardDir), math.up());
        }

        internal static ushort FeatureCodeFromSegmentHitpoint(float3 hitpoint, float3 endpointA, float3 endpointB)
        {
            bool isB       = hitpoint.Equals(endpointB);
            int  pointCode = math.select(0, 1, isB);
            return (ushort)math.select(0x4000, pointCode, isB || hitpoint.Equals(endpointA));
        }

        internal static ushort FeatureCodeFromGjk(byte count, byte a)
        {
            return count switch
                   {
                       1 => a,
                       2 => 0x4000,
                       _ => a
                   };
        }
    }
}

