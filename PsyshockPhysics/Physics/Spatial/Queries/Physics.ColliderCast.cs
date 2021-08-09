using Unity.Mathematics;

namespace Latios.Psyshock
{
    public static partial class Physics
    {
        #region Sphere
        public static bool ColliderCast(SphereCollider sphereToCast,
                                        RigidTransform castStart,
                                        float3 castEnd,
                                        SphereCollider targetSphere,
                                        RigidTransform targetSphereTransform,
                                        out ColliderCastResult result)
        {
            var cso     = targetSphere;
            cso.radius += sphereToCast.radius;
            var  start  = math.transform(castStart, sphereToCast.center);
            var  ray    = new Ray(start, start + castEnd - castStart.pos);
            bool hit    = Raycast(ray, cso, targetSphereTransform, out var raycastResult);
            if (hit)
            {
                var hitTransform  = castStart;
                hitTransform.pos += raycastResult.position - start;
                DistanceBetween(sphereToCast, hitTransform, targetSphere, targetSphereTransform, 1f, out var distanceResult);
                result = new ColliderCastResult
                {
                    hitpointOnCaster         = distanceResult.hitpointA,
                    hitpointOnTarget         = distanceResult.hitpointB,
                    normalOnSweep            = distanceResult.normalA,
                    normalOnTarget           = distanceResult.normalB,
                    subColliderIndexOnCaster = distanceResult.subColliderIndexA,
                    subColliderIndexOnTarget = distanceResult.subColliderIndexB,
                    distance                 = math.distance(hitTransform.pos, castStart.pos)
                };
                return true;
            }
            result = default;
            return false;
        }

        public static bool ColliderCast(SphereCollider sphereToCast,
                                        RigidTransform castStart,
                                        float3 castEnd,
                                        CapsuleCollider targetCapsule,
                                        RigidTransform targetCapsuleTransform,
                                        out ColliderCastResult result)
        {
            var cso     = targetCapsule;
            cso.radius += sphereToCast.radius;
            var  start  = math.transform(castStart, sphereToCast.center);
            var  ray    = new Ray(start, start + castEnd - castStart.pos);
            bool hit    = Raycast(ray, cso, targetCapsuleTransform, out var raycastResult);
            if (hit)
            {
                var hitTransform  = castStart;
                hitTransform.pos += raycastResult.position - start;
                DistanceBetween(sphereToCast, hitTransform, targetCapsule, targetCapsuleTransform, 1f, out var distanceResult);
                result = new ColliderCastResult
                {
                    hitpointOnCaster         = distanceResult.hitpointA,
                    hitpointOnTarget         = distanceResult.hitpointB,
                    normalOnSweep            = distanceResult.normalA,
                    normalOnTarget           = distanceResult.normalB,
                    subColliderIndexOnCaster = distanceResult.subColliderIndexA,
                    subColliderIndexOnTarget = distanceResult.subColliderIndexB,
                    distance                 = math.distance(hitTransform.pos, castStart.pos)
                };
                return true;
            }
            result = default;
            return false;
        }

        public static bool ColliderCast(SphereCollider sphereToCast,
                                        RigidTransform castStart,
                                        float3 castEnd,
                                        BoxCollider targetBox,
                                        RigidTransform targetBoxTransform,
                                        out ColliderCastResult result)
        {
            var  targetBoxTransformInverse = math.inverse(targetBoxTransform);
            var  casterInTargetSpace       = math.mul(targetBoxTransformInverse, castStart);
            var  start                     = math.transform(casterInTargetSpace, sphereToCast.center);
            var  ray                       = new Ray(start, start + math.rotate(targetBoxTransformInverse, castEnd - castStart.pos));
            bool hit                       = Raycasting.RaycastRoundedBox(ray, targetBox, sphereToCast.radius, out var fraction, out var normal);
            if (hit)
            {
                var hitTransform = castStart;
                hitTransform.pos = math.lerp(castStart.pos, castEnd, fraction);
                DistanceBetween(sphereToCast, hitTransform, targetBox, targetBoxTransform, 1f, out var distanceResult);
                result = new ColliderCastResult
                {
                    hitpointOnCaster         = distanceResult.hitpointA,
                    hitpointOnTarget         = distanceResult.hitpointB,
                    normalOnSweep            = distanceResult.normalA,
                    normalOnTarget           = distanceResult.normalB,
                    subColliderIndexOnCaster = distanceResult.subColliderIndexA,
                    subColliderIndexOnTarget = distanceResult.subColliderIndexB,
                    distance                 = math.distance(hitTransform.pos, castStart.pos)
                };
                return true;
            }
            result = default;
            return false;
        }

