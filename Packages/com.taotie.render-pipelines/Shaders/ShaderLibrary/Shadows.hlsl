#ifndef TAOTIE_SHADOWS_INCLUDED
#define TAOTIE_SHADOWS_INCLUDED
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Shadow/ShadowSamplingTent.hlsl"

#if defined(_SHADOW_FILTER_HIGH)
	#define DIRECTIONAL_FILTER_SAMPLES 16
	#define DIRECTIONAL_FILTER_SETUP SampleShadow_ComputeSamples_Tent_7x7
	#define OTHER_FILTER_SAMPLES 16
	#define OTHER_FILTER_SETUP SampleShadow_ComputeSamples_Tent_7x7
#elif defined(_SHADOW_FILTER_MEDIUM)
	#define DIRECTIONAL_FILTER_SAMPLES 9
	#define DIRECTIONAL_FILTER_SETUP SampleShadow_ComputeSamples_Tent_5x5
	#define OTHER_FILTER_SAMPLES 9
	#define OTHER_FILTER_SETUP SampleShadow_ComputeSamples_Tent_5x5
#else
	#define DIRECTIONAL_FILTER_SAMPLES 4
	#define DIRECTIONAL_FILTER_SETUP SampleShadow_ComputeSamples_Tent_3x3
	#define OTHER_FILTER_SAMPLES 4
	#define OTHER_FILTER_SETUP SampleShadow_ComputeSamples_Tent_3x3
#endif

#define MAX_SHADOWED_DIRECTIONAL_LIGHT_COUNT 4
#define MAX_SHADOWED_OTHER_LIGHT_COUNT 16
#define MAX_CASCADE_COUNT 4

TEXTURE2D_SHADOW(_DirectionalShadowAtlas);
TEXTURE2D_SHADOW(_OtherShadowAtlas);
#define SHADOW_SAMPLER sampler_linear_clamp_compare
SAMPLER_CMP(SHADOW_SAMPLER);

CBUFFER_START(_CustomShadows)
	int _CascadeCount;
	float4 _CascadeCullingSpheres[MAX_CASCADE_COUNT];
	float4 _CascadeData[MAX_CASCADE_COUNT];
	float4x4 _DirectionalShadowMatrices
		[MAX_SHADOWED_DIRECTIONAL_LIGHT_COUNT * MAX_CASCADE_COUNT];
	float4x4 _OtherShadowMatrices[MAX_SHADOWED_OTHER_LIGHT_COUNT];
	float4 _OtherShadowTiles[MAX_SHADOWED_OTHER_LIGHT_COUNT];
	float4 _ShadowAtlasSize;
	float4 _ShadowDistanceFade;
	float _SoftCascadeBlend;
	float _ShadowMaskMode;
CBUFFER_END

struct ShadowMask {
	bool always;
	bool distance;
	float4 shadows;
};

float GetBakedShadow (ShadowMask mask, int channel) {
	float shadow = 1.0;
	if (mask.always || mask.distance) {
		if (channel >= 0) {
			// GLSL ES 1.00 (WebGL1) does not support dynamic indexing of vec4;
			// channel is a function parameter, not a constant-index-expression.
			if (channel == 0) shadow = mask.shadows.x;
			else if (channel == 1) shadow = mask.shadows.y;
			else if (channel == 2) shadow = mask.shadows.z;
			else if (channel == 3) shadow = mask.shadows.w;
		}
	}
	return shadow;
}

float GetBakedShadow (ShadowMask mask, int channel, float strength) {
	if (mask.always || mask.distance) {
		return lerp(1.0, GetBakedShadow(mask, channel), strength);
	}
	return 1.0;
}

struct ShadowData {
	int cascadeIndex;
	float cascadeBlend;
	float strength;
	ShadowMask shadowMask;
};

float MixBakedAndRealtimeShadows (
	ShadowData global, float shadow, int shadowMaskChannel, float strength
) {
	float baked = GetBakedShadow(global.shadowMask, shadowMaskChannel);
	if (global.shadowMask.always) {
		shadow = lerp(1.0, shadow, global.strength);
		shadow = min(baked, shadow);
		return lerp(1.0, shadow, strength);
	}
	if (global.shadowMask.distance) {
		shadow = lerp(baked, shadow, global.strength);
		return lerp(1.0, shadow, strength);
	}
	return lerp(1.0, shadow, strength * global.strength);
}

float FadedShadowStrength (float distance, float scale, float fade) {
	return saturate((1.0 - distance * scale) * fade);
}

