using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Calligraphics
{
    public static class TextRendererUtility
    {
        /// <summary>
        /// Convert a BCP 47 compliant langugage string into a blob asset. Risk of creating leaks when creating
        /// such a blobasset at runtime is high: ensure to dispose it when there are no more BlobAssetReferences to this blob asset.
        /// This is a non issue for the baking workflow as this is handled automatically.
        /// </summary>
        public static BlobAssetReference<LanguageBlob> BakeLanguage(FixedString128Bytes language)
        {
            var              blobBuilder      = new BlobBuilder(Allocator.Temp, 1024);
            ref LanguageBlob languageBlobRoot = ref blobBuilder.ConstructRoot<LanguageBlob>();
            blobBuilder.AllocateString(ref languageBlobRoot.langugage, ref language);
            var result = blobBuilder.CreateBlobAssetReference<LanguageBlob>(Allocator.Persistent);
            blobBuilder.Dispose();
            languageBlobRoot = result.Value;
            return result;
        }

        /// <summary>
        /// Convert an opentype language tag into a blob asset. Risk of creating leaks when creating
        /// such a blobasset at runtime is high: ensure to dispose it when there are no more BlobAssetReferences to this blob asset.
        /// This is a non issue for the baking workflow as this is handled automatically.
        /// </summary>
        public static BlobAssetReference<LanguageBlob> BakeLanguage(char first, char second, char third, char fourth = ' ')
        {
            var language = HarfBuzz.Language.OpentypeTagToHBLanguage(HarfBuzz.Harfbuzz.HB_TAG(first, second, third, fourth)).LanguageToFixedString();
            return BakeLanguage(language);
        }

        /// <summary>
        /// Get TextBaseConfiguration IComponent by providing all required data. Note: for achitectural
        /// reasons we cannot avoid providing the language as blob asset. Risk of creating leaks when creating
        /// such a blob asset at runtime is high: ensure to dispose it when there are no more BlobAssetReferences
        /// to this blob asset. This is a non issue for the baking workflow as this is handled automatically.
        /// </summary>
        public static TextBaseConfiguration GetTextBaseConfiguration(
            BlobAssetReference<LanguageBlob> language,
            FixedString128Bytes fontName,
            int fontSize,
            Color32 color,
            float maxLineWidth,
            HorizontalAlignmentOptions lineJustification,
            VerticalAlignmentOptions verticalAlignment,
            FontStyles fontStyles,
            FontWeight fontWeight,
            FontWidth fontWidth,
            float wordSpacing,
            float lineSpacing,
            float paragraphSpacing,
            bool isOrthographic
            )
        {
            return new TextBaseConfiguration
            {
                defaultFontFamilyHash = TextHelper.GetHashCodeCaseInsensitive(fontName),
                fontSize              = (half)fontSize,
                color                 = color,
                maxLineWidth          = maxLineWidth,
                lineJustification     = lineJustification,
                verticalAlignment     = verticalAlignment,
                isOrthographic        = isOrthographic,
                fontStyles            = fontStyles,
                fontWeight            = fontWeight,
                fontWidth             = fontWidth,
                wordSpacing           = (half)wordSpacing,
                lineSpacing           = (half)lineSpacing,
                paragraphSpacing      = (half)paragraphSpacing,
                language              = language
            };
        }
    }
}

