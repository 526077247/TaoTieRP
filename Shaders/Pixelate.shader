Shader "Hidden/TaoTie RP/Pixelate" {
    SubShader {
        Cull Off
        ZTest Always
        ZWrite Off

        Pass {
            Name "Pixelate"

            HLSLPROGRAM
            #include "ShaderLibrary/Common.hlsl"
            #include "ShaderLibrary/Pixelate.hlsl"
            #pragma vertex PixelatePassVertex
            #pragma fragment PixelateFragment
            ENDHLSL
        }
    }
}
