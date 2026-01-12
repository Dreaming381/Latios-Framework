using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Properties;
using Unity.Rendering;

namespace Latios.Kinemation
{
    [MaterialProperty("_DecalAngleFade")]
    public struct DecalAngleFade : IComponentData
    {
        [CreateProperty(ReadOnly = true)]
        internal float2 fadeValues;

        /// <summary>
        /// Creates a DecalAngleFade designed for HDRP.
        /// </summary>
        /// <param name="minAngleRadians">The minimum angle difference between the decal receiver 
        /// and the negative direction of the decal projection at which the decal starts to fade out.
        /// [0, PI], though typically [0, PI/2]</param>
        /// <param name="maxAngleRadians">The maximum angle difference between the decal receiver
        /// and the negative direction of the decal projection at which the decal is fully faded out.
        /// [0, PI], though typically [0, PI/2]</param>
        /// <returns>A DecalAngleFade material property override.</returns>
        public static DecalAngleFade CreateForHdrp(float minAngleRadians, float maxAngleRadians)
        {
            var angles = new float2(minAngleRadians, maxAngleRadians);
            angles = math.clamp(angles, 0f, math.PI) / math.PI;
            var range = math.max(0.0001f, angles.y - angles.x);
            angles.x = 0.222222222f / range;
            angles.y = (angles.y - 0.5f) / range;
            return new DecalAngleFade { fadeValues = angles };
        }

        /// <summary>
        /// Creates a DecalAngleFade designed for URP.
        /// </summary>
        /// <param name="minAngleRadians">The minimum angle difference between the decal receiver 
        /// and the negative direction of the decal projection at which the decal starts to fade out.
        /// [0, PI], though typically [0, PI/2]</param>
        /// <param name="maxAngleRadians">The maximum angle difference between the decal receiver
        /// and the negative direction of the decal projection at which the decal is fully faded out.
        /// [0, PI], though typically [0, PI/2]</param>
        /// <returns>A DecalAngleFade material property override.</returns>
        public static DecalAngleFade CreateForUrp(float minAngleRadians, float maxAngleRadians)
        {
            var angles = new float2(minAngleRadians, maxAngleRadians);
            angles = math.clamp(angles, 0f, math.PI) / math.PI;
            var range = math.max(0.0001f, angles.y - angles.x);
            angles.x = 1.0f - (0.25f - angles.x) / range;
            angles.y = -0.25f / range;
            return new DecalAngleFade { fadeValues = angles };
        }


#if HDRP_10_0_0_OR_NEWER && !URP_10_0_0_OR_NEWER
        /// <summary>
        /// Creates a DecalAngleFade designed for the current render pipeline (HDRP).
        /// </summary>
        /// <param name="minAngleRadians">The minimum angle difference between the decal receiver 
        /// and the negative direction of the decal projection at which the decal starts to fade out.
        /// [0, PI], though typically [0, PI/2]</param>
        /// <param name="maxAngleRadians">The maximum angle difference between the decal receiver
        /// and the negative direction of the decal projection at which the decal is fully faded out.
        /// [0, PI], though typically [0, PI/2]</param>
        /// <returns>A DecalAngleFade material property override.</returns>
        public DecalAngleFade(float minAngleRadians, float maxAngleRadians)
        {
            this = CreateForHdrp(minAngleRadians, maxAngleRadians);
        }
#elif !HDRP_10_0_0_OR_NEWER && URP_10_0_0_OR_NEWER
        /// <summary>
        /// Creates a DecalAngleFade designed for the current render pipeline (URP).
        /// </summary>
        /// <param name="minAngleRadians">The minimum angle difference between the decal receiver 
        /// and the negative direction of the decal projection at which the decal starts to fade out.
        /// [0, PI], though typically [0, PI/2]</param>
        /// <param name="maxAngleRadians">The maximum angle difference between the decal receiver
        /// and the negative direction of the decal projection at which the decal is fully faded out.
        /// [0, PI], though typically [0, PI/2]</param>
        /// <returns>A DecalAngleFade material property override.</returns>
        public DecalAngleFade(float minAngleRadians, float maxAngleRadians)
        {
            this = CreateForUrp(minAngleRadians, maxAngleRadians);
        }
#endif
    }

    internal static class DecalMeshCreator
    {
        public const string kOrthographicDecalMeshPath = "Packages/com.latios.latiosframework/Kinemation/Resources/OrthographicDecalMesh.mesh";
        public const string kOrthographicDecalMeshResource = "OrthographicDecalMesh";
        public const string kPerspectiveDecalMeshPath = "Packages/com.latios.latiosframework/Kinemation/Resources/PerspectiveDecalMesh.mesh";
        public const string kPerspectiveDecalMeshResource = "PerspectiveDecalMesh";

        static UnityEngine.Mesh CreateOrthographicMeshBase()
        {
            var go = UnityEngine.GameObject.CreatePrimitive(UnityEngine.PrimitiveType.Cube);
            var sharedMesh = go.GetComponent<UnityEngine.MeshFilter>().sharedMesh;
            var mesh = UnityEngine.Object.Instantiate(sharedMesh);
            go.DestroySafelyFromAnywhere();
            mesh.uv = null;
            mesh.uv2 = null;
            var vertices = new System.Collections.Generic.List<UnityEngine.Vector3>();
            var offset = new UnityEngine.Vector3(0f, 0f, 0.5f);
            mesh.GetVertices(vertices);
            for (int i = 0; i < vertices.Count; i++)
                vertices[i] += offset;
            mesh.SetVertices(vertices);
            return mesh;
        }

        internal static UnityEngine.Mesh CreateOrthographicMesh()
        {
            var mesh = CreateOrthographicMeshBase();
            mesh.RecalculateBounds();
            mesh.Optimize();
            return mesh;
        }

        internal static UnityEngine.Mesh CreatePerspectiveMesh()
        {
            var mesh = CreateOrthographicMeshBase();
            var vertices = new System.Collections.Generic.List<UnityEngine.Vector3>();
            mesh.GetVertices(vertices);
            for (int i = 0; i < vertices.Count; i++)
            {
                var vertex = vertices[i];
                if (vertex.z < 0.5f)
                {
                    vertex.x = 0f;
                    vertex.y = 0f;
                    vertices[i] = vertex;
                }
            }
            mesh.SetVertices(vertices);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            mesh.Optimize();
            return mesh;
        }

#if UNITY_EDITOR
        //[UnityEditor.MenuItem("Assets/Create/Latios/Decal Meshes")]
        internal static void CreateMeshAssets()
        {
            var ortho = CreateOrthographicMesh();
            ortho.UploadMeshData(true);
            UnityEditor.AssetDatabase.CreateAsset(ortho, kOrthographicDecalMeshPath);
            var persp = CreatePerspectiveMesh();
            persp.UploadMeshData(true);
            UnityEditor.AssetDatabase.CreateAsset(persp, kPerspectiveDecalMeshPath);
        }
#endif
    }

#if UNITY_EDITOR

#endif
}