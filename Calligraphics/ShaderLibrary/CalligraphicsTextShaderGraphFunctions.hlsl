#ifndef CALLIGRAPHICS_TEXT_SHADER_GRAPH_FUNCTIONS
#define CALLIGRAPHICS_TEXT_SHADER_GRAPH_FUNCTIONS

#include "CalligraphicsTextGlobalsApi.hlsl"
#include "CalligraphicsSdfUtils.hlsl"

void GetGlyphFromBuffer_float(float2 textShaderIndex, float vertexID, out float3 position, out float3 normal, out float3 tangent, out float4 vertexColor, out float4 uvAandB, out float4 atlasIndexScaleIsSdf16IsBitmap)
{
    uint glyphIndex;
    uint cornerIndex;
    GetGlyphIndexAndCornerFromQuadVertexID(vertexID, glyphIndex, cornerIndex);
    uint glyphStartIndex = asuint(textShaderIndex.x);
    uint glyphCount = asuint(textShaderIndex.y);
    float2 position2D;
    float3 uvA;
    float2 uvB;
    float4 color;
    float scale;
    uint glyphEntryID;
    GetGlyphCorner(glyphIndex, cornerIndex, glyphStartIndex, glyphCount, position2D, uvA, uvB, color, scale, glyphEntryID);
    bool isSdf16;
    bool isBitmap;
    float texelsInDilationDomain;
    ExtractGlyphFlagsFromEntryID(glyphEntryID, isSdf16, isBitmap, texelsInDilationDomain);
    position = float3(position2D, 0.0);
    normal = float3(0.0, 0.0, -1.0); //text face is pointing forward
    tangent = float3(1.0, 0.0, 0.0);
    vertexColor = color;
    uvAandB = float4(uvA.xy, uvB);
    float bits = 0.0;
    if (isSdf16)
        bits += 1.0;
    if (isBitmap)
        bits -= 1.0;
    atlasIndexScaleIsSdf16IsBitmap = float4(uvA.z, scale, texelsInDilationDomain, bits);
}

