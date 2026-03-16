using System.IO;
using Font = Latios.Calligraphics.HarfBuzz.Font;
using Latios.Calligraphics.HarfBuzz;
using Unity.Collections;
using UnityEngine;

namespace Latios.Calligraphics
{
    internal static class TextHelper
    {
        internal static bool IsValidFont(string fontAssetPath)
        {
            bool isTrueType           = fontAssetPath.EndsWith("ttf", System.StringComparison.OrdinalIgnoreCase);
            bool isTrueTypeCollection = fontAssetPath.EndsWith("ttc", System.StringComparison.OrdinalIgnoreCase);
            bool isOpentype           = fontAssetPath.EndsWith("otf", System.StringComparison.OrdinalIgnoreCase);
            return isOpentype || isTrueType || isTrueTypeCollection;
        }
        internal static bool GetFontInfo(string fontAssetPath, bool useSystemFont, Language language, NativeList<FontLoadDescription> fontReferences)
        {
            if (IsValidFont(fontAssetPath))
            {
                var                 blob              = new Blob(fontAssetPath);
                FontLoadDescription baseFontReference = new FontLoadDescription();
                baseFontReference.isSystemFont        = useSystemFont;
                if (useSystemFont)
                    baseFontReference.filePath = fontAssetPath;
                else
                    ValidateStreamingAssetPath(fontAssetPath, ref baseFontReference);

                GetFaceInfo(blob, language, baseFontReference, fontReferences);
                blob.Dispose();
                return true;
            }
            else
            {
                Debug.LogWarning("Ensure you only have files ending with 'ttf' or 'otf' (case insensitiv) in font list");
                return false;
            }
        }
        internal static void ValidateStreamingAssetPath(string fontAssetPath, ref FontLoadDescription fontReference)
        {
            var pathIndex = fontAssetPath.IndexOf("Assets/StreamingAssets");
            if (pathIndex == -1)
            {
                Debug.LogError(
                    $"Unless you want to use System Fonts, the source font asset MUST be in \"Assets/StreamingAssets\"! Font cannot be loaded in a build from current location \"{fontAssetPath}\"");
                fontReference.streamingAssetLocationValidated = false;
                fontReference.filePath                        = Path.GetFullPath(fontAssetPath);
            }
            else
            {
                fontReference.streamingAssetLocationValidated = true;
                fontReference.filePath                        = fontAssetPath.Substring(pathIndex + 23);
            }
        }
        internal static bool GetFaceInfo(Blob blob, Language language, FontLoadDescription validatedFontReference, NativeList<FontLoadDescription> fontReferences)
        {
            for (int i = 0, ii = blob.FaceCount; i < ii; i++)
            {
                var face                                      = new Face(blob, i);
                var fontReference                             = new FontLoadDescription();
                fontReference.filePath                        = validatedFontReference.filePath;
                fontReference.isSystemFont                    = validatedFontReference.isSystemFont;
                fontReference.streamingAssetLocationValidated = validatedFontReference.streamingAssetLocationValidated;

                fontReference.faceIndexInFile      = i;
                fontReference.fontFamily           = face.GetName(NameID.FONT_FAMILY, language);
                fontReference.fontSubFamily        = face.GetName(NameID.FONT_SUBFAMILY, language);
                fontReference.typographicFamily    = face.GetName(NameID.TYPOGRAPHIC_FAMILY, language);
                fontReference.typographicSubfamily = face.GetName(NameID.TYPOGRAPHIC_SUBFAMILY, language);

                var font                    = new Font(face);
                fontReference.defaultWeight = font.GetStyleTag(StyleTag.WEIGHT);
                fontReference.defaultWidth  = font.GetStyleTag(StyleTag.WIDTH);
                fontReference.isItalic      = font.GetStyleTag(StyleTag.ITALIC) == 1;
                fontReference.slant         = font.GetStyleTag(StyleTag.SLANT_ANGLE);

                if (!fontReferences.Contains(fontReference))
                    fontReferences.Add(fontReference);
                // Duplicates are expected due to langauge support
                //else if(!validatedFontReference.isSystemFont)
                //    Debug.LogWarning($"font {sFontReference.fontFamily} {sFontReference.fontSubFamily} (system font? {sFontReference.isSystemFont}) was previously added to the list of fonts");

                face.Dispose();
                font.Dispose();
            }
            return true;
        }
        public static int GetHashCodeCaseInsensitive(FixedString128Bytes text)
        {
            var s   = text.GetEnumerator();
            int num = 0;
            while (s.MoveNext())
            {
                num = ((num << 5) + num) ^ s.Current.ToUpper().value;
            }
            return num;
        }
        public static int GetValueHash(FixedString128Bytes text)
        {
            var s   = text.GetEnumerator();
            int num = 0;
            while (s.MoveNext())
            {
                num = (num << 5) + num ^ s.Current.value;
                //num = ((num << 5) + num) ^ s.Current.ToUpper().value;
            }
            return num;
        }
    }
}