        public static bool ColliderCast(SphereCollider sphereToCast,
                                        RigidTransform castStart,
                                        float3 castEnd,
                                        CompoundCollider targetCompound,
                                        RigidTransform targetCompoundTransform,
                                        out ColliderCastResult result)
        {
            bool hit              = false;
            result                = default;
            result.distance       = float.MaxValue;
            ref var blob          = ref targetCompound.compoundColliderBlob.Value;
            var     compoundScale = new PhysicsScale { scale = targetCompound.scale, state = PhysicsScale.State.Uniform };
            for (int i = 0; i < blob.colliders.Length; i++)
            {
                var blobTransform  = blob.transforms[i];
                blobTransform.pos *= targetCompound.scale;
                bool newHit        = ColliderCast(sphereToCast, castStart, castEnd, ScaleCollider(blob.colliders[i], compoundScale),
                                                  math.mul(targetCompoundTransform, blobTransform),
                                                  out var newResult);

                newResult.subColliderIndexOnTarget  = i;
                newHit                             &= newResult.distance < result.distance;
                hit                                |= newHit;
                result                              = newHit ? newResult : result;
            }
            return hit;
        }
        #endregion

        #region Capsule
        public static bool ColliderCast(CapsuleCollider capsuleToCast,
                                        RigidTransform castStart,
                                        float3 castEnd,
                                        SphereCollider targetSphere,
                                        RigidTransform targetSphereTransform,
                                        out ColliderCastResult result)
        {
            var cso           = capsuleToCast;
            cso.radius       += targetSphere.radius;
            var  castReverse  = castStart.pos - castEnd;
            var  start        = math.transform(targetSphereTransform, targetSphere.center);
            var  ray          = new Ray(start, start + castReverse);
            bool hit          = Raycast(ray, cso, targetSphereTransform, out var raycastResult);
            if (hit)
            {
                var hitTransform  = castStart;
                hitTransform.pos -= castReverse - (raycastResult.position - start);
                DistanceBetween(capsuleToCast, hitTransform, targetSphere, targetSphereTransform, 1f, out var distanceResult);
                result = new ColliderCastResult
                {
                    hitpointOnCaster         = distanceResult.hitpointA,
                    hitpointOnTarget         = distanceResult.hitpointB,
                    normalOnSweep            = distanceResult.normalA,
                    normalOnTarget           = distanceResult.normalB,
                    subColliderIndexOnCaster = distanceResult.subColliderIndexA,
                    subColliderIndexOnTarget = distanceResult.subColliderIndexB,
                    distance                 = math.distance(hitTransform.pos, castStart.pos)
                };
                return true;
            }
            result = default;
            return false;
        }

