Shader "Hidden/TaoTie RP/TAA" {
    SubShader {
        Cull Off
        ZTest Always
        ZWrite Off

        HLSLINCLUDE
        #include "ShaderLibrary/Common.hlsl"
        ENDHLSL

        Pass {
            Name "TAA Resolve"

            HLSLPROGRAM
            #pragma vertex DefaultPassVertex
            #pragma fragment TAAResolveFragment
            #pragma multi_compile _ UNITY_COLORSPACE_GAMMA

            #include "ShaderLibrary/Common.hlsl"

            Texture2D<float4> _TAACurrentColor;
            Texture2D<float4> _TAAHistoryColor;

            float4 _TAATexelSize; // (1/w, 1/h, w, h)
            float _TAABlendFactor;
            float _TAAAntiFlicker;
            float2 _TAAJitter; // current frame jitter in texels

            float4x4 _InverseNonJitteredViewProj;
            float4x4 _PrevViewProj;

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float2 screenUV : TEXCOORD0;
            };

            struct VSInput {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings DefaultPassVertex (VSInput i) {
                Varyings output;
                output.positionCS = mul(UNITY_MATRIX_VP, float4(i.positionOS, 1.0));
                output.screenUV = i.uv;
                return output;
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
                #if UNITY_REVERSED_Z
                    prevUV.y = 1.0 - prevUV.y;
                #endif
                return prevUV;
            }

            float4 SampleCurrent(float2 uv) {
                return _TAACurrentColor.SampleLevel(_Sampler_ClampU_ClampV_Point, uv, 0);
            }

            float4 TAAResolveFragment(Varyings input) : SV_TARGET {
                float2 uv = input.screenUV;

                // Un-jitter: offset UV to cancel jitter applied during geometry rendering
                float2 jitterOffset = _TAAJitter * _TAATexelSize.xy;

                // 1. Current color (un-jittered by sampling at offset)
                float4 current = _TAACurrentColor.SampleLevel(
                    _Sampler_ClampU_ClampV_Point, uv + jitterOffset, 0);

                // 2. Reconstruct world position from depth (depth sampled at jittered uv)
                float depth = _CameraDepthTexture.SampleLevel(
                    _Sampler_ClampU_ClampV_Point, uv, 0);
                #if UNITY_REVERSED_Z
                    if (depth >= 1.0) return current;
                #else
                    if (depth <= 0.0) return current;
                #endif

                float3 worldPos = ReconstructWorldPos(uv, depth);

                // 3. Reproject to previous frame UV
                float2 prevUV = Reproject(worldPos);

                // 4. Check if reprojected UV is valid
                bool valid = all(prevUV >= 0.0) && all(prevUV <= 1.0);
                if (!valid) return current;

                // 5. Sample history at reprojected UV
                float4 history = _TAAHistoryColor.SampleLevel(
                    _Sampler_ClampU_ClampV_Linear, prevUV, 0);

                // 6. Neighborhood clamping (3x3 min/max) with anti-flicker expansion
                float2 texel = _TAATexelSize.xy;
                float3 colorMin = current.rgb;
                float3 colorMax = current.rgb;
                for (int x = -1; x <= 1; x++) {
                    for (int y = -1; y <= 1; y++) {
                        if (x == 0 && y == 0) continue;
                        float3 c = _TAACurrentColor.SampleLevel(
                            _Sampler_ClampU_ClampV_Point,
                            uv + jitterOffset + float2(x, y) * texel, 0).rgb;
                        colorMin = min(colorMin, c);
                        colorMax = max(colorMax, c);
                    }
                }

                // Anti-flicker: expand clamp bounds by configurable amount of the neighborhood range
                float3 neighborhoodRange = colorMax - colorMin;
                colorMin -= neighborhoodRange * _TAAAntiFlicker;
                colorMax += neighborhoodRange * _TAAAntiFlicker;

                // 7. Clamp history to neighborhood
                float3 historyClamped = clamp(history.rgb, colorMin, colorMax);

                // 8. Blend
                float3 result = lerp(current.rgb, historyClamped, _TAABlendFactor);

                return float4(result, current.a);
            }
            ENDHLSL
        }
    }
}
