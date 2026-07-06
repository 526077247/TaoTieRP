#ifndef TAOTIE_SMAA_PASS_INCLUDED
#define TAOTIE_SMAA_PASS_INCLUDED

// Full SMAA 1x reference implementation (adapted from Jorge Jimenez et al.)
// Adapted for TaoTie RP PostFXStack infrastructure

#define SMAA_RT_METRICS _PostFXSource_TexelSize
#define SMAA_THRESHOLD 0.1
#define SMAA_MAX_SEARCH_STEPS 16
#define SMAA_MAX_SEARCH_STEPS_DIAG 8
#define SMAA_CORNER_ROUNDING 25
#define SMAA_LOCAL_CONTRAST_ADAPTATION_FACTOR 2.0
#define SMAA_AREATEX_MAX_DISTANCE 16
#define SMAA_AREATEX_MAX_DISTANCE_DIAG 20
#define SMAA_AREATEX_PIXEL_SIZE (1.0 / float2(160.0, 560.0))
#define SMAA_AREATEX_SUBTEX_SIZE (1.0 / 7.0)
#define SMAA_SEARCHTEX_SIZE float2(66.0, 33.0)
#define SMAA_SEARCHTEX_PACKED_SIZE float2(64.0, 16.0)
#define SMAA_CORNER_ROUNDING_NORM (25.0 / 100.0)

Texture2D<float4> _SMAAAreaTex;
Texture2D<float> _SMAASearchTex;

#define SMAASampleLevelZero(tex, coord) tex.SampleLevel(_Sampler_ClampU_ClampV_Linear, coord, 0)
#define SMAASampleLevelZeroPoint(tex, coord) tex.SampleLevel(_Sampler_ClampU_ClampV_Point, coord, 0)
#define SMAASampleLevelZeroOffset(tex, coord, offset) tex.SampleLevel(_Sampler_ClampU_ClampV_Linear, coord, 0, offset)
#define SMAASample(tex, coord) tex.SampleLevel(_Sampler_ClampU_ClampV_Linear, coord, 0)
#define SMAASamplePoint(tex, coord) tex.SampleLevel(_Sampler_ClampU_ClampV_Point, coord, 0)
#define SMAA_AREATEX_SELECT(sample) sample.rg
#define SMAA_SEARCHTEX_SELECT(sample) sample.r

void SMAAMovc(bool2 cond, inout float2 variable, float2 value) {
    [flatten] if (cond.x) variable.x = value.x;
    [flatten] if (cond.y) variable.y = value.y;
}
void SMAAMovc(bool4 cond, inout float4 variable, float4 value) {
    SMAAMovc(cond.xy, variable.xy, value.xy);
    SMAAMovc(cond.zw, variable.zw, value.zw);
}

//----------------------------------------------------------------------------
// Edge Detection (Pass 1)
// _PostFXSource = color
//----------------------------------------------------------------------------
float2 SMAALumaEdgeDetectionPS(float2 texcoord) {
    float4 offset[3];
    offset[0] = mad(SMAA_RT_METRICS.xyxy, float4(-1.0, 0.0, 0.0, -1.0), texcoord.xyxy);
    offset[1] = mad(SMAA_RT_METRICS.xyxy, float4( 1.0, 0.0, 0.0,  1.0), texcoord.xyxy);
    offset[2] = mad(SMAA_RT_METRICS.xyxy, float4(-2.0, 0.0, 0.0, -2.0), texcoord.xyxy);

    float2 threshold = float2(SMAA_THRESHOLD, SMAA_THRESHOLD);
    float3 weights = float3(0.2126, 0.7152, 0.0722);

    float L = dot(SMAASamplePoint(_PostFXSource, texcoord).rgb, weights);
    float Lleft = dot(SMAASamplePoint(_PostFXSource, offset[0].xy).rgb, weights);
    float Ltop  = dot(SMAASamplePoint(_PostFXSource, offset[0].zw).rgb, weights);

    float4 delta;
    delta.xy = abs(L - float2(Lleft, Ltop));
    float2 edges = step(threshold, delta.xy);

    if (dot(edges, float2(1.0, 1.0)) == 0.0)
        return float2(0.0, 0.0);

    float Lright = dot(SMAASamplePoint(_PostFXSource, offset[1].xy).rgb, weights);
    float Lbottom = dot(SMAASamplePoint(_PostFXSource, offset[1].zw).rgb, weights);
    delta.zw = abs(L - float2(Lright, Lbottom));

    float2 maxDelta = max(delta.xy, delta.zw);

    float Lleftleft = dot(SMAASamplePoint(_PostFXSource, offset[2].xy).rgb, weights);
    float Ltoptop = dot(SMAASamplePoint(_PostFXSource, offset[2].zw).rgb, weights);
    delta.zw = abs(float2(Lleft, Ltop) - float2(Lleftleft, Ltoptop));

    maxDelta = max(maxDelta.xy, delta.zw);
    float finalDelta = max(maxDelta.x, maxDelta.y);

    edges.xy *= step(finalDelta, SMAA_LOCAL_CONTRAST_ADAPTATION_FACTOR * delta.xy);
    return edges;
}

