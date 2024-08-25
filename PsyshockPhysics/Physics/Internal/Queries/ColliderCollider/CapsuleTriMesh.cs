using Latios.Transforms;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class CapsuleTriMesh
    {
        public static bool DistanceBetween(in TriMeshCollider triMesh,
                                           in RigidTransform triMeshTransform,
                                           in CapsuleCollider capsule,
                                           in RigidTransform capsuleTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            var capInTriMeshTransform = math.mul(math.inverse(triMeshTransform), capsuleTransform);
            var capsuleInTriMesh      = new CapsuleCollider(math.transform(capInTriMeshTransform, capsule.pointA),
                                                            math.transform(capInTriMeshTransform, capsule.pointB),
                                                            capsule.radius);
            var aabb       = Physics.AabbFrom(capsuleInTriMesh, RigidTransform.identity);
            aabb.min      -= maxDistance;
            aabb.max      += maxDistance;
            var processor  = new CapsuleDistanceProcessor
            {
                blob         = triMesh.triMeshColliderBlob,
                capsule      = capsuleInTriMesh,
                maxDistance  = maxDistance,
                bestDistance = float.MaxValue,
                found        = false,
                scale        = triMesh.scale
            };
            triMesh.triMeshColliderBlob.Value.FindTriangles(in aabb, ref processor, triMesh.scale);
            if (processor.found)
            {
                var hitTriangle          = Physics.ScaleStretchCollider(triMesh.triMeshColliderBlob.Value.triangles[processor.bestIndex], 1f, triMesh.scale);
                var hit                  = CapsuleTriangle.DistanceBetween(in hitTriangle, in triMeshTransform, in capsule, in capsuleTransform, maxDistance, out result);
                result.subColliderIndexA = processor.bestIndex;
                return hit;
            }
            result = default;
            return false;
        }

        public static unsafe void DistanceBetweenAll<T>(in TriMeshCollider triMesh,
                                                        in RigidTransform triMeshTransform,
                                                        in CapsuleCollider capsule,
                                                        in RigidTransform capsuleTransform,
                                                        float maxDistance,
                                                        ref T processor) where T : unmanaged, IDistanceBetweenAllProcessor
        {
            var capsuleInTriMeshTransform  = math.mul(math.inverse(triMeshTransform), capsuleTransform);
            var aabb                       = Physics.AabbFrom(capsule, capsuleInTriMeshTransform);
            aabb.min                      -= maxDistance;
            aabb.max                      += maxDistance;
            var triProcessor               = new DistanceAllProcessor<T>
            {
                triMesh          = triMesh,
                triMeshTransform = triMeshTransform,
                capsule          = capsule,
                capsuleTransform = capsuleTransform,
                maxDistance      = maxDistance,
                processor        = (T*)UnsafeUtility.AddressOf(ref processor)
            };
            triMesh.triMeshColliderBlob.Value.FindTriangles(in aabb, ref triProcessor, triMesh.scale);
        }

        public static bool ColliderCast(in CapsuleCollider capsuleToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in TriMeshCollider targetTriMesh,
                                        in RigidTransform targetTriMeshTransform,
                                        out ColliderCastResult result)
        {
            var targetTriMeshTransformInverse = math.inverse(targetTriMeshTransform);
            var casterInTargetSpace           = math.mul(targetTriMeshTransformInverse, castStart);
            var aabb                          =
                Physics.AabbFrom(capsuleToCast, in casterInTargetSpace, casterInTargetSpace.pos + math.rotate(targetTriMeshTransformInverse, castEnd - castStart.pos));
            var processor = new CastProcessor
            {
                blob            = targetTriMesh.triMeshColliderBlob,
                capsule         = capsuleToCast,
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
            CapsuleTriangle.DistanceBetween(in hitTriangle,
                                            in targetTriMeshTransform,
                                            in capsuleToCast,
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
                                        in CapsuleCollider targetCapsule,
                                        in RigidTransform targetCapsuleTransform,
                                        out ColliderCastResult result)
        {
            var castReverse         = castStart.pos - castEnd;
            var worldToCasterSpace  = math.inverse(castStart);
            var targetInCasterSpace = math.mul(worldToCasterSpace, targetCapsuleTransform);
            var reverseCastEnd      = targetInCasterSpace.pos + math.rotate(worldToCasterSpace, castReverse);
            var aabb                = Physics.AabbFrom(targetCapsule, in targetInCasterSpace, reverseCastEnd);
            var processor           = new CastProcessor
            {
                blob            = triMeshToCast.triMeshColliderBlob,
                capsule         = targetCapsule,
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
            CapsuleTriangle.DistanceBetween(in hitTriangle,
                                            in hitTransform,
                                            in targetCapsule,
                                            in targetCapsuleTransform,
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
                                                                          in CapsuleCollider capsule,
                                                                          in RigidTransform capsuleTransform,
                                                                          in ColliderDistanceResult distanceResult)
        {
            var triangle = Physics.ScaleStretchCollider(triMesh.triMeshColliderBlob.Value.triangles[distanceResult.subColliderIndexA], 1f, triMesh.scale);
            return CapsuleTriangle.UnityContactsBetween(in triangle, in triMeshTransform, in capsule, in capsuleTransform, in distanceResult);
        }

        struct CapsuleDistanceProcessor : TriMeshColliderBlob.IFindTrianglesProcessor
        {
            public BlobAssetReference<TriMeshColliderBlob> blob;
            public CapsuleCollider                         capsule;
            public float3                                  scale;
            public float                                   maxDistance;
            public float                                   bestDistance;
            public int                                     bestIndex;
            public bool                                    found;

            public bool Execute(int index)
            {
                var triangle = Physics.ScaleStretchCollider(blob.Value.triangles[index], 1f, scale);
                if (CapsuleTriangle.TriangleCapsuleDistance(in triangle, in capsule, maxDistance, out var hit))
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
            public CapsuleCollider capsule;
            public RigidTransform  capsuleTransform;
            public float           maxDistance;
            public T*              processor;

            public bool Execute(int index)
            {
                var triangle = Physics.ScaleStretchCollider(triMesh.triMeshColliderBlob.Value.triangles[index], 1f, triMesh.scale);
                if (CapsuleTriangle.DistanceBetween(in triangle, in triMeshTransform, in capsule, in capsuleTransform, maxDistance, out var result))
                {
                    result.subColliderIndexA = index;
                    processor->Execute(in result);
                }
                return true;
            }
        }

        struct CastProcessor : TriMeshColliderBlob.IFindTrianglesProcessor
        {
            public BlobAssetReference<TriMeshColliderBlob> blob;
            public CapsuleCollider                         capsule;
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
                if (CapsuleTriangle.DistanceBetween(in triangle, in targetTransform, in capsule, in castStart, 0f, out _))
                {
                    invalid = true;
                    return false;
                }
                if (CapsuleTriangle.ColliderCast(in capsule, in castStart, castEnd, in triangle, in targetTransform, out var hit))
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

