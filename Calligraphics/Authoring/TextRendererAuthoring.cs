using System;
using System.Collections.Generic;
using Latios.Authoring;
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
        [Multiline]
        public string text;

        public float               fontSize            = 12f;
        public bool                wordWrap            = true;
        public float               maxLineWidth        = float.MaxValue;
        public HorizontalAlignment horizontalAlignment = HorizontalAlignment.Left;
        public VerticalAlignment   verticalAlignment   = VerticalAlignment.Top;

        public Color32 color = UnityEngine.Color.white;

        public List<FontMaterialPair> fontsAndMaterials;

        public enum HorizontalAlignment : byte
        {
            Left = 0x0,
            Right = 0x1,
            Center = 0x2,
            Justified = 0x3
        }

        public enum VerticalAlignment : byte
        {
            Top = 0x0,
            Middle = 0x1 << 2,
            Bottom = 0x2 << 2,
        }
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

            //Fonts
            AddFontRendering(entity, authoring.fontsAndMaterials[0]);
            if (authoring.fontsAndMaterials.Count > 1)
            {
                var additionalEntities = AddBuffer<AdditionalFontMaterialEntity>(entity).Reinterpret<Entity>();
                for (int i = 1; i < authoring.fontsAndMaterials.Count; i++)
                {
                    var newEntity = CreateAdditionalEntity(TransformUsageFlags.Renderable);
                    AddFontRendering(newEntity, authoring.fontsAndMaterials[i]);
                    additionalEntities.Add(newEntity);
                }
            }

            //Text Content
            var calliString = new CalliString(AddBuffer<CalliByte>(entity));
            calliString.Append(authoring.text);
            AddComponent(entity, new TextBaseConfiguration
            {
                fontSize     = authoring.fontSize,
                color        = authoring.color,
                maxLineWidth = math.select(float.MaxValue, authoring.maxLineWidth, authoring.wordWrap),
                alignMode    = (AlignMode)(((byte)authoring.horizontalAlignment) | ((byte)authoring.verticalAlignment))
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

