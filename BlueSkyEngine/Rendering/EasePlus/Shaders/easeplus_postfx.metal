#include <metal_stdlib>
using namespace metal;

struct PostFXUniforms {
    float2 ScreenSize;
    float2 InvScreenSize;
    float  Time;
    float  FXAAThreshold;
    float  FilmGrainIntensity;
    float  VignetteIntensity;
};

struct VertexOutput {
    float4 position [[position]];
    float2 uv;
};

vertex VertexOutput easeplus_vs_postfx(uint vertexID [[vertex_id]]) {
    VertexOutput out;
    out.uv = float2((vertexID << 1) & 2, vertexID & 2);
    out.position = float4(out.uv * float2(2, -2) + float2(-1, 1), 0, 1);
    return out;
}

float Luminance(float3 c) { return dot(c, float3(0.299, 0.587, 0.114)); }

float3 FXAA(float2 uv, texture2d<float> SceneColor, sampler linearSamp, float2 invScreenSize, float threshold) {
    float3 center = SceneColor.sample(linearSamp, uv).rgb;
    float lumC = Luminance(center);
    
    float lumN = Luminance(SceneColor.sample(linearSamp, uv + float2(0, -invScreenSize.y)).rgb);
    float lumS = Luminance(SceneColor.sample(linearSamp, uv + float2(0,  invScreenSize.y)).rgb);
    float lumW = Luminance(SceneColor.sample(linearSamp, uv + float2(-invScreenSize.x, 0)).rgb);
    float lumE = Luminance(SceneColor.sample(linearSamp, uv + float2( invScreenSize.x, 0)).rgb);
    
    float lumMax = max(max(lumN, lumS), max(lumW, lumE));
    float lumMin = min(min(lumN, lumS), min(lumW, lumE));
    float contrast = lumMax - lumMin;
    
    if (contrast < threshold) return center;
    
    float horizontal = abs(lumN + lumS - 2.0 * lumC);
    float vertical   = abs(lumW + lumE - 2.0 * lumC);
    bool isHorizontal = horizontal > vertical;
    
    float2 blendDir = isHorizontal ? float2(invScreenSize.x, 0) : float2(0, invScreenSize.y);
    
    float3 blend1 = SceneColor.sample(linearSamp, uv + blendDir).rgb;
    float3 blend2 = SceneColor.sample(linearSamp, uv - blendDir).rgb;
    
    return mix(center, (blend1 + blend2) * 0.5, 0.5);
}

float BlueNoise(float2 co) {
    return fract(52.9829189 * fract(0.06711056 * co.x + 0.00583715 * co.y));
}

float Hash(float2 p) {
    // Cheaper 2D hash (saves ALU cycles over 3D hash)
    return fract(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
}

// ── CONSTEXPR SAMPLERS ───────────────────────────────────────────────────────
constexpr sampler linearSamp(coord::normalized, filter::linear, address::clamp_to_edge);

fragment float4 easeplus_fs_postfx(VertexOutput in [[stage_in]],
                                   constant PostFXUniforms& uniforms [[buffer(0)]],
                                   texture2d<float> SceneColor [[texture(0)]]) {
    float2 uv = in.uv;
    
    // FXAA
    float3 color = FXAA(uv, SceneColor, linearSamp, uniforms.InvScreenSize, uniforms.FXAAThreshold);
    
    // Dithering
    float noise = BlueNoise(in.position.xy + uniforms.Time * 100.0);
    color += (noise - 0.5) / 255.0;
    
    // Film Grain
    float grain = Hash(uv * uniforms.ScreenSize + uniforms.Time * 73.0) * 2.0 - 1.0;
    color += grain * uniforms.FilmGrainIntensity;
    
    // Vignette
    float2 vigUV = uv * (1.0 - uv);
    float vig = vigUV.x * vigUV.y * 15.0;
    vig = pow(saturate(vig), uniforms.VignetteIntensity);
    color *= vig;
    
    return float4(saturate(color), 1.0);
}
