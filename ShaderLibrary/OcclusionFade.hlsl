#ifndef TAOTIE_OCCLUSION_FADE_INCLUDED
#define TAOTIE_OCCLUSION_FADE_INCLUDED

// Generic screen-space circular mask. The render pipeline fills these from an
// IScreenMaskProvider; no gameplay concept (player, tree, etc.) lives here.
#define TAOTIE_SCREEN_MASK_MAX_CIRCLES 4

float4 _ScreenMaskCircles[TAOTIE_SCREEN_MASK_MAX_CIRCLES];
float  _ScreenMaskStrengths[TAOTIE_SCREEN_MASK_MAX_CIRCLES];
// Anchor depth in linear eye space. A fragment is clipped only when it is in
// front of the anchor, so scene props behind the player stay untouched.
float  _ScreenMaskEyeDepths[TAOTIE_SCREEN_MASK_MAX_CIRCLES];
int    _ScreenMaskCount;
float  _ScreenMaskAspect;

// 1 = keep the fragment, 0 = fully dithering it away.
float ScreenMaskVisibility (Fragment fragment) {
    #if defined(_SCREEN_MASK_ENABLED)
        if (_ScreenMaskCount <= 0) {
            return 1.0;
        }
        float visibility = 1.0;
        for (int i = 0; i < TAOTIE_SCREEN_MASK_MAX_CIRCLES; i++) {
            if (i >= _ScreenMaskCount) {
                break;
            }
            float4 circle = _ScreenMaskCircles[i];
            float2 offset = (fragment.screenUV - circle.xy) * float2(_ScreenMaskAspect, 1.0);
            float outer = max(circle.w, circle.z + 0.0001);
            float falloff = smoothstep(circle.z, outer, length(offset));
            // 0.02 eye-space bias keeps surfaces at the anchor depth (and the
            // player itself) from being clipped by rounding noise.
            float anchorDepth = _ScreenMaskEyeDepths[i];
            float occludes = anchorDepth > 0.0
                ? step(fragment.depth + 0.02, anchorDepth) : 1.0;
            float amount = saturate(_ScreenMaskStrengths[i]) * occludes;
            visibility = min(visibility, lerp(1.0, falloff, amount));
        }
        return visibility;
    #else
        return 1.0;
    #endif
}

void ClipByScreenMask (Fragment fragment) {
    #if defined(_SCREEN_MASK_ENABLED)
        float visibility = ScreenMaskVisibility(fragment);
        if (visibility >= 0.999) {
            return;
        }
        clip(visibility - InterleavedGradientNoise(fragment.positionSS, 0));
    #endif
}

#endif
