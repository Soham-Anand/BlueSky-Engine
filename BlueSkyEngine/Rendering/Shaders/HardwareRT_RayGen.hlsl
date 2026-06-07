// BlueSkyEngine - Hardware Ray Tracing: Ray Generation Shader
//
// RAYGEN SHADER (DXR/Vulkan RT/Metal RT)
// ======================================
// Generates primary rays from camera
// One ray per pixel (or less for performance)

// Ray payload structure
struct RayPayload
{
    float3 color;
    float distance;
    float3 normal;
    uint hitType; // 0 = miss, 1 = hit, 2 = shadow
};

// Camera parameters
cbuffer CameraParams : register(b0)
{
    float4x4 viewMatrix;
    float4x4 projMatrix;
    float4x4 invViewMatrix;
    float4x4 invProjMatrix;
    float3 cameraPosition;
    uint frameIndex;
    uint2 screenSize;
    uint temporalSampleIndex;
};

// Acceleration structure
RaytracingAccelerationStructure scene : register(t0);

// Output texture
RWTexture2D<float4> outputTexture : register(u0);

// Generate ray from pixel coordinates
void GenerateRay(uint2 pixelCoord, out float3 origin, out float3 direction)
{
    // Convert pixel to NDC [-1, 1]
    float2 ndc = (float2(pixelCoord) + 0.5f) / float2(screenSize) * 2.0f - 1.0f;
    ndc.y = -ndc.y; // Flip Y
    
    // Temporal jitter (Halton sequence for temporal AA)
    float2 jitter = float2(0, 0);
    if (temporalSampleIndex > 0)
    {
        // Simple Halton sequence
        float u = frac((float)temporalSampleIndex * 0.5f);
        float v = frac((float)temporalSampleIndex * 0.333333f);
        jitter = (float2(u, v) - 0.5f) / float2(screenSize);
    }
    ndc += jitter;
    
    // Unproject to world space
    float4 target = mul(invProjMatrix, float4(ndc, 1.0f, 1.0f));
    target /= target.w;
    
    float4 worldTarget = mul(invViewMatrix, target);
    
    origin = cameraPosition;
    direction = normalize(worldTarget.xyz - origin);
}

[shader("raygeneration")]
void RayGenMain()
{
    // Get pixel coordinates
    uint2 pixelCoord = DispatchRaysIndex().xy;
    
    // Generate ray
    float3 origin, direction;
    GenerateRay(pixelCoord, origin, direction);
    
    // Setup ray descriptor
    RayDesc ray;
    ray.Origin = origin;
    ray.Direction = direction;
    ray.TMin = 0.001f;
    ray.TMax = 10000.0f;
    
    // Trace ray
    RayPayload payload;
    payload.color = float3(0, 0, 0);
    payload.distance = 0;
    payload.normal = float3(0, 0, 0);
    payload.hitType = 0;
    
    TraceRay(
        scene,                  // Acceleration structure
        RAY_FLAG_NONE,          // Ray flags
        0xFF,                   // Instance mask
        0,                      // Ray contribution to hit group index
        1,                      // Multiplier for geometry contribution
        0,                      // Miss shader index
        ray,                    // Ray descriptor
        payload                 // Ray payload
    );
    
    // Write output
    outputTexture[pixelCoord] = float4(payload.color, 1.0f);
}
