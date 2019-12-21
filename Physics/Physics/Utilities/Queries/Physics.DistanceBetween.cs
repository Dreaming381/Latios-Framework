using System;
using Unity.Mathematics;

namespace Latios.PhysicsEngine
{
    public static partial class Physics
    {
        //All algorithms return a negative distance if inside the collider.
        public static bool DistanceBetween(float3 point, SphereCollider sphere, RigidTransform sphereTransform, float maxDistance, out PointDistanceResult result)
        {
            float3 pointInSphereSpace = point - sphereTransform.pos;
            bool   hit                = DistanceQueries.DistanceBetween(pointInSphereSpace, sphere, maxDistance, out DistanceQueries.PointDistanceResultInternal localResult);
            result                    = new PointDistanceResult
            {
                hitpoint = localResult.hitpoint + sphereTransform.pos,
                distance = localResult.distance,
                normal   = localResult.normal
            };
            return hit;
        }

        //Todo: Other point queries

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
    }
}

