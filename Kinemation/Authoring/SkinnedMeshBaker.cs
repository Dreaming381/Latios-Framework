using System.Collections.Generic;
using Latios.Transforms;
using Latios.Transforms.Authoring;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
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
            if (GetComponent<OverrideMeshRendererBase>() != null)
                return;

            var sharedMesh = authoring.sharedMesh;
            if (sharedMesh == null)
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
            if (sharedMesh.bindposeCount > 0)
            {
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
            }
            if (sharedMesh.blendShapeCount > 0)
            {
                AddComponent<BlendShapeState>();
                var weightsBuffer = AddBuffer<BlendShapeWeight>();
                weightsBuffer.ResizeUninitialized(sharedMesh.blendShapeCount);
                var weights = weightsBuffer.AsNativeArray().Reinterpret<float>();
                for (int i = 0; i < sharedMesh.blendShapeCount; i++)
                {
                    weights[i] = authoring.GetBlendShapeWeight(i);
                }
            }

            AddComponent(new PendingMeshDeformDataBlob
            {
                blobHandle = this.RequestCreateBlobAsset(sharedMesh)
            });
            AddComponent<MeshDeformDataBlobReference>();

            var additionalEntities = new NativeList<Entity>(Allocator.Temp);
            SkinnedMeshRendererBakingUtility.Convert(this, authoring, sharedMesh, m_materialsCache, additionalEntities, knownValidMaterialIndex);

            m_materialsCache.Clear();
        }
    }

    // Copied from MeshRendererBakingUtility.cs and modified to conform to Kinemation's format
    class SkinnedMeshRendererBakingUtility
    {
        struct CopyParentRequestTag : IRequestCopyParentTransform { }

        static int s_currentVertexMatrixProperty  = Shader.PropertyToID("_latiosCurrentVertexSkinningMatrixBase");
        static int s_previousVertexMatrixProperty = Shader.PropertyToID("_latiosPreviousVertexSkinningMatrixBase");
        static int s_twoAgoVertexMatrixProperty   = Shader.PropertyToID("_latiosTwoAgoVertexSkinningMatrixBase");
        static int s_currentVertexDqsProperty     = Shader.PropertyToID("_latiosCurrentVertexSkinningDqsBase");
        static int s_previousVertexDqsProperty    = Shader.PropertyToID("_latiosPreviousVertexSkinningDqsBase");
        static int s_twoAgoVertexDqsProperty      = Shader.PropertyToID("_latiosTwoAgoVertexSkinningDqsBase");
        static int s_currentDeformProperty        = Shader.PropertyToID("_latiosCurrentDeformBase");
        static int s_previousDeformProperty       = Shader.PropertyToID("_latiosPreviousDeformBase");
        static int s_twoAgoDeformProperty         = Shader.PropertyToID("_latiosTwoAgoDeformBase");

        static int s_legacyLbsProperty           = Shader.PropertyToID("_SkinMatrixIndex");
        static int s_legacyDotsDeformProperty    = Shader.PropertyToID("_DotsDeformationParams");
        static int s_legacyComputeDeformProperty = Shader.PropertyToID("_ComputeMeshIndex");

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

            var                              material                   = renderMesh.material;
            FixedList128Bytes<ComponentType> materialPropertyTypesToAdd = default;

            if (material.HasProperty(s_legacyLbsProperty))
                materialPropertyTypesToAdd.Add(ComponentType.ReadWrite<LegacyLinearBlendSkinningShaderIndex>());
            if (material.HasProperty(s_legacyComputeDeformProperty))
                materialPropertyTypesToAdd.Add(ComponentType.ReadWrite<LegacyComputeDeformShaderIndex>());
            if (material.HasProperty(s_legacyDotsDeformProperty))
                materialPropertyTypesToAdd.Add(ComponentType.ReadWrite<LegacyDotsDeformParamsShaderIndex>());
            if (material.HasProperty(s_currentVertexMatrixProperty))
                materialPropertyTypesToAdd.Add(ComponentType.ReadWrite<CurrentMatrixVertexSkinningShaderIndex>());
            if (material.HasProperty(s_previousVertexMatrixProperty))
                materialPropertyTypesToAdd.Add(ComponentType.ReadWrite<PreviousMatrixVertexSkinningShaderIndex>());
            if (material.HasProperty(s_twoAgoVertexMatrixProperty))
                materialPropertyTypesToAdd.Add(ComponentType.ReadWrite<TwoAgoMatrixVertexSkinningShaderIndex>());
            if (material.HasProperty(s_currentVertexDqsProperty))
                materialPropertyTypesToAdd.Add(ComponentType.ReadWrite<CurrentDqsVertexSkinningShaderIndex>());
            if (material.HasProperty(s_previousVertexDqsProperty))
                materialPropertyTypesToAdd.Add(ComponentType.ReadWrite<PreviousDqsVertexSkinningShaderIndex>());
            if (material.HasProperty(s_twoAgoVertexDqsProperty))
                materialPropertyTypesToAdd.Add(ComponentType.ReadWrite<TwoAgoDqsVertexSkinningShaderIndex>());
            if (material.HasProperty(s_currentDeformProperty))
                materialPropertyTypesToAdd.Add(ComponentType.ReadWrite<CurrentDeformShaderIndex>());
            if (material.HasProperty(s_previousDeformProperty))
                materialPropertyTypesToAdd.Add(ComponentType.ReadWrite<PreviousDeformShaderIndex>());
            if (material.HasProperty(s_twoAgoDeformProperty))
                materialPropertyTypesToAdd.Add(ComponentType.ReadWrite<TwoAgoDeformShaderIndex>());

            if (materialPropertyTypesToAdd.IsEmpty)
            {
                Debug.LogWarning(
                    $"Singular mesh for Skinned Mesh Renderer {renderer.gameObject.name} uses shader {material.shader.name} which does not support deformations. Please see the Kinemation Getting Started Guide Part 2 to learn how to set up a proper shader graph shader for deformations.");
            }
            else
            {
                baker.AddComponent(entity, new ComponentTypeSet(in materialPropertyTypesToAdd));
            }

            // This serves no other purpose than to ensure post-processing occurs.
            baker.AddComponent(entity, new SkinnedMeshRendererBakingData { SkinnedMeshRenderer = renderer as SkinnedMeshRenderer });
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

            int                  materialCount                  = sharedMaterials.Count;
            Entity               referenceEntity                = default;
            TransformQvvs        worldTransform                 = TransformQvvs.identity;
            DeformClassification requiredPropertiesForReference = DeformClassification.None;
            for (var m = firstValidMaterialIndex; m != materialCount; m++)
            {
                Entity meshEntity;
                if (m == firstValidMaterialIndex)
                {
                    meshEntity = baker.GetEntity(renderer);

                    worldTransform = baker.GetComponent<Transform>(renderer).GetWorldSpaceQvvs();

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

                    baker.AddComponent(                      meshEntity, new WorldTransform { worldTransform = worldTransform });
                    baker.AddComponent(                      meshEntity, new Parent { parent                 = referenceEntity });
                    baker.AddComponent<CopyParentRequestTag>(meshEntity);
                    baker.AddComponent(                      meshEntity, new CopyDeformFromEntity { sourceDeformedEntity = referenceEntity });
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

                DeformClassification classification = DeformClassification.None;
                if (material.HasProperty(s_legacyLbsProperty))
                    classification |= DeformClassification.LegacyLbs;
                if (material.HasProperty(s_legacyComputeDeformProperty))
                    classification |= DeformClassification.LegacyCompute;
                if (material.HasProperty(s_legacyDotsDeformProperty))
                    classification |= DeformClassification.LegacyDotsDefom;
                if (material.HasProperty(s_currentVertexMatrixProperty))
                    classification |= DeformClassification.CurrentVertexMatrix;
                if (material.HasProperty(s_previousVertexMatrixProperty))
                    classification |= DeformClassification.PreviousVertexMatrix;
                if (material.HasProperty(s_twoAgoVertexMatrixProperty))
                    classification |= DeformClassification.TwoAgoVertexMatrix;
                if (material.HasProperty(s_currentVertexDqsProperty))
                    classification |= DeformClassification.CurrentVertexDqs;
                if (material.HasProperty(s_previousVertexDqsProperty))
                    classification |= DeformClassification.PreviousVertexDqs;
                if (material.HasProperty(s_twoAgoVertexDqsProperty))
                    classification |= DeformClassification.TwoAgoVertexDqs;
                if (material.HasProperty(s_currentDeformProperty))
                    classification |= DeformClassification.CurrentDeform;
                if (material.HasProperty(s_previousDeformProperty))
                    classification |= DeformClassification.PreviousDeform;
                if (material.HasProperty(s_twoAgoDeformProperty))
                    classification |= DeformClassification.TwoAgoDeform;

                if (classification == DeformClassification.None)
                {
                    Debug.LogWarning(
                        $"Submesh {m} for Skinned Mesh Renderer {renderer.gameObject.name} uses shader {material.shader.name} which does not support deformations. Please see the Kinemation Getting Started Guide Part 2 to learn how to set up a proper shader graph shader for deformations.");
                }
                else if (m != firstValidMaterialIndex)
                {
                    AddMaterialPropertiesFromDeformClassification(baker, meshEntity, classification);
                }
                requiredPropertiesForReference |= classification;
            }

            if (requiredPropertiesForReference != DeformClassification.None)
                AddMaterialPropertiesFromDeformClassification(baker, referenceEntity, requiredPropertiesForReference);

            // This serves no other purpose than to ensure post-processing occurs.
            baker.AddComponent(referenceEntity, new SkinnedMeshRendererBakingData { SkinnedMeshRenderer = renderer as SkinnedMeshRenderer });
        }

        static void AddMaterialPropertiesFromDeformClassification(IBaker baker, Entity entity, DeformClassification classification)
        {
            FixedList128Bytes<ComponentType> materialPropertyTypesToAdd = default;
            if ((classification & DeformClassification.LegacyLbs) != DeformClassification.None)
                materialPropertyTypesToAdd.Add(ComponentType.ReadWrite<LegacyLinearBlendSkinningShaderIndex>());
            if ((classification & DeformClassification.LegacyCompute) != DeformClassification.None)
                materialPropertyTypesToAdd.Add(ComponentType.ReadWrite<LegacyComputeDeformShaderIndex>());
            if ((classification & DeformClassification.LegacyDotsDefom) != DeformClassification.None)
                materialPropertyTypesToAdd.Add(ComponentType.ReadWrite<LegacyDotsDeformParamsShaderIndex>());
            if ((classification & DeformClassification.CurrentVertexMatrix) != DeformClassification.None)
                materialPropertyTypesToAdd.Add(ComponentType.ReadWrite<CurrentMatrixVertexSkinningShaderIndex>());
            if ((classification & DeformClassification.PreviousVertexMatrix) != DeformClassification.None)
                materialPropertyTypesToAdd.Add(ComponentType.ReadWrite<PreviousMatrixVertexSkinningShaderIndex>());
            if ((classification & DeformClassification.TwoAgoVertexMatrix) != DeformClassification.None)
                materialPropertyTypesToAdd.Add(ComponentType.ReadWrite<TwoAgoMatrixVertexSkinningShaderIndex>());
            if ((classification & DeformClassification.CurrentVertexDqs) != DeformClassification.None)
                materialPropertyTypesToAdd.Add(ComponentType.ReadWrite<CurrentDqsVertexSkinningShaderIndex>());
            if ((classification & DeformClassification.PreviousVertexDqs) != DeformClassification.None)
                materialPropertyTypesToAdd.Add(ComponentType.ReadWrite<PreviousDqsVertexSkinningShaderIndex>());
            if ((classification & DeformClassification.TwoAgoVertexDqs) != DeformClassification.None)
                materialPropertyTypesToAdd.Add(ComponentType.ReadWrite<TwoAgoDqsVertexSkinningShaderIndex>());
            if ((classification & DeformClassification.CurrentDeform) != DeformClassification.None)
                materialPropertyTypesToAdd.Add(ComponentType.ReadWrite<CurrentDeformShaderIndex>());
            if ((classification & DeformClassification.PreviousDeform) != DeformClassification.None)
                materialPropertyTypesToAdd.Add(ComponentType.ReadWrite<PreviousDeformShaderIndex>());
            if ((classification & DeformClassification.TwoAgoDeform) != DeformClassification.None)
                materialPropertyTypesToAdd.Add(ComponentType.ReadWrite<TwoAgoDeformShaderIndex>());

            baker.AddComponent(entity, new ComponentTypeSet(in materialPropertyTypesToAdd));
        }
    }
}

