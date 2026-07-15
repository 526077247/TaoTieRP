Shader "Hidden/TaoTie RP/Lens Flare" {
    Properties {
        _FlareTex ("Texture", 2D) = "white" {}
    }
    SubShader {
        Cull Off
        ZWrite Off
        ZTest Always

        // Pass 0: Additive
        Pass {
            Name "Additive"
            Blend One One

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ FLARE_CIRCLE FLARE_POLYGON
            #pragma multi_compile _ FLARE_INVERSE_SDF
            #pragma multi_compile _ FLARE_HAS_OCCLUSION
            #pragma multi_compile _ FLARE_OPENGL3_OR_OPENGLCORE

            #include "ShaderLibrary/LensFlareSetup.hlsl"
            ENDHLSL
        }

        // Pass 1: Screen
        Pass {
            Name "Screen"
            Blend One OneMinusSrcColor

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ FLARE_CIRCLE FLARE_POLYGON
            #pragma multi_compile _ FLARE_INVERSE_SDF
            #pragma multi_compile _ FLARE_HAS_OCCLUSION
            #pragma multi_compile _ FLARE_OPENGL3_OR_OPENGLCORE

            #include "ShaderLibrary/LensFlareSetup.hlsl"
            ENDHLSL
        }

        // Pass 2: Premultiply
        Pass {
            Name "Premultiply"
            Blend One OneMinusSrcAlpha
            ColorMask RGB

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ FLARE_CIRCLE FLARE_POLYGON
            #pragma multi_compile _ FLARE_INVERSE_SDF
            #pragma multi_compile _ FLARE_HAS_OCCLUSION
            #pragma multi_compile _ FLARE_OPENGL3_OR_OPENGLCORE

            #include "ShaderLibrary/LensFlareSetup.hlsl"
            ENDHLSL
        }

        // Pass 3: Lerp
        Pass {
            Name "Lerp"
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ FLARE_CIRCLE FLARE_POLYGON
            #pragma multi_compile _ FLARE_INVERSE_SDF
            #pragma multi_compile _ FLARE_HAS_OCCLUSION
            #pragma multi_compile _ FLARE_OPENGL3_OR_OPENGLCORE

            #include "ShaderLibrary/LensFlareSetup.hlsl"
            ENDHLSL
        }

        // Pass 4: Occlusion (writes to occlusionRT)
        Pass {
            Name "Occlusion"
            Blend One Zero

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vertOcclusion
            #pragma fragment fragOcclusion

            #define FLARE_COMPUTE_OCCLUSION
            #include "ShaderLibrary/LensFlareSetup.hlsl"
            ENDHLSL
        }
    }
}
