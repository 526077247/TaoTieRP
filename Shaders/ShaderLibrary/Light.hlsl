#ifndef TAOTIE_LIGHT_INCLUDED
#define TAOTIE_LIGHT_INCLUDED

#define MAX_DIRECTIONAL_LIGHT_COUNT 4
#if defined(SHADER_API_GLES)
    #define MAX_OTHER_LIGHT_COUNT 8
#elif defined(SHADER_API_GLES3)
	#define MAX_OTHER_LIGHT_COUNT 32
#else
    #define MAX_OTHER_LIGHT_COUNT 256
#endif

#include "Cookies.hlsl"

// GLES2/GLES3: CBUFFER arrays not supported or cause performance regression.
#if !defined(SHADER_API_GLES) && !defined(SHADER_API_GLES3)
CBUFFER_START(_CustomLight)
#endif
	float _DirectionalLightCount;
	float4 _DirectionalLightColors[MAX_DIRECTIONAL_LIGHT_COUNT];
	float4 _DirectionalLightDirectionsAndMasks[MAX_DIRECTIONAL_LIGHT_COUNT];
	float4 _DirectionalLightShadowData[MAX_DIRECTIONAL_LIGHT_COUNT];

	float _OtherLightCount;
	float _VertexLightCount;
	float4 _OtherLightColors[MAX_OTHER_LIGHT_COUNT];
	float4 _OtherLightPositions[MAX_OTHER_LIGHT_COUNT];
	float4 _OtherLightDirectionsAndMasks[MAX_OTHER_LIGHT_COUNT];
	float4 _OtherLightSpotAngles[MAX_OTHER_LIGHT_COUNT];
	float4 _OtherLightShadowData[MAX_OTHER_LIGHT_COUNT];
#if !defined(SHADER_API_GLES) && !defined(SHADER_API_GLES3)
CBUFFER_END
#endif

struct VertexLight {
	float3 color;
	float3 direction;
	float attenuation;
};

// Reads from the shared _OtherLight arrays (indices _OtherLightCount.._VertexLightCount-1)
VertexLight GetVertexOtherLight (int index, float3 positionWS) {
	VertexLight light;
	light.color = _OtherLightColors[index].rgb;
	float3 lightPos = _OtherLightPositions[index].xyz;
	float3 ray = lightPos - positionWS;
	light.direction = normalize(ray);
	float distanceSqr = max(dot(ray, ray), 0.00001);
	float rangeAttenuation = Square(
		saturate(1.0 - Square(distanceSqr * _OtherLightPositions[index].w))
	);
	float4 spotAngles = _OtherLightSpotAngles[index];
	float3 spotDirection = _OtherLightDirectionsAndMasks[index].xyz;
	float spotAttenuation = Square(
		saturate(dot(spotDirection, light.direction) * spotAngles.x + spotAngles.y)
	);
	light.attenuation = spotAttenuation * rangeAttenuation / distanceSqr;
	return light;
}

struct Light {
	float3 color;
	float3 direction;
	float attenuation;
	#ifndef SHADER_API_GLES
	uint renderingLayerMask;
	#endif
};

int GetDirectionalLightCount () {
	return (int)_DirectionalLightCount;
}

DirectionalShadowData GetDirectionalShadowData (
	int lightIndex, ShadowData shadowData
) {
	DirectionalShadowData data;
	data.strength = _DirectionalLightShadowData[lightIndex].x;
	data.tileIndex =
		_DirectionalLightShadowData[lightIndex].y + shadowData.cascadeIndex;
	data.normalBias = _DirectionalLightShadowData[lightIndex].z;
	data.shadowMaskChannel = _DirectionalLightShadowData[lightIndex].w;
	return data;
}

Light GetDirectionalLight (int index, Surface surfaceWS, ShadowData shadowData) {
	Light light;
	light.color = _DirectionalLightColors[index].rgb;
	#if !defined(SHADER_API_GLES) && !defined(SHADER_API_GLES3) && !defined(SHADER_API_GLCORE)
		UNITY_BRANCH
		if (_DirLightCookieEnabled[index] > 0.5)
			light.color *= SampleDirectionalCookie(index, surfaceWS.position);
	#endif
	light.direction = _DirectionalLightDirectionsAndMasks[index].xyz;
	#ifndef SHADER_API_GLES
	light.renderingLayerMask = (uint)_DirectionalLightDirectionsAndMasks[index].w;
	#endif
	DirectionalShadowData dirShadowData =
		GetDirectionalShadowData(index, shadowData);
	light.attenuation =
		GetDirectionalShadowAttenuation(dirShadowData, shadowData, surfaceWS);
	return light;
}

int GetOtherLightCount () {
	return (int)_OtherLightCount;
}

OtherShadowData GetOtherShadowData (int lightIndex) {
	OtherShadowData data;
	float4 sd = _OtherLightShadowData[lightIndex];
	data.strength = sd.x;
	data.tileIndex = sd.y;
	data.shadowMaskChannel = sd.w;
	data.isPoint = sd.z == 1.0;
	data.lightPositionWS = 0.0;
	data.lightDirectionWS = 0.0;
	data.spotDirectionWS = 0.0;
	return data;
}

Light GetOtherLight (int index, Surface surfaceWS, ShadowData shadowData) {
	Light light;
	light.color = _OtherLightColors[index].rgb;
	float3 position = _OtherLightPositions[index].xyz;
	float3 ray = position - surfaceWS.position;
	light.direction = normalize(ray);
	float distanceSqr = max(dot(ray, ray), 0.00001);
	float rangeAttenuation = Square(
		saturate(1.0 - Square(distanceSqr * _OtherLightPositions[index].w))
	);
	float4 spotAngles = _OtherLightSpotAngles[index];
	float3 spotDirection = _OtherLightDirectionsAndMasks[index].xyz;
	#ifndef SHADER_API_GLES
	light.renderingLayerMask = (uint)_OtherLightDirectionsAndMasks[index].w;
	#endif
	float spotAttenuation = Square(
		saturate(dot(spotDirection, light.direction) *
		spotAngles.x + spotAngles.y)
	);
	// Apply cookie for spot lights (works in all paths including Forward+)
	#if !defined(SHADER_API_GLES) && !defined(SHADER_API_GLES3) && !defined(SHADER_API_GLCORE)
		UNITY_BRANCH
		if (spotAngles.x != 0.0 && index < 8 && _OtherLightCookieEnabled[index] > 0.5)
			light.color *= SampleSpotCookie(index, surfaceWS.position);
	#else
	if (spotAngles.x != 0.0)
		light.color *= SampleSpotCookie(index, surfaceWS.position);
	#endif
	OtherShadowData otherShadowData = GetOtherShadowData(index);
	otherShadowData.lightPositionWS = position;
	otherShadowData.lightDirectionWS = light.direction;
	otherShadowData.spotDirectionWS = spotDirection;
	light.attenuation =
		GetOtherShadowAttenuation(otherShadowData, shadowData, surfaceWS) *
		spotAttenuation * rangeAttenuation / distanceSqr;
	return light;
}


#endif
