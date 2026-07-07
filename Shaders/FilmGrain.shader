Shader "Hidden/TaoTie RP/Film Grain" {
    SubShader {
        Cull Off
        ZTest Always
        ZWrite Off

        Pass {
            Name "Film Grain"

            HLSLPROGRAM
            #include "ShaderLibrary/Common.hlsl"
            #include "ShaderLibrary/FilmGrain.hlsl"
            #pragma vertex GrainPassVertex
            #pragma fragment GrainFragment
            ENDHLSL
        }
    }
}
