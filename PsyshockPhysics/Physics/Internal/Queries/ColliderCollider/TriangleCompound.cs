using Unity.Burst;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class TriangleCompound
    {
        public static bool DistanceBetween(in CompoundCollider compound,
                                           in RigidTransform compoundTransform,
                                           in TriangleCollider triangle,
                                           in RigidTransform triangleTransform,
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
                                              in triangle,
                                              in triangleTransform,
                                              maxDistance,
                                              out var newResult);

                newResult.subColliderIndexA  = i;
                newHit                      &= newResult.distance < result.distance;
                hit                         |= newHit;
                result                       = newHit ? newResult : result;
            }
            return hit;
        }

        public static bool ColliderCast(in TriangleCollider triangleToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in CompoundCollider targetCompound,
                                        in RigidTransform targetCompoundTransform,
                                        out ColliderCastResult result)
        {
            bool hit        = false;
            result          = default;
            result.distance = float.MaxValue;
            if (DistanceBetween(in targetCompound, in targetCompoundTransform, in triangleToCast, in castStart, 0f, out _))
            {
                return false;
            }
            ref var blob = ref targetCompound.compoundColliderBlob.Value;
            for (int i = 0; i < blob.colliders.Length; i++)
            {
                targetCompound.GetScaledStretchedSubCollider(i, out var blobCollider, out var blobTransform);
                bool newHit = ColliderCast(in triangleToCast, in castStart, castEnd, in blobCollider,
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
                                        in TriangleCollider targetTriangle,
                                        in RigidTransform targetTriangleTransform,
                                        out ColliderCastResult result)
        {
            bool hit        = false;
            result          = default;
            result.distance = float.MaxValue;
            if (DistanceBetween(in compoundToCast, in castStart, in targetTriangle, in targetTriangleTransform, 0f, out _))
            {
                return false;
            }
            ref var blob = ref compoundToCast.compoundColliderBlob.Value;
            for (int i = 0; i < blob.colliders.Length; i++)
            {
                compoundToCast.GetScaledStretchedSubCollider(i, out var blobCollider, out var blobTransform);
                var  start  = math.mul(castStart, blobTransform);
                bool newHit = ColliderCast(in blobCollider,
                                           start, start.pos + (castEnd - castStart.pos),
                                           in targetTriangle,
                                           in targetTriangleTransform,
                                           out var newResult);

                newResult.subColliderIndexOnCaster  = i;
                newHit                             &= newResult.distance < result.distance;
                hit                                |= newHit;
                result                              = newHit ? newResult : result;
            }
            return hit;
        }

        // We use a reduced set dispatch here so that Burst doesn't have to try to make these methods re-entrant.
        private static bool DistanceBetween(in Collider collider,
                                            in RigidTransform colliderTransform,
                                            in TriangleCollider triangle,
                                            in RigidTransform triangleTransform,
                                            float maxDistance,
                                            out ColliderDistanceResult result)
        {
            switch (collider.type)
            {
                case ColliderType.Sphere:
                    var sphereResult = SphereTriangle.DistanceBetween(in triangle,
                                                                      in triangleTransform,
                                                                      in collider.m_sphere,
                                                                      in colliderTransform,
                                                                      maxDistance,
                                                                      out result);
                    (result.hitpointA, result.hitpointB)                 = (result.hitpointB, result.hitpointA);
                    (result.normalA, result.normalB)                     = (result.normalB, result.normalA);
                    (result.subColliderIndexA, result.subColliderIndexB) = (result.subColliderIndexB, result.subColliderIndexA);
                    return sphereResult;
                case ColliderType.Capsule:
                    var capsuleResult = CapsuleTriangle.DistanceBetween(in triangle,
                                                                        in triangleTransform,
                                                                        in collider.m_capsule,
                                                                        in colliderTransform,
                                                                        maxDistance,
                                                                        out result);
                    (result.hitpointA, result.hitpointB)                 = (result.hitpointB, result.hitpointA);
                    (result.normalA, result.normalB)                     = (result.normalB, result.normalA);
                    (result.subColliderIndexA, result.subColliderIndexB) = (result.subColliderIndexB, result.subColliderIndexA);
                    return capsuleResult;
                case ColliderType.Box:
                    var boxResult = BoxTriangle.DistanceBetween(in triangle,
                                                                in triangleTransform,
                                                                in collider.m_box,
                                                                in colliderTransform,
                                                                maxDistance,
                                                                out result);
                    (result.hitpointA, result.hitpointB)                 = (result.hitpointB, result.hitpointA);
                    (result.normalA, result.normalB)                     = (result.normalB, result.normalA);
                    (result.subColliderIndexA, result.subColliderIndexB) = (result.subColliderIndexB, result.subColliderIndexA);
                    return boxResult;
                default:
                    result = default;
                    return false;
            }
        }

        private static bool ColliderCast(in TriangleCollider triangleToCast,
                                         in RigidTransform castStart,
                                         float3 castEnd,
                                         in Collider target,
                                         in RigidTransform targetTransform,
                                         out ColliderCastResult result)
        {
            switch (target.type)
            {
                case ColliderType.Sphere:
                    return SphereTriangle.ColliderCast(in triangleToCast, in castStart, castEnd, in target.m_sphere, in targetTransform, out result);
                case ColliderType.Capsule:
                    return CapsuleTriangle.ColliderCast(in triangleToCast, in castStart, castEnd, in target.m_capsule, in targetTransform, out result);
                case ColliderType.Box:
                    return BoxTriangle.ColliderCast(in triangleToCast, in castStart, castEnd, in target.m_box, in targetTransform, out result);
                default:
                    result = default;
                    return false;
            }
        }

        private static bool ColliderCast(in Collider colliderToCast,
                                         in RigidTransform castStart,
                                         float3 castEnd,
                                         in TriangleCollider targetTriangle,
                                         in RigidTransform targetTriangleTransform,
                                         out ColliderCastResult result)
        {
            switch (colliderToCast.type)
            {
                case ColliderType.Sphere:
                    return SphereTriangle.ColliderCast(in colliderToCast.m_sphere, in castStart, castEnd, in targetTriangle, in targetTriangleTransform, out result);
                case ColliderType.Capsule:
                    return CapsuleTriangle.ColliderCast(in colliderToCast.m_capsule, in castStart, castEnd, in targetTriangle, in targetTriangleTransform, out result);
                case ColliderType.Box:
                    return BoxTriangle.ColliderCast(in colliderToCast.m_box, in castStart, castEnd, in targetTriangle, in targetTriangleTransform, out result);
                default:
                    result = default;
                    return false;
            }
        }
    }
}

