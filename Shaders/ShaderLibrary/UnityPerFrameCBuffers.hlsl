#ifndef TAOTIE_UNITY_PER_FRAME_CBUFFERS_INCLUDED
#define TAOTIE_UNITY_PER_FRAME_CBUFFERS_INCLUDED

// Central declaration of every per-frame / per-camera shader uniform used by the
// pipeline, split into dedicated constant buffers:
//
//   UnityPerFrame      - engine built-in per-frame layout (SRP Batcher matches this).
//   UnityTime          - engine-updated time uniforms (not part of UnityPerFrame).
//   CameraBuffer       - per-camera full-screen / reconstruction data.
//   ForwardPlusParams  - Forward+ tile parameters (ForwardPlus.hlsl).
//   CustomLight        - directional + other light data (Light.hlsl).
//   CookieMatrices     - cookie projection matrices + enable flags (Cookies.hlsl).
//   CustomShadows      - shadow data incl. pancaking flag (Shadows.hlsl / ShadowCasterPass.hlsl).
//
// The SRP Batcher requires every per-frame shader uniform to live inside a constant
// buffer. Engine data must stay in UnityPerFrame exactly as the engine declares it;
// adding non-engine members there would break the built-in layout match. Everything
// else gets its own CBUFFER so it can be updated and shared independently.
//
// This file is included from UnityInput.hlsl (which is included by Common.hlsl), so it
// expands exactly once. Individual include files (Light.hlsl, Shadows.hlsl, Cookies.hlsl,
// GBuffer.hlsl, ForwardPlus.hlsl, Fragment.hlsl, ShadowCasterPass.hlsl) must NOT open
// these CBUFFERs themselves; they only consume the variables.

// ---- Buffer-size macros shared with Light/Shadows/Cookies (kept in sync) ----
#ifndef MAX_DIRECTIONAL_LIGHT_COUNT
    #define MAX_DIRECTIONAL_LIGHT_COUNT 4
#endif

#ifndef MAX_COOKIE_OTHER_LIGHT_COUNT
    #define MAX_COOKIE_OTHER_LIGHT_COUNT 8
#endif

#ifndef MAX_OTHER_LIGHT_COUNT
    #if defined(SHADER_API_GLES)
        #define MAX_OTHER_LIGHT_COUNT 8
    #elif defined(SHADER_API_GLES3)
        #define MAX_OTHER_LIGHT_COUNT 32
    #else
        #define MAX_OTHER_LIGHT_COUNT 256
    #endif
#endif

#ifndef MAX_SHADOWED_DIRECTIONAL_LIGHT_COUNT
    #define MAX_SHADOWED_DIRECTIONAL_LIGHT_COUNT 4
#endif

#ifndef MAX_SHADOWED_OTHER_LIGHT_COUNT
    #define MAX_SHADOWED_OTHER_LIGHT_COUNT 16
#endif

#ifndef MAX_CASCADE_COUNT
    #define MAX_CASCADE_COUNT 4
#endif

// GLES2/GLES3 do not support cbuffer arrays, so every block is only declared on
// desktop-like APIs (variables become plain globals there). The shared macros still
// default to small counts for safety.

// ---- 1. Engine built-in per-frame / per-camera layout ----
#if !defined(SHADER_API_GLES) && !defined(SHADER_API_GLES3)
CBUFFER_START(UnityPerFrame)
#endif

    // ---- Camera / per-frame transform data ----
    float4x4 unity_MatrixVP;
    float4x4 unity_MatrixV;
    float4x4 unity_MatrixInvV;
    float4x4 glstate_matrix_projection;

    float3 _WorldSpaceCameraPos;

    float4 unity_OrthoParams;
    float4 _ProjectionParams;
    float4 _ScreenParams;
    float4 _ZBufferParams;

    float4x4 unity_CameraProjection;
    float4x4 unity_CameraInvProjection;

#if !defined(SHADER_API_GLES) && !defined(SHADER_API_GLES3)
CBUFFER_END
#endif

// ---- 2. Time uniforms (populated by the engine every frame) ----
#if !defined(SHADER_API_GLES) && !defined(SHADER_API_GLES3)
CBUFFER_START(UnityTime)
#endif

    float4 _Time;          // (t/20, t, t*2, t*3)
    float4 _SinTime;       // (sin(t/8), sin(t/4), sin(t/2), sin(t))
    float4 _CosTime;       // (cos(t/8), cos(t/4), cos(t/2), cos(t))
    float4 unity_DeltaTime;// (dt, 1/dt, smoothDt, 1/smoothDt)
    float4 _LastImageEffectsEnabledVideo;

#if !defined(SHADER_API_GLES) && !defined(SHADER_API_GLES3)
CBUFFER_END
#endif

// ---- 3. Per-camera full-screen / reconstruction data ----
#if !defined(SHADER_API_GLES) && !defined(SHADER_API_GLES3)
CBUFFER_START(CameraBuffer)
#endif

    float4 _CameraBufferSize;
    float4x4 _InverseViewProj;

#if !defined(SHADER_API_GLES) && !defined(SHADER_API_GLES3)
CBUFFER_END
#endif

// ---- 4. Forward+ tile parameters (ForwardPlus.hlsl) ----
#if !defined(SHADER_API_GLES) && !defined(SHADER_API_GLES3)
CBUFFER_START(ForwardPlusParams)
#endif

    float4 _ForwardPlusTileSettings; // xy: tile size, z: tiles per row, w: wordsPerTile
    float4 _ForwardPlusDataSize;     // x: dataStride, y: zBinCount, z: wordsPerTile
    float4 _ZBinParams;              // x: zBinCount, y: near, z: 1/(far-near), w: far

#if !defined(SHADER_API_GLES) && !defined(SHADER_API_GLES3)
CBUFFER_END
#endif

// ---- 5. Directional / other light data (Light.hlsl) ----
#if !defined(SHADER_API_GLES) && !defined(SHADER_API_GLES3)
CBUFFER_START(CustomLight)
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

// ---- 6. Cookie projection matrices + enable flags (Cookies.hlsl) ----
#if !defined(SHADER_API_GLES) && !defined(SHADER_API_GLES3)
CBUFFER_START(CookieMatrices)
#endif

    float4x4 _DirLightCookieMatrix[MAX_DIRECTIONAL_LIGHT_COUNT];
    float4x4 _OtherLightCookieMatrix[MAX_COOKIE_OTHER_LIGHT_COUNT];
    float _DirLightCookieEnabled[MAX_DIRECTIONAL_LIGHT_COUNT];
    float _OtherLightCookieEnabled[MAX_COOKIE_OTHER_LIGHT_COUNT];

#if !defined(SHADER_API_GLES) && !defined(SHADER_API_GLES3)
CBUFFER_END
#endif

// ---- 7. Shadow data (Shadows.hlsl / ShadowCasterPass.hlsl) ----
#if !defined(SHADER_API_GLES) && !defined(SHADER_API_GLES3)
CBUFFER_START(CustomShadows)
#endif

    float _CascadeCount;
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
    bool _ShadowPancaking;

#if !defined(SHADER_API_GLES) && !defined(SHADER_API_GLES3)
CBUFFER_END
#endif

#endif // TAOTIE_UNITY_PER_FRAME_CBUFFERS_INCLUDED
