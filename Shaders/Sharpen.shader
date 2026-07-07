Shader "Hidden/TaoTie RP/Sharpen" {
    SubShader {
        Cull Off
        ZTest Always
        ZWrite Off

        Pass {
            Name "Sharpen"

            HLSLPROGRAM
            #include "ShaderLibrary/Common.hlsl"
            #include "ShaderLibrary/Sharpen.hlsl"
            #pragma vertex SharpenPassVertex
            #pragma fragment SharpenFragment
            ENDHLSL
        }
    }
}
