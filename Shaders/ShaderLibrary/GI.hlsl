#ifndef TAOTIE_GI_INCLUDED
#define TAOTIE_GI_INCLUDED

#if defined(LIGHTMAP_ON)
    #define GI_ATTRIBUTE_DATA float2 lightMapUV : TEXCOORD1;
    #define GI_VARYINGS_DATA float2 lightMapUV : VAR_LIGHT_MAP_UV;
    #define TRANSFER_GI_DATA(input, output) \
    output.lightMapUV = input.lightMapUV * \
    unity_LightmapST.xy + unity_LightmapST.zw;
    #define GI_FRAGMENT_DATA(input) input.lightMapUV
#else
    #define GI_ATTRIBUTE_DATA
    #define GI_VARYINGS_DATA
    #define TRANSFER_GI_DATA(input, output)
    #define GI_FRAGMENT_DATA(input) 0.0
#endif

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ImageBasedLighting.hlsl"

TEXTURE2D(unity_Lightmap);
SAMPLER(samplerunity_Lightmap);

TEXTURE2D(unity_ShadowMask);
SAMPLER(samplerunity_ShadowMask);

TEXTURE3D_FLOAT(unity_ProbeVolumeSH);
SAMPLER(samplerunity_ProbeVolumeSH);

// unity_SpecCube0/1 declared in UnityInput.hlsl

struct GI {
    float3 diffuse;
    float3 specular;
    ShadowMask shadowMask;
};

float3 SampleLightMap (float2 lightMapUV) {
    #if defined(LIGHTMAP_ON)
    return SampleSingleLightmap(
            TEXTURE2D_ARGS(unity_Lightmap, samplerunity_Lightmap), lightMapUV,
            float4(1.0, 1.0, 0.0, 0.0),
            #if defined(UNITY_LIGHTMAP_FULL_HDR)
                false,
            #else
                true,
            #endif
            float4(LIGHTMAP_HDR_MULTIPLIER, LIGHTMAP_HDR_EXPONENT, 0.0, 0.0)
        );
    #else
    return 0.0;
    #endif
}

float3 SampleLightProbe (Surface surfaceWS) {
    #if defined(LIGHTMAP_ON)
    return 0.0;
    #else
    #if !defined(SHADER_API_GLES) || defined(SHADER_API_GLES3)
    if (unity_ProbeVolumeParams.x) {
        return SampleProbeVolumeSH4(
            TEXTURE3D_ARGS(unity_ProbeVolumeSH, samplerunity_ProbeVolumeSH),
            surfaceWS.position, surfaceWS.normal,
            unity_ProbeVolumeWorldToObject,
            unity_ProbeVolumeParams.y, unity_ProbeVolumeParams.z,
            unity_ProbeVolumeMin.xyz, unity_ProbeVolumeSizeInv.xyz
        );
    }
    else
    #endif
    {
        float4 coefficients[7];
        coefficients[0] = unity_SHAr;
        coefficients[1] = unity_SHAg;
        coefficients[2] = unity_SHAb;
        coefficients[3] = unity_SHBr;
        coefficients[4] = unity_SHBg;
        coefficients[5] = unity_SHBb;
        coefficients[6] = unity_SHC;
        return max(0.0, SampleSH9(coefficients, surfaceWS.normal));
    }
    #endif
}

float4 SampleBakedShadows (float2 lightMapUV, Surface surfaceWS) {
    #if defined(LIGHTMAP_ON)
    return SAMPLE_TEXTURE2D(
        unity_ShadowMask, samplerunity_ShadowMask, lightMapUV
    );
    #else
    #if !defined(SHADER_API_GLES) || defined(SHADER_API_GLES3)
    if (unity_ProbeVolumeParams.x) {
        return SampleProbeOcclusion(
            TEXTURE3D_ARGS(unity_ProbeVolumeSH, samplerunity_ProbeVolumeSH),
            surfaceWS.position, unity_ProbeVolumeWorldToObject,
            unity_ProbeVolumeParams.y, unity_ProbeVolumeParams.z,
            unity_ProbeVolumeMin.xyz, unity_ProbeVolumeSizeInv.xyz
        );
    }
    else
    #endif
    {
        return unity_ProbesOcclusion;
    }
    #endif
}

