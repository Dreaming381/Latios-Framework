using Latios.Calci;
using Unity.Burst;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class PointRaySphere
    {
        // When the square of any single component drops below FLT_MIN_NORMAL, the lengthsq calculation ends up losing some or all of that component's contribution.
        // This can result in normals greater unit length. This threshold is just large enough to catch all three components doing this within half a ULP.
        public const float kUnmeasurableLengthSqThreshold = 1e-30f;

        public static bool AreOverlapping(float3 point, in SphereCollider sphere, in RigidTransform sphereTransform)
        {
            return WithinDistance(point, in sphere, in sphereTransform, 0f);
        }

        public static bool WithinDistance(float3 point, in SphereCollider sphere, in RigidTransform sphereTransform, float maxDistance)
        {
            var pointShifted        = point - sphereTransform.pos;
            var sphereCenterShifted = math.rotate(sphereTransform.rot, sphere.center);
            var threshold           = sphere.radius + maxDistance;
            return math.distancesq(pointShifted, sphereCenterShifted) <= threshold * threshold && threshold >= 0f;
        }

        public static bool DistanceBetween(float3 point, in SphereCollider sphere, in RigidTransform sphereTransform, float maxDistance, out PointDistanceResult result)
        {
            var  pointShifted  = point - sphereTransform.pos;
            var  sphereShifted = new SphereCollider(math.rotate(sphereTransform.rot, sphere.center), sphere.radius);
            bool hit           = PointSphereDistance(pointShifted, in sphereShifted, maxDistance, out var localResult, out _);
            result             = new PointDistanceResult
            {
                hitpoint         = localResult.hitpoint + sphereTransform.pos,
                normal           = localResult.normal,
                distance         = localResult.distance,
                subColliderIndex = 0
            };
            return hit;
        }

        public static bool Raycast(in Ray ray, in SphereCollider sphere, in RigidTransform sphereTransform, out RaycastResult result)
        {
            var  rayStart                           = ray.start - sphereTransform.pos;
            var  sphereInWorldPositionRelativeSpace = new SphereCollider(math.rotate(sphereTransform.rot, sphere.center), sphere.radius);
            bool hit                                = RaycastSphere(rayStart, ray.displacement, in sphereInWorldPositionRelativeSpace, out var fraction, out var normal);
            result.position                         = ray.start + ray.displacement * fraction;
            result.normal                           = normal;
            result.distance                         = fraction * math.length(ray.displacement);
            result.subColliderIndex                 = 0;
            return hit;
        }

        public static bool PointSphereDistance(float3 point, in SphereCollider sphere, float maxDistance, out PointDistanceResultInternal result, out bool degenerate)
        {
            float3 delta        = sphere.center - point;
            float  pcDistanceSq = math.lengthsq(delta);  // point center distance
            float  pcDistance   = math.sqrt(pcDistanceSq);
            degenerate          = pcDistanceSq < kUnmeasurableLengthSqThreshold;
            float3 inNormal     = math.select(delta / pcDistance, new float3(0, -1, 0), degenerate);  // choose an arbitrary normal when the distance is near zero
            float  distance     = pcDistance - sphere.radius;
            result              = new PointDistanceResultInternal
            {
                hitpoint = point + inNormal * distance,
                distance = distance,
                normal   = -inNormal,
            };
            return distance <= maxDistance;
        }

        // Todo: Phase this out.
        public static bool RaycastSphere(in Ray ray, in SphereCollider sphere, out float fraction, out float3 normal)
        {
            return RaycastSphere(ray.start, ray.displacement, in sphere, out fraction, out normal);
        }

        public static bool RaycastSphere(float3 rayStart, float3 rayDisplacement, in SphereCollider sphere, out float fraction, out float3 normal)
        {
            // Somewhat based on Ray tracing gems Ch 7 by Eric Haines Johannes Gunther and Tomas Akenine-Moller.
            // This improves precision by not squaring the distance from the ray to the center directly.
            // Let's shift everything so that the sphere is at the origin.
            var shiftedRayStart = rayStart - sphere.center;
            // Then, let's imagine a ray that cuts through the sphere. There are three points along the ray of interest: entry, closestToCenter, and exit.
            // The origin, entry, and closestToCenter create a right triangle. origin-to-entry is the hypotenus of length radius.
            // Thus, if we know lengthSq(closestToCenter), we can compute the distance between closestToCenter and entry. And if we know the distance between
            // the shiftedRayStart and closestToCenter, then we can calculate the distance from shiftedRayStart to entry.
            // That means we need to calculate where closestToCenter is. Fortunately, that is as simple as projecting the vector from shiftedRayStart to the origin onto the ray.
            var radiusSq    = sphere.radius * sphere.radius;
            var rayLengthSq = math.lengthsq(rayDisplacement);
            // Our vector to project onto the ray is -shiftedRayStart.
            var projectionScalar   = -math.dot(rayDisplacement, shiftedRayStart) / rayLengthSq;
            var projection         = projectionScalar * rayDisplacement;
            var closestToCenter    = shiftedRayStart + projection;
            var entryClosestDistSq = radiusSq - math.lengthsq(closestToCenter);
            // The following is the base derivation, but contains 2 sqrt operations we can eliminate
            // var rayLength = math.sqrt(rayLengthSq);
            // var startClosestDist = projectionScalar * rayLength;
            // var hitDistance = startClosestDist - math.sqrt(math.abs(entryClosestDistSq));
            // fraction = hitDistance / rayLength;

            // When computing fraction, substitute the formula for hitDistance directly, and distribute:
            // fraction = startClosestDist / rayLength - math.sqrt(math.abs(entryClosestDistSq)) / rayLength
            // Substitute the formula for startClosestDist, and rayLength cancels out. Move the other rayLength
            // into the sqrt by squaring it.
            fraction           = projectionScalar - math.sqrt(entryClosestDistSq / rayLengthSq);
            var entryPoint     = shiftedRayStart + fraction * rayDisplacement;
            var entryRadiusSq  = math.lengthsq(entryPoint);  // Not the same as radius because of floating-point error
            normal             = entryPoint / math.sqrt(entryRadiusSq);
            var fallbackNormal = -rayDisplacement / math.sqrt(rayLengthSq);
            normal             = math.select(normal, fallbackNormal, entryRadiusSq < kUnmeasurableLengthSqThreshold);
            return entryClosestDistSq >= 0f && fraction > 0f && fraction <= 1f;
        }

        public static bool4 Raycast4Spheres(in simdFloat3 rayStart, in simdFloat3 rayDisplacement, in simdFloat3 center, float4 radius, out float4 fraction, out simdFloat3 normal)
        {
            var shiftedRayStart    = rayStart - center;
            var radiusSq           = radius * radius;
            var rayLengthSq        = simd.lengthsq(rayDisplacement);
            var projectionScalar   = -simd.dot(rayDisplacement, shiftedRayStart) / rayLengthSq;
            var projection         = projectionScalar * rayDisplacement;
            var closestToCenter    = shiftedRayStart + projection;
            var entryClosestDistSq = radiusSq - simd.lengthsq(closestToCenter);
            fraction               = projectionScalar - math.sqrt(entryClosestDistSq / rayLengthSq);
            var entryPoint         = shiftedRayStart + fraction * rayDisplacement;
            var entryRadiusSq      = simd.lengthsq(entryPoint);  // Not the same as radius because of floating-point error
            normal                 = entryPoint / math.sqrt(entryRadiusSq);
            var fallbackNormal     = -rayDisplacement / math.sqrt(rayLengthSq);
            normal                 = simd.select(normal, fallbackNormal, entryRadiusSq < kUnmeasurableLengthSqThreshold);
            return (entryClosestDistSq >= 0f) & (fraction > 0f) & (fraction <= 1f);
        }
    }
}

