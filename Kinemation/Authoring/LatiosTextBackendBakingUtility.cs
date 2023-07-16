using Latios.Transforms;
using Latios.Transforms.Authoring;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;

namespace Latios.Kinemation.TextBackend.Authoring
{
    [BurstCompile]
    public static class LatiosTextBackendBakingUtility
    {
        public const string kTextBackendMeshPath     = "Packages/com.latios.latiosframework/Kinemation/Resources/LatiosTextBackendMesh.mesh";
        public const string kTextBackendMeshResource = "LatiosTextBackendMesh";

        public static void BakeTextBackendMeshAndMaterial(this IBaker baker, Renderer renderer, Material material)
        {
            var mesh = Resources.Load<Mesh>(kTextBackendMeshResource);

            Convert(baker, renderer, mesh, material);

            var entity                                                           = baker.GetEntity(TransformUsageFlags.Renderable);
            baker.AddComponent(entity, new MeshRendererBakingData { MeshRenderer = renderer });

            baker.AddComponent(entity, new TextRenderControl { flags = TextRenderControl.Flags.Dirty });
            baker.AddBuffer<RenderGlyph>(entity);
            baker.AddComponent<TextShaderIndex>(entity);
        }

        public static void BakeTextBackendMeshAndMaterial(this IBaker baker, Entity renderableEntity, RenderMeshDescription renderDescription, Material material)
        {
            var mesh = Resources.Load<Mesh>(kTextBackendMeshResource);

            Convert(baker, renderableEntity, renderDescription, mesh, material);

            var entity = baker.GetEntity(TransformUsageFlags.Renderable);
            baker.AddComponent<SkinnedMeshRendererBakingData>(entity);  // Skinned skips lightmaps which require a valid Renderer.

            baker.AddComponent(                               entity, new TextRenderControl { flags = TextRenderControl.Flags.Dirty });
            baker.AddBuffer<RenderGlyph>(entity);
            baker.AddComponent<TextShaderIndex>(entity);
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

        #region Entities Graphics Stuff
        [BakingType]
        struct CopyParentRequestTag : IRequestCopyParentTransform { }

        struct LODState
        {
            public LODGroup LodGroup;
            public Entity   LodGroupEntity;
            public int      LodGroupIndex;
        }

        static void CreateLODState(IBaker baker, Renderer authoringSource, out LODState lodState)
        {
            // LODGroup
            lodState                = new LODState();
            lodState.LodGroup       = baker.GetComponentInParent<LODGroup>();
            lodState.LodGroupEntity = baker.GetEntity(lodState.LodGroup, TransformUsageFlags.Renderable);
            lodState.LodGroupIndex  = FindInLODs(lodState.LodGroup, authoringSource);
        }

        private static int FindInLODs(LODGroup lodGroup, Renderer authoring)
        {
            if (lodGroup != null)
            {
                var lodGroupLODs = lodGroup.GetLODs();

                // Find the renderer inside the LODGroup
                for (int i = 0; i < lodGroupLODs.Length; ++i)
                {
                    foreach (var renderer in lodGroupLODs[i].renderers)
                    {
                        if (renderer == authoring)
                        {
                            return i;
                        }
                    }
                }
            }
            return -1;
        }

#pragma warning disable CS0162
        [BakingType] struct RequestPreviousTag : IRequestPreviousTransform { }

        private static void AddRendererComponents(Entity entity, IBaker baker, in RenderMeshDescription renderMeshDescription, RenderMesh renderMesh)
        {
            // Entities with Static are never rendered with motion vectors
            bool inMotionPass = RenderMeshUtility.kUseHybridMotionPass &&
                                renderMeshDescription.FilterSettings.IsInMotionPass &&
                                !baker.IsStatic();

            RenderMeshUtility.EntitiesGraphicsComponentFlags flags = RenderMeshUtility.EntitiesGraphicsComponentFlags.Baking;
            if (inMotionPass)
                flags |= RenderMeshUtility.EntitiesGraphicsComponentFlags.InMotionPass;
            flags     |= RenderMeshUtility.LightProbeFlags(renderMeshDescription.LightProbeUsage);
            flags     |= RenderMeshUtility.DepthSortedFlags(renderMesh.material);

            // Add all components up front using as few calls as possible.
            var componentTypes = RenderMeshUtility.s_EntitiesGraphicsComponentTypes.GetComponentTypes(flags);
            baker.AddComponent(entity, componentTypes);
            for (int i = 0; i < componentTypes.Length; i++)
            {
                // Todo: What to do for Unity Transforms?
#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
                if (componentTypes.GetTypeIndex(i) == TypeManager.GetTypeIndex<PreviousTransform>())
                    baker.AddComponent<RequestPreviousTag>(entity);
#endif
            }

            baker.SetSharedComponentManaged(entity, renderMesh);
            baker.SetSharedComponentManaged(entity, renderMeshDescription.FilterSettings);

            var localBounds                                     = renderMesh.mesh.bounds.ToAABB();
            baker.SetComponent(entity, new RenderBounds { Value = localBounds });
        }

        private static void Convert(IBaker baker,
                                    Renderer authoring,
                                    Mesh mesh,
                                    Material sharedMaterial)
        {
            if (mesh == null || sharedMaterial == null)
            {
                Debug.LogWarning(
                    $"Renderer is not converted because either the assigned mesh is null or no materials are assigned on GameObject {authoring.name}.",
                    authoring);
                return;
            }

            // Takes a dependency on the material
            baker.DependsOn(sharedMaterial);

            // Takes a dependency on the mesh
            baker.DependsOn(mesh);

            // RenderMeshDescription accesses the GameObject layer.
            // Declaring the dependency on the GameObject with GetLayer, so the baker rebakes if the layer changes
            baker.GetLayer(authoring);
            var desc       = new RenderMeshDescription(authoring);
            var renderMesh = new RenderMesh
            {
                material = sharedMaterial,
                mesh     = mesh,
                subMesh  = 0
            };

            // Always disable per-object motion vectors for static objects
            if (baker.IsStatic())
            {
                if (desc.FilterSettings.MotionMode == MotionVectorGenerationMode.Object)
                    desc.FilterSettings.MotionMode = MotionVectorGenerationMode.Camera;
            }

            ConvertToSingleEntity(
                baker,
                desc,
                renderMesh,
                authoring);
        }

        private static void Convert(IBaker baker,
                                    Entity renderableEntity,
                                    RenderMeshDescription renderMeshDescription,
                                    Mesh mesh,
                                    Material sharedMaterial)
        {
            if (mesh == null || sharedMaterial == null)
            {
                Debug.LogWarning(
                    $"RenderMeshDescription is not converted because either the assigned mesh is null or no materials are assigned.");
                return;
            }

            // Takes a dependency on the material
            baker.DependsOn(sharedMaterial);

            // Takes a dependency on the mesh
            baker.DependsOn(mesh);

            var renderMesh = new RenderMesh
            {
                material = sharedMaterial,
                mesh     = mesh,
                subMesh  = 0
            };

            // Always disable per-object motion vectors for static objects
            if (baker.IsStatic())
            {
                if (renderMeshDescription.FilterSettings.MotionMode == MotionVectorGenerationMode.Object)
                    renderMeshDescription.FilterSettings.MotionMode = MotionVectorGenerationMode.Camera;
            }

            AddRendererComponents(renderableEntity, baker, in renderMeshDescription, renderMesh);
        }

#pragma warning restore CS0162

        static void ConvertToSingleEntity(
            IBaker baker,
            RenderMeshDescription renderMeshDescription,
            RenderMesh renderMesh,
            Renderer renderer)
        {
            CreateLODState(baker, renderer, out var lodState);

            var entity = baker.GetEntity(renderer, TransformUsageFlags.Renderable);

            AddRendererComponents(entity, baker, renderMeshDescription, renderMesh);

            if (lodState.LodGroupEntity != Entity.Null && lodState.LodGroupIndex != -1)
            {
                var lodComponent = new MeshLODComponent { Group = lodState.LodGroupEntity, LODMask = 1 << lodState.LodGroupIndex };
                baker.AddComponent(entity, lodComponent);
            }
        }
        #endregion
    }
}

