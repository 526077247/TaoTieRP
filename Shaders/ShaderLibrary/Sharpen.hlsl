#ifndef TAOTIE_SHARPEN_INCLUDED
#define TAOTIE_SHARPEN_INCLUDED

#include "Common.hlsl"

TEXTURE2D(_SharpenSource);

float _SharpenIntensity;
float _SharpenRadius;
float4 _SharpenTexelSize;

struct SharpenVaryings
{
    float4 positionCS : SV_POSITION;
    float2 screenUV : TEXCOORD0;
};

SharpenVaryings SharpenPassVertex(float3 positionOS : POSITION, float2 uv : TEXCOORD0)
{
    SharpenVaryings output;
    output.positionCS = float4(positionOS.xy, 0.0, 1.0);
    output.screenUV = uv;
    if (_ProjectionParams.x < 0.0)
        output.screenUV.y = 1.0 - output.screenUV.y;
    return output;
}

float4 SharpenFragment(SharpenVaryings input) : SV_Target
{
    float2 uv = input.screenUV;

    float2 texel = _SharpenTexelSize.xy * _SharpenRadius;

    float4 center = SAMPLE_TEXTURE2D(_SharpenSource, sampler_linear_clamp, uv);
    float4 up = SAMPLE_TEXTURE2D(_SharpenSource, sampler_linear_clamp, uv + float2(0.0, texel.y));
    float4 down = SAMPLE_TEXTURE2D(_SharpenSource, sampler_linear_clamp, uv - float2(0.0, texel.y));
    float4 left = SAMPLE_TEXTURE2D(_SharpenSource, sampler_linear_clamp, uv - float2(texel.x, 0.0));
    float4 right = SAMPLE_TEXTURE2D(_SharpenSource, sampler_linear_clamp, uv + float2(texel.x, 0.0));

    float3 neighborAvg = (up.rgb + down.rgb + left.rgb + right.rgb) * 0.25;
    float3 edge = center.rgb - neighborAvg;
    float3 result = center.rgb + edge * _SharpenIntensity;

    return float4(result, center.a);
}

#endif