float4 SMAAEdgeDetectionPassFragment(Varyings input) : SV_TARGET {
    return float4(SMAALumaEdgeDetectionPS(input.screenUV), 0.0, 1.0);
}

//----------------------------------------------------------------------------
// Search functions (Pass 2)
//----------------------------------------------------------------------------
float SMAASearchLength(float2 e, float offset) {
    float2 scale = SMAA_SEARCHTEX_SIZE * float2(0.5, -1.0);
    float2 bias = SMAA_SEARCHTEX_SIZE * float2(offset, 1.0);
    scale += float2(-1.0, 1.0);
    bias += float2(0.5, -0.5);
    scale *= 1.0 / SMAA_SEARCHTEX_PACKED_SIZE;
    bias *= 1.0 / SMAA_SEARCHTEX_PACKED_SIZE;
    return SMAA_SEARCHTEX_SELECT(SMAASampleLevelZero(_SMAASearchTex, mad(scale, e, bias)));
}

float SMAASearchXLeft(float2 texcoord, float end) {
    float2 e = float2(0.0, 1.0);
    while (texcoord.x > end && e.g > 0.8281 && e.r == 0.0) {
        e = SMAASampleLevelZero(_PostFXSource, texcoord).rg;
        texcoord = mad(-float2(2.0, 0.0), SMAA_RT_METRICS.xy, texcoord);
    }
    float offset = mad(-(255.0 / 127.0), SMAASearchLength(e, 0.0), 3.25);
    return mad(SMAA_RT_METRICS.x, offset, texcoord.x);
}

float SMAASearchXRight(float2 texcoord, float end) {
    float2 e = float2(0.0, 1.0);
    while (texcoord.x < end && e.g > 0.8281 && e.r == 0.0) {
        e = SMAASampleLevelZero(_PostFXSource, texcoord).rg;
        texcoord = mad(float2(2.0, 0.0), SMAA_RT_METRICS.xy, texcoord);
    }
    float offset = mad(-(255.0 / 127.0), SMAASearchLength(e, 0.5), 3.25);
    return mad(-SMAA_RT_METRICS.x, offset, texcoord.x);
}

float SMAASearchYUp(float2 texcoord, float end) {
    float2 e = float2(1.0, 0.0);
    while (texcoord.y > end && e.r > 0.8281 && e.g == 0.0) {
        e = SMAASampleLevelZero(_PostFXSource, texcoord).rg;
        texcoord = mad(-float2(0.0, 2.0), SMAA_RT_METRICS.xy, texcoord);
    }
    float offset = mad(-(255.0 / 127.0), SMAASearchLength(e.gr, 0.0), 3.25);
    return mad(SMAA_RT_METRICS.y, offset, texcoord.y);
}

