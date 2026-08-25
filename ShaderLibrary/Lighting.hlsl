#ifndef TAOTIE_LIGHTING_INCLUDED
#define TAOTIE_LIGHTING_INCLUDED

float3 IncomingLight (Surface surface, Light light) {
    return saturate(dot(surface.normal, light.direction) * light.attenuation ) * light.color;
}

float3 GetLighting (Surface surface,BRDF brdf, Light light) {
    return IncomingLight(surface, light) * DirectBRDF(surface, brdf, light);
}

// Vertex lighting: simplified per-vertex diffuse accumulation (no shadows/cookies)
// Iterates shared _OtherLight arrays from _OtherLightCount to _VertexLightCount
float3 GetVertexLighting (float3 positionWS, float3 normalWS) {
    int start = (int)_OtherLightCount;
    int end = (int)_VertexLightCount;
    float3 color = 0.0;
    #if defined(SHADER_API_GLES)
    [loop]
    for (int i = 0; i < MAX_OTHER_LIGHT_COUNT; i++) {
        if (i >= end) break;
        if (i < start) continue;
        VertexLight vl = GetVertexOtherLight(i, positionWS);
        color += saturate(dot(normalWS, vl.direction)) * vl.attenuation * vl.color;
    }
    #else
    [loop]
    for (int i = start; i < end; i++) {
        VertexLight vl = GetVertexOtherLight(i, positionWS);
        color += saturate(dot(normalWS, vl.direction)) * vl.attenuation * vl.color;
    }
    #endif
    return color;
}

bool RenderingLayersOverlap (Surface surface, Light light)
{
    #if defined(SHADER_API_GLES)
    return true;
    #else
    // 0x00FFFFFF sentinel: C# sends this when renderingLayerMask == 0x7FFFFFFF (Everything)
    // to avoid float overflow. Treat as all-layers-match.
    if (light.renderingLayerMask == 0x00FFFFFF)
        return true;
    return (surface.renderingLayerMask & light.renderingLayerMask) != 0;
    #endif
}

// --- LIGHT_LOOP_BEGIN / LIGHT_LOOP_END macros ---
// Abstract the Other Light iteration so GetLighting stays clean.
// Forward+ path: bitmask + ZBin tile culling.
// Plain Forward / Deferred without Forward+: iterate all (capped) lights.
#if defined(TAOTIE_FORWARD_PLUS)

    #if !defined(SHADER_API_GLES) && !defined(SHADER_API_GLES3) && !defined(SHADER_API_GLCORE)
        // Non-GLES: firstbitlow to iterate only set bits (SM5.0+)
        #define LIGHT_LOOP_BEGIN(fragment, surfaceWS, brdf, shadowData, color) \
        { \
            ForwardPlusTile tile = GetForwardPlusTile(fragment.screenUV); \
            int wpt = tile.GetWordsPerTile(); \
            int zBinIdx = GetZBinIndex(fragment.depth); \
            [loop] \
            for (int w = 0; w < wpt; w++) \
            { \
                uint tileMask = tile.GetBitmaskWord(w); \
                uint zBinMask = LoadZBinBitmaskWord(zBinIdx, w); \
                uint mask = tileMask & zBinMask; \
                uint remaining = mask; \
                [loop] \
                while (remaining) \
                { \
                    int bit = firstbitlow(remaining); \
                    remaining &= remaining - 1; \
                    int lightIdx = w * 32 + bit; \
                    Light light = GetOtherLight(lightIdx, surfaceWS, shadowData); \
                    if (RenderingLayersOverlap(surfaceWS, light)) \
                        color += GetLighting(surfaceWS, brdf, light);

        #define LIGHT_LOOP_END \
                } \
            } \
        }
    #else
        // GLES3/WebGL2/GLCore: 32-bit for loop (firstbitlow not guaranteed)
        #define LIGHT_LOOP_BEGIN(fragment, surfaceWS, brdf, shadowData, color) \
        { \
            ForwardPlusTile tile = GetForwardPlusTile(fragment.screenUV); \
            int wpt = tile.GetWordsPerTile(); \
            int zBinIdx = GetZBinIndex(fragment.depth); \
            [loop] \
            for (int w = 0; w < wpt; w++) \
            { \
                uint tileMask = tile.GetBitmaskWord(w); \
                uint zBinMask = LoadZBinBitmaskWord(zBinIdx, w); \
                uint mask = tileMask & zBinMask; \
                [loop] \
                for (int bit = 0; bit < 32; bit++) \
                { \
                    if (mask & (1u << bit)) \
                    { \
                        int lightIdx = w * 32 + bit; \
                        Light light = GetOtherLight(lightIdx, surfaceWS, shadowData); \
                        if (RenderingLayersOverlap(surfaceWS, light)) \
                            color += GetLighting(surfaceWS, brdf, light);

        #define LIGHT_LOOP_END \
                    } \
                } \
            } \
        }
    #endif

#elif defined(SHADER_API_GLES)

    #define LIGHT_LOOP_BEGIN(fragment, surfaceWS, brdf, shadowData, color) \
    { \
        int otherLightCount = GetOtherLightCount(); \
        for (int j = 0; j < MAX_OTHER_LIGHT_COUNT; j++) { \
            if (j < otherLightCount) { \
                Light light = GetOtherLight(j, surfaceWS, shadowData); \
                if (RenderingLayersOverlap(surfaceWS, light)) \
                    color += GetLighting(surfaceWS, brdf, light);

    #define LIGHT_LOOP_END \
            } \
        } \
    }

#else

    #define LIGHT_LOOP_BEGIN(fragment, surfaceWS, brdf, shadowData, color) \
    { \
        int otherLightCount = GetOtherLightCount(); \
        [loop] \
        for (int j = 0; j < otherLightCount; j++) { \
            Light light = GetOtherLight(j, surfaceWS, shadowData); \
            if (RenderingLayersOverlap(surfaceWS, light)) \
                color += GetLighting(surfaceWS, brdf, light);

    #define LIGHT_LOOP_END \
        } \
    }

#endif

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

    LIGHT_LOOP_BEGIN(fragment, surfaceWS, brdf, shadowData, color)
    LIGHT_LOOP_END

    return color;
}

#endif
