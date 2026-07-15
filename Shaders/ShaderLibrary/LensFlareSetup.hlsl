#ifndef TAOTIE_LENS_FLARE_SETUP_INCLUDED
#define TAOTIE_LENS_FLARE_SETUP_INCLUDED

// Include TaoTie RP common (provides TEXTURE2D, SAMPLER, _ScreenParams, _CameraDepthTexture, etc.)
#include "ShaderLibrary/Common.hlsl"

// LensFlareCommon.hlsl uses XR texture macros (TEXTURE2D_X, LOAD_TEXTURE2D_X_LOD, etc.)
// which are only defined in URP/HDRP, not in the core RP package.
// TaoTie RP does not use XR, so map them to regular 2D versions.
#ifndef TEXTURE2D_X
#define TEXTURE2D_X TEXTURE2D
#endif
#ifndef TEXTURE2D_X_FLOAT
#define TEXTURE2D_X_FLOAT TEXTURE2D_FLOAT
#endif
#ifndef LOAD_TEXTURE2D_X_LOD
#define LOAD_TEXTURE2D_X_LOD LOAD_TEXTURE2D_LOD
#endif
#ifndef LOAD_TEXTURE2D_ARRAY_LOD
#define LOAD_TEXTURE2D_ARRAY_LOD(textureName, unCoord2, index, lod) textureName.Load(int4(unCoord2, index, lod))
#endif

// GetScaledScreenParams is URP-specific; _ScreenParams (declared in UnityInput.hlsl) is equivalent.
#ifndef GetScaledScreenParams
#define GetScaledScreenParams() _ScreenParams
#endif

// unity_StereoEyeIndex is only declared by UnityInstancing.hlsl on XR platforms.
// Define as 0 so LensFlareCommon.hlsl compiles without XR.
#ifndef unity_StereoEyeIndex
#define unity_StereoEyeIndex 0
#endif

#include "Packages/com.unity.render-pipelines.core/Runtime/PostProcessing/Shaders/LensFlareCommon.hlsl"

#endif // TAOTIE_LENS_FLARE_SETUP_INCLUDED
