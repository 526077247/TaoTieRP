#ifndef TAOTIE_SHADOWS_INCLUDED
#define TAOTIE_SHADOWS_INCLUDED
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Shadow/ShadowSamplingTent.hlsl"

#if defined(_SHADOW_FILTER_HIGH)
    #define DIRECTIONAL_FILTER_SAMPLES 16
    #define DIRECTIONAL_FILTER_SETUP SampleShadow_ComputeSamples_Tent_7x7
    #define OTHER_FILTER_SAMPLES 16
    #define OTHER_FILTER_SETUP SampleShadow_ComputeSamples_Tent_7x7
#elif defined(_SHADOW_FILTER_MEDIUM)
    #define DIRECTIONAL_FILTER_SAMPLES 9
    #define DIRECTIONAL_FILTER_SETUP SampleShadow_ComputeSamples_Tent_5x5
    #define OTHER_FILTER_SAMPLES 9
    #define OTHER_FILTER_SETUP SampleShadow_ComputeSamples_Tent_5x5
#else
    #define DIRECTIONAL_FILTER_SAMPLES 4
    #define DIRECTIONAL_FILTER_SETUP SampleShadow_ComputeSamples_Tent_3x3
    #define OTHER_FILTER_SAMPLES 4
    #define OTHER_FILTER_SETUP SampleShadow_ComputeSamples_Tent_3x3
#endif

TEXTURE2D_SHADOW(_DirectionalShadowAtlas);
TEXTURE2D_SHADOW(_OtherShadowAtlas);
#define SHADOW_SAMPLER sampler_linear_clamp_compare
SAMPLER_CMP(SHADOW_SAMPLER);

CBUFFER_START(_CustomShadows)
    int _CascadeCount;
    float4 _ShadowAtlasSize;
    float4 _ShadowDistanceFade;
CBUFFER_END

struct DirectionalShadowCascade
{
    float4 cullingSphere, data;
};

StructuredBuffer<DirectionalShadowCascade> _DirectionalShadowCascades;

StructuredBuffer<float4x4> _DirectionalShadowMatrices;

struct OtherShadowBufferData
{
    float4 tileData;
    float4x4 shadowMatrix;
};

StructuredBuffer<OtherShadowBufferData> _OtherShadowData;

struct DirectionalShadowData {
    float strength;
    int tileIndex;
    float normalBias;
    int shadowMaskChannel;
};

struct ShadowMask {
    bool always;
    bool distance;
    float4 shadows;
};

struct ShadowData {
    int cascadeIndex;
    float cascadeBlend;
    float strength;
    ShadowMask shadowMask;
};


float FadedShadowStrength (float distance, float scale, float fade) {
    return saturate((1.0 - distance * scale) * fade);
}
ShadowData GetShadowData (Surface surfaceWS) {
    ShadowData data;
    data.shadowMask.always = false;
    data.shadowMask.distance = false;
    data.shadowMask.shadows = 1.0;
    data.cascadeBlend = 1.0;
    data.strength = FadedShadowStrength(
        surfaceWS.depth, _ShadowDistanceFade.x, _ShadowDistanceFade.y
    );
    int i;
    for (i = 0; i < _CascadeCount; i++) {
        DirectionalShadowCascade cascade = _DirectionalShadowCascades[i];
        float distanceSqr = DistanceSquared(
            surfaceWS.position, cascade.cullingSphere.xyz);
        if (distanceSqr < cascade.cullingSphere.w)
        {
            float fade = FadedShadowStrength(
                distanceSqr, cascade.data.x, _ShadowDistanceFade.z);
            if (i == _CascadeCount - 1) {
                data.strength *= fade;
            }
            else {
                data.cascadeBlend = fade;
            }
            break;
        }
    }
    if (i == _CascadeCount && _CascadeCount > 0) {
        data.strength = 0.0;
    }
#if !defined(_SOFT_CASCADE_BLEND)
    else if (data.cascadeBlend < surfaceWS.dither) {
    	i += 1;
    }
    data.cascadeBlend = 1.0;
#endif
    data.cascadeIndex = i;
    return data;
}

float SampleDirectionalShadowAtlas (float3 positionSTS) {
    return SAMPLE_TEXTURE2D_SHADOW(
        _DirectionalShadowAtlas, SHADOW_SAMPLER, positionSTS
    );
}

float FilterDirectionalShadow (float3 positionSTS) {
    #if defined(DIRECTIONAL_FILTER_SETUP)
    float weights[DIRECTIONAL_FILTER_SAMPLES];
    float2 positions[DIRECTIONAL_FILTER_SAMPLES];
    float4 size = _ShadowAtlasSize.yyxx;
    DIRECTIONAL_FILTER_SETUP(size, positionSTS.xy, weights, positions);
    float shadow = 0;
    for (int i = 0; i < DIRECTIONAL_FILTER_SAMPLES; i++) {
        shadow += weights[i] * SampleDirectionalShadowAtlas(
            float3(positions[i].xy, positionSTS.z)
        );
    }
    return shadow;
    #else
    return SampleDirectionalShadowAtlas(positionSTS);
    #endif
}

float SampleOtherShadowAtlas (float3 positionSTS, float3 bounds) {
    positionSTS.xy = clamp(positionSTS.xy, bounds.xy, bounds.xy + bounds.z);
    return SAMPLE_TEXTURE2D_SHADOW(
        _OtherShadowAtlas, SHADOW_SAMPLER, positionSTS
    );
}