//sample SDF 5+4 times: face, outline1, outline2, outline3, underlay, and 4x for light normal
void Sample5Texture2DArrayLIT_float(
    float4 uvAandB,
    float4 atlasIndexScaleIsSdf16IsBitmap,
    bool isFront,
    bool innerBevel,
    float bevelAmount,
    float bevelWidth,
    float bevelRoundness,
    float bevelClamp,
    float2 underlayColorOffset,
    float2 outlineColor1Offset,
    float2 outlineColor2Offset,
    float2 outlineColor3Offset,
    out bool isBitmap,
    out float4 bitmapColor,
    out float2 texelSize,
	out float SDR,
    out float4 SD, //x: face, y: outline1, z: outline2, w: outline3
    out float underlaySD,
    out float3 normal,
	out float2 uvA,
    out float2 uvB,
    out float scale)
{
    uint width, height, elements, numberOfLevels;
    uvA = uvAandB.xy;
    uvB = uvAandB.zw;
    float arrayIndex = atlasIndexScaleIsSdf16IsBitmap.x;
    scale = atlasIndexScaleIsSdf16IsBitmap.y;
    SDR = atlasIndexScaleIsSdf16IsBitmap.z;
    bool isSdf16 = atlasIndexScaleIsSdf16IsBitmap.w > 0.5;
    isBitmap = atlasIndexScaleIsSdf16IsBitmap.w < -0.5;
    SD = 0;
    if (isBitmap)
    {
        _tmdBitmap.GetDimensions(0, width, height, elements, numberOfLevels); // Get dimensions of mip level 0
        texelSize = 1.0f / float2(width, height);
                 
        UnityTexture2DArray bitmap = UnityBuildTexture2DArrayStruct(_tmdBitmap);
        bitmapColor = SAMPLE_TEXTURE2D_ARRAY(bitmap, sampler_LinearClamp, uvA.xy, arrayIndex);
        normal = float3(0, 0, -1);
        underlaySD = 0;
        return;
    }
    else
    {
        bitmapColor = float4(0, 0, 0, 0);
        if (isSdf16)
        {
            _tmdSdf16.GetDimensions(0, width, height, elements, numberOfLevels); // Get dimensions of mip level 0
            texelSize = 1.0f / float2(width, height);
            float offSetScale = SDR * texelSize.x;
            
            outlineColor1Offset *= offSetScale;
            outlineColor2Offset *= offSetScale;
            outlineColor3Offset *= offSetScale;
            underlayColorOffset *= offSetScale;
            
            UnityTexture2DArray sdf = UnityBuildTexture2DArrayStruct(_tmdSdf16);
            SD.x = SAMPLE_TEXTURE2D_ARRAY(sdf, sampler_LinearClamp, uvA.xy, arrayIndex).r;
            SD.y = SAMPLE_TEXTURE2D_ARRAY(sdf, sampler_LinearClamp, uvA.xy - outlineColor1Offset, arrayIndex).r;
            SD.z = SAMPLE_TEXTURE2D_ARRAY(sdf, sampler_LinearClamp, uvA.xy - outlineColor2Offset, arrayIndex).r;
            SD.w = SAMPLE_TEXTURE2D_ARRAY(sdf, sampler_LinearClamp, uvA.xy - outlineColor3Offset, arrayIndex).r;
            underlaySD = SAMPLE_TEXTURE2D_ARRAY(sdf, sampler_LinearClamp, uvA.xy - underlayColorOffset, arrayIndex).r;
            
            normal = getSurfaceNormal(sdf, texelSize, uvA, arrayIndex, SDR, isFront, innerBevel, bevelAmount, bevelWidth, bevelRoundness, bevelClamp);
        }
        else
        {
            _tmdSdf8.GetDimensions(0, width, height, elements, numberOfLevels); // Get dimensions of mip level 0
            texelSize = 1.0f / float2(width, height);
            float offSetScale = SDR * texelSize.x;
            
            outlineColor1Offset *= offSetScale;
            outlineColor2Offset *= offSetScale;
            outlineColor3Offset *= offSetScale;
            underlayColorOffset *= offSetScale;
            
            UnityTexture2DArray sdf = UnityBuildTexture2DArrayStruct(_tmdSdf8);
            SD.x = SAMPLE_TEXTURE2D_ARRAY(sdf, sampler_LinearClamp, uvA.xy, arrayIndex).r;
            SD.y = SAMPLE_TEXTURE2D_ARRAY(sdf, sampler_LinearClamp, uvA.xy - outlineColor1Offset, arrayIndex).r;
            SD.z = SAMPLE_TEXTURE2D_ARRAY(sdf, sampler_LinearClamp, uvA.xy - outlineColor2Offset, arrayIndex).r;
            SD.w = SAMPLE_TEXTURE2D_ARRAY(sdf, sampler_LinearClamp, uvA.xy - outlineColor3Offset, arrayIndex).r;
            underlaySD = SAMPLE_TEXTURE2D_ARRAY(sdf, sampler_LinearClamp, uvA.xy - underlayColorOffset, arrayIndex).r;
            
            normal = getSurfaceNormal(sdf, texelSize, uvA, arrayIndex, SDR, isFront, innerBevel, bevelAmount, bevelWidth, bevelRoundness, bevelClamp);
        }
    }
}

