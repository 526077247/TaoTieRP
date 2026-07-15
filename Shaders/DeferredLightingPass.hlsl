#ifndef TAOTIE_DEFERRED_LIGHTING_PASS_INCLUDED
#define TAOTIE_DEFERRED_LIGHTING_PASS_INCLUDED

#include "ShaderLibrary/Common.hlsl"
#include "ShaderLibrary/GBuffer.hlsl"
#include "ShaderLibrary/Surface.hlsl"
#include "ShaderLibrary/Shadows.hlsl"
#include "ShaderLibrary/Light.hlsl"
#include "ShaderLibrary/BRDF.hlsl"
#include "ShaderLibrary/GI.hlsl"
#include "ShaderLibrary/Lighting.hlsl"
#include "ShaderLibrary/Fragment.hlsl"

TEXTURE2D(_GBufferAlbedoAO);
TEXTURE2D(_GBufferNormalMS);
TEXTURE2D(_GBufferEmission);
// _CameraDepthTexture declared in Fragment.hlsl

struct DeferredVaryings {
    float4 positionCS : SV_POSITION;
    float2 texUV : VAR_SCREEN_UV;
};

DeferredVaryings DeferredLightingVertex (
    float3 positionOS : POSITION,
    float2 uv : TEXCOORD0
) {
    DeferredVaryings output;
    output.positionCS = float4(positionOS.xy, 0.0, 1.0);
    output.texUV = uv;
    // On D3D (_ProjectionParams.x < 0), flip Y to match texture convention.
    if (_ProjectionParams.x < 0.0) {
        output.texUV.y = 1.0 - output.texUV.y;
    }
    return output;
}

float4 DeferredLightingFragment (DeferredVaryings input) : SV_TARGET {
    float2 texUV = input.texUV;

    float rawDepth = SAMPLE_TEXTURE2D_LOD(
        _CameraDepthTexture, sampler_point_clamp, texUV, 0).r;

    #if UNITY_REVERSED_Z
        clip(rawDepth - 0.00001);
    #else
        clip(0.99999 - rawDepth);
    #endif

    float4 albedoAO = SAMPLE_TEXTURE2D_LOD(
        _GBufferAlbedoAO, sampler_point_clamp, texUV, 0);
    float4 normalMS = SAMPLE_TEXTURE2D_LOD(
        _GBufferNormalMS, sampler_point_clamp, texUV, 0);
    float4 emission = SAMPLE_TEXTURE2D_LOD(
        _GBufferEmission, sampler_point_clamp, texUV, 0);

    float3 worldPos = ReconstructWorldPos(texUV, rawDepth);

    Surface surface;
    surface.position = worldPos;
    surface.normal = DecodeGBufferNormal(normalMS.xy);
    surface.interpolatedNormal = surface.normal;
    surface.viewDirection = normalize(_WorldSpaceCameraPos - worldPos);
    surface.depth = -TransformWorldToView(worldPos).z;
    surface.color = albedoAO.rgb;
    surface.metallic = normalMS.z;
    surface.occlusion = albedoAO.a;
    surface.smoothness = normalMS.w;

    #if UNITY_COLORSPACE_GAMMA
        surface.color = SRGBToLinear(surface.color);
    #endif
    surface.alpha = 1.0;

    #if defined(_SSAO_ENABLED)
        surface.occlusion *= SAMPLE_TEXTURE2D(_ScreenSpaceOcclusionTexture,
            sampler_ScreenSpaceOcclusionTexture, texUV).r;
    #endif
    surface.fresnelStrength = 1.0;
    surface.dither = InterleavedGradientNoise(input.positionCS.xy, 0);
    #ifndef SHADER_API_GLES
    surface.renderingLayerMask = (uint)emission.a;
    #endif
    surface.receiveShadows = true;

    BRDF brdf = GetBRDF(surface);

    Fragment fragment;
    fragment.positionSS = input.positionCS.xy;
    fragment.screenUV = texUV;
    fragment.depth = surface.depth;
    fragment.bufferDepth = 0.0;

    // GI diffuse is already baked into the emission G-Buffer by DeferredGBufferPass.
    // Only reflection probe specular is computed here.
    GI gi;
    gi.diffuse = 0.0;
    gi.specular = SampleEnvironment(surface, brdf);
    gi.shadowMask.always = false;
    gi.shadowMask.distance = false;
    gi.shadowMask.shadows = 1.0;

    float3 color = GetLighting(fragment, surface, brdf, gi);
    #if UNITY_COLORSPACE_GAMMA
        color += SRGBToLinear(emission.rgb) * surface.occlusion;
        color = LinearToSRGB(color);
    #else
        color += emission.rgb * surface.occlusion;
    #endif

    // DEBUG: 0=off, 1=worldPos, 2=viewDir, 3=normal, 4=albedo, 5=GI diffuse, 6=direct light only
    #define DEBUG_STAGE 0
    #if DEBUG_STAGE == 1
        return float4(frac(surface.position * 0.5), 1.0);
    #elif DEBUG_STAGE == 2
        return float4(surface.viewDirection * 0.5 + 0.5, 1.0);
    #elif DEBUG_STAGE == 3
        return float4(surface.normal * 0.5 + 0.5, 1.0);
    #elif DEBUG_STAGE == 4
        return float4(surface.color, 1.0);
    #elif DEBUG_STAGE == 5
        return float4(gi.diffuse, 1.0);
    #elif DEBUG_STAGE == 6
        float3 indirect = IndirectBRDF(surface, brdf, gi.diffuse, gi.specular);
        return float4(color - indirect, 1.0);
    #endif

    return float4(color, 1.0);
}

#endif
