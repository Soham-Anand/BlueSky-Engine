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

cbuffer ParticleUniforms : register(b0)
{
    float4x4 viewProjection;
    float3 cameraUp;
    float atlasCols;
    float3 cameraRight;
    float atlasRows;
};

// Slot 0: quad vertices (per-vertex), Slot 1: particle instances (per-instance)
struct VSInput
{
    // Quad
    float2 quadPos : POSITION0;
    
    // Particle
    float3 instPosition : POSITION1;
    float instSize : TEXCOORD0;
    float3 instVelocity : NORMAL0;
    float instRotation : TEXCOORD1;
    float4 instColor : COLOR0;
    float instLife : BLENDWEIGHT0;
    float instMaxLife : BLENDWEIGHT1;
};

struct PSInput
{
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
    float4 color : COLOR0;
};

Texture2D particleAtlas : register(t0);
SamplerState atlasSampler : register(s0);

PSInput VSMain(VSInput input)
{
    PSInput output;
    
    // Discard dead particles
    if (input.instLife <= 0)
    {
        output.position = float4(0, 0, 0, 0); // Need proper degenerate tri
        output.uv = float2(0,0);
        output.color = float4(0,0,0,0);
        return output;
    }
    
    // Billboard computation
    float3 pos = input.instPosition;
    
    // Rotate quad
    float c = cos(input.instRotation);
    float s = sin(input.instRotation);
    
    float2 rotQuadPos;
    rotQuadPos.x = input.quadPos.x * c - input.quadPos.y * s;
    rotQuadPos.y = input.quadPos.x * s + input.quadPos.y * c;
    
    // Scale and position
    pos += cameraRight * rotQuadPos.x * input.instSize;
    pos += cameraUp * rotQuadPos.y * input.instSize;
    
    output.position = mul(float4(pos, 1.0), viewProjection);
    
    // Base UV
    output.uv = input.quadPos + 0.5; // [-0.5, 0.5] -> [0, 1]
    
    output.color = input.instColor;
    
    return output;
}

float4 PSMain(PSInput input) : SV_TARGET
{
    float4 texColor = particleAtlas.Sample(atlasSampler, input.uv);
    return input.color * texColor;
}
