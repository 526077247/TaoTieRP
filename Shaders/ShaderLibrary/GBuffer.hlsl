#ifndef TAOTIE_GBUFFER_INCLUDED
#define TAOTIE_GBUFFER_INCLUDED

// G-Buffer layout:
// RT0: RGB = albedo, A = occlusion
// RT1: RG = octahedral-encoded normal.xy (remapped to [0,1]), B = metallic, A = smoothness
// RT2: RGB = emission, A = 1
// Normals are stored in [0,1] range so the format works with both UNorm and SFloat textures.

struct GBufferOutput {
    float4 albedoAO : SV_Target0;
    float4 normalMS : SV_Target1;
    float4 emission : SV_Target2;
};

// Pack renderingLayerMask into emission.a (otherwise unused, was hardcoded 1.0).
// Uses (float) value cast — matches C# side, normal floats survive CBUFFER.
float4 PackGBufferEmission(float3 emissionColor, uint renderingLayerMask)
{
    return float4(emissionColor, (float)renderingLayerMask);
}
// --- Octahedral normal encoding (handles all directions) ---

float2 OctWrap(float2 v) {
    return (1.0 - abs(v.yx)) * (step(0.0, v.xy) * 2.0 - 1.0);
}

// Encode normal to [0,1] range for format-agnostic storage (UNorm / SFloat).
float2 EncodeNormal(float3 n) {
    n /= (abs(n.x) + abs(n.y) + abs(n.z));
    if (n.z < 0.0) n.xy = OctWrap(n.xy);
    return n.xy * 0.5 + 0.5;
}

// Decode normal from [0,1] range.
float3 DecodeGBufferNormal(float2 f) {
    f = f * 2.0 - 1.0;
    float3 n = float3(f.x, f.y, 1.0 - abs(f.x) - abs(f.y));
    if (n.z < 0.0) n.xy = OctWrap(n.xy);
    return normalize(n);
}

// Inverse view-projection matrix, set from C# for reliable world position reconstruction.
float4x4 _InverseViewProj;

// Reconstruct world-space position from depth and screen UV.
// _InverseViewProj computed with GL.GetGPUProjectionMatrix(, true) includes Y-flip,
// so we flip clipPos.y to match.
float3 ReconstructWorldPos(float2 screenUV, float rawDepth) {
    #if UNITY_REVERSED_Z
        float depth = rawDepth;
    #else
        float depth = rawDepth * 2.0 - 1.0;
    #endif
    float4 clipPos = float4(screenUV.x * 2.0 - 1.0, screenUV.y * 2.0 - 1.0, depth, 1.0);
    float4 worldPos = mul(_InverseViewProj, clipPos);
    return worldPos.xyz / worldPos.w;
}

#endif
