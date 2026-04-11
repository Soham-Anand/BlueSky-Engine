// ──────────────────────────────────────────────────────────────────────────────
// DirectX 9 HLSL UI Shader
// Matches simple_ui.metal functionality
// ──────────────────────────────────────────────────────────────────────────────

float4x4 projection : register(c0);
sampler2D fontAtlas : register(s0);

struct VS_INPUT {
    float2 position : POSITION;
    float4 color    : COLOR0;
    float2 uv       : TEXCOORD0;
    float  mode     : TEXCOORD1;
};

struct VS_OUTPUT {
    float4 position : POSITION;
    float4 color    : COLOR0;
    float2 uv       : TEXCOORD0;
    float  mode     : TEXCOORD1;
};

VS_OUTPUT vs_ui(VS_INPUT input) {
    VS_OUTPUT output;
    output.position = mul(projection, float4(input.position, 0.0, 1.0));
    output.color = input.color;
    output.uv = input.uv;
    output.mode = input.mode;
    return output;
}

float4 fs_ui(VS_OUTPUT input) : COLOR0 {
    if (input.mode > 0.5) {
        float coverage = tex2D(fontAtlas, input.uv).r;
        if (coverage < 0.01) discard;
        return float4(input.color.rgb, input.color.a * coverage);
    }
    return input.color;
}
