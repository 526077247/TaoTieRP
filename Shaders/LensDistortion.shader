Shader "Hidden/TaoTie RP/Lens Distortion" {
    SubShader {
        Cull Off
        ZTest Always
        ZWrite Off

        Pass {
            Name "Lens Distortion"

            HLSLPROGRAM
            #include "ShaderLibrary/Common.hlsl"
            #include "ShaderLibrary/LensDistortion.hlsl"
            #pragma vertex LDPassVertex
            #pragma fragment LDFragment
            ENDHLSL
        }
    }
}
