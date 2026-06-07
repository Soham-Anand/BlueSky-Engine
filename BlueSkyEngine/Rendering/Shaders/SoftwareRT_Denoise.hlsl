// Software Ray Tracing - Temporal Denoising Shader
// Accumulates samples over time and applies edge-aware blur
// Critical for making 0.25-1 ray/pixel look good!

Texture2D<float4> CurrentFrame : register(t0);
Texture2D<float4> HistoryFrame : register(t1);
Texture2D<float4> NormalTexture : register(t2);
Texture2D<float> DepthTexture : register(t3);

RWTexture2D<float4> OutputTexture : register(u0);
RWTexture2D<float4> NewHistoryTexture : register(u1);

cbuffer DenoiseParams : register(b0)
{
    uint FrameIndex;
    uint TemporalSamples;
    float BlurRadius;
    float NormalThreshold;
    float DepthThreshold;
    uint _padding1;
    uint _padding2;
    uint _padding3;
};

// Temporal accumulation weight
float GetTemporalWeight()
{
    // Exponential moving average
    return 1.0 / float(min(FrameIndex + 1, TemporalSamples));
}

// Edge-aware bilateral blur
float4 BilateralBlur(uint2 pixelCoord, float4 centerColor, float3 centerNormal, float centerDepth)
{
    float4 result = centerColor;
    float totalWeight = 1.0;
    
    int radius = int(BlurRadius);
    
    for (int y = -radius; y <= radius; y++)
    {
        for (int x = -radius; x <= radius; x++)
        {
            if (x == 0 && y == 0)
                continue;
            
            int2 sampleCoord = int2(pixelCoord) + int2(x, y);
            
            // Bounds check
            uint width, height;
            CurrentFrame.GetDimensions(width, height);
            if (sampleCoord.x < 0 || sampleCoord.x >= int(width) ||
                sampleCoord.y < 0 || sampleCoord.y >= int(height))
                continue;
            
            // Load sample
            float4 sampleColor = CurrentFrame[sampleCoord];
            float3 sampleNormal = NormalTexture[sampleCoord].xyz * 2.0 - 1.0;
            float sampleDepth = DepthTexture[sampleCoord];
            
            // Compute weights
            float normalWeight = pow(max(0.0, dot(centerNormal, sampleNormal)), 32.0);
            float depthWeight = exp(-abs(centerDepth - sampleDepth) / DepthThreshold);
            float spatialWeight = exp(-(x*x + y*y) / (BlurRadius * BlurRadius));
            
            float weight = normalWeight * depthWeight * spatialWeight;
            
            result += sampleColor * weight;
            totalWeight += weight;
        }
    }
    
    return result / totalWeight;
}

[numthreads(8, 8, 1)]
void main(uint3 dispatchThreadID : SV_DispatchThreadID)
{
    uint2 pixelCoord = dispatchThreadID.xy;
    
    // Get dimensions
    uint width, height;
    CurrentFrame.GetDimensions(width, height);
    
    if (pixelCoord.x >= width || pixelCoord.y >= height)
        return;
    
    // Load current frame
    float4 currentColor = CurrentFrame[pixelCoord];
    float3 normal = NormalTexture[pixelCoord].xyz * 2.0 - 1.0;
    float depth = DepthTexture[pixelCoord];
    
    // Apply bilateral blur
    float4 blurredColor = BilateralBlur(pixelCoord, currentColor, normal, depth);
    
    // Temporal accumulation
    float4 historyColor = HistoryFrame[pixelCoord];
    float temporalWeight = GetTemporalWeight();
    
    float4 finalColor = lerp(historyColor, blurredColor, temporalWeight);
    
    // Write outputs
    OutputTexture[pixelCoord] = finalColor;
    NewHistoryTexture[pixelCoord] = finalColor;
}
