using System;
using System.Collections.Generic;
using Latios.Transforms;
using Latios.Transforms.Authoring;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;

namespace Latios.Kinemation.Authoring
{
    class LatiosMeshRendererBakingUtility
    {
        [BakingType]
        internal struct CopyParentRequestTag : IRequestCopyParentTransform { }

        [BakingType] struct RequestPreviousTag : IRequestPreviousTransform { }

        static List<int> s_validIndexCache = new List<int>();

        internal static void Convert(IBaker baker,
                                     ReadOnlySpan<MeshRendererBakeSettings>    rendererSettings,
                                     ReadOnlySpan<MeshMaterialSubmeshSettings> meshMaterialSubmeshes,
                                     ReadOnlySpan<int>                         meshMaterialSubmeshCountsByRenderer)
        {
            if (rendererSettings.IsEmpty && meshMaterialSubmeshes.IsEmpty && meshMaterialSubmeshCountsByRenderer.IsEmpty)
                return;

            var authoringForLogging = baker.GetAuthoringObjectForDebugDiagnostics();
            if (rendererSettings.IsEmpty)
            {
                Debug.LogWarning($"Renderer is not baked because the provided span of MeshRendererBakeSettings is empty on object {authoringForLogging.name}.",
                                 authoringForLogging);
                return;
            }
            if (meshMaterialSubmeshes.IsEmpty)
            {
                Debug.LogWarning($"Renderer is not baked because the provided span of MeshMaterialSubmeshSettings is empty on object {authoringForLogging.name}.",
                                 authoringForLogging);
                return;
            }
            if (rendererSettings.Length != meshMaterialSubmeshCountsByRenderer.Length)
            {
                Debug.LogWarning(
                    $"Renderer is not baked because the provided span of MeshRendererBakeSettings is of different length than the meshMaterialSubmeshCountsByRenderer {rendererSettings.Length} vs {meshMaterialSubmeshCountsByRenderer.Length} for object {authoringForLogging.name}.",
                    authoringForLogging);
                return;
            }

            DeformClassification requiredPropertiesForReference = DeformClassification.None;
            int                  primaryRendererIndex           = -1;
            for (int rangeStart = 0, rendererIndex = 0; rendererIndex < rendererSettings.Length; rendererIndex++)
            {
                var renderer = rendererSettings[rendererIndex];

                if (renderer.targetEntity == Entity.Null)
                {
                    Debug.LogError($"MeshRendererBakeSettings at index {rendererIndex} for object {authoringForLogging.name} did not provide a valid target entity.",
                                   authoringForLogging);
                    rangeStart += meshMaterialSubmeshCountsByRenderer[rendererIndex];
                    continue;
                }

                var countInRenderer = meshMaterialSubmeshCountsByRenderer[rendererIndex];
                s_validIndexCache.Clear();
                DeformClassification                             classification = DeformClassification.None;
                RenderMeshUtility.EntitiesGraphicsComponentFlags flags          = RenderMeshUtility.EntitiesGraphicsComponentFlags.UseRenderMeshArray;

                for (int i = 0; i < countInRenderer; i++)
                {
                    var mmsIndex = rangeStart + i;
                    var mms      = meshMaterialSubmeshes[mmsIndex];
                    var mesh     = mms.mesh.Value;
                    if (mesh == null)
                    {
                        Debug.LogWarning($"MeshMaterialSubmeshSettings at index {mmsIndex} has a null mesh for object {authoringForLogging.name}", authoringForLogging);
                        continue;
                    }
                    var material = mms.material.Value;
                    if (material == null)
                    {
                        Debug.LogWarning($"MeshMaterialSubmeshSettings at index {mmsIndex} has a null material for object {authoringForLogging.name}", authoringForLogging);
                        continue;
                    }

                    baker.DependsOn(mesh);
                    baker.DependsOn(material);

                    var mmsClassification = GetDeformClassificationFromMaterial(material);
                    if (renderer.isDeforming && mmsClassification == DeformClassification.None && !renderer.suppressDeformationWarnings)
                    {
                        Debug.LogWarning(
                            $"Material {material.name} used by deforming MeshRendererBakeSettings index {rendererIndex} for object {authoringForLogging.name} uses shader {material.shader.name} which does not support deformations. Please see the Kinemation Getting Started Guide Part 2 to learn how to set up a proper shader graph shader for deformations.",
                            authoringForLogging);
                    }
                    else if (!renderer.isDeforming && mmsClassification != DeformClassification.None && !renderer.suppressDeformationWarnings)
                    {
                        Debug.LogWarning(
                            $"Material {material.name} used by non-deforming MeshRendererBakeSettings index {rendererIndex} for object {authoringForLogging.name} uses shader {material.shader.name} which uses deformations. This may result in incorrect rendering.",
                            authoringForLogging);
                    }
                    classification |= mmsClassification;

                    flags |= RenderMeshUtility.DepthSortedFlags(material);

                    s_validIndexCache.Add(mmsIndex);
                }

                if (s_validIndexCache.Count > 0)
                {
                    // Always disable per-object motion vectors for static objects
                    if (renderer.isStatic)
                    {
                        if (renderer.renderMeshDescription.FilterSettings.MotionMode == MotionVectorGenerationMode.Object)
                            renderer.renderMeshDescription.FilterSettings.MotionMode = MotionVectorGenerationMode.Camera;
                    }

                    AddRendererComponents(renderer.targetEntity, baker, in renderer, flags);

                    if (renderer.isDeforming && primaryRendererIndex >= 0)
                    {
                        AddMaterialPropertiesFromDeformClassification(baker, renderer.targetEntity, classification);
                        baker.AddComponent(renderer.targetEntity, new CopyDeformFromEntity { sourceDeformedEntity = rendererSettings[primaryRendererIndex].targetEntity });
                    }

                    if (renderer.isDeforming)
                        requiredPropertiesForReference |= classification;

                    if (!renderer.lodSettings.Equals(default(LodSettings)))
                    {
                        bool negHeight = (renderer.lodSettings.lowestResLodLevel & 0x1) == 1;
                        bool negMin    = (renderer.lodSettings.lowestResLodLevel & 0x2) == 2;
                        bool negMax    = (renderer.lodSettings.lowestResLodLevel & 0x4) == 4;
                        if (negHeight)
                            renderer.lodSettings.localHeight *= -1;
                        if (negMin)
                            renderer.lodSettings.minScreenHeightPercent *= new half(-1f);
                        if (negMax)
                            renderer.lodSettings.maxScreenHeightPercent *= new half(-1f);

                        if (renderer.lodSettings.minScreenHeightPercentAtCrossfadeEdge > 0f || renderer.lodSettings.maxScreenHeightPercentAtCrossfadeEdge > 0f)
                        {
                            if (renderer.lodSettings.isSpeedTree)
                                baker.AddComponent<SpeedTreeCrossfadeTag>(renderer.targetEntity);

                            if (renderer.lodSettings.maxScreenHeightPercentAtCrossfadeEdge < 0f)
                                renderer.lodSettings.maxScreenHeightPercentAtCrossfadeEdge = half.MaxValueAsHalf;

                            baker.AddComponent<LodCrossfade>(renderer.targetEntity);
                            baker.AddComponent(              renderer.targetEntity, new LodHeightPercentagesWithCrossfadeMargins
                            {
                                localSpaceHeight = renderer.lodSettings.localHeight,
                                maxCrossFadeEdge = renderer.lodSettings.maxScreenHeightPercentAtCrossfadeEdge,
                                maxPercent       = renderer.lodSettings.maxScreenHeightPercent,
                                minCrossFadeEdge = renderer.lodSettings.minScreenHeightPercentAtCrossfadeEdge,
                                minPercent       = renderer.lodSettings.minScreenHeightPercent
                            });
                        }
                        else
                        {
                            baker.AddComponent(renderer.targetEntity, new LodHeightPercentages
                            {
                                localSpaceHeight = renderer.lodSettings.localHeight,
                                maxPercent       = renderer.lodSettings.maxScreenHeightPercent,
                                minPercent       = renderer.lodSettings.minScreenHeightPercent
                            });
                        }
                    }

                    if (renderer.useLightmapsIfPossible)
                        baker.DependsOnLightBaking();
                    if (renderer.useLightmapsIfPossible && GetStaticLightingModeFromLightmapIndex(renderer.lightmapIndex) == RenderMeshUtility.StaticLightingMode.LightMapped)
                    {
                        var lightmapSet = new ComponentTypeSet(ComponentType.ReadWrite<BakingLightmapIndex>(),
                                                               ComponentType.ReadWrite<BuiltinMaterialPropertyUnity_LightmapIndex>(),
                                                               ComponentType.ReadWrite<BuiltinMaterialPropertyUnity_LightmapST>(),
                                                               ComponentType.ReadWrite<LightMaps>());
                        baker.AddComponent(renderer.targetEntity, lightmapSet);

                        baker.SetComponent(renderer.targetEntity, new BakingLightmapIndex { lightmapIndex             = renderer.lightmapIndex });
                        baker.SetComponent(renderer.targetEntity, new BuiltinMaterialPropertyUnity_LightmapST { Value = renderer.lightmapScaleOffset });
                    }

                    if (primaryRendererIndex >= 0)
                    {
                        baker.AddComponent<AdditionalMeshRendererEntity>(renderer.targetEntity);
                        if (!renderer.isStatic)
                            baker.AddComponent<CopyParentRequestTag>(renderer.targetEntity);
                    }
                    else
                        primaryRendererIndex = rendererIndex;

                    var mmsBuffer      = baker.AddBuffer<BakingMaterialMeshSubmesh>(renderer.targetEntity);
                    mmsBuffer.Capacity = s_validIndexCache.Count;

                    foreach (var i in s_validIndexCache)
                    {
                        var src = meshMaterialSubmeshes[i];
                        mmsBuffer.Add(new BakingMaterialMeshSubmesh
                        {
                            mesh     = src.mesh,
                            material = src.material,
                            submesh  = src.submesh
                        });
                    }
                }
                rangeStart += meshMaterialSubmeshCountsByRenderer[rendererIndex];
            }

            if (primaryRendererIndex >= 0)
                AddMaterialPropertiesFromDeformClassification(baker, rendererSettings[primaryRendererIndex].targetEntity, requiredPropertiesForReference);
        }

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

