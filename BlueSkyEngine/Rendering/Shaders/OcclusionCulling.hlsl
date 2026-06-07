// Occlusion Culling Compute Shader
// Tests instances against Hi-Z buffer for occlusion
// Uses hierarchical depth buffer for fast conservative tests

cbuffer CullingParams : register(b0)
{
    float4x4 ViewMatrix;
    float4x4 ProjMatrix;
    float4x4 ViewProjMatrix;
    float4 CameraPosition;
    
    float4 FrustumLeft;
    float4 FrustumRight;
    float4 FrustumBottom;
    float4 FrustumTop;
    float4 FrustumNear;
    float4 FrustumFar;
    
    float NearPlane;
    float FarPlane;
    float DrawDistance;
    float SmallObjectThreshold;
    uint ScreenWidth;
    uint ScreenHeight;
    uint _padding1;
    uint _padding2;
};

struct GPUInstance
{
    float4x4 Transform;
    float4 BoundingSphere;
    uint MeshId;
    uint MaterialId;
    uint LODLevel;
    uint Flags;
};

StructuredBuffer<GPUInstance> Instances : register(t0);
RWStructuredBuffer<uint> Visibility : register(u0);
Texture2D<float> HiZBuffer : register(t1);  // Hierarchical depth buffer
SamplerState HiZSampler : register(s0);

// Project sphere to screen space and get depth range
void ProjectSphere(float3 center, float radius, out float2 screenMin, out float2 screenMax, out float depthMin, out float depthMax)
{
    // Transform to clip space
    float4 clipCenter = mul(float4(center, 1.0), ViewProjMatrix);
    
    // Perspective divide
    float3 ndcCenter = clipCenter.xyz / clipCenter.w;
    
    // Calculate screen-space bounding box
    // Conservative: project sphere as axis-aligned box
    float radiusNDC = radius / clipCenter.w;
    
    screenMin = (ndcCenter.xy - radiusNDC) * 0.5 + 0.5;
    screenMax = (ndcCenter.xy + radiusNDC) * 0.5 + 0.5;
    
    // Depth range
    depthMin = max(0.0, ndcCenter.z - radiusNDC);
    depthMax = min(1.0, ndcCenter.z + radiusNDC);
}

// Sample Hi-Z buffer at appropriate mip level
float SampleHiZ(float2 uv, float mipLevel)
{
    return HiZBuffer.SampleLevel(HiZSampler, uv, mipLevel);
}

// Occlusion test using Hi-Z buffer
bool OcclusionTest(float3 center, float radius)
{
    float2 screenMin, screenMax;
    float depthMin, depthMax;
    
    ProjectSphere(center, radius, screenMin, screenMax, depthMin, depthMax);
    
    // Clamp to screen bounds
    screenMin = max(screenMin, float2(0.0, 0.0));
    screenMax = min(screenMax, float2(1.0, 1.0));
    
    // Calculate appropriate mip level based on screen-space size
    float2 screenSize = (screenMax - screenMin) * float2(ScreenWidth, ScreenHeight);
    float maxSize = max(screenSize.x, screenSize.y);
    float mipLevel = max(0.0, log2(maxSize));
    
    // Sample Hi-Z at 4 corners (conservative test)
    float depth00 = SampleHiZ(screenMin, mipLevel);
    float depth01 = SampleHiZ(float2(screenMin.x, screenMax.y), mipLevel);
    float depth10 = SampleHiZ(float2(screenMax.x, screenMin.y), mipLevel);
    float depth11 = SampleHiZ(screenMax, mipLevel);
    
    // Get maximum depth (furthest point)
    float maxDepth = max(max(depth00, depth01), max(depth10, depth11));
    
    // If object is behind existing geometry, it's occluded
    return depthMin <= maxDepth;
}

[numthreads(64, 1, 1)]
void main(uint3 dispatchThreadID : SV_DispatchThreadID)
{
    uint instanceIndex = dispatchThreadID.x;
    
    // Bounds check
    uint instanceCount;
    uint stride;
    Instances.GetDimensions(instanceCount, stride);
    
    if (instanceIndex >= instanceCount)
        return;
    
    // Only test instances that passed frustum culling
    if (Visibility[instanceIndex] == 0)
        return;
    
    // Load instance data
    GPUInstance instance = Instances[instanceIndex];
    
    // Transform bounding sphere to world space
    float3 worldCenter = mul(float4(instance.BoundingSphere.xyz, 1.0), instance.Transform).xyz;
    float worldRadius = instance.BoundingSphere.w;
    
    // Perform occlusion test
    bool visible = OcclusionTest(worldCenter, worldRadius);
    
    // Update visibility (only cull, don't un-cull)
    if (!visible)
        Visibility[instanceIndex] = 0;
}
