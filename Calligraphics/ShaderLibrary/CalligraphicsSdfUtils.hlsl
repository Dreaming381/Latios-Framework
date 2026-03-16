#ifndef CALLIGRAPHICS_SDF_UTILS
#define CALLIGRAPHICS_SDF_UTILS

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"

/// Averages the reference dilation with the shader-emulated font weight dilation
/// Dilation is a value between -1 and 1 which repesent where the edge should be relative to the SDF sampled as a
/// signed normalized value [-1, 1].
/// Weight is a Unity BSed value (as far as I can tell) which is 8 times the dilation that should be applied
/// A negative value shrinks the contour relative to the original glyph, while a positive value increases it.
/// dilation: A dilation value to be averaged with the font weight dilation
/// scale: The scale value from the glyph, used to select bold weight if negative
/// weightNormal: The base weight of non-bold text
/// weightBold: The base weight of bold text when using shader-based boldness emulation
float getFontWeightedDilation(float dilation, float scale, float weightNormal, float weightBold)
{
    float bold = step(scale, 0); //float bold = scale < 0.0;
    float weight = lerp(weightNormal, weightBold, bold) / 4.0;
    return (weight + dilation) * 0.5;
}
/// Averages the reference dilations with the shader-emulated font weight dilation
/// Dilation is a value between -1 and 1 which repesent where the edge should be relative to the SDF sampled as a
/// signed normalized value (-1, 1).
/// Weight is a Unity BSed value (as far as I can tell) which is 8 times the dilation that should be applied
/// A negative value shrinks the contour relative to the original glyph, while a positive value increases it.
/// dilation: Dilation values to be averaged with the font weight dilation
/// scale: The scale value from the glyph, used to select bold weight if negative
/// weightNormal: The base weight of non-bold text
/// weightBold: The base weight of bold text when using shader-based boldness emulation
float2 getFontWeightedDilation2(float2 dilation, float scale, float weightNormal, float weightBold)
{
    float bold = step(scale, 0); //float bold = scale < 0.0;
    float weight = lerp(weightNormal, weightBold, bold) / 4.0;
    return (weight + dilation) * 0.5;
}
/// Averages the reference dilations with the shader-emulated font weight dilation
/// Dilation is a value between -1 and 1 which repesent where the edge should be relative to the SDF sampled as a
/// signed normalized value (-1, 1).
/// Weight is a Unity BSed value (as far as I can tell) which is 8 times the dilation that should be applied
/// A negative value shrinks the contour relative to the original glyph, while a positive value increases it.
/// dilation: Dilation values to be averaged with the font weight dilation
/// scale: The scale value from the glyph, used to select bold weight if negative
/// weightNormal: The base weight of non-bold text
/// weightBold: The base weight of bold text when using shader-based boldness emulation
float4 getFontWeightedDilation4(float4 dilation, float scale, float weightNormal, float weightBold)
{
    float bold = step(scale, 0); //float bold = scale < 0.0;
    float weight = lerp(weightNormal, weightBold, bold) / 4.0;
    return (weight + dilation) * 0.5;
}

/// Computes the ratio of screen pixels per SDF texel
/// texelSize: The width and height of the SDF texture
/// texelsInDilationDomain: The number of texels used to fill the dilation domain of [-1, 1]. This is double the
/// the number of texels away from from an edge before the SDF values saturate (SPREAD).
/// uvA: The SDF atlas UV coordinates of the glyph, whose pixel quad derivatives are used to estimate texel density
float getScreenSpaceRatio(float2 texelSize, float texelsInDilationDomain, float2 uvA)
{	
	float2x2 pixelFootprint = float2x2(ddx(uvA), ddy(uvA));
	float pixelFootprintDiameterSqr = abs(determinant(pixelFootprint)); // Calculate the area under the pixel =  "Jacobian determinant" 
	float screenSpaceRatio = rsqrt(pixelFootprintDiameterSqr) * texelSize.x;				//pixels per texel
	
	// Prevent collapse at extreme minification
	float minScreenSpaceRatio = 1.0 / texelsInDilationDomain; // one SDF range per pixel
	screenSpaceRatio = max(screenSpaceRatio, minScreenSpaceRatio);
    return screenSpaceRatio;
}

