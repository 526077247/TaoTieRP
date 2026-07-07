Shader "Hidden/TaoTie RP/Volumetric Fog" {
    SubShader {
        Cull Off
        ZTest Always
        ZWrite Off

        Pass {
            Name "Volumetric Fog"

            HLSLPROGRAM
            #include "ShaderLibrary/Common.hlsl"
            #include "ShaderLibrary/VolumetricFog.hlsl"
            #pragma vertex VFogPassVertex
            #pragma fragment VolumetricFogFragment
            ENDHLSL
        }
    }
}
