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
            bool hit                       = SpatialInternal.RaycastRoundedBox(ray, targetBox, sphereToCast.radius, out var fraction, out var normal);
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
            bool hit        = false;
            result          = default;
            result.distance = float.MaxValue;
            if (DistanceBetween(sphereToCast, castStart, targetCompound, targetCompoundTransform, 0f, out _))
            {
                return false;
            }

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
            bool hit          = Raycast(ray, cso, castStart, out var raycastResult);
            if (hit)
            {
                var hitTransform  = castStart;
                hitTransform.pos -= raycastResult.position - start;
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
            var csoRadius           = capsuleToCast.radius + targetCapsule.radius;
            var casterAInWorldSpace = math.transform(castStart, capsuleToCast.pointA);
            var casterBInWorldSpace = math.transform(castStart, capsuleToCast.pointB);
            var targetAInWorldSpace = math.transform(targetCapsuleTransform, targetCapsule.pointA);
            var targetBInWorldSpace = math.transform(targetCapsuleTransform, targetCapsule.pointB);
            var cso                 = new simdFloat3(casterAInWorldSpace - targetAInWorldSpace,
                                                     casterAInWorldSpace - targetBInWorldSpace,
                                                     casterBInWorldSpace - targetBInWorldSpace,
                                                     casterBInWorldSpace - targetAInWorldSpace);
            var  ray = new Ray(0f, castStart.pos - castEnd);
            bool hit = SpatialInternal.RaycastRoundedQuad(ray, cso, csoRadius, out float fraction, out float3 normal);
            if (hit)
            {
                var hitTransform = castStart;
                hitTransform.pos = math.lerp(castStart.pos, castEnd, fraction);
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
            if (DistanceBetween(capsuleToCast, castStart, targetBox, targetBoxTransform, 0f, out _))
            {
                result = default;
                return false;
            }

            var targetBoxTransformInverse = math.inverse(targetBoxTransform);
            var casterInTargetSpace       = math.mul(targetBoxTransformInverse, castStart);
            var inflatedBoxAabb           = new Aabb(targetBox.center - targetBox.halfSize - capsuleToCast.radius, targetBox.center + targetBox.halfSize + capsuleToCast.radius);

            var  startA     = math.transform(casterInTargetSpace, capsuleToCast.pointA);
            var  rayA       = new Ray(startA, startA + math.rotate(targetBoxTransformInverse, castEnd - castStart.pos));
            bool hitA       = SpatialInternal.RaycastAabb(rayA, inflatedBoxAabb, out var fractionA);
            var  hitpointA  = math.lerp(rayA.start, rayA.end, fractionA) - targetBox.center;
            hitA           &= math.countbits(math.bitmask(new bool4(math.abs(hitpointA) > targetBox.halfSize, false))) == 1;
            fractionA       = math.select(2f, fractionA, hitA);
            var  startB     = math.transform(casterInTargetSpace, capsuleToCast.pointB);
            var  rayB       = new Ray(startB, startB + math.rotate(targetBoxTransformInverse, castEnd - castStart.pos));
            bool hitB       = SpatialInternal.RaycastAabb(rayB, inflatedBoxAabb, out var fractionB);
            var  hitpointB  = math.lerp(rayB.start, rayB.end, fractionB) - targetBox.center;
            hitB           &= math.countbits(math.bitmask(new bool4(math.abs(hitpointB) > targetBox.halfSize, false))) == 1;
            fractionB       = math.select(2f, fractionB, hitB);

            var        ray = new Ray(0f, math.rotate(targetBoxTransformInverse, castStart.pos - castEnd));
            bool4      hitX;
            float4     fractionsX;
            float3     targetA = targetBox.center - targetBox.halfSize;
            float3     targetB = targetBox.center + new float3(targetBox.halfSize.x, -targetBox.halfSize.y, -targetBox.halfSize.z);
            simdFloat3 cso     = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitX.x             = SpatialInternal.RaycastRoundedQuad(ray, cso, capsuleToCast.radius, out fractionsX.x, out _);
            targetA            = targetBox.center + new float3(-targetBox.halfSize.x, -targetBox.halfSize.y, targetBox.halfSize.z);
            targetB            = targetBox.center + new float3(targetBox.halfSize.x, -targetBox.halfSize.y, targetBox.halfSize.z);
            cso                = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitX.y             = SpatialInternal.RaycastRoundedQuad(ray, cso, capsuleToCast.radius, out fractionsX.y, out _);
            targetA            = targetBox.center + new float3(-targetBox.halfSize.x, targetBox.halfSize.y, -targetBox.halfSize.z);
            targetB            = targetBox.center + new float3( targetBox.halfSize.x, targetBox.halfSize.y, -targetBox.halfSize.z);
            cso                = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitX.z             = SpatialInternal.RaycastRoundedQuad(ray, cso, capsuleToCast.radius, out fractionsX.z, out _);
            targetA            = targetBox.center + new float3(-targetBox.halfSize.x, targetBox.halfSize.y, targetBox.halfSize.z);
            targetB            = targetBox.center + targetBox.halfSize;
            cso                = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitX.w             = SpatialInternal.RaycastRoundedQuad(ray, cso, capsuleToCast.radius, out fractionsX.w, out _);
            fractionsX         = math.select(2f, fractionsX, hitX);

            bool4  hitY;
            float4 fractionsY;
            targetA    = targetBox.center - targetBox.halfSize;
            targetB    = targetBox.center + new float3(-targetBox.halfSize.x, targetBox.halfSize.y, -targetBox.halfSize.z);
            cso        = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitY.x     = SpatialInternal.RaycastRoundedQuad(ray, cso, capsuleToCast.radius, out fractionsY.x, out _);
            targetA    = targetBox.center + new float3(-targetBox.halfSize.x, -targetBox.halfSize.y, targetBox.halfSize.z);
            targetB    = targetBox.center + new float3(-targetBox.halfSize.x, targetBox.halfSize.y, targetBox.halfSize.z);
            cso        = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitY.y     = SpatialInternal.RaycastRoundedQuad(ray, cso, capsuleToCast.radius, out fractionsY.y, out _);
            targetA    = targetBox.center + new float3(targetBox.halfSize.x, -targetBox.halfSize.y, -targetBox.halfSize.z);
            targetB    = targetBox.center + new float3(targetBox.halfSize.x, targetBox.halfSize.y, -targetBox.halfSize.z);
            cso        = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitY.z     = SpatialInternal.RaycastRoundedQuad(ray, cso, capsuleToCast.radius, out fractionsY.z, out _);
            targetA    = targetBox.center + new float3(targetBox.halfSize.x, -targetBox.halfSize.y, targetBox.halfSize.z);
            targetB    = targetBox.center + targetBox.halfSize;
            cso        = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitY.w     = SpatialInternal.RaycastRoundedQuad(ray, cso, capsuleToCast.radius, out fractionsY.w, out _);
            fractionsY = math.select(2f, fractionsY, hitY);

            bool4  hitZ;
            float4 fractionsZ;
            targetA    = targetBox.center - targetBox.halfSize;
            targetB    = targetBox.center + new float3(-targetBox.halfSize.x, -targetBox.halfSize.y, targetBox.halfSize.z);
            cso        = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitZ.x     = SpatialInternal.RaycastRoundedQuad(ray, cso, capsuleToCast.radius, out fractionsZ.x, out _);
            targetA    = targetBox.center + new float3(-targetBox.halfSize.x, targetBox.halfSize.y, -targetBox.halfSize.z);
            targetB    = targetBox.center + new float3(-targetBox.halfSize.x, targetBox.halfSize.y, targetBox.halfSize.z);
            cso        = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitZ.y     = SpatialInternal.RaycastRoundedQuad(ray, cso, capsuleToCast.radius, out fractionsZ.y, out _);
            targetA    = targetBox.center + new float3(targetBox.halfSize.x, -targetBox.halfSize.y, -targetBox.halfSize.z);
            targetB    = targetBox.center + new float3(targetBox.halfSize.x, -targetBox.halfSize.y, targetBox.halfSize.z);
            cso        = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitZ.z     = SpatialInternal.RaycastRoundedQuad(ray, cso, capsuleToCast.radius, out fractionsZ.z, out _);
            targetA    = targetBox.center + new float3(targetBox.halfSize.x, targetBox.halfSize.y, -targetBox.halfSize.z);
            targetB    = targetBox.center + targetBox.halfSize;
            cso        = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitZ.w     = SpatialInternal.RaycastRoundedQuad(ray, cso, capsuleToCast.radius, out fractionsZ.w, out _);
            fractionsZ = math.select(2f, fractionsZ, hitZ);

            bool  hit      = math.any(hitX | hitY | hitZ) | hitA | hitB;
            float fraction = math.min(math.min(fractionA, fractionB), math.cmin(math.min(fractionsX, math.min(fractionsY, fractionsZ))));
            if (hit)
            {
                var hitTransform = castStart;
                hitTransform.pos = math.lerp(castStart.pos, castEnd, fraction);
                DistanceBetween(capsuleToCast, hitTransform, targetBox, targetBoxTransform, 1f, out var distanceResult);
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
                                        CompoundCollider targetCompound,
                                        RigidTransform targetCompoundTransform,
                                        out ColliderCastResult result)
        {
            bool hit        = false;
            result          = default;
            result.distance = float.MaxValue;
            if (DistanceBetween(capsuleToCast, castStart, targetCompound, targetCompoundTransform, 0f, out _))
            {
                return false;
            }
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
            bool hit                = SpatialInternal.RaycastRoundedBox(ray, boxToCast, targetSphere.radius, out var fraction, out _);
            if (hit)
            {
                var hitTransform = castStart;
                hitTransform.pos = math.lerp(castStart.pos, castEnd, fraction);
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
            if (DistanceBetween(boxToCast, castStart, targetCapsule, targetCapsuleTransform, 0f, out _))
            {
                result = default;
                return false;
            }

            var castStartInverse    = math.inverse(castStart);
            var targetInCasterSpace = math.mul(castStartInverse, targetCapsuleTransform);
            var inflatedBoxAabb     = new Aabb(boxToCast.center - boxToCast.halfSize - targetCapsule.radius, boxToCast.center + boxToCast.halfSize + targetCapsule.radius);

            var  targetA    = math.transform(targetInCasterSpace, targetCapsule.pointA);
            var  rayA       = new Ray(targetA, targetA - math.rotate(castStartInverse, castEnd - castStart.pos));
            bool hitA       = SpatialInternal.RaycastAabb(rayA, inflatedBoxAabb, out var fractionA);
            var  hitpointA  = math.lerp(rayA.start, rayA.end, fractionA) - boxToCast.center;
            hitA           &= math.countbits(math.bitmask(new bool4(math.abs(hitpointA) > boxToCast.halfSize, false))) == 1;
            fractionA       = math.select(2f, fractionA, hitA);
            var  targetB    = math.transform(targetInCasterSpace, targetCapsule.pointB);
            var  rayB       = new Ray(targetB, targetB - math.rotate(castStartInverse, castEnd - castStart.pos));
            bool hitB       = SpatialInternal.RaycastAabb(rayB, inflatedBoxAabb, out var fractionB);
            var  hitpointB  = math.lerp(rayB.start, rayB.end, fractionB) - boxToCast.center;
            hitB           &= math.countbits(math.bitmask(new bool4(math.abs(hitpointB) > boxToCast.halfSize, false))) == 1;
            fractionB       = math.select(2f, fractionB, hitB);

            var        ray = new Ray(0f, math.rotate(castStartInverse, castStart.pos - castEnd));
            bool4      hitX;
            float4     fractionsX;
            float3     startA = boxToCast.center - boxToCast.halfSize;
            float3     startB = boxToCast.center + new float3(boxToCast.halfSize.x, -boxToCast.halfSize.y, -boxToCast.halfSize.z);
            simdFloat3 cso    = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitX.x            = SpatialInternal.RaycastRoundedQuad(ray, cso, targetCapsule.radius, out fractionsX.x, out _);
            startA            = boxToCast.center + new float3(-boxToCast.halfSize.x, -boxToCast.halfSize.y, boxToCast.halfSize.z);
            startB            = boxToCast.center + new float3(boxToCast.halfSize.x, -boxToCast.halfSize.y, boxToCast.halfSize.z);
            cso               = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitX.y            = SpatialInternal.RaycastRoundedQuad(ray, cso, targetCapsule.radius, out fractionsX.y, out _);
            startA            = boxToCast.center + new float3(-boxToCast.halfSize.x, boxToCast.halfSize.y, -boxToCast.halfSize.z);
            startB            = boxToCast.center + new float3(boxToCast.halfSize.x, boxToCast.halfSize.y, -boxToCast.halfSize.z);
            cso               = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitX.z            = SpatialInternal.RaycastRoundedQuad(ray, cso, targetCapsule.radius, out fractionsX.z, out _);
            startA            = boxToCast.center + new float3(-boxToCast.halfSize.x, boxToCast.halfSize.y, boxToCast.halfSize.z);
            startB            = boxToCast.center + boxToCast.halfSize;
            cso               = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitX.w            = SpatialInternal.RaycastRoundedQuad(ray, cso, targetCapsule.radius, out fractionsX.w, out _);
            fractionsX        = math.select(2f, fractionsX, hitX);

            bool4  hitY;
            float4 fractionsY;
            startA     = boxToCast.center - boxToCast.halfSize;
            startB     = boxToCast.center + new float3(-boxToCast.halfSize.x, boxToCast.halfSize.y, -boxToCast.halfSize.z);
            cso        = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitY.x     = SpatialInternal.RaycastRoundedQuad(ray, cso, targetCapsule.radius, out fractionsY.x, out _);
            startA     = boxToCast.center + new float3(-boxToCast.halfSize.x, -boxToCast.halfSize.y, boxToCast.halfSize.z);
            startB     = boxToCast.center + new float3(-boxToCast.halfSize.x, boxToCast.halfSize.y, boxToCast.halfSize.z);
            cso        = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitY.y     = SpatialInternal.RaycastRoundedQuad(ray, cso, targetCapsule.radius, out fractionsY.y, out _);
            startA     = boxToCast.center + new float3(boxToCast.halfSize.x, -boxToCast.halfSize.y, -boxToCast.halfSize.z);
            startB     = boxToCast.center + new float3(boxToCast.halfSize.x, boxToCast.halfSize.y, -boxToCast.halfSize.z);
            cso        = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitY.z     = SpatialInternal.RaycastRoundedQuad(ray, cso, targetCapsule.radius, out fractionsY.z, out _);
            startA     = boxToCast.center + new float3(boxToCast.halfSize.x, -boxToCast.halfSize.y, boxToCast.halfSize.z);
            startB     = boxToCast.center + boxToCast.halfSize;
            cso        = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitY.w     = SpatialInternal.RaycastRoundedQuad(ray, cso, targetCapsule.radius, out fractionsY.w, out _);
            fractionsY = math.select(2f, fractionsY, hitY);

            bool4  hitZ;
            float4 fractionsZ;
            startA     = boxToCast.center - boxToCast.halfSize;
            startB     = boxToCast.center + new float3(-boxToCast.halfSize.x, -boxToCast.halfSize.y, boxToCast.halfSize.z);
            cso        = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitZ.x     = SpatialInternal.RaycastRoundedQuad(ray, cso, targetCapsule.radius, out fractionsZ.x, out _);
            startA     = boxToCast.center + new float3(-boxToCast.halfSize.x, boxToCast.halfSize.y, -boxToCast.halfSize.z);
            startB     = boxToCast.center + new float3(-boxToCast.halfSize.x, boxToCast.halfSize.y, boxToCast.halfSize.z);
            cso        = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitZ.y     = SpatialInternal.RaycastRoundedQuad(ray, cso, targetCapsule.radius, out fractionsZ.y, out _);
            startA     = boxToCast.center + new float3(boxToCast.halfSize.x, -boxToCast.halfSize.y, -boxToCast.halfSize.z);
            startB     = boxToCast.center + new float3(boxToCast.halfSize.x, -boxToCast.halfSize.y, boxToCast.halfSize.z);
            cso        = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitZ.z     = SpatialInternal.RaycastRoundedQuad(ray, cso, targetCapsule.radius, out fractionsZ.z, out _);
            startA     = boxToCast.center + new float3(boxToCast.halfSize.x, boxToCast.halfSize.y, -boxToCast.halfSize.z);
            startB     = boxToCast.center + boxToCast.halfSize;
            cso        = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitZ.w     = SpatialInternal.RaycastRoundedQuad(ray, cso, targetCapsule.radius, out fractionsZ.w, out _);
            fractionsZ = math.select(2f, fractionsZ, hitZ);

            bool  hit      = math.any(hitX | hitY | hitZ) | hitA | hitB;
            float fraction = math.min(math.min(fractionA, fractionB), math.cmin(math.min(fractionsX, math.min(fractionsY, fractionsZ))));
            if (hit)
            {
                var hitTransform = castStart;
                hitTransform.pos = math.lerp(castStart.pos, castEnd, fraction);
                DistanceBetween(boxToCast, hitTransform, targetCapsule, targetCapsuleTransform, 1f, out var distanceResult);
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
                                        BoxCollider targetBox,
                                        RigidTransform targetBoxTransform,
                                        out ColliderCastResult result)
        {
            var  castStartInverse             = math.inverse(castStart);
            var  targetInCasterSpaceTransform = math.mul(castStartInverse, targetBoxTransform);
            var  castDirection                = math.rotate(castStartInverse, castEnd - castStart.pos);
            var  normalizedCastDirection      = math.normalize(castDirection);
            bool hit                          = SpatialInternal.MprCastNoRoundness(boxToCast,
                                                                                   targetBox,
                                                                                   targetInCasterSpaceTransform,
                                                                                   normalizedCastDirection,
                                                                                   math.length(castDirection),
                                                                                   out float distanceOfImpact,
                                                                                   out bool somethingWentWrong);
            if (!hit || distanceOfImpact <= 0f)
            {
                result = default;
                return false;
            }

            var castHitOffset       = math.rotate(castStart, normalizedCastDirection * distanceOfImpact);
            var casterHitTransform  = castStart;
            casterHitTransform.pos += castHitOffset;
            DistanceBetween(boxToCast, casterHitTransform, targetBox, targetBoxTransform, float.MaxValue, out var distanceResult);

            result = new ColliderCastResult
            {
                distance                 = distanceOfImpact,
                hitpointOnCaster         = distanceResult.hitpointA,
                hitpointOnTarget         = distanceResult.hitpointB,
                normalOnSweep            = distanceResult.normalA,
                normalOnTarget           = distanceResult.normalB,
                subColliderIndexOnCaster = 0,
                subColliderIndexOnTarget = 0
            };

#if PSYSHOCK_DEBUG
            if (math.abs(distanceResult.distance) > SpatialInternal.k_boxBoxAccuracy || somethingWentWrong)
            {
                UnityEngine.Debug.LogError(
                    $"MPR generated a distance error of {distanceResult.distance} and somethingWentWrong = { somethingWentWrong }. If you are seeing this error, please report a bug! The following logs are from rerunning the algorithm with detailed info.");
                UnityEngine.Debug.Log($"casterHitTransform.pos: {casterHitTransform.pos}, targetBoxTransform.pos: {targetBoxTransform.pos}, distance: {distanceResult.distance}");
                SpatialInternal.MprCastNoRoundnessDebug(boxToCast,
                                                        targetBox,
                                                        targetInCasterSpaceTransform,
                                                        normalizedCastDirection,
                                                        math.length(castDirection),
                                                        out _,
                                                        out _);
                DistanceBetweenDebug(boxToCast, casterHitTransform, targetBox, targetBoxTransform, float.MaxValue, out _);
                var gjkDistance = SpatialInternal.DoGjkEpa(boxToCast, targetBox, math.mul(math.inverse(casterHitTransform), targetBoxTransform));
                UnityEngine.Debug.Log($"Distance evaluated using gjk (if this is less, than the bug is in DistanceBetween): {gjkDistance.distance}");

                simdFloat3 bTopPoints    = default;
                simdFloat3 bBottomPoints = default;
                bTopPoints.x    = math.select(-boxToCast.halfSize.x, boxToCast.halfSize.x, new bool4(true, true, false, false));
                bBottomPoints.x = bTopPoints.x;
                bBottomPoints.y = -boxToCast.halfSize.y;
                bTopPoints.y    = boxToCast.halfSize.y;
                bTopPoints.z    = math.select(-boxToCast.halfSize.z, boxToCast.halfSize.z, new bool4(true, false, true, false));
                bBottomPoints.z = bTopPoints.z;
                bTopPoints     += boxToCast.center;
                bBottomPoints  += boxToCast.center;
                bTopPoints      = simd.transform(casterHitTransform, bTopPoints);
                bBottomPoints   = simd.transform(casterHitTransform, bBottomPoints);

                UnityEngine.Debug.DrawLine(bTopPoints.a,    bTopPoints.b,    UnityEngine.Color.red, 10f);
                UnityEngine.Debug.DrawLine(bTopPoints.b,    bTopPoints.d,    UnityEngine.Color.red, 10f);
                UnityEngine.Debug.DrawLine(bTopPoints.c,    bTopPoints.a,    UnityEngine.Color.red, 10f);
                UnityEngine.Debug.DrawLine(bTopPoints.d,    bTopPoints.c,    UnityEngine.Color.red, 10f);

                UnityEngine.Debug.DrawLine(bBottomPoints.a, bBottomPoints.b, UnityEngine.Color.red, 10f);
                UnityEngine.Debug.DrawLine(bBottomPoints.b, bBottomPoints.d, UnityEngine.Color.red, 10f);
                UnityEngine.Debug.DrawLine(bBottomPoints.c, bBottomPoints.a, UnityEngine.Color.red, 10f);
                UnityEngine.Debug.DrawLine(bBottomPoints.d, bBottomPoints.c, UnityEngine.Color.red, 10f);

                UnityEngine.Debug.DrawLine(bTopPoints.b,    bBottomPoints.b, UnityEngine.Color.red, 10f);
                UnityEngine.Debug.DrawLine(bTopPoints.c,    bBottomPoints.c, UnityEngine.Color.red, 10f);
                UnityEngine.Debug.DrawLine(bTopPoints.d,    bBottomPoints.d, UnityEngine.Color.red, 10f);
                UnityEngine.Debug.DrawLine(bTopPoints.a,    bBottomPoints.a, UnityEngine.Color.red, 10f);

                UnityEngine.Debug.DrawLine(bTopPoints.a,    float3.zero,     UnityEngine.Color.red);
            }
#endif
            return true;
        }

        public static bool ColliderCast(BoxCollider boxToCast,
                                        RigidTransform castStart,
                                        float3 castEnd,
                                        CompoundCollider targetCompound,
                                        RigidTransform targetCompoundTransform,
                                        out ColliderCastResult result)
        {
            bool hit        = false;
            result          = default;
            result.distance = float.MaxValue;
            if (DistanceBetween(boxToCast, castStart, targetCompound, targetCompoundTransform, 0f, out _))
            {
                return false;
            }
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
        public static bool ColliderCast(CompoundCollider compoundToCast,
                                        RigidTransform castStart,
                                        float3 castEnd,
                                        SphereCollider targetSphere,
                                        RigidTransform targetSphereTransform,
                                        out ColliderCastResult result)
        {
            bool hit        = false;
            result          = default;
            result.distance = float.MaxValue;
            if (DistanceBetween(compoundToCast, castStart, targetSphere, targetSphereTransform, 0f, out _))
            {
                return false;
            }
            ref var blob          = ref compoundToCast.compoundColliderBlob.Value;
            var     compoundScale = new PhysicsScale { scale = compoundToCast.scale, state = PhysicsScale.State.Uniform };
            for (int i = 0; i < blob.colliders.Length; i++)
            {
                var blobTransform  = blob.transforms[i];
                blobTransform.pos *= compoundToCast.scale;
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

        public static bool ColliderCast(CompoundCollider compoundToCast,
                                        RigidTransform castStart,
                                        float3 castEnd,
                                        CapsuleCollider targetCapsule,
                                        RigidTransform targetCapsuleTransform,
                                        out ColliderCastResult result)
        {
            bool hit        = false;
            result          = default;
            result.distance = float.MaxValue;
            if (DistanceBetween(compoundToCast, castStart, targetCapsule, targetCapsuleTransform, 0f, out _))
            {
                return false;
            }
            ref var blob          = ref compoundToCast.compoundColliderBlob.Value;
            var     compoundScale = new PhysicsScale { scale = compoundToCast.scale, state = PhysicsScale.State.Uniform };
            for (int i = 0; i < blob.colliders.Length; i++)
            {
                var blobTransform  = blob.transforms[i];
                blobTransform.pos *= compoundToCast.scale;
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

        public static bool ColliderCast(CompoundCollider compoundToCast,
                                        RigidTransform castStart,
                                        float3 castEnd,
                                        BoxCollider targetBox,
                                        RigidTransform targetBoxTransform,
                                        out ColliderCastResult result)
        {
            bool hit        = false;
            result          = default;
            result.distance = float.MaxValue;
            if (DistanceBetween(compoundToCast, castStart, targetBox, targetBoxTransform, 0f, out _))
            {
                return false;
            }
            ref var blob          = ref compoundToCast.compoundColliderBlob.Value;
            var     compoundScale = new PhysicsScale { scale = compoundToCast.scale, state = PhysicsScale.State.Uniform };
            for (int i = 0; i < blob.colliders.Length; i++)
            {
                var blobTransform  = blob.transforms[i];
                blobTransform.pos *= compoundToCast.scale;
                var  start         = math.mul(castStart, blobTransform);
                bool newHit        = ColliderCast(ScaleCollider(blob.colliders[i], compoundScale),
                                                  start, start.pos + (castEnd - castStart.pos),
                                                  targetBox,
                                                  targetBoxTransform,
                                                  out var newResult);

                newResult.subColliderIndexOnCaster  = i;
                newHit                             &= newResult.distance < result.distance;
                hit                                |= newHit;
                result                              = newHit ? newResult : result;
            }
            return hit;
        }

        public static bool ColliderCast(CompoundCollider compoundToCast,
                                        RigidTransform castStart,
                                        float3 castEnd,
                                        CompoundCollider targetCompound,
                                        RigidTransform targetCompoundTransform,
                                        out ColliderCastResult result)
        {
            bool hit        = false;
            result          = default;
            result.distance = float.MaxValue;
            if (DistanceBetween(compoundToCast, castStart, targetCompound, targetCompoundTransform, 0f, out _))
            {
                return false;
            }
            ref var blob          = ref compoundToCast.compoundColliderBlob.Value;
            var     compoundScale = new PhysicsScale { scale = compoundToCast.scale, state = PhysicsScale.State.Uniform };
            for (int i = 0; i < blob.colliders.Length; i++)
            {
                var blobTransform  = blob.transforms[i];
                blobTransform.pos *= compoundToCast.scale;
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
    }
}

