#ifndef TAOTIE_PANINI_PROJECTION_INCLUDED
#define TAOTIE_PANINI_PROJECTION_INCLUDED

// Adapted from URP Panini Projection
// Cylindrical projection that keeps vertical and radial lines straight.
// Reference: https://wiki.panotools.org/The_General_Panini_Projection

#include "ShaderLibrary/Common.hlsl"

TEXTURE2D(_PPSource);

float _PPDistance;    // [0, 1] distortion strength
float _PPCropToFit;   // [0, 1] crop to fit screen

struct PPVaryings
{
    float4 positionCS : SV_POSITION;
    float2 screenUV : TEXCOORD0;
};

PPVaryings PPPassVertex(float3 positionOS : POSITION, float2 uv : TEXCOORD0)
{
    PPVaryings output;
    output.positionCS = float4(positionOS.xy, 0.0, 1.0);
    output.screenUV = uv;
    if (_ProjectionParams.x < 0.0)
        output.screenUV.y = 1.0 - output.screenUV.y;
    return output;
}

// Panini projection (stereographic variant)
float2 PaniniProject(float2 viewPos, float distance)
{
    // For distance = 0, this should be identity
    // viewPos is in [-1, 1] centered space

    // Spherical projection with stereographic mapping
    float d = distance + 1.0;
    float r2 = dot(viewPos, viewPos);

    // Avoid singularity at r = 0
    float f = r2 * 0.5 + 0.5;

    // Stereographic projection
    float2 dir = viewPos;
    float denom = d + r2 * distance * 0.5;
    denom = max(denom, 0.0001);
    float2 projected = dir * d / denom;

    return projected;
}

float4 PPFragment(PPVaryings input) : SV_Target
{
    float2 uv = input.screenUV;

    // Convert to centered [-1, 1] space
    float2 viewPos = (uv - 0.5) * 2.0;

    // Apply Panini projection
    float2 projected = PaniniProject(viewPos, _PPDistance);

    // Crop to fit: scale back toward center to keep within screen bounds
    float scale = lerp(1.0, 1.0 / (1.0 + _PPDistance * 0.5), _PPCropToFit);
    projected *= scale;

    // Convert back to [0, 1] UV
    float2 distortedUV = projected * 0.5 + 0.5;

    return _PPSource.Sample(sampler_linear_clamp, distortedUV);
}

#endif
