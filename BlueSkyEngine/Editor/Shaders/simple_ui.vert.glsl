#version 450

layout(set = 0, binding = 1) uniform Uniforms
{
    mat4 projection;
} ubo;

layout(location = 0) in vec2 inPosition;
layout(location = 1) in vec4 inColor;
layout(location = 2) in vec2 inUv;
layout(location = 3) in float inMode;

layout(location = 0) out vec4 outColor;
layout(location = 1) out vec2 outUv;
layout(location = 2) out float outMode;

void main()
{
    gl_Position = ubo.projection * vec4(inPosition, 0.0, 1.0);
    outColor = inColor;
    outUv = inUv;
    outMode = inMode;
}
