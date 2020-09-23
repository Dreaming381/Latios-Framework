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

        public static bool Raycast(Ray ray, CompoundCollider compound, RigidTransform compoundTransform, out RaycastResult result)
        {
            result                     = default;
            result.distance            = float.MaxValue;
            bool    hit                = false;
            var     rayInCompoundSpace = Ray.TransformRay(math.inverse(compoundTransform), ray);
            var     scaledRay          = new Ray(rayInCompoundSpace.start / compound.scale, rayInCompoundSpace.end / compound.scale);
            ref var blob               = ref compound.compoundColliderBlob.Value;
            for (int i = 0; i < blob.colliders.Length; i++)
            {
                var newHit  = Raycast(scaledRay, blob.colliders[i], blob.transforms[i], out var newResult);
                newHit     &= newResult.distance < result.distance;
                hit        |= newHit;
                result      = newHit ? newResult : result;
            }
            return hit;
        }
    }
}

