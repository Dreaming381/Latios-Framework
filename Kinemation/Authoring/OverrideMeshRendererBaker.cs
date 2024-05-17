using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Hybrid.Baking;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;

namespace Latios.Kinemation.Authoring
{
    /// <summary>
    /// Override this class and attach to a GameObject to disable normal Mesh Renderer or Skinned Mesh Renderer baking
    /// </summary>
    public class OverrideMeshRendererBase : MonoBehaviour
    {
    }

    public static class OverrideMeshRendererBakerExtensions
    {
        /// <summary>
        /// Bakes the Mesh Renderer settings with the provided mesh and material onto a single entity.
        /// </summary>
        /// <param name="rendererSettings">The rendering settings for the given entity that should have rendering components baked for it</param>
        /// <param name="mesh">The mesh the entity should use</param>
        /// <param name="material">The material (or potentially a copy in the case of lightmaps) the entity should use</param>
        /// <param name="submesh">The submesh this entity should render at</param>
        public static void BakeMeshAndMaterial(this IBaker baker, MeshRendererBakeSettings rendererSettings, Mesh mesh, Material material, int submesh = 0)
        {
            Span<MeshRendererBakeSettings> renderers = stackalloc MeshRendererBakeSettings[1];
            renderers[0]                             = rendererSettings;
            Span<MeshMaterialSubmeshSettings> mms    = stackalloc MeshMaterialSubmeshSettings[1];
            mms[0]                                   = new MeshMaterialSubmeshSettings
            {
                mesh     = mesh,
                material = material,
                submesh  = submesh
            };
            Span<int> counts = stackalloc int[1];
            counts[0]        = 1;
            baker.BakeMeshAndMaterial(renderers, mms, counts);
        }

        /// <summary>
        /// Bakes the Mesh Renderer settings and associated meshes and materials into the collection of entities.
        /// The first settings instance is assumed to be the primary entity while the remaining entities are children entities.
        /// </summary>
        /// <param name="rendererSettings">The rendering settings for each given entity that should have rendering components baked for it</param>
        /// <param name="meshMaterialSubmeshes">The flattened array of MeshMaterialSubmeshSettings ordered by their corresponding MeshRendererBakeSettings</param>
        /// <param name="meshMaterialSubmeshCountsByRenderer">The count of MeshMaterialSubmeshSettings associated with each MeshRendererBakeSettings</param>
        public static void BakeMeshAndMaterial(this IBaker baker,
                                               ReadOnlySpan<MeshRendererBakeSettings>    rendererSettings,
                                               ReadOnlySpan<MeshMaterialSubmeshSettings> meshMaterialSubmeshes,
                                               ReadOnlySpan<int>                         meshMaterialSubmeshCountsByRenderer)
        {
            LatiosMeshRendererBakingUtility.Convert(baker, rendererSettings, meshMaterialSubmeshes, meshMaterialSubmeshCountsByRenderer);
        }
    }

    /// <summary>
    /// The settings used to bake rendering components onto a target entity, independent of the meshes and materials used
    /// </summary>
    public struct MeshRendererBakeSettings
    {
        /// <summary>
        /// The entity the rendering components should be added to
        /// </summary>
        public Entity targetEntity;
        /// <summary>
        /// The RenderMeshDescription that describes rendering flags, masks, and filters for the entity
        /// </summary>
        public RenderMeshDescription renderMeshDescription;
        /// <summary>
        /// The local bounds of this entity (not used for skeletal meshes)
        /// </summary>
        public UnityEngine.Bounds localBounds;
        /// <summary>
        /// Set to true if the entity is expected to have deformations
        /// </summary>
        public bool isDeforming;
        /// <summary>
        /// Set to true if materials that do not match the isDeforming status are expected and intended
        /// </summary>
        public bool suppressDeformationWarnings;
        /// <summary>
        /// Set to true if lightmaps should be accounted for with this entity, assuming the lightmapIndex is valid
        /// </summary>
        public bool useLightmapsIfPossible;
        /// <summary>
        /// The lightmap index retrieved from the Renderer (some values are reserved as invalid)
        /// </summary>
        public int lightmapIndex;
        /// <summary>
        /// The lightmap texture scale and offset for the Renderer
        /// </summary>
        public float4 lightmapScaleOffset;
        /// <summary>
        /// The LOD settings for this entity, typically extracted from a belonging LOD group
        /// </summary>
        public LodSettings lodSettings;
        /// <summary>
        /// True if the entity should be treated as if it were marked static
        /// </summary>
        public bool isStatic;
    }

