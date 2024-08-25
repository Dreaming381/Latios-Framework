using Latios.Transforms;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class ConvexTriMesh
    {
        public static bool DistanceBetween(in TriMeshCollider triMesh,
                                           in RigidTransform triMeshTransform,
                                           in ConvexCollider convex,
                                           in RigidTransform convexTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            var convexInTriMeshTransform  = math.mul(math.inverse(triMeshTransform), convexTransform);
            var aabb                      = Physics.AabbFrom(convex, in convexInTriMeshTransform);
            aabb.min                     -= maxDistance;
            aabb.max                     += maxDistance;
            var processor                 = new ConvexDistanceProcessor
            {
                blob            = triMesh.triMeshColliderBlob,
                convex          = convex,
                convexTransform = convexInTriMeshTransform,
                maxDistance     = maxDistance,
                bestDistance    = float.MaxValue,
                found           = false,
                scale           = triMesh.scale
            };
            triMesh.triMeshColliderBlob.Value.FindTriangles(in aabb, ref processor, triMesh.scale);
            if (processor.found)
            {
                var hitTriangle = Physics.ScaleStretchCollider(triMesh.triMeshColliderBlob.Value.triangles[processor.bestIndex], 1f, triMesh.scale);
                var hit         = TriangleConvex.DistanceBetween(in convex,
                                                                 in convexTransform,
                                                                 in hitTriangle,
                                                                 in triMeshTransform,
                                                                 maxDistance,
                                                                 out result);
                (result.hitpointA, result.hitpointB) = (result.hitpointB, result.hitpointA);
                (result.normalA, result.normalB)     = (result.normalB, result.normalA);
                result.subColliderIndexB             = result.subColliderIndexA;
                result.subColliderIndexA             = processor.bestIndex;
                return hit;
            }
            result = default;
            return false;
        }

        public static unsafe void DistanceBetweenAll<T>(in TriMeshCollider triMesh,
                                                        in RigidTransform triMeshTransform,
                                                        in ConvexCollider convex,
                                                        in RigidTransform convexTransform,
                                                        float maxDistance,
                                                        ref T processor) where T : unmanaged, IDistanceBetweenAllProcessor
        {
            var convexInTriMeshTransform  = math.mul(math.inverse(triMeshTransform), convexTransform);
            var aabb                      = Physics.AabbFrom(convex, convexInTriMeshTransform);
            aabb.min                     -= maxDistance;
            aabb.max                     += maxDistance;
            var triProcessor              = new DistanceAllProcessor<T>
            {
                triMesh          = triMesh,
                triMeshTransform = triMeshTransform,
                convex           = convex,
                convexTransform  = convexTransform,
                maxDistance      = maxDistance,
                processor        = (T*)UnsafeUtility.AddressOf(ref processor)
            };
            triMesh.triMeshColliderBlob.Value.FindTriangles(in aabb, ref triProcessor, triMesh.scale);
        }

        public static bool ColliderCast(in ConvexCollider convexToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in TriMeshCollider targetTriMesh,
                                        in RigidTransform targetTriMeshTransform,
                                        out ColliderCastResult result)
        {
            var targetTriMeshTransformInverse = math.inverse(targetTriMeshTransform);
            var casterInTargetSpace           = math.mul(targetTriMeshTransformInverse, castStart);
            var aabb                          =
                Physics.AabbFrom(convexToCast, in casterInTargetSpace, casterInTargetSpace.pos + math.rotate(targetTriMeshTransformInverse, castEnd - castStart.pos));
            var processor = new CastProcessor
            {
                blob            = targetTriMesh.triMeshColliderBlob,
                convex          = convexToCast,
                castEnd         = castEnd,
                castStart       = castStart,
                found           = false,
                invalid         = false,
                targetTransform = targetTriMeshTransform,
                scale           = targetTriMesh.scale
            };
            targetTriMesh.triMeshColliderBlob.Value.FindTriangles(in aabb, ref processor, targetTriMesh.scale);

            if (processor.invalid || !processor.found)
            {
                result = default;
                return false;
            }

            var hitTransform  = castStart;
            hitTransform.pos += math.normalize(castEnd - castStart.pos) * processor.bestDistance;
            var hitTriangle   = Physics.ScaleStretchCollider(targetTriMesh.triMeshColliderBlob.Value.triangles[processor.bestIndex], 1f, targetTriMesh.scale);
            TriangleConvex.DistanceBetween(in convexToCast,
                                           in hitTransform,
                                           in hitTriangle,
                                           in targetTriMeshTransform,
                                           1f,
                                           out var distanceResult);
            result = new ColliderCastResult
            {
                hitpoint                 = distanceResult.hitpointA,
                normalOnCaster           = distanceResult.normalA,
                normalOnTarget           = distanceResult.normalB,
                subColliderIndexOnCaster = processor.bestIndex,
                subColliderIndexOnTarget = distanceResult.subColliderIndexA,
                distance                 = math.distance(hitTransform.pos, castStart.pos)
            };
            return true;
        }

        public static bool ColliderCast(in TriMeshCollider triMeshToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in ConvexCollider targetConvex,
                                        in RigidTransform targetConvexTransform,
                                        out ColliderCastResult result)
        {
            var castReverse         = castStart.pos - castEnd;
            var worldToCasterSpace  = math.inverse(castStart);
            var targetInCasterSpace = math.mul(worldToCasterSpace, targetConvexTransform);
            var reverseCastEnd      = targetInCasterSpace.pos + math.rotate(worldToCasterSpace, castReverse);
            var aabb                = Physics.AabbFrom(targetConvex, in targetInCasterSpace, reverseCastEnd);
            var processor           = new CastProcessor
            {
                blob            = triMeshToCast.triMeshColliderBlob,
                convex          = targetConvex,
                castEnd         = reverseCastEnd,
                castStart       = targetInCasterSpace,
                found           = false,
                invalid         = false,
                targetTransform = RigidTransform.identity,
                scale           = triMeshToCast.scale
            };
            triMeshToCast.triMeshColliderBlob.Value.FindTriangles(in aabb, ref processor, triMeshToCast.scale);

            if (processor.invalid || !processor.found)
            {
                result = default;
                return false;
            }

            var hitTransform  = castStart;
            hitTransform.pos += math.normalize(castEnd - castStart.pos) * processor.bestDistance;
            var hitTriangle   = Physics.ScaleStretchCollider(triMeshToCast.triMeshColliderBlob.Value.triangles[processor.bestIndex], 1f, triMeshToCast.scale);
            TriangleConvex.DistanceBetween(in targetConvex,
                                           in targetConvexTransform,
                                           in hitTriangle,
                                           in hitTransform,
                                           1f,
                                           out var distanceResult);
            result = new ColliderCastResult
            {
                hitpoint                 = distanceResult.hitpointB,
                normalOnCaster           = distanceResult.normalB,
                normalOnTarget           = distanceResult.normalA,
                subColliderIndexOnCaster = distanceResult.subColliderIndexA,
                subColliderIndexOnTarget = processor.bestIndex,
                distance                 = math.distance(hitTransform.pos, castStart.pos)
            };
            return true;
        }

        public static UnitySim.ContactsBetweenResult UnityContactsBetween(in TriMeshCollider triMesh,
                                                                          in RigidTransform triMeshTransform,
                                                                          in ConvexCollider convex,
                                                                          in RigidTransform convexTransform,
                                                                          in ColliderDistanceResult distanceResult)
        {
            var triangle = Physics.ScaleStretchCollider(triMesh.triMeshColliderBlob.Value.triangles[distanceResult.subColliderIndexA], 1f, triMesh.scale);
            return TriangleConvex.UnityContactsBetween(in convex, in convexTransform, in triangle, in triMeshTransform, distanceResult.ToFlipped()).ToFlipped();
        }

        struct ConvexDistanceProcessor : TriMeshColliderBlob.IFindTrianglesProcessor
        {
            public BlobAssetReference<TriMeshColliderBlob> blob;
            public ConvexCollider                          convex;
            public RigidTransform                          convexTransform;
            public float3                                  scale;
            public float                                   maxDistance;
            public float                                   bestDistance;
            public int                                     bestIndex;
            public bool                                    found;

            public bool Execute(int index)
            {
                var triangle = Physics.ScaleStretchCollider(blob.Value.triangles[index], 1f, scale);
                if (TriangleConvex.DistanceBetween(in convex, in convexTransform, in triangle, in RigidTransform.identity, maxDistance, out var hit))
                {
                    if (!found || hit.distance < bestDistance)
                    {
                        found        = true;
                        bestDistance = hit.distance;
                        bestIndex    = index;
                    }
                }

                return true;
            }
        }

        unsafe struct DistanceAllProcessor<T> : TriMeshColliderBlob.IFindTrianglesProcessor where T : unmanaged, IDistanceBetweenAllProcessor
        {
            public TriMeshCollider triMesh;
            public RigidTransform  triMeshTransform;
            public ConvexCollider  convex;
            public RigidTransform  convexTransform;
            public float           maxDistance;
            public T*              processor;

            public bool Execute(int index)
            {
                var triangle = Physics.ScaleStretchCollider(triMesh.triMeshColliderBlob.Value.triangles[index], 1f, triMesh.scale);
                if (TriangleConvex.DistanceBetween(in convex, in convexTransform, in triangle, in triMeshTransform, maxDistance, out var result))
                {
                    result.subColliderIndexA = index;

                    (result.hitpointA, result.hitpointB)                 = (result.hitpointB, result.hitpointA);
                    (result.normalA, result.normalB)                     = (result.normalB, result.normalA);
                    (result.subColliderIndexA, result.subColliderIndexB) = (result.subColliderIndexB, result.subColliderIndexA);

                    processor->Execute(in result);
                }
                return true;
            }
        }

        struct CastProcessor : TriMeshColliderBlob.IFindTrianglesProcessor
        {
            public BlobAssetReference<TriMeshColliderBlob> blob;
            public ConvexCollider                          convex;
            public RigidTransform                          castStart;
            public float3                                  castEnd;
            public RigidTransform                          targetTransform;
            public float3                                  scale;
            public float                                   bestDistance;
            public int                                     bestIndex;
            public bool                                    found;
            public bool                                    invalid;

            public bool Execute(int index)
            {
                var triangle = Physics.ScaleStretchCollider(blob.Value.triangles[index], 1f, scale);
                // Check that we don't start already intersecting.
                if (TriangleConvex.DistanceBetween(in convex, in castStart, in triangle, in targetTransform, 0f, out _))
                {
                    invalid = true;
                    return false;
                }
                if (TriangleConvex.ColliderCast(in convex, in castStart, castEnd, in triangle, in targetTransform, out var hit))
                {
                    if (!found || hit.distance < bestDistance)
                    {
                        found        = true;
                        bestDistance = hit.distance;
                        bestIndex    = index;
                    }
                }

                return true;
            }
        }
    }
}

