#if !LATIOS_DISABLE_CALLIGRAPHICS
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;

public static class TextCoreExtensions
{
#if !UNITY_6000_3_OR_NEWER
    // Obsolete. Will be removed in a future feature release.
    public static Dictionary<uint, GlyphPairAdjustmentRecord> GetGlyphPairAdjustmentRecordLookup(this FontAsset font)
    {
        return font.fontFeatureTable.m_GlyphPairAdjustmentRecordLookup;
    }
#endif

    public static List<GlyphPairAdjustmentRecord> GetGlyphPairAdjustmentRecords(this FontAsset font)
    {
#if UNITY_6000_3_OR_NEWER
        return font.fontFeatureTable.GetType().GetRuntimeProperty("glyphPairAdjustmentRecords")
               .GetValue(font.fontFeatureTable, BindingFlags.NonPublic | BindingFlags.Instance, null, null, null)
               as List<GlyphPairAdjustmentRecord>;
#else
        return font.fontFeatureTable.glyphPairAdjustmentRecords;
#endif
    }

    public static float GetPaddingForText(this Material material, bool enableExtraPadding, bool isBold)
    {
#if UNITY_6000_3_OR_NEWER
        return (float)typeof(TextShaderUtilities).GetMethod("GetPadding", BindingFlags.Static | BindingFlags.NonPublic)
               .Invoke(null, new object[] { material, enableExtraPadding, isBold });
#else
        return TextShaderUtilities.GetPadding(material, enableExtraPadding, isBold);
#endif
    }
}
#endif

