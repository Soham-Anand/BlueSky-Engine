float4x4 mvp : register(c0);

struct VS_INPUT {
    float3 position : POSITION;
    float4 color    : COLOR0;
};

struct VS_OUTPUT {
    float4 position : POSITION;
    float4 color    : COLOR0;
};

VS_OUTPUT vs_main(VS_INPUT input) {
    VS_OUTPUT output;
    output.position = mul(float4(input.position, 1.0f), mvp);
    output.color = input.color;
    return output;
}

float4 ps_main(VS_OUTPUT input) : COLOR0 {
    return input.color;
}
