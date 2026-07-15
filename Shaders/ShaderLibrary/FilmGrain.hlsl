#ifndef TAOTIE_FILM_GRAIN_INCLUDED
#define TAOTIE_FILM_GRAIN_INCLUDED

#include "Common.hlsl"

TEXTURE2D(_GrainSource);

float _GrainIntensity;
float _GrainLumaResponse;
float4 _GrainTexelSize; // xy = 1/width, 1/height

struct GrainVaryings
{
    float4 positionCS : SV_POSITION;
    float2 screenUV : TEXCOORD0;
};

GrainVaryings GrainPassVertex(float3 positionOS : POSITION, float2 uv : TEXCOORD0)
{
    GrainVaryings output;
    output.positionCS = float4(positionOS.xy, 0.0, 1.0);
    output.screenUV = uv;
    if (_ProjectionParams.x < 0.0)
        output.screenUV.y = 1.0 - output.screenUV.y;
    return output;
}

// Hash-based noise �?no texture lookup needed
float Hash(float2 p)
{
    float2 seed = p + float2(_Time.y * 0.5 + 0.1, _Time.y * 0.7 + 0.3);
    return frac(sin(dot(seed, float2(12.9898, 78.233))) * 43758.5453);
}

float4 GrainFragment(GrainVaryings input) : SV_Target
{
    float2 uv = input.screenUV;
    float4 source = SAMPLE_TEXTURE2D(_GrainSource, sampler_linear_clamp, uv);

    // Generate grain noise at pixel resolution
    float2 px = uv * _GrainTexelSize.zw;
    float noise = Hash(px);

    // Remap noise to [-1, 1] centered
    noise = noise * 2.0 - 1.0;

    // Luma-weighted response: less grain on bright areas
    float luma = dot(source.rgb, float3(0.2126, 0.7152, 0.0722));
    float response = saturate(1.0 - abs(luma - 0.5) * 2.0 * _GrainLumaResponse);
    response = lerp(1.0, response, _GrainLumaResponse);

    float3 result = source.rgb + noise * _GrainIntensity * response;

    return float4(result, source.a);
}

#endif
