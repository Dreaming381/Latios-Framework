using Unity.Burst;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class PointRayCompound
    {
        public static bool DistanceBetween(float3 point, in CompoundCollider compound, in RigidTransform compoundTransform, float maxDistance, out PointDistanceResult result)
        {
            bool hit        = false;
            result          = default;
            result.distance = float.MaxValue;
            ref var blob    = ref compound.compoundColliderBlob.Value;
            for (int i = 0; i < blob.colliders.Length; i++)
            {
                compound.GetScaledStretchedSubCollider(i, out var blobCollider, out var blobTransform);
                bool newHit = DistanceBetween(point,
                                              in blobCollider,
                                              math.mul(compoundTransform, blobTransform),
                                              math.min(result.distance, maxDistance),
                                              out var newResult);

                newResult.subColliderIndex  = i;
                newHit                     &= newResult.distance < result.distance;
                hit                        |= newHit;
                result                      = newHit ? newResult : result;
            }
            return hit;
        }

        public static bool Raycast(in Ray ray, in CompoundCollider compound, in RigidTransform compoundTransform, out RaycastResult result)
        {
            // Note: Each collider in the compound may evaluate the ray in its local space,
            // so it is better to keep the ray in world-space relative to the blob so that the result is in the right space.
            // Todo: Is the cost of transforming each collider to world space worth it?
            result          = default;
            result.distance = float.MaxValue;
            bool    hit     = false;
            ref var blob    = ref compound.compoundColliderBlob.Value;
            for (int i = 0; i < blob.colliders.Length; i++)
            {
                compound.GetScaledStretchedSubCollider(i, out var blobCollider, out var blobTransform);
                var newHit                  = Raycast(in ray, in blobCollider, math.mul(compoundTransform, blobTransform), out var newResult);
                newResult.subColliderIndex  = i;
                newHit                     &= newResult.distance < result.distance;
                hit                        |= newHit;
                result                      = newHit ? newResult : result;
            }
            return hit;
        }

        // We use a reduced set dispatch here so that Burst doesn't have to try to make these methods re-entrant.
        private static bool DistanceBetween(float3 point, in Collider collider, in RigidTransform transform, float maxDistance, out PointDistanceResult result)
        {
            switch (collider.type)
            {
                case ColliderType.Sphere:
                    return PointRaySphere.DistanceBetween(point, in collider.m_sphere, in transform, maxDistance, out result);
                case ColliderType.Capsule:
                    return PointRayCapsule.DistanceBetween(point, in collider.m_capsule, in transform, maxDistance, out result);
                case ColliderType.Box:
                    return PointRayBox.DistanceBetween(point, in collider.m_box, in transform, maxDistance, out result);
                default:
                    result = default;
                    return false;
            }
        }

        private static bool Raycast(in Ray ray, in Collider collider, in RigidTransform transform, out RaycastResult result)
        {
            switch (collider.type)
            {
                case ColliderType.Sphere:
                    return PointRaySphere.Raycast(in ray, in collider.m_sphere, in transform, out result);
                case ColliderType.Capsule:
                    return PointRayCapsule.Raycast(in ray, in collider.m_capsule, in transform, out result);
                case ColliderType.Box:
                    return PointRayBox.Raycast(in ray, in collider.m_box, in transform, out result);
                default:
                    result = default;
                    return false;
            }
        }
    }
}

