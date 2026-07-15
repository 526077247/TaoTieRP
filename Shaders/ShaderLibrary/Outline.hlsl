#ifndef TAOTIE_OUTLINE_INCLUDED
#define TAOTIE_OUTLINE_INCLUDED

#include "Common.hlsl"
#include "GBuffer.hlsl"

TEXTURE2D(_GBufferNormalMS);
TEXTURE2D(_OutlineSource);

float4 _OutlineColor;
float _OutlineDepthSensitivity;
float _OutlineNormalSensitivity;
float _OutlineWidth;

struct OutlineVaryings
{
    float4 positionCS : SV_POSITION;
    float2 screenUV : TEXCOORD0;
};

OutlineVaryings OutlinePassVertex(
    float3 positionOS : POSITION,
    float2 uv : TEXCOORD0)
{
    OutlineVaryings output;
    output.positionCS = float4(positionOS.xy, 0.0, 1.0);
    output.screenUV = uv;
    // On D3D (_ProjectionParams.x < 0), flip Y to match texture convention.
    if (_ProjectionParams.x < 0.0) {
        output.screenUV.y = 1.0 - output.screenUV.y;
    }
    return output;
}

float4 OutlinePassFragment(OutlineVaryings input) : SV_Target
{
    float2 uv = input.screenUV;
    float2 texel = _CameraBufferSize.xy * _OutlineWidth;

    // Roberts Cross on depth �?4-tap pattern
    float d0 = SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, sampler_point_clamp, uv, 0);
    float d1 = SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, sampler_point_clamp, uv + float2(texel.x, 0.0), 0);
    float d2 = SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, sampler_point_clamp, uv + float2(0.0, texel.y), 0);
    float d3 = SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, sampler_point_clamp, uv + float2(texel.x, texel.y), 0);

    // Convert to linear eye depth so sensitivity is in view-space units
    float ld0 = LinearEyeDepth(d0, _ZBufferParams);
    float ld1 = LinearEyeDepth(d1, _ZBufferParams);
    float ld2 = LinearEyeDepth(d2, _ZBufferParams);
    float ld3 = LinearEyeDepth(d3, _ZBufferParams);

    float depthDiff = max(abs(ld0 - ld3), abs(ld1 - ld2));
    float depthEdge = smoothstep(0.0, _OutlineDepthSensitivity, depthDiff);

    // Normal edge detection �?only in deferred path where G-Buffer normals exist
    float normalEdge = 0.0;

    #if defined(_OUTLINE_USE_GBUFFER_NORMALS)
        float3 n0 = DecodeGBufferNormal(SAMPLE_TEXTURE2D_LOD(_GBufferNormalMS, sampler_point_clamp, uv, 0).xy);
        float3 n1 = DecodeGBufferNormal(SAMPLE_TEXTURE2D_LOD(_GBufferNormalMS, sampler_point_clamp, uv + float2(texel.x, 0.0), 0).xy);
        float3 n2 = DecodeGBufferNormal(SAMPLE_TEXTURE2D_LOD(_GBufferNormalMS, sampler_point_clamp, uv + float2(0.0, texel.y), 0).xy);
        float3 n3 = DecodeGBufferNormal(SAMPLE_TEXTURE2D_LOD(_GBufferNormalMS, sampler_point_clamp, uv + float2(texel.x, texel.y), 0).xy);

        float normalDiff = max(1.0 - dot(n0, n3), 1.0 - dot(n1, n2));
        normalEdge = smoothstep(0.0, _OutlineNormalSensitivity, normalDiff);
    #endif

    float edge = max(depthEdge, normalEdge);

    float4 source = SAMPLE_TEXTURE2D(_OutlineSource, sampler_linear_clamp, uv);
    float3 color = lerp(source.rgb, _OutlineColor.rgb, edge * _OutlineColor.a);
    return float4(color, source.a);
}

float4 OutlineCopyFragment(OutlineVaryings input) : SV_Target
{
    return SAMPLE_TEXTURE2D(_OutlineSource, sampler_linear_clamp, input.screenUV);
}

#endif // TAOTIE_OUTLINE_INCLUDED
