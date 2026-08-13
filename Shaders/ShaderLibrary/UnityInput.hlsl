#ifndef TAOTIE_UNITY_INPUT_INCLUDED
#define TAOTIE_UNITY_INPUT_INCLUDED
// GLES2/GLES3: CBUFFER arrays not supported or cause performance regression.
#if !defined(SHADER_API_GLES) && !defined(SHADER_API_GLES3)
CBUFFER_START(UnityPerDraw)
#endif
// Space block feature
float4x4 unity_ObjectToWorld;
float4x4 unity_WorldToObject;
float4 unity_LODFade;
real4 unity_WorldTransformParams;

// Render Layer block feature
float4 unity_RenderingLayer;

// Light Indices block feature
float4 unity_PackedLightIndices;
half4 unity_LightData;

float4 unity_ProbesOcclusion;

// Reflection Probe block feature
real4 unity_SpecCube0_HDR;
real4 unity_SpecCube1_HDR;

float4 unity_SpecCube0_BoxMax;
float4 unity_SpecCube0_BoxMin;
float4 unity_SpecCube0_ProbePosition;
float4 unity_SpecCube0_Rotation;
float4 unity_SpecCube1_BoxMax;
float4 unity_SpecCube1_BoxMin;
float4 unity_SpecCube1_ProbePosition;
float4 unity_SpecCube1_Rotation;

// Lightmap block feature
float4 unity_LightmapST;
float4 unity_DynamicLightmapST;

// SH block feature
real4 unity_SHAr;
real4 unity_SHAg;
real4 unity_SHAb;
real4 unity_SHBr;
real4 unity_SHBg;
real4 unity_SHBb;
real4 unity_SHC;

// Renderer bounding box
float4 unity_RendererBounds_Min;
float4 unity_RendererBounds_Max;

// Velocity
float4x4 unity_MatrixPreviousM;
float4x4 unity_MatrixPreviousMI;
float4 unity_MotionVectorsParams;

// Sprite
float4 unity_SpriteColor;
float4 unity_SpriteProps;

// Light Probe Proxy Volume
float4 unity_ProbeVolumeParams;
float4x4 unity_ProbeVolumeWorldToObject;
float4 unity_ProbeVolumeSizeInv;
float4 unity_ProbeVolumeMin;

#if !defined(SHADER_API_GLES) && !defined(SHADER_API_GLES3)
CBUFFER_END
#endif

// Reflection probe textures stay outside the constant buffer.
TEXTURECUBE(unity_SpecCube0);
SAMPLER(samplerunity_SpecCube0);
TEXTURECUBE(unity_SpecCube1);
SAMPLER(samplerunity_SpecCube1);

// All per-frame / per-camera uniforms for the whole pipeline are declared once,
// centrally, in dedicated constant buffers (see UnityPerFrameCBuffers.hlsl).
#include "UnityPerFrameCBuffers.hlsl"
#endif