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
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    public struct ConvexCollider
    {
        /// <summary>
        /// The blob asset containing the raw convex hull data
        /// </summary>
        [FieldOffset(24)] public BlobAssetReference<ConvexColliderBlob> convexColliderBlob;
        /// <summary>
        /// The premultiplied scale and stretch in local space
        /// </summary>
        [FieldOffset(0)] public float3 scale;

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
        // Note: Max vertices is 252, max faces is 252, and max vertices per face is 32
        public BlobArray<float>  verticesX;
        public BlobArray<float>  verticesY;
        public BlobArray<float>  verticesZ;
        public BlobArray<float3> vertexNormals;

        public BlobArray<IndexPair> vertexIndicesInEdges;
        public BlobArray<float3>    edgeNormals;

        // outward normals and distance to origin
        public BlobArray<float> facePlaneX;
        public BlobArray<float> facePlaneY;
        public BlobArray<float> facePlaneZ;
        public BlobArray<float> facePlaneDist;

        // xyz normal w, signed distance
        public BlobArray<float4> faceEdgeOutwardPlanes;

        public BlobArray<EdgeIndexInFace> edgeIndicesInFaces;
        public BlobArray<StartAndCount>   edgeIndicesInFacesStartsAndCounts;

        public BlobArray<IndexPair>     faceIndicesByEdge;
        public BlobArray<byte>          faceIndicesByVertex;
        public BlobArray<StartAndCount> faceIndicesByVertexStartsAndCounts;

        public BlobArray<byte> yz2DVertexIndices;
        public BlobArray<byte> xz2DVertexIndices;
        public BlobArray<byte> xy2DVertexIndices;

        public Aabb localAabb;

        public float3     centerOfMass;
        public float3x3   inertiaTensor;
        public quaternion unscaledInertiaTensorOrientation;
        public float3     unscaledInertiaTensorDiagonal;

        public FixedString128Bytes meshName;

        public struct IndexPair
        {
            public byte x;
            public byte y;
        }

        public struct EdgeIndexInFace
        {
            // 32 edges per face * 252 faces only requires 13 bits
            public ushort raw;
            public int index => raw & 0x7fff;
            public bool flipped => (raw & 0x8000) != 0;
        }

        public struct StartAndCount
        {
            // 32 vertices / edges per face * 252 faces only requires 13 bits, and the count only requires 6
            public ushort start;
            public byte   count;
        }
    }
}