        public static bool ColliderCast(CapsuleCollider capsuleToCast,
                                        RigidTransform castStart,
                                        float3 castEnd,
                                        CapsuleCollider targetCapsule,
                                        RigidTransform targetCapsuleTransform,
                                        out ColliderCastResult result)
        {
            var  csoRadius           = capsuleToCast.radius + targetCapsule.radius;
            var  casterAInWorldSpace = math.transform(castStart, capsuleToCast.pointA);
            var  casterBInWorldSpace = math.transform(castStart, capsuleToCast.pointB);
            var  targetAInWorldSpace = math.transform(targetCapsuleTransform, targetCapsule.pointA);
            var  targetBInWorldSpace = math.transform(targetCapsuleTransform, targetCapsule.pointB);
            var  csoExtension        = casterAInWorldSpace - casterBInWorldSpace;
            var  cso                 = new simdFloat3(targetAInWorldSpace, targetAInWorldSpace + csoExtension, targetBInWorldSpace + csoExtension, targetBInWorldSpace);
            var  ray                 = new Ray(casterAInWorldSpace, casterAInWorldSpace + (castStart.pos - castEnd));
            bool hit                 = Raycasting.RaycastRoundedQuad(ray, cso, csoRadius, out float fraction, out float3 normal);
            if (hit)
            {
                var hitTransform = castStart;
                hitTransform.pos = math.lerp(castStart.pos, castEnd.x, fraction);
                DistanceBetween(capsuleToCast, hitTransform, targetCapsule, targetCapsuleTransform, 1f, out var distanceResult);
                result = new ColliderCastResult
                {
                    hitpointOnCaster         = distanceResult.hitpointA,
                    hitpointOnTarget         = distanceResult.hitpointB,
                    normalOnSweep            = distanceResult.normalA,
                    normalOnTarget           = distanceResult.normalB,
                    subColliderIndexOnCaster = distanceResult.subColliderIndexA,
                    subColliderIndexOnTarget = distanceResult.subColliderIndexB,
                    distance                 = math.distance(hitTransform.pos, castStart.pos)
                };
                return true;
            }
            result = default;
            return false;
        }

        public static bool ColliderCast(CapsuleCollider capsuleToCast,
                                        RigidTransform castStart,
                                        float3 castEnd,
                                        BoxCollider targetBox,
                                        RigidTransform targetBoxTransform,
                                        out ColliderCastResult result)
        {
            var stepper = new CapsuleBoxStepper { capsuleCaster = capsuleToCast, boxTarget = targetBox };
            return ConvexConservativeAdvancementCast(stepper, castStart, castEnd, targetBoxTransform, out result);
        }

        public static bool ColliderCast(CapsuleCollider capsuleToCast,
                                        RigidTransform castStart,
                                        float3 castEnd,
                                        CompoundCollider targetCompound,
                                        RigidTransform targetCompoundTransform,
                                        out ColliderCastResult result)
        {
            bool hit              = false;
            result                = default;
            result.distance       = float.MaxValue;
            ref var blob          = ref targetCompound.compoundColliderBlob.Value;
            var     compoundScale = new PhysicsScale { scale = targetCompound.scale, state = PhysicsScale.State.Uniform };
            for (int i = 0; i < blob.colliders.Length; i++)
            {
                var blobTransform  = blob.transforms[i];
                blobTransform.pos *= targetCompound.scale;
                bool newHit        = ColliderCast(capsuleToCast, castStart, castEnd, ScaleCollider(blob.colliders[i], compoundScale),
                                                  math.mul(targetCompoundTransform, blobTransform),
                                                  out var newResult);

                newResult.subColliderIndexOnTarget  = i;
                newHit                             &= newResult.distance < result.distance;
                hit                                |= newHit;
                result                              = newHit ? newResult : result;
            }
            return hit;
        }
        #endregion

        #region Box
        public static bool ColliderCast(BoxCollider boxToCast,
                                        RigidTransform castStart,
                                        float3 castEnd,
                                        SphereCollider targetSphere,
                                        RigidTransform targetSphereTransform,
                                        out ColliderCastResult result)
        {
            var  castReverse        = castStart.pos - castEnd;
            var  worldToCasterSpace = math.inverse(castStart);
            var  start              = math.transform(targetSphereTransform, targetSphere.center);
            var  ray                = new Ray(math.transform(worldToCasterSpace, start), math.transform(worldToCasterSpace, start + castReverse));
            bool hit                = Raycasting.RaycastRoundedBox(ray, boxToCast, targetSphere.radius, out var fraction, out var normal);
            if (hit)
            {
                var hitTransform = castStart;
                hitTransform.pos = math.lerp(castEnd, castStart.pos, fraction);
                DistanceBetween(boxToCast, hitTransform, targetSphere, targetSphereTransform, 1f, out var distanceResult);
                result = new ColliderCastResult
                {
                    hitpointOnCaster         = distanceResult.hitpointA,
                    hitpointOnTarget         = distanceResult.hitpointB,
                    normalOnSweep            = distanceResult.normalA,
                    normalOnTarget           = distanceResult.normalB,
                    subColliderIndexOnCaster = distanceResult.subColliderIndexA,
                    subColliderIndexOnTarget = distanceResult.subColliderIndexB,
                    distance                 = math.distance(hitTransform.pos, castStart.pos)
                };
                return true;
            }
            result = default;
            return false;
        }