    /// <summary>
    /// A struct which describes the membership within a LOD group.
    /// </summary>
    public struct LodSettings : IEquatable<LodSettings>
    {
        /// <summary>
        /// The local height as dictated by the LOD Group
        /// </summary>
        public float localHeight;
        /// <summary>
        /// The smallest percentage of the screen height this entity should consume to be drawn
        /// </summary>
        public half minScreenHeightPercent;
        /// <summary>
        /// The largest percentage of the screen height this entity should consume to be drawn
        /// </summary>
        public half maxScreenHeightPercent;
        /// <summary>
        /// The percentage of the screen height at which the entity begins crossfading to the LOD that
        /// shares the minScreenHeightPercent. (The adjacent LOD is higher indexed and lower-res or complete fade-out.)
        /// Zero or negative if crossfade is unused for this edge.
        /// </summary>
        public half minScreenHeightPercentAtCrossfadeEdge;
        /// <summary>
        /// The percentage of the screen height at which the entity begins crossfading to the LOD that
        /// shares the maxScreenHeightPercent. (The adjacent LOD is lower indexed and higher res.)
        /// Zero or negative if crossfade is unused for this edge.
        /// </summary>
        public half maxScreenHeightPercentAtCrossfadeEdge;

        /// <summary>
        /// The LOD level used to compare against QualitySettings.maximumLODLevel
        /// </summary>
        public byte lowestResLodLevel;

        /// <summary>
        /// True if the crossfade mode uses speedtree
        /// </summary>
        public bool isSpeedTree;

        public bool Equals(LodSettings other)
        {
            return localHeight.Equals(other.localHeight) &&
                   minScreenHeightPercent.Equals(other.minScreenHeightPercent) &&
                   maxScreenHeightPercent.Equals(other.maxScreenHeightPercent) &&
                   minScreenHeightPercentAtCrossfadeEdge.Equals(other.minScreenHeightPercentAtCrossfadeEdge) &&
                   maxScreenHeightPercentAtCrossfadeEdge.Equals(other.maxScreenHeightPercentAtCrossfadeEdge) &&
                   lowestResLodLevel.Equals(other.lowestResLodLevel) &&
                   isSpeedTree.Equals(other.isSpeedTree);
        }
    }

    /// <summary>
    /// A combination of mesh, material, and submesh that can be rendered
    /// </summary>
    public struct MeshMaterialSubmeshSettings
    {
        public UnityObjectRef<Mesh>     mesh;
        public UnityObjectRef<Material> material;
        public int                      submesh;
    }

    public static class RenderingBakingTools
    {
        /// <summary>
        /// Extracts the mesh and materials combination into the destination span of MeshMaterialSubmeshSettings
        /// </summary>
        /// <param name="dst">Length must be greater or equal to the count of shared materials</param>
        /// <param name="mesh">The mesh used for all settings</param>
        /// <param name="sharedMaterials">The list of shared materials, where a MeshMaterialSubmeshSettings will be created for each
        /// material using a submesh of the corresponding index</param>
        public static void ExtractMeshMaterialSubmeshes(Span<MeshMaterialSubmeshSettings> dst, Mesh mesh, List<Material> sharedMaterials)
        {
            if (sharedMaterials == null)
                return;

            if (dst.Length < sharedMaterials.Count)
                throw new ArgumentException("dst is not large enough to contain sharedMaterials", "dst");

            for (int i = 0; i < sharedMaterials.Count; i++)
            {
                dst[i] = new MeshMaterialSubmeshSettings
                {
                    mesh     = mesh,
                    material = sharedMaterials[i],
                    submesh  = i
                };
            }
        }

        /// <summary>
        /// Returns true if the material requires depth sorting due to alpha transparency
        /// </summary>
        public static bool RequiresDepthSorting(Material material)
        {
            var span                                               = s_materialSpanCache.AsSpan();
            span[0]                                                = material;
            RenderMeshUtility.EntitiesGraphicsComponentFlags flags = default;
            flags.AppendDepthSortedFlag(span);
            return flags == RenderMeshUtility.EntitiesGraphicsComponentFlags.DepthSorted;
        }

