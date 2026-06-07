// ACES Tonemapping Shader
// Academy Color Encoding System - Industry standard for film and games
// Used in: Uncharted 4, Frostbite, Unreal Engine, Unity HDRP
// Reference: https://github.com/TheRealMJP/BakingLab/blob/master/BakingLab/ACES.hlsl

cbuffer ACESSettings : register(b0)
{
    float Exposure;
    float Contrast;
    float WhitePoint;
    float ToeStrength;
    float ToeLength;
    float ShoulderStrength;
    float ShoulderLength;
    float ShoulderAngle;
};

Texture2D HDRInput : register(t0);
SamplerState LinearSampler : register(s0);

struct VSOutput
{
    float4 Position : SV_POSITION;
    float2 TexCoord : TEXCOORD0;
};

// Fullscreen triangle vertex shader
VSOutput VSMain(uint vertexID : SV_VertexID)
{
    VSOutput output;
    
    // Generate fullscreen triangle
    output.TexCoord = float2((vertexID << 1) & 2, vertexID & 2);
    output.Position = float4(output.TexCoord * float2(2, -2) + float2(-1, 1), 0, 1);
    
    return output;
}

// ============================================================================
// ACES Color Space Conversion Matrices
// ============================================================================

static const float3x3 ACESInputMat = float3x3(
    0.59719, 0.35458, 0.04823,
    0.07600, 0.90834, 0.01566,
    0.02840, 0.13383, 0.83777
);

static const float3x3 ACESOutputMat = float3x3(
     1.60475, -0.53108, -0.07367,
    -0.10208,  1.10813, -0.00605,
    -0.00327, -0.07276,  1.07602
);

// ============================================================================
// ACES RRT (Reference Rendering Transform) and ODT (Output Device Transform)
// ============================================================================

float3 RRTAndODTFit(float3 v)
{
    float3 a = v * (v + 0.0245786) - 0.000090537;
    float3 b = v * (0.983729 * v + 0.4329510) + 0.238081;
    return a / b;
}

float3 ACESFitted(float3 color)
{
    // Apply exposure
    color *= Exposure;
    
    // Transform to ACES color space
    color = mul(ACESInputMat, color);
    
    // Apply RRT and ODT
    color = RRTAndODTFit(color);
    
    // Transform back to sRGB
    color = mul(ACESOutputMat, color);
    
    // Clamp to [0, 1]
    color = saturate(color);
    
    return color;
}

// ============================================================================
// Alternative: Full ACES with adjustable parameters
// ============================================================================

float3 ACESFilm(float3 x)
{
    float a = 2.51;
    float b = 0.03;
    float c = 2.43;
    float d = 0.59;
    float e = 0.14;
    return saturate((x * (a * x + b)) / (x * (c * x + d) + e));
}

// ============================================================================
// Contrast adjustment (applied after tonemapping)
// ============================================================================

float3 ApplyContrast(float3 color, float contrast)
{
    // Adjust contrast around middle gray (0.18 in linear, 0.5 in gamma)
    const float midGray = 0.18;
    return midGray + (color - midGray) * contrast;
}

// ============================================================================
// Pixel Shader
// ============================================================================

float4 PSMain(VSOutput input) : SV_TARGET
{
    // Sample HDR input
    float3 hdrColor = HDRInput.Sample(LinearSampler, input.TexCoord).rgb;
    
    // Apply ACES tonemapping
    float3 ldrColor = ACESFitted(hdrColor);
    
    // Apply contrast adjustment
    if (Contrast != 1.0)
    {
        ldrColor = ApplyContrast(ldrColor, Contrast);
    }
    
    // Output is in sRGB space (hardware will apply gamma correction)
    return float4(ldrColor, 1.0);
}

// ============================================================================
// Alternative Tonemappers (for comparison)
// ============================================================================

// Reinhard tonemapping (simple, but can wash out colors)
float3 Reinhard(float3 color)
{
    return color / (1.0 + color);
}

// Uncharted 2 tonemapping (John Hable)
float3 Uncharted2Tonemap(float3 x)
{
    float A = 0.15; // Shoulder strength
    float B = 0.50; // Linear strength
    float C = 0.10; // Linear angle
    float D = 0.20; // Toe strength
    float E = 0.02; // Toe numerator
    float F = 0.30; // Toe denominator
    
    return ((x * (A * x + C * B) + D * E) / (x * (A * x + B) + D * F)) - E / F;
}

// Filmic tonemapping (Jim Hejl and Richard Burgess-Dawson)
float3 FilmicTonemap(float3 color)
{
    color = max(0, color - 0.004);
    color = (color * (6.2 * color + 0.5)) / (color * (6.2 * color + 1.7) + 0.06);
    return color;
}

// ============================================================================
// Debug visualization
// ============================================================================

float3 DebugLuminance(float3 color)
{
    float lum = dot(color, float3(0.2126, 0.7152, 0.0722));
    
    // Color code by luminance range
    if (lum < 0.1) return float3(0, 0, 1); // Dark blue
    if (lum < 0.5) return float3(0, 1, 0); // Green
    if (lum < 1.0) return float3(1, 1, 0); // Yellow
    if (lum < 2.0) return float3(1, 0.5, 0); // Orange
    return float3(1, 0, 0); // Red (overexposed)
}
