using Latios.Transforms;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class TriMeshTriMesh
    {
        public static bool DistanceBetween(in TriMeshCollider triMeshA,
                                           in RigidTransform aTransform,
                                           in TriMeshCollider triMeshB,
                                           in RigidTransform bTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            var transformAinB = math.mul(math.inverse(bTransform), aTransform);
            var transformBinA = math.mul(math.inverse(aTransform), bTransform);
            var aabbAinB      =
                Physics.TransformAabb(new TransformQvvs(transformAinB.pos, transformAinB.rot, 1f, triMeshA.scale), in triMeshA.triMeshColliderBlob.Value.localAabb);
            var aabbBinA =
                Physics.TransformAabb(new TransformQvvs(transformBinA.pos, transformBinA.rot, 1f, triMeshB.scale), in triMeshB.triMeshColliderBlob.Value.localAabb);
            aabbAinB.min -= maxDistance;
            aabbAinB.max += maxDistance;
            aabbBinA.min -= maxDistance;
            aabbBinA.max += maxDistance;

            var processor = new TriMeshDistanceOuterProcessor
            {
                innerProcessor = new TriMeshDistanceInnerProcessor
                {
                    blobA         = triMeshA.triMeshColliderBlob,
                    maxDistance   = maxDistance,
                    bestDistance  = float.MaxValue,
                    scaleA        = triMeshA.scale,
                    transformBinA = transformBinA,
                },
                aabbBinA     = aabbBinA,
                blobB        = triMeshB.triMeshColliderBlob,
                bestDistance = float.MaxValue,
                found        = false,
                scaleB       = triMeshB.scale,
            };

            triMeshB.triMeshColliderBlob.Value.FindTriangles(in aabbAinB, ref processor, triMeshB.scale);
            if (processor.found)
            {
                var hitA                 = Physics.ScaleStretchCollider(triMeshA.triMeshColliderBlob.Value.triangles[processor.bestIndexA], 1f, triMeshA.scale);
                var hitB                 = Physics.ScaleStretchCollider(triMeshB.triMeshColliderBlob.Value.triangles[processor.bestIndexB], 1f, triMeshB.scale);
                var hit                  = TriangleTriangle.DistanceBetween(in hitA, in aTransform, in hitB, in bTransform, maxDistance, out result);
                result.subColliderIndexA = processor.bestIndexA;
                result.subColliderIndexB = processor.bestIndexB;
                return hit;
            }
            result = default;
            return false;
        }

        public static unsafe void DistanceBetweenAll<T>(in TriMeshCollider triMeshA,
                                                        in RigidTransform aTransform,
                                                        in TriMeshCollider triMeshB,
                                                        in RigidTransform bTransform,
                                                        float maxDistance,
                                                        ref T processor) where T : unmanaged, IDistanceBetweenAllProcessor
        {
            var transformAinB = math.mul(math.inverse(bTransform), aTransform);
            var transformBinA = math.mul(math.inverse(aTransform), bTransform);
            var aabbAinB      =
                Physics.TransformAabb(new TransformQvvs(transformAinB.pos, transformAinB.rot, 1f, triMeshA.scale), in triMeshA.triMeshColliderBlob.Value.localAabb);
            var aabbBinA =
                Physics.TransformAabb(new TransformQvvs(transformBinA.pos, transformBinA.rot, 1f, triMeshB.scale), in triMeshB.triMeshColliderBlob.Value.localAabb);
            aabbAinB.min *= maxDistance;
            aabbAinB.max *= maxDistance;
            aabbBinA.min *= maxDistance;
            aabbBinA.max *= maxDistance;

            var outerProcessor = new DistanceAllOuterProcessor<T>
            {
                processor = new DistanceAllInnerProcessor<T>
                {
                    maxDistance = maxDistance,
                    processor   = (T*)UnsafeUtility.AddressOf(ref processor),
                    transformA  = aTransform,
                    transformB  = bTransform,
                    triMeshA    = triMeshA
                },
                aabbBinA   = aabbBinA,
                transformB = bTransform,
                triMeshB   = triMeshB
            };

            triMeshB.triMeshColliderBlob.Value.FindTriangles(in aabbAinB, ref outerProcessor, triMeshB.scale);
        }

        public static bool ColliderCast(in TriMeshCollider triMeshToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in TriMeshCollider targetTriMesh,
                                        in RigidTransform targetTriMeshTransform,
                                        out ColliderCastResult result)
        {
            var targetTriMeshTransformInverse = math.inverse(targetTriMeshTransform);
            var casterInTargetSpace           = math.mul(targetTriMeshTransformInverse, castStart);
            var aabb                          =
                Physics.AabbFrom(triMeshToCast, in casterInTargetSpace, casterInTargetSpace.pos + math.rotate(targetTriMeshTransformInverse, castEnd - castStart.pos));
            var processor = new TriangleTriMesh.CastProcessor
            {
                blob = targetTriMesh.triMeshColliderBlob,
                //triangle = triangleToCast,
                castEnd   = castEnd,
                castStart = castStart,
                //found = false,
                invalid         = false,
                targetTransform = targetTriMeshTransform,
                scale           = targetTriMesh.scale
            };
            ref var trianglesA      = ref triMeshToCast.triMeshColliderBlob.Value.triangles;
            float   bestDistance    = 0f;
            int     bestIndexCast   = -1;
            int     bestIndexTarget = 0;
            for (int i = 0; i < trianglesA.Length; i++)
            {
                processor.triangle = Physics.ScaleStretchCollider(trianglesA[i], 1f, triMeshToCast.scale);
                processor.found    = false;
                targetTriMesh.triMeshColliderBlob.Value.FindTriangles(in aabb, ref processor, targetTriMesh.scale);
                if (processor.invalid)
                {
                    result = default;
                    return false;
                }
                if (bestIndexCast < 0 || processor.bestDistance < bestDistance)
                {
                    bestDistance    = processor.bestDistance;
                    bestIndexCast   = i;
                    bestIndexTarget = 0;
                }
            }

            if (bestIndexCast < 0)
            {
                result = default;
                return false;
            }

            var hitTransform  = castStart;
            hitTransform.pos += math.normalize(castEnd - castStart.pos) * processor.bestDistance;
            var castTriangle  = Physics.ScaleStretchCollider(triMeshToCast.triMeshColliderBlob.Value.triangles[bestIndexCast], 1f, triMeshToCast.scale);
            var hitTriangle   = Physics.ScaleStretchCollider(targetTriMesh.triMeshColliderBlob.Value.triangles[bestIndexTarget], 1f, targetTriMesh.scale);
            TriangleTriangle.DistanceBetween(in castTriangle,
                                             in hitTransform,
                                             in hitTriangle,
                                             in targetTriMeshTransform,
                                             1f,
                                             out var distanceResult);
            result = new ColliderCastResult
            {
                hitpoint                 = distanceResult.hitpointB,
                normalOnCaster           = distanceResult.normalA,
                normalOnTarget           = distanceResult.normalB,
                subColliderIndexOnCaster = bestIndexCast,
                subColliderIndexOnTarget = bestIndexTarget,
                distance                 = math.distance(hitTransform.pos, castStart.pos)
            };
            return true;
        }

        public static UnitySim.ContactsBetweenResult UnityContactsBetween(in TriMeshCollider triMeshA,
                                                                          in RigidTransform aTransform,
                                                                          in TriMeshCollider triMeshB,
                                                                          in RigidTransform bTransform,
                                                                          in ColliderDistanceResult distanceResult)
        {
            var a = Physics.ScaleStretchCollider(triMeshA.triMeshColliderBlob.Value.triangles[distanceResult.subColliderIndexA], 1f, triMeshA.scale);
            var b = Physics.ScaleStretchCollider(triMeshB.triMeshColliderBlob.Value.triangles[distanceResult.subColliderIndexB], 1f, triMeshB.scale);
            return TriangleTriangle.UnityContactsBetween(in a, in aTransform, in b, in bTransform, in distanceResult);
        }

        struct TriMeshDistanceInnerProcessor : TriMeshColliderBlob.IFindTrianglesProcessor
        {
            public BlobAssetReference<TriMeshColliderBlob> blobA;
            public float3                                  scaleA;
            public TriangleCollider                        triangleB;
            public RigidTransform                          transformBinA;
            public float                                   maxDistance;
            public float                                   bestDistance;
            public int                                     bestIndex;
            public bool                                    found;

            public bool Execute(int index)
            {
                var triangleA = Physics.ScaleStretchCollider(blobA.Value.triangles[index], 1f, scaleA);
                if (TriangleTriangle.DistanceBetween(in triangleA, in RigidTransform.identity, in triangleB, in transformBinA, maxDistance, out var hit))
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

        struct TriMeshDistanceOuterProcessor : TriMeshColliderBlob.IFindTrianglesProcessor
        {
            public TriMeshDistanceInnerProcessor           innerProcessor;
            public BlobAssetReference<TriMeshColliderBlob> blobB;
            public float3                                  scaleB;
            public Aabb                                    aabbBinA;
            public float                                   bestDistance;
            public int                                     bestIndexA;
            public int                                     bestIndexB;
            public bool                                    found;

            public bool Execute(int index)
            {
                innerProcessor.triangleB = Physics.ScaleStretchCollider(blobB.Value.triangles[index], 1f, scaleB);
                innerProcessor.found     = false;
                innerProcessor.blobA.Value.FindTriangles(in aabbBinA, ref innerProcessor, scaleB);
                if (innerProcessor.found)
                {
                    if (!found || innerProcessor.bestDistance < bestDistance)
                    {
                        found        = true;
                        bestDistance = innerProcessor.bestDistance;
                        bestIndexA   = innerProcessor.bestIndex;
                        bestIndexB   = index;
                    }
                }

                return true;
            }
        }

        unsafe struct DistanceAllInnerProcessor<T> : TriMeshColliderBlob.IFindTrianglesProcessor where T : unmanaged, IDistanceBetweenAllProcessor
        {
            public TriMeshCollider  triMeshA;
            public RigidTransform   transformA;
            public TriangleCollider triangleB;
            public RigidTransform   transformB;
            public int              bIndex;
            public float            maxDistance;
            public T*               processor;

            public bool Execute(int index)
            {
                var triangleA = Physics.ScaleStretchCollider(triMeshA.triMeshColliderBlob.Value.triangles[index], 1f, triMeshA.scale);
                if (TriangleTriangle.DistanceBetween(in triangleA, in transformA, in triangleB, in transformB, maxDistance, out var hit))
                {
                    hit.subColliderIndexA = index;
                    hit.subColliderIndexB = bIndex;
                    processor->Execute(in hit);
                }

                return true;
            }
        }

        unsafe struct DistanceAllOuterProcessor<T> : TriMeshColliderBlob.IFindTrianglesProcessor where T : unmanaged, IDistanceBetweenAllProcessor
        {
            public TriMeshCollider              triMeshB;
            public RigidTransform               transformB;
            public Aabb                         aabbBinA;
            public DistanceAllInnerProcessor<T> processor;

            public bool Execute(int index)
            {
                processor.triangleB = Physics.ScaleStretchCollider(triMeshB.triMeshColliderBlob.Value.triangles[index], 1f, triMeshB.scale);
                processor.bIndex    = index;
                processor.triMeshA.triMeshColliderBlob.Value.FindTriangles(in aabbBinA, ref processor, triMeshB.scale);

                return true;
            }
        }
    }
}

