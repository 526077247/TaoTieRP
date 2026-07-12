#ifndef TAOTIE_SURFACE_INCLUDED
#define TAOTIE_SURFACE_INCLUDED

struct Surface {
    float3 position;
    float3 normal;
    float3 interpolatedNormal;
    float3 viewDirection;
    float depth;
    float3 color;
    float alpha;
    float metallic;
    float occlusion;
    float smoothness;
    float fresnelStrength;
    float dither;
    #if defined(SHADER_API_GLES)
    float renderingLayerMask;
    #else
    uint renderingLayerMask;
    #endif
    bool receiveShadows;
};

#endif