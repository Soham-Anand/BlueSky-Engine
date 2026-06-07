// ═══════════════════════════════════════════════════════════════════════════════
// Ease+ Lighting Pass — Half-Res Tiled PBR + SH Global Illumination
// ═══════════════════════════════════════════════════════════════════════════════
// Fullscreen pass at HALF resolution. For each pixel:
//   1. Reconstruct world position from depth
//   2. Unpack normal from G-buffer
//   3. Evaluate per-tile light list (PBR: GGX specular + Lambert diffuse)
//   4. Add SH probe GI for indirect bounce light
//   5. Output accumulated lighting to RGBA16F buffer
//
// This is the "Beast" — where all the math magic happens.
// Target: SM 4.0 (DX10 / Intel HD 3000)
// ═══════════════════════════════════════════════════════════════════════════════

cbuffer ViewUniforms : register(b10)
{
    float4x4 View;
    float4x4 Proj;
    float4x4 ViewProj;
    float4x4 InvViewProj;
    float4   CameraPos;
    float2   ScreenSize;
    float    NearPlane;
    float    FarPlane;
    float3   SunDirection;
    float    SunIntensity;
    float3   SunColor;
    int      TilesX;
};

// Per-tile light indices (7 lights max per tile)
cbuffer TileLightData : register(b1)
{
    // Each tile is two int4s:
    // [lightCount, l0, l1, l2] [l3, l4, l5, l6]
    int4 TileIndices[8192];
};

// Light array
struct LightData
{
    float3 Position;
    float  Range;
    float3 Color;
    float  Intensity;
    float3 Direction;
    float  SpotAngle;
};

cbuffer LightBuffer : register(b2)
{
    LightData Lights[128];
};

// SH Probe grid
cbuffer SHProbeData : register(b3)
{
    float4 SHCoeffs[256 * 3]; // 9 coefficients × 3 RGB packed as float4s
};

// G-Buffer textures
Texture2D    GBufferNormal : register(t0);
Texture2D    GBufferDepth  : register(t1);
SamplerState PointSampler  : register(s0);

// ── Fullscreen Vertex Shader ─────────────────────────────────────────────────

struct VS_FULLSCREEN_OUTPUT
{
    float4 position : SV_Position;
    float2 uv       : TEXCOORD0;
};

VS_FULLSCREEN_OUTPUT easeplus_vs_fullscreen(uint vertexID : SV_VertexID)
{
    VS_FULLSCREEN_OUTPUT output;
    
    // Fullscreen triangle trick (no vertex buffer needed)
    output.uv = float2((vertexID << 1) & 2, vertexID & 2);
    output.position = float4(output.uv * float2(2, -2) + float2(-1, 1), 0, 1);
    
    return output;
}

// ── PBR Functions ────────────────────────────────────────────────────────────

static const float PI = 3.14159265359;

// GGX Normal Distribution Function
float DistributionGGX(float3 N, float3 H, float roughness)
{
    float a  = roughness * roughness;
    float a2 = a * a;
    float NdotH = max(dot(N, H), 0.0);
    float denom = NdotH * NdotH * (a2 - 1.0) + 1.0;
    return a2 / (PI * denom * denom + 0.0001);
}

// Schlick-GGX Geometry Function
float GeometrySchlickGGX(float NdotV, float roughness)
{
    float k = (roughness + 1.0) * (roughness + 1.0) / 8.0;
    return NdotV / (NdotV * (1.0 - k) + k);
}

float GeometrySmith(float3 N, float3 V, float3 L, float roughness)
{
    return GeometrySchlickGGX(max(dot(N, V), 0.0), roughness)
         * GeometrySchlickGGX(max(dot(N, L), 0.0), roughness);
}

// Fresnel-Schlick
float3 FresnelSchlick(float cosTheta, float3 F0)
{
    return F0 + (1.0 - F0) * pow(saturate(1.0 - cosTheta), 5.0);
}

// ── World Position Reconstruction ────────────────────────────────────────────

float3 ReconstructWorldPos(float2 uv, float depth)
{
    // NDC position
    float4 ndc = float4(uv * 2.0 - 1.0, depth, 1.0);
    ndc.y = -ndc.y; // Flip Y for D3D
    
    // Unproject to world space
    float4 worldPos = mul(InvViewProj, ndc);
    return worldPos.xyz / worldPos.w;
}

float3 DecodeNormal(float2 f)
{
    f = f * 2.0 - 1.0;
    float3 n = float3(f.x, f.y, 1.0 - abs(f.x) - abs(f.y));
    float t = saturate(-n.z);
    n.xy += n.xy >= 0.0 ? -t : t;
    return normalize(n);
}

// ── Fragment Shader (The Beast) ──────────────────────────────────────────────

