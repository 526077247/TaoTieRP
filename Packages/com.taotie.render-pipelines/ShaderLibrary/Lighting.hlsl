#ifndef TAOTIE_LIGHTING_INCLUDED
#define TAOTIE_LIGHTING_INCLUDED

float3 IncomingLight (Surface surface, Light light) {
    return saturate(dot(surface.normal, light.direction) * light.attenuation ) * light.color;
}

float3 GetLighting (Surface surface,BRDF brdf, Light light) {
    return IncomingLight(surface, light) * DirectBRDF(surface, brdf, light);
}

bool RenderingLayersOverlap (Surface surface, Light light) {
    return (surface.renderingLayerMask & light.renderingLayerMask) != 0;
}

float3 GetLighting (Fragment fragment,Surface surfaceWS, BRDF brdf, GI gi) {
    ShadowData shadowData = GetShadowData(surfaceWS);
    shadowData.shadowMask = gi.shadowMask;
    float3 color = IndirectBRDF(surfaceWS, brdf, gi.diffuse, gi.specular);
    for (int i = 0; i < GetDirectionalLightCount(); i++) {
        Light light = GetDirectionalLight(i, surfaceWS, shadowData);
        if (RenderingLayersOverlap(surfaceWS, light)) {
            color += GetLighting(surfaceWS, brdf, light);
        }
    }
    
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
    return color;
}

#endif