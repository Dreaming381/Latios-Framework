using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

namespace Latios.Kinemation.Authoring
{
    /// <summary>
    /// This baker provides the default baking implementation for converting a SkinnedMeshRenderer into something Kinemation can render.
    /// See Kinemation's documentation for details.
    /// </summary>
    [DisableAutoCreation]
    public class SkinnedMeshBaker : Baker<SkinnedMeshRenderer>
    {
        List<Material> m_materialsCache = new List<Material>();

        public override void Bake(SkinnedMeshRenderer authoring)
        {
            if (authoring.sharedMesh == null)
            {
                Debug.LogError($"Kinemation failed to bake Skinned Mesh Renderer {authoring.gameObject.name} because no mesh was assigned.");
                return;
            }
            m_materialsCache.Clear();

            authoring.GetSharedMaterials(m_materialsCache);
            if (m_materialsCache.Count == 0)
            {
                Debug.LogError($"Kinemation failed to bake Skinned Mesh Renderer {authoring.gameObject.name} because no materials were assigned.");
                return;
            }

            int  countValidMaterials     = 0;
            int  knownValidMaterialIndex = -1;
            bool needsWarning            = false;
            for (int i = 0; i < m_materialsCache.Count; i++)
            {
                if (m_materialsCache[i] == null)
                {
                    needsWarning = true;
                }
                else
                {
                    if (knownValidMaterialIndex < 0)
                        knownValidMaterialIndex = i;
                    countValidMaterials++;
                }
            }

            if (countValidMaterials == 0)
            {
                Debug.LogError($"Kinemation failed to bake Skinned Mesh Renderer {authoring.gameObject.name} because no materials were assigned.");
                return;
            }
            if (needsWarning)
            {
                Debug.LogWarning($"Some materials on Skinned Mesh Renderer {authoring.gameObject.name} were not assigned. Rendering results may be incorrect.");
            }

            var settings = GetComponent<SkinnedMeshSettingsAuthoring>();
            if (settings == null || settings.bindingMode == SkinnedMeshSettingsAuthoring.BindingMode.BakeTime)
            {
                var bones = authoring.bones;
                if (bones == null || bones.Length == 0)
                {
                    var animator = GetComponentInParent<Animator>();
                    if (animator == null)
                    {
                        Debug.LogWarning(
                            $"Skinned Mesh Renderer {authoring.gameObject.name} is in an optimized hierachy (bones array is empty) which does not have an animator. No binding paths will be generated.");
                    }
                    else
                    {
                        AddComponent(new PendingMeshBindingPathsBlob
                        {
                            blobHandle = this.RequestCreateBlobAsset(animator, authoring)
                        });
                        AddComponent<MeshBindingPathsBlobReference>();
                    }
                }
                else
                {
                    bool skipBecauseOfWarning = false;

                    foreach (var bone in bones)
                    {
                        if (bone == null)
                        {
                            Debug.LogWarning(
                                $"Skinned Mesh Renderer {authoring.gameObject.name} has a missing bone in its bone array. Please ensure the bones array is set correctly. No binding paths will be generated.");
                            skipBecauseOfWarning = true;
                            break;
                        }
                    }

                    if (!skipBecauseOfWarning)
                    {
                        var pathsPacked = new NativeText(Allocator.Temp);
                        var offsets     = new NativeArray<int>(bones.Length, Allocator.Temp);
                        int index       = 0;
                        foreach (var bone in bones)
                        {
                            offsets[index] = pathsPacked.Length;
                            var go         = bone.gameObject;

                            var parent = GetParent(go);
                            while (parent != null)
                            {
                                pathsPacked.Append(GetName(go));
                                pathsPacked.Append('/');
                                go     = parent;
                                parent = GetParent(parent);
                            }
                            index++;
                        }

                        AddComponent(new PendingMeshBindingPathsBlob
                        {
                            blobHandle = this.RequestCreateBlobAsset(pathsPacked, offsets)
                        });
                        AddComponent<MeshBindingPathsBlobReference>();
                    }
                }
            }
            else if (settings.bindingMode == SkinnedMeshSettingsAuthoring.BindingMode.OverridePaths)
            {
                if (settings.customBonePathsReversed == null)
                {
                    Debug.LogWarning(
                        $"Skinned Mesh Settings {authoring.gameObject.name} specify override paths but the override paths list is null. No binding paths will be generated.");
                }
                else
                {
                    AddComponent(new PendingMeshBindingPathsBlob
                    {
                        blobHandle = this.RequestCreateBlobAsset(settings.customBonePathsReversed)
                    });
                    AddComponent<MeshBindingPathsBlobReference>();
                }
            }

            AddComponent(new PendingMeshSkinningBlob
            {
                blobHandle = this.RequestCreateBlobAsset(authoring.sharedMesh)
            });
            AddComponent<MeshSkinningBlobReference>();

            var additionalEntities = new NativeList<Entity>(Allocator.Temp);
            SkinnedMeshRendererBakingUtility.Convert(this, authoring, authoring.sharedMesh, m_materialsCache, additionalEntities, knownValidMaterialIndex);

            m_materialsCache.Clear();
        }
    }

