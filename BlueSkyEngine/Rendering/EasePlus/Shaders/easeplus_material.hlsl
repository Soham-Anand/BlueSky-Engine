// ═══════════════════════════════════════════════════════════════════════════════
// Ease+ Material Pass — Forward PBR Reconstruction
// ═══════════════════════════════════════════════════════════════════════════════
// Re-renders geometry with depth-test=Equal (zero overdraw).
// Samples the half-res light buffer with bilateral upsample.
// Applies material albedo to produce final lit color.
// ═══════════════════════════════════════════════════════════════════════════════

cbuffer ViewUniforms : register(b10)
{
    float4x4 View, Proj, ViewProj, InvViewProj;
    float4   CameraPos;
    float2   ScreenSize;
    float    NearPlane, FarPlane;
    float3   SunDirection;
    float    SunIntensity;
    float3   SunColor;
    int      TilesX;
};

cbuffer ObjectUniforms : register(b11)
{
    float4x4 Model;
    float4   AlbedoColor;
    float    Metallic, Roughness, AO, Emission;
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

Texture2D    LightBuffer  : register(t0);
Texture2D    DepthBuffer  : register(t1);
Texture2D    AlbedoTex    : register(t2);
Texture2D    NormalTex    : register(t3);
Texture2D    RmaTex       : register(t4);
SamplerState LinearSamp   : register(s0);
SamplerState PointSamp    : register(s1);

struct VS_INPUT  { float3 pos : POSITION; float3 nrm : NORMAL; float2 uv : TEXCOORD0; };
struct VS_OUTPUT { float4 pos : SV_Position; float3 normal : TEXCOORD0; float3 worldPos : TEXCOORD1; float2 uv : TEXCOORD2; };

VS_OUTPUT easeplus_vs_material(VS_INPUT input, uint instanceID : SV_InstanceID)
{
    VS_OUTPUT o;
    float4x4 model = UseInstanceBuffer != 0 ? InstanceModels[InstanceBase + (int)instanceID] : Model;
    float4 wp = mul(model, float4(input.pos, 1.0));
    o.pos = mul(ViewProj, wp);
    o.worldPos = wp.xyz;
    o.normal = normalize(mul((float3x3)model, input.nrm));
    o.uv = input.uv;
    return o;
}

float3 BilateralSampleLight(float2 screenUV, float centerDepth)
{
    float2 halfSize = ScreenSize * 0.5;
    float2 texelSize = 1.0 / halfSize;
    
    return LightBuffer.SampleLevel(LinearSamp, screenUV, 0).rgb * 5.0;
}

// ── ENVIRONMENT BRDF & SKY ───────────────────────────────────────────────────
float2 EnvBRDFApprox(float roughness, float NdotV) {
    float4 c0 = float4(-1.0, -0.0275, -0.572, 0.022);
    float4 c1 = float4(1.0, 0.0425, 1.04, -0.04);
    float4 r = roughness * c0 + c1;
    float a004 = min(r.x * r.x, exp2(-9.28 * NdotV)) * r.x + r.y;
    float2 AB = float2(-1.04, 1.04) * a004 + r.zw;
    return AB;
}

float3 sampleSkyEnv(float3 dir, float3 sunDir, float roughness) {
    float horizonAngle = abs(dir.y);
    float3 zenith  = float3(0.08, 0.26, 0.56);
    float3 horizon = float3(0.52, 0.72, 0.92);
    float3 sky = lerp(horizon, zenith, pow(saturate(dir.y), 0.6));
    
    float cosTheta = dot(dir, normalize(sunDir));
    float sunPower = exp2(10.0 * (1.0 - roughness) + 1.0);
    float sunGlow = pow(saturate(cosTheta), sunPower) * (1.05 / (1.0 + roughness * 6.0));
    sky += float3(1.0, 0.93, 0.78) * sunGlow;
    
    float horizonStrip = exp(-horizonAngle * lerp(40.0, 15.0, roughness));
    float secondaryStrip = exp(-abs(dir.y - 0.2) * 20.0) * 0.3;
    sky += float3(0.95, 0.98, 1.0) * (horizonStrip + secondaryStrip) * (0.35 * (1.0 - roughness));
    
    float3 ground = float3(0.065, 0.070, 0.075);
    sky = lerp(ground, sky, smoothstep(-0.08, 0.18, dir.y));
    
    float3 avgIrradiance = float3(0.24, 0.28, 0.34);
    sky = lerp(sky, avgIrradiance, roughness * roughness * 0.78);
    return max(sky, 0.0);
}

float4 easeplus_fs_material(VS_OUTPUT input) : SV_Target0
{
    float2 screenUV = input.pos.xy / ScreenSize;
    float  depth = input.pos.z;
    
    // Sample light buffer with bilateral upsample
    float3 lighting = BilateralSampleLight(screenUV, depth);
    
    // Apply material
    float3 albedo = AlbedoColor.rgb;
    if (UseAlbedoTex != 0)
        albedo *= AlbedoTex.Sample(LinearSamp, input.uv).rgb;

    float roughness = clamp(Roughness, 0.08, 0.95);
    float metallic = saturate(Metallic);
    float ao = max(AO, 0.15);

    if (UseRMATex != 0)
    {
        float3 rma = RmaTex.Sample(LinearSamp, input.uv).rgb;
        roughness = clamp(rma.r, 0.08, 0.95);
        metallic = saturate(rma.g);
        ao = max(rma.b, 0.35);
    }

    float3 N = normalize(input.normal);
    if (UseNormalTex != 0)
    {
        float3 tangentNormal = NormalTex.Sample(LinearSamp, input.uv).rgb * 2.0 - 1.0;
        float3 up = abs(N.y) < 0.999 ? float3(0, 1, 0) : float3(1, 0, 0);
        float3 T = normalize(cross(up, N));
        float3 B = cross(N, T);
        N = normalize(T * tangentNormal.x + B * tangentNormal.y + N * tangentNormal.z);
    }

    float3 V = normalize(CameraPos.xyz - input.worldPos);
    float NdotV = saturate(dot(N, V));
    
    float3 L = normalize(SunDirection); // SunDirection already points TOWARD sun
    float NdotL = saturate(dot(N, L));
    float wrap = saturate(dot(N, L) * 0.5 + 0.5);
    float skyMix = saturate(N.y * 0.5 + 0.5);
    
    float sunStrength = clamp(SunIntensity, 1.0, 4.5);
    float3 skyBounce = lerp(float3(0.07, 0.06, 0.05), float3(0.34, 0.42, 0.56), skyMix);
    float3 wrappedSun = SunColor * sunStrength * (NdotL * 0.48 + wrap * 0.16);
    float3 minimumLighting = skyBounce + wrappedSun;
    lighting = max(lighting, minimumLighting);
    
    float diffuseKeep = lerp(1.0, 0.38, metallic);
    float3 color = albedo * lighting * ao * diffuseKeep;
    
    float3 H = normalize(V + L);
    float NdotH = max(dot(N, H), 0.0);
    float a = roughness * roughness;
    float a2 = a * a;
    float denom = NdotH * NdotH * (a2 - 1.0) + 1.0;
    float D = a2 / (3.14159 * denom * denom + 0.0001);
    
    float k = (roughness + 1.0) * (roughness + 1.0) * 0.125;
    float G = (NdotV / (NdotV * (1.0 - k) + k)) * (NdotL / (NdotL * (1.0 - k) + k));
    
    float3 F0 = lerp(float3(0.04, 0.04, 0.04), albedo, metallic);
    float HdotV = max(dot(H, V), 0.0);
    float3 F = F0 + (1.0 - F0) * pow(1.0 - HdotV, 5.0);
    
    float3 sunSpec = (D * G * F) / (4.0 * max(NdotV, 0.001) * max(NdotL, 0.001) + 0.0001);
    color += sunSpec * SunColor * sunStrength * NdotL * ao;

    float3 R = reflect(-V, N);
    float horizonAO = saturate(R.y * 0.5 + 0.5);
    horizonAO = horizonAO * horizonAO;
    
    float3 envColor = sampleSkyEnv(R, SunDirection, roughness);
    float2 envBRDF = EnvBRDFApprox(roughness, NdotV);
    float3 specularColor = F0 * envBRDF.x + envBRDF.y;
    
    float3 energyComp = 1.0 + F0 * (1.0 / envBRDF.x - 1.0);
    specularColor *= energyComp;
    
    color += envColor * specularColor * ao * horizonAO;
    
    if (roughness < 0.2 && metallic > 0.5) {
        float3 ccR = reflect(-V, input.normal);
        float3 ccEnv = sampleSkyEnv(ccR, SunDirection, 0.02);
        float ccFresnel = 0.04 + 0.96 * pow(1.0 - NdotV, 5.0);
        color += ccEnv * ccFresnel * ao * saturate(ccR.y * 0.5 + 0.5);
    }
    
    float rim = 1.0 - NdotV;
    rim = smoothstep(0.6, 1.0, rim) * saturate(NdotL + 0.2);
    color += SunColor * rim * 0.15 * ao;
    
    // Add emission
    color += albedo * Emission;
    
    // ACES tonemapping (inline for one fewer pass on HD 3000)
    color = color * (2.51 * color + 0.03) / (color * (2.43 * color + 0.59) + 0.14);
    
    // Gamma correction
    color = pow(saturate(color), 1.0 / 2.2);
    
    return float4(color, AlbedoColor.a);
}
