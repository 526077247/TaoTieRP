#ifndef TAOTIE_CHROMATIC_ABERRATION_INCLUDED
#define TAOTIE_CHROMATIC_ABERRATION_INCLUDED

#include "ShaderLibrary/Common.hlsl"

TEXTURE2D(_CASource);

float _CAIntensity;
float2 _CACenter;

struct CAVaryings
{
    float4 positionCS : SV_POSITION;
    float2 screenUV : TEXCOORD0;
};

CAVaryings CAPassVertex(float3 positionOS : POSITION, float2 uv : TEXCOORD0)
{
    CAVaryings output;
    output.positionCS = float4(positionOS.xy, 0.0, 1.0);
    output.screenUV = uv;
    if (_ProjectionParams.x < 0.0)
        output.screenUV.y = 1.0 - output.screenUV.y;
    return output;
}

float4 CAFragment(CAVaryings input) : SV_Target
{
    float2 uv = input.screenUV;

    float2 dir = uv - _CACenter;
    float dist = length(dir);
    float2 offset = normalize(dir) * dist * _CAIntensity;

    float r = _CASource.Sample(sampler_linear_clamp, uv + offset).r;
    float g = _CASource.Sample(sampler_linear_clamp, uv).g;
    float b = _CASource.Sample(sampler_linear_clamp, uv - offset).b;
    float a = _CASource.Sample(sampler_linear_clamp, uv).a;

    return float4(r, g, b, a);
}

#endif
