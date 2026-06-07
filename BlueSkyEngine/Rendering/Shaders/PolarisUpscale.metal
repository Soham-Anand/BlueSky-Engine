#include <metal_stdlib>
using namespace metal;

struct UpscaleParams {
    float4 InputSize;
    float4 OutputSize;
    float Sharpness;
    float DepthThreshold;
    float NormalThreshold;
    float Padding;
};

struct VSInput {
    float2 Position [[attribute(0)]];
    float2 TexCoord [[attribute(1)]];
};

struct VSOutput {
    float4 Position [[position]];
    float2 TexCoord;
};

vertex VSOutput VSMain(VSInput input [[stage_in]]) {
    VSOutput output;
    output.Position = float4(input.Position, 0.0, 1.0);
    output.TexCoord = input.TexCoord;
    return output;
}

float4 CubicWeights(float x) {
    float x2 = x * x;
    float x3 = x2 * x;
    return float4(
        -0.5 * x3 + x2 - 0.5 * x,
        1.5 * x3 - 2.5 * x2 + 1.0,
        -1.5 * x3 + 2.0 * x2 + 0.5 * x,
        0.5 * x3 - 0.5 * x2
    );
}

float4 SampleBicubic(texture2d<float> tex, sampler samp, float2 uv, float2 texelSize) {
    float2 coord = uv / texelSize - 0.5;
    float2 f = fract(coord);
    float2 i = floor(coord);

    float4 wx = CubicWeights(f.x);
    float4 wy = CubicWeights(f.y);

    float4 color = 0;
    for (int y = -1; y <= 2; y++) {
        for (int x = -1; x <= 2; x++) {
            float weight = wx[x + 1] * wy[y + 1];
            float2 sampleUV = (i + float2(x, y) + 0.5) * texelSize;
            color += tex.sample(samp, sampleUV, level(0)) * weight;
        }
    }
    return color;
}

// ACES Fitted Tonemapping
float3 ACESFitted(float3 color) {
    const float3x3 ACESInputMat = float3x3(
        float3(0.59719, 0.07600, 0.02840),
        float3(0.35458, 0.90834, 0.13383),
        float3(0.04823, 0.01566, 0.83777)
    );
    const float3x3 ACESOutputMat = float3x3(
        float3( 1.60475, -0.10208, -0.00327),
        float3(-0.53108,  1.10813, -0.07276),
        float3(-0.07367, -0.00605,  1.07602)
    );
    color = ACESInputMat * color;
    float3 a = color * (color + 0.0245786) - 0.000090537;
    float3 b = color * (0.983729 * color + 0.4329510) + 0.238081;
    color = a / b;
    color = ACESOutputMat * color;
    return saturate(color);
}

fragment float4 PSMain(VSOutput input [[stage_in]],
                       constant UpscaleParams& params [[buffer(0)]],
                       texture2d<float> ColorTex [[texture(0)]],
                       texture2d<float> DepthTex [[texture(1)]],
                       texture2d<float> NormalTex [[texture(2)]]) {
                       
    constexpr sampler PointSampler(coord::normalized, filter::nearest, address::clamp_to_edge);
    constexpr sampler LinearSampler(coord::normalized, filter::linear, address::clamp_to_edge);
    
    float2 uv = input.TexCoord;
    float2 texelSize = params.InputSize.zw;
    
    float centerDepth = DepthTex.sample(PointSampler, uv, level(0)).r;
    float3 centerNormal = NormalTex.sample(PointSampler, uv, level(0)).rgb;
    
    bool isEdge = false;
    
    for (int y = -1; y <= 1; y++) {
        for (int x = -1; x <= 1; x++) {
            if (x == 0 && y == 0) continue;
            
            float2 offsetUV = uv + float2(x, y) * texelSize;
            float d = DepthTex.sample(PointSampler, offsetUV, level(0)).r;
            float3 n = NormalTex.sample(PointSampler, offsetUV, level(0)).rgb;
            
            if (abs(d - centerDepth) > params.DepthThreshold || distance(n, centerNormal) > params.NormalThreshold) {
                isEdge = true;
                break;
            }
        }
        if (isEdge) break;
    }
    
    float3 finalColor;
    if (isEdge) {
        finalColor = ColorTex.sample(PointSampler, uv, level(0)).rgb;
    } else {
        float3 bicubic = SampleBicubic(ColorTex, PointSampler, uv, texelSize).rgb;
        float3 linearColor = ColorTex.sample(LinearSampler, uv, level(0)).rgb;
        finalColor = mix(linearColor, bicubic, params.Sharpness);
    }
    
    // ACES Fitted Tonemapping
    finalColor = ACESFitted(finalColor);
    
    // Warmth color grading
    finalColor *= float3(1.02, 1.0, 0.98);
    
    // sRGB Gamma Correction
    finalColor = pow(finalColor, float3(1.0 / 2.2));
    
    return float4(finalColor, 1.0);
}
