// ═══════════════════════════════════════════════════════════════════════════════
// Ease+ Pre-Pass Shader — Thin G-Buffer (Depth + Normal)
// ═══════════════════════════════════════════════════════════════════════════════
// Writes ONLY view-space normals + roughness + metallic.
// Depth is written by the hardware depth buffer.
// No albedo, no lighting — pure geometry pass for minimal bandwidth.
// Target: SM 4.0 (DX10 / Intel HD 3000)
// ═══════════════════════════════════════════════════════════════════════════════

cbuffer ViewUniforms : register(b10)
{
    float4x4 View;
    float4x4 Proj;
    float4x4 ViewProj;
    float4x4 InvViewProj;
    float4   CameraPos;      // xyz=pos, w=time
    float2   ScreenSize;
    float    NearPlane;
    float    FarPlane;
    float3   SunDirection;
    float    SunIntensity;
    float3   SunColor;
    int      TilesX;
};

cbuffer ObjectUniforms : register(b11)
{
    float4x4 Model;
    float4   AlbedoColor;
    float    Metallic;
    float    Roughness;
    float    AO;
    float    Emission;
    int      UseAlbedoTex;
    int      UseNormalTex;
    int      UseRMATex;
    int      UseInstanceBuffer;
    int      InstanceBase;
    int      _pad0;
    int      _pad1;
    int      _pad2;
};

cbuffer InstanceUniforms : register(b12)
{
    float4x4 InstanceModels[1024];
};

// ── Vertex Input/Output ──────────────────────────────────────────────────────

struct VS_INPUT
{
    float3 position : POSITION;
    float3 normal   : NORMAL;
    float2 uv       : TEXCOORD0;
};

struct VS_OUTPUT
{
    float4 position   : SV_Position;
    float3 worldNormal : TEXCOORD0;
};

// ── Vertex Shader ────────────────────────────────────────────────────────────

VS_OUTPUT easeplus_vs_prepass(VS_INPUT input, uint instanceID : SV_InstanceID)
{
    VS_OUTPUT output;
    float4x4 model = UseInstanceBuffer != 0 ? InstanceModels[InstanceBase + (int)instanceID] : Model;
    
    float4 worldPos = mul(model, float4(input.position, 1.0));
    output.position = mul(ViewProj, worldPos);
    
    // Store full world-space normal. The old XY-only view-space encoding lost
    // the Z sign and made deferred lighting unstable on imported meshes.
    output.worldNormal = normalize(mul((float3x3)model, input.normal));
    
    return output;
}

// Output: RGBA8
//   RG = Octahedron encoded normal
//   B  = Roughness
//   A  = Metallic

float2 EncodeNormal(float3 n)
{
    n /= (abs(n.x) + abs(n.y) + abs(n.z) + 0.0001);
    float2 enc = n.z >= 0.0 ? n.xy : (1.0 - abs(n.yx)) * (n.xy >= 0.0 ? 1.0 : -1.0);
    return enc * 0.5 + 0.5;
}

float4 easeplus_fs_prepass(VS_OUTPUT input) : SV_Target0
{
    float3 N = normalize(input.worldNormal);
    float2 enc = EncodeNormal(N);
    
    return float4(enc.x, enc.y, Roughness, Metallic);
}

// ── Masked Variant (for foliage/fences) ──────────────────────────────────────

struct VS_MASKED_OUTPUT
{
    float4 position   : SV_Position;
    float3 worldNormal : TEXCOORD0;
    float2 uv         : TEXCOORD1;
};

Texture2D    AlphaMask   : register(t0);
SamplerState MaskSampler : register(s0);

VS_MASKED_OUTPUT easeplus_vs_prepass_masked(VS_INPUT input, uint instanceID : SV_InstanceID)
{
    VS_MASKED_OUTPUT output;
    float4x4 model = UseInstanceBuffer != 0 ? InstanceModels[InstanceBase + (int)instanceID] : Model;
    
    float4 worldPos = mul(model, float4(input.position, 1.0));
    output.position = mul(ViewProj, worldPos);
    output.worldNormal = normalize(mul((float3x3)model, input.normal));
    output.uv = input.uv;
    
    return output;
}

float4 easeplus_fs_prepass_masked(VS_MASKED_OUTPUT input) : SV_Target0
{
    float alpha = AlphaMask.Sample(MaskSampler, input.uv).a;
    clip(alpha - 0.5); // Alpha test — discard transparent pixels
    
    float3 N = normalize(input.worldNormal);
    float2 enc = EncodeNormal(N);
    return float4(enc.x, enc.y, Roughness, Metallic);
}
