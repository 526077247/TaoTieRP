#ifndef TAOTIE_FXAA_PASS_INCLUDED
#define TAOTIE_FXAA_PASS_INCLUDED

// NVIDIA FXAA 3.11 quality presets
#if defined(FXAA_QUALITY_LOW)
    #define FXAA_QUALITY_PRESET 12
#elif defined(FXAA_QUALITY_MEDIUM)
    #define FXAA_QUALITY_PRESET 28
#else
    #define FXAA_QUALITY_PRESET 39
#endif

// NVIDIA FXAA 3.11 quality scan counts and offsets
#if (FXAA_QUALITY_PRESET == 12)
    #define FXAA_QUALITY_P 5
    static const float2 fxaaQualityP[5] = {
        float2(0.0, 1.0/1.0),
        float2(1.0/1.5, 1.0/2.0),
        float2(1.0/2.0, 1.0/2.0),
        float2(1.0/2.0, 1.0/2.0),
        float2(1.0/2.0, 1.0/2.0)
    };
#elif (FXAA_QUALITY_PRESET == 28)
    #define FXAA_QUALITY_P 11
    static const float2 fxaaQualityP[11] = {
        float2(0.0, 1.0/1.0),
        float2(1.0/1.5, 1.0/2.0),
        float2(1.0/2.0, 1.0/2.0),
        float2(1.0/2.0, 1.0/2.0),
        float2(1.0/2.0, 1.0/2.0),
        float2(1.0/2.0, 1.0/2.0),
        float2(1.0/2.0, 1.0/2.0),
        float2(1.0/2.0, 1.0/2.0),
        float2(1.0/2.0, 1.0/2.0),
        float2(1.0/2.0, 1.0/2.0),
        float2(1.0/2.0, 1.0/2.0)
    };
#else // preset 39
    #define FXAA_QUALITY_P 13
    static const float2 fxaaQualityP[13] = {
        float2(0.0, 1.0/1.0),
        float2(1.0/1.5, 1.0/2.0),
        float2(1.0/2.0, 1.0/2.0),
        float2(1.0/2.0, 1.0/2.0),
        float2(1.0/2.0, 1.0/2.0),
        float2(1.0/2.0, 1.0/2.0),
        float2(1.0/2.0, 1.0/2.0),
        float2(1.0/2.0, 1.0/2.0),
        float2(1.0/2.0, 1.0/2.0),
        float2(1.0/2.0, 1.0/2.0),
        float2(1.0/2.0, 1.0/2.0),
        float2(1.0/2.0, 1.0/2.0),
        float2(1.0/2.0, 1.0/2.0)
    };
#endif

float4 _FXAAConfig;

float GetLuma (float2 uv) {
    #if defined(FXAA_ALPHA_CONTAINS_LUMA)
    return GetSource(uv).a;
    #else
    return GetSource(uv).g;
    #endif
}

