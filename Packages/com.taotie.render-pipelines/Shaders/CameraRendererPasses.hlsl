#ifndef TAOTIE_CAMERA_RENDERER_PASSES_INCLUDED
#define TAOTIE_CAMERA_RENDERER_PASSES_INCLUDED

Texture2D<float4> _SourceTexture;

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
    if (_ProjectionParams.x < 0.0) {
        output.screenUV.y = 1.0 - output.screenUV.y;
    }
    return output;
}

float4 CopyPassFragment (Varyings input) : SV_TARGET {
    return _SourceTexture.SampleLevel(_Sampler_ClampU_ClampV_Linear, input.screenUV, 0);
}

float CopyDepthPassFragment (Varyings input) : SV_DEPTH {
    return _SourceTexture.SampleLevel(_Sampler_ClampU_ClampV_Linear, input.screenUV, 0).r;
}
#endif