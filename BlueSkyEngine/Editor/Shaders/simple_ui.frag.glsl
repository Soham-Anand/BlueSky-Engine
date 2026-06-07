#version 450

layout(set = 0, binding = 0) uniform sampler2D fontAtlasTex;

layout(location = 0) in vec4 inColor;
layout(location = 1) in vec2 inUv;
layout(location = 2) in float inMode;

layout(location = 0) out vec4 outColor;

void main()
{
    if (inMode > 1.5)
    {
        outColor = texture(fontAtlasTex, inUv);
    }
    else if (inMode > 0.5)
    {
        float coverage = texture(fontAtlasTex, inUv).r;
        if (coverage < 0.01)
            discard;
        outColor = vec4(inColor.rgb, inColor.a * coverage);
    }
    else
    {
        outColor = inColor;
    }
}
