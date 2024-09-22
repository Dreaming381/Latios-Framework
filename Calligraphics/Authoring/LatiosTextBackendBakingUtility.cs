using Latios.Kinemation.Authoring;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;

namespace Latios.Calligraphics.Rendering.Authoring
{
    [BurstCompile]
    public static class LatiosTextBackendBakingUtility
    {
        public const string kTextBackendMeshPath     = "Packages/com.latios.latiosframework/Kinemation/Resources/LatiosTextBackendMesh.mesh";
        public const string kTextBackendMeshResource = "LatiosTextBackendMesh";

        public static void BakeTextBackendMeshAndMaterial(this IBaker baker, Renderer renderer, Material material)
        {
            var mesh = Resources.Load<Mesh>(kTextBackendMeshResource);

            var entity = baker.GetEntity(TransformUsageFlags.Renderable);

            RenderingBakingTools.GetLOD(baker, renderer, out var lodSettings);
            RenderingBakingTools.BakeLodMaskForEntity(baker, entity, lodSettings);

            var rendererSettings = new MeshRendererBakeSettings
            {
                targetEntity                = entity,
                renderMeshDescription       = new RenderMeshDescription(renderer),
                isDeforming                 = true,
                suppressDeformationWarnings = false,
                useLightmapsIfPossible      = true,
                lightmapIndex               = renderer.lightmapIndex,
                lightmapScaleOffset         = renderer.lightmapScaleOffset,
                isStatic                    = baker.IsStatic(),
                localBounds                 = default,
            };

            baker.BakeMeshAndMaterial(rendererSettings, mesh, material);

            baker.AddComponent(                 entity, new TextRenderControl { flags = TextRenderControl.Flags.Dirty });
            baker.AddComponent<TextShaderIndex>(entity);
        }

        public static void BakeTextBackendMeshAndMaterial(this IBaker baker, MeshRendererBakeSettings rendererSettings, Material material)
        {
            var mesh = Resources.Load<Mesh>(kTextBackendMeshResource);

            baker.BakeMeshAndMaterial(rendererSettings, mesh, material);

            baker.AddComponent(                 rendererSettings.targetEntity, new TextRenderControl { flags = TextRenderControl.Flags.Dirty });
            baker.AddComponent<TextShaderIndex>(rendererSettings.targetEntity);
        }

        #region Mesh Building
#if UNITY_EDITOR
        [UnityEditor.MenuItem("Assets/Create/Latios/Text BackendMesh")]
        static void CreateMeshAsset()
        {
            var glyphCounts = new NativeArray<int>(5, Allocator.Temp);
            glyphCounts[0] = 8;
            glyphCounts[1] = 64;
            glyphCounts[2] = 512;
            glyphCounts[3] = 4096;
            glyphCounts[4] = 16384;

            var mesh = CreateMesh(16384, glyphCounts);
            UnityEditor.AssetDatabase.CreateAsset(mesh, kTextBackendMeshPath);
        }
#endif

        internal static unsafe Mesh CreateMesh(int glyphCount, NativeArray<int> glyphCountsBySubmesh)
        {
            Mesh mesh      = new Mesh();
            var  f3Pattern = new NativeArray<float3>(4, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            f3Pattern[0]   = new float3(-1f, 0f, 0f);
            f3Pattern[1]   = new float3(0f, 1f, 0f);
            f3Pattern[2]   = new float3(1f, 1f, 0f);
            f3Pattern[3]   = new float3(1f, 0f, 0f);
            var f3s        = new NativeArray<float3>(glyphCount * 4, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            UnsafeUtility.MemCpyReplicate(f3s.GetUnsafePtr(), f3Pattern.GetUnsafePtr(), 48, glyphCount);
            mesh.SetVertices(f3s);
            var f4Pattern = new NativeArray<float4>(4, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            f4Pattern[0]  = new float4(0f, 0f, 0f, 0f);
            f4Pattern[1]  = new float4(0f, 1f, 0f, 0f);
            f4Pattern[2]  = new float4(1f, 1f, 0f, 0f);
            f4Pattern[3]  = new float4(1f, 0f, 0f, 0f);
            var f4s       = new NativeArray<float4>(glyphCount * 4, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            UnsafeUtility.MemCpyReplicate(f4s.GetUnsafePtr(), f4Pattern.GetUnsafePtr(), 64, glyphCount);
            mesh.SetUVs(0, f4s);
            mesh.SetColors(f4s);
            var f2Pattern = new NativeArray<float2>(4, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            f2Pattern[0]  = new float2(0f, 0f);
            f2Pattern[1]  = new float2(0f, 1f);
            f2Pattern[2]  = new float2(1f, 1f);
            f2Pattern[3]  = new float2(1f, 0f);
            var f2s       = new NativeArray<float2>(glyphCount * 4, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            UnsafeUtility.MemCpyReplicate(f2s.GetUnsafePtr(), f2Pattern.GetUnsafePtr(), 32, glyphCount);
            mesh.SetUVs(2, f2s);

            mesh.subMeshCount = glyphCountsBySubmesh.Length;
            for (int submesh = 0; submesh < glyphCountsBySubmesh.Length; submesh++)
            {
                var indices = new NativeArray<ushort>(glyphCountsBySubmesh[submesh] * 6, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                BuildIndexBuffer(ref indices);
                mesh.SetIndices(indices, MeshTopology.Triangles, submesh);
            }

            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.UploadMeshData(true);

            return mesh;
        }

        [BurstCompile]
        static void BuildIndexBuffer(ref NativeArray<ushort> indices)
        {
            int glyphCount = indices.Length / 6;
            for (ushort i = 0; i < glyphCount; i++)
            {
                ushort dst       = (ushort)(i * 6);
                ushort src       = (ushort)(i * 4);
                indices[dst]     = src;
                indices[dst + 1] = (ushort)(src + 1);
                indices[dst + 2] = (ushort)(src + 2);
                indices[dst + 3] = (ushort)(src + 2);
                indices[dst + 4] = (ushort)(src + 3);
                indices[dst + 5] = src;
            }
        }
        #endregion
    }
}

