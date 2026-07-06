#ifndef TAOTIE_LIGHTING_INCLUDED
#define TAOTIE_LIGHTING_INCLUDED

float3 IncomingLight (Surface surface, Light light) {
    return saturate(dot(surface.normal, light.direction) * light.attenuation ) * light.color;
}

float3 GetLighting (Surface surface,BRDF brdf, Light light) {
    return IncomingLight(surface, light) * DirectBRDF(surface, brdf, light);
}

bool RenderingLayersOverlap (Surface surface, Light light) {
#if defined(SHADER_API_GLES)
    return true;
#else
    return (surface.renderingLayerMask & light.renderingLayerMask) != 0;
#endif
}

float3 GetLighting (Fragment fragment,Surface surfaceWS, BRDF brdf, GI gi) {
    ShadowData shadowData = GetShadowData(surfaceWS);
    shadowData.shadowMask = gi.shadowMask;
    float3 color = IndirectBRDF(surfaceWS, brdf, gi.diffuse, gi.specular);
    int dirLightCount = GetDirectionalLightCount();
    #if defined(SHADER_API_GLES)
    // GLES2/WebGL1: loops must have constant bounds
    for (int i = 0; i < MAX_DIRECTIONAL_LIGHT_COUNT; i++) {
        if (i < dirLightCount) {
            Light light = GetDirectionalLight(i, surfaceWS, shadowData);
            if (RenderingLayersOverlap(surfaceWS, light)) {
                color += GetLighting(surfaceWS, brdf, light);
            }
        }
    }
    #else
    for (int i = 0; i < dirLightCount; i++) {
        Light light = GetDirectionalLight(i, surfaceWS, shadowData);
        if (RenderingLayersOverlap(surfaceWS, light)) {
            color += GetLighting(surfaceWS, brdf, light);
        }
    }
    #endif
    
    // In deferred lighting pass, iterate all other lights directly (no tile culling).
    // In forward pass, use Forward+ tiles when available, else iterate all (capped) lights.
    #if defined(TAOTIE_DEFERRED_LIGHTING)
    {
        int otherLightCount = GetOtherLightCount();
        [loop]
        for (int j = 0; j < otherLightCount; j++)
        {
            Light light = GetOtherLight(j, surfaceWS, shadowData);
            if (RenderingLayersOverlap(surfaceWS, light))
            {
                color += GetLighting(surfaceWS, brdf, light);
            }
        }
    }
    #elif defined(TAOTIE_FORWARD_PLUS)
    ForwardPlusTile tile = GetForwardPlusTile(fragment.screenUV);
    int lastLightIndex = tile.GetLastLightIndexInTile();
    for (int j = tile.GetFirstLightIndexInTile(); j <= lastLightIndex; j++)
    {
        Light light = GetOtherLight(
            tile.GetLightIndex(j), surfaceWS, shadowData);
        if (RenderingLayersOverlap(surfaceWS, light))
        {
            color += GetLighting(surfaceWS, brdf, light);
        }
    }
    #else
    int otherLightCount = GetOtherLightCount();
    #if defined(SHADER_API_GLES)
    // GLES2/WebGL1: loops must have constant bounds
    for (int j = 0; j < MAX_OTHER_LIGHT_COUNT; j++) {
        if (j < otherLightCount) {
            Light light = GetOtherLight(j, surfaceWS, shadowData);
            if (RenderingLayersOverlap(surfaceWS, light)) {
                color += GetLighting(surfaceWS, brdf, light);
            }
        }
    }
    #else
    [loop]
    for (int j = 0; j < otherLightCount; j++)
    {
        Light light = GetOtherLight(j, surfaceWS, shadowData);
        if (RenderingLayersOverlap(surfaceWS, light))
        {
            color += GetLighting(surfaceWS, brdf, light);
        }
    }
    #endif
    #endif
    return color;
}

#endif