// Box projection: reflect the sampling direction off the probe box boundaries
// so reflections appear to come from the correct world-space position.
float3 BoxProjection(float3 direction, float3 position,
    float4 boxMin, float4 boxMax)
{
    // boxMin/boxMax: xyz = min/max bounds in probe-local space, w = unused
    // Only apply if the box has valid bounds (max > min on any axis)
    float3 boxMinXYZ = boxMin.xyz;
    float3 boxMaxXYZ = boxMax.xyz;

    // If bounds are all zero, skip box projection (infinite probe)
    if (all(boxMaxXYZ == 0.0) && all(boxMinXYZ == 0.0))
        return direction;

    float3 factors = ((0.5 * (boxMaxXYZ + boxMinXYZ)) - position) / direction;
    float3 center = 0.5 * (boxMaxXYZ + boxMinXYZ);

    // Find the nearest intersection plane
    float3 t = abs(factors);
    float scalar = min(min(t.x, t.y), t.z);

    // Project: new direction points from the box intersection to the probe center
    float3 projected = (position + direction * scalar) - center;
    return projected;
}

float3 SampleProbe(TEXTURECUBE_PARAM(tex, sampTex), float3 uvw, float mip,
    float4 hdrDecode)
{
    float4 environment = SAMPLE_TEXTURECUBE_LOD(tex, sampTex, uvw, mip);
    return DecodeHDREnvironment(environment, hdrDecode);
}

float3 SampleEnvironment (Surface surfaceWS, BRDF brdf) {
    float3 reflectDir = reflect(-surfaceWS.viewDirection, surfaceWS.normal);
    float mip = PerceptualRoughnessToMipmapLevel(brdf.perceptualRoughness);

    // Box-projected reflection direction for probe 0
    float3 uvw0 = BoxProjection(reflectDir, surfaceWS.position,
        unity_SpecCube0_BoxMin, unity_SpecCube0_BoxMax);

    float3 env0 = SampleProbe(
        TEXTURECUBE_ARGS(unity_SpecCube0, samplerunity_SpecCube0),
        uvw0, mip, unity_SpecCube0_HDR);

    // Blend weight from unity_SpecCube1_HDR.a (engine sets this for probe blending)
    // When .a is 0 or probe 1 has no valid data, only probe 0 is used.
    float blendWeight = unity_SpecCube1_HDR.a;

    if (blendWeight > 0.001)
    {
        // Box-projected reflection direction for probe 1
        float3 uvw1 = BoxProjection(reflectDir, surfaceWS.position,
            unity_SpecCube1_BoxMin, unity_SpecCube1_BoxMax);

        float3 env1 = SampleProbe(
            TEXTURECUBE_ARGS(unity_SpecCube1, samplerunity_SpecCube1),
            uvw1, mip, unity_SpecCube1_HDR);

        env0 = lerp(env0, env1, blendWeight);
    }

    return env0;
}

GI GetGI (float2 lightMapUV, Surface surfaceWS, BRDF brdf) {
    GI gi;
    gi.diffuse = SampleLightMap(lightMapUV)  + SampleLightProbe(surfaceWS);
    gi.specular = SampleEnvironment(surfaceWS, brdf);
    gi.shadowMask.always = false;
    gi.shadowMask.distance = false;
    gi.shadowMask.shadows = 1.0;

    // _ShadowMaskMode: 0 = off, 1 = Shadowmask (always), 2 = Distance Shadowmask
    // Deferred fullscreen pass has no lightmap UVs — skip baked shadow sampling.
    #if !defined(TAOTIE_DEFERRED_LIGHTING)
    if (_ShadowMaskMode > 0.5) {
        gi.shadowMask.shadows = SampleBakedShadows(lightMapUV, surfaceWS);
        if (_ShadowMaskMode > 1.5) {
            gi.shadowMask.distance = true;
        } else {
            gi.shadowMask.always = true;
        }
    }
    #endif
    return gi;
}
#endif