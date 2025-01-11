using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Hybrid.Baking;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;

namespace Latios.Kinemation.Authoring
{
    /// <summary>
    /// Implement this interface on a MonoBehaviour and attach to a GameObject to disable normal Mesh Renderer or Skinned Mesh Renderer baking
    /// </summary>
    public interface IOverrideMeshRenderer
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
                submesh  = (ushort)submesh,
                lodMask  = MeshMaterialSubmeshSettings.kDefaultLodMask
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
        public ushort                   submesh;
        public byte                     lodMask;  // LSB is LOD0 (highest resolution)

        public const byte kDefaultLodMask = 0xff;
    }

    /// <summary>
    /// Various rendering baking helper methods
    /// </summary>
    public static partial class RenderingBakingTools
    {
        static Mesh s_uniqueMeshPlaceholder = null;

        /// <summary>
        /// Gets a Unique Mesh Placeholder
        /// </summary>
        public static Mesh uniqueMeshPlaceholder
        {
            get
            {
                if (s_uniqueMeshPlaceholder == null)
                {
#if UNITY_EDITOR
                    s_uniqueMeshPlaceholder =
                        UnityEditor.AssetDatabase.LoadAssetAtPath<Mesh>("Packages/com.latios.latiosframework/Kinemation/Authoring/UniqueMeshPlaceholder.asset");
#endif
                }
                return s_uniqueMeshPlaceholder;
            }
        }

        /// <summary>
        /// Extracts the mesh and materials combination into the destination span of MeshMaterialSubmeshSettings
        /// </summary>
        /// <param name="dst">Length must be greater or equal to the count of shared materials</param>
        /// <param name="mesh">The mesh used for all settings</param>
        /// <param name="sharedMaterials">The list of shared materials, where a MeshMaterialSubmeshSettings will be created for each
        /// material using a submesh of the corresponding index</param>
        /// <param name="lodMask">A LOD mask for up to 8 LODs that all MaterialMeshSubmehsSettings in the span should use</param>
        /// <param name="clampSubmeshes">If there are more materials than submeshes, this forces all extra materials to use the last submesh,
        /// matching the behavior of Mesh Renderer</param>
        public static void ExtractMeshMaterialSubmeshes(Span<MeshMaterialSubmeshSettings> dst,
                                                        Mesh mesh,
                                                        List<Material>                    sharedMaterials,
                                                        byte lodMask        = MeshMaterialSubmeshSettings.kDefaultLodMask,
                                                        bool clampSubmeshes = true)
        {
            if (sharedMaterials == null)
                return;

            if (dst.Length < sharedMaterials.Count)
                throw new ArgumentException("dst is not large enough to contain sharedMaterials", "dst");

            var lastSubmesh = mesh.subMeshCount - 1;
            for (int i = 0; i < sharedMaterials.Count; i++)
            {
                dst[i] = new MeshMaterialSubmeshSettings
                {
                    mesh     = mesh,
                    material = sharedMaterials[i],
                    submesh  = (ushort)(clampSubmeshes ? math.min(lastSubmesh, i) : i),
                    lodMask  = lodMask
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

        /// <summary>
        /// Returns VertexSkinning and Deform feature flags if they are required by any of the materials.
        /// </summary>
        public static MeshDeformDataFeatures GetDeformFeaturesFromMaterials(List<Material> materials)
        {
            MeshDeformDataFeatures features = MeshDeformDataFeatures.None;
            foreach (var material in materials)
            {
                var classification = LatiosMeshRendererBakingUtility.GetDeformClassificationFromMaterial(material);
                if ((classification & (DeformClassification.AnyCurrentDeform | DeformClassification.AnyPreviousDeform | DeformClassification.TwoAgoDeform)) !=
                    DeformClassification.None)
                    features |= MeshDeformDataFeatures.Deform;
                else if (classification != DeformClassification.None)
                    features |= MeshDeformDataFeatures.VertexSkinning;
            }
            return features;
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
                var lodGroupLODs = group.GetLODs();

                int  firstLOD         = -1;
                int  lastLOD          = -1;
                bool wasInPreviousLod = false;
                int  lodIndex         = 0;
                foreach (var lod in lodGroupLODs)
                {
                    if (lod.renderers.Contains(renderer))
                    {
                        if (!wasInPreviousLod && firstLOD >= 0)
                        {
                            Debug.LogWarning(
                                $"Renderer {renderer.gameObject.name} is in two non-adjacent LOD groups. This is not supported in Kinemation. Duplicate the renderer if you intended this.");
                        }

                        if (firstLOD == -1)
                            firstLOD     = lodIndex;
                        lastLOD          = math.max(lodIndex, lastLOD);
                        wasInPreviousLod = true;
                    }
                    lodIndex++;
                }
                if (firstLOD < 0)
                    return;

                lodSettings.isSpeedTree = group.fadeMode == LODFadeMode.SpeedTree;
                bool hasCrossfades      = group.fadeMode != LODFadeMode.None;

                // Max heights
                if (firstLOD == 0)
                {
                    lodSettings.maxScreenHeightPercent                = half.MaxValueAsHalf;
                    lodSettings.maxScreenHeightPercentAtCrossfadeEdge = hasCrossfades ? half.MaxValueAsHalf : half.zero;
                }
                else
                {
                    var lodBefore = lodGroupLODs[firstLOD - 1];
                    if (hasCrossfades)
                    {
                        lodSettings.maxScreenHeightPercentAtCrossfadeEdge = (half)lodBefore.screenRelativeTransitionHeight;
                        var lodBeforeMaxEdge                              = firstLOD == 1 ? 1f : lodGroupLODs[firstLOD - 2].screenRelativeTransitionHeight;
                        lodSettings.maxScreenHeightPercent                = (half)math.lerp(lodBefore.screenRelativeTransitionHeight,
                                                                                            lodBeforeMaxEdge,
                                                                                            lodBefore.fadeTransitionWidth);
                    }
                    else
                    {
                        lodSettings.maxScreenHeightPercent                = (half)lodBefore.screenRelativeTransitionHeight;
                        lodSettings.maxScreenHeightPercentAtCrossfadeEdge = half.zero;
                    }
                }

                // Min heights
                lodSettings.lowestResLodLevel      = (byte)lastLOD;
                var minLod                         = lodGroupLODs[lastLOD];
                lodSettings.minScreenHeightPercent = (half)minLod.screenRelativeTransitionHeight;
                if (hasCrossfades)
                {
                    var maxEdge                                       = lastLOD == 0 ? 1f : lodGroupLODs[lastLOD - 1].screenRelativeTransitionHeight;
                    lodSettings.minScreenHeightPercentAtCrossfadeEdge = (half)math.lerp(minLod.screenRelativeTransitionHeight, maxEdge, minLod.fadeTransitionWidth);
                }
                else
                    lodSettings.minScreenHeightPercentAtCrossfadeEdge = half.zero;

                // Local height
                var rendererTransform = baker.GetComponent<Transform>(renderer);
                var groupTransform    = baker.GetComponent<Transform>(group);
                var relativeTransform = Transforms.Authoring.Abstract.AbstractBakingUtilities.ExtractTransformRelativeTo(rendererTransform, groupTransform);
                if (math.lengthsq(relativeTransform.position) > math.EPSILON)
                {
                    Debug.LogWarning(
                        $"LOD renderer {renderer.gameObject.name} has a different world position than the LOD Group {group.gameObject.name} it belongs to. This is currently not supported and artifacts may occur. If you are seeing this message, please report it to the Latios Framework developers so that we can better understand your use case.");
                }
                lodSettings.localHeight = group.size / math.cmax(relativeTransform.scale * relativeTransform.stretch);
            }
        }

        /// <summary>
        /// Bakes the LODSettings to optionally add a LodHeightPercentages or LodHeightPercentagesWithCrossfadeMargins
        /// and optionally add the SpeedTreeCrossfadeTag and LodCrossfade components.
        /// This is designed for baking a unique LOD mask that applies to the entire entity. Do not use this if you intend
        /// to use UseMmiRangeLodTag.
        /// </summary>
        /// <param name="baker">The active baker</param>
        /// <param name="targetEntity">The entity to add the components to</param>
        /// <param name="lodSettings">The LOD settings used to derive the components to bake.</param>
        public static void BakeLodMaskForEntity(IBaker baker, Entity targetEntity, LodSettings lodSettings)
        {
            if (!lodSettings.Equals(default(LodSettings)))
            {
                bool negHeight = (lodSettings.lowestResLodLevel & 0x1) == 1;
                bool negMin    = (lodSettings.lowestResLodLevel & 0x2) == 2;
                bool negMax    = (lodSettings.lowestResLodLevel & 0x4) == 4;
                if (negHeight)
                    lodSettings.localHeight *= -1;
                if (negMin)
                    lodSettings.minScreenHeightPercent *= new half(-1f);
                if (negMax)
                    lodSettings.maxScreenHeightPercent *= new half(-1f);

                if (lodSettings.minScreenHeightPercentAtCrossfadeEdge > 0f || lodSettings.maxScreenHeightPercentAtCrossfadeEdge > 0f)
                {
                    if (lodSettings.isSpeedTree)
                        baker.AddComponent<SpeedTreeCrossfadeTag>(targetEntity);

                    if (lodSettings.maxScreenHeightPercentAtCrossfadeEdge < 0f)
                        lodSettings.maxScreenHeightPercentAtCrossfadeEdge = half.MaxValueAsHalf;

                    baker.AddComponent<LodCrossfade>(targetEntity);
                    baker.AddComponent(              targetEntity, new LodHeightPercentagesWithCrossfadeMargins
                    {
                        localSpaceHeight = lodSettings.localHeight,
                        maxCrossFadeEdge = lodSettings.maxScreenHeightPercentAtCrossfadeEdge,
                        maxPercent       = lodSettings.maxScreenHeightPercent,
                        minCrossFadeEdge = lodSettings.minScreenHeightPercentAtCrossfadeEdge,
                        minPercent       = lodSettings.minScreenHeightPercent
                    });
                }
                else
                {
                    baker.AddComponent(targetEntity, new LodHeightPercentages
                    {
                        localSpaceHeight = lodSettings.localHeight,
                        maxPercent       = lodSettings.maxScreenHeightPercent,
                        minPercent       = lodSettings.minScreenHeightPercent
                    });
                }
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
            if (RenderingBakingTools.IsOverridden(this, authoring))
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

            var entity = GetEntity(TransformUsageFlags.Renderable);
            RenderingBakingTools.GetLOD(this, authoring, out var lodSettings);
            RenderingBakingTools.BakeLodMaskForEntity(this, entity, lodSettings);

            int  totalMms        = m_materialsCache.Count;
            var  lodAppend       = GetComponent<LodAppendAuthoring>();
            bool lodAppend1Null  = false;
            bool lodAppend2Null  = false;
            int  lodAppend1Count = 0;
            if (lodAppend != null)
            {
                if (lodAppend.lod1Mesh == null)
                    lodAppend1Null = true;
                else if (lodAppend.useOverrideMaterialsForLod1 && (lodAppend.overrideMaterialsForLod1 == null || lodAppend.overrideMaterialsForLod1.Count == 0))
                    lodAppend1Null = true;
                else
                    totalMms    += lodAppend.useOverrideMaterialsForLod1 ? lodAppend.overrideMaterialsForLod1.Count : m_materialsCache.Count;
                lodAppend1Count  = totalMms - m_materialsCache.Count;

                if (!lodAppend.enableLod2)
                    lodAppend2Null = true;
                else if (lodAppend.lod2Mesh == null)
                    lodAppend2Null = true;
                else if (lodAppend.useOverrideMaterialsForLod2 && (lodAppend.overrideMaterialsForLod2 == null || lodAppend.overrideMaterialsForLod2.Count == 0))
                    lodAppend2Null = true;
                else
                    totalMms += lodAppend.useOverrideMaterialsForLod2 ? lodAppend.overrideMaterialsForLod2.Count : m_materialsCache.Count;
            }
            Span<MeshMaterialSubmeshSettings> mms = stackalloc MeshMaterialSubmeshSettings[totalMms];
            if (lodAppend != null)
            {
                var lod0 = mms.Slice(0, m_materialsCache.Count);
                RenderingBakingTools.ExtractMeshMaterialSubmeshes(lod0, mesh, m_materialsCache, 0x01);
                if (!lodAppend1Null)
                {
                    if (lodAppend.lod1Mesh.subMeshCount != mesh.subMeshCount && !lodAppend.useOverrideMaterialsForLod1)
                    {
                        UnityEngine.Debug.LogWarning(
                            $"In {authoring.gameObject.name}, lod1Mesh {lodAppend.lod1Mesh.name} has a different submesh count {lodAppend.lod1Mesh.subMeshCount} than the main mesh {mesh.name} count {mesh.subMeshCount}, but does not use override materials. This will often lead to incorrect rendering.");
                    }

                    var lod1     = mms.Slice(m_materialsCache.Count, lodAppend1Count);
                    var lod1Mats = lodAppend.useOverrideMaterialsForLod1 ? lodAppend.overrideMaterialsForLod1 : m_materialsCache;
                    RenderingBakingTools.ExtractMeshMaterialSubmeshes(lod1, lodAppend.lod1Mesh, lod1Mats, (byte)(lodAppend.enableLod2 ? 0x02 : 0xfe));
                }

                if (!lodAppend2Null)
                {
                    if (lodAppend.lod2Mesh.subMeshCount != mesh.subMeshCount && !lodAppend.useOverrideMaterialsForLod2)
                    {
                        UnityEngine.Debug.LogWarning(
                            $"In {authoring.gameObject.name}, lod2Mesh {lodAppend.lod2Mesh.name} has a different submesh count {lodAppend.lod2Mesh.subMeshCount} than the main mesh {mesh.name} count {mesh.subMeshCount}, but does not use override materials. This will often lead to incorrect rendering.");
                    }

                    var lod2     = mms.Slice(m_materialsCache.Count + lodAppend1Count);
                    var lod2Mats = lodAppend.useOverrideMaterialsForLod2 ? lodAppend.overrideMaterialsForLod2 : m_materialsCache;
                    RenderingBakingTools.ExtractMeshMaterialSubmeshes(lod2, lodAppend.lod2Mesh, lod2Mats, 0xfc);
                }
            }
            else
                RenderingBakingTools.ExtractMeshMaterialSubmeshes(mms, mesh, m_materialsCache);

            var opaqueMaterialCount = RenderingBakingTools.GroupByDepthSorting(mms);

            var rendererSettings = new MeshRendererBakeSettings
            {
                targetEntity                = entity,
                renderMeshDescription       = new RenderMeshDescription(authoring),
                isDeforming                 = false,
                suppressDeformationWarnings = false,
                useLightmapsIfPossible      = true,
                lightmapIndex               = authoring.lightmapIndex,
                lightmapScaleOffset         = authoring.lightmapScaleOffset,
                isStatic                    = IsStatic(),
                localBounds                 = mesh != null ? mesh.bounds : default,
            };

            MmiRange2LodSelect select2Lod = default;
            MmiRange3LodSelect select3Lod = default;
            if (lodAppend != null)
            {
                if (!lodAppend.enableLod2)
                {
                    select2Lod.height                       = math.cmax(rendererSettings.localBounds.extents) * 2f * math.select(1f, -1f, lodAppend.lod1Mesh == null);
                    select2Lod.fullLod0ScreenHeightFraction = (half)(lodAppend.lod01TransitionMaxPercentage / 100f);
                    select2Lod.fullLod1ScreenHeightFraction = (half)(lodAppend.lod01TransitionMinPercentage / 100f);
                    if (lodAppend.lod01TransitionMaxPercentage < lodAppend.lod01TransitionMinPercentage) // Be nice to the designers
                        (select2Lod.fullLod1ScreenHeightFraction, select2Lod.fullLod0ScreenHeightFraction) =
                            (select2Lod.fullLod0ScreenHeightFraction, select2Lod.fullLod1ScreenHeightFraction);
                    AddComponent(entity, select2Lod);
                }
                else
                {
                    select3Lod.height                          = math.cmax(rendererSettings.localBounds.extents) * 2f * math.select(1f, -1f, lodAppend.lod2Mesh == null);
                    select3Lod.fullLod0ScreenHeightFraction    = (half)(lodAppend.lod01TransitionMaxPercentage / 100f);
                    select3Lod.fullLod1ScreenHeightMaxFraction = (half)(lodAppend.lod01TransitionMinPercentage / 100f);
                    select3Lod.fullLod1ScreenHeightMinFraction = (half)(lodAppend.lod12TransitionMaxPercentage / 100f);
                    select3Lod.fullLod2ScreenHeightFraction    = (half)(lodAppend.lod12TransitionMinPercentage / 100f);
                    AddComponent(entity, select3Lod);
                }
                AddComponent<UseMmiRangeLodTag>(entity);
                AddComponent<LodCrossfade>(     entity);
            }

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
                var additionalEntity = CreateAdditionalEntity(TransformUsageFlags.Renderable, false, $"{GetName()}-TransparentRenderEntity");
                if (lodAppend != null)
                {
                    if (!lodAppend.enableLod2)
                        AddComponent(additionalEntity, select2Lod);
                    else
                        AddComponent(additionalEntity, select3Lod);
                    AddComponent<UseMmiRangeLodTag>(additionalEntity);
                    AddComponent<LodCrossfade>(     additionalEntity);
                }
                RenderingBakingTools.BakeLodMaskForEntity(this, additionalEntity, lodSettings);
                Span <MeshRendererBakeSettings> renderers = stackalloc MeshRendererBakeSettings[2];
                renderers[0]                              = rendererSettings;
                renderers[1]                              = rendererSettings;
                renderers[1].targetEntity                 = additionalEntity;
                Span<int> counts                          = stackalloc int[2];
                counts[0]                                 = opaqueMaterialCount;
                counts[1]                                 = mms.Length - opaqueMaterialCount;
                this.BakeMeshAndMaterial(renderers, mms, counts);
            }
        }
    }
}