float4 easeplus_fs_lighting(VS_FULLSCREEN_OUTPUT input) : SV_Target0
{
    float2 uv = input.uv;
    
    // Sample G-buffer
    float4 gbufferData = GBufferNormal.Sample(PointSampler, uv);
    float  depth       = GBufferDepth.Sample(PointSampler, uv).r;
    
    // Early out for sky pixels
    if (depth >= 1.0) return float4(0, 0, 0, 0);
    
    // Unpack Octahedron normal, roughness, metallic
    float3 N = DecodeNormal(gbufferData.xy);
    float roughness = clamp(gbufferData.z, 0.08, 0.95);
    float metallic  = saturate(gbufferData.w);
    
    // Reconstruct world position from depth
    float3 worldPos = ReconstructWorldPos(uv, depth);
    float3 V = normalize(CameraPos.xyz - worldPos);
    
    // Base reflectance (dielectric = 0.04, metallic = albedo)
    float3 F0 = lerp(float3(0.04, 0.04, 0.04), float3(1, 1, 1), metallic);
    
    // ── Direct Sun Light ─────────────────────────────────────────────────
    float3 L = normalize(SunDirection); // SunDirection already points TOWARD sun
    float3 H = normalize(V + L);
    float NdotL = max(dot(N, L), 0.0);
    
    float  D = DistributionGGX(N, H, roughness);
    float  G = GeometrySmith(N, V, L, roughness);
    float3 F = FresnelSchlick(max(dot(H, V), 0.0), F0);
    
    float3 specular = (D * G * F) / (4.0 * max(dot(N, V), 0.0) * NdotL + 0.0001);
    float3 kD = (1.0 - F) * (1.0 - metallic);
    
    float3 sunContrib = (kD / PI + specular) * SunColor * SunIntensity * NdotL;
    
    // ── Per-Tile Local Lights ────────────────────────────────────────────
    float3 localLight = float3(0, 0, 0);
    
    // Determine which tile this pixel belongs to
    int2 pixelCoord = int2(uv * ScreenSize);
    int tileX = pixelCoord.x / 16;
    int tileY = pixelCoord.y / 16;
    int tileIdx = tileY * TilesX + tileX;
    
    tileIdx = clamp(tileIdx, 0, 4095);
    int4 tileA = TileIndices[tileIdx * 2];
    int4 tileB = TileIndices[tileIdx * 2 + 1];
    int tileLightCount = clamp(tileA.x, 0, 7);

    for (int slot = 0; slot < tileLightCount; slot++)
    {
        int li = slot == 0 ? tileA.y :
                 slot == 1 ? tileA.z :
                 slot == 2 ? tileA.w :
                 slot == 3 ? tileB.x :
                 slot == 4 ? tileB.y :
                 slot == 5 ? tileB.z : tileB.w;
        if (li < 0 || li >= 128) continue;

        LightData light = Lights[li];
        if (light.Intensity <= 0) continue;
        
        float3 lightVec = light.Position - worldPos;
        float dist = length(lightVec);
        
        if (dist > light.Range) continue;
        
        float3 Ll = lightVec / dist;
        float3 Hl = normalize(V + Ll);
        float NdotLl = max(dot(N, Ll), 0.0);
        
        // Attenuation (smooth falloff)
        float atten = 1.0 - saturate(dist / max(light.Range, 0.001));
        atten = atten * atten * (3.0 - 2.0 * atten);
        
        // Proper GGX for local lights
        float Dl = DistributionGGX(N, Hl, roughness);
        float Gl = GeometrySmith(N, V, Ll, roughness);
        float3 Fl = FresnelSchlick(max(dot(Hl, V), 0.0), F0);
        
        float3 specL = (Dl * Gl * Fl) / (4.0 * max(dot(N, V), 0.0) * NdotLl + 0.0001);
        float3 kDl = (1.0 - Fl) * (1.0 - metallic);
        
        localLight += (kDl / PI + specL) * light.Color * light.Intensity * NdotLl * atten;
    }
    
    // ── SH Global Illumination (ambient bounce) ──────────────────────────
    float skyMix = saturate(N.y * 0.5 + 0.5);
    float groundMix = saturate((-N.y) * 0.5 + 0.5);
    float sideMix = saturate(1.0 - abs(N.y));
    float3 skyBounce = lerp(float3(0.08, 0.12, 0.15), float3(0.34, 0.42, 0.56), skyMix);
    float3 groundBounce = float3(0.12, 0.10, 0.08) * groundMix;
    float3 sideBounce = float3(0.18, 0.20, 0.22) * sideMix;
    float3 giContrib = (skyBounce + groundBounce + sideBounce) * (1.0 - metallic);
    
    // ── Combine and Pack ─────────────────────────────────────────────────
    float3 totalLight = sunContrib + localLight + giContrib;
    float  specIntensity = length(specular);
    
    // Pack into RGBA8Unorm (HDR range 0..5 via x0.20)
    return float4(totalLight * 0.20, saturate(specIntensity));
}
