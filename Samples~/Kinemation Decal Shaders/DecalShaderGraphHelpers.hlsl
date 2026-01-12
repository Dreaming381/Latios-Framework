#ifndef INCLUDE_DECAL_SHADER_GRAPH_HELPERS
#define INCLUDE_DECAL_SHADER_GRAPH_HELPERS

// This file contains shader graph utilities to transform a mesh decal into a projected decal
// for both URP and HDRP for DOTS shaders.
//
// There are two features that are only supported with projected decals and not mesh decals.
// The first is DBuffer layers. This is stupid. There's absolutely no reason this can't work.
// In URP, this can be trivially fixed in a mesh decal shader with the following line:
// #define _DecalLayerMaskFromDecal asuint(unity_RenderingLayer.x)
// In HDRP, there is no good workaround, as it is tied to the pass. But it is easy to reimplement.
//
// The second feature is angle fade. This one is slightly less stupid, because it requires
// fade limits be passed in, and those limits require C# precomputation. However, Unity
// could still make a separate MonoBehaviour for it, as we do (and then bake).
// However, both URP and HDRP couple this to their projector clipping logic, which is incompatible
// with Kinemation's projector meshes. Therefore, this feature does not work at this time.

#ifdef UNIVERSAL_SHADERPASS_INCLUDED
#define IS_UNIVERSAL_RENDER_PIPELINE
#else
#ifdef SHADERPASS_CS_HLSL // Todo: Someone could have copied this from HDRP
#define IS_HD_RENDER_PIPELINE
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Decal/DecalPrepassBuffer.hlsl"
#endif
#endif

// Begin shared helper functions
void SetupProjectedDecalVertex_float(in float3 positionObjectSpace, in float3 normalObjectSpace, in float nearClipPlane, out float3 outPositionObjectSpace, out float3 outNormalObjectSpace, out float3 outTangentObjectSpace)
{
    float3 posVS = mul(UNITY_MATRIX_V, mul(UNITY_MATRIX_M, float4(positionObjectSpace, 1.0))).xyz;
    float3 normalVS = mul(UNITY_MATRIX_V, mul(UNITY_MATRIX_M, float4(normalObjectSpace, 0.0))).xyz;
    float distanceBehindPlane = -posVS.z - nearClipPlane;
    if (distanceBehindPlane < 0.00001 && normalVS.z > 0.00001)
    {
        distanceBehindPlane -= 0.00001;
        float distanceAgainstNormal = abs(distanceBehindPlane / normalVS.z);
        posVS -= distanceAgainstNormal * normalVS;
        outPositionObjectSpace = mul(UNITY_MATRIX_I_M, mul(UNITY_MATRIX_I_V, float4(posVS, 1.0))).xyz;
    }
    else
    {
        outPositionObjectSpace = positionObjectSpace;
    }
    
    outNormalObjectSpace = float3(0.0, 0.0, -1.0);
    outTangentObjectSpace = float3(1.0, 0.0, 0.0);
}

void ProjectDecalOrthographic_float(in float2 screenUV, in float sceneDepthRaw, out float2 projectionUVs, out float distanceFactor)
{
    float3 positionWS = ComputeWorldSpacePosition(screenUV, sceneDepthRaw, UNITY_MATRIX_I_VP);
    float3 positionLS = mul(UNITY_MATRIX_I_M, float4(positionWS, 1.0)).xyz;
    projectionUVs = positionLS.xy + 0.5;
    distanceFactor = positionLS.z;
    
    // In HDRP, there's a note that clipping on Metal platforms causes weird behaviors with
    // derivative functions. There's not really a good way to deal with this from within this
    // file though.
    float clipValue = 1.0;
    positionLS.z -= 0.5;
    float3 absolutes = abs(positionLS);
    float biggest = max(absolutes.x, max(absolutes.y, absolutes.z));
    if (biggest < 0.0 || biggest > 0.5)
        clipValue = -1.0;
    
#if defined(IS_HD_RENDER_PIPELINE) && !defined(HDRP_DECAL_EXPERIMENTAL)
    float depth = sceneDepthRaw;
#if (SHADERPASS == SHADERPASS_FORWARD_EMISSIVE_MESH) && UNITY_REVERSED_Z
    // For the sky adjust the depth so that the following LOD calculation (GetSurfaceData() in DecalData.hlsl) of adjacent
    // non-sky pixels using depth derivatives results in LOD0 sampling
    depth = IsSky(depth) ? UNITY_NEAR_CLIP_VALUE : depth;
#endif
    PositionInputs posInput = GetPositionInput(screenUV * _ScreenSize.xy, _ScreenSize.zw, depth, UNITY_MATRIX_I_VP, UNITY_MATRIX_V);

    // Decal layer mask accepted by the receiving material
    DecalPrepassData material;
    ZERO_INITIALIZE(DecalPrepassData, material);
    if (_EnableDecalLayers)
    {
        DecodeFromDecalPrepass(posInput.positionSS, material);
        
        // Clip the decal if it does not pass the decal layer mask of the receiving material.
        // Decal layer of the decal
        uint decalLayerMask = uint(UNITY_ACCESS_INSTANCED_PROP(Decal, _DecalLayerMaskFromDecal).x);

        if ((decalLayerMask & material.renderingLayerMask) == 0)
        {
            clipValue = -1.0;
        }
    }
#endif
    
    clip(clipValue);
}

