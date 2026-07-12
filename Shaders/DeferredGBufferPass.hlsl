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

    float3 viewDir = normalize(_WorldSpaceCameraPos - input.positionWS);
    float metallic = GetMetallic(config);
    float occlusion = GetOcclusion(config);
    float smoothness = GetSmoothness(config);

    // Build surface + BRDF to compute GI diffuse (deferred lighting pass can't access lightmap UVs)
    Surface surface;
    surface.position = input.positionWS;
    surface.normal = normalWS;
    surface.interpolatedNormal = normalWS;
    surface.viewDirection = viewDir;
    surface.depth = -TransformWorldToView(input.positionWS).z;
    surface.color = base.rgb;
    #if UNITY_COLORSPACE_GAMMA
        surface.color = SRGBToLinear(surface.color);
    #endif
    surface.alpha = 1.0;
    surface.metallic = metallic;
    surface.occlusion = occlusion;
    surface.smoothness = smoothness;
    surface.fresnelStrength = GetFresnel(config);
    surface.dither = InterleavedGradientNoise(input.positionCS_SS, 0);
    #if defined(SHADER_API_GLES)
    surface.renderingLayerMask = unity_RenderingLayer.x;
    #else
    surface.renderingLayerMask = asuint(unity_RenderingLayer.x);
    #endif
    surface.receiveShadows = INPUT_PROP(_ReceiveShadows) > 0.5;

    BRDF brdf = GetBRDF(surface);
    GI gi = GetGI(GI_FRAGMENT_DATA(input), surface, brdf);
    #if UNITY_COLORSPACE_GAMMA
        #if !defined(LIGHTMAP_ON)
            gi.diffuse = SRGBToLinear(gi.diffuse);
        #endif
    #endif

    // Bake GI diffuse into emission so deferred lighting pass can use it without lightmap UVs.
    // Don't apply occlusion here — deferred lighting pass applies it via surface.occlusion (incl. SSAO).
    float3 bakedGI = gi.diffuse * brdf.diffuse;

    GBufferOutput output;
    output.albedoAO = float4(base.rgb, occlusion);
    output.normalMS = float4(EncodeNormal(normalWS), metallic, smoothness);
    #if UNITY_COLORSPACE_GAMMA
        output.emission = PackGBufferEmission(GetEmission(config) + LinearToSRGB(bakedGI), surface.renderingLayerMask);
    #else
        output.emission = PackGBufferEmission(GetEmission(config) + bakedGI, surface.renderingLayerMask);
    #endif
    return output;
}

#endif