//sample SDF 5 times: face, outline1, outline2, outline3, underlay
void Sample5Texture2DArrayUNLIT_float(
    float4 uvAandB,
    float4 atlasIndexScaleIsSdf16IsBitmap,
    float2 underlayColorOffset,
    float2 outlineColor1Offset,
    float2 outlineColor2Offset,
    float2 outlineColor3Offset,
    out bool isBitmap,
    out float4 bitmapColor,
    out float2 texelSize,
	out float SDR,
    out float4 SD, //x: face, y: outline1, z: outline2, w: outline3
    out float underlaySD,
	out float2 uvA,
    out float2 uvB,
    out float scale)
{
    uint width, height, elements, numberOfLevels;
    uvA = uvAandB.xy;
    uvB = uvAandB.zw;
    float arrayIndex = atlasIndexScaleIsSdf16IsBitmap.x;
    scale = atlasIndexScaleIsSdf16IsBitmap.y;
    SDR = atlasIndexScaleIsSdf16IsBitmap.z;
    bool isSdf16 = atlasIndexScaleIsSdf16IsBitmap.w > 0.5;
    isBitmap = atlasIndexScaleIsSdf16IsBitmap.w < -0.5;
    SD = 0;
    if (isBitmap)
    {
        _tmdBitmap.GetDimensions(0, width, height, elements, numberOfLevels); // Get dimensions of mip level 0
        texelSize = 1.0f / float2(width, height);
        
        UnityTexture2DArray bitmap = UnityBuildTexture2DArrayStruct(_tmdBitmap);
        bitmapColor = SAMPLE_TEXTURE2D_ARRAY(bitmap, sampler_LinearClamp, uvA.xy, arrayIndex);
        underlaySD = 0;
        return;
    }
    else
    {
        bitmapColor = float4(0, 0, 0, 0);
        if (isSdf16)
        {
            _tmdSdf16.GetDimensions(0, width, height, elements, numberOfLevels); // Get dimensions of mip level 0
            texelSize = 1.0f / float2(width, height);
            float offSetScale = SDR * texelSize.x;
            
            outlineColor1Offset *= offSetScale;
            outlineColor2Offset *= offSetScale;
            outlineColor3Offset *= offSetScale;
            underlayColorOffset *= offSetScale;
            
            UnityTexture2DArray sdf = UnityBuildTexture2DArrayStruct(_tmdSdf16);
            SD.x = SAMPLE_TEXTURE2D_ARRAY(sdf, sampler_LinearClamp, uvA.xy, arrayIndex).r;
            SD.y = SAMPLE_TEXTURE2D_ARRAY(sdf, sampler_LinearClamp, uvA.xy - outlineColor1Offset, arrayIndex).r;
            SD.z = SAMPLE_TEXTURE2D_ARRAY(sdf, sampler_LinearClamp, uvA.xy - outlineColor2Offset, arrayIndex).r;
            SD.w = SAMPLE_TEXTURE2D_ARRAY(sdf, sampler_LinearClamp, uvA.xy - outlineColor3Offset, arrayIndex).r;
            underlaySD = SAMPLE_TEXTURE2D_ARRAY(sdf, sampler_LinearClamp, uvA.xy - underlayColorOffset, arrayIndex).r;
        }
        else
        {
            _tmdSdf8.GetDimensions(0, width, height, elements, numberOfLevels); // Get dimensions of mip level 0
            texelSize = 1.0f / float2(width, height);
            float offSetScale = SDR * texelSize.x;
            
            outlineColor1Offset *= offSetScale;
            outlineColor2Offset *= offSetScale;
            outlineColor3Offset *= offSetScale;
            underlayColorOffset *= offSetScale;
            
            UnityTexture2DArray sdf = UnityBuildTexture2DArrayStruct(_tmdSdf8);
            SD.x = SAMPLE_TEXTURE2D_ARRAY(sdf, sampler_LinearClamp, uvA.xy, arrayIndex).r;
            SD.y = SAMPLE_TEXTURE2D_ARRAY(sdf, sampler_LinearClamp, uvA.xy - outlineColor1Offset, arrayIndex).r;
            SD.z = SAMPLE_TEXTURE2D_ARRAY(sdf, sampler_LinearClamp, uvA.xy - outlineColor2Offset, arrayIndex).r;
            SD.w = SAMPLE_TEXTURE2D_ARRAY(sdf, sampler_LinearClamp, uvA.xy - outlineColor3Offset, arrayIndex).r;
            underlaySD = SAMPLE_TEXTURE2D_ARRAY(sdf, sampler_LinearClamp, uvA.xy - underlayColorOffset, arrayIndex).r;
        }
    }
}

