#ifndef TAOTIE_DOF_INCLUDED
#define TAOTIE_DOF_INCLUDED

#include "ShaderLibrary/Common.hlsl"

TEXTURE2D(_DOFSource);
TEXTURE2D(_DOFCoCTexture);

float4 _DOFParams;     // x = focusDistance, y = focusRange, z = nearBlur, w = farBlur
float4 _DOFTexelSize;  // xy = 1/width, 1/height, z = width, w = height

struct DOFVaryings
{
    float4 positionCS : SV_POSITION;
    float2 screenUV : TEXCOORD0;
};

DOFVaryings DOFPassVertex(float3 positionOS : POSITION, float2 uv : TEXCOORD0)
{
    DOFVaryings output;
    output.positionCS = float4(positionOS.xy, 0.0, 1.0);
    output.screenUV = uv;
    if (_ProjectionParams.x < 0.0)
        output.screenUV.y = 1.0 - output.screenUV.y;
    return output;
}

// Circle of Confusion calculation
// Returns CoC in [-1, 1] range:
//   negative = near blur (foreground), positive = far blur (background)
//   0 = in focus
float GetCircleOfConfusion(float2 uv)
{
    float rawDepth = SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, sampler_point_clamp, uv, 0);
    #if UNITY_REVERSED_Z
        if (rawDepth >= 1.0) return 0.0;
    #else
        if (rawDepth <= 0.0) return 0.0;
    #endif

    float linearDepth = LinearEyeDepth(rawDepth, _ZBufferParams);

    float focusDistance = _DOFParams.x;
    float focusRange = max(_DOFParams.y, 0.0001f);

    // CoC based on distance from focus plane
    float coc = (linearDepth - focusDistance) / focusRange;

    // Clamp to [-1, 1]
    return clamp(coc, -1.0, 1.0);
}

// Pass 0: Calculate CoC into alpha channel
float4 CoCPassFragment(DOFVaryings input) : SV_Target
{
    float3 color = SAMPLE_TEXTURE2D(_DOFSource, sampler_linear_clamp, input.screenUV).rgb;
    float coc = GetCircleOfConfusion(input.screenUV);
    return float4(color, coc);
}

// Pass 1: Small Gaussian blur (prefilter for downsampled or full-res)
float4 DOFBlurPassFragment(DOFVaryings input) : SV_Target
{
    float2 uv = input.screenUV;
    float2 texel = _DOFTexelSize.xy;

    float4 center = SAMPLE_TEXTURE2D(_DOFSource, sampler_linear_clamp, uv);
    float coc = center.a;

    // Skip in-focus pixels
    if (abs(coc) < 0.01)
        return center;

    // 13-tap poisson disc
    float2 offsets[13] = {
        float2(0.0, 0.0),
        float2(0.0, 1.0), float2(0.0, -1.0),
        float2(1.0, 0.0), float2(-1.0, 0.0),
        float2(0.707, 0.707), float2(-0.707, 0.707),
        float2(0.707, -0.707), float2(-0.707, -0.707),
        float2(0.0, 2.0), float2(0.0, -2.0),
        float2(2.0, 0.0), float2(-2.0, 0.0)
    };
    float weights[13] = {
        0.25,
        0.0625, 0.0625, 0.0625, 0.0625,
        0.0625, 0.0625, 0.0625, 0.0625,
        0.03125, 0.03125, 0.03125, 0.03125
    };

    float3 color = 0;
    float weightSum = 0;

    [unroll]
    for (int i = 0; i < 13; i++)
    {
        float2 sampleUV = uv + offsets[i] * texel * abs(coc) * 2.0;
        float4 s = SAMPLE_TEXTURE2D(_DOFSource, sampler_linear_clamp, sampleUV);
        // Only accept samples whose CoC indicates background blur
        float sampleWeight = saturate(s.a) * weights[i];
        color += s.rgb * sampleWeight;
        weightSum += sampleWeight;
    }

    color = weightSum > 0.001 ? color / weightSum : center.rgb;
    return float4(color, coc);
}

// Pass 2: Composite blurred background with sharp foreground
float4 DOFCompositePassFragment(DOFVaryings input) : SV_Target
{
    float2 uv = input.screenUV;

    float4 source = SAMPLE_TEXTURE2D(_DOFSource, sampler_linear_clamp, uv);
    float4 blurred = SAMPLE_TEXTURE2D(_DOFCoCTexture, sampler_linear_clamp, uv);

    float coc = source.a;
    float blurStrength = saturate(abs(coc));

    // Far field: blend source with blurred
    float farBlur = saturate(coc) * blurStrength;

    // Near field: we don't have a separate near blur, so just use the blur result directly
    float nearBlur = saturate(-coc) * blurStrength;

    float3 result = lerp(source.rgb, blurred.rgb, max(farBlur, nearBlur * 0.5));

    return float4(result, 1.0);
}

#endif // TAOTIE_DOF_INCLUDED
