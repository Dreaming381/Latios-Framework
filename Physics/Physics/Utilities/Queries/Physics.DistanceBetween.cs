using System;
using Unity.Mathematics;

namespace Latios.PhysicsEngine
{
    public static partial class Physics
    {
        #region Sphere
        public static bool DistanceBetween(SphereCollider sphereA,
                                           RigidTransform aTransform,
                                           SphereCollider sphereB,
                                           RigidTransform bTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            SphereCollider bInASpace = new SphereCollider(sphereB.center + bTransform.pos - aTransform.pos, sphereB.radius);
            bool           hit       = DistanceQueries.DistanceBetween(sphereA, bInASpace, maxDistance, out DistanceQueries.ColliderDistanceResultInternal localResult);
            result                   = new ColliderDistanceResult
            {
                hitpointA = localResult.hitpointA + aTransform.pos,
                hitpointB = localResult.hitpointB + aTransform.pos,
                normalA   = localResult.normalA,
                normalB   = localResult.normalB,
                distance  = localResult.distance
            };
            return hit;
        }
        #endregion

        #region Capsule
        public static bool DistanceBetween(SphereCollider sphere,
                                           RigidTransform sphereTransform,
                                           CapsuleCollider capsule,
                                           RigidTransform capsuleTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            var            capWorldToLocal        = math.inverse(capsuleTransform);
            float3         sphereCenterInCapSpace = math.transform(capWorldToLocal, sphere.center + sphereTransform.pos);
            SphereCollider sphereInCapSpace       = new SphereCollider(sphereCenterInCapSpace, sphere.radius);
            bool           hit                    = DistanceQueries.DistanceBetween(sphereInCapSpace,
                                                                          capsule,
                                                                          maxDistance,
                                                                          out DistanceQueries.ColliderDistanceResultInternal localResult);
            result = new ColliderDistanceResult
            {
                hitpointA = math.transform(capsuleTransform, localResult.hitpointA),
                hitpointB = math.transform(capsuleTransform, localResult.hitpointB),
                normalA   = math.rotate(capsuleTransform, localResult.normalA),
                normalB   = math.rotate(capsuleTransform, localResult.normalB),
                distance  = localResult.distance
            };
            return hit;
        }

        public static bool DistanceBetween(CapsuleCollider capsule,
                                           RigidTransform capsuleTransform,
                                           SphereCollider sphere,
                                           RigidTransform sphereTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            bool hit = DistanceBetween(sphere, sphereTransform, capsule, capsuleTransform, maxDistance, out ColliderDistanceResult flipResult);
            result   = FlipResult(flipResult);
            return hit;
        }

        public static bool DistanceBetween(CapsuleCollider capsuleA,
                                           RigidTransform aTransform,
                                           CapsuleCollider capsuleB,
                                           RigidTransform bTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            var             bWorldToLocal      = math.inverse(bTransform);
            var             aInBSpaceTransform = math.mul(bWorldToLocal, aTransform);
            CapsuleCollider aInBSpace          = new CapsuleCollider(math.transform(aInBSpaceTransform, capsuleA.pointA),
                                                                     math.transform(aInBSpaceTransform, capsuleA.pointB),
                                                                     capsuleA.radius);
            bool hit = DistanceQueries.DistanceBetween(aInBSpace, capsuleB, maxDistance, out DistanceQueries.ColliderDistanceResultInternal localResult);
            result   = AinBResultToWorld(localResult, bTransform);
            return hit;
        }
        #endregion

        #region Compound
        public static bool DistanceBetween(SphereCollider sphere,
                                           RigidTransform sphereTransform,
                                           CompoundCollider compound,
                                           RigidTransform compoundTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            bool hit        = false;
            result          = default;
            result.distance = float.MaxValue;
            ref var blob    = ref compound.compoundColliderBlob.Value;
            for (int i = 0; i < blob.colliders.Length; i++)
            {
                bool newHit = DistanceBetween(sphere,
                                              sphereTransform,
                                              blob.colliders[i],
                                              math.mul(compoundTransform, blob.transforms[i]),
                                              math.min(result.distance, maxDistance),
                                              out var newResult);
                newHit &= newResult.distance < result.distance;
                hit    |= newHit;
                result  = newHit ? newResult : result;
            }
            return hit;
        }

