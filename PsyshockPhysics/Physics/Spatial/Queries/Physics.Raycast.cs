using System;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    public static partial class Physics
    {
        public static bool Raycast(float3 start, float3 end, in SphereCollider sphere, in RigidTransform sphereTransform, out RaycastResult result)
        {
            return Raycast(new Ray(start, end), in sphere, in sphereTransform, out result);
        }

        public static bool Raycast(Ray ray, in SphereCollider sphere, in RigidTransform sphereTransform, out RaycastResult result)
        {
            //Todo: No need to apply rotation to ray for sphere.
            var  rayInSphereSpace   = Ray.TransformRay(math.inverse(sphereTransform), ray);
            bool hit                = SpatialInternal.RaycastSphere(rayInSphereSpace, sphere, out float fraction, out float3 normal);
            result.position         = math.lerp(ray.start, ray.end, fraction);
            result.normal           = math.rotate(sphereTransform, normal);
            result.distance         = math.distance(ray.start, result.position);
            result.subColliderIndex = 0;
            return hit;
        }

        public static bool Raycast(float3 start, float3 end, in CapsuleCollider capsule, in RigidTransform capsuleTransform, out RaycastResult result)
        {
            return Raycast(new Ray(start, end), in capsule, in capsuleTransform, out result);
        }

        public static bool Raycast(Ray ray, in CapsuleCollider capsule, in RigidTransform capsuleTransform, out RaycastResult result)
        {
            var  rayInCapsuleSpace  = Ray.TransformRay(math.inverse(capsuleTransform), ray);
            bool hit                = SpatialInternal.RaycastCapsule(rayInCapsuleSpace, capsule, out float fraction, out float3 normal);
            result.position         = math.lerp(ray.start, ray.end, fraction);
            result.normal           = math.rotate(capsuleTransform, normal);
            result.distance         = math.distance(ray.start, result.position);
            result.subColliderIndex = 0;
            return hit;
        }

        public static bool Raycast(float3 start, float3 end, in BoxCollider box, in RigidTransform boxTransform, out RaycastResult result)
        {
            return Raycast(new Ray(start, end), in box, in boxTransform, out result);
        }

        public static bool Raycast(Ray ray, in BoxCollider box, in RigidTransform boxTransform, out RaycastResult result)
        {
            var  rayInBoxSpace      = Ray.TransformRay(math.inverse(boxTransform), ray);
            bool hit                = SpatialInternal.RaycastBox(rayInBoxSpace, box, out float fraction, out float3 normal);
            result.position         = math.lerp(ray.start, ray.end, fraction);
            result.normal           = math.rotate(boxTransform, normal);
            result.distance         = math.distance(ray.start, result.position);
            result.subColliderIndex = 0;
            return hit;
        }

        public static bool Raycast(float3 start, float3 end, in TriangleCollider triangle, in RigidTransform triangleTransform, out RaycastResult result)
        {
            return Raycast(new Ray(start, end), in triangle, in triangleTransform, out result);
        }

        public static bool Raycast(Ray ray, in TriangleCollider triangle, in RigidTransform triangleTransform, out RaycastResult result)
        {
            var  rayInTriangleSpace = Ray.TransformRay(math.inverse(triangleTransform), ray);
            bool hit                = SpatialInternal.RaycastTriangle(rayInTriangleSpace,
                                                                      new simdFloat3(triangle.pointA, triangle.pointB, triangle.pointC, triangle.pointC),
                                                                      out float fraction,
                                                                      out float3 normal);
            result.position         = math.lerp(ray.start, ray.end, fraction);
            result.normal           = math.rotate(triangleTransform, normal);
            result.distance         = math.distance(ray.start, result.position);
            result.subColliderIndex = 0;
            return hit;
        }

        public static bool Raycast(float3 start, float3 end, in ConvexCollider convex, in RigidTransform convexTransform, out RaycastResult result)
        {
            return Raycast(new Ray(start, end), in convex, in convexTransform, out result);
        }

        public static bool Raycast(Ray ray, in ConvexCollider convex, in RigidTransform convexTransform, out RaycastResult result)
        {
            var  rayInConvexSpace   = Ray.TransformRay(math.inverse(convexTransform), ray);
            bool hit                = SpatialInternal.RaycastConvex(rayInConvexSpace, convex, out float fraction, out float3 normal);
            result.position         = math.lerp(ray.start, ray.end, fraction);
            result.normal           = math.rotate(convexTransform, normal);
            result.distance         = math.distance(ray.start, result.position);
            result.subColliderIndex = 0;
            return hit;
        }

        public static bool Raycast(float3 start, float3 end, in CompoundCollider compound, in RigidTransform compoundTransform, out RaycastResult result)
        {
            return Raycast(new Ray(start, end), in compound, in compoundTransform, out result);
        }

        public static bool Raycast(Ray ray, in CompoundCollider compound, in RigidTransform compoundTransform, out RaycastResult result)
        {
            result                     = default;
            result.distance            = float.MaxValue;
            bool    hit                = false;
            var     rayInCompoundSpace = Ray.TransformRay(math.inverse(compoundTransform), ray);
            var     scaledRay          = new Ray(rayInCompoundSpace.start / compound.scale, rayInCompoundSpace.end / compound.scale);
            ref var blob               = ref compound.compoundColliderBlob.Value;
            for (int i = 0; i < blob.colliders.Length; i++)
            {
                var newHit                  = Raycast(scaledRay, blob.colliders[i], blob.transforms[i], out var newResult);
                newResult.subColliderIndex  = i;
                newHit                     &= newResult.distance < result.distance;
                hit                        |= newHit;
                result                      = newHit ? newResult : result;
            }
            return hit;
        }

        public static bool Raycast(float3 start, float3 end, in CollisionLayer layer, out RaycastResult result, out LayerBodyInfo layerBodyInfo)
        {
            return Raycast(new Ray(start, end), in layer, out result, out layerBodyInfo);
        }

        public static bool Raycast(Ray ray, in CollisionLayer layer, out RaycastResult result, out LayerBodyInfo layerBodyInfo)
        {
            result        = default;
            layerBodyInfo = default;
            var processor = new LayerQueryProcessors.RaycastClosestImmediateProcessor(ray, ref result, ref layerBodyInfo);
            FindObjects(AabbFrom(ray), layer, processor).RunImmediate();
            var hit                 = result.subColliderIndex >= 0;
            result.subColliderIndex = math.max(result.subColliderIndex, 0);
            return hit;
        }

        public static bool RaycastAny(float3 start, float3 end, in CollisionLayer layer, out RaycastResult result, out LayerBodyInfo layerBodyInfo)
        {
            return RaycastAny(new Ray(start, end), in layer, out result, out layerBodyInfo);
        }

        public static bool RaycastAny(Ray ray, in CollisionLayer layer, out RaycastResult result, out LayerBodyInfo layerBodyInfo)
        {
            result        = default;
            layerBodyInfo = default;
            var processor = new LayerQueryProcessors.RaycastAnyImmediateProcessor(ray, ref result, ref layerBodyInfo);
            FindObjects(AabbFrom(ray), layer, processor).RunImmediate();
            var hit                 = result.subColliderIndex >= 0;
            result.subColliderIndex = math.max(result.subColliderIndex, 0);
            return hit;
        }
    }
}

