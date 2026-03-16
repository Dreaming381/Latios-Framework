#if UNITY_EDITOR
using System.IO;
using Font = Latios.Calligraphics.HarfBuzz.Font;
using Latios.Calligraphics.HarfBuzz;
using UnityEditor;
using UnityEngine;

namespace Latios.Calligraphics.Authoring
{
    [CreateAssetMenu(fileName = "FontUtility", menuName = "Latios/Calligraphics/FontUtility")]

    // Use this utility to get the information requiered for spawning TextRenderer at runtime vai FontRequest. See
    // RuntimeSpawner/RuntimeSingleFontTextRendererSpawner and
    // RuntimeSpawner/RuntimeMultiFontTextRendererSpawner
    // Drag and drop a font object into the font field
    public class FontUtility : ScriptableObject
    {
        public Object font;
        public string fontAssetPath;
        public string fontFamily;
        public string fontSubFamily;
        public string typographicFamily;
        public string typographicSubfamily;
        public int weight;
        public int width;
        public string isItalic;
        public int slant;

        public void OnValidate()
        {
            if ((font == null))
                return;

            fontAssetPath = AssetDatabase.GetAssetPath(font);
            bool isTrueType = fontAssetPath.EndsWith("ttf", System.StringComparison.OrdinalIgnoreCase);
            bool isOpentype = fontAssetPath.EndsWith("otf", System.StringComparison.OrdinalIgnoreCase);
            if (isOpentype || isTrueType)
            {
                var fontBytes = File.ReadAllBytes(fontAssetPath);
                Blob blob;
                unsafe
                {
                    fixed (byte* bytes = fontBytes)
                    {
                        blob = new Blob(bytes, (uint)fontBytes.Length, MemoryMode.READONLY);
                    }
                }

                var face = new Face(blob, 0);
                var font = new Font(face);

                //fetch name of fontFamily and subFamily, generate hash code from that used to lookup this font
                var language = Language.English;

                fontFamily           = face.GetName(NameID.FONT_FAMILY, language).ToString();
                fontSubFamily        = face.GetName(NameID.FONT_SUBFAMILY, language).ToString();
                typographicFamily    = face.GetName(NameID.TYPOGRAPHIC_FAMILY, language).ToString();
                typographicSubfamily = face.GetName(NameID.TYPOGRAPHIC_SUBFAMILY, language).ToString();

                weight = (int)font.GetStyleTag(StyleTag.WEIGHT);
                width  = (int)font.GetStyleTag(StyleTag.WIDTH);
                var italic = (byte)font.GetStyleTag(StyleTag.ITALIC);
                isItalic = italic == 1 ? "true" : "false";
                slant    = (int)font.GetStyleTag(StyleTag.SLANT_ANGLE);
                font.Dispose();
                face.Dispose();
                blob.Dispose();
            }
            else
            {
                Debug.LogWarning("Ensure you only have files ending with 'ttf' or 'otf' (case insensitiv) in font list");
                return;
            }
        }
    }
}
#endif

