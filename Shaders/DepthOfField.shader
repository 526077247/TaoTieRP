Shader "Hidden/TaoTie RP/Depth Of Field" {
    SubShader {
        Cull Off
        ZTest Always
        ZWrite Off

        HLSLINCLUDE
        #include "ShaderLibrary/Common.hlsl"
        #include "ShaderLibrary/DepthOfField.hlsl"
        ENDHLSL

        // Pass 0: Calculate CoC and store in alpha
        Pass {
            Name "DoF CoC"

            HLSLPROGRAM
            #pragma vertex DOFPassVertex
            #pragma fragment CoCPassFragment
            ENDHLSL
        }

        // Pass 1: Gaussian blur weighted by CoC
        Pass {
            Name "DoF Blur"

            HLSLPROGRAM
            #pragma vertex DOFPassVertex
            #pragma fragment DOFBlurPassFragment
            ENDHLSL
        }

        // Pass 2: Composite sharp + blurred
        Pass {
            Name "DoF Composite"

            HLSLPROGRAM
            #pragma vertex DOFPassVertex
            #pragma fragment DOFCompositePassFragment
            ENDHLSL
        }
    }
}
