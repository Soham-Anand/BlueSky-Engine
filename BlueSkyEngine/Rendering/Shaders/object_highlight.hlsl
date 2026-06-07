cbuffer HighlightUniforms : register(b0)
{
    float4x4 viewProjection;
    float4x4 modelMatrix;
    float4 highlightColor;
    float outlineScale;
    float3 _padding;
};

struct VSInput
{
    float3 position : POSITION;
    float3 normal : NORMAL;
};

struct PSInput
{
    float4 position : SV_POSITION;
    float4 color : COLOR;
};

PSInput VSMain(VSInput input)
{
    PSInput output;
    
    // Scale position along normal for outline
    float3 expandedPos = input.position + (input.normal * (outlineScale - 1.0));
    
    float4 worldPos = mul(float4(expandedPos, 1.0), modelMatrix);
    output.position = mul(worldPos, viewProjection);
    
    output.color = highlightColor;
    
    return output;
}

float4 PSMain(PSInput input) : SV_TARGET
{
    // A more advanced version would compute a glow falloff here
    return input.color;
}
