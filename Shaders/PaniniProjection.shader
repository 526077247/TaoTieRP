Shader "Hidden/TaoTie RP/Panini Projection" {
    SubShader {
        Cull Off
        ZTest Always
        ZWrite Off

        Pass {
            Name "Panini Projection"

            HLSLPROGRAM
            #include "ShaderLibrary/Common.hlsl"
            #include "ShaderLibrary/PaniniProjection.hlsl"
            #pragma vertex PPPassVertex
            #pragma fragment PPFragment
            ENDHLSL
        }
    }
}
