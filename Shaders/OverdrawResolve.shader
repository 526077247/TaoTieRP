Shader "Hidden/TaoTie RP/Overdraw Resolve"
{
    SubShader
    {
        Cull Off
        ZTest Always
        ZWrite Off

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "ShaderLibrary/Common.hlsl"

            float _OverdrawOpacity;
            TEXTURE2D(_OverdrawCounterTex);
            SAMPLER(sampler_OverdrawCounterTex);

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(uint vertexID : SV_VertexID)
            {
                Varyings output;
                output.positionCS = float4(
                    vertexID <= 1 ? -1.0 : 3.0,
                    vertexID == 1 ? 3.0 : -1.0,
                    0.0, 1.0);
                output.uv = float2(
                    vertexID <= 1 ? 0.0 : 2.0,
                    vertexID == 1 ? 2.0 : 0.0);
                if (_ProjectionParams.x < 0.0)
                    output.uv.y = 1.0 - output.uv.y;
                return output;
            }

            float3 OverdrawHeatColor(float t)
            {
                t = saturate(t);
                float3 c0 = float3(0.0, 0.0, 0.5);       // deep blue
                float3 c1 = float3(0.0, 1.0, 1.0);       // cyan
                float3 c2 = float3(0.0, 1.0, 0.0);       // green
                float3 c3 = float3(1.0, 1.0, 0.0);       // yellow
                float3 c4 = float3(1.0, 0.0, 0.0);       // red
                float3 c5 = float3(1.0, 1.0, 1.0);       // white (extreme overdraw)

                float scaled = t * 5.0;
                int idx = (int)floor(scaled);
                float f = scaled - idx;
                float3 colors[6] = { c0, c1, c2, c3, c4, c5 };
                return lerp(colors[min(idx, 5)], colors[min(idx + 1, 5)], f);
            }

            float4 frag(Varyings input) : SV_TARGET
            {
                float3 counter = SAMPLE_TEXTURE2D(_OverdrawCounterTex, sampler_OverdrawCounterTex, input.uv).rgb;
                float overdraw = counter.r;
                float3 color = OverdrawHeatColor(overdraw * 10.0);
                return float4(color, _OverdrawOpacity);
            }
            ENDHLSL
        }
    }
}
