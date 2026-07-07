#ifndef TAOTIE_LENS_FLARE_INCLUDED
#define TAOTIE_LENS_FLARE_INCLUDED

#include "ShaderLibrary/Common.hlsl"

// Lens Flare shader: renders procedural shapes (Circle, Polygon) and image textures
// with additive/screen/lerp blend modes and occlusion-based fade.

TEXTURE2D(_FlareTexture);

float4 _FlareData;      // x = intensity, y = rotation(rad), z = aspectRatio, w = type(0=image,1=circle,2=polygon)
float4 _FlareColor;     // tint color
float _FlarePolygonSides; // number of sides for polygon type

struct FlareVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float2 screenPos : TEXCOORD1;
};

FlareVaryings FlarePassVertex(
    float3 positionOS : POSITION,
    float2 uv : TEXCOORD0,
    uint instanceID : SV_InstanceID)
{
    FlareVaryings output;
    output.positionCS = float4(positionOS.xy, 0.0, 1.0);
    output.uv = uv * 2.0 - 1.0; // [-1, 1]
    output.screenPos = uv;

    // Apply rotation
    float angle = _FlareData.y;
    float s, c;
    sincos(angle, s, c);
    output.uv = float2(
        output.uv.x * c - output.uv.y * s,
        output.uv.x * s + output.uv.y * c
    );

    return output;
}

// Signed distance to regular polygon
float SdPolygon(float2 p, float n)
{
    float an = 3.14159265 / n;
    float bn = 3.14159265 / n;
    float bn2 = an * 2.0;
    float m = an;
    float2 q = abs(p);
    float d = length(q - float2(cos(m), sin(m)) * max(0.0, dot(q, float2(cos(m), sin(m)))));

    // Correct polygon distance
    float he = 2.0 * cos(an);
    q = abs(p);
    q = float2(cos(m) * q.x + sin(m) * q.y, -sin(m) * q.x + cos(m) * q.y);
    if (q.x > 0.0)
    {
        m = an;
        q = abs(p);
    }

    return length(q - float2(1.0, 0.0) * clamp(dot(q, float2(1.0, 0.0)), 0.0, 1.0)) * sign(q.y);
}

float4 FlareFragment(FlareVaryings input) : SV_Target
{
    float2 uv = input.uv;
    float type = _FlareData.w;
    float intensity = _FlareData.x;

    float alpha = 0.0;
    float3 color = _FlareColor.rgb;

    if (type < 0.5)
    {
        // Image
        float4 tex = _FlareTexture.Sample(sampler_linear_clamp, input.screenPos);
        alpha = tex.a * intensity;
        color *= tex.rgb;
    }
    else if (type < 1.5)
    {
        // Circle — soft radial falloff
        float d = length(uv);
        alpha = smoothstep(1.0, 0.8, d) * intensity;
    }
    else
    {
        // Polygon
        float n = _FlarePolygonSides;
        float an = 3.14159265 / n;
        float bn = 2.0 * an;
        float he = 2.0 * cos(an);

        // Rotate to nearest sector
        float a = atan2(uv.y, uv.x);
        a = fmod(a, bn) - an;
        float r = length(uv);
        float2 p = r * float2(cos(a), sin(a));

        float d = p.x - he * 0.5;
        alpha = smoothstep(0.05, -0.05, d) * intensity;
    }

    return float4(color * alpha, alpha);
}

#endif // TAOTIE_LENS_FLARE_INCLUDED