//sample SDF 3+4 times: face, outline1, underlay, and 4x for light normal
void Sample3Texture2DArrayLIT_float(
    float4 uvAandB,
    float4 atlasIndexScaleIsSdf16IsBitmap,
    bool isFront,
    bool innerBevel,
    float bevelAmount,
    float bevelWidth,
    float bevelRoundness,
    float bevelClamp,
    float2 underlayColorOffset,
    float2 outlineColor1Offset,
    out bool isBitmap,
    out float4 bitmapColor,
    out float2 texelSize,
	out float SDR,
    out float2 SD, //x: face, y: outline1
    out float underlaySD,
    out float3 normal,
	out float2 uvA,
    out float2 uvB,
    out float scale)
{
    uint width, height, elements, numberOfLevels;
    uvA = uvAandB.xy;
    uvB = uvAandB.zw;
    float arrayIndex = atlasIndexScaleIsSdf16IsBitmap.x;
    scale = atlasIndexScaleIsSdf16IsBitmap.y;
    SDR = atlasIndexScaleIsSdf16IsBitmap.z;
    bool isSdf16 = atlasIndexScaleIsSdf16IsBitmap.w > 0.5;
    isBitmap = atlasIndexScaleIsSdf16IsBitmap.w < -0.5;
    SD = 0;
    if (isBitmap)
    {
        _tmdBitmap.GetDimensions(0, width, height, elements, numberOfLevels); // Get dimensions of mip level 0
        texelSize = 1.0f / float2(width, height);
        
        UnityTexture2DArray bitmap = UnityBuildTexture2DArrayStruct(_tmdBitmap);
        bitmapColor = SAMPLE_TEXTURE2D_ARRAY(bitmap, sampler_LinearClamp, uvA.xy, arrayIndex);
        normal = float3(0, 0, -1);
        underlaySD = 0;
        return;
    }
    else
    {
        bitmapColor = float4(0, 0, 0, 0);
        if (isSdf16)
        {
            _tmdSdf16.GetDimensions(0, width, height, elements, numberOfLevels); // Get dimensions of mip level 0
            texelSize = 1.0f / float2(width, height);
            float offSetScale = SDR * texelSize.x;
            
            outlineColor1Offset *= offSetScale;
            underlayColorOffset *= offSetScale;
            
            UnityTexture2DArray sdf = UnityBuildTexture2DArrayStruct(_tmdSdf16);
            SD.x = SAMPLE_TEXTURE2D_ARRAY(sdf, sampler_LinearClamp, uvA.xy, arrayIndex).r;
            SD.y = SAMPLE_TEXTURE2D_ARRAY(sdf, sampler_LinearClamp, uvA.xy - outlineColor1Offset, arrayIndex).r;
            underlaySD = SAMPLE_TEXTURE2D_ARRAY(sdf, sampler_LinearClamp, uvA.xy - underlayColorOffset, arrayIndex).r;
            
            normal = getSurfaceNormal(sdf, texelSize, uvA, arrayIndex, SDR, isFront, innerBevel, bevelAmount, bevelWidth, bevelRoundness, bevelClamp);
        }
        else
        {
            _tmdSdf8.GetDimensions(0, width, height, elements, numberOfLevels); // Get dimensions of mip level 0
            texelSize = 1.0f / float2(width, height);
            float offSetScale = SDR * texelSize.x;
            
            outlineColor1Offset *= offSetScale;
            underlayColorOffset *= offSetScale;
            
            UnityTexture2DArray sdf = UnityBuildTexture2DArrayStruct(_tmdSdf8);
            SD.x = SAMPLE_TEXTURE2D_ARRAY(sdf, sampler_LinearClamp, uvA.xy, arrayIndex).r;
            SD.y = SAMPLE_TEXTURE2D_ARRAY(sdf, sampler_LinearClamp, uvA.xy - outlineColor1Offset, arrayIndex).r;
            underlaySD = SAMPLE_TEXTURE2D_ARRAY(sdf, sampler_LinearClamp, uvA.xy - underlayColorOffset, arrayIndex).r;
            
            normal = getSurfaceNormal(sdf, texelSize, uvA, arrayIndex, SDR, isFront, innerBevel, bevelAmount, bevelWidth, bevelRoundness, bevelClamp);
        }
    }
}
//sample SDF 3 times: face, outline1, underlay
void Sample3Texture2DArrayUNLIT_float(
    float4 uvAandB,
    float4 atlasIndexScaleIsSdf16IsBitmap,
    float2 underlayColorOffset,
    float2 outlineColor1Offset,
    out bool isBitmap,
    out float4 bitmapColor,
    out float2 texelSize,
	out float SDR,
    out float2 SD, //x: face, y: outline1
    out float underlaySD,
	out float2 uvA,
    out float2 uvB,
    out float scale)
{
    uint width, height, elements, numberOfLevels;
    uvA = uvAandB.xy;
    uvB = uvAandB.zw;
    float arrayIndex = atlasIndexScaleIsSdf16IsBitmap.x;
    scale = atlasIndexScaleIsSdf16IsBitmap.y;
    SDR = atlasIndexScaleIsSdf16IsBitmap.z;
    bool isSdf16 = atlasIndexScaleIsSdf16IsBitmap.w > 0.5;
    isBitmap = atlasIndexScaleIsSdf16IsBitmap.w < -0.5;
    SD = 0;
    if (isBitmap)
    {
        _tmdBitmap.GetDimensions(0, width, height, elements, numberOfLevels); // Get dimensions of mip level 0
        texelSize = 1.0f / float2(width, height);
        
        UnityTexture2DArray bitmap = UnityBuildTexture2DArrayStruct(_tmdBitmap);
        bitmapColor = SAMPLE_TEXTURE2D_ARRAY(bitmap, sampler_LinearClamp, uvA.xy, arrayIndex);
        underlaySD = 0;
        return;
    }
    else
    {
        bitmapColor = float4(0, 0, 0, 0);
        if (isSdf16)
        {
            _tmdSdf16.GetDimensions(0, width, height, elements, numberOfLevels); // Get dimensions of mip level 0
            texelSize = 1.0f / float2(width, height);
            float offSetScale = SDR * texelSize.x;
            
            outlineColor1Offset *= offSetScale;
            underlayColorOffset *= offSetScale;
            
            UnityTexture2DArray sdf = UnityBuildTexture2DArrayStruct(_tmdSdf16);
            SD.x = SAMPLE_TEXTURE2D_ARRAY(sdf, sampler_LinearClamp, uvA.xy, arrayIndex).r;
            SD.y = SAMPLE_TEXTURE2D_ARRAY(sdf, sampler_LinearClamp, uvA.xy - outlineColor1Offset, arrayIndex).r;
            underlaySD = SAMPLE_TEXTURE2D_ARRAY(sdf, sampler_LinearClamp, uvA.xy - underlayColorOffset, arrayIndex).r;
        }
        else
        {
            _tmdSdf8.GetDimensions(0, width, height, elements, numberOfLevels); // Get dimensions of mip level 0
            texelSize = 1.0f / float2(width, height);
            float offSetScale = SDR * texelSize.x;
            
            outlineColor1Offset *= offSetScale;
            underlayColorOffset *= offSetScale;
            
            UnityTexture2DArray sdf = UnityBuildTexture2DArrayStruct(_tmdSdf8);
            SD.x = SAMPLE_TEXTURE2D_ARRAY(sdf, sampler_LinearClamp, uvA.xy, arrayIndex).r;
            SD.y = SAMPLE_TEXTURE2D_ARRAY(sdf, sampler_LinearClamp, uvA.xy - outlineColor1Offset, arrayIndex).r;
            underlaySD = SAMPLE_TEXTURE2D_ARRAY(sdf, sampler_LinearClamp, uvA.xy - underlayColorOffset, arrayIndex).r;
        }
    }
}
//sample SDF 1+4 times: face, and 4x for light normal
void Sample1Texture2DArrayLIT_float(
    float4 uvAandB,
    float4 atlasIndexScaleIsSdf16IsBitmap,
    bool isFront,
    bool innerBevel,
    float bevelAmount,
    float bevelWidth,
    float bevelRoundness,
    float bevelClamp,
    out bool isBitmap,
    out float4 bitmapColor,
    out float2 texelSize,
	out float SDR,
    out float SD, //x: face
    out float3 normal,
	out float2 uvA,
    out float2 uvB,
    out float scale)
{
    uint width, height, elements, numberOfLevels;
    uvA = uvAandB.xy;
    uvB = uvAandB.zw;
    float arrayIndex = atlasIndexScaleIsSdf16IsBitmap.x;
    scale = atlasIndexScaleIsSdf16IsBitmap.y;
    SDR = atlasIndexScaleIsSdf16IsBitmap.z;
    bool isSdf16 = atlasIndexScaleIsSdf16IsBitmap.w > 0.5;
    isBitmap = atlasIndexScaleIsSdf16IsBitmap.w < -0.5;
    SD = 0;
    if (isBitmap)
    {
        _tmdBitmap.GetDimensions(0, width, height, elements, numberOfLevels); // Get dimensions of mip level 0
        texelSize = 1.0f / float2(width, height);
        
        UnityTexture2DArray bitmap = UnityBuildTexture2DArrayStruct(_tmdBitmap);
        bitmapColor = SAMPLE_TEXTURE2D_ARRAY(bitmap, sampler_LinearClamp, uvA.xy, arrayIndex);
        normal = float3(0, 0, -1);
        return;
    }
    else
    {
        bitmapColor = float4(0, 0, 0, 0);
        if (isSdf16)
        {
            _tmdSdf16.GetDimensions(0, width, height, elements, numberOfLevels); // Get dimensions of mip level 0
            texelSize = 1.0f / float2(width, height);
            
            UnityTexture2DArray sdf = UnityBuildTexture2DArrayStruct(_tmdSdf16);
            SD.x = SAMPLE_TEXTURE2D_ARRAY(sdf, sampler_LinearClamp, uvA.xy, arrayIndex).r;
            
            normal = getSurfaceNormal(sdf, texelSize, uvA, arrayIndex, SDR, isFront, innerBevel, bevelAmount, bevelWidth, bevelRoundness, bevelClamp);
        }
        else
        {
            _tmdSdf8.GetDimensions(0, width, height, elements, numberOfLevels); // Get dimensions of mip level 0
            texelSize = 1.0f / float2(width, height);
            
            UnityTexture2DArray sdf = UnityBuildTexture2DArrayStruct(_tmdSdf8);
            SD.x = SAMPLE_TEXTURE2D_ARRAY(sdf, sampler_LinearClamp, uvA.xy, arrayIndex).r;
            
            normal = getSurfaceNormal(sdf, texelSize, uvA, arrayIndex, SDR, isFront, innerBevel, bevelAmount, bevelWidth, bevelRoundness, bevelClamp);
        }
    }
}