        public static bool DistanceBetween(CompoundCollider compound,
                                           RigidTransform compoundTransform,
                                           SphereCollider sphere,
                                           RigidTransform sphereTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            bool hit = DistanceBetween(sphere, sphereTransform, compound, compoundTransform, maxDistance, out var flipResult);
            result   = FlipResult(flipResult);
            return hit;
        }

        public static bool DistanceBetween(CapsuleCollider capsule,
                                           RigidTransform capsuleTransform,
                                           CompoundCollider compound,
                                           RigidTransform compoundTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            bool hit        = false;
            result          = default;
            result.distance = float.MaxValue;
            ref var blob    = ref compound.compoundColliderBlob.Value;
            for (int i = 0; i < blob.colliders.Length; i++)
            {
                bool newHit = DistanceBetween(capsule,
                                              capsuleTransform,
                                              blob.colliders[i],
                                              math.mul(compoundTransform, blob.transforms[i]),
                                              math.min(result.distance, maxDistance),
                                              out var newResult);
                newHit &= newResult.distance < result.distance;
                hit    |= newHit;
                result  = newHit ? newResult : result;
            }
            return hit;
        }

        public static bool DistanceBetween(CompoundCollider compound,
                                           RigidTransform compoundTransform,
                                           CapsuleCollider capsule,
                                           RigidTransform capsuleTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            bool hit = DistanceBetween(capsule, capsuleTransform, compound, compoundTransform, maxDistance, out var flipResult);
            result   = FlipResult(flipResult);
            return hit;
        }

        public static bool DistanceBetween(CompoundCollider compoundA,
                                           RigidTransform aTransform,
                                           CompoundCollider compoundB,
                                           RigidTransform bTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            bool hit        = false;
            result          = default;
            result.distance = float.MaxValue;
            ref var blob    = ref compoundA.compoundColliderBlob.Value;
            for (int i = 0; i < blob.colliders.Length; i++)
            {
                bool newHit = DistanceBetween(blob.colliders[i],
                                              math.mul(aTransform, blob.transforms[i]),
                                              compoundB,
                                              bTransform,
                                              math.min(result.distance, maxDistance),
                                              out var newResult);
                newHit &= newResult.distance < result.distance;
                hit    |= newHit;
                result  = newHit ? newResult : result;
            }
            return hit;
        }

        #endregion

        private static ColliderDistanceResult FlipResult(ColliderDistanceResult resultToFlip)
        {
            return new ColliderDistanceResult
            {
                hitpointA = resultToFlip.hitpointB,
                hitpointB = resultToFlip.hitpointA,
                normalA   = resultToFlip.normalB,
                normalB   = resultToFlip.normalA,
                distance  = resultToFlip.distance
            };
        }

        private static ColliderDistanceResult AinBResultToWorld(DistanceQueries.ColliderDistanceResultInternal AinBResult, RigidTransform bTransform)
        {
            return new ColliderDistanceResult
            {
                hitpointA = math.transform(bTransform, AinBResult.hitpointA),
                hitpointB = math.transform(bTransform, AinBResult.hitpointB),
                normalA   = math.rotate(bTransform, AinBResult.normalA),
                normalB   = math.rotate(bTransform, AinBResult.normalB),
                distance  = AinBResult.distance
            };
        }

        /*private static ColliderDistanceResult BinAResultToWorld(GjkResult bInAResult, RigidTransform aTransform)
           {
            return new ColliderDistanceResult
            {
                hitpointA = math.transform(aTransform, bInAResult.ClosestPoints.hitpointA),
                hitpointB = math.transform(aTransform, bInAResult.ClosestPoints.hitpointB),
                normalA   = math.rotate(aTransform, bInAResult.ClosestPoints.normalA),
                normalB   = math.rotate(aTransform, bInAResult.ClosestPoints.normalB),
                distance  = bInAResult.ClosestPoints.distance
            };
           }*/
    }
}

