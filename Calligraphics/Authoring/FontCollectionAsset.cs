#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Latios.Calligraphics.HarfBuzz;
using Object = UnityEngine.Object;
using Unity.Collections;
using UnityEditor;
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
        public List<Object> streamingAssetFonts;
        public List<FontLoadDescription> fontReferences;
        public List<string> fontFamilies;
        public void ProcessFonts()
        {
            Debug.Log("Process Fonts");
            if (this.fontReferences == null)
                this.fontReferences = new List<FontLoadDescription>(streamingAssetFonts.Count + systemFonts.Count);
            else
                this.fontReferences.Clear();

            var tempFontReferences = new NativeList<FontLoadDescription>(streamingAssetFonts.Count + systemFonts.Count, Allocator.Temp);

            for (int i = 0, ii = systemFonts.Count; i < ii; i++)
            {
                var fontItem      = systemFonts[i];
                var fontAssetPath = AssetDatabase.GetAssetPath(fontItem);
                // The language is used to get a localized string, if the font has one.
                // For consistency, we always want to use the same values, so we pick English.
                TextHelper.GetFontInfo(fontAssetPath, true, Language.English, tempFontReferences);
            }

            for (int i = 0, ii = streamingAssetFonts.Count; i < ii; i++)
            {
                var fontItem      = streamingAssetFonts[i];
                var fontAssetPath = AssetDatabase.GetAssetPath(fontItem);
                // The language is used to get a localized string, if the font has one.
                // For consistency, we always want to use the same values, so we pick English.
                TextHelper.GetFontInfo(fontAssetPath, false, Language.English, tempFontReferences);
            }

            foreach (var fontItem in tempFontReferences)
                fontReferences.Add(fontItem);

            if (fontFamilies == null)
                fontFamilies = new List<string>(this.fontReferences.Count);
            else
                fontFamilies.Clear();

            for (int i = 0, ii = tempFontReferences.Length; i < ii; i++)
            {
                var fontReference = tempFontReferences[i];
                var fontFamily    = fontReference.typographicFamily == String.Empty ? fontReference.fontFamily.ToString() : fontReference.typographicFamily.ToString();
                if (!fontFamilies.Contains(fontFamily))
                    fontFamilies.Add(fontFamily);
            }
            //ensure values are serialized
            EditorUtility.SetDirty(this);
        }
    }
}
#endif

