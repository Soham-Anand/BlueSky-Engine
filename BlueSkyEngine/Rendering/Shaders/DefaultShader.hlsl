// DefaultShader.hlsl - BlueSky Engine PBR
// Compiled for DirectX 9/10/11 backends

cbuffer Constants : register(b0)
{
    matrix model;
    matrix view;
    matrix projection;
    
    float3 albedo;
    float metallic;
    float roughness;
    float3 lightPos;
    float3 lightColor;
    float3 viewPos;
};

struct VSInput
{
    float3 Position : POSITION;
    float3 Normal : NORMAL;
    float2 TexCoord : TEXCOORD;
};

struct PSInput
{
    float4 PositionCS : SV_Position;
    float3 FragPos : POSITION;
    float3 Normal : NORMAL;
    float2 TexCoord : TEXCOORD;
};

PSInput VSMain(VSInput input)
{
    PSInput output;
    
    float4 worldPos = mul(model, float4(input.Position, 1.0f));
    output.FragPos = worldPos.xyz;
    
    float3x3 normalMatrix = (float3x3)model; // Assuming uniform scaling for simplicity
    output.Normal = normalize(mul(normalMatrix, input.Normal));
    
    output.TexCoord = input.TexCoord;
    output.PositionCS = mul(projection, mul(view, worldPos));
    
    return output;
}

float4 PSMain(PSInput input) : SV_Target
{
    float3 N = normalize(input.Normal);
    float3 V = normalize(viewPos - input.FragPos);
    
    float3 L = normalize(lightPos - input.FragPos);
    float3 H = normalize(V + L);
    
    float NdotL = max(dot(N, L), 0.0);
    float NdotV = max(dot(N, V), 0.0);
    float NdotH = max(dot(N, H), 0.0);
    float VdotH = max(dot(V, H), 0.0);
    
    float3 F0 = float3(0.04, 0.04, 0.04);
    F0 = lerp(F0, albedo, metallic);
    
    float3 F = F0 + (1.0 - F0) * pow(1.0 - VdotH, 5.0);
    
    float alpha = roughness * roughness;
    float alpha2 = alpha * alpha;
    float k = (alpha + 1) * (alpha + 1) / 8.0;
    
    float NDF = alpha2 / (3.14159265359 * pow(NdotH * NdotH * (alpha2 - 1) + 1, 2));
    float G = NdotL / (NdotL * (1 - k) + k) * NdotV / (NdotV * (1 - k) + k);
    
    float3 numerator = NDF * G * F;
    float denominator = 4.0 * NdotV * NdotL + 0.0001;
    float3 specular = numerator / denominator;
    
    float3 kD = (1.0 - F) * (1.0 - metallic);
    
    float3 Lo = (kD * albedo / 3.14159265359 + specular) * lightColor * NdotL;
    
    float3 ambient = float3(0.03, 0.03, 0.03) * albedo * 0.1;
    float3 finalColor = ambient + Lo;
    
    return float4(finalColor, 1.0);
}
