#ifndef CALLIGRAPHICS_TEXT_GLOBALS_API
#define CALLIGRAPHICS_TEXT_GLOBALS_API

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"

uniform ByteAddressBuffer _tmdGlyphs;
TEXTURE2D_ARRAY(_tmdSdf8);
SAMPLER(sampler_tmdSdf8);
TEXTURE2D_ARRAY(_tmdSdf16);
SAMPLER(sampler_tmdSdf16);
TEXTURE2D_ARRAY(_tmdBitmap);
SAMPLER(sampler_tmdBitmap);

// Helpers
float4 UnpackHalfColor(uint2 packedColor)
{
    uint4 expanded = packedColor.xxyy;
    expanded.yw = expanded.yw >> 16u;
    expanded = expanded & 0xffffu;
    return f16tof32(expanded);
}

// Base APIs
void GetGlyph(uint glyphIndex, uint glyphStartIndex, uint glyphCount,
    out float2 blPosition,
    out float2 brPosition,
    out float2 tlPosition,
    out float2 trPosition,

    out float2 blUVB,
    out float2 brUVB,
    out float2 tlUVB,
    out float2 trUVB,

    out float4 blColor,
    out float4 brColor,
    out float4 tlColor,
    out float4 trColor,

    out float2 blUVA,
    out float2 trUVA,

    out float arrayIndex,
    out uint glyphEntryId,
    out float scale,
    out uint reserved)
{
    if (glyphIndex >= glyphCount)
    {
        blPosition = asfloat(~0u);
        brPosition = blPosition;
        tlPosition = blPosition;
        trPosition = blPosition;
        blUVB = blPosition;
        brUVB = blPosition;
        tlUVB = blPosition;
        trUVB = blPosition;
        blColor = 0;
        brColor = blColor;
        tlColor = blColor;
        trColor = blColor;
        blUVA = blPosition;
        trUVA = blPosition;
        arrayIndex = 0;
        glyphEntryId = 0;
        scale = 0;
        reserved = 0u;        
    }
    else
    {
        uint baseAddress = (glyphStartIndex + glyphIndex) * 128;
        uint4 load0_15 = _tmdGlyphs.Load4(baseAddress);
        blPosition = asfloat(load0_15.xy);
        brPosition = asfloat(load0_15.zw);
        uint4 load16_31 = _tmdGlyphs.Load4(baseAddress + 16);
        tlPosition = asfloat(load16_31.xy);
        trPosition = asfloat(load16_31.zw);

        uint4 load32_47 = _tmdGlyphs.Load4(baseAddress + 32);
        blUVB = asfloat(load32_47.xy);
        brUVB = asfloat(load32_47.zw);
        uint4 load48_63 = _tmdGlyphs.Load4(baseAddress + 48);
        tlUVB = asfloat(load48_63.xy);
        trUVB = asfloat(load48_63.zw);

        uint4 load64_79 = _tmdGlyphs.Load4(baseAddress + 64); //load half4 blColor and half4 brColor
        blColor = UnpackHalfColor(load64_79.xy); //convert blColor from half4 to float4
        brColor = UnpackHalfColor(load64_79.zw); //convert brColor from half4 to float4
        uint4 load80_95 = _tmdGlyphs.Load4(baseAddress + 80);
        tlColor = UnpackHalfColor(load80_95.xy);
        trColor = UnpackHalfColor(load80_95.zw);

        uint4 load96_111 = _tmdGlyphs.Load4(baseAddress + 96);
        blUVA = asfloat(load96_111.xy);
        trUVA = asfloat(load96_111.zw);

        uint4 load112_127 = _tmdGlyphs.Load4(baseAddress + 112);
        arrayIndex = asfloat(load112_127.x);
        glyphEntryId = load112_127.y;
        scale = asfloat(load112_127.z);
        reserved = load112_127.w;
    }
    return;
}

// Corner order:  bl = 0, tl = 1, tr = 2, br = 3
void GetGlyphCorner(uint glyphIndex, uint cornerIndex, uint glyphStartIndex, uint glyphCount, out float2 position, out float3 uvA, out float2 uvB, out float4 color, out float scale, out uint glyphEntryID)
{
    float2 blPosition;
    float2 brPosition;
    float2 tlPosition;
    float2 trPosition;
    float2 blUVB;
    float2 brUVB;
    float2 tlUVB;
    float2 trUVB;
    float4 blColor;
    float4 brColor;
    float4 tlColor;
    float4 trColor;
    float2 blUVA;
    float2 trUVA;
    float arrayIndex;
    uint reserved;
    GetGlyph(glyphIndex, glyphStartIndex, glyphCount, blPosition, brPosition, tlPosition, trPosition, blUVB, brUVB, tlUVB, trUVB, blColor, brColor, tlColor, trColor, blUVA, trUVA, arrayIndex, glyphEntryID, scale, reserved);
    if (cornerIndex == 0)
    {
        // bottom left
        position = blPosition;
        uvA = float3(blUVA, arrayIndex);
        //uvB = blUVB;
        uvB = float2(0, 0);
        color = blColor;
    }
    else if (cornerIndex == 1)
    {
        // top left
        position = tlPosition;
        uvA = float3(blUVA.x, trUVA.y, arrayIndex);
        //uvB = tlUVB;
        uvB = float2(0, 1);
        color = tlColor;
    }
    else if (cornerIndex == 2)
    {
        // top right
        position = trPosition;
        uvA = float3(trUVA, arrayIndex);
        //uvB = trUVB;
        uvB = float2(1, 1);
        color = trColor;
    }
    else
    {
        // bottom right
        position = brPosition;
        uvA = float3(trUVA.x, blUVA.y, arrayIndex);
        //uvB = brUVB;
        uvB = float2(1, 0);
        color = brColor;
    }
}

void ExtractGlyphFlagsFromEntryID(uint glyphEntryID, out bool isSdf16, out bool isBitmap, out float texelsInDilationDomain)
{
    uint format = glyphEntryID >> 30u;
    isSdf16 = format == 1 || format == 2;
    isBitmap = format == 3;
    switch (format)
    {
    
        case 0:
            texelsInDilationDomain = 12.0;
            break;
        case 1:
            texelsInDilationDomain = 32.0;
            break;
        case 2:
            texelsInDilationDomain = 128.0;
            break;
        case 3:
        default:
            texelsInDilationDomain = 0.0;
            break;
    }
    texelsInDilationDomain *= 2.0; // This is double the spread.
}

void GetGlyphIndexAndCornerFromQuadVertexID(uint vertexID, out uint glyphIndex, out uint cornerIndex)
{
    glyphIndex = vertexID >> 2u;
    cornerIndex = vertexID & 3u;
}

//API to sample Bitmap and SDF TEXTURE2D_ARRAY

// Todo: This causes Unity's shader compiler to break. Attempt to reenable this later.
//UnityTexture2DArray GetSdfTextureArray(bool is16Bit)
//{
//    if (is16Bit)
//    {
//        return UnityBuildTexture2DArrayStruct(_tmdSdf16);
//    }
//    else
//    {
//        return UnityBuildTexture2DArrayStruct(_tmdSdf8);
//    }
//}

#endif