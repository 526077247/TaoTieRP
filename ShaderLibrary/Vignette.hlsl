#ifndef TAOTIE_VIGNETTE_INCLUDED
#define TAOTIE_VIGNETTE_INCLUDED

#include "Common.hlsl"

TEXTURE2D(_VignetteSource);

float _VignetteIntensity;
float _VignetteSmoothness;
float2 _VignetteCenter;
float _VignetteRoundness;
float4 _VignetteColor;

struct VignetteVaryings
{
    float4 positionCS : SV_POSITION;
    float2 screenUV : TEXCOORD0;
};

VignetteVaryings VignettePassVertex(float3 positionOS : POSITION, float2 uv : TEXCOORD0)
{
    VignetteVaryings output;
    output.positionCS = float4(positionOS.xy, 0.0, 1.0);
    output.screenUV = uv;
    if (_ProjectionParams.x < 0.0)
        output.screenUV.y = 1.0 - output.screenUV.y;
    return output;
}

float4 VignetteFragment(VignetteVaryings input) : SV_Target
{
    float2 uv = input.screenUV;
    float4 source = SAMPLE_TEXTURE2D(_VignetteSource, sampler_linear_clamp, uv);

    float2 dist = uv - _VignetteCenter;
    dist.x *= _VignetteRoundness;

    float d = length(dist);

    float factor = saturate(1.0 - d * _VignetteIntensity);
    factor = smoothstep(0.0, 1.0 - _VignetteSmoothness, factor);

    float3 result = lerp(_VignetteColor.rgb, source.rgb, factor);
    float alpha = lerp(_VignetteColor.a, 1.0, factor);

    return float4(result * alpha + source.rgb * (1.0 - alpha), source.a);
}

#endif