ShadowData GetShadowData (Surface surfaceWS) {
	ShadowData data;
	data.shadowMask.always = false;
	data.shadowMask.distance = false;
	data.shadowMask.shadows = 1.0;
	data.cascadeBlend = 1.0;
	data.strength = FadedShadowStrength(
		surfaceWS.depth, _ShadowDistanceFade.x, _ShadowDistanceFade.y
	);
	// Manually unrolled cascade loop with constant indices.
	// GLSL ES 1.00 (WebGL1) does not support dynamic indexing of uniform arrays,
	// and the original break-based loop made the index non-constant.
	int i = _CascadeCount;

	if (_CascadeCount > 0) {
		float4 sphere = _CascadeCullingSpheres[0];
		float distanceSqr = DistanceSquared(surfaceWS.position, sphere.xyz);
		if (distanceSqr < sphere.w) {
			i = 0;
			float fade = FadedShadowStrength(
				distanceSqr, _CascadeData[0].x, _ShadowDistanceFade.z);
			if (_CascadeCount == 1) data.strength *= fade;
			else data.cascadeBlend = fade;
		}
	}
	if (i == _CascadeCount && _CascadeCount > 1) {
		float4 sphere = _CascadeCullingSpheres[1];
		float distanceSqr = DistanceSquared(surfaceWS.position, sphere.xyz);
		if (distanceSqr < sphere.w) {
			i = 1;
			float fade = FadedShadowStrength(
				distanceSqr, _CascadeData[1].x, _ShadowDistanceFade.z);
			if (_CascadeCount == 2) data.strength *= fade;
			else data.cascadeBlend = fade;
		}
	}
	if (i == _CascadeCount && _CascadeCount > 2) {
		float4 sphere = _CascadeCullingSpheres[2];
		float distanceSqr = DistanceSquared(surfaceWS.position, sphere.xyz);
		if (distanceSqr < sphere.w) {
			i = 2;
			float fade = FadedShadowStrength(
				distanceSqr, _CascadeData[2].x, _ShadowDistanceFade.z);
			if (_CascadeCount == 3) data.strength *= fade;
			else data.cascadeBlend = fade;
		}
	}
	if (i == _CascadeCount && _CascadeCount > 3) {
		float4 sphere = _CascadeCullingSpheres[3];
		float distanceSqr = DistanceSquared(surfaceWS.position, sphere.xyz);
		if (distanceSqr < sphere.w) {
			i = 3;
			float fade = FadedShadowStrength(
				distanceSqr, _CascadeData[3].x, _ShadowDistanceFade.z);
			data.strength *= fade;
		}
	}

	if (i == _CascadeCount && _CascadeCount > 0) {
		data.strength = 0.0;
	}
	else if (_SoftCascadeBlend < 0.5 && data.cascadeBlend < surfaceWS.dither) {
		i += 1;
	}
	if (_SoftCascadeBlend < 0.5) {
		data.cascadeBlend = 1.0;
	}
	data.cascadeIndex = i;
	return data;
}

struct DirectionalShadowData {
	float strength;
	int tileIndex;
	float normalBias;
	int shadowMaskChannel;
};

float SampleDirectionalShadowAtlas (float3 positionSTS) {
	return SAMPLE_TEXTURE2D_SHADOW(
		_DirectionalShadowAtlas, SHADOW_SAMPLER, positionSTS
	);
}

float FilterDirectionalShadow (float3 positionSTS) {
	#if defined(DIRECTIONAL_FILTER_SETUP)
		real weights[DIRECTIONAL_FILTER_SAMPLES];
		real2 positions[DIRECTIONAL_FILTER_SAMPLES];
		float4 size = _ShadowAtlasSize.yyxx;
		DIRECTIONAL_FILTER_SETUP(size, positionSTS.xy, weights, positions);
		float shadow = 0;
		for (int i = 0; i < DIRECTIONAL_FILTER_SAMPLES; i++) {
			shadow += weights[i] * SampleDirectionalShadowAtlas(
				float3(positions[i].xy, positionSTS.z)
			);
		}
		return shadow;
	#else
		return SampleDirectionalShadowAtlas(positionSTS);
	#endif
}

// Helper functions that use restrictive for-loops so the loop variable
// qualifies as a constant-index-expression in GLSL ES 1.00 (WebGL1).
float GetCascadeDataY (int cascadeIndex) {
	float result = _CascadeData[0].y;
	[unroll]
	for (int c = 1; c < MAX_CASCADE_COUNT; c++) {
		if (c == cascadeIndex) result = _CascadeData[c].y;
	}
	return result;
}

float4x4 GetDirectionalShadowMatrix (int tileIndex) {
	float4x4 result = _DirectionalShadowMatrices[0];
	[unroll]
	for (int t = 1; t < MAX_SHADOWED_DIRECTIONAL_LIGHT_COUNT * MAX_CASCADE_COUNT; t++) {
		if (t == tileIndex) result = _DirectionalShadowMatrices[t];
	}
	return result;
}

float GetCascadedShadow (
	DirectionalShadowData directional, ShadowData global, Surface surfaceWS
) {
	float3 normalBias = surfaceWS.interpolatedNormal *
		(directional.normalBias * GetCascadeDataY(global.cascadeIndex));
	float3 positionSTS = mul(
		GetDirectionalShadowMatrix(directional.tileIndex),
		float4(surfaceWS.position + normalBias, 1.0)
	).xyz;
	float shadow = FilterDirectionalShadow(positionSTS);
	if (global.cascadeBlend < 1.0) {
		normalBias = surfaceWS.interpolatedNormal *
			(directional.normalBias * GetCascadeDataY(global.cascadeIndex + 1));
		positionSTS = mul(
			GetDirectionalShadowMatrix(directional.tileIndex + 1),
			float4(surfaceWS.position + normalBias, 1.0)
		).xyz;
		shadow = lerp(
			FilterDirectionalShadow(positionSTS), shadow, global.cascadeBlend
		);
	}
	return shadow;
}

