#ifndef TAOTIE_FXAA_PASS_INCLUDED
#define TAOTIE_FXAA_PASS_INCLUDED

// URP-style simplified FXAA (NVIDIA FXAA 3.11 console version)
#define FXAA_SPAN_MAX       (8.0)
#define FXAA_REDUCE_MUL     (1.0 / 8.0)
#define FXAA_REDUCE_MIN     (1.0 / 128.0)

// Point sample at integer pixel offset from UV.
// Uses SampleLevel with point sampler for WebGL1/GLES2 compatibility
// (Texture2D.Load is not available in GLSL ES 1.00).
float3 LoadSource(float2 uv, int2 offset)
{
    float2 sampleUV = uv + offset * _PostFXSource_TexelSize.xy;
    return SAMPLE_TEXTURE2D_LOD(_PostFXSource, sampler_PostFXSource, sampleUV, 0).rgb;
}

// Linear sample at arbitrary UV offset
float3 FetchSource(float2 uv, float2 offset)
{
    return SAMPLE_TEXTURE2D_LOD(_PostFXSource, sampler_PostFXSource, uv + offset, 0).rgb;
}

float4 FXAAPassFragment(Varyings input) : SV_TARGET
{
    float2 uv = input.screenUV;

    float3 color = LoadSource(uv, int2(0, 0));

    // Edge detection - load 4 diagonal neighbors
    float3 rgbNW = LoadSource(uv, int2(-1, -1));
    float3 rgbNE = LoadSource(uv, int2( 1, -1));
    float3 rgbSW = LoadSource(uv, int2(-1,  1));
    float3 rgbSE = LoadSource(uv, int2( 1,  1));

    rgbNW = saturate(rgbNW);
    rgbNE = saturate(rgbNE);
    rgbSW = saturate(rgbSW);
    rgbSE = saturate(rgbSE);
    color = saturate(color);

    float lumaNW = Luminance(rgbNW);
    float lumaNE = Luminance(rgbNE);
    float lumaSW = Luminance(rgbSW);
    float lumaSE = Luminance(rgbSE);
    float lumaM  = Luminance(color);

    float2 dir;
    dir.x = -((lumaNW + lumaNE) - (lumaSW + lumaSE));
    dir.y =  ((lumaNW + lumaSW) - (lumaNE + lumaSE));

    float lumaSum = lumaNW + lumaNE + lumaSW + lumaSE;
    float dirReduce = max(lumaSum * (0.25 * FXAA_REDUCE_MUL), FXAA_REDUCE_MIN);
    float rcpDirMin = rcp(min(abs(dir.x), abs(dir.y)) + dirReduce);

    dir = min((FXAA_SPAN_MAX).xx, max((-FXAA_SPAN_MAX).xx, dir * rcpDirMin)) * _PostFXSource_TexelSize.xy;

    // Blur along edge direction
    float3 rgb03 = FetchSource(uv, dir * (0.0 / 3.0 - 0.5));
    float3 rgb13 = FetchSource(uv, dir * (1.0 / 3.0 - 0.5));
    float3 rgb23 = FetchSource(uv, dir * (2.0 / 3.0 - 0.5));
    float3 rgb33 = FetchSource(uv, dir * (3.0 / 3.0 - 0.5));

    rgb03 = saturate(rgb03);
    rgb13 = saturate(rgb13);
    rgb23 = saturate(rgb23);
    rgb33 = saturate(rgb33);

    float3 rgbA = 0.5 * (rgb13 + rgb23);
    float3 rgbB = rgbA * 0.5 + 0.25 * (rgb03 + rgb33);

    float lumaB = Luminance(rgbB);

    float lumaMin = min(lumaM, min(min(lumaNW, lumaNE), min(lumaSW, lumaSE)));
    float lumaMax = max(lumaM, max(max(lumaNW, lumaNE), max(lumaSW, lumaSE)));

    color = ((lumaB < lumaMin) || (lumaB > lumaMax)) ? rgbA : rgbB;

    float dither = (InterleavedGradientNoise(input.positionCS.xy, 0) - 0.5) / 255.0;
    return float4(color + dither, GetSource(uv).a);
}

#endif
