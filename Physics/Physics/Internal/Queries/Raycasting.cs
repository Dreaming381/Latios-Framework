using Unity.Mathematics;

namespace Latios.PhysicsEngine
{
    internal static class Raycasting
    {
        public static bool RaycastAabb(Ray ray, Aabb aabb, out float fraction)
        {
            //slab clipping method
            float3 l     = aabb.min - ray.start;
            float3 h     = aabb.max - ray.start;
            float3 nearT = l * ray.reciprocalDisplacement;
            float3 farT  = h * ray.reciprocalDisplacement;

            float3 near = math.min(nearT, farT);
            float3 far  = math.max(nearT, farT);

            float nearMax = math.cmax(math.float4(near, 0f));
            float farMin  = math.cmin(math.float4(far, 1f));

            fraction = nearMax;

            return (nearMax <= farMin) & (l.x <= h.x);
        }

        public static bool RaycastSphere(Ray ray, SphereCollider sphere, out float fraction, out float3 normal)
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

        public static bool4 Raycast4Spheres(Ray ray, float4 xCenter, float4 yCenter, float4 zCenter, float radius, out float4 fractions)
        {
            float4 deltaX           = ray.start.x - xCenter;
            float4 deltaY           = ray.start.y - yCenter;
            float4 deltaZ           = ray.start.z - zCenter;
            float4 a                = math.dot(ray.displacement, ray.displacement);
            float4 b                = 2f * (ray.displacement.x * deltaX + ray.displacement.y * deltaY + ray.displacement.z * deltaZ);
            float4 c                = deltaX * deltaX + deltaY * deltaY + deltaZ * deltaZ - radius * radius;
            float4 discriminant     = b * b - 4f * a * c;
            bool4  hit              = discriminant >= 0f & c >= 0f;  //Unlike Unity.Physics, we ignore inside hits.
            discriminant            = math.abs(discriminant);
            float4 sqrtDiscriminant = math.sqrt(discriminant);
            float4 root1            = (-b - sqrtDiscriminant) / (2f * a);
            float4 root2            = (-b + sqrtDiscriminant) / (2f * a);
            float4 rootmin          = math.min(root1, root2);
            float4 rootmax          = math.max(root1, root2);
            bool4  rootminValid     = rootmin >= 0f & rootmin <= 1f;
            bool4  rootmaxValid     = rootmax >= 0f & rootmax <= 1f;
            fractions               = math.select(rootmax, rootmin, rootminValid);
            bool4 aRootIsValid      = rootminValid | rootmaxValid;
            return hit & aRootIsValid;
        }

        public static bool RaycastCapsule(Ray ray, CapsuleCollider capsule, out float fraction, out float3 normal)
        {
            float          axisLength = mathex.getLengthAndNormal(capsule.pointB - capsule.pointA, out float3 axis);
            SphereCollider sphere1    = new SphereCollider(capsule.pointA, capsule.radius);

            // Ray vs infinite cylinder
            {
                float  directionDotAxis  = math.dot(ray.displacement, axis);
                float  originDotAxis     = math.dot(ray.start - capsule.pointA, axis);
                float3 rayDisplacement2D = ray.displacement - axis * directionDotAxis;
                float3 rayOrigin2D       = ray.start - axis * originDotAxis;
                Ray    rayIn2d           = new Ray(rayOrigin2D, rayOrigin2D + rayDisplacement2D);

                if (RaycastSphere(rayIn2d, sphere1, out float cylinderFraction, out normal))
                {
                    float t = originDotAxis + cylinderFraction * directionDotAxis;  // distance of the hit from Vertex0 along axis
                    if (t >= 0.0f && t <= axisLength)
                    {
                        fraction = cylinderFraction;
                        return true;
                    }
                }
            }

            //Ray vs caps
            SphereCollider sphere2 = new SphereCollider(capsule.pointB, capsule.radius);
            bool           hit1    = RaycastSphere(ray, sphere1, out float fraction1, out float3 normal1);
            bool           hit2    = RaycastSphere(ray, sphere2, out float fraction2, out float3 normal2);
            fraction1              = hit1 ? fraction1 : fraction1 + 1f;
            fraction2              = hit2 ? fraction2 : fraction2 + 1f;
            fraction               = math.select(fraction2, fraction2, fraction1 < fraction2);
            normal                 = math.select(normal2, normal1, fraction1 < fraction2);
            return hit1 | hit2;
        }

        public static bool4 Raycast2d4Circles(Ray2d ray, float4 xCenter, float4 yCenter, float radius, out float4 fractions)
        {
            float4 deltaX           = ray.start.x - xCenter;
            float4 deltaY           = ray.start.y - yCenter;
            float4 a                = math.dot(ray.displacement, ray.displacement);
            float4 b                = 2f * (ray.displacement.x * deltaX + ray.displacement.y * deltaY);
            float4 c                = deltaX * deltaX + deltaY * deltaY - radius * radius;
            float4 discriminant     = b * b - 4f * a * c;
            bool4  hit              = discriminant >= 0f & c >= 0f;  //Unlike Unity.Physics, we ignore inside hits.
            discriminant            = math.abs(discriminant);
            float4 sqrtDiscriminant = math.sqrt(discriminant);
            float4 root1            = (-b - sqrtDiscriminant) / (2f * a);
            float4 root2            = (-b + sqrtDiscriminant) / (2f * a);
            float4 rootmin          = math.min(root1, root2);
            float4 rootmax          = math.max(root1, root2);
            bool4  rootminValid     = rootmin >= 0f & rootmin <= 1f;
            bool4  rootmaxValid     = rootmax >= 0f & rootmax <= 1f;
            fractions               = math.select(rootmax, rootmin, rootminValid);
            bool4 aRootIsValid      = rootminValid | rootmaxValid;
            return hit & aRootIsValid;
        }
    }
}

