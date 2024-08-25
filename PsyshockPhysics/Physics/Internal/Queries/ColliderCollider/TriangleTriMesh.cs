using Latios.Transforms;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class TriangleTriMesh
    {
        public static bool DistanceBetween(in TriMeshCollider triMesh,
                                           in RigidTransform triMeshTransform,
                                           in TriangleCollider triangle,
                                           in RigidTransform triangleTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            var triangleInTriMeshTransform  = math.mul(math.inverse(triMeshTransform), triangleTransform);
            var aabb                        = Physics.AabbFrom(triangle, in triangleInTriMeshTransform);
            aabb.min                       -= maxDistance;
            aabb.max                       += maxDistance;
            var processor                   = new TriangleDistanceProcessor
            {
                blob              = triMesh.triMeshColliderBlob,
                triangle          = triangle,
                triangleTransform = triangleInTriMeshTransform,
                maxDistance       = maxDistance,
                bestDistance      = float.MaxValue,
                found             = false,
                scale             = triMesh.scale
            };
            triMesh.triMeshColliderBlob.Value.FindTriangles(in aabb, ref processor, triMesh.scale);
            if (processor.found)
            {
                var hitTriangle = Physics.ScaleStretchCollider(triMesh.triMeshColliderBlob.Value.triangles[processor.bestIndex], 1f, triMesh.scale);
                var hit         = TriangleTriangle.DistanceBetween(in hitTriangle,
                                                                   in triMeshTransform,
                                                                   in triangle,
                                                                   in triangleTransform,
                                                                   maxDistance,
                                                                   out result);
                result.subColliderIndexA = processor.bestIndex;
                return hit;
            }
            result = default;
            return false;
        }

        public static unsafe void DistanceBetweenAll<T>(in TriMeshCollider triMesh,
                                                        in RigidTransform triMeshTransform,
                                                        in TriangleCollider triangle,
                                                        in RigidTransform triangleTransform,
                                                        float maxDistance,
                                                        ref T processor) where T : unmanaged, IDistanceBetweenAllProcessor
        {
            var triangleInTriMeshTransform  = math.mul(math.inverse(triMeshTransform), triangleTransform);
            var aabb                        = Physics.AabbFrom(triangle, triangleInTriMeshTransform);
            aabb.min                       -= maxDistance;
            aabb.max                       += maxDistance;
            var triProcessor                = new DistanceAllProcessor<T>
            {
                triMesh           = triMesh,
                triMeshTransform  = triMeshTransform,
                triangle          = triangle,
                triangleTransform = triangleTransform,
                maxDistance       = maxDistance,
                processor         = (T*)UnsafeUtility.AddressOf(ref processor)
            };
            triMesh.triMeshColliderBlob.Value.FindTriangles(in aabb, ref triProcessor, triMesh.scale);
        }

        public static bool ColliderCast(in TriangleCollider triangleToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in TriMeshCollider targetTriMesh,
                                        in RigidTransform targetTriMeshTransform,
                                        out ColliderCastResult result)
        {
            var targetTriMeshTransformInverse = math.inverse(targetTriMeshTransform);
            var casterInTargetSpace           = math.mul(targetTriMeshTransformInverse, castStart);
            var aabb                          =
                Physics.AabbFrom(triangleToCast, in casterInTargetSpace, casterInTargetSpace.pos + math.rotate(targetTriMeshTransformInverse, castEnd - castStart.pos));
            var processor = new CastProcessor
            {
                blob            = targetTriMesh.triMeshColliderBlob,
                triangle        = triangleToCast,
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
            TriangleTriangle.DistanceBetween(in hitTriangle,
                                             in targetTriMeshTransform,
                                             in triangleToCast,
                                             in hitTransform,
                                             1f,
                                             out var distanceResult);
            result = new ColliderCastResult
            {
                hitpoint                 = distanceResult.hitpointB,
                normalOnCaster           = distanceResult.normalB,
                normalOnTarget           = distanceResult.normalA,
                subColliderIndexOnCaster = distanceResult.subColliderIndexB,
                subColliderIndexOnTarget = processor.bestIndex,
                distance                 = math.distance(hitTransform.pos, castStart.pos)
            };
            return true;
        }

        public static bool ColliderCast(in TriMeshCollider triMeshToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in TriangleCollider targetTriangle,
                                        in RigidTransform targetTriangleTransform,
                                        out ColliderCastResult result)
        {
            var castReverse         = castStart.pos - castEnd;
            var worldToCasterSpace  = math.inverse(castStart);
            var targetInCasterSpace = math.mul(worldToCasterSpace, targetTriangleTransform);
            var reverseCastEnd      = targetInCasterSpace.pos + math.rotate(worldToCasterSpace, castReverse);
            var aabb                = Physics.AabbFrom(targetTriangle, in targetInCasterSpace, reverseCastEnd);
            var processor           = new CastProcessor
            {
                blob            = triMeshToCast.triMeshColliderBlob,
                triangle        = targetTriangle,
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
            TriangleTriangle.DistanceBetween(in hitTriangle,
                                             in hitTransform,
                                             in targetTriangle,
                                             in targetTriangleTransform,
                                             1f,
                                             out var distanceResult);
            result = new ColliderCastResult
            {
                hitpoint                 = distanceResult.hitpointA,
                normalOnCaster           = distanceResult.normalA,
                normalOnTarget           = distanceResult.normalB,
                subColliderIndexOnCaster = processor.bestIndex,
                subColliderIndexOnTarget = distanceResult.subColliderIndexB,
                distance                 = math.distance(hitTransform.pos, castStart.pos)
            };
            return true;
        }

        public static UnitySim.ContactsBetweenResult UnityContactsBetween(in TriMeshCollider triMesh,
                                                                          in RigidTransform triMeshTransform,
                                                                          in TriangleCollider triangle,
                                                                          in RigidTransform triangleTransform,
                                                                          in ColliderDistanceResult distanceResult)
        {
            var a = Physics.ScaleStretchCollider(triMesh.triMeshColliderBlob.Value.triangles[distanceResult.subColliderIndexA], 1f, triMesh.scale);
            return TriangleTriangle.UnityContactsBetween(in a, in triMeshTransform, in triangle, in triangleTransform, in distanceResult);
        }

        struct TriangleDistanceProcessor : TriMeshColliderBlob.IFindTrianglesProcessor
        {
            public BlobAssetReference<TriMeshColliderBlob> blob;
            public TriangleCollider                        triangle;
            public RigidTransform                          triangleTransform;
            public float3                                  scale;
            public float                                   maxDistance;
            public float                                   bestDistance;
            public int                                     bestIndex;
            public bool                                    found;

            public bool Execute(int index)
            {
                var triangle2 = Physics.ScaleStretchCollider(blob.Value.triangles[index], 1f, scale);
                if (TriangleTriangle.DistanceBetween(in triangle2, in RigidTransform.identity, in triangle, in triangleTransform, maxDistance, out var hit))
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
            public TriMeshCollider  triMesh;
            public RigidTransform   triMeshTransform;
            public TriangleCollider triangle;
            public RigidTransform   triangleTransform;
            public float            maxDistance;
            public T*               processor;

            public bool Execute(int index)
            {
                var triangle2 = Physics.ScaleStretchCollider(triMesh.triMeshColliderBlob.Value.triangles[index], 1f, triMesh.scale);
                if (TriangleTriangle.DistanceBetween(in triangle2, in triMeshTransform, in triangle, in triangleTransform, maxDistance, out var result))
                {
                    result.subColliderIndexA = index;
                    processor->Execute(in result);
                }
                return true;
            }
        }

        internal struct CastProcessor : TriMeshColliderBlob.IFindTrianglesProcessor
        {
            public BlobAssetReference<TriMeshColliderBlob> blob;
            public TriangleCollider                        triangle;
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
                var triangle2 = Physics.ScaleStretchCollider(blob.Value.triangles[index], 1f, scale);
                // Check that we don't start already intersecting.
                if (TriangleTriangle.DistanceBetween(in triangle, in targetTransform, in triangle2, in castStart, 0f, out _))
                {
                    invalid = true;
                    return false;
                }
                if (TriangleTriangle.ColliderCast(in triangle, in castStart, castEnd, in triangle2, in targetTransform, out var hit))
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

