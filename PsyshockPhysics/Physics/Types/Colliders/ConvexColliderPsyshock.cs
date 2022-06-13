using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Psyshock
{
    [Serializable]
    public struct ConvexCollider
    {
        public BlobAssetReference<ConvexColliderBlob> convexColliderBlob;
        public float3                                 scale;

        public ConvexCollider(BlobAssetReference<ConvexColliderBlob> convexColliderBlob) : this(convexColliderBlob, new float3(1f, 1f, 1f)) {
        }

        public ConvexCollider(BlobAssetReference<ConvexColliderBlob> convexColliderBlob, float3 scale)
        {
            this.convexColliderBlob = convexColliderBlob;
            this.scale              = scale;
        }
    }

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

        public Aabb localAabb;

        public FixedString128Bytes meshName;
    }
}