        public static bool ColliderCast(BoxCollider boxToCast,
                                        RigidTransform castStart,
                                        float3 castEnd,
                                        CapsuleCollider targetCapsule,
                                        RigidTransform targetCapsuleTransform,
                                        out ColliderCastResult result)
        {
            var stepper = new BoxCapsuleStepper { boxCaster = boxToCast, capsuleTarget = targetCapsule };
            return ConvexConservativeAdvancementCast(stepper, castStart, castEnd, targetCapsuleTransform, out result);
        }

        public static bool ColliderCast(BoxCollider boxToCast,
                                        RigidTransform castStart,
                                        float3 castEnd,
                                        BoxCollider targetBox,
                                        RigidTransform targetBoxTransform,
                                        out ColliderCastResult result)
        {
            var stepper = new BoxBoxStepper { boxCaster = boxToCast, boxTarget = targetBox };
            return ConvexConservativeAdvancementCast(stepper, castStart, castEnd, targetBoxTransform, out result);
        }

        public static bool ColliderCast(BoxCollider boxToCast,
                                        RigidTransform castStart,
                                        float3 castEnd,
                                        CompoundCollider targetCompound,
                                        RigidTransform targetCompoundTransform,
                                        out ColliderCastResult result)
        {
            bool hit              = false;
            result                = default;
            result.distance       = float.MaxValue;
            ref var blob          = ref targetCompound.compoundColliderBlob.Value;
            var     compoundScale = new PhysicsScale { scale = targetCompound.scale, state = PhysicsScale.State.Uniform };
            for (int i = 0; i < blob.colliders.Length; i++)
            {
                var blobTransform  = blob.transforms[i];
                blobTransform.pos *= targetCompound.scale;
                bool newHit        = ColliderCast(boxToCast, castStart, castEnd, ScaleCollider(blob.colliders[i], compoundScale),
                                                  math.mul(targetCompoundTransform, blobTransform),
                                                  out var newResult);

                newResult.subColliderIndexOnTarget  = i;
                newHit                             &= newResult.distance < result.distance;
                hit                                |= newHit;
                result                              = newHit ? newResult : result;
            }
            return hit;
        }
        #endregion

        #region Compound
        public static bool ColliderCast(CompoundCollider compountToCast,
                                        RigidTransform castStart,
                                        float3 castEnd,
                                        SphereCollider targetSphere,
                                        RigidTransform targetSphereTransform,
                                        out ColliderCastResult result)
        {
            bool hit              = false;
            result                = default;
            result.distance       = float.MaxValue;
            ref var blob          = ref compountToCast.compoundColliderBlob.Value;
            var     compoundScale = new PhysicsScale { scale = compountToCast.scale, state = PhysicsScale.State.Uniform };
            for (int i = 0; i < blob.colliders.Length; i++)
            {
                var blobTransform  = blob.transforms[i];
                blobTransform.pos *= compountToCast.scale;
                var  start         = math.mul(castStart, blobTransform);
                bool newHit        = ColliderCast(ScaleCollider(blob.colliders[i], compoundScale),
                                                  start, start.pos + (castEnd - castStart.pos),
                                                  targetSphere,
                                                  targetSphereTransform,
                                                  out var newResult);

                newResult.subColliderIndexOnCaster  = i;
                newHit                             &= newResult.distance < result.distance;
                hit                                |= newHit;
                result                              = newHit ? newResult : result;
            }
            return hit;
        }

