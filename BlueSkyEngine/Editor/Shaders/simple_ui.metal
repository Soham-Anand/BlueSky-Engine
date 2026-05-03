#include <metal_stdlib>
using namespace metal;

// ──────────────────────────────────────────────────────────────────────────────
// Vertex layout (must match UIVertex in C#, stride = 40 bytes)
//   offset  0 :  float2  position
//   offset  8 :  float4  color
//   offset 24 :  float2  uv
//   offset 32 :  float   mode   (0 = solid geometry, 1 = glyph alpha-texture, 2 = full texture)
//   offset 36 :  float   _pad
// ──────────────────────────────────────────────────────────────────────────────

struct VertexIn {
    float2 position [[attribute(0)]];
    float4 color    [[attribute(1)]];
    float2 uv       [[attribute(2)]];
    float  mode     [[attribute(3)]];
};

struct VertexOut {
    float4 clipPosition [[position]];
    float4 color;
    float2 uv;
    float  mode;
};

// Compile-time sampler — no RHI sampler object needed on the CPU side.
constexpr sampler kFontSampler(
    filter::linear,
    address::clamp_to_edge,
    mip_filter::none
);

// ──────────────────────────────────────── Vertex ────────────────────────────

vertex VertexOut vs_ui(
    VertexIn             in         [[stage_in]],
    constant float4x4&  projection [[buffer(1)]]
) {
    VertexOut out;
    out.clipPosition = projection * float4(in.position, 0.0, 1.0);
    out.color        = in.color;
    out.uv           = in.uv;
    out.mode         = in.mode;
    return out;
}

// ──────────────────────────────────────── Fragment ──────────────────────────

fragment float4 fs_ui(
    VertexOut              in        [[stage_in]],
    texture2d<float>       fontAtlas [[texture(0)]]
) {
    if (in.mode > 1.5) {
        // Mode 2: Full texture render (viewport). RGBA8Unorm — no swizzle needed.
        return fontAtlas.sample(kFontSampler, in.uv);
    }
    else if (in.mode > 0.5) {
        // Mode 1: Font atlas with coverage
        float coverage = fontAtlas.sample(kFontSampler, in.uv).r;
        if (coverage < 0.01) discard_fragment();
        return float4(in.color.rgb, in.color.a * coverage);
    }

    // Mode 0: Solid geometry
    return in.color;
}
