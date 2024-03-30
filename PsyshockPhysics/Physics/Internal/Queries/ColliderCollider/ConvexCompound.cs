using Unity.Burst;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class ConvexCompound
    {
        public static bool DistanceBetween(in CompoundCollider compound,
                                           in RigidTransform compoundTransform,
                                           in ConvexCollider convex,
                                           in RigidTransform convexTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            bool hit        = false;
            result          = default;
            result.distance = float.MaxValue;
            ref var blob    = ref compound.compoundColliderBlob.Value;
            for (int i = 0; i < blob.colliders.Length; i++)
            {
                compound.GetScaledStretchedSubCollider(i, out var blobCollider, out var blobTransform);
                bool newHit = DistanceBetween(in blobCollider,
                                              math.mul(compoundTransform, blobTransform),
                                              in convex,
                                              in convexTransform,
                                              maxDistance,
                                              out var newResult);

                newResult.subColliderIndexA  = i;
                newHit                      &= newResult.distance < result.distance;
                hit                         |= newHit;
                result                       = newHit ? newResult : result;
            }
            return hit;
        }

        public static void DistanceBetweenAll<T>(in CompoundCollider compound,
                                                 in RigidTransform compoundTransform,
                                                 in ConvexCollider convex,
                                                 in RigidTransform convexTransform,
                                                 float maxDistance,
                                                 ref T processor) where T : unmanaged, IDistanceBetweenAllProcessor
        {
            ref var blob = ref compound.compoundColliderBlob.Value;
            for (int i = 0; i < blob.colliders.Length; i++)
            {
                compound.GetScaledStretchedSubCollider(i, out var blobCollider, out var blobTransform);
                bool newHit = DistanceBetween(in blobCollider,
                                              math.mul(compoundTransform, blobTransform),
                                              in convex,
                                              in convexTransform,
                                              maxDistance,
                                              out var newResult);

                newResult.subColliderIndexA = i;

                if (newHit)
                    processor.Execute(in newResult);
            }
        }

        public static bool ColliderCast(in ConvexCollider convexToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in CompoundCollider targetCompound,
                                        in RigidTransform targetCompoundTransform,
                                        out ColliderCastResult result)
        {
            bool hit        = false;
            result          = default;
            result.distance = float.MaxValue;
            if (DistanceBetween(in targetCompound, in targetCompoundTransform, in convexToCast, in castStart, 0f, out _))
            {
                return false;
            }
            ref var blob = ref targetCompound.compoundColliderBlob.Value;
            for (int i = 0; i < blob.colliders.Length; i++)
            {
                targetCompound.GetScaledStretchedSubCollider(i, out var blobCollider, out var blobTransform);
                bool newHit = ColliderCast(in convexToCast, in castStart, castEnd, in blobCollider,
                                           math.mul(targetCompoundTransform, blobTransform),
                                           out var newResult);

                newResult.subColliderIndexOnTarget  = i;
                newHit                             &= newResult.distance < result.distance;
                hit                                |= newHit;
                result                              = newHit ? newResult : result;
            }
            return hit;
        }

        public static bool ColliderCast(in CompoundCollider compoundToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in ConvexCollider targetConvex,
                                        in RigidTransform targetConvexTransform,
                                        out ColliderCastResult result)
        {
            bool hit        = false;
            result          = default;
            result.distance = float.MaxValue;
            if (DistanceBetween(in compoundToCast, in castStart, in targetConvex, in targetConvexTransform, 0f, out _))
            {
                return false;
            }
            ref var blob = ref compoundToCast.compoundColliderBlob.Value;
            for (int i = 0; i < blob.colliders.Length; i++)
            {
                compoundToCast.GetScaledStretchedSubCollider(i, out var blobCollider, out var blobTransform);
                var  start  = math.mul(castStart, blobTransform);
                bool newHit = ColliderCast(in blobCollider,
                                           start,
                                           start.pos + (castEnd - castStart.pos),
                                           in targetConvex,
                                           in targetConvexTransform,
                                           out var newResult);

                newResult.subColliderIndexOnCaster  = i;
                newHit                             &= newResult.distance < result.distance;
                hit                                |= newHit;
                result                              = newHit ? newResult : result;
            }
            return hit;
        }

        public static UnitySim.ContactsBetweenResult UnityContactsBetween(in CompoundCollider compound,
                                                                          in RigidTransform compoundTransform,
                                                                          in ConvexCollider convex,
                                                                          in RigidTransform convexTransform,
                                                                          in ColliderDistanceResult distanceResult)
        {
            compound.GetScaledStretchedSubCollider(distanceResult.subColliderIndexA, out var collider, out var colliderTransform);
            colliderTransform = math.mul(compoundTransform, colliderTransform);
            return collider.type switch
                   {
                       ColliderType.Sphere => ContactManifoldHelpers.GetSingleContactManifold(in distanceResult),
                       ColliderType.Capsule => CapsuleConvex.UnityContactsBetween(in convex, in convexTransform, in collider.m_capsule, in colliderTransform,
                                                                                  distanceResult.ToFlipped()).ToFlipped(),
                       ColliderType.Box => BoxConvex.UnityContactsBetween(in convex, in convexTransform, in collider.m_box, in colliderTransform,
                                                                          distanceResult.ToFlipped()).ToFlipped(),
                       _ => ContactManifoldHelpers.GetSingleContactManifold(in distanceResult)
                   };
        }

        // We use a reduced set dispatch here so that Burst doesn't have to try to make these methods re-entrant.
        private static bool DistanceBetween(in Collider collider,
                                            in RigidTransform colliderTransform,
                                            in ConvexCollider convex,
                                            in RigidTransform convexTransform,
                                            float maxDistance,
                                            out ColliderDistanceResult result)
        {
            switch (collider.type)
            {
                case ColliderType.Sphere:
                    var sphereResult = SphereConvex.DistanceBetween(in convex,
                                                                    in convexTransform,
                                                                    in collider.m_sphere,
                                                                    in colliderTransform,
                                                                    maxDistance,
                                                                    out result);
                    result.FlipInPlace();
                    return sphereResult;
                case ColliderType.Capsule:
                    var capsuleResult = CapsuleConvex.DistanceBetween(in convex,
                                                                      in convexTransform,
                                                                      in collider.m_capsule,
                                                                      in colliderTransform,
                                                                      maxDistance,
                                                                      out result);
                    result.FlipInPlace();
                    return capsuleResult;
                case ColliderType.Box:
                    var boxResult = BoxConvex.DistanceBetween(in convex,
                                                              in convexTransform,
                                                              in collider.m_box,
                                                              in colliderTransform,
                                                              maxDistance,
                                                              out result);
                    result.FlipInPlace();
                    return boxResult;
                default:
                    result = default;
                    return false;
            }
        }

        private static bool ColliderCast(in ConvexCollider convexToCast,
                                         in RigidTransform castStart,
                                         float3 castEnd,
                                         in Collider target,
                                         in RigidTransform targetTransform,
                                         out ColliderCastResult result)
        {
            switch (target.type)
            {
                case ColliderType.Sphere:
                    return SphereConvex.ColliderCast(in convexToCast, in castStart, castEnd, in target.m_sphere, in targetTransform, out result);
                case ColliderType.Capsule:
                    return CapsuleConvex.ColliderCast(in convexToCast, in castStart, castEnd, in target.m_capsule, in targetTransform, out result);
                case ColliderType.Box:
                    return BoxConvex.ColliderCast(in convexToCast, in castStart, castEnd, in target.m_box, in targetTransform, out result);
                default:
                    result = default;
                    return false;
            }
        }

        private static bool ColliderCast(in Collider colliderToCast,
                                         in RigidTransform castStart,
                                         float3 castEnd,
                                         in ConvexCollider targetConvex,
                                         in RigidTransform targetConvexTransform,
                                         out ColliderCastResult result)
        {
            switch (colliderToCast.type)
            {
                case ColliderType.Sphere:
                    return SphereConvex.ColliderCast(in colliderToCast.m_sphere, in castStart, castEnd, in targetConvex, in targetConvexTransform, out result);
                case ColliderType.Capsule:
                    return CapsuleConvex.ColliderCast(in colliderToCast.m_capsule, in castStart, castEnd, in targetConvex, in targetConvexTransform, out result);
                case ColliderType.Box:
                    return BoxConvex.ColliderCast(in colliderToCast.m_box, in castStart, castEnd, in targetConvex, in targetConvexTransform, out result);
                default:
                    result = default;
                    return false;
            }
        }
    }
}