        public static bool ColliderCast(CompoundCollider compountToCast,
                                        RigidTransform castStart,
                                        float3 castEnd,
                                        CapsuleCollider targetCapsule,
                                        RigidTransform targetCapsuleTransform,
                                        out ColliderCastResult result)
        {
            bool hit              = false;
            result                = default;
            result.distance       = float.MaxValue;
            ref var blob          = ref compountToCast.compoundColliderBlob.Value;
            var     compoundScale = new PhysicsScale { scale = compountToCast.scale, state = PhysicsScale.State.Uniform };
            for (int i = 0; i < blob.colliders.Length; i++)
            {
                var blobTransform  = blob.transforms[i];
                blobTransform.pos *= compountToCast.scale;
                var  start         = math.mul(castStart, blobTransform);
                bool newHit        = ColliderCast(ScaleCollider(blob.colliders[i], compoundScale),
                                                  start, start.pos + (castEnd - castStart.pos),
                                                  targetCapsule,
                                                  targetCapsuleTransform,
                                                  out var newResult);

                newResult.subColliderIndexOnCaster  = i;
                newHit                             &= newResult.distance < result.distance;
                hit                                |= newHit;
                result                              = newHit ? newResult : result;
            }
            return hit;
        }

        public static bool ColliderCast(CompoundCollider compountToCast,
                                        RigidTransform castStart,
                                        float3 castEnd,
                                        BoxCollider targetBox,
                                        RigidTransform ttargetBoxTransform,
                                        out ColliderCastResult result)
        {
            bool hit              = false;
            result                = default;
            result.distance       = float.MaxValue;
            ref var blob          = ref compountToCast.compoundColliderBlob.Value;
            var     compoundScale = new PhysicsScale { scale = compountToCast.scale, state = PhysicsScale.State.Uniform };
            for (int i = 0; i < blob.colliders.Length; i++)
            {
                var blobTransform  = blob.transforms[i];
                blobTransform.pos *= compountToCast.scale;
                var  start         = math.mul(castStart, blobTransform);
                bool newHit        = ColliderCast(ScaleCollider(blob.colliders[i], compoundScale),
                                                  start, start.pos + (castEnd - castStart.pos),
                                                  targetBox,
                                                  ttargetBoxTransform,
                                                  out var newResult);

                newResult.subColliderIndexOnCaster  = i;
                newHit                             &= newResult.distance < result.distance;
                hit                                |= newHit;
                result                              = newHit ? newResult : result;
            }
            return hit;
        }

        public static bool ColliderCast(CompoundCollider compountToCast,
                                        RigidTransform castStart,
                                        float3 castEnd,
                                        CompoundCollider targetCompound,
                                        RigidTransform targetCompoundTransform,
                                        out ColliderCastResult result)
        {
            bool hit              = false;
            result                = default;
            result.distance       = float.MaxValue;
            ref var blob          = ref compountToCast.compoundColliderBlob.Value;
            var     compoundScale = new PhysicsScale { scale = compountToCast.scale, state = PhysicsScale.State.Uniform };
            for (int i = 0; i < blob.colliders.Length; i++)
            {
                var blobTransform  = blob.transforms[i];
                blobTransform.pos *= compountToCast.scale;
                var  start         = math.mul(castStart, blobTransform);
                bool newHit        = ColliderCast(ScaleCollider(blob.colliders[i], compoundScale),
                                                  start, start.pos + (castEnd - castStart.pos),
                                                  targetCompound,
                                                  targetCompoundTransform,
                                                  out var newResult);

                newResult.subColliderIndexOnCaster  = i;
                newHit                             &= newResult.distance < result.distance;
                hit                                |= newHit;
                result                              = newHit ? newResult : result;
            }
            return hit;
        }
        #endregion

        #region ConservativeAdvancement
        internal interface ConservativeAdvancementStepper
        {
            bool DoDistanceBetween(RigidTransform casterTransform, RigidTransform targetTransform, float maxDistance, out ColliderDistanceResult result);
        }

        struct CapsuleBoxStepper : ConservativeAdvancementStepper
        {
            public CapsuleCollider capsuleCaster;
            public BoxCollider     boxTarget;

