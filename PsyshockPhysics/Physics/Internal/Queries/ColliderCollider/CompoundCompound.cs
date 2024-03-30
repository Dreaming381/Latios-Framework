using Unity.Burst;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class CompoundCompound
    {
        public static bool DistanceBetween(in CompoundCollider compoundA,
                                           in RigidTransform aTransform,
                                           in CompoundCollider compoundB,
                                           in RigidTransform bTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            bool hit        = false;
            result          = default;
            result.distance = float.MaxValue;
            ref var blobA   = ref compoundA.compoundColliderBlob.Value;
            ref var blobB   = ref compoundB.compoundColliderBlob.Value;
            for (int i = 0; i < blobA.colliders.Length; i++)
            {
                compoundA.GetScaledStretchedSubCollider(i, out var blobACollider, out var blobATransform);

                for (int j = 0; j < blobB.colliders.Length; j++)
                {
                    compoundB.GetScaledStretchedSubCollider(j, out var blobBCollider, out var blobBTransform);

                    bool newHit = DistanceBetween(in blobACollider,
                                                  math.mul(aTransform, blobATransform),
                                                  in blobBCollider,
                                                  math.mul(bTransform, blobBTransform),
                                                  maxDistance,
                                                  out var newResult);

                    newResult.subColliderIndexA  = i;
                    newResult.subColliderIndexB  = j;
                    newHit                      &= newResult.distance < result.distance;
                    hit                         |= newHit;
                    result                       = newHit ? newResult : result;
                }
            }
            return hit;
        }

        public static void DistanceBetweenAll<T>(in CompoundCollider compoundA,
                                                 in RigidTransform aTransform,
                                                 in CompoundCollider compoundB,
                                                 in RigidTransform bTransform,
                                                 float maxDistance,
                                                 ref T processor) where T : unmanaged, IDistanceBetweenAllProcessor
        {
            ref var blobA = ref compoundA.compoundColliderBlob.Value;
            ref var blobB = ref compoundB.compoundColliderBlob.Value;
            for (int i = 0; i < blobA.colliders.Length; i++)
            {
                compoundA.GetScaledStretchedSubCollider(i, out var blobACollider, out var blobATransform);

                for (int j = 0; j < blobB.colliders.Length; j++)
                {
                    compoundB.GetScaledStretchedSubCollider(j, out var blobBCollider, out var blobBTransform);

                    bool newHit = DistanceBetween(in blobACollider,
                                                  math.mul(aTransform, blobATransform),
                                                  in blobBCollider,
                                                  math.mul(bTransform, blobBTransform),
                                                  maxDistance,
                                                  out var result);

                    result.subColliderIndexA = i;
                    result.subColliderIndexB = j;

                    if (newHit)
                        processor.Execute(in result);
                }
            }
        }

        public static bool ColliderCast(in CompoundCollider compoundToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in CompoundCollider targetCompound,
                                        in RigidTransform targetCompoundTransform,
                                        out ColliderCastResult result)
        {
            bool hit        = false;
            result          = default;
            result.distance = float.MaxValue;
            if (DistanceBetween(in compoundToCast, in castStart, in targetCompound, in targetCompoundTransform, 0f, out _))
            {
                return false;
            }
            ref var blobToCast = ref compoundToCast.compoundColliderBlob.Value;
            ref var targetBlob = ref targetCompound.compoundColliderBlob.Value;
            for (int i = 0; i < blobToCast.colliders.Length; i++)
            {
                compoundToCast.GetScaledStretchedSubCollider(i, out var blobColliderToCast, out var blobTransformToCast);

                for (int j = 0; j < targetBlob.colliders.Length; j++)
                {
                    targetCompound.GetScaledStretchedSubCollider(j, out var targetBlobCollider, out var targetBlobTransform);
                    var  start  = math.mul(castStart, blobTransformToCast);
                    bool newHit = ColliderCast(in blobColliderToCast,
                                               start,
                                               start.pos + (castEnd - castStart.pos),
                                               in targetBlobCollider,
                                               math.mul(targetCompoundTransform, targetBlobTransform),
                                               out var newResult);

                    newResult.subColliderIndexOnCaster  = i;
                    newResult.subColliderIndexOnTarget  = j;
                    newHit                             &= newResult.distance < result.distance;
                    hit                                |= newHit;
                    result                              = newHit ? newResult : result;
                }
            }
            return hit;
        }

        public static UnitySim.ContactsBetweenResult UnityContactsBetween(in CompoundCollider compoundA,
                                                                          in RigidTransform aTransform,
                                                                          in CompoundCollider compoundB,
                                                                          in RigidTransform bTransform,
                                                                          in ColliderDistanceResult distanceResult)
        {
            compoundA.GetScaledStretchedSubCollider(distanceResult.subColliderIndexA, out var collider, out var colliderTransform);
            colliderTransform = math.mul(aTransform, colliderTransform);
            return collider.type switch
                   {
                       ColliderType.Sphere => ContactManifoldHelpers.GetSingleContactManifold(in distanceResult),
                       ColliderType.Capsule => CapsuleCompound.UnityContactsBetween(in compoundB, in bTransform, in collider.m_capsule, in colliderTransform,
                                                                                    distanceResult.ToFlipped()).ToFlipped(),
                       ColliderType.Box => BoxCompound.UnityContactsBetween(in compoundB, in bTransform, in collider.m_box, in colliderTransform,
                                                                            distanceResult.ToFlipped()).ToFlipped(),
                       _ => ContactManifoldHelpers.GetSingleContactManifold(in distanceResult)
                   };
        }

        // We use a reduced set dispatch here so that Burst doesn't have to try to make these methods re-entrant.
        private static bool DistanceBetween(in Collider colliderA,
                                            in RigidTransform aTransform,
                                            in Collider colliderB,
                                            in RigidTransform bTransform,
                                            float maxDistance,
                                            out ColliderDistanceResult result)
        {
            switch ((colliderA.type, colliderB.type))
            {
                case (ColliderType.Sphere, ColliderType.Sphere):
                    return SphereSphere.DistanceBetween(in colliderA.m_sphere, in aTransform, in colliderB.m_sphere, in bTransform, maxDistance, out result);
                case (ColliderType.Sphere, ColliderType.Capsule):
                    var sphereCapsuleResult = SphereCapsule.DistanceBetween(in colliderB.m_capsule, in bTransform, in colliderA.m_sphere, in aTransform, maxDistance, out result);
                    result.FlipInPlace();
                    return sphereCapsuleResult;
                case (ColliderType.Sphere, ColliderType.Box):
                    var sphereBoxResult = SphereBox.DistanceBetween(in colliderB.m_box, in bTransform, in colliderA.m_sphere, in aTransform, maxDistance, out result);
                    result.FlipInPlace();
                    return sphereBoxResult;
                case (ColliderType.Capsule, ColliderType.Sphere):
                    return SphereCapsule.DistanceBetween(in colliderA.m_capsule, in aTransform, in colliderB.m_sphere, in bTransform, maxDistance, out result);
                case (ColliderType.Capsule, ColliderType.Capsule):
                    return CapsuleCapsule.DistanceBetween(in colliderA.m_capsule, in aTransform, in colliderB.m_capsule, in bTransform, maxDistance, out result);
                case (ColliderType.Capsule, ColliderType.Box):
                    var capsuleBoxResult = CapsuleBox.DistanceBetween(in colliderB.m_box, in bTransform, in colliderA.m_capsule, in aTransform, maxDistance, out result);
                    result.FlipInPlace();
                    return capsuleBoxResult;
                case (ColliderType.Box, ColliderType.Sphere):
                    return SphereBox.DistanceBetween(in colliderA.m_box, in aTransform, in colliderB.m_sphere, in bTransform, maxDistance, out result);
                case (ColliderType.Box, ColliderType.Capsule):
                    return CapsuleBox.DistanceBetween(in colliderA.m_box, in aTransform, in colliderB.m_capsule, in bTransform, maxDistance, out result);
                case (ColliderType.Box, ColliderType.Box):
                    return BoxBox.DistanceBetween(in colliderA.m_box, in aTransform, in colliderB.m_box, in bTransform, maxDistance, out result);
                default:
                    result = default;
                    return false;
            }
        }

        private static bool ColliderCast(in Collider colliderToCast,
                                         in RigidTransform castStart,
                                         float3 castEnd,
                                         in Collider target,
                                         in RigidTransform targetTransform,
                                         out ColliderCastResult result)
        {
            switch ((colliderToCast.type, target.type))
            {
                case (ColliderType.Sphere, ColliderType.Sphere):
                    return SphereSphere.ColliderCast(in colliderToCast.m_sphere, in castStart, castEnd, in target.m_sphere, in targetTransform, out result);
                case (ColliderType.Sphere, ColliderType.Capsule):
                    return SphereCapsule.ColliderCast(in colliderToCast.m_sphere, in castStart, castEnd, in target.m_capsule, in targetTransform, out result);
                case (ColliderType.Sphere, ColliderType.Box):
                    return SphereBox.ColliderCast(in colliderToCast.m_sphere, in castStart, castEnd, in target.m_box, in targetTransform, out result);
                case (ColliderType.Capsule, ColliderType.Sphere):
                    return SphereCapsule.ColliderCast(in colliderToCast.m_capsule, in castStart, castEnd, in target.m_sphere, in targetTransform, out result);
                case (ColliderType.Capsule, ColliderType.Capsule):
                    return CapsuleCapsule.ColliderCast(in colliderToCast.m_capsule, in castStart, castEnd, in target.m_capsule, in targetTransform, out result);
                case (ColliderType.Capsule, ColliderType.Box):
                    return CapsuleBox.ColliderCast(in colliderToCast.m_capsule, in castStart, castEnd, in target.m_box, in targetTransform, out result);
                case (ColliderType.Box, ColliderType.Sphere):
                    return SphereBox.ColliderCast(in colliderToCast.m_box, in castStart, castEnd, in target.m_sphere, in targetTransform, out result);
                case (ColliderType.Box, ColliderType.Capsule):
                    return CapsuleBox.ColliderCast(in colliderToCast.m_box, in castStart, castEnd, in target.m_capsule, in targetTransform, out result);
                case (ColliderType.Box, ColliderType.Box):
                    return BoxBox.ColliderCast(in colliderToCast.m_box, in castStart, castEnd, in target.m_box, in targetTransform, out result);
                default:
                    result = default;
                    return false;
            }
        }
    }
}

