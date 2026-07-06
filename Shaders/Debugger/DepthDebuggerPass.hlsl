#ifndef DEPTH_DEBUGGER_PASSES_INCLUDED
#define DEPTH_DEBUGGER_PASSES_INCLUDED

float _DepthDebuggerMode;    // 0 = LinearEye, 1 = Linear01, 2 = Raw
float _DepthDebuggerSplit;  // 0 = fullscreen, 1 = right-half only
float _DepthDebuggerOpacity; // 0..1

struct Varyings {
    float4 positionCS : SV_POSITION;
    float2 screenUV : VAR_SCREEN_UV;
};

Varyings DepthDebuggerVertex(uint vertexID : SV_VertexID) {
    Varyings output;
    output.positionCS = float4(
        vertexID <= 1 ? -1.0 : 3.0,
        vertexID == 1 ? 3.0 : -1.0,
        0.0, 1.0
    );
    output.screenUV = float2(
        vertexID <= 1 ? 0.0 : 2.0,
        vertexID == 1 ? 2.0 : 0.0
    );
    if (_ProjectionParams.x < 0.0) {
        output.screenUV.y = 1.0 - output.screenUV.y;
    }
    return output;
}

float4 DepthDebuggerFragment(Varyings input) : SV_TARGET {
    // Split-screen: left half transparent (shows original color)
    if (_DepthDebuggerSplit > 0.5 && input.screenUV.x < 0.5) {
        return float4(0.0, 0.0, 0.0, 0.0);
    }

    float rawDepth = SAMPLE_DEPTH_TEXTURE_LOD(
        _CameraDepthTexture, sampler_point_clamp, input.screenUV, 0);

    // Sky / no geometry
    #if UNITY_REVERSED_Z
    bool isSky = rawDepth <= 0.0;
    #else
    bool isSky = rawDepth >= 1.0;
    #endif

    if (isSky) {
        return float4(1.0, 0.0, 0.0, _DepthDebuggerOpacity); // red
    }

    float visDepth; // 0 = far (black), 1 = near (white)

    if (IsOrthographicCamera()) {
        float linearDepth = OrthographicDepthBufferToLinear(rawDepth);
        visDepth = 1.0 - saturate((linearDepth - _ProjectionParams.y) /
            max(_ProjectionParams.z - _ProjectionParams.y, 0.0001));
    } else if (_DepthDebuggerMode < 0.5) {
        // Linear eye depth, normalized to [0, far]
        float eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
        visDepth = 1.0 - saturate(eyeDepth / max(_ProjectionParams.z, 0.0001));
    } else if (_DepthDebuggerMode < 1.5) {
        // Linear 01 depth
        visDepth = 1.0 - Linear01Depth(rawDepth, _ZBufferParams);
    } else {
        // Raw depth (adjusted so near = white, far = black)
        #if UNITY_REVERSED_Z
        visDepth = rawDepth;
        #else
        visDepth = 1.0 - rawDepth;
        #endif
    }

    return float4(visDepth, visDepth, visDepth, _DepthDebuggerOpacity);
}

#endif
