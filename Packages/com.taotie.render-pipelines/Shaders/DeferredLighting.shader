Shader "Hidden/TaoTie RP/Deferred Lighting" {
    SubShader {
        Pass {
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma exclude_renderers gles
            #pragma multi_compile_local _ _TAOTIE_FORWARD_PLUS
            #pragma multi_compile_local _ _COMPUTE_BUFFER
            #pragma multi_compile_local _ LIGHTMAP_ON
            #pragma multi_compile_local _ _SHADOW_FILTER_MEDIUM _SHADOW_FILTER_HIGH
            #pragma multi_compile_local _ _SHADOW_MASK
            #pragma vertex DeferredLightingVertex
            #pragma fragment DeferredLightingFragment
            #define TAOTIE_DEFERRED_LIGHTING 1
            #include "DeferredLightingPass.hlsl"
            ENDHLSL
        }
    }
}
