// Software Ray Tracing - Ray Generation Shader
// Generates primary rays from camera for each pixel
// Supports checkerboard rendering and temporal jitter

cbuffer CameraParams : register(b0)
{
    float4x4 ViewMatrix;
    float4x4 ProjMatrix;
    float4x4 InvViewMatrix;
    float4x4 InvProjMatrix;
    float4 CameraPosition;
    uint ScreenWidth;
    uint ScreenHeight;
    uint FrameIndex;
    uint TemporalSampleIndex;
};

struct Ray
{
    float3 Origin;
    float TMin;
    float3 Direction;
    float TMax;
};

RWStructuredBuffer<Ray> Rays : register(u0);

// Halton sequence for temporal jitter (better than random)
float Halton(uint index, uint base)
{
    float result = 0.0;
    float f = 1.0;
    uint i = index;
    
    while (i > 0)
    {
        f = f / float(base);
        result = result + f * float(i % base);
        i = i / base;
    }
    
    return result;
}

// Generate jittered pixel coordinate for temporal anti-aliasing
float2 GetJitteredPixelCoord(uint2 pixelCoord)
{
    // Use Halton sequence for low-discrepancy sampling
    float jitterX = Halton(FrameIndex, 2) - 0.5;
    float jitterY = Halton(FrameIndex, 3) - 0.5;
    
    return float2(pixelCoord) + float2(jitterX, jitterY);
}

// Generate ray from camera through pixel
Ray GenerateCameraRay(uint2 pixelCoord)
{
    // Get jittered pixel coordinate
    float2 jitteredCoord = GetJitteredPixelCoord(pixelCoord);
    
    // Convert to NDC space [-1, 1]
    float2 ndc;
    ndc.x = (jitteredCoord.x / float(ScreenWidth)) * 2.0 - 1.0;
    ndc.y = 1.0 - (jitteredCoord.y / float(ScreenHeight)) * 2.0; // Flip Y
    
    // Unproject to view space
    float4 rayClip = float4(ndc, -1.0, 1.0);
    float4 rayView = mul(rayClip, InvProjMatrix);
    rayView = float4(rayView.xy, -1.0, 0.0);
    
    // Transform to world space
    float4 rayWorld = mul(rayView, InvViewMatrix);
    float3 rayDir = normalize(rayWorld.xyz);
    
    Ray ray;
    ray.Origin = CameraPosition.xyz;
    ray.Direction = rayDir;
    ray.TMin = 0.001;
    ray.TMax = 10000.0;
    
    return ray;
}

[numthreads(8, 8, 1)]
void main(uint3 dispatchThreadID : SV_DispatchThreadID)
{
    uint2 pixelCoord = dispatchThreadID.xy;
    
    // Bounds check
    if (pixelCoord.x >= ScreenWidth || pixelCoord.y >= ScreenHeight)
        return;
    
    // Checkerboard rendering (optional - controlled by TemporalSampleIndex)
    // Only generate rays for half the pixels each frame
    bool checkerboard = (TemporalSampleIndex % 2) == 1;
    if (checkerboard)
    {
        bool evenPixel = ((pixelCoord.x + pixelCoord.y) % 2) == 0;
        bool oddFrame = (FrameIndex % 2) == 1;
        
        if (evenPixel != oddFrame)
            return; // Skip this pixel this frame
    }
    
    // Generate ray
    Ray ray = GenerateCameraRay(pixelCoord);
    
    // Write to buffer
    uint rayIndex = pixelCoord.y * ScreenWidth + pixelCoord.x;
    Rays[rayIndex] = ray;
}