    // Copied from MeshRendererBakingUtility.cs and modified to conform to Kinemation's format
    class SkinnedMeshRendererBakingUtility
    {
        static int s_SkinMatrixIndexProperty = Shader.PropertyToID("_SkinMatrixIndex");

#if ENABLE_DOTS_DEFORMATION_MOTION_VECTORS && false
        static int s_DOTSDeformedProperty = Shader.PropertyToID("_DotsDeformationParams");
#else
        static int s_ComputeMeshIndexProperty = Shader.PropertyToID("_ComputeMeshIndex");
#endif

        struct LODState
        {
            public LODGroup LodGroup;
            public Entity   LodGroupEntity;
            public int      LodGroupIndex;
        }

        static void CreateLODState<T>(Baker<T> baker, Renderer authoringSource, out LODState lodState) where T : Component
        {
            // LODGroup
            lodState                = new LODState();
            lodState.LodGroup       = baker.GetComponentInParent<LODGroup>();
            lodState.LodGroupEntity = baker.GetEntity(lodState.LodGroup);
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
        private static void AddRendererComponents<T>(Entity entity, Baker<T> baker, in RenderMeshDescription renderMeshDescription, RenderMesh renderMesh) where T : Component
        {
#if UNITY_EDITOR
            // Skip the validation check in the player to minimize overhead.
            if (!RenderMeshUtility.ValidateMesh(renderMesh))
                return;
#endif

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

            //if (renderMesh.mesh == null || renderMesh.material == null)
            //Debug.Log($"Baked Mesh {renderMesh.mesh.name}, Material {renderMesh.material.name}");
            baker.SetSharedComponentManaged(entity, renderMesh);
            baker.SetSharedComponentManaged(entity, renderMeshDescription.FilterSettings);
        }

        internal static void Convert<T>(Baker<T>           baker,
                                        Renderer authoring,
                                        Mesh mesh,
                                        List<Material>     sharedMaterials,
                                        NativeList<Entity> additionalEntities,
                                        int firstValidMaterialIndex) where T : Component
        {
            // Takes a dependency on the material
            foreach (var material in sharedMaterials)
                baker.DependsOn(material);

            // Takes a dependency on the mesh
            baker.DependsOn(mesh);

            // RenderMeshDescription accesses the GameObject layer in its constructor.
            // Declaring the dependency on the GameObject with GetLayer, so the baker rebakes if the layer changes
            baker.GetLayer(authoring);
            var desc       = new RenderMeshDescription(authoring);
            var renderMesh = new RenderMesh(authoring, mesh, sharedMaterials, firstValidMaterialIndex);

            // Always disable per-object motion vectors for static objects
            if (baker.IsStatic())
            {
                if (desc.FilterSettings.MotionMode == MotionVectorGenerationMode.Object)
                    desc.FilterSettings.MotionMode = MotionVectorGenerationMode.Camera;
            }

            if (sharedMaterials.Count == 1)
            {
                ConvertToSingleEntity(
                    baker,
                    desc,
                    renderMesh,
                    authoring);
            }
            else
            {
                ConvertToMultipleEntities(
                    baker,
                    desc,
                    renderMesh,
                    authoring,
                    sharedMaterials,
                    additionalEntities,
                    firstValidMaterialIndex);
            }
        }

#pragma warning restore CS0162

        static void ConvertToSingleEntity<T>(
            Baker<T>              baker,
            RenderMeshDescription renderMeshDescription,
            RenderMesh renderMesh,
            Renderer renderer) where T : Component
        {
            CreateLODState(baker, renderer, out var lodState);

            var entity = baker.GetEntity(renderer);

            AddRendererComponents(entity, baker, renderMeshDescription, renderMesh);

            if (lodState.LodGroupEntity != Entity.Null && lodState.LodGroupIndex != -1)
            {
                var lodComponent = new MeshLODComponent { Group = lodState.LodGroupEntity, LODMask = 1 << lodState.LodGroupIndex };
                baker.AddComponent(entity, lodComponent);
            }

            baker.ConfigureEditorRenderData(entity, renderer.gameObject, true);

            var  material     = renderMesh.material;
            bool needsWarning = true;
            if (material.HasProperty(s_SkinMatrixIndexProperty))
            {
                var linearBlendComponents = new ComponentTypeSet(ComponentType.ReadWrite<LinearBlendSkinningShaderIndex>(),
                                                                 ComponentType.ChunkComponent<ChunkLinearBlendSkinningMemoryMetadata>());

                baker.AddComponent(entity, linearBlendComponents);
                needsWarning = false;
            }
            if (material.HasProperty(s_ComputeMeshIndexProperty))
            {
                var computeDeformComponents = new ComponentTypeSet(ComponentType.ReadWrite<ComputeDeformShaderIndex>(),
                                                                   ComponentType.ChunkComponent<ChunkComputeDeformMemoryMetadata>());
                baker.AddComponent(entity, computeDeformComponents);
                needsWarning = false;
            }

            if (needsWarning)
            {
                Debug.LogWarning(
                    $"Singular mesh for Skinned Mesh Renderer {renderer.gameObject.name} uses shader {material.shader.name} which does not support skinning. Please see documentation for Linear Blend Skinning Node and Compute Deformation Node in Shader Graph.");
            }
        }

        internal static void ConvertToMultipleEntities<T>(
            Baker<T>              baker,
            RenderMeshDescription renderMeshDescription,
            RenderMesh renderMesh,
            Renderer renderer,
            List<Material>        sharedMaterials,
            NativeList<Entity>    additionalEntities,
            int firstValidMaterialIndex) where T : Component
        {
            CreateLODState(baker, renderer, out var lodState);

            int          materialCount               = sharedMaterials.Count;
            Entity       referenceEntity             = default;
            LocalToWorld ltw                         = default;
            bool         referenceNeedsLinearBlend   = false;
            bool         referenceNeedsComputeDeform = false;
            var          linearBlendComponents       = new ComponentTypeSet(ComponentType.ReadWrite<LinearBlendSkinningShaderIndex>(),
                                                                   ComponentType.ChunkComponent<ChunkLinearBlendSkinningMemoryMetadata>());
            var computeDeformComponents = new ComponentTypeSet(ComponentType.ReadWrite<ComputeDeformShaderIndex>(),
                                                               ComponentType.ChunkComponent<ChunkComputeDeformMemoryMetadata>());
            for (var m = firstValidMaterialIndex; m != materialCount; m++)
            {
                Entity meshEntity;
                if (m == firstValidMaterialIndex)
                {
                    meshEntity = baker.GetEntity(renderer, TransformUsageFlags.ManualOverride);

                    ltw = new LocalToWorld { Value = baker.GetComponent<Transform>(renderer).localToWorldMatrix };
                    baker.AddComponent(meshEntity, ltw);
#if !ENABLE_TRANSFORM_V1
                    baker.AddComponent(meshEntity,
                                       new LocalToWorldTransform { Value = UniformScaleTransform.FromMatrix(localToWorld) });
#endif

                    referenceEntity = meshEntity;
                    // Other transforms are handled in baking system once we know the animator wants the smr
                }
                else if (sharedMaterials[m] == null)
                {
                    continue;
                }
                else
                {
                    meshEntity = baker.CreateAdditionalEntity(TransformUsageFlags.ManualOverride, false, $"{baker.GetName()}-CopySkinSubMeshEntity{m}");

                    baker.AddComponent(meshEntity, ltw);
#if !ENABLE_TRANSFORM_V1
                    baker.AddComponent(meshEntity,
                                       new LocalToWorldTransform { Value = UniformScaleTransform.FromMatrix(localToWorld) });
#endif

                    baker.AddComponent(meshEntity, new Parent { Value = referenceEntity });
#if !ENABLE_TRANSFORM_V1
                    baker.AddComponent(meshEntity, new LocalToParentTransform {Value = UniformScaleTransform.Identity});
#else
                    baker.AddComponent(meshEntity, new LocalToParent { Value = float4x4.identity });
#endif
                    baker.AddComponent(meshEntity, new ShareSkinFromEntity { sourceSkinnedEntity = referenceEntity });
                }

                additionalEntities.Add(meshEntity);

                var material = sharedMaterials[m];

                renderMesh.subMesh  = m;
                renderMesh.material = material;

                AddRendererComponents(
                    meshEntity,
                    baker,
                    renderMeshDescription,
                    renderMesh);

                if (lodState.LodGroupEntity != Entity.Null && lodState.LodGroupIndex != -1)
                {
                    var lodComponent = new MeshLODComponent { Group = lodState.LodGroupEntity, LODMask = 1 << lodState.LodGroupIndex };
                    baker.AddComponent(meshEntity, lodComponent);
                }

                baker.ConfigureEditorRenderData(meshEntity, renderer.gameObject, true);

                bool needsWarning = m != firstValidMaterialIndex;
                if (m != firstValidMaterialIndex && material.HasProperty(s_SkinMatrixIndexProperty))
                {
                    referenceNeedsLinearBlend = true;
                    baker.AddComponent<LinearBlendSkinningShaderIndex>(meshEntity);
                    needsWarning = false;
                }
                if (m != firstValidMaterialIndex && material.HasProperty(s_ComputeMeshIndexProperty))
                {
                    referenceNeedsComputeDeform = true;
                    baker.AddComponent<ComputeDeformShaderIndex>(meshEntity);
                    needsWarning = false;
                }

                if (needsWarning)
                {
                    Debug.LogWarning(
                        $"Submesh {m} for Skinned Mesh Renderer {renderer.gameObject.name} uses shader {material.shader.name} which does not support skinning. Please see documentation for Linear Blend Skinning Node and Compute Deformation Node in Shader Graph.");
                }
            }

            var referenceMaterial      = sharedMaterials[firstValidMaterialIndex];
            var referenceLinearBlend   = referenceMaterial.HasProperty(s_SkinMatrixIndexProperty);
            var referenceComputeDeform = referenceMaterial.HasProperty(s_ComputeMeshIndexProperty);

            if (!referenceLinearBlend && !referenceComputeDeform)
            {
                Debug.LogWarning(
                    $"Submesh {firstValidMaterialIndex} for Skinned Mesh Renderer {renderer.gameObject.name} uses shader {referenceMaterial.shader.name} which does not support skinning. Please see documentation for Linear Blend Skinning Node and Compute Deformation Node in Shader Graph.");
            }

            if (referenceLinearBlend || referenceNeedsLinearBlend)
                baker.AddComponent(referenceEntity, linearBlendComponents);
            if (referenceComputeDeform || referenceNeedsComputeDeform)
                baker.AddComponent(referenceEntity, computeDeformComponents);
        }
    }
}

