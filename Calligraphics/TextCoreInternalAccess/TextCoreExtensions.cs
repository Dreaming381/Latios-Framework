using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;

public static class TextCoreExtensions
{
    public static Dictionary<uint, GlyphPairAdjustmentRecord> GetGlyphPairAdjustmentRecordLookup(this FontAsset font)
    {
        return font.fontFeatureTable.m_GlyphPairAdjustmentRecordLookup;
    }

    public static List<GlyphPairAdjustmentRecord> GetGlyphPairAdjustmentRecords(this FontAsset font)
    {
        return font.fontFeatureTable.glyphPairAdjustmentRecords;
    }

    public static bool s_initialized = false;

    public static float GetPaddingForText(this Material material, bool enableExtraPadding, bool isBold)
    {
        if (!s_initialized)
            TextShaderUtilities.GetShaderPropertyIDs();

        return TextShaderUtilities.GetPadding(material, enableExtraPadding, isBold);
    }
}

