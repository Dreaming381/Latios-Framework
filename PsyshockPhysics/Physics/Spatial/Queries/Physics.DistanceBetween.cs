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
            var            aWorldToLocal      = math.inverse(aTransform);
            var            bInASpaceTransform = math.mul(aWorldToLocal, bTransform);
            SphereCollider bInASpace          = new SphereCollider(math.transform(bInASpaceTransform, sphereB.center), sphereB.radius);
            bool           hit                =
                SpatialInternal.SphereSphereDistance(sphereA, bInASpace, maxDistance, out SpatialInternal.ColliderDistanceResultInternal localResult);
            result = new ColliderDistanceResult
            {
                hitpointA = math.transform(aTransform, localResult.hitpointA),
                hitpointB = math.transform(aTransform, localResult.hitpointB),
                normalA   = math.rotate(aTransform, localResult.normalA),
                normalB   = math.rotate(aTransform, localResult.normalB),
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
            var            capWorldToLocal           = math.inverse(capsuleTransform);
            var            sphereInCapSpaceTransfrom = math.mul(capWorldToLocal, sphereTransform);
            float3         sphereCenterInCapSpace    = math.transform(sphereInCapSpaceTransfrom, sphere.center);
            SphereCollider sphereInCapSpace          = new SphereCollider(sphereCenterInCapSpace, sphere.radius);
            bool           hit                       = SpatialInternal.CapsuleSphereDistance(capsule,
                                                                                   sphereInCapSpace,
                                                                                   maxDistance,
                                                                                   out SpatialInternal.ColliderDistanceResultInternal localResult);
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
            bool hit = SpatialInternal.CapsuleCapsuleDistance(capsuleA, BinASpace, maxDistance, out SpatialInternal.ColliderDistanceResultInternal localResult);
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
            var            boxWorldToLocal           = math.inverse(boxTransform);
            var            sphereInBoxSpaceTransform = math.mul(boxWorldToLocal, sphereTransform);
            float3         sphereCenterInBoxSpace    = math.transform(sphereInBoxSpaceTransform, sphere.center);
            SphereCollider sphereInBoxSpace          = new SphereCollider(sphereCenterInBoxSpace, sphere.radius);
            bool           hit                       = SpatialInternal.BoxSphereDistance(box,
                                                                               sphereInBoxSpace,
                                                                               maxDistance,
                                                                               out SpatialInternal.ColliderDistanceResultInternal localResult);
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
            bool hit = SpatialInternal.BoxCapsuleDistance(box, capsuleInBoxSpace, maxDistance, out SpatialInternal.ColliderDistanceResultInternal localResult);
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
            var hit                = SpatialInternal.BoxBoxDistance(boxA,
                                                                    boxB,
                                                                    bInASpaceTransform,
                                                                    aInBSpaceTransform,
                                                                    maxDistance,
                                                                    out SpatialInternal.ColliderDistanceResultInternal localResult);
            result = BinAResultToWorld(localResult, aTransform);
            return hit;
        }
        #endregion

        #region Triangle
        public static bool DistanceBetween(TriangleCollider triangle,
                                           RigidTransform triangleTransform,
                                           SphereCollider sphere,
                                           RigidTransform sphereTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            var            triangleWorldToLocal           = math.inverse(triangleTransform);
            var            sphereInTriangleSpaceTransform = math.mul(triangleWorldToLocal, sphereTransform);
            float3         sphereCenterInTriangleSpace    = math.transform(sphereInTriangleSpaceTransform, sphere.center);
            SphereCollider sphereInTriangleSpace          = new SphereCollider(sphereCenterInTriangleSpace, sphere.radius);
            bool           hit                            = SpatialInternal.TriangleSphereDistance(triangle,
                                                                                         sphereInTriangleSpace,
                                                                                         maxDistance,
                                                                                         out SpatialInternal.ColliderDistanceResultInternal localResult);
            result = new ColliderDistanceResult
            {
                hitpointA = math.transform(triangleTransform, localResult.hitpointA),
                hitpointB = math.transform(triangleTransform, localResult.hitpointB),
                normalA   = math.rotate(triangleTransform, localResult.normalA),
                normalB   = math.rotate(triangleTransform, localResult.normalB),
                distance  = localResult.distance
            };
            return hit;
        }

        public static bool DistanceBetween(SphereCollider sphere,
                                           RigidTransform sphereTransform,
                                           TriangleCollider triangle,
                                           RigidTransform triangleTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            bool hit = DistanceBetween(triangle, triangleTransform, sphere, sphereTransform, maxDistance, out ColliderDistanceResult flipResult);
            result   = FlipResult(flipResult);
            return hit;
        }

        public static bool DistanceBetween(TriangleCollider triangle,
                                           RigidTransform triangleTransform,
                                           CapsuleCollider capsule,
                                           RigidTransform capsuleTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            var triangleWorldToLocal        = math.inverse(triangleTransform);
            var capInTriangleSpaceTransform = math.mul(triangleWorldToLocal, capsuleTransform);
            var capsuleInTriangleSpace      = new CapsuleCollider(math.transform(capInTriangleSpaceTransform, capsule.pointA),
                                                                  math.transform(capInTriangleSpaceTransform, capsule.pointB),
                                                                  capsule.radius);
            bool hit = SpatialInternal.TriangleCapsuleDistance(triangle, capsuleInTriangleSpace, maxDistance, out SpatialInternal.ColliderDistanceResultInternal localResult);
            result   = BinAResultToWorld(localResult, triangleTransform);

            return hit;
        }

        public static bool DistanceBetween(CapsuleCollider capsule,
                                           RigidTransform capsuleTransform,
                                           TriangleCollider triangle,
                                           RigidTransform triangleTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            bool hit = DistanceBetween(triangle, triangleTransform, capsule, capsuleTransform, maxDistance, out ColliderDistanceResult flipResult);
            result   = FlipResult(flipResult);
            return hit;
        }

        public static bool DistanceBetween(TriangleCollider triangle,
                                           RigidTransform triangleTransform,
                                           BoxCollider box,
                                           RigidTransform boxTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            // Todo: SAT algorithm similar to box vs box.
            result = DistanceBetweenGjk(triangle, triangleTransform, box, boxTransform);
            return result.distance <= maxDistance;
        }

        public static bool DistanceBetween(BoxCollider box,
                                           RigidTransform boxTransform,
                                           TriangleCollider triangle,
                                           RigidTransform triangleTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            bool hit = DistanceBetween(triangle, triangleTransform, box, boxTransform, maxDistance, out ColliderDistanceResult flipResult);
            result   = FlipResult(flipResult);
            return hit;
        }

        public static bool DistanceBetween(TriangleCollider triangleA,
                                           RigidTransform aTransform,
                                           TriangleCollider triangleB,
                                           RigidTransform bTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            result = DistanceBetweenGjk(triangleA, aTransform, triangleB, bTransform);
            return result.distance <= maxDistance;
        }
        #endregion

        #region Convex
        public static bool DistanceBetween(ConvexCollider convex,
                                           RigidTransform convexTransform,
                                           SphereCollider sphere,
                                           RigidTransform sphereTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            var            convexWorldToLocal           = math.inverse(convexTransform);
            var            sphereInConvexSpaceTransform = math.mul(convexWorldToLocal, sphereTransform);
            float3         sphereCenterInConvexSpace    = math.transform(sphereInConvexSpaceTransform, sphere.center);
            SphereCollider sphereInConvexSpace          = new SphereCollider(sphereCenterInConvexSpace, sphere.radius);
            bool           hit                          = SpatialInternal.ConvexSphereDistance(convex,
                                                                                     sphereInConvexSpace,
                                                                                     maxDistance,
                                                                                     out SpatialInternal.ColliderDistanceResultInternal localResult);
            result = new ColliderDistanceResult
            {
                hitpointA = math.transform(convexTransform, localResult.hitpointA),
                hitpointB = math.transform(convexTransform, localResult.hitpointB),
                normalA   = math.rotate(convexTransform, localResult.normalA),
                normalB   = math.rotate(convexTransform, localResult.normalB),
                distance  = localResult.distance
            };
            return hit;
        }

        public static bool DistanceBetween(SphereCollider sphere,
                                           RigidTransform sphereTransform,
                                           ConvexCollider convex,
                                           RigidTransform convexTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            bool hit = DistanceBetween(convex, convexTransform, sphere, sphereTransform, maxDistance, out ColliderDistanceResult flipResult);
            result   = FlipResult(flipResult);
            return hit;
        }

        public static bool DistanceBetween(ConvexCollider convex,
                                           RigidTransform convexTransform,
                                           CapsuleCollider capsule,
                                           RigidTransform capsuleTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            result = DistanceBetweenGjk(convex, convexTransform, capsule, capsuleTransform);
            return result.distance <= maxDistance;
        }

        public static bool DistanceBetween(CapsuleCollider capsule,
                                           RigidTransform capsuleTransform,
                                           ConvexCollider convex,
                                           RigidTransform convexTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            bool hit = DistanceBetween(convex, convexTransform, capsule, capsuleTransform, maxDistance, out ColliderDistanceResult flipResult);
            result   = FlipResult(flipResult);
            return hit;
        }

        public static bool DistanceBetween(ConvexCollider convex,
                                           RigidTransform convexTransform,
                                           BoxCollider box,
                                           RigidTransform boxTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            result = DistanceBetweenGjk(convex, convexTransform, box, boxTransform);
            return result.distance <= maxDistance;
        }

        public static bool DistanceBetween(BoxCollider box,
                                           RigidTransform boxTransform,
                                           ConvexCollider convex,
                                           RigidTransform convexTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            bool hit = DistanceBetween(convex, convexTransform, box, boxTransform, maxDistance, out ColliderDistanceResult flipResult);
            result   = FlipResult(flipResult);
            return hit;
        }

        public static bool DistanceBetween(ConvexCollider convex,
                                           RigidTransform convexTransform,
                                           TriangleCollider triangle,
                                           RigidTransform triangleTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            result = DistanceBetweenGjk(convex, convexTransform, triangle, triangleTransform);
            return result.distance <= maxDistance;
        }

        public static bool DistanceBetween(TriangleCollider triangle,
                                           RigidTransform triangleTransform,
                                           ConvexCollider convex,
                                           RigidTransform convexTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            bool hit = DistanceBetween(convex, convexTransform, triangle, triangleTransform, maxDistance, out ColliderDistanceResult flipResult);
            result   = FlipResult(flipResult);
            return hit;
        }

        public static bool DistanceBetween(ConvexCollider convexA,
                                           RigidTransform aTransform,
                                           ConvexCollider convexB,
                                           RigidTransform bTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            result = DistanceBetweenGjk(convexA, aTransform, convexB, bTransform);
            return result.distance <= maxDistance;
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

        public static bool DistanceBetween(CompoundCollider compound,
                                           RigidTransform compoundTransform,
                                           TriangleCollider triangle,
                                           RigidTransform triangleTransform,
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
                                                     triangle,
                                                     triangleTransform,
                                                     maxDistance,
                                                     out var newResult);

                newResult.subColliderIndexA  = i;
                newHit                      &= newResult.distance < result.distance;
                hit                         |= newHit;
                result                       = newHit ? newResult : result;
            }
            return hit;
        }

        public static bool DistanceBetween(TriangleCollider triangle,
                                           RigidTransform triangleTransform,
                                           CompoundCollider compound,
                                           RigidTransform compoundTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            bool hit = DistanceBetween(compound, compoundTransform, triangle, triangleTransform, maxDistance, out var flipResult);
            result   = FlipResult(flipResult);
            return hit;
        }

        public static bool DistanceBetween(CompoundCollider compound,
                                           RigidTransform compoundTransform,
                                           ConvexCollider convex,
                                           RigidTransform convexTransform,
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
                                                     convex,
                                                     convexTransform,
                                                     maxDistance,
                                                     out var newResult);

                newResult.subColliderIndexA  = i;
                newHit                      &= newResult.distance < result.distance;
                hit                         |= newHit;
                result                       = newHit ? newResult : result;
            }
            return hit;
        }

        public static bool DistanceBetween(ConvexCollider convex,
                                           RigidTransform convexTransform,
                                           CompoundCollider compound,
                                           RigidTransform compoundTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            bool hit = DistanceBetween(compound, compoundTransform, convex, convexTransform, maxDistance, out var flipResult);
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

        private static ColliderDistanceResult BinAResultToWorld(SpatialInternal.ColliderDistanceResultInternal BinAResult, RigidTransform aTransform)
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

        private static ColliderDistanceResult DistanceBetweenGjk(Collider colliderA, RigidTransform aTransform, Collider colliderB, RigidTransform bTransform)
        {
            var bInATransform = math.mul(math.inverse(aTransform), bTransform);
            var gjkResult     = SpatialInternal.DoGjkEpa(colliderA, colliderB, bInATransform);
            var epsilon       = gjkResult.normalizedOriginToClosestCsoPoint * math.select(1e-4f, -1e-4f, gjkResult.distance < 0f);
            DistanceBetween(gjkResult.hitpointOnAInASpace + epsilon, colliderA, RigidTransform.identity, float.MaxValue, out var closestOnA);
            DistanceBetween(gjkResult.hitpointOnBInASpace - epsilon, colliderB, bInATransform,           float.MaxValue, out var closestOnB);
            return BinAResultToWorld(new SpatialInternal.ColliderDistanceResultInternal
            {
                distance  = gjkResult.distance,
                hitpointA = gjkResult.hitpointOnAInASpace,
                hitpointB = gjkResult.hitpointOnBInASpace,
                normalA   = closestOnA.normal,
                normalB   = closestOnB.normal
            }, aTransform);
        }

        #region Point
        public static bool DistanceBetween(float3 point, SphereCollider sphere, RigidTransform sphereTransform, float maxDistance, out PointDistanceResult result)
        {
            var  pointInSphereSpace = math.transform(math.inverse(sphereTransform), point);
            bool hit                = SpatialInternal.PointSphereDistance(pointInSphereSpace, sphere, maxDistance, out var localResult);
            result                  = new PointDistanceResult
            {
                hitpoint = math.transform(sphereTransform, localResult.hitpoint),
                normal   = math.rotate(sphereTransform, localResult.normal),
                distance = localResult.distance
            };
            return hit;
        }

        public static bool DistanceBetween(float3 point, CapsuleCollider capsule, RigidTransform capsuleTransform, float maxDistance, out PointDistanceResult result)
        {
            var  pointInCapSpace = math.transform(math.inverse(capsuleTransform), point);
            bool hit             = SpatialInternal.PointCapsuleDistance(pointInCapSpace, capsule, maxDistance, out var localResult);
            result               = new PointDistanceResult
            {
                hitpoint = math.transform(capsuleTransform, localResult.hitpoint),
                normal   = math.rotate(capsuleTransform, localResult.normal),
                distance = localResult.distance
            };
            return hit;
        }

        public static bool DistanceBetween(float3 point, BoxCollider box, RigidTransform boxTransform, float maxDistance, out PointDistanceResult result)
        {
            var  pointInBoxSpace = math.transform(math.inverse(boxTransform), point);
            bool hit             = SpatialInternal.PointBoxDistance(pointInBoxSpace, box, maxDistance, out var localResult);
            result               = new PointDistanceResult
            {
                hitpoint = math.transform(boxTransform, localResult.hitpoint),
                normal   = math.rotate(boxTransform, localResult.normal),
                distance = localResult.distance
            };
            return hit;
        }

        public static bool DistanceBetween(float3 point, TriangleCollider triangle, RigidTransform triangleTransform, float maxDistance, out PointDistanceResult result)
        {
            var  pointInTriangleSpace = math.transform(math.inverse(triangleTransform), point);
            bool hit                  = SpatialInternal.PointTriangleDistance(pointInTriangleSpace, triangle, maxDistance, out var localResult);
            result                    = new PointDistanceResult
            {
                hitpoint = math.transform(triangleTransform, localResult.hitpoint),
                normal   = math.rotate(triangleTransform, localResult.normal),
                distance = localResult.distance
            };
            return hit;
        }

        public static bool DistanceBetween(float3 point, ConvexCollider convex, RigidTransform convexTransform, float maxDistance, out PointDistanceResult result)
        {
            var  pointInConvexSpace = math.transform(math.inverse(convexTransform), point);
            bool hit                = SpatialInternal.PointConvexDistance(pointInConvexSpace, convex, maxDistance, out var localResult);
            result                  = new PointDistanceResult
            {
                hitpoint = math.transform(convexTransform, localResult.hitpoint),
                normal   = math.rotate(convexTransform, localResult.normal),
                distance = localResult.distance
            };
            return hit;
        }

        public static bool DistanceBetween(float3 point, CompoundCollider compound, RigidTransform compoundTransform, float maxDistance, out PointDistanceResult result)
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
                bool newHit        = DistanceBetween(point,
                                                     ScaleCollider(blob.colliders[i], compoundScale),
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
        #endregion
    }
}