        static Material[] s_materialSpanCache = new Material[1];

        /// <summary>
        /// Rearranges the MeshMaterialSubmeshSettings such that the elements that do not require depth sorting are at the beginning
        /// of the span and those that do are at the end of the span
        /// </summary>
        /// <param name="meshMaterialSubmeshSettings">The span whose settings elements should be rearranged</param>
        /// <returns>The number of elements that do not require depth sorting</returns>
        public static int GroupByDepthSorting(Span<MeshMaterialSubmeshSettings> meshMaterialSubmeshSettings)
        {
            int tail      = meshMaterialSubmeshSettings.Length - 1;
            var lastIndex = tail;
            for (int head = 0; head < tail; head++)
            {
                var headMaterial = meshMaterialSubmeshSettings[head].material.Value;
                if (RequiresDepthSorting(headMaterial))
                {
                    for (; tail > head; tail--)
                    {
                        var tailMaterial = meshMaterialSubmeshSettings[tail].material.Value;
                        if (!RequiresDepthSorting(tailMaterial))
                        {
                            (meshMaterialSubmeshSettings[tail], meshMaterialSubmeshSettings[head]) = (meshMaterialSubmeshSettings[head], meshMaterialSubmeshSettings[tail]);
                            break;
                        }
                    }

                    if (tail == head)
                        return tail;
                }
            }
            if (tail != lastIndex || RequiresDepthSorting(meshMaterialSubmeshSettings[lastIndex].material.Value))
                return tail;
            return lastIndex + 1;
        }

        /// <summary>
        /// Acquires the LOD Group the renderer belongs to and extracts LOD settings for the renderer.
        /// </summary>
        /// <param name="baker">The baker used to search for the LOD Group in the hierarchy</param>
        /// <param name="renderer">The renderer to find within the LOD Group</param>
        /// <param name="lodSettings">The LOD settings for this renderer inside the LOD Group</param>
        public static void GetLOD(IBaker baker, Renderer renderer, out LodSettings lodSettings)
        {
            var group   = baker.GetComponentInParent<LODGroup>();
            lodSettings = default;
            if (group != null)
            {
                lodSettings.localHeight = group.size;
                lodSettings.isSpeedTree = group.fadeMode == LODFadeMode.SpeedTree;
                var lodGroupLODs        = group.GetLODs();

                float previousMargin     = -1f;
                float previousTransition = 1f;
                bool  foundFirst         = false;

                // Find the renderer inside the LODGroup
                for (int i = 0; i < lodGroupLODs.Length; ++i)
                {
                    float currentMargin = group.fadeMode != LODFadeMode.None ? math.lerp(lodGroupLODs[i].screenRelativeTransitionHeight,
                                                                                         previousTransition,
                                                                                         lodGroupLODs[i].fadeTransitionWidth) : -1f;
                    foreach (var candidate in lodGroupLODs[i].renderers)
                    {
                        if (renderer == candidate)
                        {
                            if (!foundFirst)
                            {
                                if (previousMargin > 0f)
                                {
                                    // For non-animated cross-fade, the fade starts before the transition and ends at the transition.
                                    lodSettings.maxScreenHeightPercent                = (half)previousMargin;
                                    lodSettings.maxScreenHeightPercentAtCrossfadeEdge = (half)previousTransition;
                                }
                                else
                                {
                                    lodSettings.maxScreenHeightPercent                = (half)previousTransition;
                                    lodSettings.maxScreenHeightPercentAtCrossfadeEdge = new half(-1f);
                                }
                                foundFirst = true;
                            }
                            if (currentMargin > 0f)
                            {
                                lodSettings.minScreenHeightPercentAtCrossfadeEdge = (half)currentMargin;
                            }
                            lodSettings.minScreenHeightPercent = (half)lodGroupLODs[i].screenRelativeTransitionHeight;
                            lodSettings.lowestResLodLevel      = (byte)i;
                            break;
                        }
                    }
                    previousMargin     = currentMargin;
                    previousTransition = lodGroupLODs[i].screenRelativeTransitionHeight;
                }
                if (lodSettings.maxScreenHeightPercent <= 0f)
                {
                    lodSettings = default;
                    return;
                }

                // We have found valid LODs. But we should validate that the screenHeight is useful or if an offset would be required.
                var rendererTransform = baker.GetComponent<Transform>(renderer);
                var groupTransform    = baker.GetComponent<Transform>(group);
                var relativeTransform = Transforms.Authoring.Abstract.AbstractBakingUtilities.ExtractTransformRelativeTo(rendererTransform, groupTransform);
                if (math.lengthsq(relativeTransform.position) > math.EPSILON)
                {
                    Debug.LogWarning(
                        $"LOD renderer {renderer.gameObject.name} has a different world position than the LOD Group {group.gameObject.name} it belongs to. This is currently not supported and artifacts may occur. If you are seeing this message, please report it to the Latios Framework developers so that we can better understand your use case.");
                }
                lodSettings.localHeight /= math.cmax(relativeTransform.scale * relativeTransform.stretch);
            }
        }
    }

