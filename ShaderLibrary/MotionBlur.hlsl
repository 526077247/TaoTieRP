#ifndef TAOTIE_MOTION_BLUR_INCLUDED
#define TAOTIE_MOTION_BLUR_INCLUDED

// Camera-motion-based motion blur, adapted from URP approach:
// 1. Reconstruct world position from depth + current VP inverse
// 2. Reproject to previous frame UV using previous VP
// 3. Velocity = current UV - previous UV
// 4. Sample along velocity direction (multi-sample gather)

#include "Common.hlsl"

TEXTURE2D(_MBSource);

float _MBIntensity;        // blur strength [0, 1]
int _MBSampleCount;         // number of samples along motion vector
float4x4 _MBInverseVP;     // current frame inverse view-projection
float4x4 _MBPreviousVP;    // previous frame view-projection

struct MBVaryings
{
    float4 positionCS : SV_POSITION;
    float2 screenUV : TEXCOORD0;
};

MBVaryings MBPassVertex(float3 positionOS : POSITION, float2 uv : TEXCOORD0)
{
    MBVaryings output;
    output.positionCS = float4(positionOS.xy, 0.0, 1.0);
    output.screenUV = uv;
    if (_ProjectionParams.x < 0.0)
        output.screenUV.y = 1.0 - output.screenUV.y;
    return output;
}

// Reconstruct world position from depth + UV
float3 ReconstructWorldPos(float2 uv, float rawDepth)
{
    #if UNITY_REVERSED_Z
        float clipZ = rawDepth;
    #else
        float clipZ = rawDepth * 2.0 - 1.0;
    #endif
    float4 clipPos = float4(uv * 2.0 - 1.0, clipZ, 1.0);
    float4 worldPos = mul(_MBInverseVP, clipPos);
    return worldPos.xyz / worldPos.w;
}

// Reproject world position to previous frame screen UV
float2 ReprojectToPrevUV(float3 worldPos)
{
    float4 prevClip = mul(_MBPreviousVP, float4(worldPos, 1.0));
    float2 prevUV = prevClip.xy / prevClip.w;
    return prevUV * 0.5 + 0.5;
}

float4 MBFragment(MBVaryings input) : SV_Target
{
    float2 uv = input.screenUV;

    float rawDepth = SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, sampler_point_clamp, uv, 0);
    #if UNITY_REVERSED_Z
        if (rawDepth >= 1.0) return SAMPLE_TEXTURE2D(_MBSource, sampler_linear_clamp, uv);
    #else
        if (rawDepth <= 0.0) return SAMPLE_TEXTURE2D(_MBSource, sampler_linear_clamp, uv);
    #endif

    // Compute per-pixel velocity
    float3 worldPos = ReconstructWorldPos(uv, rawDepth);
    float2 prevUV = ReprojectToPrevUV(worldPos);
    float2 velocity = uv - prevUV;

    // Scale velocity by intensity
    float2 dir = velocity * _MBIntensity;

    // Multi-sample gather along motion direction
    float3 color = 0.0;
    float totalWeight = 0.0;

    [loop]
    for (int i = 0; i < _MBSampleCount; i++)
    {
        float t = (float)i / (float)_MBSampleCount;
        // Jitter sampling position between 0 and 1
        float2 sampleUV = uv - dir * (t - 0.5);
        float w = 1.0;
        color += SAMPLE_TEXTURE2D(_MBSource, sampler_linear_clamp, sampleUV).rgb * w;
        totalWeight += w;
    }

    color /= totalWeight;
    return float4(color, 1.0);
}

#endif // TAOTIE_MOTION_BLUR_INCLUDED
