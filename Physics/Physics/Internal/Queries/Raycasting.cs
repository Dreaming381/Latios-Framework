using Unity.Mathematics;

namespace Latios.PhysicsEngine
{
    internal static class Raycasting
    {
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
    }
}

