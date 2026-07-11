#ifndef TAOTIE_POSTERIZE_INCLUDED
#define TAOTIE_POSTERIZE_INCLUDED

#include "ShaderLibrary/Common.hlsl"

TEXTURE2D(_PosterizeSource);

float _PosterizeLevels;

struct PosterizeVaryings
{
    float4 positionCS : SV_POSITION;
    float2 screenUV : TEXCOORD0;
};

PosterizeVaryings PosterizePassVertex(float3 positionOS : POSITION, float2 uv : TEXCOORD0)
{
    PosterizeVaryings output;
    output.positionCS = float4(positionOS.xy, 0.0, 1.0);
    output.screenUV = uv;
    if (_ProjectionParams.x < 0.0)
        output.screenUV.y = 1.0 - output.screenUV.y;
    return output;
}

float4 PosterizeFragment(PosterizeVaryings input) : SV_Target
{
    float2 uv = input.screenUV;
    float4 source = SAMPLE_TEXTURE2D(_PosterizeSource, sampler_linear_clamp, uv);

    float levels = max(_PosterizeLevels, 2.0);
    float3 result = floor(source.rgb * levels) / (levels - 1.0);

    return float4(result, source.a);
}

#endif
