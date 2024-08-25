using Latios.Transforms;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class SphereTriMesh
    {
        public static bool DistanceBetween(in TriMeshCollider triMesh,
                                           in RigidTransform triMeshTransform,
                                           in SphereCollider sphere,
                                           in RigidTransform sphereTransform,
                                           float maxDistance,
                                           out ColliderDistanceResult result)
        {
            var transform      = new TransformQvvs(triMeshTransform.pos, triMeshTransform.rot, 1f, triMesh.scale);
            var pointInTriMesh = qvvs.InverseTransformPoint(in transform, math.transform(sphereTransform, sphere.center));
            var aabb           = Physics.AabbFrom(pointInTriMesh - maxDistance - sphere.radius, pointInTriMesh + maxDistance + sphere.radius);
            var processor      = new PointRayTriMesh.PointProcessor
            {
                blob         = triMesh.triMeshColliderBlob,
                point        = pointInTriMesh,
                maxDistance  = maxDistance + sphere.radius,
                bestDistance = float.MaxValue,
                found        = false
            };
            triMesh.triMeshColliderBlob.Value.FindTriangles(in aabb, ref processor, triMesh.scale);
            if (processor.found)
            {
                var hitTriangle          = Physics.ScaleStretchCollider(triMesh.triMeshColliderBlob.Value.triangles[processor.bestIndex], 1f, triMesh.scale);
                var hit                  = SphereTriangle.DistanceBetween(in hitTriangle, in triMeshTransform, in sphere, in sphereTransform, maxDistance, out result);
                result.subColliderIndexA = processor.bestIndex;
                return hit;
            }
            result = default;
            return false;
        }

        public static unsafe void DistanceBetweenAll<T>(in TriMeshCollider triMesh,
                                                        in RigidTransform triMeshTransform,
                                                        in SphereCollider sphere,
                                                        in RigidTransform sphereTransform,
                                                        float maxDistance,
                                                        ref T processor) where T : unmanaged, IDistanceBetweenAllProcessor
        {
            var sphereInTriMeshTransform  = math.mul(math.inverse(triMeshTransform), sphereTransform);
            var aabb                      = Physics.AabbFrom(sphere, sphereInTriMeshTransform);
            aabb.min                     -= maxDistance;
            aabb.max                     += maxDistance;
            var triProcessor              = new DistanceAllProcessor<T>
            {
                triMesh          = triMesh,
                triMeshTransform = triMeshTransform,
                sphere           = sphere,
                sphereTransform  = sphereTransform,
                maxDistance      = maxDistance,
                processor        = (T*)UnsafeUtility.AddressOf(ref processor)
            };
            triMesh.triMeshColliderBlob.Value.FindTriangles(in aabb, ref triProcessor, triMesh.scale);
        }

        public static bool ColliderCast(in SphereCollider sphereToCast,
                                        in RigidTransform castStart,
                                        float3 castEnd,
                                        in TriMeshCollider targetTriMesh,
                                        in RigidTransform targetTriMeshTransform,
                                        out ColliderCastResult result)
        {
            var targetTriMeshTransformInverse  = math.inverse(targetTriMeshTransform);
            var casterInTargetSpace            = math.mul(targetTriMeshTransformInverse, castStart);
            var start                          = math.transform(casterInTargetSpace, sphereToCast.center);
            var ray                            = new Ray(start, start + math.rotate(targetTriMeshTransformInverse, castEnd - castStart.pos));
            var aabb                           = Physics.AabbFrom(ray.start, ray.end);
            aabb.min                          -= sphereToCast.radius;
            aabb.max                          += sphereToCast.radius;
            var processor                      = new CastProcessor
            {
                blob    = targetTriMesh.triMeshColliderBlob,
                found   = false,
                invalid = false,
                radius  = sphereToCast.radius,
                ray     = ray,
                scale   = targetTriMesh.scale
            };
            targetTriMesh.triMeshColliderBlob.Value.FindTriangles(in aabb, ref processor, targetTriMesh.scale);

            if (processor.invalid || !processor.found)
            {
                result = default;
                return false;
            }

            var hitTransform = castStart;
            hitTransform.pos = math.lerp(castStart.pos, castEnd, processor.bestFraction);
            var hitTriangle  = Physics.ScaleStretchCollider(targetTriMesh.triMeshColliderBlob.Value.triangles[processor.bestIndex], 1f, targetTriMesh.scale);
            SphereTriangle.DistanceBetween(in hitTriangle,
                                           in targetTriMeshTransform,
                                           in sphereToCast,
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
                                        in SphereCollider targetSphere,
                                        in RigidTransform targetSphereTransform,
                                        out ColliderCastResult result)
        {
            var castReverse         = castStart.pos - castEnd;
            var worldToCasterSpace  = math.inverse(castStart);
            var start               = math.transform(targetSphereTransform, targetSphere.center);
            var ray                 = new Ray(math.transform(worldToCasterSpace, start), math.transform(worldToCasterSpace, start + castReverse));
            var aabb                = Physics.AabbFrom(ray.start, ray.end);
            aabb.min               -= targetSphere.radius;
            aabb.max               += targetSphere.radius;
            var processor           = new CastProcessor
            {
                blob    = triMeshToCast.triMeshColliderBlob,
                found   = false,
                invalid = false,
                radius  = targetSphere.radius,
                ray     = ray,
                scale   = triMeshToCast.scale
            };
            triMeshToCast.triMeshColliderBlob.Value.FindTriangles(in aabb, ref processor, triMeshToCast.scale);

            if (processor.invalid || !processor.found)
            {
                result = default;
                return false;
            }

            var hitTransform = castStart;
            hitTransform.pos = math.lerp(castStart.pos, castEnd, processor.bestFraction);
            var hitTriangle  = Physics.ScaleStretchCollider(triMeshToCast.triMeshColliderBlob.Value.triangles[processor.bestIndex], 1f, triMeshToCast.scale);
            SphereTriangle.DistanceBetween(in hitTriangle,
                                           in hitTransform,
                                           in targetSphere,
                                           in targetSphereTransform,
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
                                                                          in SphereCollider sphere,
                                                                          in RigidTransform sphereTransform,
                                                                          in ColliderDistanceResult distanceResult)
        {
            return ContactManifoldHelpers.GetSingleContactManifold(in distanceResult);
        }

        unsafe struct DistanceAllProcessor<T> : TriMeshColliderBlob.IFindTrianglesProcessor where T : unmanaged, IDistanceBetweenAllProcessor
        {
            public TriMeshCollider triMesh;
            public RigidTransform  triMeshTransform;
            public SphereCollider  sphere;
            public RigidTransform  sphereTransform;
            public float           maxDistance;
            public T*              processor;

            public bool Execute(int index)
            {
                var triangle = Physics.ScaleStretchCollider(triMesh.triMeshColliderBlob.Value.triangles[index], 1f, triMesh.scale);
                if (SphereTriangle.DistanceBetween(in triangle, in triMeshTransform, in sphere, in sphereTransform, maxDistance, out var result))
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
            public Ray                                     ray;
            public float3                                  scale;
            public float                                   radius;
            public float                                   bestFraction;
            public int                                     bestIndex;
            public bool                                    found;
            public bool                                    invalid;

            public bool Execute(int index)
            {
                var triangle = Physics.ScaleStretchCollider(blob.Value.triangles[index], 1f, scale);
                // Check that we don't start already intersecting.
                if (PointRayTriangle.PointTriangleDistance(ray.start, in triangle, radius, out _))
                {
                    invalid = true;
                    return false;
                }
                if (PointRayTriangle.RaycastRoundedTriangle(in ray, triangle.AsSimdFloat3(), radius, out var fraction, out _))
                {
                    if (!found || fraction < bestFraction)
                    {
                        found        = true;
                        bestFraction = fraction;
                        bestIndex    = index;
                    }
                }

                return true;
            }
        }
    }
}

