// Frustum Culling Compute Shader
// Performs frustum, distance, and small object culling on GPU
// Runs at 64 threads per group for optimal occupancy

// Culling parameters
cbuffer CullingParams : register(b0)
{
    float4x4 ViewMatrix;
    float4x4 ProjMatrix;
    float4x4 ViewProjMatrix;
    float4 CameraPosition;
    
    // Frustum planes (Ax + By + Cz + D = 0)
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

// Instance data
struct GPUInstance
{
    float4x4 Transform;
    float4 BoundingSphere;  // xyz = center, w = radius
    uint MeshId;
    uint MaterialId;
    uint LODLevel;
    uint Flags;
};

// Input/Output buffers
StructuredBuffer<GPUInstance> Instances : register(t0);
RWStructuredBuffer<uint> Visibility : register(u0);

// Test sphere against frustum plane
bool TestSphereAgainstPlane(float3 center, float radius, float4 plane)
{
    float distance = dot(float4(center, 1.0), plane);
    return distance >= -radius;
}

// Test sphere against all frustum planes
bool FrustumCull(float3 center, float radius)
{
    // If sphere is outside any plane, it's culled
    if (!TestSphereAgainstPlane(center, radius, FrustumLeft))   return false;
    if (!TestSphereAgainstPlane(center, radius, FrustumRight))  return false;
    if (!TestSphereAgainstPlane(center, radius, FrustumBottom)) return false;
    if (!TestSphereAgainstPlane(center, radius, FrustumTop))    return false;
    if (!TestSphereAgainstPlane(center, radius, FrustumNear))   return false;
    if (!TestSphereAgainstPlane(center, radius, FrustumFar))    return false;
    
    return true; // Visible
}

// Distance culling
bool DistanceCull(float3 center)
{
    float distance = length(center - CameraPosition.xyz);
    return distance <= DrawDistance;
}

// Small object culling - cull objects that are too small on screen
bool SmallObjectCull(float3 center, float radius)
{
    float distance = length(center - CameraPosition.xyz);
    
    // Calculate screen-space size (approximate)
    // screenSize = (radius * screenHeight) / (distance * tan(fov/2))
    // For simplicity, use a conservative estimate
    float screenSize = (radius * ScreenHeight) / (distance + 0.001);
    
    return screenSize >= SmallObjectThreshold;
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
    
    // Load instance data
    GPUInstance instance = Instances[instanceIndex];
    
    // Transform bounding sphere to world space
    float3 worldCenter = mul(float4(instance.BoundingSphere.xyz, 1.0), instance.Transform).xyz;
    float worldRadius = instance.BoundingSphere.w;
    
    // Perform culling tests
    bool visible = true;
    
    // Frustum culling
    if (!FrustumCull(worldCenter, worldRadius))
        visible = false;
    
    // Distance culling
    if (visible && !DistanceCull(worldCenter))
        visible = false;
    
    // Small object culling
    if (visible && !SmallObjectCull(worldCenter, worldRadius))
        visible = false;
    
    // Write visibility result
    Visibility[instanceIndex] = visible ? 1 : 0;
}