//sample SDF 1 time: face
void Sample1Texture2DArrayUNLIT_float(
    float4 uvAandB,
    float4 atlasIndexScaleIsSdf16IsBitmap,
    out bool isBitmap,
    out float4 bitmapColor,
    out float2 texelSize,
	out float SDR,
    out float SD, //x: face
	out float2 uvA,
    out float2 uvB,
    out float scale)
{
    uint width, height, elements, numberOfLevels;
    uvA = uvAandB.xy;
    uvB = uvAandB.zw;
    float arrayIndex = atlasIndexScaleIsSdf16IsBitmap.x;
    scale = atlasIndexScaleIsSdf16IsBitmap.y;
    SDR = atlasIndexScaleIsSdf16IsBitmap.z;
    bool isSdf16 = atlasIndexScaleIsSdf16IsBitmap.w > 0.5;
    isBitmap = atlasIndexScaleIsSdf16IsBitmap.w < -0.5;
    SD = 0;
    if (isBitmap)
    {
        _tmdBitmap.GetDimensions(0, width, height, elements, numberOfLevels); // Get dimensions of mip level 0
        texelSize = 1.0f / float2(width, height);
        
        UnityTexture2DArray bitmap = UnityBuildTexture2DArrayStruct(_tmdBitmap);
        bitmapColor = SAMPLE_TEXTURE2D_ARRAY(bitmap, sampler_LinearClamp, uvA.xy, arrayIndex);
        return;
    }
    else
    {
        bitmapColor = float4(0, 0, 0, 0);
        if (isSdf16)
        {
            _tmdSdf16.GetDimensions(0, width, height, elements, numberOfLevels); // Get dimensions of mip level 0
            texelSize = 1.0f / float2(width, height);
            
            UnityTexture2DArray sdf = UnityBuildTexture2DArrayStruct(_tmdSdf16);
            SD.x = SAMPLE_TEXTURE2D_ARRAY(sdf, sampler_LinearClamp, uvA.xy, arrayIndex).r;
        }
        else
        {
            _tmdSdf8.GetDimensions(0, width, height, elements, numberOfLevels); // Get dimensions of mip level 0
            texelSize = 1.0f / float2(width, height);
            
            UnityTexture2DArray sdf = UnityBuildTexture2DArrayStruct(_tmdSdf8);
            SD.x = SAMPLE_TEXTURE2D_ARRAY(sdf, sampler_LinearClamp, uvA.xy, arrayIndex).r;
        }
    }
}

