#include <metal_stdlib>
using namespace metal;

struct HighlightUniforms
{
    float4x4 viewProjection;
    float4x4 modelMatrix;
    float4 highlightColor;
    float outlineScale;
    float3 _padding;
};

struct VertexIn
{
    float3 position [[attribute(0)]];
    float3 normal [[attribute(1)]];
};

struct VertexOut
{
    float4 position [[position]];
    float4 color;
};

vertex VertexOut vertex_main(
    VertexIn in [[stage_in]],
    constant HighlightUniforms& uniforms [[buffer(1)]])
{
    VertexOut out;
    
    float3 expandedPos = in.position + (in.normal * (uniforms.outlineScale - 1.0));
    
    float4 worldPos = uniforms.modelMatrix * float4(expandedPos, 1.0);
    out.position = uniforms.viewProjection * worldPos;
    
    out.color = uniforms.highlightColor;
    
    return out;
}

fragment float4 fragment_main(VertexOut in [[stage_in]])
{
    return in.color;
}
