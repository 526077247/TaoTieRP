Shader "Hidden/TaoTie RP/Vignette" {
    SubShader {
        Cull Off
        ZTest Always
        ZWrite Off

        Pass {
            Name "Vignette"

            HLSLPROGRAM
            #include "ShaderLibrary/Common.hlsl"
            #include "ShaderLibrary/Vignette.hlsl"
            #pragma vertex VignettePassVertex
            #pragma fragment VignetteFragment
            ENDHLSL
        }
    }
}
