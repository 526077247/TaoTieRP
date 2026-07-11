Shader "Hidden/TaoTie RP/Overdraw"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            Name "Overdraw"
            Tags { "LightMode" = "Overdraw" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off
            ColorMask RGB

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "ShaderLibrary/Common.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            float4 frag(Varyings input) : SV_TARGET
            {
                return float4(0.01, 0.01, 0.01, 1.0);
            }
            ENDHLSL
        }
    }
}
