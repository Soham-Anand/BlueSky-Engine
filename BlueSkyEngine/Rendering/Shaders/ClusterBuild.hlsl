// Cluster Build Compute Shader
// Builds cluster AABBs in view space for Forward+ rendering
// Compatible with DX11+, Vulkan, Metal

cbuffer ClusterParams : register(b0)
{
    float4x4 ViewMatrix;
    float4x4 InvProjMatrix;
    uint ScreenWidth;
    uint ScreenHeight;
    float NearPlane;
    float FarPlane;
    uint ClusterCountX;
    uint ClusterCountY;
    uint ClusterCountZ;
    uint _padding;
};

struct ClusterAABB
{
    float3 Min;
    float _pad1;
    float3 Max;
    float _pad2;
};

RWStructuredBuffer<ClusterAABB> ClusterAABBs : register(u0);

float3 ScreenToView(float2 screenPos, float depth)
{
    float4 ndc = float4(screenPos * 2.0 - 1.0, depth, 1.0);
    float4 viewPos = mul(ndc, InvProjMatrix);
    return viewPos.xyz / viewPos.w;
}

[numthreads(8, 8, 8)]
void main(uint3 DTid : SV_DispatchThreadID)
{
    if (DTid.x >= ClusterCountX || DTid.y >= ClusterCountY || DTid.z >= ClusterCountZ)
        return;
    
    uint clusterIndex = DTid.x + DTid.y * ClusterCountX + DTid.z * ClusterCountX * ClusterCountY;
    
    // Calculate cluster bounds in screen space
    float2 minScreen = float2(DTid.x, DTid.y) / float2(ClusterCountX, ClusterCountY);
    float2 maxScreen = float2(DTid.x + 1, DTid.y + 1) / float2(ClusterCountX, ClusterCountY);
    
    // Exponential depth slicing for better distribution
    float minDepth = NearPlane * pow(FarPlane / NearPlane, float(DTid.z) / float(ClusterCountZ));
    float maxDepth = NearPlane * pow(FarPlane / NearPlane, float(DTid.z + 1) / float(ClusterCountZ));
    
    // Convert to view space
    float3 corners[8];
    corners[0] = ScreenToView(float2(minScreen.x, minScreen.y), minDepth);
    corners[1] = ScreenToView(float2(maxScreen.x, minScreen.y), minDepth);
    corners[2] = ScreenToView(float2(minScreen.x, maxScreen.y), minDepth);
    corners[3] = ScreenToView(float2(maxScreen.x, maxScreen.y), minDepth);
    corners[4] = ScreenToView(float2(minScreen.x, minScreen.y), maxDepth);
    corners[5] = ScreenToView(float2(maxScreen.x, minScreen.y), maxDepth);
    corners[6] = ScreenToView(float2(minScreen.x, maxScreen.y), maxDepth);
    corners[7] = ScreenToView(float2(maxScreen.x, maxScreen.y), maxDepth);
    
    // Calculate AABB
    float3 minAABB = corners[0];
    float3 maxAABB = corners[0];
    
    [unroll]
    for (int i = 1; i < 8; i++)
    {
        minAABB = min(minAABB, corners[i]);
        maxAABB = max(maxAABB, corners[i]);
    }
    
    // Store result
    ClusterAABBs[clusterIndex].Min = minAABB;
    ClusterAABBs[clusterIndex].Max = maxAABB;
}
