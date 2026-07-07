#ifndef TAOTIE_PIXELATE_INCLUDED
#define TAOTIE_PIXELATE_INCLUDED

#include "ShaderLibrary/Common.hlsl"

TEXTURE2D(_PixelateSource);

float _PixelateCellSize;
float4 _PixelateTexelSize;

struct PixelateVaryings
{
    float4 positionCS : SV_POSITION;
    float2 screenUV : TEXCOORD0;
};

PixelateVaryings PixelatePassVertex(float3 positionOS : POSITION, float2 uv : TEXCOORD0)
{
    PixelateVaryings output;
    output.positionCS = float4(positionOS.xy, 0.0, 1.0);
    output.screenUV = uv;
    if (_ProjectionParams.x < 0.0)
        output.screenUV.y = 1.0 - output.screenUV.y;
    return output;
}

float4 PixelateFragment(PixelateVaryings input) : SV_Target
{
    float2 uv = input.screenUV;

    float cellSize = max(_PixelateCellSize, 1.0);
    float2 pixels = _PixelateTexelSize.zw;
    float2 grid = pixels / cellSize;

    float2 cell = floor(uv * grid) / grid;
    float2 centerUV = (cell + 0.5 / grid);

    float4 source = _PixelateSource.Sample(sampler_point_clamp, centerUV);

    return source;
}

#endif
