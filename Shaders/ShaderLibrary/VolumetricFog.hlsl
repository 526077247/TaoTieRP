#ifndef TAOTIE_VOLUMETRIC_FOG_INCLUDED
#define TAOTIE_VOLUMETRIC_FOG_INCLUDED

// Adapted from https://github.com/sinnwrig/URP-Fog-Volumes (MIT)
// Enhanced for TaoTie RP: per-light shadow sampling, Beer-Lambert extinction,
// height-based density variation, real light direction Mie scattering.

#include "Common.hlsl"
#include "Surface.hlsl"
#include "Shadows.hlsl"
#include "Light.hlsl"

TEXTURE2D(_VFogSource);

int _VFogSampleCount;
float4 _VFogStepParams;   // x: min step, y: max step, z: step increment factor, w: max ray length
float _VFogJitter;
float _VFogScattering;
float _VFogExtinction;
float _VFogMieG;
float _VFogDensity;
float4 _VFogColor;         // rgb = fog albedo, a = density multiplier
float _VFogMaxDistance;
float _VFogBaseHeight;
float _VFogHeightFalloff;

struct VFogVaryings
{
    float4 positionCS : SV_POSITION;
    float2 screenUV : TEXCOORD0;
};

VFogVaryings VFogPassVertex(float3 positionOS : POSITION, float2 uv : TEXCOORD0)
{
    VFogVaryings output;
    output.positionCS = float4(positionOS.xy, 0.0, 1.0);
    output.screenUV = uv;
    if (_ProjectionParams.x < 0.0)
        output.screenUV.y = 1.0 - output.screenUV.y;
    return output;
}

float sqr(float v) { return v * v; }

// Mie phase function �?from https://github.com/SlightlyMad/VolumetricLights (BSD)
float MiePhase(float cosAngle, float mieG)
{
    float gSqr = sqr(mieG);
    return 0.07957747154 * ((1.0 - gSqr) / pow(abs((1.0 + gSqr) - (2.0 * mieG) * cosAngle), 1.5));
}

// Reconstruct world-space view direction from screen UV
float3 GetViewDir(float2 uv)
{
    float3 viewVector = mul(unity_CameraInvProjection, float4(uv * 2.0 - 1.0, 0.0, -1.0)).xyz;
    viewVector = mul(unity_MatrixInvV, float4(viewVector, 0.0)).xyz;
    return normalize(viewVector);
}

// Hash for jitter
float MathRand(float2 uv)
{
    return frac(sin(dot(uv, float2(12.9898, 78.233))) * 43758.5453);
}

half4 RayMarch(float3 rayStart, float3 rayDir, float rayLength, float2 uv)
{
    float stepSize = _VFogStepParams.x;
    float transmittance = 1.0;          // Beer-Lambert accumulated transmittance
    half3 inScatteredLight = 0.0;      // Accumulated in-scattered radiance

    // Jitter the start position to reduce banding
    float jitter = MathRand(uv) * _VFogJitter;
    float distance = jitter;

    half3 fogAlbedo = _VFogColor.rgb;
    float baseDensity = _VFogDensity * _VFogColor.a;
    int dirLightCount = GetDirectionalLightCount();

    [loop]
    for (int i = 0; i < _VFogSampleCount; ++i)
    {
        if (distance >= rayLength)
            break;

        float3 worldPos = rayStart + rayDir * distance;

        // Height-based density variation
        float heightDensity = 1.0;
        if (_VFogHeightFalloff > 0.0)
        {
            heightDensity = exp(-max(0.0, worldPos.y - _VFogBaseHeight) * _VFogHeightFalloff);
        }
        float localDensity = baseDensity * heightDensity;

        // Beer-Lambert extinction: update transmittance
        float extinctionStep = _VFogExtinction * stepSize * localDensity;
        transmittance *= exp(-extinctionStep);

        // Build Surface for shadow sampling at this point in space
        Surface surface = (Surface)0;
        surface.position = worldPos;
        surface.interpolatedNormal = float3(0.0, 1.0, 0.0);
        surface.depth = -TransformWorldToView(worldPos).z;
        surface.dither = InterleavedGradientNoise(uv * 64.0 + distance, 0);
        #ifndef SHADER_API_GLES
        surface.renderingLayerMask = ~0u;
        #endif
        surface.receiveShadows = true;

        ShadowData shadowData = GetShadowData(surface);

        // Accumulate in-scattering from all directional lights
        half3 lightContribution = 0.0;
        [loop]
        for (int j = 0; j < dirLightCount; j++)
        {
            DirectionalShadowData dirShadow =
                GetDirectionalShadowData(j, shadowData);
            float shadowAttenuation =
                GetDirectionalShadowAttenuation(dirShadow, shadowData, surface);

            float3 lightDir = _DirectionalLightDirectionsAndMasks[j].xyz;
            float3 lightColor = _DirectionalLightColors[j].rgb;

            // Mie phase using real light direction
            float cosAngle = dot(rayDir, -lightDir);
            half phase = MiePhase(cosAngle, _VFogMieG);

            lightContribution += half3(lightColor * shadowAttenuation) * phase;
        }

        // In-scattering: T * sigma_scat * density * stepSize * Sigma(light * shadow * phase)
        float scatteringStep = _VFogScattering * stepSize * localDensity;
        inScatteredLight += transmittance * scatteringStep * lightContribution * fogAlbedo;

        // Exponential step size
        distance += stepSize;
        stepSize = min(_VFogStepParams.y, stepSize * _VFogStepParams.z);
    }

    float opacity = 1.0 - transmittance;
    return half4(inScatteredLight, opacity);
}

half4 CalculateVolumetricFog(float3 cameraPos, float3 viewDir, float linearDepth, float2 uv)
{
    float rayLength = min(linearDepth, _VFogMaxDistance);

    if (rayLength <= 0.0)
        return 0.0;

    float3 rayStart = cameraPos;

    return RayMarch(rayStart, viewDir, rayLength, uv);
}

half4 VolumetricFogFragment(VFogVaryings input) : SV_Target
{
    float2 uv = input.screenUV;

    float4 source = SAMPLE_TEXTURE2D(_VFogSource, sampler_linear_clamp, uv);

    float rawDepth = SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, sampler_point_clamp, uv, 0);
    #if UNITY_REVERSED_Z
        if (rawDepth >= 1.0) return source;
    #else
        if (rawDepth <= 0.0) return source;
    #endif

    float3 viewDir = GetViewDir(uv);
    float linearDepth = LinearEyeDepth(rawDepth, _ZBufferParams);

    half4 fog = CalculateVolumetricFog(_WorldSpaceCameraPos, viewDir, linearDepth, uv);

    // Beer-Lambert compositing: L_final = L_background * T + L_inscattered
    float3 result = source.rgb * (1.0 - fog.a) + fog.rgb;
    return float4(result, source.a);
}

#endif // TAOTIE_VOLUMETRIC_FOG_INCLUDED
