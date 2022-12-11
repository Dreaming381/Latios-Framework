using System.Diagnostics;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    public static partial class Physics
    {
        #region Sphere
        public static bool ColliderCast(in SphereCollider sphereToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in SphereCollider targetSphere,
                                        in RigidTransform targetSphereTransform,
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
                DistanceBetween(in sphereToCast, in hitTransform, in targetSphere, in targetSphereTransform, 1f, out var distanceResult);
                result = new ColliderCastResult
                {
                    hitpointOnCaster         = distanceResult.hitpointA,
                    hitpointOnTarget         = distanceResult.hitpointB,
                    normalOnCaster           = distanceResult.normalA,
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

        public static bool ColliderCast(in SphereCollider sphereToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in CapsuleCollider targetCapsule,
                                        in RigidTransform targetCapsuleTransform,
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
                DistanceBetween(in sphereToCast, in hitTransform, in targetCapsule, in targetCapsuleTransform, 1f, out var distanceResult);
                result = new ColliderCastResult
                {
                    hitpointOnCaster         = distanceResult.hitpointA,
                    hitpointOnTarget         = distanceResult.hitpointB,
                    normalOnCaster           = distanceResult.normalA,
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

        public static bool ColliderCast(in SphereCollider sphereToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in BoxCollider targetBox,
                                        in RigidTransform targetBoxTransform,
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
                DistanceBetween(in sphereToCast, in hitTransform, in targetBox, in targetBoxTransform, 1f, out var distanceResult);
                result = new ColliderCastResult
                {
                    hitpointOnCaster         = distanceResult.hitpointA,
                    hitpointOnTarget         = distanceResult.hitpointB,
                    normalOnCaster           = distanceResult.normalA,
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

        public static bool ColliderCast(in SphereCollider sphereToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in TriangleCollider targetTriangle,
                                        in RigidTransform targetTriangleTransform,
                                        out ColliderCastResult result)
        {
            var  targetTriangleTransformInverse = math.inverse(targetTriangleTransform);
            var  casterInTargetSpace            = math.mul(targetTriangleTransformInverse, castStart);
            var  start                          = math.transform(casterInTargetSpace, sphereToCast.center);
            var  ray                            = new Ray(start, start + math.rotate(targetTriangleTransformInverse, castEnd - castStart.pos));
            bool hit                            =
                SpatialInternal.RaycastRoundedTriangle(ray,
                                                       targetTriangle.AsSimdFloat3(),
                                                       sphereToCast.radius,
                                                       out var fraction,
                                                       out _);
            if (hit)
            {
                var hitTransform = castStart;
                hitTransform.pos = math.lerp(castStart.pos, castEnd, fraction);
                DistanceBetween(in sphereToCast, in hitTransform, in targetTriangle, in targetTriangleTransform, 1f, out var distanceResult);
                result = new ColliderCastResult
                {
                    hitpointOnCaster         = distanceResult.hitpointA,
                    hitpointOnTarget         = distanceResult.hitpointB,
                    normalOnCaster           = distanceResult.normalA,
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

        public static bool ColliderCast(in SphereCollider sphereToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in ConvexCollider targetConvex,
                                        in RigidTransform targetConvexTransform,
                                        out ColliderCastResult result)
        {
            var  targetConvexTransformInverse = math.inverse(targetConvexTransform);
            var  casterInTargetSpace          = math.mul(targetConvexTransformInverse, castStart);
            var  start                        = math.transform(casterInTargetSpace, sphereToCast.center);
            var  ray                          = new Ray(start, start + math.rotate(targetConvexTransformInverse, castEnd - castStart.pos));
            bool hit                          =
                SpatialInternal.RaycastRoundedConvex(ray,
                                                     targetConvex,
                                                     sphereToCast.radius,
                                                     out var fraction);
            if (hit)
            {
                var hitTransform = castStart;
                hitTransform.pos = math.lerp(castStart.pos, castEnd, fraction);
                DistanceBetween(in sphereToCast, in hitTransform, in targetConvex, in targetConvexTransform, 1f, out var distanceResult);
                result = new ColliderCastResult
                {
                    hitpointOnCaster         = distanceResult.hitpointA,
                    hitpointOnTarget         = distanceResult.hitpointB,
                    normalOnCaster           = distanceResult.normalA,
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

        public static bool ColliderCast(in SphereCollider sphereToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in CompoundCollider targetCompound,
                                        in RigidTransform targetCompoundTransform,
                                        out ColliderCastResult result)
        {
            bool hit        = false;
            result          = default;
            result.distance = float.MaxValue;
            if (DistanceBetween(in sphereToCast, in castStart, in targetCompound, in targetCompoundTransform, 0f, out _))
            {
                return false;
            }

            ref var blob          = ref targetCompound.compoundColliderBlob.Value;
            var     compoundScale = new PhysicsScale { scale = targetCompound.scale, state = PhysicsScale.State.Uniform };
            for (int i = 0; i < blob.colliders.Length; i++)
            {
                var blobTransform  = blob.transforms[i];
                blobTransform.pos *= targetCompound.scale;
                bool newHit        = ColliderCast(in sphereToCast, in castStart, castEnd, ScaleCollider(in blob.colliders[i], compoundScale),
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
        public static bool ColliderCast(in CapsuleCollider capsuleToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in SphereCollider targetSphere,
                                        in RigidTransform targetSphereTransform,
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
                DistanceBetween(in capsuleToCast, in hitTransform, in targetSphere, in targetSphereTransform, 1f, out var distanceResult);
                result = new ColliderCastResult
                {
                    hitpointOnCaster         = distanceResult.hitpointA,
                    hitpointOnTarget         = distanceResult.hitpointB,
                    normalOnCaster           = distanceResult.normalA,
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

        public static bool ColliderCast(in CapsuleCollider capsuleToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in CapsuleCollider targetCapsule,
                                        in RigidTransform targetCapsuleTransform,
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
                DistanceBetween(in capsuleToCast, in hitTransform, in targetCapsule, in targetCapsuleTransform, 1f, out var distanceResult);
                result = new ColliderCastResult
                {
                    hitpointOnCaster         = distanceResult.hitpointA,
                    hitpointOnTarget         = distanceResult.hitpointB,
                    normalOnCaster           = distanceResult.normalA,
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

        public static bool ColliderCast(in CapsuleCollider capsuleToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in BoxCollider targetBox,
                                        in RigidTransform targetBoxTransform,
                                        out ColliderCastResult result)
        {
            if (DistanceBetween(in capsuleToCast, in castStart, in targetBox, in targetBoxTransform, 0f, out _))
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
                DistanceBetween(in capsuleToCast, in hitTransform, in targetBox, in targetBoxTransform, 1f, out var distanceResult);
                result = new ColliderCastResult
                {
                    hitpointOnCaster         = distanceResult.hitpointA,
                    hitpointOnTarget         = distanceResult.hitpointB,
                    normalOnCaster           = distanceResult.normalA,
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

        public static bool ColliderCast(in CapsuleCollider capsuleToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in TriangleCollider targetTriangle,
                                        in RigidTransform targetTriangleTransform,
                                        out ColliderCastResult result)
        {
            if (DistanceBetween(in capsuleToCast, in castStart, in targetTriangle, in targetTriangleTransform, 0f, out _))
            {
                result = default;
                return false;
            }

            var targetTriangleTransformInverse = math.inverse(targetTriangleTransform);
            var casterInTargetSpace            = math.mul(targetTriangleTransformInverse, castStart);
            var triPoints                      = targetTriangle.AsSimdFloat3();

            var  startA = math.transform(casterInTargetSpace, capsuleToCast.pointA);
            var  rayA   = new Ray(startA, startA + math.rotate(targetTriangleTransformInverse, castEnd - castStart.pos));
            bool hitA   = SpatialInternal.RaycastRoundedTriangle(rayA, triPoints, capsuleToCast.radius, out var fractionA, out _);
            fractionA   = math.select(2f, fractionA, hitA);
            var  startB = math.transform(casterInTargetSpace, capsuleToCast.pointB);
            var  rayB   = new Ray(startB, startB + math.rotate(targetTriangleTransformInverse, castEnd - castStart.pos));
            bool hitB   = SpatialInternal.RaycastRoundedTriangle(rayB, triPoints, capsuleToCast.radius, out var fractionB, out _);
            fractionB   = math.select(2f, fractionB, hitB);

            var        ray = new Ray(0f, math.rotate(targetTriangleTransformInverse, castStart.pos - castEnd));
            bool3      hitEdge;
            float3     fractionsEdge;
            simdFloat3 startSimd = new simdFloat3(startA, startA, startB, startB);
            simdFloat3 cso       = startSimd - triPoints.abba;
            hitEdge.x            = SpatialInternal.RaycastRoundedQuad(ray, cso, capsuleToCast.radius, out fractionsEdge.x, out _);
            cso                  = startSimd - triPoints.bccb;
            hitEdge.y            = SpatialInternal.RaycastRoundedQuad(ray, cso, capsuleToCast.radius, out fractionsEdge.y, out _);
            cso                  = startSimd - triPoints.caac;
            hitEdge.z            = SpatialInternal.RaycastRoundedQuad(ray, cso, capsuleToCast.radius, out fractionsEdge.z, out _);
            fractionsEdge        = math.select(2f, fractionsEdge, hitEdge);

            bool  hit      = math.any(hitEdge) | hitA | hitB;
            float fraction = math.min(math.min(fractionA, fractionB), math.cmin(fractionsEdge));
            if (hit)
            {
                var hitTransform = castStart;
                hitTransform.pos = math.lerp(castStart.pos, castEnd, fraction);
                DistanceBetween(in capsuleToCast, in hitTransform, in targetTriangle, in targetTriangleTransform, 1f, out var distanceResult);
                result = new ColliderCastResult
                {
                    hitpointOnCaster         = distanceResult.hitpointA,
                    hitpointOnTarget         = distanceResult.hitpointB,
                    normalOnCaster           = distanceResult.normalA,
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

        public static bool ColliderCast(in CapsuleCollider capsuleToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in ConvexCollider targetConvex,
                                        in RigidTransform targetConvexTransform,
                                        out ColliderCastResult result)
        {
            if (DistanceBetween(in capsuleToCast, in castStart, in targetConvex, in targetConvexTransform, 0f, out _))
            {
                result = default;
                return false;
            }

            var targetConvexTransformInverse = math.inverse(targetConvexTransform);
            var casterInTargetSpace          = math.mul(targetConvexTransformInverse, castStart);

            var  startA = math.transform(casterInTargetSpace, capsuleToCast.pointA);
            var  rayA   = new Ray(startA, startA + math.rotate(targetConvexTransformInverse, castEnd - castStart.pos));
            bool hitA   = SpatialInternal.RaycastRoundedConvex(rayA, targetConvex, capsuleToCast.radius, out var fractionA);
            fractionA   = math.select(2f, fractionA, hitA);
            var  startB = math.transform(casterInTargetSpace, capsuleToCast.pointB);
            var  rayB   = new Ray(startB, startB + math.rotate(targetConvexTransformInverse, castEnd - castStart.pos));
            bool hitB   = SpatialInternal.RaycastRoundedConvex(rayB, targetConvex, capsuleToCast.radius, out var fractionB);
            fractionB   = math.select(2f, fractionB, hitB);

            var        ray           = new Ray(0f, math.rotate(targetConvexTransformInverse, castStart.pos - castEnd));
            simdFloat3 startSimd     = new simdFloat3(startA, startA, startB, startB);
            int        bestEdgeIndex = -1;
            float      bestFraction  = 2f;
            ref var    blob          = ref targetConvex.convexColliderBlob.Value;

            var capEdge         = startB - startA;
            var capEdgeCrossRay = math.normalizesafe(math.cross(capEdge, ray.displacement), float3.zero);
            if (capEdgeCrossRay.Equals(float3.zero))
            {
                // The capsule aligns with the ray. We already have this case tested.
            }
            else
            {
                // We need four culling planes around the capsule
                var    correctedCapEdge = math.normalize(math.cross(ray.displacement, capEdgeCrossRay));
                var    ta               = math.select(startB, startA, math.dot(capEdgeCrossRay, startA) >= math.dot(capEdgeCrossRay, startB));
                var    tb               = math.select(startB, startA, math.dot(-capEdgeCrossRay, startA) >= math.dot(-capEdgeCrossRay, startB));
                var    tc               = math.select(startB, startA, math.dot(correctedCapEdge, startA) >= math.dot(correctedCapEdge, startB));
                var    td               = math.select(startB, startA, math.dot(-correctedCapEdge, startA) >= math.dot(-correctedCapEdge, startB));
                Plane  planeA           = new Plane(capEdgeCrossRay, -math.dot(capEdgeCrossRay, ta + capEdgeCrossRay * capsuleToCast.radius));
                Plane  planeB           = new Plane(-capEdgeCrossRay, -math.dot(-capEdgeCrossRay, tb - capEdgeCrossRay * capsuleToCast.radius));
                Plane  planeC           = new Plane(correctedCapEdge, -math.dot(correctedCapEdge, tc + correctedCapEdge * capsuleToCast.radius));
                Plane  planeD           = new Plane(-correctedCapEdge, -math.dot(-correctedCapEdge, td - correctedCapEdge * capsuleToCast.radius));
                float4 obbX             = new float4(planeA.normal.x, planeB.normal.x, planeC.normal.x, planeD.normal.x);
                float4 obbY             = new float4(planeA.normal.y, planeB.normal.y, planeC.normal.y, planeD.normal.y);
                float4 obbZ             = new float4(planeA.normal.z, planeB.normal.z, planeC.normal.z, planeD.normal.z);
                float4 obbD             = new float4(planeA.distanceFromOrigin, planeB.distanceFromOrigin, planeC.distanceFromOrigin, planeD.distanceFromOrigin);

                for (int i = 0; i < blob.vertexIndicesInEdges.Length; i++)
                {
                    var indices = blob.vertexIndicesInEdges[i];
                    var ax      = blob.verticesX[indices.x] * targetConvex.scale.x;
                    var ay      = blob.verticesY[indices.x] * targetConvex.scale.y;
                    var az      = blob.verticesZ[indices.x] * targetConvex.scale.z;
                    var bx      = blob.verticesX[indices.y] * targetConvex.scale.x;
                    var by      = blob.verticesY[indices.y] * targetConvex.scale.y;
                    var bz      = blob.verticesZ[indices.y] * targetConvex.scale.z;

                    bool4 isAOutside = obbX * ax + obbY * ay + obbZ * az + obbD > 0f;
                    bool4 isBOutside = obbX * bx + obbY * by + obbZ * bz + obbD > 0f;
                    if (math.any(isAOutside & isBOutside))
                        continue;

                    var edgePoints = new simdFloat3(new float4(ax, bx, bx, ax), new float4(ay, by, by, ay), new float4(az, bz, bz, az));
                    var cso        = startSimd - edgePoints;
                    if (SpatialInternal.RaycastRoundedQuad(ray, cso, capsuleToCast.radius, out var edgeFraction, out _))
                    {
                        if (edgeFraction < bestFraction)
                        {
                            bestFraction  = edgeFraction;
                            bestEdgeIndex = i;
                        }
                    }
                }
            }

            bool  hit      = (bestEdgeIndex >= 0) | hitA | hitB;
            float fraction = math.min(math.min(fractionA, fractionB), bestFraction);
            if (hit)
            {
                var hitTransform = castStart;
                hitTransform.pos = math.lerp(castStart.pos, castEnd, fraction);
                DistanceBetween(in capsuleToCast, in hitTransform, in targetConvex, in targetConvexTransform, 1f, out var distanceResult);
                result = new ColliderCastResult
                {
                    hitpointOnCaster         = distanceResult.hitpointA,
                    hitpointOnTarget         = distanceResult.hitpointB,
                    normalOnCaster           = distanceResult.normalA,
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

        public static bool ColliderCast(in CapsuleCollider capsuleToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in CompoundCollider targetCompound,
                                        in RigidTransform targetCompoundTransform,
                                        out ColliderCastResult result)
        {
            bool hit        = false;
            result          = default;
            result.distance = float.MaxValue;
            if (DistanceBetween(in capsuleToCast, in castStart, in targetCompound, in targetCompoundTransform, 0f, out _))
            {
                return false;
            }
            ref var blob          = ref targetCompound.compoundColliderBlob.Value;
            var     compoundScale = new PhysicsScale { scale = targetCompound.scale, state = PhysicsScale.State.Uniform };
            for (int i = 0; i < blob.colliders.Length; i++)
            {
                var blobTransform  = blob.transforms[i];
                blobTransform.pos *= targetCompound.scale;
                bool newHit        = ColliderCast(in capsuleToCast, in castStart, castEnd, ScaleCollider(in blob.colliders[i], compoundScale),
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
        public static bool ColliderCast(in BoxCollider boxToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in SphereCollider targetSphere,
                                        in RigidTransform targetSphereTransform,
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
                DistanceBetween(in boxToCast, in hitTransform, in targetSphere, in targetSphereTransform, 1f, out var distanceResult);
                result = new ColliderCastResult
                {
                    hitpointOnCaster         = distanceResult.hitpointA,
                    hitpointOnTarget         = distanceResult.hitpointB,
                    normalOnCaster           = distanceResult.normalA,
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

        public static bool ColliderCast(in BoxCollider boxToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in CapsuleCollider targetCapsule,
                                        in RigidTransform targetCapsuleTransform,
                                        out ColliderCastResult result)
        {
            if (DistanceBetween(in boxToCast, in castStart, in targetCapsule, in targetCapsuleTransform, 0f, out _))
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
            float4     fractionsEdge;
            float3     startA = boxToCast.center - boxToCast.halfSize;
            float3     startB = boxToCast.center + new float3(boxToCast.halfSize.x, -boxToCast.halfSize.y, -boxToCast.halfSize.z);
            simdFloat3 cso    = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitX.x            = SpatialInternal.RaycastRoundedQuad(ray, cso, targetCapsule.radius, out fractionsEdge.x, out _);
            startA            = boxToCast.center + new float3(-boxToCast.halfSize.x, -boxToCast.halfSize.y, boxToCast.halfSize.z);
            startB            = boxToCast.center + new float3(boxToCast.halfSize.x, -boxToCast.halfSize.y, boxToCast.halfSize.z);
            cso               = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitX.y            = SpatialInternal.RaycastRoundedQuad(ray, cso, targetCapsule.radius, out fractionsEdge.y, out _);
            startA            = boxToCast.center + new float3(-boxToCast.halfSize.x, boxToCast.halfSize.y, -boxToCast.halfSize.z);
            startB            = boxToCast.center + new float3(boxToCast.halfSize.x, boxToCast.halfSize.y, -boxToCast.halfSize.z);
            cso               = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitX.z            = SpatialInternal.RaycastRoundedQuad(ray, cso, targetCapsule.radius, out fractionsEdge.z, out _);
            startA            = boxToCast.center + new float3(-boxToCast.halfSize.x, boxToCast.halfSize.y, boxToCast.halfSize.z);
            startB            = boxToCast.center + boxToCast.halfSize;
            cso               = new simdFloat3(startA - targetA, startA - targetB, startB - targetB, startB - targetA);
            hitX.w            = SpatialInternal.RaycastRoundedQuad(ray, cso, targetCapsule.radius, out fractionsEdge.w, out _);
            fractionsEdge     = math.select(2f, fractionsEdge, hitX);

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
            float fraction = math.min(math.min(fractionA, fractionB), math.cmin(math.min(fractionsEdge, math.min(fractionsY, fractionsZ))));
            if (hit)
            {
                var hitTransform = castStart;
                hitTransform.pos = math.lerp(castStart.pos, castEnd, fraction);
                DistanceBetween(in boxToCast, in hitTransform, in targetCapsule, in targetCapsuleTransform, 1f, out var distanceResult);
                result = new ColliderCastResult
                {
                    hitpointOnCaster         = distanceResult.hitpointA,
                    hitpointOnTarget         = distanceResult.hitpointB,
                    normalOnCaster           = distanceResult.normalA,
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

        public static bool ColliderCast(in BoxCollider boxToCast, in RigidTransform castStart, float3 castEnd, in BoxCollider targetBox, in RigidTransform targetBoxTransform,
                                        out ColliderCastResult result)
        {
            return ColliderCastMpr(boxToCast, in castStart, castEnd,  targetBox, in targetBoxTransform, out result);
        }

        public static bool ColliderCast(in BoxCollider boxToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in TriangleCollider targetTriangle,
                                        in RigidTransform targetTriangleTransform,
                                        out ColliderCastResult result)
        {
            return ColliderCastMpr(boxToCast, in castStart, castEnd,  targetTriangle, in targetTriangleTransform, out result);
        }

        public static bool ColliderCast(in BoxCollider boxToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in ConvexCollider targetConvex,
                                        in RigidTransform targetConvexTransform,
                                        out ColliderCastResult result)
        {
            return ColliderCastMpr(boxToCast, in castStart, castEnd,  targetConvex, in targetConvexTransform, out result);
        }

        public static bool ColliderCast(in BoxCollider boxToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in CompoundCollider targetCompound,
                                        in RigidTransform targetCompoundTransform,
                                        out ColliderCastResult result)
        {
            bool hit        = false;
            result          = default;
            result.distance = float.MaxValue;
            if (DistanceBetween(in boxToCast, in castStart, in targetCompound, in targetCompoundTransform, 0f, out _))
            {
                return false;
            }
            ref var blob          = ref targetCompound.compoundColliderBlob.Value;
            var     compoundScale = new PhysicsScale { scale = targetCompound.scale, state = PhysicsScale.State.Uniform };
            for (int i = 0; i < blob.colliders.Length; i++)
            {
                var blobTransform  = blob.transforms[i];
                blobTransform.pos *= targetCompound.scale;
                bool newHit        = ColliderCast(in boxToCast, in castStart, castEnd, ScaleCollider(in blob.colliders[i], compoundScale),
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

        #region Triangle
        public static bool ColliderCast(in TriangleCollider triangleToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in SphereCollider targetSphere,
                                        in RigidTransform targetSphereTransform,
                                        out ColliderCastResult result)
        {
            var  castReverse        = castStart.pos - castEnd;
            var  worldToCasterSpace = math.inverse(castStart);
            var  start              = math.transform(targetSphereTransform, targetSphere.center);
            var  ray                = new Ray(math.transform(worldToCasterSpace, start), math.transform(worldToCasterSpace, start + castReverse));
            bool hit                = SpatialInternal.RaycastRoundedTriangle(ray, triangleToCast.AsSimdFloat3(), targetSphere.radius, out var fraction, out _);
            if (hit)
            {
                var hitTransform = castStart;
                hitTransform.pos = math.lerp(castStart.pos, castEnd, fraction);
                DistanceBetween(in triangleToCast, in hitTransform, in targetSphere, in targetSphereTransform, 1f, out var distanceResult);
                result = new ColliderCastResult
                {
                    hitpointOnCaster         = distanceResult.hitpointA,
                    hitpointOnTarget         = distanceResult.hitpointB,
                    normalOnCaster           = distanceResult.normalA,
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

        public static bool ColliderCast(in TriangleCollider triangleToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in CapsuleCollider targetCapsule,
                                        in RigidTransform targetCapsuleTransform,
                                        out ColliderCastResult result)
        {
            if (DistanceBetween(in triangleToCast, in castStart, in targetCapsule, in targetCapsuleTransform, 0f, out _))
            {
                result = default;
                return false;
            }

            var castStartInverse    = math.inverse(castStart);
            var targetInCasterSpace = math.mul(castStartInverse, targetCapsuleTransform);
            var triPoints           = triangleToCast.AsSimdFloat3();

            var  targetA = math.transform(targetInCasterSpace, targetCapsule.pointA);
            var  rayA    = new Ray(targetA, targetA - math.rotate(castStartInverse, castEnd - castStart.pos));
            bool hitA    = SpatialInternal.RaycastRoundedTriangle(rayA, triPoints, targetCapsule.radius, out var fractionA, out _);
            fractionA    = math.select(2f, fractionA, hitA);
            var  targetB = math.transform(targetInCasterSpace, targetCapsule.pointB);
            var  rayB    = new Ray(targetB, targetB - math.rotate(castStartInverse, castEnd - castStart.pos));
            bool hitB    = SpatialInternal.RaycastRoundedTriangle(rayB, triPoints, targetCapsule.radius, out var fractionB, out _);
            fractionB    = math.select(2f, fractionB, hitB);

            var        ray = new Ray(0f, math.rotate(castStartInverse, castStart.pos - castEnd));
            bool3      hitEdge;
            float3     fractionsEdge;
            simdFloat3 targetSimd = new simdFloat3(targetA, targetB, targetA, targetB);
            simdFloat3 cso        = triPoints.aabb - targetSimd;
            hitEdge.x             = SpatialInternal.RaycastRoundedQuad(ray, cso, targetCapsule.radius, out fractionsEdge.x, out _);
            cso                   = triPoints.bbcc - targetSimd;
            hitEdge.y             = SpatialInternal.RaycastRoundedQuad(ray, cso, targetCapsule.radius, out fractionsEdge.y, out _);
            cso                   = triPoints.ccaa - targetSimd;
            hitEdge.z             = SpatialInternal.RaycastRoundedQuad(ray, cso, targetCapsule.radius, out fractionsEdge.z, out _);
            fractionsEdge         = math.select(2f, fractionsEdge, hitEdge);

            bool  hit      = math.any(hitEdge) | hitA | hitB;
            float fraction = math.min(math.min(fractionA, fractionB), math.cmin(fractionsEdge));
            if (hit)
            {
                var hitTransform = castStart;
                hitTransform.pos = math.lerp(castStart.pos, castEnd, fraction);
                DistanceBetween(in triangleToCast, in hitTransform, in targetCapsule, in targetCapsuleTransform, 1f, out var distanceResult);
                result = new ColliderCastResult
                {
                    hitpointOnCaster         = distanceResult.hitpointA,
                    hitpointOnTarget         = distanceResult.hitpointB,
                    normalOnCaster           = distanceResult.normalA,
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

        public static bool ColliderCast(in TriangleCollider triangleToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in BoxCollider targetBox,
                                        in RigidTransform targetBoxTransform,
                                        out ColliderCastResult result)
        {
            return ColliderCastMpr(triangleToCast, in castStart, castEnd,  targetBox, in targetBoxTransform, out result);
        }

        public static bool ColliderCast(in TriangleCollider triangleToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in TriangleCollider targetTriangle,
                                        in RigidTransform targetTriangleTransform,
                                        out ColliderCastResult result)
        {
            return ColliderCastMpr(triangleToCast, in castStart, castEnd,  targetTriangle, in targetTriangleTransform, out result);
        }

        public static bool ColliderCast(in TriangleCollider triangleToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in ConvexCollider targetConvex,
                                        in RigidTransform targetConvexTransform,
                                        out ColliderCastResult result)
        {
            return ColliderCastMpr(triangleToCast, in castStart, castEnd,  targetConvex, in targetConvexTransform, out result);
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
            if (DistanceBetween(in triangleToCast, in castStart, in targetCompound, in targetCompoundTransform, 0f, out _))
            {
                return false;
            }
            ref var blob          = ref targetCompound.compoundColliderBlob.Value;
            var     compoundScale = new PhysicsScale { scale = targetCompound.scale, state = PhysicsScale.State.Uniform };
            for (int i = 0; i < blob.colliders.Length; i++)
            {
                var blobTransform  = blob.transforms[i];
                blobTransform.pos *= targetCompound.scale;
                bool newHit        = ColliderCast(in triangleToCast, in castStart, castEnd, ScaleCollider(in blob.colliders[i], compoundScale),
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

        #region Convex
        public static bool ColliderCast(in ConvexCollider convexToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in SphereCollider targetSphere,
                                        in RigidTransform targetSphereTransform,
                                        out ColliderCastResult result)
        {
            var  castReverse        = castStart.pos - castEnd;
            var  worldToCasterSpace = math.inverse(castStart);
            var  start              = math.transform(targetSphereTransform, targetSphere.center);
            var  ray                = new Ray(math.transform(worldToCasterSpace, start), math.transform(worldToCasterSpace, start + castReverse));
            bool hit                = SpatialInternal.RaycastRoundedConvex(ray, convexToCast, targetSphere.radius, out var fraction);
            if (hit)
            {
                var hitTransform = castStart;
                hitTransform.pos = math.lerp(castStart.pos, castEnd, fraction);
                DistanceBetween(in convexToCast, in hitTransform, in targetSphere, in targetSphereTransform, 1f, out var distanceResult);
                result = new ColliderCastResult
                {
                    hitpointOnCaster         = distanceResult.hitpointA,
                    hitpointOnTarget         = distanceResult.hitpointB,
                    normalOnCaster           = distanceResult.normalA,
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

        public static bool ColliderCast(in ConvexCollider convexToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in CapsuleCollider targetCapsule,
                                        in RigidTransform targetCapsuleTransform,
                                        out ColliderCastResult result)
        {
            if (DistanceBetween(in convexToCast, in castStart, in targetCapsule, in targetCapsuleTransform, 0f, out _))
            {
                result = default;
                return false;
            }

            var castStartInverse    = math.inverse(castStart);
            var targetInCasterSpace = math.mul(castStartInverse, targetCapsuleTransform);

            var  targetA = math.transform(targetInCasterSpace, targetCapsule.pointA);
            var  rayA    = new Ray(targetA, targetA - math.rotate(castStartInverse, castEnd - castStart.pos));
            bool hitA    = SpatialInternal.RaycastRoundedConvex(rayA, convexToCast, targetCapsule.radius, out var fractionA);
            fractionA    = math.select(2f, fractionA, hitA);
            var  targetB = math.transform(targetInCasterSpace, targetCapsule.pointB);
            var  rayB    = new Ray(targetB, targetB - math.rotate(castStartInverse, castEnd - castStart.pos));
            bool hitB    = SpatialInternal.RaycastRoundedConvex(rayB, convexToCast, targetCapsule.radius, out var fractionB);
            fractionB    = math.select(2f, fractionB, hitB);

            var        ray           = new Ray(0f, math.rotate(castStartInverse, castStart.pos - castEnd));
            simdFloat3 targetSimd    = new simdFloat3(targetA, targetB, targetB, targetA);
            int        bestEdgeIndex = -1;
            float      bestFraction  = 2f;
            ref var    blob          = ref convexToCast.convexColliderBlob.Value;

            var capEdge         = targetB - targetA;
            var capEdgeCrossRay = math.normalizesafe(math.cross(capEdge, ray.displacement), float3.zero);
            if (capEdgeCrossRay.Equals(float3.zero))
            {
                // The capsule aligns with the ray. We already have this case tested.
            }
            else
            {
                // We need four culling planes around the capsule
                var    correctedCapEdge = math.normalize(math.cross(ray.displacement, capEdgeCrossRay));
                var    ta               = math.select(targetB, targetA, math.dot(capEdgeCrossRay, targetA) >= math.dot(capEdgeCrossRay, targetB));
                var    tb               = math.select(targetB, targetA, math.dot(-capEdgeCrossRay, targetA) >= math.dot(-capEdgeCrossRay, targetB));
                var    tc               = math.select(targetB, targetA, math.dot(correctedCapEdge, targetA) >= math.dot(correctedCapEdge, targetB));
                var    td               = math.select(targetB, targetA, math.dot(-correctedCapEdge, targetA) >= math.dot(-correctedCapEdge, targetB));
                Plane  planeA           = new Plane(capEdgeCrossRay, -math.dot(capEdgeCrossRay, ta + capEdgeCrossRay * targetCapsule.radius));
                Plane  planeB           = new Plane(-capEdgeCrossRay, -math.dot(-capEdgeCrossRay, tb - capEdgeCrossRay * targetCapsule.radius));
                Plane  planeC           = new Plane(correctedCapEdge, -math.dot(correctedCapEdge, tc + correctedCapEdge * targetCapsule.radius));
                Plane  planeD           = new Plane(-correctedCapEdge, -math.dot(-correctedCapEdge, td - correctedCapEdge * targetCapsule.radius));
                float4 obbX             = new float4(planeA.normal.x, planeB.normal.x, planeC.normal.x, planeD.normal.x);
                float4 obbY             = new float4(planeA.normal.y, planeB.normal.y, planeC.normal.y, planeD.normal.y);
                float4 obbZ             = new float4(planeA.normal.z, planeB.normal.z, planeC.normal.z, planeD.normal.z);
                float4 obbD             = new float4(planeA.distanceFromOrigin, planeB.distanceFromOrigin, planeC.distanceFromOrigin, planeD.distanceFromOrigin);

                for (int i = 0; i < blob.vertexIndicesInEdges.Length; i++)
                {
                    var indices = blob.vertexIndicesInEdges[i];
                    var ax      = blob.verticesX[indices.x] * convexToCast.scale.x;
                    var ay      = blob.verticesY[indices.x] * convexToCast.scale.y;
                    var az      = blob.verticesZ[indices.x] * convexToCast.scale.z;
                    var bx      = blob.verticesX[indices.y] * convexToCast.scale.x;
                    var by      = blob.verticesY[indices.y] * convexToCast.scale.y;
                    var bz      = blob.verticesZ[indices.y] * convexToCast.scale.z;

                    bool4 isAOutside = obbX * ax + obbY * ay + obbZ * az + obbD > 0f;
                    bool4 isBOutside = obbX * bx + obbY * by + obbZ * bz + obbD > 0f;
                    if (math.any(isAOutside & isBOutside))
                        continue;

                    var edgePoints = new simdFloat3(new float4(ax, ax, bx, bx), new float4(ay, ay, by, by), new float4(az, az, bz, bz));
                    var cso        = edgePoints - targetSimd;
                    if (SpatialInternal.RaycastRoundedQuad(ray, cso, targetCapsule.radius, out var edgeFraction, out _))
                    {
                        if (edgeFraction < bestFraction)
                        {
                            bestFraction  = edgeFraction;
                            bestEdgeIndex = i;
                        }
                    }
                }
            }

            bool  hit      = (bestEdgeIndex > 0) | hitA | hitB;
            float fraction = math.min(math.min(fractionA, fractionB), bestFraction);
            if (hit)
            {
                var hitTransform = castStart;
                hitTransform.pos = math.lerp(castStart.pos, castEnd, fraction);
                DistanceBetween(in convexToCast, in hitTransform, in targetCapsule, in targetCapsuleTransform, 1f, out var distanceResult);
                result = new ColliderCastResult
                {
                    hitpointOnCaster         = distanceResult.hitpointA,
                    hitpointOnTarget         = distanceResult.hitpointB,
                    normalOnCaster           = distanceResult.normalA,
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

        public static bool ColliderCast(in ConvexCollider convexToCast, in RigidTransform castStart, float3 castEnd, in BoxCollider targetBox, in RigidTransform targetBoxTransform,
                                        out ColliderCastResult result)
        {
            return ColliderCastMpr(convexToCast, in castStart, castEnd,  targetBox, in targetBoxTransform, out result);
        }

        public static bool ColliderCast(in ConvexCollider convexToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in TriangleCollider targetTriangle,
                                        in RigidTransform targetTriangleTransform,
                                        out ColliderCastResult result)
        {
            return ColliderCastMpr(convexToCast, in castStart, castEnd,  targetTriangle, in targetTriangleTransform, out result);
        }

        public static bool ColliderCast(in ConvexCollider convexToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in ConvexCollider targetConvex,
                                        in RigidTransform targetConvexTransform,
                                        out ColliderCastResult result)
        {
            return ColliderCastMpr(convexToCast, in castStart, castEnd,  targetConvex, in targetConvexTransform, out result);
        }

        public static bool ColliderCast(in ConvexCollider convexToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in CompoundCollider targetCompound,
                                        in RigidTransform targetCompoundTransform,
                                        out ColliderCastResult result)
        {
            bool hit        = false;
            result          = default;
            result.distance = float.MaxValue;
            if (DistanceBetween(in convexToCast, in castStart, in targetCompound, in targetCompoundTransform, 0f, out _))
            {
                return false;
            }
            ref var blob          = ref targetCompound.compoundColliderBlob.Value;
            var     compoundScale = new PhysicsScale { scale = targetCompound.scale, state = PhysicsScale.State.Uniform };
            for (int i = 0; i < blob.colliders.Length; i++)
            {
                var blobTransform  = blob.transforms[i];
                blobTransform.pos *= targetCompound.scale;
                bool newHit        = ColliderCast(in convexToCast, in castStart, castEnd, ScaleCollider(in blob.colliders[i], compoundScale),
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
        public static bool ColliderCast(in CompoundCollider compoundToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in SphereCollider targetSphere,
                                        in RigidTransform targetSphereTransform,
                                        out ColliderCastResult result)
        {
            bool hit        = false;
            result          = default;
            result.distance = float.MaxValue;
            if (DistanceBetween(in compoundToCast, in castStart, in targetSphere, in targetSphereTransform, 0f, out _))
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
                bool newHit        = ColliderCast(ScaleCollider(in blob.colliders[i], compoundScale),
                                                  start, start.pos + (castEnd - castStart.pos),
                                                  in targetSphere,
                                                  in targetSphereTransform,
                                                  out var newResult);

                newResult.subColliderIndexOnCaster  = i;
                newHit                             &= newResult.distance < result.distance;
                hit                                |= newHit;
                result                              = newHit ? newResult : result;
            }
            return hit;
        }

        public static bool ColliderCast(in CompoundCollider compoundToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in CapsuleCollider targetCapsule,
                                        in RigidTransform targetCapsuleTransform,
                                        out ColliderCastResult result)
        {
            bool hit        = false;
            result          = default;
            result.distance = float.MaxValue;
            if (DistanceBetween(in compoundToCast, in castStart, in targetCapsule, in targetCapsuleTransform, 0f, out _))
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
                bool newHit        = ColliderCast(ScaleCollider(in blob.colliders[i], compoundScale),
                                                  start, start.pos + (castEnd - castStart.pos),
                                                  in targetCapsule,
                                                  in targetCapsuleTransform,
                                                  out var newResult);

                newResult.subColliderIndexOnCaster  = i;
                newHit                             &= newResult.distance < result.distance;
                hit                                |= newHit;
                result                              = newHit ? newResult : result;
            }
            return hit;
        }

        public static bool ColliderCast(in CompoundCollider compoundToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in BoxCollider targetBox,
                                        in RigidTransform targetBoxTransform,
                                        out ColliderCastResult result)
        {
            bool hit        = false;
            result          = default;
            result.distance = float.MaxValue;
            if (DistanceBetween(in compoundToCast, in castStart, in targetBox, in targetBoxTransform, 0f, out _))
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
                bool newHit        = ColliderCast(ScaleCollider(in blob.colliders[i], compoundScale),
                                                  start, start.pos + (castEnd - castStart.pos),
                                                  in targetBox,
                                                  in targetBoxTransform,
                                                  out var newResult);

                newResult.subColliderIndexOnCaster  = i;
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
            ref var blob          = ref compoundToCast.compoundColliderBlob.Value;
            var     compoundScale = new PhysicsScale { scale = compoundToCast.scale, state = PhysicsScale.State.Uniform };
            for (int i = 0; i < blob.colliders.Length; i++)
            {
                var blobTransform  = blob.transforms[i];
                blobTransform.pos *= compoundToCast.scale;
                var  start         = math.mul(castStart, blobTransform);
                bool newHit        = ColliderCast(ScaleCollider(in blob.colliders[i], compoundScale),
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

        public static bool ColliderCast(in CompoundCollider compoundToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in ConvexCollider targetConvex,
                                        in RigidTransform targetConvexTransform,
                                        out ColliderCastResult result)
        {
            bool hit        = false;
            result          = default;
            result.distance = float.MaxValue;
            if (DistanceBetween(in compoundToCast, in castStart, in targetConvex, in targetConvexTransform, 0f, out _))
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
                bool newHit        = ColliderCast(ScaleCollider(in blob.colliders[i], compoundScale),
                                                  start, start.pos + (castEnd - castStart.pos),
                                                  in targetConvex,
                                                  in targetConvexTransform,
                                                  out var newResult);

                newResult.subColliderIndexOnCaster  = i;
                newHit                             &= newResult.distance < result.distance;
                hit                                |= newHit;
                result                              = newHit ? newResult : result;
            }
            return hit;
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
            ref var blob          = ref compoundToCast.compoundColliderBlob.Value;
            var     compoundScale = new PhysicsScale { scale = compoundToCast.scale, state = PhysicsScale.State.Uniform };
            for (int i = 0; i < blob.colliders.Length; i++)
            {
                var blobTransform  = blob.transforms[i];
                blobTransform.pos *= compoundToCast.scale;
                var  start         = math.mul(castStart, blobTransform);
                bool newHit        = ColliderCast(ScaleCollider(in blob.colliders[i], compoundScale),
                                                  start, start.pos + (castEnd - castStart.pos),
                                                  in targetCompound,
                                                  in targetCompoundTransform,
                                                  out var newResult);

                newResult.subColliderIndexOnCaster  = i;
                newHit                             &= newResult.distance < result.distance;
                hit                                |= newHit;
                result                              = newHit ? newResult : result;
            }
            return hit;
        }
        #endregion

        #region Layer

        public static bool ColliderCast(in Collider colliderToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in CollisionLayer layer,
                                        out ColliderCastResult result,
                                        out LayerBodyInfo layerBodyInfo)
        {
            result        = default;
            layerBodyInfo = default;
            var processor = new LayerQueryProcessors.ColliderCastClosestImmediateProcessor(colliderToCast, castStart, castEnd, ref result, ref layerBodyInfo);
            FindObjects(AabbFrom(in colliderToCast, in castStart, castEnd), layer, processor).RunImmediate();
            var hit                         = result.subColliderIndexOnTarget >= 0;
            result.subColliderIndexOnTarget = math.max(result.subColliderIndexOnTarget, 0);
            return hit;
        }

        public static bool ColliderCastAny(in Collider colliderToCast,
                                           in RigidTransform castStart,
                                           float3 castEnd,
                                           in CollisionLayer layer,
                                           out ColliderCastResult result,
                                           out LayerBodyInfo layerBodyInfo)
        {
            result        = default;
            layerBodyInfo = default;
            var processor = new LayerQueryProcessors.ColliderCastAnyImmediateProcessor(colliderToCast, castStart, castEnd, ref result, ref layerBodyInfo);
            FindObjects(AabbFrom(in colliderToCast, in castStart, castEnd), layer, processor).RunImmediate();
            var hit                         = result.subColliderIndexOnTarget >= 0;
            result.subColliderIndexOnTarget = math.max(result.subColliderIndexOnTarget, 0);
            return hit;
        }

        #endregion

        #region Internal
        internal static bool ColliderCastMpr(in Collider colliderToCast, in RigidTransform castStart, float3 castEnd, in Collider targetCollider, in RigidTransform targetTransform,
                                             out ColliderCastResult result)
        {
            var  castStartInverse             = math.inverse(castStart);
            var  targetInCasterSpaceTransform = math.mul(castStartInverse, targetTransform);
            var  castDirection                = math.rotate(castStartInverse, castEnd - castStart.pos);
            var  normalizedCastDirection      = math.normalize(castDirection);
            bool hit                          = SpatialInternal.MprCastNoRoundness(in colliderToCast,
                                                                                   in targetCollider,
                                                                                   in targetInCasterSpaceTransform,
                                                                                   normalizedCastDirection,
                                                                                   math.length(castDirection),
                                                                                   out float distanceOfImpact,
                                                                                   out bool somethingWentWrong);
            CheckMprResolved(somethingWentWrong);
            if (!hit || distanceOfImpact <= 0f)
            {
                result = default;
                return false;
            }

            var castHitOffset       = math.rotate(castStart, normalizedCastDirection * distanceOfImpact);
            var casterHitTransform  = castStart;
            casterHitTransform.pos += castHitOffset;
            DistanceBetween(in colliderToCast, in casterHitTransform, in targetCollider, in targetTransform, float.MaxValue, out var distanceResult);

            result = new ColliderCastResult
            {
                distance                 = distanceOfImpact,
                hitpointOnCaster         = distanceResult.hitpointA,
                hitpointOnTarget         = distanceResult.hitpointB,
                normalOnCaster           = distanceResult.normalA,
                normalOnTarget           = distanceResult.normalB,
                subColliderIndexOnCaster = 0,
                subColliderIndexOnTarget = 0
            };

            return true;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        internal static void CheckMprResolved(bool somethingWentWrong)
        {
            if (somethingWentWrong)
                UnityEngine.Debug.LogWarning("MPR failed to resolve within the allotted number of iterations. If you see this, please report a bug.");
        }
        #endregion
    }
}