            public bool DoDistanceBetween(RigidTransform casterTransform, RigidTransform targetTransform, float maxDistance, out ColliderDistanceResult result)
            {
                return DistanceBetween(capsuleCaster, casterTransform, boxTarget, targetTransform, maxDistance, out result);
            }
        }

        struct BoxCapsuleStepper : ConservativeAdvancementStepper
        {
            public BoxCollider     boxCaster;
            public CapsuleCollider capsuleTarget;

            public bool DoDistanceBetween(RigidTransform casterTransform, RigidTransform targetTransform, float maxDistance, out ColliderDistanceResult result)
            {
                return DistanceBetween(boxCaster, casterTransform, capsuleTarget, targetTransform, maxDistance, out result);
            }
        }

        struct BoxBoxStepper : ConservativeAdvancementStepper
        {
            public BoxCollider boxCaster;
            public BoxCollider boxTarget;

            public bool DoDistanceBetween(RigidTransform casterTransform, RigidTransform targetTransform, float maxDistance, out ColliderDistanceResult result)
            {
                return DistanceBetween(boxCaster, casterTransform, boxTarget, targetTransform, maxDistance, out result);
            }
        }

        static bool ConvexConservativeAdvancementCast<T>(T stepper, RigidTransform castStart, float3 castEnd, RigidTransform targetTransform,
                                                         out ColliderCastResult result) where T : struct, ConservativeAdvancementStepper
        {
            // This is when we assume that tunneling is no longer possible and should switch to bilateral advancement.
            // Todo: This may require tuning.
            const float bilateralThreshold = 1e-3f;

            // The desired precision for bilateral advancement.
            // Todo: This may require tuning.
            const float refinementThreshold = 1e-6f;

            // How many iterations to perform of bilateral advancement refinement if the desired precision can't be met.
            // Todo: This may require tuning.
            const int bilateralMaxIterations = 10;

            float castDistance     = mathex.getLengthAndNormal(castEnd - castStart.pos, out float3 castDirection);
            var   runningTransform = castStart;
            var   distanceTraveled = 0f;

            while (distanceTraveled <= castDistance)
            {
                stepper.DoDistanceBetween(runningTransform, targetTransform, float.MaxValue, out var distanceResult);

                if (distanceResult.distance <= bilateralThreshold)
                {
                    float bestDistanceTraveled = distanceTraveled;
                    var   bestDistanceResult   = distanceResult;
                    int   iterations           = bilateralMaxIterations;
                    for (; iterations >= 0 && distanceResult.distance > refinementThreshold; iterations--)
                    {
                        var maxTransform  = runningTransform;
                        maxTransform.pos += castDirection * distanceResult.distance;
                        bool progress     = stepper.DoDistanceBetween(maxTransform, targetTransform, distanceResult.distance, out var maxResult);
                        if (!progress)
                        {
                            // Advance again
                            // Unity Physics uses the normal of one of the colliders, which I think may be a bug.
                            // I'm using the actual hitpoint difference vector
                            float3 hitAToB = distanceResult.hitpointB - distanceResult.hitpointA;
                            float  dot     = math.dot(castDirection, hitAToB);
                            if (dot <= 0f)
                            {
                                // Our colliders are moving away from each other.
                                result = default;
                                return false;
                            }

                            // Advance by the projection of the distance onto the cast vector
                            distanceTraveled     += dot;
                            runningTransform.pos += castDirection * dot;

                            stepper.DoDistanceBetween(runningTransform, targetTransform, float.MaxValue, out distanceResult);
                        }
                        else if (maxResult.distance > 0f)
                        {
                            // That wasn't aggressive enough.
                            runningTransform      = maxTransform;
                            distanceTraveled     += distanceResult.distance;
                            bestDistanceTraveled  = distanceTraveled;
                            bestDistanceResult    = maxResult;
                        }
                        else
                        {
                            // We found penetration.
                            var minTransform            = runningTransform;
                            var minDistanceTraveled     = distanceTraveled;
                            var positiveDistanceBetween = distanceResult.distance;
                            var maxDistanceTraveled     = distanceTraveled + distanceResult.distance;
                            var negativeDistanceBetween = maxResult.distance;

                            if (math.abs(maxResult.distance) < bestDistanceResult.distance)
                                bestDistanceResult = maxResult;

                            //Source: https://www.youtube.com/watch?v=7_nKOET6zwI
                            for (; iterations >= 0 && minDistanceTraveled > refinementThreshold; iterations--)
                            {
                                // False position step
                                var factor            = math.unlerp(positiveDistanceBetween, negativeDistanceBetween, 0f);
                                var newTravelDistance = math.lerp(minDistanceTraveled, maxDistanceTraveled, factor);
                                if (newTravelDistance > minDistanceTraveled && newTravelDistance < maxDistanceTraveled)
                                {
                                    runningTransform.pos = minTransform.pos + castDirection * (newTravelDistance - minDistanceTraveled);

                                    if (stepper.DoDistanceBetween(runningTransform, targetTransform, positiveDistanceBetween, out var newDistanceResult))
                                    {
                                        if (math.abs(newDistanceResult.distance) < math.abs(bestDistanceResult.distance))
                                        {
                                            bestDistanceResult   = newDistanceResult;
                                            bestDistanceTraveled = newTravelDistance;
                                        }

                                        if (newDistanceResult.distance >= 0f)
                                        {
                                            minDistanceTraveled     = newTravelDistance;
                                            minTransform            = runningTransform;
                                            positiveDistanceBetween = newDistanceResult.distance;
                                        }
                                        else if (newDistanceResult.distance > negativeDistanceBetween)
                                        {
                                            maxDistanceTraveled     = newTravelDistance;
                                            maxTransform            = runningTransform;
                                            negativeDistanceBetween = newDistanceResult.distance;
                                        }
                                    }
                                }

                                // Bisect step
                                newTravelDistance    = (minDistanceTraveled + maxDistanceTraveled) / 2f;
                                runningTransform.pos = minTransform.pos + castDirection * (newTravelDistance - minDistanceTraveled);
                                {
                                    if (stepper.DoDistanceBetween(runningTransform, targetTransform, positiveDistanceBetween, out var newDistanceResult))
                                    {
                                        if (math.abs(newDistanceResult.distance) < math.abs(bestDistanceResult.distance))
                                        {
                                            bestDistanceResult   = newDistanceResult;
                                            bestDistanceTraveled = newTravelDistance;
                                        }
                                        if (newDistanceResult.distance >= 0f)
                                        {
                                            minDistanceTraveled     = newTravelDistance;
                                            minTransform            = runningTransform;
                                            positiveDistanceBetween = newDistanceResult.distance;
                                        }
                                        else if (newDistanceResult.distance > negativeDistanceBetween)
                                        {
                                            maxDistanceTraveled     = newTravelDistance;
                                            maxTransform            = runningTransform;
                                            negativeDistanceBetween = newDistanceResult.distance;
                                        }
                                    }
                                }
                            }
                        }
                    }

                    result = new ColliderCastResult
                    {
                        hitpointOnCaster         = bestDistanceResult.hitpointA,
                        hitpointOnTarget         = bestDistanceResult.hitpointB,
                        normalOnSweep            = bestDistanceResult.normalA,
                        normalOnTarget           = bestDistanceResult.normalB,
                        subColliderIndexOnCaster = bestDistanceResult.subColliderIndexA,
                        subColliderIndexOnTarget = bestDistanceResult.subColliderIndexB,
                        distance                 = bestDistanceTraveled
                    };
                    return true;
                }

                {
                    // Unity Physics uses the normal of one of the colliders, which I think may be a bug.
                    // I'm using the actual hitpoint difference vector
                    float3 hitAToB = distanceResult.hitpointB - distanceResult.hitpointA;
                    float  dot     = math.dot(castDirection, hitAToB);
                    if (dot <= 0f)
                    {
                        // Our colliders are moving away from each other.
                        result = default;
                        return false;
                    }

                    // Advance by the projection of the distance onto the cast vector
                    distanceTraveled     += dot;
                    runningTransform.pos += castDirection * dot;
                }
            }
            result = default;
            return false;
        }
        #endregion
    }
}

