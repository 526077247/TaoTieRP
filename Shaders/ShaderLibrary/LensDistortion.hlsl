#ifndef TAOTIE_LENS_DISTORTION_INCLUDED
#define TAOTIE_LENS_DISTORTION_INCLUDED

// Adapted from URP Lens Distortion implementation
// Reference: https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@14.0/manual/Post-Processing-Lens-Distortion.html

#include "ShaderLibrary/Common.hlsl"

TEXTURE2D(_LDSource);

float _LDIntensity;    // [-1, 1]: positive = barrel, negative = pincushion
float2 _LDCenter;       // distortion center in [0, 1]
float _LDScale;         // zoom to hide screen borders [0, 1+]

struct LDVaryings
{
    float4 positionCS : SV_POSITION;
    float2 screenUV : TEXCOORD0;
};

LDVaryings LDPassVertex(float3 positionOS : POSITION, float2 uv : TEXCOORD0)
{
    LDVaryings output;
    output.positionCS = float4(positionOS.xy, 0.0, 1.0);
    output.screenUV = uv;
    if (_ProjectionParams.x < 0.0)
        output.screenUV.y = 1.0 - output.screenUV.y;
    return output;
}

float4 LDFragment(LDVaryings input) : SV_Target
{
    float2 uv = input.screenUV;

    // Convert to centered [-1, 1] space
    float2 centered = (uv - _LDCenter) * 2.0;

    // Barrel/pincushion distortion
    // r^2 = dot(uv, uv), then uv *= 1 + r^2 * intensity
    float r2 = dot(centered, centered);
    float distortion = 1.0 + r2 * _LDIntensity;

    // Scale to zoom (hide borders revealed by distortion)
    centered *= distortion / _LDScale;

    // Convert back to [0, 1] UV
    float2 distortedUV = centered * 0.5 + _LDCenter;

    // Sample with clamped UV to avoid wrapping
    return SAMPLE_TEXTURE2D(_LDSource, sampler_linear_clamp, distortedUV);
}

#endif
