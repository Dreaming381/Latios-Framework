using Unity.Burst;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class PointRaySphere
    {
        public static bool DistanceBetween(float3 point, in SphereCollider sphere, in RigidTransform sphereTransform, float maxDistance, out PointDistanceResult result)
        {
            var  pointInSphereSpace = math.transform(math.inverse(sphereTransform), point);
            bool hit                = PointSphereDistance(pointInSphereSpace, in sphere, maxDistance, out var localResult, out _);
            result                  = new PointDistanceResult
            {
                hitpoint = math.transform(sphereTransform, localResult.hitpoint),
                normal   = math.rotate(sphereTransform, localResult.normal),
                distance = localResult.distance
            };
            return hit;
        }

        public static bool Raycast(in Ray ray, in SphereCollider sphere, in RigidTransform sphereTransform, out RaycastResult result)
        {
            //Todo: No need to apply rotation to ray for sphere.
            var  rayInSphereSpace   = Ray.TransformRay(math.inverse(sphereTransform), ray);
            bool hit                = RaycastSphere(in rayInSphereSpace, in sphere, out float fraction, out float3 normal);
            result.position         = math.lerp(ray.start, ray.end, fraction);
            result.normal           = math.rotate(sphereTransform, normal);
            result.distance         = math.distance(ray.start, result.position);
            result.subColliderIndex = 0;
            return hit;
        }

        public static bool PointSphereDistance(float3 point, in SphereCollider sphere, float maxDistance, out PointDistanceResultInternal result, out bool degenerate)
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
            degenerate = distanceIsZero;
            return distance <= maxDistance;
        }

        public static bool RaycastSphere(in Ray ray, in SphereCollider sphere, out float fraction, out float3 normal)
        {
            float3 delta           = ray.start - sphere.center;
            float  a               = math.dot(ray.displacement, ray.displacement);
            float  b               = 2f * math.dot(ray.displacement, delta);
            float  c               = math.dot(delta, delta) - sphere.radius * sphere.radius;
            float  discriminant    = b * b - 4f * a * c;
            bool   hit             = discriminant >= 0f & c >= 0f;  //Unlike Unity.Physics, we ignore inside hits.
            discriminant           = math.abs(discriminant);
            float sqrtDiscriminant = math.sqrt(discriminant);
            float root1            = (-b - sqrtDiscriminant) / (2f * a);
            float root2            = (-b + sqrtDiscriminant) / (2f * a);
            float rootmin          = math.min(root1, root2);
            float rootmax          = math.max(root1, root2);
            bool  rootminValid     = rootmin >= 0f & rootmin <= 1f;
            bool  rootmaxValid     = rootmax >= 0f & rootmax <= 1f;
            fraction               = math.select(rootmax, rootmin, rootminValid);
            normal                 = (delta + ray.displacement * fraction) / sphere.radius;  //hit point to center divided by radius = normalize normal of sphere at hit
            bool aRootIsValid      = rootminValid | rootmaxValid;
            return hit & aRootIsValid;
        }

        public static bool4 Raycast4Spheres(in simdFloat3 rayStart, in simdFloat3 rayDisplacement, in simdFloat3 center, float4 radius, out float4 fraction, out simdFloat3 normal)
        {
            simdFloat3 delta        = rayStart - center;
            float4     a            = simd.dot(rayDisplacement, rayDisplacement);
            float4     b            = 2f * simd.dot(rayDisplacement, delta);
            float4     c            = simd.dot(delta, delta) - radius * radius;
            float4     discriminant = b * b - 4f * a * c;
            bool4      hit          = discriminant >= 0f & c >= 0f;  //Unlike Unity.Physics, we ignore inside hits.
            discriminant            = math.abs(discriminant);
            float4 sqrtDiscriminant = math.sqrt(discriminant);
            float4 root1            = (-b - sqrtDiscriminant) / (2f * a);
            float4 root2            = (-b + sqrtDiscriminant) / (2f * a);
            float4 rootmin          = math.min(root1, root2);
            float4 rootmax          = math.max(root1, root2);
            bool4  rootminValid     = rootmin >= 0f & rootmin <= 1f;
            bool4  rootmaxValid     = rootmax >= 0f & rootmax <= 1f;
            fraction                = math.select(rootmax, rootmin, rootminValid);
            normal                  = (delta + rayDisplacement * fraction) / radius;
            bool4 aRootIsValid      = rootminValid | rootmaxValid;
            return hit & aRootIsValid;
        }
    }
}

