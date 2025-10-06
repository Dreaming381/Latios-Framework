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
            foreach (var i in new CompoundAabbEnumerator(new SphereCollider(point, 0f), RigidTransform.identity, compound, compoundTransform))
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
            bool hit        = false;
            foreach (var i in new CompoundAabbEnumerator(Physics.AabbFrom(in ray), compound, compoundTransform))
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

        internal struct CompoundAabbEnumerator
        {
            Aabb             queryAabbInCompoundSpace;
            CompoundCollider compound;
            int              currentIndex;

            public CompoundAabbEnumerator GetEnumerator() => this;

            public int Current => compound.compoundColliderBlob.Value.sourceIndices[currentIndex];

            public CompoundAabbEnumerator(in Aabb queryAabb, in CompoundCollider compound, in RigidTransform compoundTransform)
            {
                Physics.GetCenterExtents(queryAabb, out var c, out var e);
                var box = new BoxCollider(c, e);
                this    = new CompoundAabbEnumerator(box, RigidTransform.identity, compound, compoundTransform);
            }

            public CompoundAabbEnumerator(in Collider queryCollider, in RigidTransform queryTransform, in CompoundCollider compound, in RigidTransform compoundTransform)
            {
                var queryInCompoundSpace  = math.mul(math.inverse(compoundTransform), queryTransform);
                var queryAabb             = Physics.AabbFrom(in queryCollider, queryInCompoundSpace);
                var inverseScale          = math.rcp(compound.scale * compound.stretch);
                queryAabb.min            *= inverseScale;
                queryAabb.max            *= inverseScale;
                queryAabb.min             = math.select(queryAabb.min, float.MinValue, math.isnan(queryAabb.min));
                queryAabb.max             = math.select(queryAabb.max, float.MaxValue, math.isnan(queryAabb.max));
                var radial                = compound.stretchMode switch
                {
                    CompoundCollider.StretchMode.RotateStretchLocally => compound.scale * compound.compoundColliderBlob.Value.maxOffsetFromAnchors *
                    math.cmax(math.abs(compound.stretch)),
                    CompoundCollider.StretchMode.IgnoreStretch => compound.compoundColliderBlob.Value.maxOffsetFromAnchors,
                    CompoundCollider.StretchMode.StretchPositionsOnly => compound.scale * compound.compoundColliderBlob.Value.maxOffsetFromAnchors,
                    _ => 0f
                };
                queryAabb.min            -= radial;
                queryAabb.max            += radial;
                queryAabbInCompoundSpace  = queryAabb;
                this.compound             = compound;
                currentIndex              = Calci.BinarySearch.FirstGreaterOrEqual(compound.compoundColliderBlob.Value.boundingSphereCenterXs.AsSpan(), queryAabb.min.x) - 1;
            }

            public bool MoveNext()
            {
                ref var blob = ref compound.compoundColliderBlob.Value;
                while (true)
                {
                    currentIndex++;
                    if (currentIndex >= blob.boundingSphereCenterXs.Length)
                        return false;
                    var sourceIndex = blob.sourceIndices[currentIndex];
                    var position    = blob.transforms[sourceIndex].pos;
                    if (position.x > queryAabbInCompoundSpace.max.x)
                        return false;

                    var test = new bool4(position.yz < queryAabbInCompoundSpace.min.yz, position.yz > queryAabbInCompoundSpace.max.yz);
                    if (!math.any(test))
                        return true;
                }
            }
        }
    }
}

