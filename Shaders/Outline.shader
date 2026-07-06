Shader "Hidden/TaoTie RP/Outline" {
    SubShader {
        Cull Off
        ZTest Always
        ZWrite Off

        Pass {
            Name "Outline"

            HLSLPROGRAM
            #pragma multi_compile _ _OUTLINE_USE_GBUFFER_NORMALS
            #pragma vertex OutlinePassVertex
            #pragma fragment OutlinePassFragment
            #include "ShaderLibrary/Outline.hlsl"
            ENDHLSL
        }

        Pass {
            Name "Outline Copy"

            HLSLPROGRAM
            #pragma vertex OutlinePassVertex
            #pragma fragment OutlineCopyFragment
            #include "ShaderLibrary/Outline.hlsl"
            ENDHLSL
        }
    }
}