float4 FXAAPassFragment (Varyings input) : SV_TARGET {
    float2 posM = input.screenUV;
    float4 rgbyM = GetSource(posM);

    // 3x3 luma neighborhood — single-pass gather
    float lumaM = GetLuma(posM);
    float lumaS = GetLuma(posM + float2( 0.0, -1.0) * _PostFXSource_TexelSize.xy);
    float lumaN = GetLuma(posM + float2( 0.0,  1.0) * _PostFXSource_TexelSize.xy);
    float lumaW = GetLuma(posM + float2(-1.0,  0.0) * _PostFXSource_TexelSize.xy);
    float lumaE = GetLuma(posM + float2( 1.0,  0.0) * _PostFXSource_TexelSize.xy);

    float lumaNS = lumaN + lumaS;
    float lumaWE = lumaW + lumaE;
    float lumaMNE = lumaM + 0.25 * (lumaNS + lumaWE);

    // Early exit — NVIDIA threshold test
    float rangeMax = max(max(lumaN, lumaS), max(lumaW, lumaE));
    float rangeMin = min(min(lumaN, lumaS), min(lumaW, lumaE));
    float range = rangeMax - rangeMin;

    if (range < max(_FXAAConfig.x, _FXAAConfig.y * lumaMNE)) {
        float dither = (InterleavedGradientNoise(input.positionCS.xy, 0) - 0.5) / 255.0;
        rgbyM.rgb += dither;
        return rgbyM;
    }

    // Diagonal neighbors
    float lumaNW = GetLuma(posM + float2(-1.0,  1.0) * _PostFXSource_TexelSize.xy);
    float lumaNE = GetLuma(posM + float2( 1.0,  1.0) * _PostFXSource_TexelSize.xy);
    float lumaSW = GetLuma(posM + float2(-1.0, -1.0) * _PostFXSource_TexelSize.xy);
    float lumaSE = GetLuma(posM + float2( 1.0, -1.0) * _PostFXSource_TexelSize.xy);

    float lumaNSSE = lumaN + lumaS + lumaNW + lumaNE + lumaSW + lumaSE;

    // Edge direction — NVIDIA's horizontal/vertical test
    float edgeHorz1 = (-2.0 * lumaM) + lumaNS;
    float edgeVert1 = (-2.0 * lumaM) + lumaWE;
    float edgeHorz2 = (-2.0 * lumaMNE) + lumaNSSE;
    float edgeVert2 = (-2.0 * lumaMNE) + lumaNSSE;

    bool horzSpan = abs(edgeHorz1) >= abs(edgeVert1);
    bool horzSpan2 = abs(edgeHorz2) >= abs(edgeVert2);
    bool horzSpanFinal = horzSpan && horzSpan2;

    float lengthSign = horzSpanFinal ? _PostFXSource_TexelSize.y : _PostFXSource_TexelSize.x;

    // Opposite luma direction
    float luma1 = horzSpanFinal ? lumaN : lumaW;
    float luma2 = horzSpanFinal ? lumaS : lumaE;

    float gradient = abs(luma1 - lumaM);
    gradient = max(gradient, abs(luma2 - lumaM));
    gradient = max(gradient, 1.0/255.0);

    float lumaLocalAverage = 0.0;
    if (gradient < 1.0/15.0) {
        lumaLocalAverage = 0.5 * (luma1 + lumaM);
    } else {
        lumaLocalAverage = (0.5 * (luma1 + lumaM) + 0.5 * (luma2 + lumaM)) * 0.5;
    }

    // Subpixel — NVIDIA's optimized calculation
    float subpix1 = max(max(lumaNS, lumaWE) - 2.0 * lumaM, 0.0);
    float subpix2 = max(max(lumaNSSE, -lumaNSSE) - 2.0 * lumaM, 0.0);
    float subpixL = subpix1 + subpix2;
    float subpix = subpixL * 0.25;

    float lumaAverage = lumaMNE * 0.5;
    float subpixRange = max(lumaAverage - rangeMin, rangeMax - lumaAverage);
    float subpixAttn = saturate(subpixRange / lumaAverage);
    subpix = subpix * subpixAttn * subpixAttn;

    // NVIDIA edge scan
    float2 posB = posM;
    if (horzSpanFinal) {
        posB.y += lengthSign * 0.5;
    } else {
        posB.x += lengthSign * 0.5;
    }

    float2 offNP;
    if (horzSpanFinal) {
        offNP.x = _PostFXSource_TexelSize.x;
        offNP.y = 0.0;
    } else {
        offNP.x = 0.0;
        offNP.y = _PostFXSource_TexelSize.y;
    }

    float2 posN = posB - offNP * fxaaQualityP[0].y;
    float2 posP = posB + offNP * fxaaQualityP[0].y;

    float lumaEndN = GetLuma(posN);
    float lumaEndP = GetLuma(posP);

    bool doneN = abs(lumaEndN - lumaLocalAverage) < gradient * 0.25;
    bool doneP = abs(lumaEndP - lumaLocalAverage) < gradient * 0.25;

    if (!doneN && !doneP) {
        float lumaNN = GetLuma(posN - offNP * fxaaQualityP[0].y * 0.5);
        float lumaPP = GetLuma(posP + offNP * fxaaQualityP[0].y * 0.5);

        doneN = abs(lumaNN - lumaLocalAverage) < gradient * 0.25;
        doneP = abs(lumaPP - lumaLocalAverage) < gradient * 0.25;

        if (!doneN) posN -= offNP * fxaaQualityP[0].y * 1.5;
        if (!doneP) posP += offNP * fxaaQualityP[0].y * 1.5;
    }

    [unroll]
    for (int i = 1; i < FXAA_QUALITY_P - 1; i++) {
        if (!doneN) {
            lumaEndN = GetLuma(posN);
            doneN = abs(lumaEndN - lumaLocalAverage) < gradient * 0.25;
            if (!doneN) posN -= offNP * fxaaQualityP[i].y;
        }
        if (!doneP) {
            lumaEndP = GetLuma(posP);
            doneP = abs(lumaEndP - lumaLocalAverage) < gradient * 0.25;
            if (!doneP) posP += offNP * fxaaQualityP[i].y;
        }
        if (doneN && doneP) break;
    }

    if (doneN && doneP) {
        float distN = horzSpanFinal ? (posM.x - posN.x) : (posM.y - posN.y);
        float distP = horzSpanFinal ? (posP.x - posM.x) : (posP.y - posM.y);

        bool directionN = distN < distP;
        float dist = min(distN, distP);
        float spanLength = distN + distP;

        float pixelOffset = -dist / spanLength + 0.5;

        bool lumaMLT = lumaM < lumaLocalAverage;
        bool lumaEndLT = (directionN ? lumaEndN : lumaEndP) < lumaLocalAverage;

        if (lumaMLT == lumaEndLT) {
            pixelOffset = 0.0;
        }

        subpix += abs(pixelOffset) * 0.5;
        pixelOffset = max(pixelOffset, -subpix * _FXAAConfig.z);
        pixelOffset = min(pixelOffset, subpix * _FXAAConfig.z);

        float2 posF = posM;
        if (horzSpanFinal) {
            posF.y += pixelOffset * lengthSign;
        } else {
            posF.x += pixelOffset * lengthSign;
        }

        rgbyM = GetSource(posF);
    }
    return rgbyM;
}

#endif
