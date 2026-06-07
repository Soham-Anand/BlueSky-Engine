// Compact Indirect Args Compute Shader
// Builds DrawIndexedIndirectCommand structs from visible instances
// Uses atomic operations to build compact draw list

struct GPUInstance
{
    float4x4 Transform;
    float4 BoundingSphere;
    uint MeshId;
    uint MaterialId;
    uint LODLevel;
    uint Flags;
};

struct DrawIndexedIndirectCommand
{
    uint IndexCount;
    uint InstanceCount;
    uint FirstIndex;
    int VertexOffset;
    uint FirstInstance;
};

StructuredBuffer<GPUInstance> Instances : register(t0);
StructuredBuffer<uint> Visibility : register(t1);
RWStructuredBuffer<DrawIndexedIndirectCommand> IndirectArgs : register(u0);
RWStructuredBuffer<uint> DrawCount : register(u1);

// Mesh database (would be populated from asset system)
// For now, assume all instances use the same mesh
static const uint INDICES_PER_MESH = 36; // Cube has 36 indices

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
    
    // Check if instance is visible
    if (Visibility[instanceIndex] == 0)
        return;
    
    // Load instance data
    GPUInstance instance = Instances[instanceIndex];
    
    // Atomically increment draw count and get index
    uint drawIndex;
    InterlockedAdd(DrawCount[0], 1, drawIndex);
    
    // Build indirect draw command
    // For now, we create one draw call per visible instance
    // In a real implementation, we'd batch by mesh/material
    
    DrawIndexedIndirectCommand cmd;
    cmd.IndexCount = INDICES_PER_MESH;
    cmd.InstanceCount = 1;
    cmd.FirstIndex = 0;
    cmd.VertexOffset = 0;
    cmd.FirstInstance = instanceIndex; // Pass instance ID to vertex shader
    
    IndirectArgs[drawIndex] = cmd;
}
