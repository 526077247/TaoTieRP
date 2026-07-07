#ifndef TAOTIE_VOLUMETRIC_FOG_INCLUDED
#define TAOTIE_VOLUMETRIC_FOG_INCLUDED

// Adapted from https://github.com/sinnwrig/URP-Fog-Volumes (MIT)
// Simplified for TaoTie RP: no volume intersection, no per-light shadowing,
// full-screen raymarch with exponential extinction, Mie scattering, fog color.

#include "ShaderLibrary/Common.hlsl"

TEXTURE2D(_VFogSource);

int _VFogSampleCount;
float4 _VFogStepParams;   // x: min step, y: max step, z: step increment factor, w: max ray length
float _VFogJitter;
float _VFogScattering;
float _VFogExtinction;
float _VFogMieG;
float _VFogDensity;
float4 _VFogColor;         // rgb = fog color, a = density multiplier
float _VFogMaxDistance;

struct VFogVaryings
{
    float4 positionCS : SV_POSITION;
    float2 screenUV : TEXCOORD0;
};

VFogVaryings VFogPassVertex(float3 positionOS : POSITION, float2 uv : TEXCOORD0)
{
    VFogVaryings output;
    output.positionCS = float4(positionOS.xy, 0.0, 1.0);
    output.screenUV = uv;
    if (_ProjectionParams.x < 0.0)
        output.screenUV.y = 1.0 - output.screenUV.y;
    return output;
}

float sqr(float v) { return v * v; }
float sqrlen(float3 v) { return dot(v, v); }

// Mie phase function — from https://github.com/SlightlyMad/VolumetricLights (BSD)
float MiePhase(float cosAngle, float mieG)
{
    float gSqr = sqr(mieG);
    return 0.07957747154 * ((1.0 - gSqr) / pow(abs((1.0 + gSqr) - (2.0 * mieG) * cosAngle), 1.5));
}

// Reconstruct world-space view direction from screen UV
float3 GetViewDir(float2 uv)
{
    float3 viewVector = mul(unity_CameraInvProjection, float4(uv * 2.0 - 1.0, 0.0, -1.0)).xyz;
    viewVector = mul(unity_MatrixInvV, float4(viewVector, 0.0)).xyz;
    return normalize(viewVector);
}

// Hash for jitter
float MathRand(float2 uv)
{
    return frac(sin(dot(uv, float2(12.9898, 78.233))) * 43758.5453);
}

half4 RayMarch(float3 rayStart, float3 rayDir, float rayLength, float2 uv)
{
    float stepSize = _VFogStepParams.x;
    float extinction = 0.0;
    half3 vlight = 0.0;
    float distance = 0.0;

    // Jitter the start position to reduce banding
    float jitter = MathRand(uv) * _VFogJitter;
    distance = jitter;

    half3 fogColor = _VFogColor.rgb;
    float density = _VFogDensity * _VFogColor.a;

    [loop]
    for (int i = 0; i < _VFogSampleCount; ++i)
    {
        if (distance >= rayLength)
            break;

        float3 currentPosition = rayStart + rayDir * distance;

        // Simple density falloff — constant with distance-based fade
        float distFade = saturate(distance / _VFogMaxDistance);
        float localDensity = density * (1.0 - distFade * 0.3);

        float scattering = _VFogScattering * stepSize * localDensity;
        extinction += _VFogExtinction * stepSize * localDensity;

        float influence = scattering * exp(-extinction);

        // Apply fog color with Mie scattering (use view direction as light direction approximation)
        float cosAngle = dot(rayDir, -rayDir);
        half3 lightColor = fogColor * MiePhase(cosAngle, _VFogMieG);

        vlight += lightColor * influence;

        distance += stepSize;
        stepSize = min(_VFogStepParams.y, stepSize * _VFogStepParams.z);
    }

    float opacity = 1.0 - exp(-extinction);
    return half4(vlight, opacity);
}

half4 CalculateVolumetricFog(float3 cameraPos, float3 viewDir, float linearDepth, float2 uv)
{
    float rayLength = min(linearDepth, _VFogMaxDistance);

    if (rayLength <= 0.0)
        return 0.0;

    float3 rayStart = cameraPos;

    return RayMarch(rayStart, viewDir, rayLength, uv);
}

half4 VolumetricFogFragment(VFogVaryings input) : SV_Target
{
    float2 uv = input.screenUV;

    float4 source = _VFogSource.Sample(sampler_linear_clamp, uv);

    float rawDepth = SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, sampler_point_clamp, uv, 0);
    #if UNITY_REVERSED_Z
        if (rawDepth >= 1.0) return source;
    #else
        if (rawDepth <= 0.0) return source;
    #endif

    float3 viewDir = GetViewDir(uv);
    float linearDepth = LinearEyeDepth(rawDepth, _ZBufferParams);

    half4 fog = CalculateVolumetricFog(_WorldSpaceCameraPos, viewDir, linearDepth, uv);

    // Blend source with fog: fog.rgb is accumulated light, fog.a is opacity
    float3 result = source.rgb * (1.0 - fog.a) + fog.rgb;
    return float4(result, source.a);
}

#endif // TAOTIE_VOLUMETRIC_FOG_INCLUDED
