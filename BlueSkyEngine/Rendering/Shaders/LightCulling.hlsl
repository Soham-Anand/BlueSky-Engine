// Light Culling Compute Shader
// Culls lights against cluster AABBs for Forward+ rendering
// Compatible with DX11+, Vulkan, Metal

cbuffer CullParams : register(b0)
{
    float4x4 ViewMatrix;
    uint LightCount;
    uint MaxLightsPerCluster;
    uint _padding1;
    uint _padding2;
};

struct ClusterAABB
{
    float3 Min;
    float _pad1;
    float3 Max;
    float _pad2;
};

struct GPULight
{
    float4 PositionAndType; // xyz = position, w = type
    float4 DirectionAndRange; // xyz = direction, w = range
    float4 ColorAndIntensity; // xyz = color, w = intensity
    float4 SpotAngles; // x = inner cos, y = outer cos, z = attenuation, w = cast shadows
};

struct LightGrid
{
    uint Offset;
    uint Count;
};

StructuredBuffer<ClusterAABB> ClusterAABBs : register(t0);
StructuredBuffer<GPULight> Lights : register(t1);
RWStructuredBuffer<LightGrid> LightGrids : register(u0);
RWStructuredBuffer<uint> LightIndexList : register(u1);

groupshared uint SharedLightCount;
groupshared uint SharedLightIndices[256];
groupshared uint SharedGlobalOffset;

bool TestSphereAABB(float3 center, float radius, float3 aabbMin, float3 aabbMax)
{
    float3 closest = clamp(center, aabbMin, aabbMax);
    float distSq = dot(center - closest, center - closest);
    return distSq <= radius * radius;
}

bool TestConeAABB(float3 position, float3 direction, float range, float cosInner, float cosOuter, 
                  float3 aabbMin, float3 aabbMax)
{
    // Simplified: test sphere first
    if (!TestSphereAABB(position, range, aabbMin, aabbMax))
        return false;
    
    // Test cone against AABB center
    float3 center = (aabbMin + aabbMax) * 0.5;
    float3 toCenter = normalize(center - position);
    float angle = dot(toCenter, direction);
    
    return angle >= cosOuter;
}

[numthreads(64, 1, 1)]
void main(uint3 GTid : SV_GroupThreadID, uint3 Gid : SV_GroupID)
{
    uint clusterIndex = Gid.x;
    uint threadIndex = GTid.x;
    
    // Initialize shared memory
    if (threadIndex == 0)
    {
        SharedLightCount = 0;
    }
    GroupMemoryBarrierWithGroupSync();
    
    // Load cluster AABB
    ClusterAABB cluster = ClusterAABBs[clusterIndex];
    
    // Each thread tests a subset of lights
    for (uint lightIndex = threadIndex; lightIndex < LightCount; lightIndex += 64)
    {
        GPULight light = Lights[lightIndex];
        
        // Transform light position to view space
        float3 lightPosView = mul(float4(light.PositionAndType.xyz, 1.0), ViewMatrix).xyz;
        float3 lightDirView = mul(float4(light.DirectionAndRange.xyz, 0.0), ViewMatrix).xyz;
        
        uint lightType = (uint)light.PositionAndType.w;
        float range = light.DirectionAndRange.w;
        
        bool intersects = false;
        
        // Test light against cluster
        if (lightType == 0) // Directional
        {
            intersects = true;
        }
        else if (lightType == 1) // Point
        {
            intersects = TestSphereAABB(lightPosView, range, cluster.Min, cluster.Max);
        }
        else if (lightType == 2) // Spot
        {
            float cosInner = light.SpotAngles.x;
            float cosOuter = light.SpotAngles.y;
            intersects = TestConeAABB(lightPosView, lightDirView, range, cosInner, cosOuter,
                                     cluster.Min, cluster.Max);
        }
        
        // Add to shared list if intersects
        if (intersects)
        {
            uint index;
            InterlockedAdd(SharedLightCount, 1, index);
            
            if (index < MaxLightsPerCluster)
            {
                SharedLightIndices[index] = lightIndex;
            }
        }
    }
    
    GroupMemoryBarrierWithGroupSync();
    
    // Allocate space in global light index list
    if (threadIndex == 0)
    {
        uint count = min(SharedLightCount, MaxLightsPerCluster);
        InterlockedAdd(LightIndexList[0], count, SharedGlobalOffset);
        
        LightGrids[clusterIndex].Offset = SharedGlobalOffset;
        LightGrids[clusterIndex].Count = count;
    }
    
    GroupMemoryBarrierWithGroupSync();
    
    // Write light indices to global list
    uint lightCount = min(SharedLightCount, MaxLightsPerCluster);
    for (uint i = threadIndex; i < lightCount; i += 64)
    {
        LightIndexList[SharedGlobalOffset + i + 1] = SharedLightIndices[i];
    }
}
