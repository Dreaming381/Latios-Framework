using Latios.Transforms;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    internal static class PointRayTriMesh
    {
        public static bool DistanceBetween(float3 point, in TriMeshCollider triMesh, in RigidTransform triMeshTransform, float maxDistance, out PointDistanceResult result)
        {
            var transform      = new TransformQvvs(triMeshTransform.pos, triMeshTransform.rot, 1f, triMesh.scale);
            var pointInTriMesh = qvvs.InverseTransformPoint(in transform, point);
            var aabb           = Physics.AabbFrom(pointInTriMesh - maxDistance, pointInTriMesh + maxDistance);
            var processor      = new PointProcessor
            {
                blob        = triMesh.triMeshColliderBlob,
                point       = pointInTriMesh,
                maxDistance = maxDistance,
                found       = false
            };
            triMesh.triMeshColliderBlob.Value.FindTriangles(in aabb, ref processor, triMesh.scale);
            if (processor.found)
            {
                var hit =
                    PointRayTriangle.DistanceBetween(point, in triMesh.triMeshColliderBlob.Value.triangles[processor.bestIndex], in triMeshTransform, maxDistance, out result);
                result.subColliderIndex = processor.bestIndex;
                return hit;
            }
            result = default;
            return false;
        }

        public static bool Raycast(in Ray ray, in TriMeshCollider triMesh, in RigidTransform triMeshTransform, out RaycastResult result)
        {
            var transform         = new TransformQvvs(triMeshTransform.pos, triMeshTransform.rot, 1f, triMesh.scale);
            var rayInTriMeshSpace = new Ray(qvvs.TransformPoint(in transform, ray.start), qvvs.TransformPoint(in transform, ray.end));
            var aabb              = Physics.AabbFrom(in rayInTriMeshSpace);
            var processor         = new RayProcessor
            {
                blob  = triMesh.triMeshColliderBlob,
                ray   = rayInTriMeshSpace,
                found = false
            };
            triMesh.triMeshColliderBlob.Value.FindTriangles(in aabb, ref processor, triMesh.scale);
            if (processor.found)
            {
                var hit                 = PointRayTriangle.Raycast(in ray, in triMesh.triMeshColliderBlob.Value.triangles[processor.bestIndex], in triMeshTransform, out result);
                result.subColliderIndex = processor.bestIndex;
                return hit;
            }
            result = default;
            return false;
        }

        internal struct PointProcessor : TriMeshColliderBlob.IFindTrianglesProcessor
        {
            public BlobAssetReference<TriMeshColliderBlob> blob;
            public float3                                  point;
            public float                                   maxDistance;
            public float                                   bestDistance;
            public int                                     bestIndex;
            public bool                                    found;

            public bool Execute(int index)
            {
                if (PointRayTriangle.PointTriangleDistance(point, in blob.Value.triangles[index], maxDistance, out var hit))
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

        struct RayProcessor : TriMeshColliderBlob.IFindTrianglesProcessor
        {
            public BlobAssetReference<TriMeshColliderBlob> blob;
            public Ray                                     ray;
            public float                                   bestFraction;
            public int                                     bestIndex;
            public bool                                    found;

            public bool Execute(int index)
            {
                if (PointRayTriangle.RaycastTriangle(in ray, blob.Value.triangles[index].AsSimdFloat3(), out var fraction, out _))
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

