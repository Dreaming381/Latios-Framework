using System;
using Unity.Mathematics;

namespace Latios.Psyshock
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
        public static bool DistanceBetween(CapsuleCollider capsule,
                                           RigidTransform capsuleTransform,
                                           SphereCollider sphere,
                                           RigidTransform sphereTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            var            capWorldToLocal        = math.inverse(capsuleTransform);
            float3         sphereCenterInCapSpace = math.transform(capWorldToLocal, sphere.center + sphereTransform.pos);
            SphereCollider sphereInCapSpace       = new SphereCollider(sphereCenterInCapSpace, sphere.radius);
            bool           hit                    = DistanceQueries.DistanceBetween(capsule,
                                                                          sphereInCapSpace,
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

        public static bool DistanceBetween(SphereCollider sphere,
                                           RigidTransform sphereTransform,
                                           CapsuleCollider capsule,
                                           RigidTransform capsuleTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            bool hit = DistanceBetween(capsule, capsuleTransform, sphere, sphereTransform, maxDistance, out ColliderDistanceResult flipResult);
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
            var             aWorldToLocal      = math.inverse(aTransform);
            var             BinASpaceTransform = math.mul(aWorldToLocal, bTransform);
            CapsuleCollider BinASpace          = new CapsuleCollider(math.transform(BinASpaceTransform, capsuleB.pointA),
                                                                     math.transform(BinASpaceTransform, capsuleB.pointB),
                                                                     capsuleB.radius);
            bool hit = DistanceQueries.DistanceBetween(capsuleA, BinASpace, maxDistance, out DistanceQueries.ColliderDistanceResultInternal localResult);
            result   = BinAResultToWorld(localResult, aTransform);
            return hit;
        }
        #endregion

        #region Box
        public static bool DistanceBetween(BoxCollider box,
                                           RigidTransform boxTransform,
                                           SphereCollider sphere,
                                           RigidTransform sphereTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            var            boxWorldToLocal        = math.inverse(boxTransform);
            float3         sphereCenterInBoxSpace = math.transform(boxWorldToLocal, sphere.center + sphereTransform.pos);
            SphereCollider sphereInBoxSpace       = new SphereCollider(sphereCenterInBoxSpace, sphere.radius);
            bool           hit                    = DistanceQueries.DistanceBetween(box,
                                                                          sphereInBoxSpace,
                                                                          maxDistance,
                                                                          out DistanceQueries.ColliderDistanceResultInternal localResult);
            result = new ColliderDistanceResult
            {
                hitpointA = math.transform(boxTransform, localResult.hitpointA),
                hitpointB = math.transform(boxTransform, localResult.hitpointB),
                normalA   = math.rotate(boxTransform, localResult.normalA),
                normalB   = math.rotate(boxTransform, localResult.normalB),
                distance  = localResult.distance
            };
            return hit;
        }

        public static bool DistanceBetween(SphereCollider sphere,
                                           RigidTransform sphereTransform,
                                           BoxCollider box,
                                           RigidTransform boxTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            bool hit = DistanceBetween(box, boxTransform, sphere, sphereTransform, maxDistance, out ColliderDistanceResult flipResult);
            result   = FlipResult(flipResult);
            return hit;
        }

        public static bool DistanceBetween(BoxCollider box,
                                           RigidTransform boxTransform,
                                           CapsuleCollider capsule,
                                           RigidTransform capsuleTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            var boxWorldToLocal        = math.inverse(boxTransform);
            var capInBoxSpaceTransform = math.mul(boxWorldToLocal, capsuleTransform);
            var capsuleInBoxSpace      = new CapsuleCollider(math.transform(capInBoxSpaceTransform, capsule.pointA),
                                                             math.transform(capInBoxSpaceTransform, capsule.pointB),
                                                             capsule.radius);
            bool hit = DistanceQueries.DistanceBetween(box, capsuleInBoxSpace, maxDistance, out DistanceQueries.ColliderDistanceResultInternal localResult);
            result   = BinAResultToWorld(localResult, boxTransform);
            return hit;
        }

        public static bool DistanceBetween(CapsuleCollider capsule,
                                           RigidTransform capsuleTransform,
                                           BoxCollider box,
                                           RigidTransform boxTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            bool hit = DistanceBetween(box, boxTransform, capsule, capsuleTransform, maxDistance, out ColliderDistanceResult flipResult);
            result   = FlipResult(flipResult);
            return hit;
        }

        public static bool DistanceBetween(BoxCollider boxA,
                                           RigidTransform aTransform,
                                           BoxCollider boxB,
                                           RigidTransform bTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            var aWorldToLocal      = math.inverse(aTransform);
            var bWorldToLocal      = math.inverse(bTransform);
            var bInASpaceTransform = math.mul(aWorldToLocal, bTransform);
            var aInBSpaceTransform = math.mul(bWorldToLocal, aTransform);
            var hit                = DistanceQueries.DistanceBetween(boxA,
                                                                     boxB,
                                                                     bInASpaceTransform,
                                                                     aInBSpaceTransform,
                                                                     maxDistance,
                                                                     out DistanceQueries.ColliderDistanceResultInternal localResult);
            result = BinAResultToWorld(localResult, aTransform);
            return hit;
        }
        #endregion

        #region Compound
        public static bool DistanceBetween(CompoundCollider compound,
                                           RigidTransform compoundTransform,
                                           SphereCollider sphere,
                                           RigidTransform sphereTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            bool hit              = false;
            result                = default;
            result.distance       = float.MaxValue;
            ref var blob          = ref compound.compoundColliderBlob.Value;
            var     compoundScale = new PhysicsScale { scale = compound.scale, state = PhysicsScale.State.Uniform };
            for (int i = 0; i < blob.colliders.Length; i++)
            {
                var blobTransform  = blob.transforms[i];
                blobTransform.pos *= compound.scale;
                bool newHit        = DistanceBetween(ScaleCollider(blob.colliders[i], compoundScale),
                                                     math.mul(compoundTransform, blobTransform),
                                                     sphere,
                                                     sphereTransform,
                                                     math.min(result.distance, maxDistance),
                                                     out var newResult);

                newResult.subColliderIndexA  = i;
                newHit                      &= newResult.distance < result.distance;
                hit                         |= newHit;
                result                       = newHit ? newResult : result;
            }
            return hit;
        }

        public static bool DistanceBetween(SphereCollider sphere,
                                           RigidTransform sphereTransform,
                                           CompoundCollider compound,
                                           RigidTransform compoundTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            bool hit = DistanceBetween(compound, compoundTransform, sphere, sphereTransform, maxDistance, out var flipResult);
            result   = FlipResult(flipResult);
            return hit;
        }

        public static bool DistanceBetween(CompoundCollider compound,
                                           RigidTransform compoundTransform,
                                           CapsuleCollider capsule,
                                           RigidTransform capsuleTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            bool hit              = false;
            result                = default;
            result.distance       = float.MaxValue;
            ref var blob          = ref compound.compoundColliderBlob.Value;
            var     compoundScale = new PhysicsScale { scale = compound.scale, state = PhysicsScale.State.Uniform };
            for (int i = 0; i < blob.colliders.Length; i++)
            {
                var blobTransform  = blob.transforms[i];
                blobTransform.pos *= compound.scale;
                bool newHit        = DistanceBetween(ScaleCollider(blob.colliders[i], compoundScale),
                                                     math.mul(compoundTransform, blobTransform),
                                                     capsule,
                                                     capsuleTransform,
                                                     math.min(result.distance, maxDistance),
                                                     out var newResult);

                newResult.subColliderIndexA  = i;
                newHit                      &= newResult.distance < result.distance;
                hit                         |= newHit;
                result                       = newHit ? newResult : result;
            }
            return hit;
        }

        public static bool DistanceBetween(CapsuleCollider capsule,
                                           RigidTransform capsuleTransform,
                                           CompoundCollider compound,
                                           RigidTransform compoundTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            bool hit = DistanceBetween(compound, compoundTransform, capsule, capsuleTransform, maxDistance, out var flipResult);
            result   = FlipResult(flipResult);
            return hit;
        }

        public static bool DistanceBetween(CompoundCollider compound,
                                           RigidTransform compoundTransform,
                                           BoxCollider box,
                                           RigidTransform boxTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            bool hit              = false;
            result                = default;
            result.distance       = float.MaxValue;
            ref var blob          = ref compound.compoundColliderBlob.Value;
            var     compoundScale = new PhysicsScale { scale = compound.scale, state = PhysicsScale.State.Uniform };
            for (int i = 0; i < blob.colliders.Length; i++)
            {
                var blobTransform  = blob.transforms[i];
                blobTransform.pos *= compound.scale;
                bool newHit        = DistanceBetween(ScaleCollider(blob.colliders[i], compoundScale),
                                                     math.mul(compoundTransform, blobTransform),
                                                     box,
                                                     boxTransform,
                                                     maxDistance,
                                                     out var newResult);

                newResult.subColliderIndexA  = i;
                newHit                      &= newResult.distance < result.distance;
                hit                         |= newHit;
                result                       = newHit ? newResult : result;
            }
            return hit;
        }

        public static bool DistanceBetween(BoxCollider box,
                                           RigidTransform boxTransform,
                                           CompoundCollider compound,
                                           RigidTransform compoundTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            bool hit = DistanceBetween(compound, compoundTransform, box, boxTransform, maxDistance, out var flipResult);
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
            bool hit              = false;
            result                = default;
            result.distance       = float.MaxValue;
            ref var blob          = ref compoundA.compoundColliderBlob.Value;
            var     compoundScale = new PhysicsScale { scale = compoundA.scale, state = PhysicsScale.State.Uniform };
            for (int i = 0; i < blob.colliders.Length; i++)
            {
                var blobTransform  = blob.transforms[i];
                blobTransform.pos *= compoundA.scale;
                bool newHit        = DistanceBetween(ScaleCollider(blob.colliders[i], compoundScale),
                                                     math.mul(aTransform, blobTransform),
                                                     compoundB,
                                                     bTransform,
                                                     math.min(result.distance, maxDistance),
                                                     out var newResult);

                newResult.subColliderIndexA  = i;
                newHit                      &= newResult.distance < result.distance;
                hit                         |= newHit;
                result                       = newHit ? newResult : result;
            }
            return hit;
        }

        #endregion

        private static ColliderDistanceResult FlipResult(ColliderDistanceResult resultToFlip)
        {
            return new ColliderDistanceResult
            {
                hitpointA         = resultToFlip.hitpointB,
                hitpointB         = resultToFlip.hitpointA,
                normalA           = resultToFlip.normalB,
                normalB           = resultToFlip.normalA,
                distance          = resultToFlip.distance,
                subColliderIndexA = resultToFlip.subColliderIndexB,
                subColliderIndexB = resultToFlip.subColliderIndexA
            };
        }

        private static ColliderDistanceResult BinAResultToWorld(DistanceQueries.ColliderDistanceResultInternal BinAResult, RigidTransform aTransform)
        {
            return new ColliderDistanceResult
            {
                hitpointA = math.transform(aTransform, BinAResult.hitpointA),
                hitpointB = math.transform(aTransform, BinAResult.hitpointB),
                normalA   = math.rotate(aTransform, BinAResult.normalA),
                normalB   = math.rotate(aTransform, BinAResult.normalB),
                distance  = BinAResult.distance
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

