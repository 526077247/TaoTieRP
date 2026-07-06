#ifndef TAOTIE_DEPTH_ONLY_PASS_INCLUDED
#define TAOTIE_DEPTH_ONLY_PASS_INCLUDED

struct Attributes {
    float3 positionOS : POSITION;
    float2 baseUV : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings {
    float4 positionCS : SV_POSITION;
    float2 baseUV : VAR_BASE_UV;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

Varyings DepthOnlyPassVertex (Attributes input) {
    Varyings output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    float3 positionWS = TransformObjectToWorld(input.positionOS);
    output.positionCS = TransformWorldToHClip(positionWS);
    output.baseUV = TransformBaseUV(input.baseUV);
    return output;
}

void DepthOnlyPassFragment (Varyings input) {
    UNITY_SETUP_INSTANCE_ID(input);
    InputConfig config = GetInputConfig(input.positionCS, input.baseUV);
    ClipLOD(config.fragment, unity_LODFade.x);
    #if defined(_CLIPPING)
        float4 base = GetBase(config);
        clip(base.a - UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _Cutoff));
    #endif
}

#endif
