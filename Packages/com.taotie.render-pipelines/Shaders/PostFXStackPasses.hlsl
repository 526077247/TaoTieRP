#ifndef TAOTIE_POST_FX_PASSES_INCLUDED
#define TAOTIE_POST_FX_PASSES_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

Texture2D<float4> _PostFXSource;
Texture2D<float4> _PostFXSource2;

struct VSInput
{
    float3 positionOS : POSITION;
    float2 uv : TEXCOORD0;
};

struct PSOutput
{
    float4 color : SV_TARGET;
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float2 screenUV : TEXCOORD0;
};

float4 GetSource(float2 screenUV)
{
    return _PostFXSource.SampleLevel(_Sampler_ClampU_ClampV_Point, screenUV, 0);
}

float4 GetSource2(float2 screenUV)
{
    return _PostFXSource2.SampleLevel(_Sampler_ClampU_ClampV_Point, screenUV, 0);
}

void CopyPassFragment (Varyings input, out PSOutput o)
{
    o = (PSOutput)0;
    o.color = GetSource(input.screenUV);
}

Varyings DefaultPassVertex (VSInput i)
{
    Varyings output;
    output.positionCS = mul(UNITY_MATRIX_VP, float4(i.positionOS, 1.f));
    output.screenUV = i.uv;
    return output;
}

float4 _PostFXSource_TexelSize;

float4 GetSourceTexelSize () {
    return _PostFXSource_TexelSize;
}

float4 BloomHorizontalPassFragment (Varyings input) : SV_TARGET
{
    float3 color = 0.0;
    float offsets[] = {
        -3.23076923, -1.38461538, 0.0, 1.38461538, 3.23076923
    };
    float weights[] = {
        0.07027027, 0.31621622, 0.22702703, 0.31621622, 0.07027027
    };
    for (int i = 0; i < 5; i++) {
        float offset = offsets[i] * 2.0 * GetSourceTexelSize().x;
        color += GetSource(input.screenUV + float2(offset, 0.0)).rgb * weights[i];
    }
    return float4(color, 1.0);
}

float4 BloomVerticalPassFragment (Varyings input) : SV_TARGET {
    float3 color = 0.0;
    float offsets[] = {
        -3.23076923, -1.38461538, 0.0, 1.38461538, 3.23076923
    };
    float weights[] = {
        0.07027027, 0.31621622, 0.22702703, 0.31621622, 0.07027027
    };
    for (int i = 0; i < 5; i++) {
        float offset = offsets[i] * GetSourceTexelSize().y;
        color += GetSource(input.screenUV + float2(0.0, offset)).rgb * weights[i];
    }
    return float4(color, 1.0);
}

real2 BSpline3MiddleLeft(real2 x)
{
    return 0.16666667 + x * (0.5 + x * (0.5 - x * 0.5));
}

real2 BSpline3MiddleRight(real2 x)
{
    return 0.66666667 + x * (-1.0 + 0.5 * x) * x;
}

real2 BSpline3Rightmost(real2 x)
{
    return 0.16666667 + x * (-0.5 + x * (0.5 - x * 0.16666667));
}

void BicubicFilter(float2 fracCoord, out float2 weights[2], out float2 offsets[2])
{
    float2 r  = BSpline3Rightmost(fracCoord);
    float2 mr = BSpline3MiddleRight(fracCoord);
    float2 ml = BSpline3MiddleLeft(fracCoord);
    float2 l  = 1.0 - mr - ml - r;

    weights[0] = r + mr;
    weights[1] = ml + l;
    offsets[0] = -1.0 + mr * rcp(weights[0]);
    offsets[1] =  1.0 + l * rcp(weights[1]);
}

float4 SampleTexture2DBicubic(Texture2D<float4> tex, float2 coord, float4 texSize, float2 maxCoord, uint unused /* needed to match signature of texarray version below */)
{
    float2 xy = coord * texSize.xy + 0.5;
    float2 ic = floor(xy);
    float2 fc = frac(xy);

    float2 weights[2], offsets[2];
    BicubicFilter(fc, weights, offsets);

    return weights[0].y * (weights[0].x * tex.SampleLevel(_Sampler_ClampU_ClampV_Linear, min((ic + float2(offsets[0].x, offsets[0].y) - 0.5) * texSize.zw, maxCoord), 0.0)  +
                           weights[1].x * tex.SampleLevel(_Sampler_ClampU_ClampV_Linear, min((ic + float2(offsets[1].x, offsets[0].y) - 0.5) * texSize.zw, maxCoord), 0.0)) +
           weights[1].y * (weights[0].x * tex.SampleLevel(_Sampler_ClampU_ClampV_Linear, min((ic + float2(offsets[0].x, offsets[1].y) - 0.5) * texSize.zw, maxCoord), 0.0)  +
                           weights[1].x * tex.SampleLevel(_Sampler_ClampU_ClampV_Linear, min((ic + float2(offsets[1].x, offsets[1].y) - 0.5) * texSize.zw, maxCoord), 0.0));
}

