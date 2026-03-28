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
        internal static bool GetFontInfo(string fontAssetPath, bool useSystemFont, Language language, NativeList<FontLoadDescription> fontLoadDescriptions)
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

                GetFaceInfo(blob, language, baseFontReference, fontLoadDescriptions);
                blob.Dispose();
                return true;
            }
            else
            {
                Debug.LogWarning("Ensure you only have files ending with 'ttf' or 'otf' (case insensitiv) in font list");
                return false;
            }
        }
        internal static void ValidateStreamingAssetPath(string fontAssetPath, ref FontLoadDescription fontLoadDescription)
        {
            var pathIndex = fontAssetPath.IndexOf("Assets/StreamingAssets");
            if (pathIndex == -1)
            {
                Debug.LogError(
                    $"Unless you want to use System Fonts, the source font asset MUST be in \"Assets/StreamingAssets\"! Font cannot be loaded in a build from current location \"{fontAssetPath}\"");
                fontLoadDescription.streamingAssetLocationValidated = false;
                fontLoadDescription.filePath                        = Path.GetFullPath(fontAssetPath);
            }
            else
            {
                fontLoadDescription.streamingAssetLocationValidated = true;
                fontLoadDescription.filePath                        = fontAssetPath.Substring(pathIndex + 23);
            }
        }
        internal static bool GetFaceInfo(Blob blob, Language language, FontLoadDescription validatedFontLoadDescription, NativeList<FontLoadDescription> fontLoadDescriptions)
        {
            for (int i = 0, ii = blob.FaceCount; i < ii; i++)
            {
                var face                                      = new Face(blob, i);
                var fontLoadDescription                             = new FontLoadDescription();
                fontLoadDescription.filePath                        = validatedFontLoadDescription.filePath;
                fontLoadDescription.isSystemFont                    = validatedFontLoadDescription.isSystemFont;
                fontLoadDescription.streamingAssetLocationValidated = validatedFontLoadDescription.streamingAssetLocationValidated;

                fontLoadDescription.faceIndexInFile      = i;
                fontLoadDescription.fontFamily           = face.GetName(NameID.FONT_FAMILY, language);
                fontLoadDescription.fontSubFamily        = face.GetName(NameID.FONT_SUBFAMILY, language);
                fontLoadDescription.typographicFamily    = face.GetName(NameID.TYPOGRAPHIC_FAMILY, language);
                fontLoadDescription.typographicSubfamily = face.GetName(NameID.TYPOGRAPHIC_SUBFAMILY, language);

                var font                    = new Font(face);
                fontLoadDescription.defaultWeight = font.GetStyleTag(StyleTag.WEIGHT);
                fontLoadDescription.defaultWidth  = font.GetStyleTag(StyleTag.WIDTH);
                fontLoadDescription.isItalic      = font.GetStyleTag(StyleTag.ITALIC) == 1;
                fontLoadDescription.slant         = font.GetStyleTag(StyleTag.SLANT_ANGLE);

                if (!fontLoadDescriptions.Contains(fontLoadDescription))
                    fontLoadDescriptions.Add(fontLoadDescription);
                // Duplicates are expected due to langauge support
                //else if(!validatedFontLoadDescription.isSystemFont)
                //    Debug.LogWarning($"font {fontLoadDescription.fontFamily} {fontLoadDescription.fontSubFamily} (system font? {sFontReference.isSystemFont}) was previously added to the list of fonts");

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