// SSR : Screen Space Ratio
// SDD  : Signed Distance (encoded : Distance / SDR + .5)
// SDR : 2x SPREAD (see last parameter in SDFGenerateSubDivisionLineEdges)
// dilation: dilate / contract the shape, normalized [-1,+1]
/// Computes the fraction of the pixel covered by the glyph (alpha)
/// screenSpaceRatio: The ratio of screen pixels per SDF texel
/// texelsInDilationDomain: The number of texels used to fill the dilation domain of [-1, 1]. This is double the
/// the number of texels away from from an edge before the SDF values saturate (SPREAD).
/// sampledSignedDistance: The value sampled from the SDF texture [0, 1]
/// dilation: The font-weighted dilation [-1,+1]
/// softness: ?
float getSdfPixelCoverage(float screenSpaceRatio, float texelsInDilationDomain, float sampledSignedDistance, float dilation, float softness)
{
	softness *= screenSpaceRatio * texelsInDilationDomain;
	float d = (sampledSignedDistance - 0.5) * texelsInDilationDomain; // Signed distance to edge in texels
	return saturate((d * 2.0 * screenSpaceRatio + 0.5 + dilation * sampledSignedDistance * screenSpaceRatio + softness * 0.5) / (1.0 + softness)); // Screen pixel coverage (alpha)
}

/// Computes the fractions of the pixel covered by the glyph (alpha) for multiple glyph regions
/// screenSpaceRatio: The ratio of screen pixels per SDF texel
/// texelsInDilationDomain: The number of texels used to fill the dilation domain of [-1, 1]. This is double the
/// the number of texels away from from an edge before the SDF values saturate (SPREAD).
/// sampledSignedDistance: The values sampled from the SDF texture [0, 1]
/// dilation: The font-weighted dilation [-1,+1]
/// softness: ?
float2 getSdfPixelCoverage2(float screenSpaceRatio, float texelsInDilationDomain, float2 sampledSignedDistance, float2 dilation, float2 softness)
{
    softness *= screenSpaceRatio * texelsInDilationDomain;
    float2 d = (sampledSignedDistance - 0.5) * texelsInDilationDomain; // Signed distance to edge, in Texture space
    return saturate((d * 2.0 * screenSpaceRatio + 0.5 + dilation * texelsInDilationDomain * screenSpaceRatio + softness * 0.5) / (1.0 + softness)); // Screen pixel coverage (alpha)
}

/// Computes the fractions of the pixel covered by the glyph (alpha) for multiple glyph regions
/// screenSpaceRatio: The ratio of screen pixels per SDF texel
/// texelsInDilationDomain: The number of texels used to fill the dilation domain of [-1, 1]. This is double the
/// the number of texels away from from an edge before the SDF values saturate (SPREAD).
/// sampledSignedDistance: The values sampled from the SDF texture [0, 1]
/// dilation: The font-weighted dilation [-1,+1]
/// softness: ?
float4 getSdfPixelCoverage4(float screenSpaceRatio, float texelsInDilationDomain, float4 sampledSignedDistance, float4 dilation, float4 softness)
{
    softness *= screenSpaceRatio * texelsInDilationDomain;
    float4 d = (sampledSignedDistance - 0.5) * texelsInDilationDomain; // Signed distance to edge, in Texture space
    return saturate((d * 2.0 * screenSpaceRatio + 0.5 + dilation * texelsInDilationDomain * screenSpaceRatio + softness * 0.5) / (1.0 + softness)); // Screen pixel coverage (alpha)
}

// Face only
/// Computes the final color given a sampled/computed face color and the pixel coverage
/// alpha: The fraction of the pixel covered by the glyph
/// faceColor: The color sampled/evaluated for the face
float4 mergeFaceColorAndAlpha(float alpha, float4 faceColor)
{
    faceColor.a *= alpha;
    return faceColor;
}
// Face + 1 Outline
/// Computes the final color given sampled/computed face and outline colors and the pixel coverages
/// alpha: The fractions of the pixel covered by glyph's face (x component) and outline (y component)
/// faceColor: The color sampled/evaluated for the face
/// outlineColor: The color sampled/evaluated for the outline
float4 mergeFaceAndOutlineColorAndAlpha(float2 alpha, float4 faceColor, float4 outlineColor)
{
    outlineColor.a *= alpha.y;
    faceColor.rgb *= faceColor.a;
    outlineColor.rgb *= outlineColor.a;
    float4 outColor = lerp(outlineColor, faceColor, alpha.x);
    outColor.rgb /= outColor.a;
    return outColor;
}
// Face + 3 Outline
/// Computes the final color given sampled/computed face and outline colors and the pixel coverages
/// alpha: The fractions of the pixel covered by glyph's face (x component) and outlines (yzw components)
/// faceColor: The color sampled/evaluated for the face
/// outline1Color: The color sampled/evaluated for the first outline
/// outline2Color: The color sampled/evaluated for the second outline
/// outline3Color: The color sampled/evaluated for the third outline
float4 mergeFaceAndOutlineColorsAndAlpha(float4 alpha, float4 faceColor, float4 outline1Color, float4 outline2Color, float4 outline3Color)
{
    outline3Color.a *= alpha.w;
    faceColor.rgb *= faceColor.a;
    outline1Color.rgb *= outline1Color.a;
    outline2Color.rgb *= outline2Color.a;
    outline3Color.rgb *= outline3Color.a;
    float4 outColor = lerp(lerp(lerp(outline3Color, outline2Color, alpha.z), outline1Color, alpha.y), faceColor, alpha.x);
    outColor.rgb /= outColor.a;
    return outColor;
}
/// Computes the final color given the evaluated primary face + outlines color and the underlay color
/// overlayColor: The final color of the face and potentially multiple outlines
/// underlayColor: The final color of the underlay, such as a drop-shadow effect
float4 mergeOverlayAndUnderlay(float4 overlayColor, float4 underlayColor)
{
    overlayColor.rgb *= overlayColor.a;
    underlayColor.rgb *= underlayColor.a;
    float3 blended = overlayColor.rgb + ((1 - overlayColor.a) * underlayColor.rgb);
    float alpha = underlayColor.a + (1 - underlayColor.a) * overlayColor.a;
    return float4(blended / alpha, alpha);
}