float4 GetSourceBicubic (float2 screenUV) {
    return SampleTexture2DBicubic(_PostFXSource, screenUV,
        _PostFXSource_TexelSize.zwxy, 1.0, 0.0
    );
}

bool _BloomBicubicUpsampling;
float _BloomIntensity;

float4 BloomAddPassFragment (Varyings input) : SV_TARGET
{
    float3 lowRes;
    if (_BloomBicubicUpsampling) {
        lowRes = GetSourceBicubic(input.screenUV).rgb;
    }
    else {
        lowRes = GetSource(input.screenUV).rgb;
    }
    float4 highRes = GetSource2(input.screenUV);
    return float4(lowRes * _BloomIntensity + highRes.rgb, highRes.a);
}

float4 _BloomThreshold;

float3 ApplyBloomThreshold (float3 color) {
    float brightness = Max3(color.r, color.g, color.b);
    float soft = brightness + _BloomThreshold.y;
    soft = clamp(soft, 0.0, _BloomThreshold.z);
    soft = soft * soft * _BloomThreshold.w;
    float contribution = max(soft, brightness - _BloomThreshold.x);
    contribution /= max(brightness, 0.00001);
    return color * contribution;
}

float4 BloomPrefilterPassFragment (Varyings input) : SV_TARGET
{
    float3 color = ApplyBloomThreshold(GetSource(input.screenUV).rgb);
    return float4(color, 1.0);
}

float4 BloomPrefilterFirefliesPassFragment (Varyings input) : SV_TARGET {
    float3 color = 0.0;
    float weightSum = 0.0;
    float2 offsets[] = {
        float2(0.0, 0.0),
        float2(-1.0, -1.0), float2(-1.0, 1.0), float2(1.0, -1.0), float2(1.0, 1.0)
    };
    for (int i = 0; i < 5; i++) {
        float3 c =
            GetSource(input.screenUV + offsets[i] * GetSourceTexelSize().xy * 2.0).rgb;
        c = ApplyBloomThreshold(c);
        float w = 1.0 / (Luminance(c) + 1.0);
        color += c * w;
        weightSum += w;
    }
    color /= weightSum;
    return float4(color, 1.0);
}

float4 BloomScatterPassFragment (Varyings input) : SV_TARGET {
    float3 lowRes;
    if (_BloomBicubicUpsampling) {
        lowRes = GetSourceBicubic(input.screenUV).rgb;
    }
    else {
        lowRes = GetSource(input.screenUV).rgb;
    }
    float3 highRes = GetSource2(input.screenUV).rgb;
    return float4(lerp(highRes, lowRes, _BloomIntensity), 1.0);
}

float4 BloomScatterFinalPassFragment (Varyings input) : SV_TARGET {
    float3 lowRes;
    if (_BloomBicubicUpsampling) {
        lowRes = GetSourceBicubic(input.screenUV).rgb;
    }
    else {
        lowRes = GetSource(input.screenUV).rgb;
    }
    float4 highRes = GetSource2(input.screenUV);
    lowRes += highRes.rgb - ApplyBloomThreshold(highRes.rgb);
    return float4(lerp(highRes.rgb, lowRes, _BloomIntensity), highRes.a);
}

float4 _ColorAdjustments;
float4 _ColorFilter;
float4 _WhiteBalance;
float4 _SplitToningShadows, _SplitToningHighlights;
float4 _ChannelMixerRed, _ChannelMixerGreen, _ChannelMixerBlue;
float4 _SMHShadows, _SMHMidtones, _SMHHighlights, _SMHRange;

float3 ColorGradePostExposure (float3 color) {
    return color * _ColorAdjustments.x;
}

float3 ColorGradeWhiteBalance (float3 color) {
    color = LinearToLMS(color);
    color *= _WhiteBalance.rgb;
    return LMSToLinear(color);
}

float3 ColorGradingContrast (float3 color, bool useACES) {
    color = useACES ? ACES_to_ACEScc(unity_to_ACES(color)) : LinearToLogC(color);
    color = (color - ACEScc_MIDGRAY) * _ColorAdjustments.y + ACEScc_MIDGRAY;
    return useACES ? ACES_to_ACEScg(ACEScc_to_ACES(color)) : LogCToLinear(color);
}

float3 ColorGradeColorFilter (float3 color) {
    return color * _ColorFilter.rgb;
}

float Luminance (float3 color, bool useACES) {
    return useACES ? AcesLuminance(color) : Luminance(color);
}

