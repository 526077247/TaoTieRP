Shader "Hidden/TaoTie RP/Posterize" {
    SubShader {
        Cull Off
        ZTest Always
        ZWrite Off

        Pass {
            Name "Posterize"

            HLSLPROGRAM
            #include "ShaderLibrary/Common.hlsl"
            #include "ShaderLibrary/Posterize.hlsl"
            #pragma vertex PosterizePassVertex
            #pragma fragment PosterizeFragment
            ENDHLSL
        }
    }
}