void SampleFaceTexture_float(
    float4 vertexColor,
    float2 uvB,
    UnityTexture2D faceTexture,
    float2 faceUVSpeed,
    float2 faceTiling,
    float2 faceOffset,
    out float4 colorOUT)
{
    float2 uvBOUT = scaleOffsetAnimateUV(uvB, faceTiling, faceOffset, faceUVSpeed, _Time.y);
    float4 textureColor = SAMPLE_TEXTURE2D(faceTexture, faceTexture.samplerstate, uvBOUT); //sampler_LinearClamp sampler_LinearRepeat
    colorOUT = vertexColor * textureColor;
}

void GetFontWeight_float(float dilationIN, float scale, float weightNormal, float weightBold, out float dilationOUT)
{
    dilationOUT = getFontWeightedDilation(dilationIN, scale, weightNormal, weightBold);
}

void GetFontWeight2_float(float2 dilationIN, float scale, float weightNormal, float weightBold, out float2 dilationOUT)
{
    dilationOUT = getFontWeightedDilation2(dilationIN, scale, weightNormal, weightBold);
}

void GetFontWeight4_float(float4 dilationIN, float scale, float weightNormal, float weightBold, out float4 dilationOUT)
{
    dilationOUT = getFontWeightedDilation4(dilationIN, scale, weightNormal, weightBold);
}