float FilterOtherShadow (float3 positionSTS, float3 bounds) {
    #if defined(OTHER_FILTER_SETUP)
    real weights[OTHER_FILTER_SAMPLES];
    real2 positions[OTHER_FILTER_SAMPLES];
    float4 size = _ShadowAtlasSize.wwzz;
    OTHER_FILTER_SETUP(size, positionSTS.xy, weights, positions);
    float shadow = 0;
    for (int i = 0; i < OTHER_FILTER_SAMPLES; i++) {
        shadow += weights[i] * SampleOtherShadowAtlas(
            float3(positions[i].xy, positionSTS.z), bounds
        );
    }
    return shadow;
    #else
    return SampleOtherShadowAtlas(positionSTS, bounds);
    #endif
}

float GetCascadedShadow (
    DirectionalShadowData directional, ShadowData global, Surface surfaceWS
) {
    float3 normalBias = surfaceWS.interpolatedNormal * (
         directional.normalBias *
         _DirectionalShadowCascades[global.cascadeIndex].data.y);
    float3 positionSTS = mul(
        _DirectionalShadowMatrices[directional.tileIndex],
        float4(surfaceWS.position + normalBias, 1.0)
    ).xyz;
    float shadow = FilterDirectionalShadow(positionSTS);
    if (global.cascadeBlend < 1.0) {
        normalBias = surfaceWS.interpolatedNormal *
            (directional.normalBias * _DirectionalShadowCascades[global.cascadeIndex + 1].data.y);
        positionSTS = mul(
            _DirectionalShadowMatrices[directional.tileIndex + 1],
            float4(surfaceWS.position + normalBias, 1.0)
        ).xyz;
        shadow = lerp(
            FilterDirectionalShadow(positionSTS), shadow, global.cascadeBlend
        );
    }
    return shadow;
}

float GetBakedShadow (ShadowMask mask, int channel) {
    float shadow = 1.0;
    if (mask.always || mask.distance) {
        if (channel >= 0) {
            shadow = mask.shadows[channel];
        }
    }
    return shadow;
}

float GetBakedShadow (ShadowMask mask, int channel, float strength) {
    if (mask.always || mask.distance) {
        return lerp(1.0, GetBakedShadow(mask, channel), strength);
    }
    return 1.0;
}

float MixBakedAndRealtimeShadows (
    ShadowData global, float shadow, int shadowMaskChannel, float strength
) {
    float baked = GetBakedShadow(global.shadowMask, shadowMaskChannel);
    if (global.shadowMask.always) {
        shadow = lerp(1.0, shadow, global.strength);
        shadow = min(baked, shadow);
        return lerp(1.0, shadow, strength);
    }
    if (global.shadowMask.distance) {
        shadow = lerp(baked, shadow, global.strength);
        return lerp(1.0, shadow, strength);
    }
    return lerp(1.0, shadow, strength * global.strength);
}

float GetDirectionalShadowAttenuation (DirectionalShadowData directional, ShadowData global, Surface surfaceWS) {
    #if !defined(_RECEIVE_SHADOWS)
    return 1.0;
    #endif
    float shadow;
    if (directional.strength * global.strength <= 0.0) {
        shadow = GetBakedShadow(
            global.shadowMask, directional.shadowMaskChannel,
            abs(directional.strength)
        );
    }
    else {
        shadow = GetCascadedShadow(directional, global, surfaceWS);
        shadow = MixBakedAndRealtimeShadows(
            global, shadow, directional.shadowMaskChannel, directional.strength
        );
    }
    return shadow;
}

struct OtherShadowData {
    float strength;
    int tileIndex;
    bool isPoint;
    int shadowMaskChannel;
    float3 lightPositionWS;
    float3 lightDirectionWS;
    float3 spotDirectionWS;
};

static const float3 pointShadowPlanes[6] = {
    float3(-1.0, 0.0, 0.0),
    float3(1.0, 0.0, 0.0),
    float3(0.0, -1.0, 0.0),
    float3(0.0, 1.0, 0.0),
    float3(0.0, 0.0, -1.0),
    float3(0.0, 0.0, 1.0)
};

float GetOtherShadow (
    OtherShadowData other, ShadowData global, Surface surfaceWS
) {
    float tileIndex = other.tileIndex;
    float3 lightPlane = other.spotDirectionWS;
    if (other.isPoint) {
        float faceOffset = CubeMapFaceID(-other.lightDirectionWS);
        tileIndex += faceOffset;
        lightPlane = pointShadowPlanes[faceOffset];
    }
    OtherShadowBufferData data = _OtherShadowData[tileIndex];
    float3 surfaceToLight = other.lightPositionWS - surfaceWS.position;
    float distanceToLightPlane = dot(surfaceToLight, lightPlane);
    float3 normalBias =
        surfaceWS.interpolatedNormal * (distanceToLightPlane * data.tileData.w);
    float4 positionSTS = mul(
        data.shadowMatrix,
        float4(surfaceWS.position + normalBias, 1.0));
    return FilterOtherShadow(
        positionSTS.xyz / positionSTS.w, data.tileData.xyz);
}

float GetOtherShadowAttenuation (
    OtherShadowData other, ShadowData global, Surface surfaceWS
) {
    #if !defined(_RECEIVE_SHADOWS)
    return 1.0;
    #endif
	
    float shadow;
    if (other.strength * global.strength <= 0.0) {
        shadow = GetBakedShadow(
            global.shadowMask, other.shadowMaskChannel, abs(other.strength)
        );
    }
    else {
        shadow = GetOtherShadow(other, global, surfaceWS);
        shadow = MixBakedAndRealtimeShadows(
            global, shadow, other.shadowMaskChannel, other.strength
        );
    }
    return shadow;
}
#endif