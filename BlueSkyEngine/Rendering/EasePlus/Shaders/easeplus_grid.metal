#include <metal_stdlib>
using namespace metal;

struct ViewUniforms {
    float4x4 View;
    float4x4 Proj;
    float4x4 ViewProj;
    float4x4 InvViewProj;
    float4   CameraPos;
    float2   ScreenSize;
    float    NearPlane;
    float    FarPlane;
    float3   SunDirection;
    float    SunIntensity;
    float3   SunColor;
    int      TilesX;
};

struct GridVertexOut {
    float4 position [[position]];
    float3 nearPoint;
    float3 farPoint;
};

struct GridFragmentOut {
    half4 color [[color(0)]];
    float depth [[depth(any)]];
};

vertex GridVertexOut easeplus_vs_grid(
    uint vertexID [[vertex_id]],
    constant ViewUniforms& view [[buffer(10)]])
{
    float2 positions[6] = {
        float2(-1.0, -1.0), float2( 1.0, -1.0), float2( 1.0,  1.0),
        float2(-1.0, -1.0), float2( 1.0,  1.0), float2(-1.0,  1.0)
    };

    float2 p = positions[vertexID];
    float4 nearH = view.InvViewProj * float4(p, 0.0, 1.0);
    float4 farH = view.InvViewProj * float4(p, 1.0, 1.0);

    GridVertexOut out;
    out.position = float4(p, 0.0, 1.0);
    out.nearPoint = nearH.xyz / nearH.w;
    out.farPoint = farH.xyz / farH.w;
    return out;
}

float EasePlusGridDepth(float3 worldPos, float4x4 viewProj)
{
    float4 clip = viewProj * float4(worldPos, 1.0);
    return clamp(clip.z / clip.w, 0.0, 1.0);
}

half EasePlusGridLine(float2 coord, half width)
{
    float2 deriv = max(fwidth(coord), float2(0.0001));
    float2 grid = abs(fract(coord - 0.5) - 0.5) / deriv;
    float line = min(grid.x, grid.y);
    return half(1.0 - smoothstep(0.0, float(width), line));
}

fragment GridFragmentOut easeplus_fs_grid(
    GridVertexOut in [[stage_in]],
    constant ViewUniforms& view [[buffer(10)]])
{
    float3 ray = in.farPoint - in.nearPoint;
    if (abs(ray.y) < 0.0001)
        discard_fragment();

    float t = -in.nearPoint.y / ray.y;
    if (t < 0.0)
        discard_fragment();

    float3 hit = in.nearPoint + ray * t;
    float dist = length(hit - view.CameraPos.xyz);

    half fadeFar = half(1.0 - smoothstep(70.0, 240.0, dist));
    half fadeNear = half(smoothstep(0.4, 1.8, dist));
    half fade = fadeFar * fadeNear;
    if (fade <= 0.001h)
        discard_fragment();

    float2 xz = hit.xz;
    half fine = EasePlusGridLine(xz, 0.010h) * 0.10h;
    half coarse = EasePlusGridLine(xz * 0.1, 0.026h) * 0.26h;
    half alpha = max(fine, coarse) * fade;

    float2 axisDeriv = max(fwidth(xz), float2(0.0001));
    half xAxis = half(1.0 - smoothstep(0.0, 1.2, abs(hit.z) / (axisDeriv.y * 0.75)));
    half zAxis = half(1.0 - smoothstep(0.0, 1.2, abs(hit.x) / (axisDeriv.x * 0.75)));

    half3 color = half3(0.20h, 0.22h, 0.25h);
    color = mix(color, half3(0.72h, 0.22h, 0.18h), xAxis);
    color = mix(color, half3(0.20h, 0.36h, 0.78h), zAxis);
    alpha = max(alpha, max(xAxis, zAxis) * 0.55h * fade);

    if (alpha <= 0.003h)
        discard_fragment();

    GridFragmentOut out;
    out.color = half4(color, alpha);
    out.depth = EasePlusGridDepth(hit, view.ViewProj);
    return out;
}
