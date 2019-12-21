using System;
using Unity.Mathematics;

namespace Latios.PhysicsEngine
{
    public static partial class Physics
    {
        //Todo: No need to apply rotation to ray for sphere.
        public static bool Raycast(Ray ray, SphereCollider sphere, RigidTransform sphereTransform, out RaycastResult result)
        {
            var  rayInSphereSpace = Ray.TransformRay(math.inverse(sphereTransform), ray);
            bool hit              = Raycasting.RaycastSphere(rayInSphereSpace, sphere, out float fraction, out float3 normal);
            result.position       = math.lerp(ray.start, ray.end, fraction);
            result.normal         = math.rotate(sphereTransform, normal);
            result.distance       = math.distance(ray.start, result.position);
            return hit;
        }

        public static bool Raycast(Ray ray, CapsuleCollider capsule, RigidTransform capsuleTransform, out RaycastResult result)
        {
            var  rayInCapsuleSpace = Ray.TransformRay(math.inverse(capsuleTransform), ray);
            bool hit               = Raycasting.RaycastCapsule(rayInCapsuleSpace, capsule, out float fraction, out float3 normal);
            result.position        = math.lerp(ray.start, ray.end, fraction);
            result.normal          = math.rotate(capsuleTransform, normal);
            result.distance        = math.distance(ray.start, result.position);
            return hit;
        }
    }
}