float SMAASearchYDown(float2 texcoord, float end) {
    float2 e = float2(1.0, 0.0);
    while (texcoord.y < end && e.r > 0.8281 && e.g == 0.0) {
        e = SMAASampleLevelZero(_PostFXSource, texcoord).rg;
        texcoord = mad(float2(0.0, 2.0), SMAA_RT_METRICS.xy, texcoord);
    }
    float offset = mad(-(255.0 / 127.0), SMAASearchLength(e.gr, 0.5), 3.25);
    return mad(-SMAA_RT_METRICS.y, offset, texcoord.y);
}

float2 SMAAArea(float2 dist, float e1, float e2, float offset) {
    float2 texcoord = mad(float2(SMAA_AREATEX_MAX_DISTANCE, SMAA_AREATEX_MAX_DISTANCE),
        round(4.0 * float2(e1, e2)), dist);
    texcoord = mad(SMAA_AREATEX_PIXEL_SIZE, texcoord, 0.5 * SMAA_AREATEX_PIXEL_SIZE);
    texcoord.y = mad(SMAA_AREATEX_SUBTEX_SIZE, offset, texcoord.y);
    return SMAA_AREATEX_SELECT(SMAASampleLevelZero(_SMAAAreaTex, texcoord));
}

void SMAADetectHorizontalCornerPattern(inout float2 weights, float4 texcoord, float2 d) {
    float2 leftRight = step(d.xy, d.yx);
    float2 rounding = (1.0 - SMAA_CORNER_ROUNDING_NORM) * leftRight;
    rounding /= leftRight.x + leftRight.y;
    float2 factor = float2(1.0, 1.0);
    factor.x -= rounding.x * SMAASampleLevelZeroOffset(_PostFXSource, texcoord.xy, int2(0, 1)).r;
    factor.x -= rounding.y * SMAASampleLevelZeroOffset(_PostFXSource, texcoord.zw, int2(1, 1)).r;
    factor.y -= rounding.x * SMAASampleLevelZeroOffset(_PostFXSource, texcoord.xy, int2(0, -2)).r;
    factor.y -= rounding.y * SMAASampleLevelZeroOffset(_PostFXSource, texcoord.zw, int2(1, -2)).r;
    weights *= saturate(factor);
}

void SMAADetectVerticalCornerPattern(inout float2 weights, float4 texcoord, float2 d) {
    float2 leftRight = step(d.xy, d.yx);
    float2 rounding = (1.0 - SMAA_CORNER_ROUNDING_NORM) * leftRight;
    rounding /= leftRight.x + leftRight.y;
    float2 factor = float2(1.0, 1.0);
    factor.x -= rounding.x * SMAASampleLevelZeroOffset(_PostFXSource, texcoord.xy, int2(1, 0)).g;
    factor.x -= rounding.y * SMAASampleLevelZeroOffset(_PostFXSource, texcoord.zw, int2(1, 1)).g;
    factor.y -= rounding.x * SMAASampleLevelZeroOffset(_PostFXSource, texcoord.xy, int2(-2, 0)).g;
    factor.y -= rounding.y * SMAASampleLevelZeroOffset(_PostFXSource, texcoord.zw, int2(-2, 1)).g;
    weights *= saturate(factor);
}

