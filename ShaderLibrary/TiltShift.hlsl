#ifndef TAOTIE_TILT_SHIFT_INCLUDED
#define TAOTIE_TILT_SHIFT_INCLUDED

#include "Common.hlsl"

TEXTURE2D(_TiltShiftSource);

float4 _TiltShiftTexelSize;
float _TiltShiftFocusOffset;
float _TiltShiftFocusRange;
float _TiltShiftSmoothness;
float _TiltShiftBlurStrength;
float _TiltShiftBlurDirection; // 0=horizontal, 1=vertical, 2=radial

struct TiltShiftVaryings
{
    float4 positionCS : SV_POSITION;
    float2 screenUV : TEXCOORD0;
};

TiltShiftVaryings TiltShiftPassVertex(float3 positionOS : POSITION, float2 uv : TEXCOORD0)
{
    TiltShiftVaryings output;
    output.positionCS = float4(positionOS.xy, 0.0, 1.0);
    output.screenUV = uv;
    if (_ProjectionParams.x < 0.0)
        output.screenUV.y = 1.0 - output.screenUV.y;
    return output;
}

// Gaussian weights for 8-tap blur
static const float gaussianWeights[4] = { 0.4026, 0.2442, 0.0545, 0.0044 };

float4 TiltShiftFragment(TiltShiftVaryings input) : SV_Target
{
    float2 uv = input.screenUV;
    float4 source = SAMPLE_TEXTURE2D(_TiltShiftSource, sampler_linear_clamp, uv);

    // Compute focus weight based on direction
    float focusCenter;
    float distFromCenter;

    int dir = (int)_TiltShiftBlurDirection;

    if (dir == 2) // radial
    {
        focusCenter = 0.5 + _TiltShiftFocusOffset * 0.5;
        float2 centered = uv - float2(focusCenter, 0.5 + _TiltShiftFocusOffset * 0.5);
        distFromCenter = length(centered);
    }
    else if (dir == 0) // horizontal (blur along X, focus band along Y)
    {
        focusCenter = 0.5 + _TiltShiftFocusOffset * 0.5;
        distFromCenter = abs(uv.x - focusCenter);
    }
    else // vertical (blur along Y, focus band along X)
    {
        focusCenter = 0.5 + _TiltShiftFocusOffset * 0.5;
        distFromCenter = abs(uv.y - focusCenter);
    }

    // Focus weight: 1 inside focus range, 0 outside with smooth transition
    float focusStart = _TiltShiftFocusRange;
    float focusEnd = _TiltShiftFocusRange + _TiltShiftSmoothness;
    float focusWeight = 1.0 - smoothstep(focusStart, focusEnd, distFromCenter);

    // Early out: if fully in focus, return source
    if (focusWeight >= 0.999)
        return source;

    // Multi-tap Gaussian blur
    float4 blurred = source * gaussianWeights[0];

    float2 texel = _TiltShiftTexelSize.xy * _TiltShiftBlurStrength;

    if (dir == 2) // radial: blur in both directions
    {
        for (int i = 1; i < 4; i++)
        {
            float offset = (float)i * texel.x;
            blurred += SAMPLE_TEXTURE2D(_TiltShiftSource, sampler_linear_clamp, uv + float2(offset, 0)) * gaussianWeights[i];
            blurred += SAMPLE_TEXTURE2D(_TiltShiftSource, sampler_linear_clamp, uv - float2(offset, 0)) * gaussianWeights[i];
            float offsetY = (float)i * texel.y;
            blurred += SAMPLE_TEXTURE2D(_TiltShiftSource, sampler_linear_clamp, uv + float2(0, offsetY)) * gaussianWeights[i];
            blurred += SAMPLE_TEXTURE2D(_TiltShiftSource, sampler_linear_clamp, uv - float2(0, offsetY)) * gaussianWeights[i];
        }
        // Normalize: 1 (center) + 2*3 (both sides, X+Y) * weights
        float totalWeight = gaussianWeights[0];
        for (int j = 1; j < 4; j++)
            totalWeight += gaussianWeights[j] * 4.0; // 4 taps per weight (left, right, up, down)
        blurred /= totalWeight;
    }
    else
    {
        float2 blurDir = (dir == 0) ? float2(texel.x, 0) : float2(0, texel.y);
        float totalWeight = gaussianWeights[0];
        for (int i = 1; i < 4; i++)
        {
            float offset = (float)i;
            blurred += SAMPLE_TEXTURE2D(_TiltShiftSource, sampler_linear_clamp, uv + blurDir * offset) * gaussianWeights[i];
            blurred += SAMPLE_TEXTURE2D(_TiltShiftSource, sampler_linear_clamp, uv - blurDir * offset) * gaussianWeights[i];
            totalWeight += gaussianWeights[i] * 2.0;
        }
        blurred /= totalWeight;
    }

    // Blend between blurred and sharp based on focus weight
    float3 result = lerp(blurred.rgb, source.rgb, focusWeight);
    return float4(result, source.a);
}

#endif
