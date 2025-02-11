#ifndef TAOTIE_LIGHTING_INCLUDED
#define TAOTIE_LIGHTING_INCLUDED

float3 IncomingLight (Surface surface, Light light) {
    return saturate(dot(surface.normal, light.direction) * light.attenuation ) * light.color;
}

float3 GetLighting (Surface surface,BRDF brdf, Light light) {
    return IncomingLight(surface, light) * DirectBRDF(surface, brdf, light);
}

float2 GetRampUV(float shadow, float RampMap)
{
    float rampU = shadow;
    float rampV = RampMap * 0.45;
    #ifdef SHADOW_FACE_ON
    rampV = 0.1;
    #endif
    return float2(rampU, rampV);
}

float3 IncomingDirLight (Surface surface, Light light) {
    half3 lightColor = light.color * light.attenuation;
    float shadow = 0.0;
    #if !defined(_SHADOW_FACE_ON)
    float lambert = ceil(dot(surface.normal, light.direction)) * 0.5 + 0.5;
    shadow = saturate(lambert  + surface.lightMap.r);
    #if defined(_RAMP_MAP)
    float2 rampUV = GetRampUV(shadow, surface.lightMap.a);
    half3 rampColor = SAMPLE_TEXTURE2D(_RampMap, sampler_RampMap, rampUV);
    lightColor = (rampColor + lightColor) * 0.5;
    #endif
    shadow = shadow * smoothstep(-0.1, 0.2, surface.lightMap.g);
    float noAOMask = step(0.9, surface.lightMap.g);
    shadow = lerp(shadow, 1.0, noAOMask);
    #else
    float2 Front = normalize(TransformObjectToWorldDir(float3(0.0, 1.0, 0.0)).xz);
    float2 Left = normalize(TransformObjectToWorldDir(float3(0.0, 0.0, 1.0)).xz);
    float ctrl = 1.0 - saturate(dot(Front, light.direction.xz) * 0.5 + 0.5);
    float ilm = dot(light.direction.xz, Left) > 0 ? surface.faceShadow.x : surface.faceShadow.y;
    shadow = ceil(saturate(abs(ctrl - ilm))) * 0.5 + 0.5;
    lightColor = (surface.color + lightColor) * 0.5;
    #endif
    return shadow * lightColor;
}


float3 GetDirLighting (Surface surface,BRDF brdf, Light light) {
    return IncomingDirLight(surface, light) * DirectBRDF(surface, brdf, light);
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
            color += GetDirLighting(surfaceWS, brdf, light);
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