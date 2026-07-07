Shader "Hidden/TaoTie RP/Chromatic Aberration" {
    SubShader {
        Cull Off
        ZTest Always
        ZWrite Off

        Pass {
            Name "Chromatic Aberration"

            HLSLPROGRAM
            #include "ShaderLibrary/Common.hlsl"
            #include "ShaderLibrary/ChromaticAberration.hlsl"
            #pragma vertex CAPassVertex
            #pragma fragment CAFragment
            ENDHLSL
        }
    }
}
