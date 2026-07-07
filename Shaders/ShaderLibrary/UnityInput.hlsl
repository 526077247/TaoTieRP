#ifndef TAOTIE_UNITY_INPUT_INCLUDED
#define TAOTIE_UNITY_INPUT_INCLUDED

CBUFFER_START(UnityPerDraw)
float4x4 unity_ObjectToWorld;
float4x4 unity_WorldToObject;
float4 unity_LODFade;
real4 unity_WorldTransformParams;

float4 unity_RenderingLayer;


float4 unity_ProbesOcclusion;

float4 unity_SpecCube0_HDR;

float4 unity_LightmapST;
float4 unity_DynamicLightmapST;

float4 unity_SHAr;
float4 unity_SHAg;
float4 unity_SHAb;
float4 unity_SHBr;
float4 unity_SHBg;
float4 unity_SHBb;
float4 unity_SHC;

float4 unity_ProbeVolumeParams;
float4x4 unity_ProbeVolumeWorldToObject;
float4 unity_ProbeVolumeSizeInv;
float4 unity_ProbeVolumeMin;
CBUFFER_END

float4x4 unity_MatrixVP;
float4x4 unity_MatrixV;
float4x4 unity_MatrixInvV;
float4x4 unity_prev_MatrixM;
float4x4 unity_prev_MatrixIM;
float4x4 glstate_matrix_projection;

float3 _WorldSpaceCameraPos;

float4 unity_OrthoParams;
float4 _ProjectionParams;
float4 _ScreenParams;
float4 _ZBufferParams;

float4x4 unity_CameraProjection;
float4x4 unity_CameraInvProjection;

// Per-frame globals (populated by Unity engine)
float4 _Time;       // (t/20, t, t*2, t*3)
float4 _SinTime;    // (sin(t/8), sin(t/4), sin(t/2), sin(t))
float4 _CosTime;    // (cos(t/8), cos(t/4), cos(t/2), cos(t))
float4 unity_DeltaTime; // (dt, 1/dt, smoothDt, 1/smoothDt)
float4 _LastImageEffectsEnabledVideo;
#endif