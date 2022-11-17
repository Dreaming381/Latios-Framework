using System;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    public static partial class Physics
    {
        #region Sphere
        public static bool DistanceBetween(in SphereCollider sphereA,
                                           in RigidTransform aTransform,
                                           in SphereCollider sphereB,
                                           in RigidTransform bTransform,
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
        public static bool DistanceBetween(in CapsuleCollider capsule,
                                           in RigidTransform capsuleTransform,
                                           in SphereCollider sphere,
                                           in RigidTransform sphereTransform,
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

        public static bool DistanceBetween(in SphereCollider sphere,
                                           in RigidTransform sphereTransform,
                                           in CapsuleCollider capsule,
                                           in RigidTransform capsuleTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            bool hit = DistanceBetween(in capsule, in capsuleTransform, in sphere, in sphereTransform, maxDistance, out ColliderDistanceResult flipResult);
            result   = FlipResult(in flipResult);
            return hit;
        }

        public static bool DistanceBetween(in CapsuleCollider capsuleA,
                                           in RigidTransform aTransform,
                                           in CapsuleCollider capsuleB,
                                           in RigidTransform bTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            var             aWorldToLocal      = math.inverse(aTransform);
            var             BinASpaceTransform = math.mul(aWorldToLocal, bTransform);
            CapsuleCollider BinASpace          = new CapsuleCollider(math.transform(BinASpaceTransform, capsuleB.pointA),
                                                                     math.transform(BinASpaceTransform, capsuleB.pointB),
                                                                     capsuleB.radius);
            bool hit = SpatialInternal.CapsuleCapsuleDistance(capsuleA, BinASpace, maxDistance, out SpatialInternal.ColliderDistanceResultInternal localResult);
            result   = BinAResultToWorld(in localResult, in aTransform);
            return hit;
        }
        #endregion

        #region Box
        public static bool DistanceBetween(in BoxCollider box,
                                           in RigidTransform boxTransform,
                                           in SphereCollider sphere,
                                           in RigidTransform sphereTransform,
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

        public static bool DistanceBetween(in SphereCollider sphere,
                                           in RigidTransform sphereTransform,
                                           in BoxCollider box,
                                           in RigidTransform boxTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            bool hit = DistanceBetween(in box, in boxTransform, in sphere, in sphereTransform, maxDistance, out ColliderDistanceResult flipResult);
            result   = FlipResult(in flipResult);
            return hit;
        }

        public static bool DistanceBetween(in BoxCollider box,
                                           in RigidTransform boxTransform,
                                           in CapsuleCollider capsule,
                                           in RigidTransform capsuleTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            var boxWorldToLocal        = math.inverse(boxTransform);
            var capInBoxSpaceTransform = math.mul(boxWorldToLocal, capsuleTransform);
            var capsuleInBoxSpace      = new CapsuleCollider(math.transform(capInBoxSpaceTransform, capsule.pointA),
                                                             math.transform(capInBoxSpaceTransform, capsule.pointB),
                                                             capsule.radius);
            bool hit = SpatialInternal.BoxCapsuleDistance(box, capsuleInBoxSpace, maxDistance, out SpatialInternal.ColliderDistanceResultInternal localResult);
            result   = BinAResultToWorld(in localResult, in boxTransform);

            return hit;
        }

        public static bool DistanceBetween(in CapsuleCollider capsule,
                                           in RigidTransform capsuleTransform,
                                           in BoxCollider box,
                                           in RigidTransform boxTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            bool hit = DistanceBetween(in box, in boxTransform, in capsule, in capsuleTransform, maxDistance, out ColliderDistanceResult flipResult);
            result   = FlipResult(in flipResult);
            return hit;
        }

        public static bool DistanceBetween(in BoxCollider boxA,
                                           in RigidTransform aTransform,
                                           in BoxCollider boxB,
                                           in RigidTransform bTransform,
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
            result = BinAResultToWorld(in localResult, in aTransform);
            return hit;
        }
        #endregion

        #region Triangle
        public static bool DistanceBetween(in TriangleCollider triangle,
                                           in RigidTransform triangleTransform,
                                           in SphereCollider sphere,
                                           in RigidTransform sphereTransform,
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

        public static bool DistanceBetween(in SphereCollider sphere,
                                           in RigidTransform sphereTransform,
                                           in TriangleCollider triangle,
                                           in RigidTransform triangleTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            bool hit = DistanceBetween(in triangle, in triangleTransform, in sphere, in sphereTransform, maxDistance, out ColliderDistanceResult flipResult);
            result   = FlipResult(in flipResult);
            return hit;
        }

        public static bool DistanceBetween(in TriangleCollider triangle, in RigidTransform triangleTransform, in CapsuleCollider capsule, in RigidTransform capsuleTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            var triangleWorldToLocal        = math.inverse(triangleTransform);
            var capInTriangleSpaceTransform = math.mul(triangleWorldToLocal, capsuleTransform);
            var capsuleInTriangleSpace      = new CapsuleCollider(math.transform(capInTriangleSpaceTransform, capsule.pointA),
                                                                  math.transform(capInTriangleSpaceTransform, capsule.pointB),
                                                                  capsule.radius);
            bool hit = SpatialInternal.TriangleCapsuleDistance(triangle, capsuleInTriangleSpace, maxDistance, out SpatialInternal.ColliderDistanceResultInternal localResult);
            result   = BinAResultToWorld(in localResult, in triangleTransform);

            return hit;
        }

        public static bool DistanceBetween(in CapsuleCollider capsule, in RigidTransform capsuleTransform, in TriangleCollider triangle, in RigidTransform triangleTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            bool hit = DistanceBetween(in triangle, in triangleTransform, in capsule, in capsuleTransform, maxDistance, out ColliderDistanceResult flipResult);
            result   = FlipResult(in flipResult);
            return hit;
        }

        public static bool DistanceBetween(in TriangleCollider triangle, in RigidTransform triangleTransform, in BoxCollider box, in RigidTransform boxTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            // Todo: SAT algorithm similar to box vs box.
            result = DistanceBetweenGjk(triangle, in triangleTransform, box, in boxTransform);
            return result.distance <= maxDistance;
        }

        public static bool DistanceBetween(in BoxCollider box, in RigidTransform boxTransform, in TriangleCollider triangle, in RigidTransform triangleTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            bool hit = DistanceBetween(in triangle, in triangleTransform, in box, in boxTransform, maxDistance, out ColliderDistanceResult flipResult);
            result   = FlipResult(in flipResult);
            return hit;
        }

        public static bool DistanceBetween(in TriangleCollider triangleA, in RigidTransform aTransform, in TriangleCollider triangleB, in RigidTransform bTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            result = DistanceBetweenGjk(triangleA, in aTransform, triangleB, in bTransform);
            return result.distance <= maxDistance;
        }
        #endregion

        #region Convex
        public static bool DistanceBetween(in ConvexCollider convex, in RigidTransform convexTransform, in SphereCollider sphere, in RigidTransform sphereTransform,
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

        public static bool DistanceBetween(in SphereCollider sphere, in RigidTransform sphereTransform, in ConvexCollider convex, in RigidTransform convexTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            bool hit = DistanceBetween(in convex, in convexTransform, in sphere, in sphereTransform, maxDistance, out ColliderDistanceResult flipResult);
            result   = FlipResult(in flipResult);
            return hit;
        }

        public static bool DistanceBetween(in ConvexCollider convex, in RigidTransform convexTransform, in CapsuleCollider capsule, in RigidTransform capsuleTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            result = DistanceBetweenGjk(convex, in convexTransform, capsule, in capsuleTransform);
            return result.distance <= maxDistance;
        }

        public static bool DistanceBetween(in CapsuleCollider capsule, in RigidTransform capsuleTransform, in ConvexCollider convex, in RigidTransform convexTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            bool hit = DistanceBetween(in convex, in convexTransform, in capsule, in capsuleTransform, maxDistance, out ColliderDistanceResult flipResult);
            result   = FlipResult(in flipResult);
            return hit;
        }

        public static bool DistanceBetween(in ConvexCollider convex, in RigidTransform convexTransform, in BoxCollider box, in RigidTransform boxTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            result = DistanceBetweenGjk(convex, in convexTransform, box, in boxTransform);
            return result.distance <= maxDistance;
        }

        public static bool DistanceBetween(in BoxCollider box, in RigidTransform boxTransform, in ConvexCollider convex, in RigidTransform convexTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            bool hit = DistanceBetween(in convex, in convexTransform, in box, in boxTransform, maxDistance, out ColliderDistanceResult flipResult);
            result   = FlipResult(in flipResult);
            return hit;
        }

        public static bool DistanceBetween(in ConvexCollider convex, in RigidTransform convexTransform, in TriangleCollider triangle, in RigidTransform triangleTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            result = DistanceBetweenGjk(convex, in convexTransform, triangle, in triangleTransform);
            return result.distance <= maxDistance;
        }

        public static bool DistanceBetween(in TriangleCollider triangle, in RigidTransform triangleTransform, in ConvexCollider convex, in RigidTransform convexTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            bool hit = DistanceBetween(in convex, in convexTransform, in triangle, in triangleTransform, maxDistance, out ColliderDistanceResult flipResult);
            result   = FlipResult(in flipResult);
            return hit;
        }

        public static bool DistanceBetween(in ConvexCollider convexA, in RigidTransform aTransform, in ConvexCollider convexB, in RigidTransform bTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            result = DistanceBetweenGjk(convexA, in aTransform, convexB, in bTransform);
            return result.distance <= maxDistance;
        }
        #endregion

        #region Compound
        public static bool DistanceBetween(in CompoundCollider compound, in RigidTransform compoundTransform, in SphereCollider sphere, in RigidTransform sphereTransform,
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
                bool newHit        = DistanceBetween(ScaleCollider(in blob.colliders[i], compoundScale),
                                                     math.mul(compoundTransform, blobTransform),
                                                     in sphere,
                                                     in sphereTransform,
                                                     math.min(result.distance, maxDistance),
                                                     out var newResult);

                newResult.subColliderIndexA  = i;
                newHit                      &= newResult.distance < result.distance;
                hit                         |= newHit;
                result                       = newHit ? newResult : result;
            }
            return hit;
        }

        public static bool DistanceBetween(in SphereCollider sphere, in RigidTransform sphereTransform, in CompoundCollider compound, in RigidTransform compoundTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            bool hit = DistanceBetween(in compound, in compoundTransform, in sphere, in sphereTransform, maxDistance, out var flipResult);
            result   = FlipResult(in flipResult);
            return hit;
        }

        public static bool DistanceBetween(in CompoundCollider compound, in RigidTransform compoundTransform, in CapsuleCollider capsule, in RigidTransform capsuleTransform,
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
                bool newHit        = DistanceBetween(ScaleCollider(in blob.colliders[i], compoundScale),
                                                     math.mul(compoundTransform, blobTransform),
                                                     in capsule,
                                                     in capsuleTransform,
                                                     math.min(result.distance, maxDistance),
                                                     out var newResult);

                newResult.subColliderIndexA  = i;
                newHit                      &= newResult.distance < result.distance;
                hit                         |= newHit;
                result                       = newHit ? newResult : result;
            }
            return hit;
        }

        public static bool DistanceBetween(in CapsuleCollider capsule, in RigidTransform capsuleTransform, in CompoundCollider compound, in RigidTransform compoundTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            bool hit = DistanceBetween(in compound, in compoundTransform, in capsule, in capsuleTransform, maxDistance, out var flipResult);
            result   = FlipResult(in flipResult);
            return hit;
        }

        public static bool DistanceBetween(in CompoundCollider compound, in RigidTransform compoundTransform, in BoxCollider box, in RigidTransform boxTransform,
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
                bool newHit        = DistanceBetween(ScaleCollider(in blob.colliders[i], compoundScale),
                                                     math.mul(compoundTransform, blobTransform),
                                                     in box,
                                                     in boxTransform,
                                                     maxDistance,
                                                     out var newResult);

                newResult.subColliderIndexA  = i;
                newHit                      &= newResult.distance < result.distance;
                hit                         |= newHit;
                result                       = newHit ? newResult : result;
            }
            return hit;
        }

        public static bool DistanceBetween(in BoxCollider box, in RigidTransform boxTransform, in CompoundCollider compound, in RigidTransform compoundTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            bool hit = DistanceBetween(in compound, in compoundTransform, in box, in boxTransform, maxDistance, out var flipResult);
            result   = FlipResult(in flipResult);
            return hit;
        }

        public static bool DistanceBetween(in CompoundCollider compound, in RigidTransform compoundTransform, in TriangleCollider triangle, in RigidTransform triangleTransform,
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
                bool newHit        = DistanceBetween(ScaleCollider(in blob.colliders[i], compoundScale),
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

        public static bool DistanceBetween(in TriangleCollider triangle, in RigidTransform triangleTransform, in CompoundCollider compound, in RigidTransform compoundTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            bool hit = DistanceBetween(in compound, in compoundTransform, in triangle, in triangleTransform, maxDistance, out var flipResult);
            result   = FlipResult(in flipResult);
            return hit;
        }

        public static bool DistanceBetween(in CompoundCollider compound, in RigidTransform compoundTransform, in ConvexCollider convex, in RigidTransform convexTransform,
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
                bool newHit        = DistanceBetween(ScaleCollider(in blob.colliders[i], compoundScale),
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

        public static bool DistanceBetween(in ConvexCollider convex, in RigidTransform convexTransform, in CompoundCollider compound, in RigidTransform compoundTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            bool hit = DistanceBetween(in compound, in compoundTransform, in convex, in convexTransform, maxDistance, out var flipResult);
            result   = FlipResult(in flipResult);
            return hit;
        }

        public static bool DistanceBetween(in CompoundCollider compoundA, in RigidTransform aTransform, in CompoundCollider compoundB, in RigidTransform bTransform,
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
                bool newHit        = DistanceBetween(ScaleCollider(in blob.colliders[i], compoundScale),
                                                     math.mul(aTransform, blobTransform),
                                                     in compoundB,
                                                     in bTransform,
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

        #region Layer
        public static bool DistanceBetween(Collider collider,
                                           RigidTransform transform,
                                           CollisionLayer layer,
                                           float maxDistance,
                                           out ColliderDistanceResult result,
                                           out LayerBodyInfo layerBodyInfo)
        {
            result              = default;
            layerBodyInfo       = default;
            var processor       = new LayerQueryProcessors.ColliderDistanceClosestImmediateProcessor(collider, transform, maxDistance, ref result, ref layerBodyInfo);
            var aabb            = AabbFrom(in collider, in transform);
            var offsetDistance  = math.max(maxDistance, 0f);
            aabb.min           -= offsetDistance;
            aabb.max           += offsetDistance;
            FindObjects(aabb, layer, processor).RunImmediate();
            var hit                  = result.subColliderIndexB >= 0;
            result.subColliderIndexB = math.max(result.subColliderIndexB, 0);
            return hit;
        }

        public static bool DistanceBetweenAny(Collider collider,
                                              RigidTransform transform,
                                              CollisionLayer layer,
                                              float maxDistance,
                                              out ColliderDistanceResult result,
                                              out LayerBodyInfo layerBodyInfo)
        {
            result              = default;
            layerBodyInfo       = default;
            var processor       = new LayerQueryProcessors.ColliderDistanceAnyImmediateProcessor(collider, transform, maxDistance, ref result, ref layerBodyInfo);
            var aabb            = AabbFrom(in collider, in transform);
            var offsetDistance  = math.max(maxDistance, 0f);
            aabb.min           -= offsetDistance;
            aabb.max           += offsetDistance;
            FindObjects(aabb, layer, processor).RunImmediate();
            var hit                  = result.subColliderIndexB >= 0;
            result.subColliderIndexB = math.max(result.subColliderIndexB, 0);
            return hit;
        }
        #endregion

        private static ColliderDistanceResult FlipResult(in ColliderDistanceResult resultToFlip)
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

        private static ColliderDistanceResult BinAResultToWorld(in SpatialInternal.ColliderDistanceResultInternal BinAResult, in RigidTransform aTransform)
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

        private static ColliderDistanceResult DistanceBetweenGjk(in Collider colliderA, in RigidTransform aTransform, in Collider colliderB, in RigidTransform bTransform)
        {
            var bInATransform = math.mul(math.inverse(aTransform), bTransform);
            var gjkResult     = SpatialInternal.DoGjkEpa(colliderA, colliderB, bInATransform);
            var epsilon       = gjkResult.normalizedOriginToClosestCsoPoint * math.select(1e-4f, -1e-4f, gjkResult.distance < 0f);
            DistanceBetween(gjkResult.hitpointOnAInASpace + epsilon, in colliderA, RigidTransform.identity, float.MaxValue, out var closestOnA);
            DistanceBetween(gjkResult.hitpointOnBInASpace - epsilon, in colliderB, in bInATransform,        float.MaxValue, out var closestOnB);
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
        public static bool DistanceBetween(float3 point, in SphereCollider sphere, in RigidTransform sphereTransform, float maxDistance, out PointDistanceResult result)
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

        public static bool DistanceBetween(float3 point, in CapsuleCollider capsule, in RigidTransform capsuleTransform, float maxDistance, out PointDistanceResult result)
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

        public static bool DistanceBetween(float3 point, in BoxCollider box, in RigidTransform boxTransform, float maxDistance, out PointDistanceResult result)
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

        public static bool DistanceBetween(float3 point, in TriangleCollider triangle, in RigidTransform triangleTransform, float maxDistance, out PointDistanceResult result)
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

        public static bool DistanceBetween(float3 point, in ConvexCollider convex, in RigidTransform convexTransform, float maxDistance, out PointDistanceResult result)
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

        public static bool DistanceBetween(float3 point, in CompoundCollider compound, in RigidTransform compoundTransform, float maxDistance, out PointDistanceResult result)
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
                                                     ScaleCollider(in blob.colliders[i], compoundScale),
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

        public static bool DistanceBetween(float3 point, in CollisionLayer layer, float maxDistance, out PointDistanceResult result, out LayerBodyInfo layerBodyInfo)
        {
            result             = default;
            layerBodyInfo      = default;
            var processor      = new LayerQueryProcessors.PointDistanceClosestImmediateProcessor(point, maxDistance, ref result, ref layerBodyInfo);
            var offsetDistance = math.max(maxDistance, 0f);
            FindObjects(AabbFrom(point - offsetDistance, point + offsetDistance), layer, processor).RunImmediate();
            var hit                 = result.subColliderIndex >= 0;
            result.subColliderIndex = math.max(result.subColliderIndex, 0);
            return hit;
        }

        public static bool DistanceBetweenAny(float3 point, in CollisionLayer layer, float maxDistance, out PointDistanceResult result, out LayerBodyInfo layerBodyInfo)
        {
            result             = default;
            layerBodyInfo      = default;
            var processor      = new LayerQueryProcessors.PointDistanceAnyImmediateProcessor(point, maxDistance, ref result, ref layerBodyInfo);
            var offsetDistance = math.max(maxDistance, 0f);
            FindObjects(AabbFrom(point - offsetDistance, point + offsetDistance), layer, processor).RunImmediate();
            var hit                 = result.subColliderIndex >= 0;
            result.subColliderIndex = math.max(result.subColliderIndex, 0);
            return hit;
        }
        #endregion
    }
}

