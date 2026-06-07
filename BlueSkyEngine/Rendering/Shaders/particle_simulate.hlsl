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

RWStructuredBuffer<Particle> Particles : register(u0);

cbuffer SimulationParams : register(b0)
{
    float deltaTime;
    float3 wind;
    float gravity;
    uint particleCount;
};

[numthreads(256, 1, 1)]
void main(uint3 DTid : SV_DispatchThreadID)
{
    uint id = DTid.x;
    
    if (id >= particleCount)
        return;
        
    Particle p = Particles[id];
    
    if (p.life <= 0)
        return;
        
    p.life -= deltaTime;
    
    if (p.life > 0)
    {
        // Apply physics
        p.velocity += float3(0, -9.81 * gravity, 0) * deltaTime;
        p.velocity += wind * deltaTime;
        
        // Drag
        p.velocity *= (1.0 - deltaTime);
        
        p.position += p.velocity * deltaTime;
        
        // Update color alpha based on life
        float lifeNorm = p.life / p.maxLife;
        p.color.a *= lifeNorm; // simple fade out
    }
    
    Particles[id] = p;
}