void ProjectDecalPerspective_float(in float2 screenUV, in float sceneDepthRaw, out float2 projectionUVs, out float distanceFactor)
{
    float3 positionWS = ComputeWorldSpacePosition(screenUV, sceneDepthRaw, UNITY_MATRIX_I_VP);
    float3 positionLS = mul(UNITY_MATRIX_I_M, float4(positionWS, 1.0)).xyz;
    distanceFactor = positionLS.z;
    
    // In HDRP, there's a note that clipping on Metal platforms causes weird behaviors with
    // derivative functions. There's not really a good way to deal with this from within this
    // file though.
    float clipValue = 1.0;
    positionLS.z -= 0.5;
    positionLS.xy /= max(distanceFactor, 0.00001);
    projectionUVs = positionLS.xy + 0.5;
    float3 absolutes = abs(positionLS);
    float biggest = max(absolutes.x, max(absolutes.y, absolutes.z));
    if (biggest < 0.0 || biggest > 0.5)
        clipValue = -1.0;
    
#if defined(IS_HD_RENDER_PIPELINE)
    float depth = sceneDepthRaw;
#if (SHADERPASS == SHADERPASS_FORWARD_EMISSIVE_MESH) && UNITY_REVERSED_Z
    // For the sky adjust the depth so that the following LOD calculation (GetSurfaceData() in DecalData.hlsl) of adjacent
    // non-sky pixels using depth derivatives results in LOD0 sampling
    depth = IsSky(depth) ? UNITY_NEAR_CLIP_VALUE : depth;
#endif
    PositionInputs posInput = GetPositionInput(screenUV * _ScreenSize.xy, _ScreenSize.zw, depth, UNITY_MATRIX_I_VP, UNITY_MATRIX_V);

    // Decal layer mask accepted by the receiving material
    DecalPrepassData material;
    ZERO_INITIALIZE(DecalPrepassData, material);
    if (_EnableDecalLayers)
    {
        DecodeFromDecalPrepass(posInput.positionSS, material);
        
        // Clip the decal if it does not pass the decal layer mask of the receiving material.
        // Decal layer of the decal
        uint decalLayerMask = asuint(unity_RenderingLayer.x);

        if ((decalLayerMask & material.renderingLayerMask) == 0)
        {
            clipValue = -1.0;
        }
    }
#endif
    
    clip(clipValue);
}
// End shared helper functions

// Begin URP
// URP is capable of using decal layer masks for mesh decals, but doesn't know how
// to read the mask data. We teach it here.
#ifdef IS_UNIVERSAL_RENDER_PIPELINE
#if (SHADERPASS == SHADERPASS_DBUFFER_MESH) || (SHADERPASS == SHADERPASS_FORWARD_EMISSIVE_MESH) || (SHADERPASS == SHADERPASS_DECAL_SCREEN_SPACE_MESH) || (SHADERPASS == SHADERPASS_DECAL_GBUFFER_MESH)
#ifndef FEATURES_GRAPH_VERTEX
#define FEATURES_GRAPH_VERTEX
#define FEATURES_GRAPH_VERTEX_NORMAL_OUTPUT
#define FEATURES_GRAPH_VERTEX_TANGENT_OUTPUT
#endif
#define _DecalLayerMaskFromDecal asuint(unity_RenderingLayer.x)
#endif
#endif
// End URP

// Begin HDRP
// HDRP logic is embedded in custom functions.
// End HDRP

#endif
