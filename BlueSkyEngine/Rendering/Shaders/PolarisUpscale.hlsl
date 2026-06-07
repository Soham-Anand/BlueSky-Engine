// BlueSkyEngine - Project Polaris: GPU Upscaler (SM 4.1)

cbuffer UpscaleParams : register(b0)
{
    float4 InputSize;    // xy = input res, zw = 1 / input res
    float4 OutputSize;   // xy = output res, zw = 1 / output res
    float Sharpness;
    float DepthThreshold;
    float NormalThreshold;
    float Padding;
};

Texture2D<float4> ColorTex  : register(t0);
Texture2D<float>  DepthTex  : register(t1);
Texture2D<float4> NormalTex : register(t2);

SamplerState PointSampler : register(s0);
SamplerState LinearSampler : register(s1);

struct VSInput
{
    float2 Position : POSITION;
    float2 TexCoord : TEXCOORD0;
};

struct PSInput
{
    float4 Position : SV_POSITION;
    float2 TexCoord : TEXCOORD0;
};

PSInput VSMain(VSInput input)
{
    PSInput output;
    output.Position = float4(input.Position, 0.0, 1.0);
    output.TexCoord = input.TexCoord;
    return output;
}

// Catmull-Rom bicubic interpolation weights
float4 CubicWeights(float x)
{
    float x2 = x * x;
    float x3 = x2 * x;
    return float4(
        -0.5 * x3 + x2 - 0.5 * x,
        1.5 * x3 - 2.5 * x2 + 1.0,
        -1.5 * x3 + 2.0 * x2 + 0.5 * x,
        0.5 * x3 - 0.5 * x2
    );
}

float4 SampleBicubic(Texture2D<float4> tex, SamplerState samp, float2 uv, float2 texelSize)
{
    float2 coord = uv / texelSize - 0.5;
    float2 f = frac(coord);
    float2 i = floor(coord);

    float4 wx = CubicWeights(f.x);
    float4 wy = CubicWeights(f.y);

    float4 color = 0;
    for (int y = -1; y <= 2; y++)
    {
        for (int x = -1; x <= 2; x++)
        {
            float weight = wx[x + 1] * wy[y + 1];
            float2 sampleUV = (i + float2(x, y) + 0.5) * texelSize;
            color += tex.SampleLevel(samp, sampleUV, 0) * weight;
        }
    }
    return color;
}

float4 PSMain(PSInput input) : SV_Target
{
    float2 uv = input.TexCoord;
    float2 texelSize = InputSize.zw;
    
    // Sample 3x3 neighborhood for edge detection
    float centerDepth = DepthTex.SampleLevel(PointSampler, uv, 0);
    float3 centerNormal = NormalTex.SampleLevel(PointSampler, uv, 0).xyz;
    
    bool isEdge = false;
    
    // Check neighbors
    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            if (x == 0 && y == 0) continue;
            
            float2 offsetUV = uv + float2(x, y) * texelSize;
            float d = DepthTex.SampleLevel(PointSampler, offsetUV, 0);
            float3 n = NormalTex.SampleLevel(PointSampler, offsetUV, 0).xyz;
            
            if (abs(d - centerDepth) > DepthThreshold || distance(n, centerNormal) > NormalThreshold)
            {
                isEdge = true;
                break;
            }
        }
        if (isEdge) break;
    }
    
    if (isEdge)
    {
        // Edge: Use nearest neighbor to preserve sharpness
        return ColorTex.SampleLevel(PointSampler, uv, 0);
    }
    else
    {
        // Smooth area: Use high-quality bicubic interpolation
        float4 bicubic = SampleBicubic(ColorTex, PointSampler, uv, texelSize);
        // Add a bit of sharpness
        float4 linearColor = ColorTex.SampleLevel(LinearSampler, uv, 0);
        return lerp(linearColor, bicubic, Sharpness);
    }
}