    /// <summary>
    /// This is the default Mesh Renderer baker in Kinemation.
    /// Kinemation replaces Unity's version to accomodate QVVS transforms
    /// and allow overrides. But it is otherwise identical.
    /// </summary>
    [DisableAutoCreation]
    public class DefaultMeshRendererBaker : Baker<MeshRenderer>
    {
        List<Material> m_materialsCache = new List<Material>();

        public override void Bake(MeshRenderer authoring)
        {
            if (GetComponent<OverrideMeshRendererBase>() != null)
                return;

            if (GetComponent<BakingOnlyEntityAuthoring>() != null)
                return;

            // TextMeshes don't need MeshFilters, early out
            var textMesh = GetComponent<TextMesh>();
            if (textMesh != null)
                return;

            var meshFilter = GetComponent<MeshFilter>();
            var mesh       = (meshFilter != null) ? GetComponent<MeshFilter>().sharedMesh : null;

            m_materialsCache.Clear();
            authoring.GetSharedMaterials(m_materialsCache);

            Span<MeshMaterialSubmeshSettings> mms = stackalloc MeshMaterialSubmeshSettings[m_materialsCache.Count];
            RenderingBakingTools.ExtractMeshMaterialSubmeshes(mms, mesh, m_materialsCache);
            var opaqueMaterialCount = RenderingBakingTools.GroupByDepthSorting(mms);

            RenderingBakingTools.GetLOD(this, authoring, out var lodSettings);

            var rendererSettings = new MeshRendererBakeSettings
            {
                targetEntity                = GetEntity(TransformUsageFlags.Renderable),
                renderMeshDescription       = new RenderMeshDescription(authoring),
                isDeforming                 = false,
                suppressDeformationWarnings = false,
                useLightmapsIfPossible      = true,
                lightmapIndex               = authoring.lightmapIndex,
                lightmapScaleOffset         = authoring.lightmapScaleOffset,
                lodSettings                 = lodSettings,
                isStatic                    = IsStatic(),
                localBounds                 = mesh != null ? mesh.bounds : default,
            };

            if (opaqueMaterialCount == mms.Length || opaqueMaterialCount == 0)
            {
                Span<MeshRendererBakeSettings> renderers = stackalloc MeshRendererBakeSettings[1];
                renderers[0]                             = rendererSettings;
                Span<int> count                          = stackalloc int[1];
                count[0]                                 = mms.Length;
                this.BakeMeshAndMaterial(renderers, mms, count);
            }
            else
            {
                var                             additionalEntity = CreateAdditionalEntity(TransformUsageFlags.Renderable, false, $"{GetName()}-TransparentRenderEntity");
                Span <MeshRendererBakeSettings> renderers        = stackalloc MeshRendererBakeSettings[2];
                renderers[0]                                     = rendererSettings;
                renderers[1]                                     = rendererSettings;
                renderers[1].targetEntity                        = additionalEntity;
                Span<int> counts                                 = stackalloc int[2];
                counts[0]                                        = opaqueMaterialCount;
                counts[1]                                        = mms.Length - opaqueMaterialCount;
                this.BakeMeshAndMaterial(renderers, mms, counts);
            }
        }
    }
}

