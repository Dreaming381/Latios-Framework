using Unity.Burst;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class CompoundCompound
    {
        public static bool AreOverlapping(in CompoundCollider compoundA,
                                          in RigidTransform aTransform,
                                          in CompoundCollider compoundB,
                                          in RigidTransform bTransform)
        {
            foreach (var i in new PointRayCompound.CompoundAabbEnumerator(compoundB, bTransform, compoundA, aTransform, 0f))
            {
                compoundA.GetScaledStretchedSubCollider(i, out var blobACollider, out var blobATransform);

                var subATransform = math.mul(aTransform, blobATransform);
                foreach (var j in new PointRayCompound.CompoundAabbEnumerator(blobACollider, subATransform, compoundB, bTransform, 0f))
                {
                    compoundB.GetScaledStretchedSubCollider(j, out var blobBCollider, out var blobBTransform);
                    if (AreOverlapping(in blobACollider, subATransform, in blobBCollider, math.mul(bTransform, blobBTransform)))
                        return true;
                }
            }
            return false;
        }

        public static bool WithinDistance(in CompoundCollider compoundA,
                                          in RigidTransform aTransform,
                                          in CompoundCollider compoundB,
                                          in RigidTransform bTransform,
                                          float maxDistance)
        {
            foreach (var i in new PointRayCompound.CompoundAabbEnumerator(compoundB, bTransform, compoundA, aTransform, maxDistance))
            {
                compoundA.GetScaledStretchedSubCollider(i, out var blobACollider, out var blobATransform);

                var subATransform = math.mul(aTransform, blobATransform);
                foreach (var j in new PointRayCompound.CompoundAabbEnumerator(blobACollider, subATransform, compoundB, bTransform, maxDistance))
                {
                    compoundB.GetScaledStretchedSubCollider(j, out var blobBCollider, out var blobBTransform);
                    if (WithinDistance(in blobACollider, subATransform, in blobBCollider, math.mul(bTransform, blobBTransform), maxDistance))
                        return true;
                }
            }
            return false;
        }

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
            foreach (var i in new PointRayCompound.CompoundAabbEnumerator(compoundB, bTransform, compoundA, aTransform, maxDistance))
            {
                compoundA.GetScaledStretchedSubCollider(i, out var blobACollider, out var blobATransform);

                var subATransform = math.mul(aTransform, blobATransform);
                foreach (var j in new PointRayCompound.CompoundAabbEnumerator(blobACollider, subATransform, compoundB, bTransform, maxDistance))
                {
                    compoundB.GetScaledStretchedSubCollider(j, out var blobBCollider, out var blobBTransform);

                    bool newHit = DistanceBetween(in blobACollider,
                                                  subATransform,
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
            foreach (var i in new PointRayCompound.CompoundAabbEnumerator(compoundB, bTransform, compoundA, aTransform, maxDistance))
            {
                compoundA.GetScaledStretchedSubCollider(i, out var blobACollider, out var blobATransform);

                var subATransform = math.mul(aTransform, blobATransform);
                foreach (var j in new PointRayCompound.CompoundAabbEnumerator(blobACollider, subATransform, compoundB, bTransform, maxDistance))
                {
                    compoundB.GetScaledStretchedSubCollider(j, out var blobBCollider, out var blobBTransform);

                    bool newHit = DistanceBetween(in blobACollider,
                                                  subATransform,
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
            ref var blobToCast      = ref compoundToCast.compoundColliderBlob.Value;
            ref var targetBlob      = ref targetCompound.compoundColliderBlob.Value;
            var     targetSweptAabb = Physics.AabbFrom(targetCompound, targetCompoundTransform, targetCompoundTransform.pos - (castEnd - castStart.pos));
            foreach (var i in new PointRayCompound.CompoundAabbEnumerator(targetSweptAabb, compoundToCast, castStart))
            {
                compoundToCast.GetScaledStretchedSubCollider(i, out var blobColliderToCast, out var blobTransformToCast);

                var start = math.mul(castStart, blobTransformToCast);
                var end   = start.pos + (castEnd - castStart.pos);
                foreach (var j in new PointRayCompound.CompoundAabbEnumerator(Physics.AabbFrom(blobColliderToCast, in start, end), targetCompound, targetCompoundTransform))
                {
                    targetCompound.GetScaledStretchedSubCollider(j, out var targetBlobCollider, out var targetBlobTransform);

                    bool newHit = ColliderCast(in blobColliderToCast,
                                               start,
                                               end,
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
        private static bool AreOverlapping(in Collider colliderA,
                                           in RigidTransform aTransform,
                                           in Collider colliderB,
                                           in RigidTransform bTransform)
        {
            switch ((colliderA.type, colliderB.type))
            {
                case (ColliderType.Sphere, ColliderType.Sphere):
                    return SphereSphere.AreOverlapping(in colliderA.m_sphere, in aTransform, in colliderB.m_sphere, in bTransform);
                case (ColliderType.Sphere, ColliderType.Capsule):
                    return SphereCapsule.AreOverlapping(in colliderB.m_capsule, in bTransform, in colliderA.m_sphere, in aTransform);
                case (ColliderType.Sphere, ColliderType.Box):
                    return SphereBox.AreOverlapping(in colliderB.m_box, in bTransform, in colliderA.m_sphere, in aTransform);
                case (ColliderType.Capsule, ColliderType.Sphere):
                    return SphereCapsule.AreOverlapping(in colliderA.m_capsule, in aTransform, in colliderB.m_sphere, in bTransform);
                case (ColliderType.Capsule, ColliderType.Capsule):
                    return CapsuleCapsule.AreOverlapping(in colliderA.m_capsule, in aTransform, in colliderB.m_capsule, in bTransform);
                case (ColliderType.Capsule, ColliderType.Box):
                    return CapsuleBox.AreOverlapping(in colliderB.m_box, in bTransform, in colliderA.m_capsule, in aTransform);
                case (ColliderType.Box, ColliderType.Sphere):
                    return SphereBox.AreOverlapping(in colliderA.m_box, in aTransform, in colliderB.m_sphere, in bTransform);
                case (ColliderType.Box, ColliderType.Capsule):
                    return CapsuleBox.AreOverlapping(in colliderA.m_box, in aTransform, in colliderB.m_capsule, in bTransform);
                case (ColliderType.Box, ColliderType.Box):
                    return BoxBox.AreOverlapping(in colliderA.m_box, in aTransform, in colliderB.m_box, in bTransform);
                default:
                    return false;
            }
        }
        private static bool WithinDistance(in Collider colliderA,
                                           in RigidTransform aTransform,
                                           in Collider colliderB,
                                           in RigidTransform bTransform,
                                           float maxDistance)
        {
            switch ((colliderA.type, colliderB.type))
            {
                case (ColliderType.Sphere, ColliderType.Sphere):
                    return SphereSphere.WithinDistance(in colliderA.m_sphere, in aTransform, in colliderB.m_sphere, in bTransform, maxDistance);
                case (ColliderType.Sphere, ColliderType.Capsule):
                    return SphereCapsule.WithinDistance(in colliderB.m_capsule, in bTransform, in colliderA.m_sphere, in aTransform, maxDistance);
                case (ColliderType.Sphere, ColliderType.Box):
                    return SphereBox.WithinDistance(in colliderB.m_box, in bTransform, in colliderA.m_sphere, in aTransform, maxDistance);
                case (ColliderType.Capsule, ColliderType.Sphere):
                    return SphereCapsule.WithinDistance(in colliderA.m_capsule, in aTransform, in colliderB.m_sphere, in bTransform, maxDistance);
                case (ColliderType.Capsule, ColliderType.Capsule):
                    return CapsuleCapsule.WithinDistance(in colliderA.m_capsule, in aTransform, in colliderB.m_capsule, in bTransform, maxDistance);
                case (ColliderType.Capsule, ColliderType.Box):
                    return CapsuleBox.WithinDistance(in colliderB.m_box, in bTransform, in colliderA.m_capsule, in aTransform, maxDistance);
                case (ColliderType.Box, ColliderType.Sphere):
                    return SphereBox.WithinDistance(in colliderA.m_box, in aTransform, in colliderB.m_sphere, in bTransform, maxDistance);
                case (ColliderType.Box, ColliderType.Capsule):
                    return CapsuleBox.WithinDistance(in colliderA.m_box, in aTransform, in colliderB.m_capsule, in bTransform, maxDistance);
                case (ColliderType.Box, ColliderType.Box):
                    return BoxBox.WithinDistance(in colliderA.m_box, in aTransform, in colliderB.m_box, in bTransform, maxDistance);
                default:
                    return false;
            }
        }
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

