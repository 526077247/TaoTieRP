#ifndef TAOTIE_CAMERA_RENDERER_PASSES_INCLUDED
#define TAOTIE_CAMERA_RENDERER_PASSES_INCLUDED

TEXTURE2D(_SourceTexture);
SAMPLER(sampler_SourceTexture);

struct Varyings {
    float4 positionCS : SV_POSITION;
    float2 screenUV : VAR_SCREEN_UV;
};

struct VSInput
{
    float3 positionOS : POSITION;
    float2 uv : TEXCOORD0;
};

Varyings DefaultPassVertex (VSInput i)
{
    Varyings output;
    output.positionCS = mul(UNITY_MATRIX_VP, float4(i.positionOS, 1.f));
    output.screenUV = i.uv;
    return output;
}

Varyings DepthCopyPassVertex (VSInput i)
{
    Varyings output;
    output.positionCS = mul(UNITY_MATRIX_VP, float4(i.positionOS, 1.f));
    output.screenUV = i.uv;
    return output;
}

float4 CopyPassFragment (Varyings input) : SV_TARGET {
    return SAMPLE_TEXTURE2D_LOD(_SourceTexture, sampler_SourceTexture, input.screenUV, 0);
}

float CopyDepthPassFragment (Varyings input) : SV_DEPTH {
    return SAMPLE_TEXTURE2D_LOD(_SourceTexture, sampler_SourceTexture, input.screenUV, 0).r;
}
#endif
