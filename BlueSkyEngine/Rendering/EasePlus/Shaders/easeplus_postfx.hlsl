// ═══════════════════════════════════════════════════════════════════════════════
// Ease+ Post-FX — FXAA + Blue Noise Dithering
// ═══════════════════════════════════════════════════════════════════════════════
// Lightweight post-processing designed for integrated GPUs.
// FXAA: ~0.5ms morphological anti-aliasing (no MSAA overhead)
// Blue Noise: 8-bit ordered dithering to hide banding from shared memory
// ═══════════════════════════════════════════════════════════════════════════════

Texture2D    SceneColor  : register(t0);
SamplerState LinearSamp  : register(s0);

cbuffer PostFXUniforms : register(b0)
{
    float2 ScreenSize;
    float2 InvScreenSize;
    float  Time;
    float  FXAAThreshold;
    float  FilmGrainIntensity;
    float  VignetteIntensity;
};

struct VS_OUTPUT { float4 pos : SV_Position; float2 uv : TEXCOORD0; };

VS_OUTPUT easeplus_vs_postfx(uint vid : SV_VertexID)
{
    VS_OUTPUT o;
    o.uv = float2((vid << 1) & 2, vid & 2);
    o.pos = float4(o.uv * float2(2, -2) + float2(-1, 1), 0, 1);
    return o;
}

// ── FXAA (Simplified for SM 4.0) ─────────────────────────────────────────────

float Luminance(float3 c) { return dot(c, float3(0.299, 0.587, 0.114)); }

float3 FXAA(float2 uv)
{
    float3 center = SceneColor.Sample(LinearSamp, uv).rgb;
    float  lumC = Luminance(center);
    
    float lumN = Luminance(SceneColor.Sample(LinearSamp, uv + float2(0, -InvScreenSize.y)).rgb);
    float lumS = Luminance(SceneColor.Sample(LinearSamp, uv + float2(0,  InvScreenSize.y)).rgb);
    float lumW = Luminance(SceneColor.Sample(LinearSamp, uv + float2(-InvScreenSize.x, 0)).rgb);
    float lumE = Luminance(SceneColor.Sample(LinearSamp, uv + float2( InvScreenSize.x, 0)).rgb);
    
    float lumMax = max(max(lumN, lumS), max(lumW, lumE));
    float lumMin = min(min(lumN, lumS), min(lumW, lumE));
    float contrast = lumMax - lumMin;
    
    if (contrast < FXAAThreshold) return center;
    
    // Edge direction
    float horizontal = abs(lumN + lumS - 2.0 * lumC);
    float vertical   = abs(lumW + lumE - 2.0 * lumC);
    bool isHorizontal = horizontal > vertical;
    
    float2 blendDir = isHorizontal ? float2(InvScreenSize.x, 0) : float2(0, InvScreenSize.y);
    
    float3 blend1 = SceneColor.Sample(LinearSamp, uv + blendDir).rgb;
    float3 blend2 = SceneColor.Sample(LinearSamp, uv - blendDir).rgb;
    
    return lerp(center, (blend1 + blend2) * 0.5, 0.5);
}

// ── Blue Noise Dithering ─────────────────────────────────────────────────────
// Hides banding artifacts from low-precision shared memory on iGPUs

float BlueNoise(float2 co)
{
    // Interleaved gradient noise (Jorge Jimenez, 2014)
    return frac(52.9829189 * frac(0.06711056 * co.x + 0.00583715 * co.y));
}

// ── Film Grain ───────────────────────────────────────────────────────────────

float Hash(float2 p)
{
    // Cheaper 2D hash (saves ALU cycles over 3D hash)
    return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
}

float4 easeplus_fs_postfx(VS_OUTPUT input) : SV_Target0
{
    float2 uv = input.uv;
    
    // FXAA
    float3 color = FXAA(uv);
    
    // Blue noise dithering (±0.5/255 amplitude — invisible but kills banding)
    float noise = BlueNoise(input.pos.xy + Time * 100.0);
    color += (noise - 0.5) / 255.0;
    
    // Film grain (very subtle)
    float grain = Hash(uv * ScreenSize + Time * 73.0) * 2.0 - 1.0;
    color += grain * FilmGrainIntensity;
    
    // Vignette
    float2 vigUV = uv * (1.0 - uv);
    float vig = vigUV.x * vigUV.y * 15.0;
    vig = pow(saturate(vig), VignetteIntensity);
    color *= vig;
    
    return float4(saturate(color), 1.0);
}
