#ifndef TAOTIE_LENS_FLARE_SETUP_INCLUDED
#define TAOTIE_LENS_FLARE_SETUP_INCLUDED

// Include TaoTie RP common (provides TEXTURE2D, SAMPLER, _ScreenParams, _CameraDepthTexture, etc.)
#include "ShaderLibrary/Common.hlsl"

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
