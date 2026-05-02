using System;
using System.Collections.Generic;
using Latios.Calligraphics.HarfBuzz;
using Object = UnityEngine.Object;
using Unity.Collections;
using UnityEngine;

namespace Latios.Calligraphics.Authoring
{
    [CreateAssetMenu(fileName = "FontCollectionAsset",
                     menuName = "Latios/Calligraphics/Font Collection Asset")]
    public class FontCollectionAsset : ScriptableObject
    {
        [Tooltip(
             "Drop here Unity Font assets of system font files (.otf .ttf .ttc). Disable \"Include Font Data\" option in these Unity Font assets to ensure fonts are NOT included in your build.")
        ]
        public List<Object> systemFonts;
        [Tooltip("Drop here .otf .ttf .ttc files located in Asset/StreamingAssets(/subfolder)")]
        public List<Object>              streamingAssetFonts;
        public List<FontLoadDescription> fontLoadDescriptions;
        public List<string>              fontFamilies;

#if UNITY_EDITOR
        public void ProcessFonts()
        {
            if (this.fontLoadDescriptions == null)
                this.fontLoadDescriptions = new List<FontLoadDescription>(streamingAssetFonts.Count + systemFonts.Count);
            else
                this.fontLoadDescriptions.Clear();

            var tempFontLoadDescriptions = new NativeList<FontLoadDescription>(streamingAssetFonts.Count + systemFonts.Count, Allocator.Temp);
            // The language is used to get a localized string, if the font has one.
            // For consistency, we always want to use the same values, so we pick English.
            var language = Language.English;

            for (int i = 0, ii = systemFonts.Count; i < ii; i++)
            {
                var fontItem      = systemFonts[i];
                var fontAssetPath = UnityEditor.AssetDatabase.GetAssetPath(fontItem);
                TextHelper.GetFontInfo(fontAssetPath, true, language, tempFontLoadDescriptions);
            }

            for (int i = 0, ii = streamingAssetFonts.Count; i < ii; i++)
            {
                var fontItem      = streamingAssetFonts[i];
                var fontAssetPath = UnityEditor.AssetDatabase.GetAssetPath(fontItem);
                TextHelper.GetFontInfo(fontAssetPath, false, language, tempFontLoadDescriptions);
            }

            foreach (var fontItem in tempFontLoadDescriptions)
                fontLoadDescriptions.Add(fontItem);

            if (fontFamilies == null)
                fontFamilies = new List<string>(this.fontLoadDescriptions.Count);
            else
                fontFamilies.Clear();

            for (int i = 0, ii = tempFontLoadDescriptions.Length; i < ii; i++)
            {
                var FontLoadDescription = tempFontLoadDescriptions[i];
                var fontFamily          = FontLoadDescription.typographicFamily ==
                                          String.Empty ? FontLoadDescription.fontFamily.ToString() : FontLoadDescription.typographicFamily.ToString();
                if (!fontFamilies.Contains(fontFamily))
                    fontFamilies.Add(fontFamily);
            }
            //ensure values are serialized
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}

