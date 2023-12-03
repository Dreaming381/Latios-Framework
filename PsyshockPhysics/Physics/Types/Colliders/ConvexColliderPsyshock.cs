using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    /// <summary>
    /// A convex hull collider shape that can be scaled and stretched in local space efficiently.
    /// It is often derived from a Mesh.
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Explicit)]
    public struct ConvexCollider
    {
        /// <summary>
        /// The blob asset containing the raw convex hull data
        /// </summary>
        [FieldOffset(0)] public BlobAssetReference<ConvexColliderBlob> convexColliderBlob;
        /// <summary>
        /// The premultiplied scale and stretch in local space
        /// </summary>
        [FieldOffset(8)] public float3 scale;

        /// <summary>
        /// Creates a new ConvexCollider
        /// </summary>
        /// <param name="convexColliderBlob">The blob asset containing the raw convex hull data</param>
        public ConvexCollider(BlobAssetReference<ConvexColliderBlob> convexColliderBlob) : this(convexColliderBlob, new float3(1f, 1f, 1f))
        {
        }

        /// <summary>
        /// Creates a new ConvexCollider
        /// </summary>
        /// <param name="convexColliderBlob">The blob asset containing the raw convex hull data</param>
        /// <param name="scale">The premultiplied scale and stretch in local space</param>
        public ConvexCollider(BlobAssetReference<ConvexColliderBlob> convexColliderBlob, float3 scale)
        {
            this.convexColliderBlob = convexColliderBlob;
            this.scale              = scale;
        }
    }

    /// <summary>
    /// A definition of a raw baked convex hull. This definition is designed for SIMD operations
    /// and direct traversal is only recommended for advanced users.
    /// </summary>
    public struct ConvexColliderBlob
    {
        public BlobArray<float>  verticesX;
        public BlobArray<float>  verticesY;
        public BlobArray<float>  verticesZ;
        public BlobArray<float3> vertexNormals;

        public BlobArray<int2>   vertexIndicesInEdges;
        public BlobArray<float3> edgeNormals;

        // outward normals and distance to origin
        public BlobArray<float> facePlaneX;
        public BlobArray<float> facePlaneY;
        public BlobArray<float> facePlaneZ;
        public BlobArray<float> facePlaneDist;

        // xyz normal w, signed distance
        public BlobArray<float4> faceEdgeOutwardPlanes;

        public BlobArray<int>  edgeIndicesInFaces;
        public BlobArray<int2> edgeIndicesInFacesStartsAndCounts;

        public BlobArray<int2> faceIndicesByEdge;
        public BlobArray<int>  faceIndicesByVertex;
        public BlobArray<int2> faceIndicesByVertexStartsAndCounts;

        public Aabb localAabb;

        public FixedString128Bytes meshName;
    }
}

