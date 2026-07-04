#ifndef TAOTIE_DEFERRED_GBUFFER_PASS_INCLUDED
#define TAOTIE_DEFERRED_GBUFFER_PASS_INCLUDED

#include "ShaderLibrary/Common.hlsl"
#include "ShaderLibrary/Surface.hlsl"
#include "ShaderLibrary/Shadows.hlsl"
#include "ShaderLibrary/Light.hlsl"
#include "ShaderLibrary/BRDF.hlsl"
#include "ShaderLibrary/GI.hlsl"
#include "ShaderLibrary/GBuffer.hlsl"
#include "LitInput.hlsl"

struct Attributes {
    float3 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float2 baseUV : TEXCOORD0;
    GI_ATTRIBUTE_DATA
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings {
    float4 positionCS_SS : SV_POSITION;
    float3 positionWS : VAR_POSITION;
    float3 normalWS : VAR_NORMAL;
    #if defined(_NORMAL_MAP)
        float4 tangentWS : VAR_TANGENT;
    #endif
    float2 baseUV : VAR_BASE_UV;
    #if defined(_DETAIL_MAP)
        float2 detailUV : VAR_DETAIL_UV;
    #endif
    GI_VARYINGS_DATA
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

Varyings DeferredGBufferPassVertex (Attributes input) {
    Varyings output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    TRANSFER_GI_DATA(input, output);
    output.positionWS = TransformObjectToWorld(input.positionOS);
    output.positionCS_SS = TransformWorldToHClip(output.positionWS);
    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
    #if defined(_NORMAL_MAP)
        output.tangentWS = float4(
            TransformObjectToWorldDir(input.tangentOS.xyz), input.tangentOS.w
        );
    #endif
    output.baseUV = TransformBaseUV(input.baseUV);
    #if defined(_DETAIL_MAP)
        output.detailUV = TransformDetailUV(input.baseUV);
    #endif
    return output;
}

GBufferOutput DeferredGBufferPassFragment (Varyings input) {
    UNITY_SETUP_INSTANCE_ID(input);
    InputConfig config = GetInputConfig(input.positionCS_SS, input.baseUV);
    ClipLOD(config.fragment, unity_LODFade.x);

    #if defined(_MASK_MAP)
        config.useMask = true;
    #endif
    #if defined(_DETAIL_MAP)
        config.detailUV = input.detailUV;
        config.useDetail = true;
    #endif

    float4 base = GetBase(config);
    #if defined(_CLIPPING)
        clip(base.a - GetCutoff(config));
    #endif

    float3 normalWS;
    #if defined(_NORMAL_MAP)
        normalWS = NormalTangentToWorld(
            GetNormalTS(config), input.normalWS, input.tangentWS
        );
    #else
        normalWS = normalize(input.normalWS);
    #endif

    GBufferOutput output;
    output.albedoAO = float4(base.rgb, GetOcclusion(config));
    output.normalMS = float4(EncodeNormal(normalWS), GetMetallic(config), GetSmoothness(config));
    output.emission = float4(GetEmission(config), 1.0);
    return output;
}

#endif
