Shader "Hidden/TaoTie RP/Tilt Shift" {
    SubShader {
        Cull Off
        ZTest Always
        ZWrite Off

        Pass {
            Name "TiltShift"

            HLSLPROGRAM
            #include "ShaderLibrary/Common.hlsl"
            #include "ShaderLibrary/TiltShift.hlsl"
            #pragma vertex TiltShiftPassVertex
            #pragma fragment TiltShiftFragment
            ENDHLSL
        }
    }
}
