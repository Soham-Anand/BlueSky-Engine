#include <metal_stdlib>
using namespace metal;

struct Particle
{
    float3 position;
    float size;
    
    float3 velocity;
    float rotation;
    
    float4 color;
    
    float life;
    float maxLife;
    float2 _padding;
};

struct ParticleUniforms
{
    float4x4 viewProjection;
    float3 cameraUp;
    float atlasCols;
    float3 cameraRight;
    float atlasRows;
};

struct VertexIn
{
    float2 quadPos [[attribute(0)]];
};

struct VertexOut
{
    float4 position [[position]];
    float2 uv;
    float4 color;
};

vertex VertexOut vertex_main(
    VertexIn in [[stage_in]],
    device Particle* particles [[buffer(1)]],
    constant ParticleUniforms& uniforms [[buffer(2)]],
    uint instanceId [[instance_id]])
{
    VertexOut out;
    
    Particle p = particles[instanceId];
    
    if (p.life <= 0)
    {
        out.position = float4(0);
        out.uv = float2(0);
        out.color = float4(0);
        return out;
    }
    
    float3 pos = p.position;
    
    float c = cos(p.rotation);
    float s = sin(p.rotation);
    
    float2 rotQuadPos;
    rotQuadPos.x = in.quadPos.x * c - in.quadPos.y * s;
    rotQuadPos.y = in.quadPos.x * s + in.quadPos.y * c;
    
    pos += uniforms.cameraRight * rotQuadPos.x * p.size;
    pos += uniforms.cameraUp * rotQuadPos.y * p.size;
    
    out.position = uniforms.viewProjection * float4(pos, 1.0);
    out.uv = in.quadPos + 0.5;
    out.color = p.color;
    
    return out;
}

fragment float4 fragment_main(
    VertexOut in [[stage_in]],
    texture2d<float> particleAtlas [[texture(0)]],
    sampler atlasSampler [[sampler(0)]])
{
    float4 texColor = particleAtlas.sample(atlasSampler, in.uv);
    return in.color * texColor;
}
