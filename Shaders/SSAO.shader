Shader "Hidden/TaoTie RP/SSAO" {
    SubShader {
        Cull Off
        ZTest Always
        ZWrite Off

        HLSLINCLUDE
        #include "ShaderLibrary/Common.hlsl"
        ENDHLSL

        // Pass 0: Generate SSAO
        Pass {
            Name "SSAO Generate"
            Blend Off

            HLSLPROGRAM
            #pragma vertex SSAOVertex
            #pragma fragment SSAOGenerateFragment
            #pragma multi_compile _ SSAO_LOW SSAO_MEDIUM SSAO_HIGH

            #include "ShaderLibrary/Common.hlsl"

            float4 _SSAOTexelSize;    // (1/w, 1/h, w, h)
            float4 _SSAOParams;      // x=intensity, y=radius, z=falloff, w=downsample
            float4x4 _SSAOInverseProj;
            float4x4 _SSAOProj;

            #if defined(SSAO_HIGH)
                #define SAMPLE_COUNT 12
            #elif defined(SSAO_MEDIUM)
                #define SAMPLE_COUNT 8
            #else
                #define SAMPLE_COUNT 4
            #endif

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float2 screenUV : TEXCOORD0;
            };

            struct VSInput {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings SSAOVertex(VSInput i) {
                Varyings output;
                output.positionCS = float4(i.positionOS.xy, 0.0, 1.0);
                output.screenUV = i.uv;
                if (_ProjectionParams.x < 0.0)
                    output.screenUV.y = 1.0 - output.screenUV.y;
                return output;
            }

            // Reconstruct view-space position from UV and depth
            float3 ReconstructViewPos(float2 uv, float depth) {
                float4 clipPos;
                clipPos.x = uv.x * 2.0 - 1.0;
                clipPos.y = uv.y * 2.0 - 1.0;
                #if UNITY_REVERSED_Z
                    clipPos.z = depth;
                #else
                    clipPos.z = depth * 2.0 - 1.0;
                #endif
                clipPos.w = 1.0;
                float4 viewPos = mul(_SSAOInverseProj, clipPos);
                return viewPos.xyz / viewPos.w;
            }

            // Reconstruct normal from depth using cross product of neighbors
            float3 ReconstructNormal(float2 uv, float3 vpos, float2 texel) {
                float3 l = ReconstructViewPos(uv - float2(texel.x, 0), 
                    _CameraDepthTexture.SampleLevel(sampler_point_clamp, uv - float2(texel.x, 0), 0).r);
                float3 r = ReconstructViewPos(uv + float2(texel.x, 0),
                    _CameraDepthTexture.SampleLevel(sampler_point_clamp, uv + float2(texel.x, 0), 0).r);
                float3 d = ReconstructViewPos(uv - float2(0, texel.y),
                    _CameraDepthTexture.SampleLevel(sampler_point_clamp, uv - float2(0, texel.y), 0).r);
                float3 u = ReconstructViewPos(uv + float2(0, texel.y),
                    _CameraDepthTexture.SampleLevel(sampler_point_clamp, uv + float2(0, texel.y), 0).r);

                float3 dx = r - l;
                float3 dy = u - d;
                return normalize(cross(dy, dx));
            }

            // Hardcoded sample directions (cosine-weighted hemisphere)
            static float3 sampleDirs[12] = {
                float3(0.3548, 0.3548, 0.8660),
                float3(-0.3548, 0.3548, 0.8660),
                float3(0.3548, -0.3548, 0.8660),
                float3(-0.3548, -0.3548, 0.8660),
                float3(0.6124, 0.6124, 0.5000),
                float3(-0.6124, 0.6124, 0.5000),
                float3(0.6124, -0.6124, 0.5000),
                float3(-0.6124, -0.6124, 0.5000),
                float3(0.8660, 0.3548, 0.3548),
                float3(-0.8660, 0.3548, 0.3548),
                float3(0.8660, -0.3548, 0.3548),
                float3(-0.8660, -0.3548, 0.3548),
            };

            float4 SSAOGenerateFragment(Varyings input) : SV_Target {
                float2 uv = input.screenUV;
                float2 texel = _SSAOTexelSize.xy;

                float rawDepth = _CameraDepthTexture.SampleLevel(sampler_point_clamp, uv, 0).r;

                #if UNITY_REVERSED_Z
                    if (rawDepth >= 1.0) return float4(1, 1, 1, 1);
                #else
                    if (rawDepth <= 0.0) return float4(1, 1, 1, 1);
                #endif

                float3 vpos = ReconstructViewPos(uv, rawDepth);
                float3 normal = ReconstructNormal(uv, vpos, texel);

                float radius = _SSAOParams.y;
                float intensity = _SSAOParams.x;
                float falloff = _SSAOParams.z;

                // Use interleaved gradient noise for rotation
                float noise = InterleavedGradientNoise(input.positionCS.xy, 0);
                float angle = noise * 6.2831853;
                float2 rot = float2(cos(angle), sin(angle));

                float ao = 0.0;
                [unroll]
                for (int i = 0; i < SAMPLE_COUNT; i++) {
                    float3 sampleDir = sampleDirs[i];
                    // Rotate sample direction in tangent plane
                    sampleDir.xy = float2(
                        sampleDir.x * rot.x - sampleDir.y * rot.y,
                        sampleDir.x * rot.y + sampleDir.y * rot.x
                    );
                    // Scale by radius and distance falloff
                    float scale = lerp(0.1, 1.0, float(i) / float(SAMPLE_COUNT));
                    sampleDir *= radius * scale;

                    float3 samplePos = vpos + sampleDir;

                    // Project sample position to screen
                    float4 clipPos = mul(_SSAOProj, float4(samplePos, 1.0));
                    float2 sampleUV = clipPos.xy / clipPos.w;
                    sampleUV = sampleUV * 0.5 + 0.5;
                    if (_ProjectionParams.x < 0.0)
                        sampleUV.y = 1.0 - sampleUV.y;

                    // Sample depth at projected position
                    float sampleDepthRaw = _CameraDepthTexture.SampleLevel(sampler_point_clamp, sampleUV, 0).r;

                    #if UNITY_REVERSED_Z
                        bool valid = sampleDepthRaw < 1.0;
                    #else
                        bool valid = sampleDepthRaw > 0.0;
                    #endif

                    if (valid) {
                        float3 sampleViewPos = ReconstructViewPos(sampleUV, sampleDepthRaw);
                        float3 v = sampleViewPos - vpos;
                        float dist = length(v);
                        float3 dir = v / max(dist, 0.0001);

                        // Alchemy AO formula
                        float dotVal = dot(dir, normal) - 0.02 * length(vpos);
                        float a1 = max(dotVal, 0.0);
                        float a2 = dot(v, v) + 0.001;
                        ao += a1 / a2;
                    }
                }

                ao *= radius;
                ao /= float(SAMPLE_COUNT);

                // Apply intensity and falloff
                float linearDepth = length(vpos);
                float falloffFactor = 1.0 - saturate(linearDepth / falloff);
                falloffFactor = falloffFactor * falloffFactor;

                ao = saturate(ao * intensity * falloffFactor);
                ao = 1.0 - ao;

                return float4(ao, ao, ao, 1.0);
            }
            ENDHLSL
        }

        // Pass 1: Horizontal Blur
        Pass {
            Name "SSAO Horizontal Blur"
            Blend Off

            HLSLPROGRAM
            #pragma vertex SSAOVertex
            #pragma fragment HorizontalBlurFragment

            #include "ShaderLibrary/Common.hlsl"

            float4 _SSAOTexelSize;

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float2 screenUV : TEXCOORD0;
            };

            struct VSInput {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings SSAOVertex(VSInput i) {
                Varyings output;
                output.positionCS = float4(i.positionOS.xy, 0.0, 1.0);
                output.screenUV = i.uv;
                if (_ProjectionParams.x < 0.0)
                    output.screenUV.y = 1.0 - output.screenUV.y;
                return output;
            }

            TEXTURE2D(_SSAOSource);

            // Bilateral blur weights (5-tap)
            static const float weights[5] = {0.227027, 0.316216, 0.316216, 0.070270, 0.070270};
            static const float offsets[5] = {-3.230769, -1.384615, 1.384615, 3.230769, 0};

            float4 HorizontalBlurFragment(Varyings input) : SV_Target {
                float2 uv = input.screenUV;
                float2 delta = float2(_SSAOTexelSize.x, 0.0);

                float result = 0;
                // Center + 4 neighbors
                result += _SSAOSource.SampleLevel(sampler_linear_clamp, uv, 0).r * 0.227027;
                result += _SSAOSource.SampleLevel(sampler_linear_clamp, uv + delta * (-1.384615), 0).r * 0.316216;
                result += _SSAOSource.SampleLevel(sampler_linear_clamp, uv + delta * (1.384615), 0).r * 0.316216;
                result += _SSAOSource.SampleLevel(sampler_linear_clamp, uv + delta * (-3.230769), 0).r * 0.070270;
                result += _SSAOSource.SampleLevel(sampler_linear_clamp, uv + delta * (3.230769), 0).r * 0.070270;
                result /= (0.227027 + 0.316216 + 0.316216 + 0.070270 + 0.070270);

                return float4(result, result, result, 1.0);
            }
            ENDHLSL
        }

        // Pass 2: Vertical Blur
        Pass {
            Name "SSAO Vertical Blur"
            Blend Off

            HLSLPROGRAM
            #pragma vertex SSAOVertex
            #pragma fragment VerticalBlurFragment

            #include "ShaderLibrary/Common.hlsl"

            float4 _SSAOTexelSize;

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float2 screenUV : TEXCOORD0;
            };

            struct VSInput {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings SSAOVertex(VSInput i) {
                Varyings output;
                output.positionCS = float4(i.positionOS.xy, 0.0, 1.0);
                output.screenUV = i.uv;
                if (_ProjectionParams.x < 0.0)
                    output.screenUV.y = 1.0 - output.screenUV.y;
                return output;
            }

            TEXTURE2D(_SSAOSource);

            float4 VerticalBlurFragment(Varyings input) : SV_Target {
                float2 uv = input.screenUV;
                float2 delta = float2(0.0, _SSAOTexelSize.y);

                float result = 0;
                result += _SSAOSource.SampleLevel(sampler_linear_clamp, uv, 0).r * 0.227027;
                result += _SSAOSource.SampleLevel(sampler_linear_clamp, uv + delta * (-1.384615), 0).r * 0.316216;
                result += _SSAOSource.SampleLevel(sampler_linear_clamp, uv + delta * (1.384615), 0).r * 0.316216;
                result += _SSAOSource.SampleLevel(sampler_linear_clamp, uv + delta * (-3.230769), 0).r * 0.070270;
                result += _SSAOSource.SampleLevel(sampler_linear_clamp, uv + delta * (3.230769), 0).r * 0.070270;
                result /= (0.227027 + 0.316216 + 0.316216 + 0.070270 + 0.070270);

                return float4(result, result, result, 1.0);
            }
            ENDHLSL
        }
    }
}