float3 getSurfaceNormal(
    UnityTexture2DArray sdf,
    float2 texelSize,
    float2 uvA,
	float arrayIndex,
    float SDR,
    bool isFront,
    bool innerBevel,
    float bevelAmount,
    float bevelWidth,
    float bevelRoundness,
    float bevelClamp)
{
    float3 delta = float3(texelSize, 0.0);

	// Read "height field"
    float4 h = float4(
		SAMPLE_TEXTURE2D_ARRAY(sdf, sampler_LinearClamp, uvA.xy - delta.xz, arrayIndex).r,
		SAMPLE_TEXTURE2D_ARRAY(sdf, sampler_LinearClamp, uvA.xy + delta.xz, arrayIndex).r,
		SAMPLE_TEXTURE2D_ARRAY(sdf, sampler_LinearClamp, uvA.xy - delta.zy, arrayIndex).r,
		SAMPLE_TEXTURE2D_ARRAY(sdf, sampler_LinearClamp, uvA.xy + delta.zy, arrayIndex).r);
    

    //h += _BevelOffset;
    bevelWidth = max(.01, bevelWidth);

	// Track outline
    h -= .5;
    h /= bevelWidth;
    h = saturate(h + .5);

    if (innerBevel)
        h = 1 - abs(h * 2.0 - 1.0);
    h = lerp(h, sin(h * 3.141592 / 2.0), float4(bevelRoundness, bevelRoundness, bevelRoundness, bevelRoundness));
    h = min(h, 1.0 - float4(bevelClamp, bevelClamp, bevelClamp, bevelClamp));
    h *= bevelAmount * bevelWidth * SDR * -2.0;

    float3 va = normalize(float3(-1.0, 0.0, h.y - h.x));
    float3 vb = normalize(float3(0.0, 1.0, h.w - h.z));

    float3 f = isFront ? float3(1, 1, -1) : float3(1, 1, 1);
    return cross(va, vb) * f;
}

float2 scaleOffsetAnimateUV(float2 inUV, float2 tiling, float2 offset, float2 animSpeed, float timeY)
{
    return inUV * tiling + offset + (animSpeed * timeY);
}

/// Computes a cheap specular lighting effect for a directional light
float3 getSpecular(float3 normal, float3 light, float4 lightColor, float reflectivityPower, float specularPower)
{
    float spec = pow(max(0.0, dot(normal, light)), reflectivityPower);
    return lightColor.rgb * spec * specularPower;
}

/// Evaluates a fake light for the text, in which the light source is specified by a single angle representing the direction
/// perpendicular to where the text is facing, and the light source is as far along that direction as it is in front of the text
/// pointing towards the center of the text.
float4 evaluateFakeLight(
    float3 normal, 
    float4 faceColor,     
    float4 lightColor,
    float lightAngle,
    float specularPower,
    float reflectivityPower,     
    float diffuseShadow, 
    float ambientShadow)
{
    normal.z = abs(normal.z);
    float sinAngle;
    float cosAngle;
    sincos(lightAngle, sinAngle, cosAngle);
    float3 light = normalize(float3(sinAngle, cosAngle, 1.0));

    float3 col = max(faceColor.rgb, 0) + getSpecular(normal, light, lightColor, reflectivityPower, specularPower) * faceColor.a;

    col *= 1 - (dot(normal, light) * diffuseShadow);
    col *= lerp(ambientShadow, 1, normal.z * normal.z);
    
    return float4(col, faceColor.a);
}
#endif