float3 ColorGradeSplitToning (float3 color, bool useACES) {
    color = PositivePow(color, 1.0 / 2.2);
    float t = saturate(Luminance(saturate(color), useACES) + _SplitToningShadows.w);
    float3 shadows = lerp(0.5, _SplitToningShadows.rgb, 1.0 - t);
    float3 highlights = lerp(0.5, _SplitToningHighlights.rgb, t);
    color = SoftLight(color, shadows);
    color = SoftLight(color, highlights);
    return PositivePow(color, 2.2);
}

float3 ColorGradingChannelMixer (float3 color) {
    return mul(
        float3x3(_ChannelMixerRed.rgb, _ChannelMixerGreen.rgb, _ChannelMixerBlue.rgb),
        color
    );
}

float3 ColorGradingShadowsMidtonesHighlights (float3 color, bool useACES) {
    float luminance = Luminance(color,useACES);
    float shadowsWeight = 1.0 - smoothstep(_SMHRange.x, _SMHRange.y, luminance);
    float highlightsWeight = smoothstep(_SMHRange.z, _SMHRange.w, luminance);
    float midtonesWeight = 1.0 - shadowsWeight - highlightsWeight;
    return
        color * _SMHShadows.rgb * shadowsWeight +
        color * _SMHMidtones.rgb * midtonesWeight +
        color * _SMHHighlights.rgb * highlightsWeight;
}


float3 ColorGradingHueShift (float3 color) {
    color = RgbToHsv(color);
    float hue = color.x + _ColorAdjustments.z;
    color.x = RotateHue(hue, 0.0, 1.0);
    return HsvToRgb(color);
}

float3 ColorGradingSaturation (float3 color, bool useACES) {
    float luminance = Luminance(color, useACES);
    return (color - luminance) * _ColorAdjustments.w + luminance;
}

float3 ColorGrade (float3 color, bool useACES = false) {
    color = ColorGradePostExposure(color);
    color = ColorGradeWhiteBalance(color);
    color = ColorGradingContrast(color, useACES);
    color = ColorGradeColorFilter(color);
    color = max(color, 0.0);
    ColorGradeSplitToning(color, useACES);
    color = ColorGradingChannelMixer(color);
    color = max(color, 0.0);
    color = ColorGradingShadowsMidtonesHighlights(color, useACES);
    color = ColorGradingHueShift(color);
    color = ColorGradingSaturation(color, useACES);
    return max(useACES ? ACEScg_to_ACES(color) : color, 0.0);
}

float4 _ColorGradingLUTParameters;
bool _ColorGradingLUTInLogC;

float3 GetColorGradedLUT (float2 uv, bool useACES = false) {
    float3 color = GetLutStripValue(uv, _ColorGradingLUTParameters);
    return ColorGrade(_ColorGradingLUTInLogC ? LogCToLinear(color) : color, useACES);
}

float4 ColorGradingNonePassFragment (Varyings input) : SV_TARGET {
    float3 color = GetColorGradedLUT(input.screenUV);
    return float4(color, 1.0);
}

float4 ColorGradingACESPassFragment (Varyings input) : SV_TARGET {
    float3 color = GetColorGradedLUT(input.screenUV, true);
    color = AcesTonemap(color);
    return float4(color, 1.0);
}

float4 ColorGradingNeutralPassFragment (Varyings input) : SV_TARGET {
    float3 color = GetColorGradedLUT(input.screenUV);
    color = NeutralTonemap(color);
    return float4(color, 1.0);
}

float4 ColorGradingReinhardPassFragment (Varyings input) : SV_TARGET {
    float3 color = GetColorGradedLUT(input.screenUV);
    color /= color + 1.0;
    return float4(color, 1.0);
}

TEXTURE2D(_ColorGradingLUT);

float3 ApplyColorGradingLUT (float3 color) {
    return ApplyLut2D(
        TEXTURE2D_ARGS(_ColorGradingLUT, sampler_linear_clamp),
        saturate(_ColorGradingLUTInLogC ? LinearToLogC(color) : color),
        _ColorGradingLUTParameters.xyz
    );
}

float4 ApplyColorGradingPassFragment (Varyings input) : SV_TARGET {
    float4 color = GetSource(input.screenUV);
    color.rgb = ApplyColorGradingLUT(color.rgb);
    return color;
}

float4 FinalPassFragmentRescale (Varyings input) : SV_TARGET {
    return GetSourceBicubic(input.screenUV);
}

float4 ApplyColorGradingWithLumaPassFragment (Varyings input) : SV_TARGET {
    float4 color = GetSource(input.screenUV);
    color.rgb = ApplyColorGradingLUT(color.rgb);
    #if UNITY_COLORSPACE_GAMMA
    color = LinearToSRGB(color);
    #endif
    color.a = sqrt(Luminance(color.rgb));
    return color;
}

#endif