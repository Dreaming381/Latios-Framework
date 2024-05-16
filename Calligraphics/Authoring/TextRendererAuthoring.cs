using System;
using System.Collections.Generic;
using Latios.Authoring;
using Latios.Calligraphics.Rendering;
using Latios.Calligraphics.Rendering.Authoring;
using Latios.Kinemation.Authoring;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Graphics;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TextCore.Text;

namespace Latios.Calligraphics.Authoring
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Latios/Calligraphics/Text Renderer")]
    public class TextRendererAuthoring : MonoBehaviour
    {
        [TextArea(5, 10)]
        public string text;

        public float                      fontSize            = 12f;
        public bool                       wordWrap            = true;
        public float                      maxLineWidth        = float.MaxValue;
        public HorizontalAlignmentOptions horizontalAlignment = HorizontalAlignmentOptions.Left;
        public VerticalAlignmentOptions   verticalAlignment   = VerticalAlignmentOptions.TopAscent;
        public bool                       isOrthographic      = false;
        public bool                       enableKerning       = true;
        public FontStyles                 fontStyle           = FontStyles.Normal;
        public FontWeight                 fontWeight          = FontWeight.Regular;
        [Tooltip("Additional word spacing in font units where a value of 1 equals 1/100em.")]
        public float wordSpacing = 0;
        [Tooltip("Additional line spacing in font units where a value of 1 equals 1/100em.")]
        public float lineSpacing = 0;
        [Tooltip("Paragraph spacing in font units where a value of 1 equals 1/100em.")]
        public float paragraphSpacing = 0;

        public Color32 color = UnityEngine.Color.white;

        public List<FontMaterialPair> fontsAndMaterials;

        [Tooltip("If enabled, text is stored perpetually in GPU memory and always uploaded on change, regardless of visibility.")]
        public bool gpuResident;
    }

    [Serializable]
    public struct FontMaterialPair
    {
        public FontAsset font;
        public Material  overrideMaterial;

        public Material material => overrideMaterial == null ? font.material : overrideMaterial;
    }

    [TemporaryBakingType]
    internal class TextRendererBaker : Baker<TextRendererAuthoring>
    {
        public override void Bake(TextRendererAuthoring authoring)
        {
            if (authoring.fontsAndMaterials == null || authoring.fontsAndMaterials.Count == 0)
                return;

            var entity = GetEntity(TransformUsageFlags.Renderable);

            // Fonts and rendering
            AddFontRendering(entity, authoring.fontsAndMaterials[0]);
            AddBuffer<RenderGlyph>(entity);
            if (authoring.gpuResident)
                AddComponent<GpuResidentTextTag>(entity);

            if (authoring.fontsAndMaterials.Count > 1)
            {
                AddComponent<TextMaterialMaskShaderIndex>(entity);
                AddBuffer<FontMaterialSelectorForGlyph>(entity);
                AddBuffer<RenderGlyphMask>(             entity);
                var additionalEntities = AddBuffer<Rendering.AdditionalFontMaterialEntity>(entity).Reinterpret<Entity>();
                for (int i = 1; i < authoring.fontsAndMaterials.Count; i++)
                {
                    if (authoring.fontsAndMaterials[i].font == null)
                        continue;
                    var newEntity = CreateAdditionalEntity(TransformUsageFlags.Renderable);
                    AddFontRendering(newEntity, authoring.fontsAndMaterials[i]);
                    AddComponent<TextMaterialMaskShaderIndex>(newEntity);
                    AddBuffer<RenderGlyphMask>(newEntity);
                    additionalEntities.Add(newEntity);
                    if (authoring.gpuResident)
                        AddComponent<GpuResidentTextTag>(newEntity);
                }
            }

            // Text Content
            var calliString = new CalliString(AddBuffer<CalliByte>(entity));
            calliString.Append(authoring.text);
            AddComponent(entity, new TextBaseConfiguration
            {
                fontSize          = authoring.fontSize,
                color             = authoring.color,
                maxLineWidth      = math.select(float.MaxValue, authoring.maxLineWidth, authoring.wordWrap),
                lineJustification = authoring.horizontalAlignment,
                verticalAlignment = authoring.verticalAlignment,
                isOrthographic    = authoring.isOrthographic,
                enableKerning     = authoring.enableKerning,
                fontStyle         = authoring.fontStyle,
                fontWeight        = authoring.fontWeight,
                wordSpacing = authoring.wordSpacing,
                lineSpacing = authoring.lineSpacing,
                paragraphSpacing = authoring.paragraphSpacing,
            });
        }

        void AddFontRendering(Entity entity, FontMaterialPair fontMaterialPair)
        {
            if (fontMaterialPair.font == null)
                return;
            DependsOn(fontMaterialPair.font);
            DependsOn(fontMaterialPair.material);
            var layer = GetLayer();
            this.BakeTextBackendMeshAndMaterial(new MeshRendererBakeSettings
            {
                targetEntity          = entity,
                renderMeshDescription = new RenderMeshDescription
                {
                    FilterSettings = new RenderFilterSettings
                    {
                        Layer              = layer,
                        RenderingLayerMask = (uint)(1 << layer),
                        ShadowCastingMode  = ShadowCastingMode.Off,
                        ReceiveShadows     = false,
                        MotionMode         = MotionVectorGenerationMode.Object,
                        StaticShadowCaster = false,
                    },
                    LightProbeUsage = LightProbeUsage.Off,
                },
            }, fontMaterialPair.material);

            AddComponent<FontBlobReference>(entity);

            this.AddPostProcessItem(entity, new FontBlobBakeItem
            {
                fontBlobHandle = this.RequestCreateBlobAsset(fontMaterialPair.font, fontMaterialPair.material)
            });
        }
    }

    struct FontBlobBakeItem : ISmartPostProcessItem
    {
        public SmartBlobberHandle<FontBlob> fontBlobHandle;

        public void PostProcessBlobRequests(EntityManager entityManager, Entity entity)
        {
            entityManager.SetComponentData(entity, new FontBlobReference { blob = fontBlobHandle.Resolve(entityManager) });
        }
    }
}

