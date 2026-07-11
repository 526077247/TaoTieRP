#ifndef TAOTIE_COOKIES_INCLUDED
#define TAOTIE_COOKIES_INCLUDED

#include "ShaderLibrary/Common.hlsl"

#define MAX_DIRECTIONAL_LIGHT_COUNT 4
#define MAX_COOKIE_OTHER_LIGHT_COUNT 8

// Each TEXTURE2D consumes one sampler slot. On GLES2/GLES3/WebGL2 the
// combined sampler limit is 16, and the Lit shader already uses ~10-14
// slots for material/shadow/GI textures. 12 cookie textures would
// exceed the limit, so cookie textures are only declared on non-GLES APIs.
#if !defined(SHADER_API_GLES) && !defined(SHADER_API_GLES3) && !defined(SHADER_API_GLCORE)

// Cookie textures — per-light global textures
TEXTURE2D(_DirLightCookie0); TEXTURE2D(_DirLightCookie1);
TEXTURE2D(_DirLightCookie2); TEXTURE2D(_DirLightCookie3);

TEXTURE2D(_OtherLightCookie0); TEXTURE2D(_OtherLightCookie1);
TEXTURE2D(_OtherLightCookie2); TEXTURE2D(_OtherLightCookie3);
TEXTURE2D(_OtherLightCookie4); TEXTURE2D(_OtherLightCookie5);
TEXTURE2D(_OtherLightCookie6); TEXTURE2D(_OtherLightCookie7);

// World-to-light cookie projection matrices + enable flags
CBUFFER_START(CookieMatrices)
    float4x4 _DirLightCookieMatrix[MAX_DIRECTIONAL_LIGHT_COUNT];
    float4x4 _OtherLightCookieMatrix[MAX_COOKIE_OTHER_LIGHT_COUNT];
    float _DirLightCookieEnabled[MAX_DIRECTIONAL_LIGHT_COUNT];
    float _OtherLightCookieEnabled[MAX_COOKIE_OTHER_LIGHT_COUNT];
CBUFFER_END

// Directional: orthographic projection, tile with frac()
float3 SampleDirectionalCookie(int index, float3 positionWS)
{
    if (_DirLightCookieEnabled[index] < 0.5)
        return float3(1, 1, 1);

    float4 posLS = mul(_DirLightCookieMatrix[index], float4(positionWS, 1.0));
    float2 uv = frac(posLS.xy * 0.5 + 0.5);

    [flatten] if (index == 0) return SAMPLE_TEXTURE2D(_DirLightCookie0, sampler_linear_clamp, uv).rgb;
    [flatten] if (index == 1) return SAMPLE_TEXTURE2D(_DirLightCookie1, sampler_linear_clamp, uv).rgb;
    [flatten] if (index == 2) return SAMPLE_TEXTURE2D(_DirLightCookie2, sampler_linear_clamp, uv).rgb;
    return SAMPLE_TEXTURE2D(_DirLightCookie3, sampler_linear_clamp, uv).rgb;
}

// Spot: perspective projection through cone
float3 SampleSpotCookie(int index, float3 positionWS)
{
    if (index >= MAX_COOKIE_OTHER_LIGHT_COUNT)
        return float3(1, 1, 1);
    if (_OtherLightCookieEnabled[index] < 0.5)
        return float3(1, 1, 1);

    float4 posCS = mul(_OtherLightCookieMatrix[index], float4(positionWS, 1.0));
    float2 uv = saturate((posCS.xy / posCS.w) * 0.5 + 0.5);

    [flatten] if (index == 0) return SAMPLE_TEXTURE2D(_OtherLightCookie0, sampler_linear_clamp, uv).rgb;
    [flatten] if (index == 1) return SAMPLE_TEXTURE2D(_OtherLightCookie1, sampler_linear_clamp, uv).rgb;
    [flatten] if (index == 2) return SAMPLE_TEXTURE2D(_OtherLightCookie2, sampler_linear_clamp, uv).rgb;
    [flatten] if (index == 3) return SAMPLE_TEXTURE2D(_OtherLightCookie3, sampler_linear_clamp, uv).rgb;
    [flatten] if (index == 4) return SAMPLE_TEXTURE2D(_OtherLightCookie4, sampler_linear_clamp, uv).rgb;
    [flatten] if (index == 5) return SAMPLE_TEXTURE2D(_OtherLightCookie5, sampler_linear_clamp, uv).rgb;
    [flatten] if (index == 6) return SAMPLE_TEXTURE2D(_OtherLightCookie6, sampler_linear_clamp, uv).rgb;
    return SAMPLE_TEXTURE2D(_OtherLightCookie7, sampler_linear_clamp, uv).rgb;
}

#else // GLES2/GLES3/WebGL2: skip cookie textures to stay within 16-sampler limit

float3 SampleDirectionalCookie(int index, float3 positionWS)
{
    return float3(1, 1, 1);
}

float3 SampleSpotCookie(int index, float3 positionWS)
{
    return float3(1, 1, 1);
}

#endif

#endif // TAOTIE_COOKIES_INCLUDED
