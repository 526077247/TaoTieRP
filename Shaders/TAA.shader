Shader "Hidden/TaoTie RP/TAA" {
    SubShader {
        Cull Off
        ZTest Always
        ZWrite Off

        HLSLINCLUDE
        #include "ShaderLibrary/Common.hlsl"

        TEXTURE2D(_TAACurrentColor);
        TEXTURE2D(_TAAHistoryColor);
        SAMPLER(sampler_TAAHistoryColor);

        float4 _TAATexelSize;
        float  _TAAFrameInfluence;
        float  _TAAVarianceClampScale;
        float2 _TAAJitter;

        float4x4 _InverseNonJitteredViewProj;
        float4x4 _PrevViewProj;

        struct VSInput {
            float3 positionOS : POSITION;
            float2 uv : TEXCOORD0;
        };

        struct Varyings {
            float4 positionCS : SV_POSITION;
            float2 screenUV : TEXCOORD0;
        };

        Varyings TAAPassVertex(VSInput i) {
            Varyings o;
            o.positionCS = float4(i.positionOS.xy, 0.0, 1.0);
            o.screenUV = i.uv;
            if (_ProjectionParams.x < 0.0)
                o.screenUV.y = 1.0 - o.screenUV.y;
            return o;
        }

        half3 RGBtoYCoCg(half3 rgb) {
            half Y  = dot(rgb, half3(0.25, 0.5, 0.25));
            half Co = dot(rgb, half3(0.5, 0.0, -0.5));
            half Cg = dot(rgb, half3(-0.25, 0.5, -0.25));
            return half3(Y, Co, Cg);
        }
        half3 YCoCgtoRGB(half3 ycocg) {
            half Y  = ycocg.x;
            half Co = ycocg.y;
            half Cg = ycocg.z;
            return half3(Y + Co - Cg, Y + Cg, Y - Co - Cg);
        }

        half3 ClipToAABBCenter(half3 history, half3 minimum, half3 maximum) {
            half3 center  = 0.5 * (maximum + minimum);
            half3 extents = max(0.5 * (maximum - minimum), 1e-8);
            half3 offset  = history - center;
            half3 vUnit   = offset / extents;
            half  maxUnit = max(max(abs(vUnit.x), abs(vUnit.y)), abs(vUnit.z));
            if (maxUnit > 1.0)
                return center + offset / maxUnit;
            return history;
        }

        float3 ReconstructWorldPos(float2 uv, float depth) {
            float4 clipPos;
            clipPos.x = uv.x * 2.0 - 1.0;
            clipPos.y = uv.y * 2.0 - 1.0;
            #if UNITY_REVERSED_Z
                clipPos.z = depth;
            #else
                clipPos.z = depth * 2.0 - 1.0;
            #endif
            clipPos.w = 1.0;
            float4 worldPos = mul(_InverseNonJitteredViewProj, clipPos);
            return worldPos.xyz / worldPos.w;
        }

        float2 Reproject(float3 worldPos) {
            float4 prevClip = mul(_PrevViewProj, float4(worldPos, 1.0));
            float2 prevUV = prevClip.xy / prevClip.w;
            prevUV = prevUV * 0.5 + 0.5;
            return prevUV;
        }

        half4 TAAResolveFragment(Varyings input) : SV_TARGET {
            float2 uv = input.screenUV;
            float2 texel = _TAATexelSize.xy;
            float2 jitterOffset = _TAAJitter * texel;

            half4 current = SAMPLE_TEXTURE2D_LOD(_TAACurrentColor, sampler_TAAHistoryColor,
                uv + jitterOffset, 0);

            float depth = SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, sampler_point_clamp, uv, 0);
            #if UNITY_REVERSED_Z
                if (depth >= 1.0) return current;
            #else
                if (depth <= 0.0) return current;
            #endif

            float3 worldPos = ReconstructWorldPos(uv, depth);
            float2 prevUV = Reproject(worldPos);

            // History out-of-bounds or first frame → return current directly
            half frameInfluence = any(abs(prevUV - 0.5) > 0.5)
                ? 1.0
                : _TAAFrameInfluence;

            if (frameInfluence >= 1.0)
                return current;

            // Bilinear history sampling (safe, no NaN from Catmull-Rom weight divisions)
            half4 history = SAMPLE_TEXTURE2D_LOD(_TAAHistoryColor, sampler_TAAHistoryColor,
                prevUV, 0);

            // 3x3 neighborhood in YCoCg
            half3 moment1 = 0;
            half3 moment2 = 0;
            half3 neighborhoodMin = current.rgb;
            half3 neighborhoodMax = current.rgb;

            [unroll]
            for (int x = -1; x <= 1; x++) {
                [unroll]
                for (int y = -1; y <= 1; y++) {
                    half3 c = SAMPLE_TEXTURE2D_LOD(_TAACurrentColor, sampler_TAAHistoryColor,
                        uv + jitterOffset + float2(x, y) * texel, 0).rgb;
                    half3 ycocg = RGBtoYCoCg(c);
                    moment1 += ycocg;
                    moment2 += ycocg * ycocg;
                    neighborhoodMin = min(neighborhoodMin, ycocg);
                    neighborhoodMax = max(neighborhoodMax, ycocg);
                }
            }

            // Variance clamp (mean ± scale * stddev, intersected with min/max)
            half perSample = 1.0 / 9.0;
            half3 mean   = moment1 * perSample;
            half3 stdDev = sqrt(max(moment2 * perSample - mean * mean, 0.0));
            half3 devMin = mean - _TAAVarianceClampScale * stdDev;
            half3 devMax = mean + _TAAVarianceClampScale * stdDev;
            half3 boxMin = max(neighborhoodMin, devMin);
            half3 boxMax = min(neighborhoodMax, devMax);

            // AABB clip-to-center (Playdead/INSIDE style)
            half3 historyYCoCg = RGBtoYCoCg(history.rgb);
            half3 clamped = ClipToAABBCenter(historyYCoCg, boxMin, boxMax);
            half3 historyClamped = YCoCgtoRGB(clamped);

            // Blend
            half3 result = lerp(historyClamped, current.rgb, frameInfluence);

            // NaN safety
            result = any(isnan(result)) ? current.rgb : result;

            return half4(result, current.a);
        }
        ENDHLSL

        Pass {
            Name "TAA Resolve"

            HLSLPROGRAM
            #pragma vertex TAAPassVertex
            #pragma fragment TAAResolveFragment
            #pragma multi_compile _ UNITY_COLORSPACE_GAMMA
            ENDHLSL
        }
    }
}
