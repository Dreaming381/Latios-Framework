using Latios.Authoring;
using Latios.Kinemation.Authoring;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Graphics;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace Latios.Calligraphics.Authoring
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Latios/Calligraphics/Text Renderer (Calligraphics)")]
    public class TextRendererAuthoring : MonoBehaviour
    {
        public FontCollectionAsset fontCollectionAsset;
        [Tooltip("Select the default font family for this TextRenderer. Ensure to assign the font collection asset first")]
        public string defaultFont;

        [TextArea(5, 10)]
        public string text;

        [EnumButtons]
        public FontStyles fontStyles = FontStyles.Normal;

        public float                      fontSize            = 12f;
        public Color32                    color               = Color.white;
        public HorizontalAlignmentOptions horizontalAlignment = HorizontalAlignmentOptions.Left;
        public VerticalAlignmentOptions   verticalAlignment   = VerticalAlignmentOptions.TopAscent;
        public bool                       wordWrap            = true;
        public float                      maxLineWidth        = 30;
        public bool                       isOrthographic      = false;
        [Tooltip("Additional word spacing in font units where a value of 1 equals 1/100em.")]
        public float wordSpacing = 0;
        [Tooltip("Additional line spacing in font units where a value of 1 equals 1/100em.")]
        public float lineSpacing = 0;
        [Tooltip("Paragraph spacing in font units where a value of 1 equals 1/100em.")]
        public float paragraphSpacing = 0;
        [Tooltip("Use BCP 47 conform tags to set the language of this text https://en.wikipedia.org/wiki/IETF_language_tag#List_of_common_primary_language_subtags)")]
        public string          language = "en";
        public Material        material;
        public FontTextureSize fontTextureSize;

        // Todo: Expose renderer settings?
    }

    class TextRendererBaker : Baker<TextRendererAuthoring>
    {
        public override void Bake(TextRendererAuthoring authoring)
        {
            DependsOn(authoring.fontCollectionAsset);
            int fontCount = 0;
            if (authoring.fontCollectionAsset == null ||
                (fontCount = authoring.fontCollectionAsset.fontLoadDescriptions.Count) == 0 ||
                authoring.defaultFont == string.Empty ||
                authoring.material == null ||
                authoring.language.Length == 0)
                return;

            var backendMesh = Resources.Load<Mesh>(TextBackendBakingUtility.kTextBackendMeshResource);

            var entity = GetEntity(TransformUsageFlags.Renderable);
            AddComponent<TextShaderIndex>(entity);
            AddBuffer<RenderGlyph>(entity);
            var calliByte   = AddBuffer<CalliByte>(entity);
            var calliString = new CalliString(calliByte);
            calliString.Append(authoring.text);
            var textBaseConfiguraton = new TextBaseConfiguration
            {
                defaultFontFamilyHash = TextHelper.GetHashCodeCaseInsensitive(authoring.defaultFont),
                fontSize              = (half)authoring.fontSize,
                color                 = authoring.color,
                maxLineWidth          = math.select(float.MaxValue, authoring.maxLineWidth, authoring.wordWrap),
                lineJustification     = authoring.horizontalAlignment,
                verticalAlignment     = authoring.verticalAlignment,
                isOrthographic        = authoring.isOrthographic,
                fontStyles            = authoring.fontStyles,
                fontWeight            = (authoring.fontStyles & FontStyles.Bold) == FontStyles.Bold ? FontWeight.Bold : FontWeight.Normal,
                fontWidth             = FontWidth.Normal,  //cannot be set from UI,
                wordSpacing           = (half)authoring.wordSpacing,
                lineSpacing           = (half)authoring.lineSpacing,
                paragraphSpacing      = (half)authoring.paragraphSpacing,
                language              = this.BakeLanguageStringBlob(authoring.language),
                fontTextureSize       = authoring.fontTextureSize
            };
            AddComponent(entity, textBaseConfiguraton);

            var fontCollectionBaker = new FontCollectionAuthoringSmartBakeItem();
            fontCollectionBaker.Bake(authoring.fontCollectionAsset, this);
            this.AddPostProcessItem(entity, fontCollectionBaker);

            var layer            = GetLayer();
            var rendererSettings = new MeshRendererBakeSettings
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
            };
            this.BakeMeshAndMaterial(rendererSettings, backendMesh, authoring.material);
        }
    }

    public static class LanguageBakerExtensions
    {
        /// <summary>
        /// Bakes the BCP 47 language string into a LanguageBlob asset
        /// </summary>
        public static BlobAssetReference<LanguageBlob> BakeLanguageStringBlob(this IBaker baker, in FixedString128Bytes language)
        {
            var customHash = new Unity.Entities.Hash128((uint)language.GetHashCode(), 0, 0, 0);
            if (!baker.TryGetBlobAssetReference(customHash, out BlobAssetReference<LanguageBlob> blobReference))
            {
                blobReference = TextRendererUtility.BakeLanguage(language);
                baker.AddBlobAssetWithCustomHash(ref blobReference, customHash);  // Register the Blob Asset to the Baker for de-duplication and reverting.
            }
            return blobReference;
        }

        /// <summary>
        /// Converts the Opentype language tag into an approximate BCP 47 language string, and then bakes the language string into a LanguageBlob asset
        /// </summary>
        public static BlobAssetReference<LanguageBlob> BakeLanguageStringBlob(this IBaker baker, char first, char second, char third, char fourth = ' ')
        {
            var language = HarfBuzz.Language.OpentypeTagToHBLanguage(HarfBuzz.Harfbuzz.HB_TAG(first, second, third, fourth)).LanguageToFixedString();
            return BakeLanguageStringBlob(baker, in language);
        }
    }
}

