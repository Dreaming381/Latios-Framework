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
    // Copied from MeshRendererBakingUtility.cs and modified to conform to Kinemation's format
    class LatiosDeformMeshRendererBakingUtility
    {
        [BakingType]
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

        public static bool CheckHasDeformMaterialProperty(Material material)
        {
            if (material.HasProperty(s_legacyLbsProperty))
                return true;
            if (material.HasProperty(s_legacyComputeDeformProperty))
                return true;
            if (material.HasProperty(s_legacyDotsDeformProperty))
                return true;
            if (material.HasProperty(s_currentVertexMatrixProperty))
                return true;
            if (material.HasProperty(s_previousVertexMatrixProperty))
                return true;
            if (material.HasProperty(s_twoAgoVertexMatrixProperty))
                return true;
            if (material.HasProperty(s_currentVertexDqsProperty))
                return true;
            if (material.HasProperty(s_previousVertexDqsProperty))
                return true;
            if (material.HasProperty(s_twoAgoVertexDqsProperty))
                return true;
            if (material.HasProperty(s_currentDeformProperty))
                return true;
            if (material.HasProperty(s_previousDeformProperty))
                return true;
            if (material.HasProperty(s_twoAgoDeformProperty))
                return true;
            return false;
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

            //if (renderMesh.mesh == null || renderMesh.material == null)
            //Debug.Log($"Baked Mesh {renderMesh.mesh.name}, Material {renderMesh.material.name}");
            baker.SetSharedComponentManaged(entity, renderMesh);
            baker.SetSharedComponentManaged(entity, renderMeshDescription.FilterSettings);
        }

        internal static void Convert(IBaker baker,
                                     Renderer authoring,
                                     Mesh mesh,
                                     List<Material>     sharedMaterials,
                                     NativeList<Entity> additionalEntities,
                                     int firstValidMaterialIndex)
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
        }

        internal static void ConvertToMultipleEntities(
            IBaker baker,
            RenderMeshDescription renderMeshDescription,
            RenderMesh renderMesh,
            Renderer renderer,
            List<Material>        sharedMaterials,
            NativeList<Entity>    additionalEntities,
            int firstValidMaterialIndex)
        {
            CreateLODState(baker, renderer, out var lodState);

            int                  materialCount                  = sharedMaterials.Count;
            Entity               referenceEntity                = default;
            DeformClassification requiredPropertiesForReference = DeformClassification.None;
            for (var m = firstValidMaterialIndex; m != materialCount; m++)
            {
                Entity meshEntity;
                if (m == firstValidMaterialIndex)
                {
                    meshEntity = baker.GetEntity(renderer, TransformUsageFlags.Renderable);

                    referenceEntity = meshEntity;
                    // Other transforms are handled in baking system once we know the animator wants the smr
                }
                else if (sharedMaterials[m] == null)
                {
                    continue;
                }
                else
                {
                    meshEntity = baker.CreateAdditionalEntity(TransformUsageFlags.Renderable, false, $"{baker.GetName()}-CopySkinSubMeshEntity{m}");

                    baker.AddComponent<CopyParentRequestTag>(meshEntity);
                    baker.AddComponent(                      meshEntity, new CopyDeformFromEntity { sourceDeformedEntity = referenceEntity });

                    additionalEntities.Add(meshEntity);
                }

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

