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

struct SimulationParams
{
    float deltaTime;
    float3 wind;
    float gravity;
    uint particleCount;
};

kernel void simulate_particles(
    device Particle* particles [[buffer(0)]],
    constant SimulationParams& params [[buffer(1)]],
    uint id [[thread_position_in_grid]])
{
    if (id >= params.particleCount)
        return;
        
    Particle p = particles[id];
    
    if (p.life <= 0)
        return;
        
    p.life -= params.deltaTime;
    
    if (p.life > 0)
    {
        // Apply physics
        p.velocity += float3(0, -9.81 * params.gravity, 0) * params.deltaTime;
        p.velocity += params.wind * params.deltaTime;
        
        // Drag
        p.velocity *= (1.0 - params.deltaTime);
        
        p.position += p.velocity * params.deltaTime;
        
        // Update color alpha based on life
        float lifeNorm = p.life / p.maxLife;
        p.color.a *= lifeNorm;
    }
    
    particles[id] = p;
}