        private static DeformClassification GetDeformClassificationFromMaterial(Material material)
        {
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
            return classification;
        }

        private static void AddRendererComponents(Entity entity, IBaker baker, in MeshRendererBakeSettings settings, RenderMeshUtility.EntitiesGraphicsComponentFlags baseFlags)
        {
            // Add all components up front using as few calls as possible.
            baseFlags        |= RenderMeshUtility.EntitiesGraphicsComponentFlags.Baking;
            var componentSet  = RenderMeshUtility.ComputeComponentTypes(baseFlags, settings.renderMeshDescription, baker.IsStatic(), null);
            baker.AddComponent(entity, componentSet);
            for (int i = 0; i < componentSet.Length; i++)
            {
                // Todo: What to do for Unity Transforms?
#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
                if (componentSet.GetTypeIndex(i) == TypeManager.GetTypeIndex<PreviousTransform>())
                    baker.AddComponent<RequestPreviousTag>(entity);
#endif
            }

            baker.SetSharedComponentManaged(entity, settings.renderMeshDescription.FilterSettings);

            baker.SetComponent(entity, new RenderBounds { Value = settings.localBounds.ToAABB() });
        }

        static void AddMaterialPropertiesFromDeformClassification(IBaker baker, Entity entity, DeformClassification classification)
        {
            if (classification == DeformClassification.None)
                return;
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

        static RenderMeshUtility.StaticLightingMode GetStaticLightingModeFromLightmapIndex(int lightmapIndex)
        {
            var staticLightingMode = RenderMeshUtility.StaticLightingMode.None;
            if (lightmapIndex >= 65534 || lightmapIndex < 0)
                staticLightingMode = RenderMeshUtility.StaticLightingMode.LightProbes;
            else if (lightmapIndex >= 0)
                staticLightingMode = RenderMeshUtility.StaticLightingMode.LightMapped;

            return staticLightingMode;
        }
    }
}

