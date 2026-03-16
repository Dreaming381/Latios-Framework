using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Latios.Calligraphics.Authoring
{
    public static class TextBackendBakingUtility
    {
        public const string kTextBackendMeshPath     = "Packages/com.latios.latiosframework/Calligraphics/Resources/LatiosTextBackendMesh.mesh";
        public const string kTextBackendMeshResource = "LatiosTextBackendMesh";

        #region Mesh Building
#if UNITY_EDITOR
        //[UnityEditor.MenuItem("Calligraphics/Text BackendMesh")]
        static void CreateMeshAsset()
        {
            var glyphCounts = new NativeArray<int>(10, Allocator.Temp);
            glyphCounts[0] = 4;
            glyphCounts[1] = 8;
            glyphCounts[2] = 16;
            glyphCounts[3] = 24;
            glyphCounts[4] = 32;
            glyphCounts[5] = 64;
            glyphCounts[6] = 256;
            glyphCounts[7] = 1024;
            glyphCounts[8] = 4096;
            glyphCounts[9] = 16384;

            var mesh = CreateMesh(16384, glyphCounts);
            UnityEditor.AssetDatabase.CreateAsset(mesh, kTextBackendMeshPath);
        }
#endif

        struct Vertex
        {
            public float2 position;
            //public float3 normal;
            //public float4 tangent;

            public Vertex(float positionX, float positionY)
            {
                this.position = new float2(positionX, positionY);
                //normal        = new float3(0f, 0f, 1f);
                //tangent       = new float4(1f, 0f, 0f, 0f);
            }
        }

        internal static unsafe Mesh CreateMesh(int glyphCount, NativeArray<int> glyphCountsBySubmesh)
        {
            Mesh mesh       = new Mesh();
            var  mda        = Mesh.AllocateWritableMeshData(1);
            var  meshData   = mda[0];
            var  attributes = new NativeArray<VertexAttributeDescriptor>(1, Allocator.Temp);
            // Todo: Can we reduce the size to a 16-bit value in a cross-platform way?
            attributes[0] = new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 2, 0);
            //attributes[1] = new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, 0);
            //attributes[2] = new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.Float32, 4, 0);
            meshData.SetVertexBufferParams(glyphCount * 4, attributes);
            var vertices = meshData.GetVertexData<Vertex>();
            for (int i = 0; i < glyphCount; i++)
            {
                var columnBase      = (i % 256) * 2;
                var rowBase         = (i / 256) * 2;
                vertices[4 * i]     = new Vertex(columnBase, rowBase);
                vertices[4 * i + 1] = new Vertex(columnBase, rowBase + 1);
                vertices[4 * i + 2] = new Vertex(columnBase + 1, rowBase + 1);
                vertices[4 * i + 3] = new Vertex(columnBase + 1, rowBase);
            }

            meshData.SetIndexBufferParams(glyphCount * 6, IndexFormat.UInt16);
            meshData.subMeshCount = glyphCountsBySubmesh.Length;
            for (int i = 0; i < glyphCountsBySubmesh.Length; i++)
            {
                meshData.SetSubMesh(i, new SubMeshDescriptor(0, glyphCountsBySubmesh[i] * 6), MeshUpdateFlags.DontValidateIndices);
            }
            var indices = meshData.GetIndexData<ushort>();
            for (int i = 0; i < glyphCount; i++)
            {
                int    dst       = i * 6;
                ushort src       = (ushort)(i * 4);
                indices[dst]     = src;
                indices[dst + 1] = (ushort)(src + 1);
                indices[dst + 2] = (ushort)(src + 2);
                indices[dst + 3] = (ushort)(src + 2);
                indices[dst + 4] = (ushort)(src + 3);
                indices[dst + 5] = src;
            }
            Mesh.ApplyAndDisposeWritableMeshData(mda, mesh);
            //mesh.RecalculateBounds();
            mesh.UploadMeshData(true);

            return mesh;
        }
        #endregion
    }
}