void ScreenSpaceRatio_float(float2 texelSize, float SDR, float2 uvA, out float SSR)
{	
    SSR = getScreenSpaceRatio(texelSize, SDR, uvA);
}

// SSR : Screen Space Ratio
// SDD  : Signed Distance (encoded : Distance / SDR + .5)
// SDR : 2x SPREAD (see last parameter in SDFGenerateSubDivisionLineEdges)
// dilation: dilate / contract the shape, normalized [-1,+1]
void ComputeSDF_float(float SSR, float SDR, float SDF, float dilation, float softness, out float outAlpha)
{
    outAlpha = getSdfPixelCoverage(SSR, SDR, SDF, dilation, softness);
}

void ComputeSDF2_float(float SSR, float SDR, float2 SDF, float2 dilation, float2 softness, out float2 outAlpha)
{
    outAlpha = getSdfPixelCoverage2(SSR, SDR, SDF, dilation, softness);
}

void ComputeSDF4_float(float SSR, float SDR, float4 SDF, float4 dilation, float4 softness, out float4 outAlpha)
{
    outAlpha = getSdfPixelCoverage4(SSR, SDR, SDF, dilation, softness);
}

// Face only
void Layer1_float(float alpha, float4 color0, out float4 outColor)
{
    outColor = mergeFaceColorAndAlpha(alpha, color0);
}
// Face + 1 Outline
void Layer2_float(float2 alpha, float4 color0, float4 color1, out float4 outColor)
{
    outColor = mergeFaceAndOutlineColorAndAlpha(alpha, color0, color1);
}
// Face + 3 Outline
void Layer4_float(float4 alpha, float4 color0, float4 color1, float4 color2, float4 color3, out float4 outColor)
{
    outColor = mergeFaceAndOutlineColorsAndAlpha(alpha, color0, color1, color2, color3);
}
void Blend_float(float4 overlying, float4 underlying, out float4 colorOUT)
{
    colorOUT = mergeOverlayAndUnderlay(overlying, underlying);
}
void ApplyVertexAlpha_float(
    float4 vertexColor,
    float4 colorIN,
    out float4 colorOUT)
{
    colorOUT = colorIN * vertexColor.w;
}

void EvaluateLight_float(
    float3 normal, 
    float4 faceColor,     
    float4 lightColor,
    float lightAngle,
    float specularPower,
    float reflectivityPower,     
    float diffuseShadow, 
    float ambientShadow,
    out float4 color)
{
    color = evaluateFakeLight(normal, faceColor, lightColor, lightAngle, specularPower, reflectivityPower, diffuseShadow, ambientShadow);
}
#endif