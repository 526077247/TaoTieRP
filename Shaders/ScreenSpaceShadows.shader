Shader "Hidden/TaoTie RP/Screen Space Shadows" {
    SubShader {
        Cull Off
        ZTest Always
        ZWrite Off

        Pass {
            Name "Screen Space Shadows"

            HLSLPROGRAM
            #pragma vertex ScreenSpaceShadowsVertex
            #pragma fragment ScreenSpaceShadowsFragment

            #pragma multi_compile_local _ _SHADOW_FILTER_MEDIUM _SHADOW_FILTER_HIGH
            #pragma multi_compile_local _ _SHADOW_MASK

            #include "ShaderLibrary/Common.hlsl"
            #include "ShaderLibrary/Fragment.hlsl"
            #include "ShaderLibrary/Surface.hlsl"
            #include "ShaderLibrary/Shadows.hlsl"
            #include "ShaderLibrary/Light.hlsl"
            #include "ShaderLibrary/GBuffer.hlsl"

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float2 screenUV : TEXCOORD0;
            };

            struct VSInput {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings ScreenSpaceShadowsVertex(VSInput i) {
                Varyings output;
                output.positionCS = float4(i.positionOS.xy, 0.0, 1.0);
                output.screenUV = i.uv;
                if (_ProjectionParams.x < 0.0)
                    output.screenUV.y = 1.0 - output.screenUV.y;
                return output;
            }

            float3 ReconstructViewPos(float2 uv, float rawDepth) {
                float4 clipPos;
                clipPos.x = uv.x * 2.0 - 1.0;
                clipPos.y = uv.y * 2.0 - 1.0;
                #if UNITY_REVERSED_Z
                    clipPos.z = rawDepth;
                #else
                    clipPos.z = rawDepth * 2.0 - 1.0;
                #endif
                clipPos.w = 1.0;
                float4 viewPos = mul(_InverseViewProj, clipPos);
                return viewPos.xyz / viewPos.w;
            }

            float3 ReconstructNormal(float2 uv, float2 texel) {
                float3 l = ReconstructViewPos(uv - float2(texel.x, 0),
                    SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, sampler_point_clamp, uv - float2(texel.x, 0), 0).r);
                float3 r = ReconstructViewPos(uv + float2(texel.x, 0),
                    SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, sampler_point_clamp, uv + float2(texel.x, 0), 0).r);
                float3 d = ReconstructViewPos(uv - float2(0, texel.y),
                    SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, sampler_point_clamp, uv - float2(0, texel.y), 0).r);
                float3 u = ReconstructViewPos(uv + float2(0, texel.y),
                    SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, sampler_point_clamp, uv + float2(0, texel.y), 0).r);
                float3 dx = r - l;
                float3 dy = u - d;
                return normalize(cross(dy, dx));
            }

            float4 ScreenSpaceShadowsFragment(Varyings input) : SV_Target {
                float rawDepth = SAMPLE_DEPTH_TEXTURE_LOD(
                    _CameraDepthTexture, sampler_point_clamp, input.screenUV, 0);

                #if UNITY_REVERSED_Z
                    clip(rawDepth - 0.00001);
                #else
                    clip(0.99999 - rawDepth);
                #endif

                float3 positionWS = ReconstructWorldPos(input.screenUV, rawDepth);

                // Reconstruct view-space normal from depth neighbors for shadow normal bias
                float2 texel = _CameraBufferSize.xy;
                float3 viewNormal = ReconstructNormal(input.screenUV, texel);
                float3 worldNormal = mul((float3x3)UNITY_MATRIX_I_V, viewNormal);

                Surface surfaceWS;
                surfaceWS.position = positionWS;
                surfaceWS.screenUV = input.screenUV;
                surfaceWS.depth = -TransformWorldToView(positionWS).z;
                surfaceWS.interpolatedNormal = worldNormal;
                surfaceWS.receiveShadows = true;
                surfaceWS.dither = 0.0;

                ShadowData shadowData = GetShadowData(surfaceWS);

                DirectionalShadowData dirShadowData =
                    GetDirectionalShadowData(0, shadowData);

                float shadow = GetDirectionalShadowAttenuation(
                    dirShadowData, shadowData, surfaceWS);

                return shadow;
            }
            ENDHLSL
        }
    }
}
