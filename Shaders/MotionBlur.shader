Shader "Hidden/TaoTie RP/Motion Blur" {
    SubShader {
        Cull Off
        ZTest Always
        ZWrite Off

        Pass {
            Name "Motion Blur"

            HLSLPROGRAM
            #include "ShaderLibrary/Common.hlsl"
            #include "ShaderLibrary/MotionBlur.hlsl"
            #pragma vertex MBPassVertex
            #pragma fragment MBFragment
            ENDHLSL
        }
    }
}