//----------------------------------------------------------------------------
// Blend Weight Calculation (Pass 2)
// _PostFXSource = edges
//----------------------------------------------------------------------------
float4 SMAABlendingWeightCalculationPassFragment(Varyings input) : SV_TARGET {
    float2 texcoord = input.screenUV;
    float2 pixcoord = texcoord * SMAA_RT_METRICS.zw;

    float4 offset[3];
    offset[0] = mad(SMAA_RT_METRICS.xyxy, float4(-0.25, -0.125, 1.25, -0.125), texcoord.xyxy);
    offset[1] = mad(SMAA_RT_METRICS.xyxy, float4(-0.125, -0.25, -0.125, 1.25), texcoord.xyxy);
    offset[2] = mad(SMAA_RT_METRICS.xxyy,
        float4(-2.0, 2.0, -2.0, 2.0) * SMAA_MAX_SEARCH_STEPS,
        float4(offset[0].xz, offset[1].yw));

    float4 weights = float4(0.0, 0.0, 0.0, 0.0);
    float2 e = SMAASample(_PostFXSource, texcoord).rg;

    [branch] if (e.g > 0.0) { // Edge at north
        float2 d;
        float3 coords;

        coords.x = SMAASearchXLeft(offset[0].xy, offset[2].x);
        coords.y = offset[1].y;
        d.x = coords.x;

        float e1 = SMAASampleLevelZero(_PostFXSource, coords.xy).r;

        coords.z = SMAASearchXRight(offset[0].zw, offset[2].y);
        d.y = coords.z;

        d = abs(round(mad(SMAA_RT_METRICS.zz, d, -pixcoord.xx)));
        float2 sqrt_d = sqrt(d);

        float e2 = SMAASampleLevelZeroOffset(_PostFXSource, coords.zy, int2(1, 0)).r;

        weights.rg = SMAAArea(sqrt_d, e1, e2, 0.0);

        coords.y = texcoord.y;
        SMAADetectHorizontalCornerPattern(weights.rg, coords.xyzy, d);
    }

    [branch] if (e.r > 0.0) { // Edge at west
        float2 d;
        float3 coords;

        coords.y = SMAASearchYUp(offset[1].xy, offset[2].z);
        coords.x = offset[0].x;
        d.x = coords.y;

        float e1 = SMAASampleLevelZero(_PostFXSource, coords.xy).g;

        coords.z = SMAASearchYDown(offset[1].zw, offset[2].w);
        d.y = coords.z;

        d = abs(round(mad(SMAA_RT_METRICS.ww, d, -pixcoord.yy)));
        float2 sqrt_d = sqrt(d);

        float e2 = SMAASampleLevelZeroOffset(_PostFXSource, coords.xz, int2(0, 1)).g;

        weights.ba = SMAAArea(sqrt_d, e1, e2, 0.0);

        coords.x = texcoord.x;
        SMAADetectVerticalCornerPattern(weights.ba, coords.xyxz, d);
    }

    return weights;
}

//----------------------------------------------------------------------------
// Neighborhood Blending (Pass 3)
// _PostFXSource = color, _PostFXSource2 = blend weights
//----------------------------------------------------------------------------
float4 SMAANeighborhoodBlendingPassFragment(Varyings input) : SV_TARGET {
    float2 texcoord = input.screenUV;
    float4 offset = mad(SMAA_RT_METRICS.xyxy, float4(1.0, 0.0, 0.0, 1.0), texcoord.xyxy);

    float4 a;
    a.x = SMAASample(_PostFXSource2, offset.xy).a; // Right
    a.y = SMAASample(_PostFXSource2, offset.zw).g; // Top
    a.wz = SMAASample(_PostFXSource2, texcoord).xz; // Bottom / Left

    [branch] if (dot(a, float4(1.0, 1.0, 1.0, 1.0)) < 1e-5) {
        return SMAASampleLevelZero(_PostFXSource, texcoord);
    } else {
        bool h = max(a.x, a.z) > max(a.y, a.w);

        float4 blendingOffset = float4(0.0, a.y, 0.0, a.w);
        float2 blendingWeight = a.yw;
        SMAAMovc(bool4(h, h, h, h), blendingOffset, float4(a.x, 0.0, a.z, 0.0));
        SMAAMovc(bool2(h, h), blendingWeight, a.xz);
        blendingWeight /= dot(blendingWeight, float2(1.0, 1.0));

        float4 blendingCoord = mad(blendingOffset,
            float4(SMAA_RT_METRICS.xy, -SMAA_RT_METRICS.xy), texcoord.xyxy);

        float4 color = blendingWeight.x * SMAASampleLevelZero(_PostFXSource, blendingCoord.xy);
        color += blendingWeight.y * SMAASampleLevelZero(_PostFXSource, blendingCoord.zw);

        float dither = (InterleavedGradientNoise(input.positionCS.xy, 0) - 0.5) / 255.0;
        color.rgb += dither;

        return color;
    }
}

#endif
