Shader "Hidden/TaoTie RP/Color Curves" {
    SubShader {
        Cull Off
        ZTest Always
        ZWrite Off

        Pass {
            Name "Color Curves"

            HLSLPROGRAM
            #include "ShaderLibrary/Common.hlsl"
            #include "ShaderLibrary/ColorCurves.hlsl"
            #pragma vertex CCPassVertex
            #pragma fragment CCFragment
            ENDHLSL
        }
    }
}