float GetDirectionalShadowAttenuation (
	DirectionalShadowData directional, ShadowData global, Surface surfaceWS
) {
	if (!surfaceWS.receiveShadows) {
		return 1.0;
	}

	float shadow;
	if (directional.strength * global.strength <= 0.0) {
		shadow = GetBakedShadow(
			global.shadowMask, directional.shadowMaskChannel,
			abs(directional.strength)
		);
	}
	else {
		shadow = GetCascadedShadow(directional, global, surfaceWS);
		shadow = MixBakedAndRealtimeShadows(
			global, shadow, directional.shadowMaskChannel, directional.strength
		);
	}
	return shadow;
}

struct OtherShadowData {
	float strength;
	int tileIndex;
	bool isPoint;
	int shadowMaskChannel;
	float3 lightPositionWS;
	float3 lightDirectionWS;
	float3 spotDirectionWS;
};

float SampleOtherShadowAtlas (float3 positionSTS, float3 bounds) {
	positionSTS.xy = clamp(positionSTS.xy, bounds.xy, bounds.xy + bounds.z);
	return SAMPLE_TEXTURE2D_SHADOW(
		_OtherShadowAtlas, SHADOW_SAMPLER, positionSTS
	);
}

float FilterOtherShadow (float3 positionSTS, float3 bounds) {
	#if defined(OTHER_FILTER_SETUP)
		real weights[OTHER_FILTER_SAMPLES];
		real2 positions[OTHER_FILTER_SAMPLES];
		float4 size = _ShadowAtlasSize.wwzz;
		OTHER_FILTER_SETUP(size, positionSTS.xy, weights, positions);
		float shadow = 0;
		for (int i = 0; i < OTHER_FILTER_SAMPLES; i++) {
			shadow += weights[i] * SampleOtherShadowAtlas(
				float3(positions[i].xy, positionSTS.z), bounds
			);
		}
		return shadow;
	#else
		return SampleOtherShadowAtlas(positionSTS, bounds);
	#endif
}

// Replaced static const array with direct selection for WebGL1/GLES2.
float3 GetPointShadowPlane (int faceIndex) {
	if (faceIndex == 0) return float3(-1.0, 0.0, 0.0);
	if (faceIndex == 1) return float3(1.0, 0.0, 0.0);
	if (faceIndex == 2) return float3(0.0, -1.0, 0.0);
	if (faceIndex == 3) return float3(0.0, 1.0, 0.0);
	if (faceIndex == 4) return float3(0.0, 0.0, -1.0);
	return float3(0.0, 0.0, 1.0);
}

float4 GetOtherShadowTile (int tileIndex) {
	float4 result = _OtherShadowTiles[0];
	[unroll]
	for (int t = 1; t < MAX_SHADOWED_OTHER_LIGHT_COUNT; t++) {
		if (t == tileIndex) result = _OtherShadowTiles[t];
	}
	return result;
}

float4x4 GetOtherShadowMatrix (int tileIndex) {
	float4x4 result = _OtherShadowMatrices[0];
	[unroll]
	for (int t = 1; t < MAX_SHADOWED_OTHER_LIGHT_COUNT; t++) {
		if (t == tileIndex) result = _OtherShadowMatrices[t];
	}
	return result;
}

float GetOtherShadow (
	OtherShadowData other, ShadowData global, Surface surfaceWS
) {
	int tileIndex = int(other.tileIndex);
	float3 lightPlane = other.spotDirectionWS;
	if (other.isPoint) {
		int faceOffset = int(CubeMapFaceID(-other.lightDirectionWS));
		tileIndex += faceOffset;
		lightPlane = GetPointShadowPlane(faceOffset);
	}
	float4 tileData = GetOtherShadowTile(tileIndex);
	float3 surfaceToLight = other.lightPositionWS - surfaceWS.position;
	float distanceToLightPlane = dot(surfaceToLight, lightPlane);
	float3 normalBias =
		surfaceWS.interpolatedNormal * (distanceToLightPlane * tileData.w);
	float4 positionSTS = mul(
		GetOtherShadowMatrix(tileIndex),
		float4(surfaceWS.position + normalBias, 1.0)
	);
	return FilterOtherShadow(positionSTS.xyz / positionSTS.w, tileData.xyz);
}

float GetOtherShadowAttenuation (
	OtherShadowData other, ShadowData global, Surface surfaceWS
) {
	if (!surfaceWS.receiveShadows) {
		return 1.0;
	}
	
	float shadow;
	if (other.strength * global.strength <= 0.0) {
		shadow = GetBakedShadow(
			global.shadowMask, other.shadowMaskChannel, abs(other.strength)
		);
	}
	else {
		shadow = GetOtherShadow(other, global, surfaceWS);
		shadow = MixBakedAndRealtimeShadows(
			global, shadow, other.shadowMaskChannel, other.strength
		);
	}
	return shadow;
}

#endif