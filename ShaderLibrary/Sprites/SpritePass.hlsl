#ifndef TAOTIE_SPRITE_PASS_INCLUDED
#define TAOTIE_SPRITE_PASS_INCLUDED

struct Attributes {
    float4 positionOS : POSITION;
    float4 color : COLOR;
    float2 baseUV : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings {
    float4 positionCS_SS : SV_POSITION;
    float4 color : VAR_COLOR;
    float2 baseUV : VAR_BASE_UV;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

Varyings SpritePassVertex (Attributes input) {
    Varyings output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
    output.positionCS_SS = TransformWorldToHClip(positionWS);
    output.color = input.color * INPUT_PROP(_Color);
    output.baseUV = input.baseUV;
    return output;
}

float4 SpritePassFragment (Varyings input) : SV_TARGET {
    UNITY_SETUP_INSTANCE_ID(input);
    float4 base = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.baseUV);
    float4 color = base * input.color;
#if defined(_ALPHA_CLIP)
    clip(color.a - INPUT_PROP(_Cutoff));
#endif
    return color;
}

#endif
