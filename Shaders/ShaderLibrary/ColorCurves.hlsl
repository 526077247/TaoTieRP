#ifndef TAOTIE_COLOR_CURVES_INCLUDED
#define TAOTIE_COLOR_CURVES_INCLUDED

// Adapted from URP Color Curves
// Uses baked 1D LUT textures (256px) sampled per-channel.
// 8 curves: Master, Red, Green, Blue, HueVsHue, HueVsSat, SatVsSat, LumVsSat

#include "ShaderLibrary/Common.hlsl"

TEXTURE2D(_CCMaster);   // R = master curve
TEXTURE2D(_CCRGB);      // R = red, G = green, B = blue curves
TEXTURE2D(_CCHueHue);   // hue vs hue shift
TEXTURE2D(_CCHueSat);   // hue vs saturation
TEXTURE2D(_CCSatSat);   // sat vs sat
TEXTURE2D(_CCLumSat);   // lum vs sat

TEXTURE2D(_CCSource);

float4 _CCTexelSize; // x = 1/256

struct CCVaryings
{
    float4 positionCS : SV_POSITION;
    float2 screenUV : TEXCOORD0;
};

CCVaryings CCPassVertex(float3 positionOS : POSITION, float2 uv : TEXCOORD0)
{
    CCVaryings output;
    output.positionCS = float4(positionOS.xy, 0.0, 1.0);
    output.screenUV = uv;
    if (_ProjectionParams.x < 0.0)
        output.screenUV.y = 1.0 - output.screenUV.y;
    return output;
}

// Sample a 1D curve baked into a 1-pixel-tall texture
float SampleCurve(TEXTURE2D(tex), float x)
{
    return SAMPLE_TEXTURE2D(tex, sampler_linear_clamp, float2(saturate(x), 0.0)).r;
}

float3 SampleCurve3(TEXTURE2D(tex), float3 rgb)
{
    return float3(
        SAMPLE_TEXTURE2D(tex, sampler_linear_clamp, float2(saturate(rgb.r), 0.0)).r,
        SAMPLE_TEXTURE2D(tex, sampler_linear_clamp, float2(saturate(rgb.g), 0.0)).g,
        SAMPLE_TEXTURE2D(tex, sampler_linear_clamp, float2(saturate(rgb.b), 0.0)).b
    );
}

float4 CCFragment(CCVaryings input) : SV_Target
{
    float2 uv = input.screenUV;
    float4 source = SAMPLE_TEXTURE2D(_CCSource, sampler_linear_clamp, uv);

    float3 color = source.rgb;
    
    // Apply to all channels (approximation: use per-channel luminance)
    float3 masterAdjusted;
    masterAdjusted.r = SampleCurve(_CCMaster, color.r);
    masterAdjusted.g = SampleCurve(_CCMaster, color.g);
    masterAdjusted.b = SampleCurve(_CCMaster, color.b);
    color = masterAdjusted;

    // RGB curves
    color = SampleCurve3(_CCRGB, color);

    // Hue vs Hue
    float3 hsv = RgbToHsv(color);
    float hueShift = SampleCurve(_CCHueHue, hsv.x);
    hsv.x = frac(hsv.x + hueShift);

    // Hue vs Sat
    float hueSat = SampleCurve(_CCHueSat, hsv.x);
    hsv.y *= hueSat;

    // Sat vs Sat
    float satSat = SampleCurve(_CCSatSat, hsv.y);
    hsv.y *= satSat;

    // Lum vs Sat
    float lum = dot(color, float3(0.2126, 0.7152, 0.0722));
    float lumSat = SampleCurve(_CCLumSat, lum);
    hsv.y *= lumSat;

    color = HsvToRgb(hsv);

    return float4(color, source.a);
}

#endif
