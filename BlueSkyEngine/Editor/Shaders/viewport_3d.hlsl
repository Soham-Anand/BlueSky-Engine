// ─────────────────────────────────────────────────────────────────────────────
// DX11 Viewport Shader (SM 4.1)
// Slot layout: b0=LightCount, b1=LightingSettings, b2=AstraSurface, b10=ViewUniforms, b12=EntityUniforms, b13=Lights
// ─────────────────────────────────────────────────────────────────────────────

// ── Slot 10: View Uniforms (384 bytes) ─────────────────────────────────────
// Matches C# ViewUniforms / Metal struct field-for-field (all 16-byte aligned;
// sun is float4 everywhere — cross-API float3 packing is layout Russian roulette).
cbuffer ViewUniforms : register(b10) {
    float4x4 View             : packoffset(c0);   // 64 bytes, offset 0
    float4x4 Proj             : packoffset(c4);   // 64 bytes, offset 64
    float4x4 ViewProj         : packoffset(c8);   // 64 bytes, offset 128
    float4x4 InvViewProj      : packoffset(c12);  // 64 bytes, offset 192
    float4x4 LightSpaceMatrix : packoffset(c16);  // 64 bytes, offset 256
    float4   CameraPos        : packoffset(c20);  // 16 bytes, offset 320
    float    Time             : packoffset(c21.x);// 4 bytes, offset 336
    // c21.y/z/w = 12 bytes padding (matches C# pads)
    float4   SunDirection     : packoffset(c22);  // 16 bytes, offset 352 (xyz = sun dir)
    float4   WindParams       : packoffset(c23);  // 16 bytes, offset 368
};

// ── Slot 2: Surface Data (112 bytes, Astra) ───────────────────────────────
// Matches C# ViewportRenderer.AstraSurface by sequential layout.
// Single global surface — no material system.
cbuffer AstraSurface : register(b2) {
    float4 BaseColor;           // rgb=base, a=draw-time alpha
    float  Roughness;
    float  Metallic;
    float  AO;
    float  EmissiveStrength;
    float  SpecularStrength;
    float  Shininess;
    float  Alpha;
    uint   Flags;               // AstraSurfaceFlags bitfield
    float2 UVScale;
    float2 UVOffset;
    float4 EmissiveColor;
    float4 Custom0;             // x=ReflectionIntensity, y=Anisotropy, z=Sheen, w=Transmission
    float4 Custom1;             // x=Strata feature mask, yzw=linear clearcoat tint
};

// ── Textures / Samplers ────────────────────────────────────────────────────
Texture2D ShadowMap        : register(t1);
SamplerState ShadowSampler : register(s1);

// Strata Tier-1 surface textures (bound per-draw; defaults when absent)
Texture2D AlbedoTex    : register(t2);
Texture2D RMATex       : register(t4);
Texture2D DetailAlbedo : register(t6);
Texture2D DetailNormal : register(t7);
Texture2D GradeLUT : register(t8);
Texture2D BentTex : register(t9); // world-space bent RGB (Ease "world" encoding)
SamplerState SurfSampler : register(s0);

// ── Slot 13: Light data array ─────────────────────────────────────────────
struct LightData {
    float3 Position;
    float  Range;
    float3 Direction;
    float  Intensity;
    float3 Color;
    int    LightType;
    float  InnerAngle;
    float  OuterAngle;
    float  Attenuation;
    int    CastShadows;
    int    Volumetric;
    float  _pad1;
    float  _pad2;
};
cbuffer LightBuffer : register(b13) {
    LightData Lights[64];
};

// ── Slot 0: Light count ───────────────────────────────────────────────────
cbuffer LightCountBuf : register(b0) {
    int LightCount;
};

// ── Slot 1: Lighting settings ────────────────────────────────────────────
// packoffset matches C# LayoutKind.Sequential: 5 ints + float(20) + float4(24)
cbuffer LightingSettingsBuf : register(b1) {
    int   Quality             : packoffset(c0.x);
    int   MaxLights           : packoffset(c0.y);
    int   EnableIBL           : packoffset(c0.z);
    int   EnableVolumetrics   : packoffset(c0.w);
    int   EnableContactShadows: packoffset(c1.x);
    float Exposure            : packoffset(c1.y);
    float4 AmbientPacked      : packoffset(c1.z); // xyz=color, w=intensity (spans c1.zw + c2.xy)
};

// ── Constants ─────────────────────────────────────────────────────────────
static const float PI = 3.14159265359;
static const float INV_PI = 0.31830988618;
static const float DIELECTRIC_F0 = 0.04;
static const int MAX_MESH_INSTANCES = 256;

// ── Astra Surface Flags ────────────────────────────────────────────────
// MUST match AstraSurfaceFlags.cs exactly.
#define ASTRA_TRANSPARENT     (1u<<0)
#define ASTRA_DOUBLESIDED     (1u<<1)
#define ASTRA_RECEIVESHADOW   (1u<<2)
#define ASTRA_CASTSHADOW      (1u<<3)
#define ASTRA_ALPHACLIP       (1u<<4)
#define ASTRA_EMISSIVE        (1u<<5)
#define ASTRA_CLEARCOAT       (1u<<6)
#define ASTRA_VERTEXCOLOR     (1u<<7)
#define ASTRA_REFLECTION      (1u<<8)
#define ASTRA_CUSTOMLIGHTING  (1u<<9)
#define ASTRA_WIND            (1u<<10)
#define ASTRA_DECAL           (1u<<11)
#define ASTRA_TERRAIN         (1u<<12)
#define ASTRA_UI              (1u<<13)
#define ASTRA_DEBUG           (1u<<14)
#define ASTRA_RESERVED        (1u<<15)

// ── Debug View Modes (ASTRA_DEBUG_VIEW) ─────────────────────────────────
#define DEBUG_FINAL      0
#define DEBUG_ALBEDO     1
#define DEBUG_NORMALS    2
#define DEBUG_AO         3
#define DEBUG_METALLIC   4
#define DEBUG_ROUGHNESS  5
#define DEBUG_SPECULAR   6
#define DEBUG_SHADOW     7
#define DEBUG_UV         8
#define DEBUG_FLAGS      9

// ── Slot 17: sky params (SkyPreset float; 0=day, 1=studio) ─────────────────
cbuffer SkyParams : register(b17) {
    float SkyPreset;
};
// ── Slot 16: Strata sky SH probe (9 float4, Y-up basis) ────────────────────
cbuffer SHProbe : register(b16) {
    float4 SH[9];
};

// ── Strata sky-captured diffuse (must match StrataSkyCapture basis order)
float3 SHEval(float3 n) {
    float3 r = SH[0].rgb * 0.282095;
    r += SH[1].rgb * (0.488603 * n.y);
    r += SH[2].rgb * (0.488603 * n.z);
    r += SH[3].rgb * (0.488603 * n.x);
    r += SH[4].rgb * (1.092548 * n.x * n.y);
    r += SH[5].rgb * (1.092548 * n.y * n.z);
    r += SH[6].rgb * (0.315392 * (3.0 * n.y * n.y - 1.0));
    r += SH[7].rgb * (1.092548 * n.x * n.z);
    r += SH[8].rgb * (0.546274 * (n.x * n.x - n.z * n.z));
    return r;
}

// ── Display-referred 16³ LUT as 256×16 strip ──────────────────────────────────
float3 LUTSample(float3 c, Texture2D lut, SamplerState s) {
    c = saturate(c);
    float bx = c.b * 15.0;
    float b0 = floor(bx);
    float f = bx - b0;
    float2 uv0 = float2((b0 * 16.0 + c.r * 15.0 + 0.5) / 256.0, (c.g * 15.0 + 0.5) / 16.0);
    float2 uv1 = float2((min(b0 + 1.0, 15.0) * 16.0 + c.r * 15.0 + 0.5) / 256.0, (c.g * 15.0 + 0.5) / 16.0);
    return lerp(lut.SampleLevel(s, uv0, 0).rgb, lut.SampleLevel(s, uv1, 0).rgb, f);
}

// ═════════════════════════════════════════════════════════════════════════════
// ASTRA BRDF HELPER FUNCTIONS
// ═════════════════════════════════════════════════════════════════════════════

float3 AstraDiffuse(float3 baseColor, float NdotL) {
    return baseColor * saturate(NdotL) * INV_PI;
}

float AstraSpecular(float3 N, float3 H, float shininess, float specularStrength) {
    float NdotH = saturate(dot(N, H));
    return pow(NdotH, shininess) * specularStrength;
}

float3 AstraAmbient(float3 baseColor, float metallic, float NdotV, float shadow) {
    float3 ambientColor = AmbientPacked.rgb;
    float ambientIntensity = AmbientPacked.w;
    float skyFac = NdotV;
    float3 irradiance = lerp(float3(0.18, 0.15, 0.12), float3(0.5, 0.6, 0.8), skyFac);
    return (ambientColor * ambientIntensity + irradiance) * baseColor * (1.0 - metallic);
}

// NOTE: fs_mesh no longer uses AstraAmbient — diffuse ambient comes from the
// sky-captured SH probe (SHEval). Kept for reference until Strata owns all paths.

float3 AstraReflection(float metallic, float3 baseColor) {
    float3 dielectricReflection = DIELECTRIC_F0;
    float3 metallicReflection = baseColor;
    return lerp(dielectricReflection, metallicReflection, metallic);
}

// ── Khronos PBR Neutral (hue-preserving, LDR-safe; mirrors viewport_3d.metal)
float3 PBRNeutral(float3 color) {
    const float startCompression = 0.8 - 0.04;
    const float desaturation = 0.15;
    float x = min(color.r, min(color.g, color.b));
    float offset = x < 0.08 ? x - 6.25 * x * x : 0.04;
    color -= offset;
    float peak = max(color.r, max(color.g, color.b));
    if (peak < startCompression) return color;
    const float d = 1.0 - startCompression;
    float newPeak = 1.0 - d * d / (peak + d - startCompression);
    color *= newPeak / max(peak, 1e-6);
    float g = 1.0 - 1.0 / (desaturation * (peak - newPeak) + 1.0);
    return lerp(color, float3(newPeak, newPeak, newPeak), g);
}

// ── Exact sRGB output encoding (linear → sRGB, piecewise)
float3 LinearToSRGB(float3 c) {
    c = saturate(c);
    float3 low = c * 12.92;
    float3 high = 1.055 * pow(c, float3(1.0 / 2.4, 1.0 / 2.4, 1.0 / 2.4)) - 0.055;
    return lerp(low, high, step(float3(0.0031308, 0.0031308, 0.0031308), c));
}

// ═════════════════════════════════════════════════════════════════════════════
// SKY
// ═════════════════════════════════════════════════════════════════════════════
struct VS_SKY_OUTPUT {
    float4 position : SV_POSITION;
    float2 uv       : TEXCOORD0;
    float3 rayDir   : TEXCOORD1;
};

VS_SKY_OUTPUT vs_sky(uint vertexID : SV_VertexID) {
    VS_SKY_OUTPUT output;
    
    float2 uv = float2((vertexID << 1) & 2, vertexID & 2);
    output.position = float4(uv * 2.0 - 1.0, 0.9999, 1.0);
    output.uv = uv;

    float4 clipPos = float4(uv * 2.0 - 1.0, 1.0, 1.0);
    float4 worldPosH = mul(InvViewProj, clipPos);
    float3 worldPos = worldPosH.xyz / worldPosH.w;
    output.rayDir = worldPos - CameraPos.xyz;

    return output;
}

float hash(float n) {
    return frac(sin(n) * 43758.5453123);
}

float noise(float2 x) {
    float2 p = floor(x);
    float2 f = frac(x);
    f = f * f * (3.0 - 2.0 * f);
    float n = p.x + p.y * 57.0;
    return lerp(lerp(hash(n), hash(n + 1.0), f.x),
               lerp(hash(n + 57.0), hash(n + 58.0), f.x), f.y);
}

float fbm(float2 x) {
    float v = 0.0;
    float a = 0.5;
    float2 shift = float2(100.0, 100.0);
    for (int i = 0; i < 5; i++) {
        v += a * noise(x);
        x = x * 2.0 + shift;
        a *= 0.5;
    }
    return v;
}

float3 computeSky(float3 rayDir, float3 sunDir, float time) {
    float height = rayDir.y;
    
    float3 deepBlue = float3(0.1, 0.35, 0.75);     
    float3 midBlue = float3(0.25, 0.55, 0.9);      
    float3 lightBlue = float3(0.5, 0.75, 0.98);    
    float3 horizonColor = float3(0.7, 0.85, 1.0); 
    
    float t = saturate(height * 0.5 + 0.5);
    
    float3 skyColor;
    if (t > 0.5) {
        float upperBlend = smoothstep(0.5, 1.0, t);
        skyColor = lerp(midBlue, deepBlue, upperBlend);
    } else {
        float lowerBlend = smoothstep(0.0, 0.5, t);
        skyColor = lerp(horizonColor, lightBlue, lowerBlend);
        skyColor = lerp(skyColor, midBlue, lowerBlend * lowerBlend);
    }
    
    float2 cloudUV = rayDir.xz * 1.5 + float2(time * 0.01, time * 0.005);
    float cloudNoise = fbm(cloudUV);
    float cloudDetail = fbm(cloudUV * 2.5) * 0.4;
    float cloudShape = cloudNoise + cloudDetail * 0.3;
    
    float cloudDensity = smoothstep(0.5, 0.7, cloudShape);
    float cloudEdge = smoothstep(0.4, 0.6, cloudShape) - cloudDensity;
    
    float sunDot = dot(rayDir, sunDir);
    float cloudBrightness = smoothstep(-0.3, 0.8, sunDot);
    
    float3 cloudLit = float3(1.0, 1.0, 0.98);
    float3 cloudShadow = float3(0.75, 0.8, 0.88);
    float3 cloudColor = lerp(cloudShadow, cloudLit, cloudBrightness);
    
    float3 finalColor = lerp(skyColor, cloudColor, cloudDensity * 0.7);
    finalColor = lerp(finalColor, skyColor, cloudEdge * 0.2);
    
    float cosTheta = dot(rayDir, sunDir);
    float sunAngle = acos(saturate(cosTheta));
    
    float sunGlow = exp(-sunAngle * sunAngle * 30.0) * 0.3;
    float sunDisc = smoothstep(0.02, 0.01, sunAngle);
    finalColor += float3(1.0, 0.98, 0.95) * (sunGlow + sunDisc);
    
    float horizonHaze = exp(-height * 3.0) * 0.15;
    finalColor = lerp(finalColor, float3(0.75, 0.88, 1.0), horizonHaze);
    
    finalColor = saturate(finalColor * 0.95);
    finalColor = pow(abs(finalColor), float3(1.0 / 2.2, 1.0 / 2.2, 1.0 / 2.2));
    
    return finalColor;
}

float4 fs_sky(VS_SKY_OUTPUT input) : SV_Target0 {
    float3 skyColor = computeSky(normalize(input.rayDir), SunDirection.xyz, Time);
    // Studio preset: dim + cool desaturation (matches C# studio palette direction).
    float luma = dot(skyColor, float3(0.299, 0.587, 0.114));
    skyColor = lerp(skyColor, luma.xxx * float3(0.9, 0.95, 1.05), SkyPreset * 0.85);
    skyColor *= lerp(1.0, 0.12, SkyPreset);
    return float4(skyColor, 1.0);
}

// ═════════════════════════════════════════════════════════════════════════════
// GRID
// ═════════════════════════════════════════════════════════════════════════════
struct VS_GRID_OUTPUT {
    float4 position : SV_POSITION;
    float3 nearPoint: TEXCOORD0;
    float3 farPoint : TEXCOORD1;
};

VS_GRID_OUTPUT vs_grid(uint vertexID : SV_VertexID) {
    VS_GRID_OUTPUT output;
    
    float2 positions[6] = {
        float2(-1, -1), float2( 1, -1), float2( 1,  1),
        float2(-1, -1), float2( 1,  1), float2(-1,  1)
    };
    float2 p = positions[vertexID];

    float4 nearH = mul(InvViewProj, float4(p, 0.0, 1.0));
    float4 farH  = mul(InvViewProj, float4(p, 1.0, 1.0));
    float3 nearPt = nearH.xyz / nearH.w;
    float3 farPt  = farH.xyz  / farH.w;

    output.position  = float4(p, 0.0, 1.0);
    output.nearPoint = nearPt;
    output.farPoint  = farPt;
    
    return output;
}

float computeDepth(float3 pos, float4x4 viewProj) {
    float4 clip = mul(viewProj, float4(pos, 1.0));
    return clip.z / clip.w;
}

float4 gridPattern(float3 worldPos, float scale, float lineWidth) {
    float2 coord = worldPos.xz * scale;
    float2 derivative = fwidth(coord);
    float2 gridLine = abs(frac(coord - 0.5) - 0.5) / derivative;
    float lineDist = min(gridLine.x, gridLine.y);
    float alpha = 1.0 - min(lineDist, 1.0);
    return float4(0.35, 0.35, 0.40, alpha);
}

struct FS_GRID_OUTPUT {
    float4 color : SV_Target0;
    float  depth : SV_Depth;
};

FS_GRID_OUTPUT fs_grid(VS_GRID_OUTPUT input) {
    FS_GRID_OUTPUT outFrag;

    float3 ray = input.farPoint - input.nearPoint;
    float t = -input.nearPoint.y / ray.y;

    if (t < 0.0) clip(-1);

    float3 hitPos = input.nearPoint + t * ray;

    float dist = length(hitPos - CameraPos.xyz);
    float fadeFar = 1.0 - smoothstep(15.0, 80.0, dist);

    if (fadeFar < 0.001) clip(-1);

    float4 fineGrid   = gridPattern(hitPos, 0.1, 1.0);
    float4 coarseGrid = gridPattern(hitPos, 0.01, 1.5);

    float4 gridColor = fineGrid;
    gridColor.a = max(fineGrid.a * 0.35, coarseGrid.a * 0.7);
    gridColor.rgb = lerp(fineGrid.rgb, coarseGrid.rgb * 1.2, coarseGrid.a);

    float2 axisDerivative = fwidth(hitPos.xz);
    
    float xAxisDist = abs(hitPos.z) / (axisDerivative.y * 1.5);
    float xAxisLine = 1.0 - min(xAxisDist, 1.0);
    
    float zAxisDist = abs(hitPos.x) / (axisDerivative.x * 1.5);
    float zAxisLine = 1.0 - min(zAxisDist, 1.0);

    if (xAxisLine > 0.01) {
        gridColor.rgb = lerp(gridColor.rgb, float3(0.9, 0.1, 0.1), xAxisLine);
        gridColor.a = max(gridColor.a, xAxisLine * 1.0);
    }
    if (zAxisLine > 0.01) {
        gridColor.rgb = lerp(gridColor.rgb, float3(0.1, 0.1, 0.9), zAxisLine);
        gridColor.a = max(gridColor.a, zAxisLine * 1.0);
    }

    gridColor.a *= fadeFar;

    float fadeNear = smoothstep(0.3, 1.5, dist);
    gridColor.a *= fadeNear;

    float depth = computeDepth(hitPos, ViewProj);
    depth = min(depth + 0.00001, 1.0);
    depth = clamp(depth, 0.0, 1.0);

    float4 lightSpacePos = mul(LightSpaceMatrix, float4(hitPos, 1.0));
    float3 projCoords = lightSpacePos.xyz / lightSpacePos.w;
    float2 shadowUV = projCoords.xy * 0.5 + 0.5;
    shadowUV.y = 1.0 - shadowUV.y;
    
    float shadow = 1.0;
    {
        float bias = 0.001;
        float currentDepth = projCoords.z - bias;
        float2 texelSize = 1.0 / 2048.0;
        float shadowSum = 0.0;
        float pcfDepth;
        pcfDepth = ShadowMap.SampleLevel(ShadowSampler, shadowUV + float2(-1, -1) * texelSize, 0).r;
        shadowSum += (currentDepth < pcfDepth) ? 1.0 : 0.0;
        pcfDepth = ShadowMap.SampleLevel(ShadowSampler, shadowUV + float2( 0, -1) * texelSize, 0).r;
        shadowSum += (currentDepth < pcfDepth) ? 1.0 : 0.0;
        pcfDepth = ShadowMap.SampleLevel(ShadowSampler, shadowUV + float2( 1, -1) * texelSize, 0).r;
        shadowSum += (currentDepth < pcfDepth) ? 1.0 : 0.0;
        pcfDepth = ShadowMap.SampleLevel(ShadowSampler, shadowUV + float2(-1,  0) * texelSize, 0).r;
        shadowSum += (currentDepth < pcfDepth) ? 1.0 : 0.0;
        pcfDepth = ShadowMap.SampleLevel(ShadowSampler, shadowUV + float2( 0,  0) * texelSize, 0).r;
        shadowSum += (currentDepth < pcfDepth) ? 1.0 : 0.0;
        pcfDepth = ShadowMap.SampleLevel(ShadowSampler, shadowUV + float2( 1,  0) * texelSize, 0).r;
        shadowSum += (currentDepth < pcfDepth) ? 1.0 : 0.0;
        pcfDepth = ShadowMap.SampleLevel(ShadowSampler, shadowUV + float2(-1,  1) * texelSize, 0).r;
        shadowSum += (currentDepth < pcfDepth) ? 1.0 : 0.0;
        pcfDepth = ShadowMap.SampleLevel(ShadowSampler, shadowUV + float2( 0,  1) * texelSize, 0).r;
        shadowSum += (currentDepth < pcfDepth) ? 1.0 : 0.0;
        pcfDepth = ShadowMap.SampleLevel(ShadowSampler, shadowUV + float2( 1,  1) * texelSize, 0).r;
        shadowSum += (currentDepth < pcfDepth) ? 1.0 : 0.0;
        shadow = shadowSum / 9.0;
    }
    
    gridColor.rgb *= lerp(0.4, 1.0, shadow);

    outFrag.color = gridColor;
    outFrag.depth = depth;
    return outFrag;
}

// ═════════════════════════════════════════════════════════════════════════════
// MESH — PBR with texture sampling + instancing
// ═════════════════════════════════════════════════════════════════════════════
struct VS_MESH_INPUT {
    float3 position : POSITION;
    float3 normal   : NORMAL;
    float2 uv       : TEXCOORD0;
};

struct VS_MESH_OUTPUT {
    float4 position      : SV_POSITION;
    float4 lightSpacePos : TEXCOORD1;
    float3 normal        : TEXCOORD2;
    float3 worldPos      : TEXCOORD3;
    float2 uv            : TEXCOORD4;
    float4 color         : TEXCOORD5;
};

// ── Entity instance array (must match C# EntityUniforms stride = 80 bytes) ─
// Keep model and color interleaved. C# uploads EntityUniforms { Matrix4x4, Vector4 }
// for each instance; separate matrix/color arrays would give every instance after
// the first a corrupted matrix.
struct EntityInstance {
    float4x4 Model;
    float4   Color;
};

cbuffer EntityUniforms : register(b12) {
    EntityInstance Entities[256];
};

VS_MESH_OUTPUT vs_mesh(VS_MESH_INPUT input, uint instanceID : SV_InstanceID) {
    VS_MESH_OUTPUT output;

    uint iid = min(instanceID, (uint)MAX_MESH_INSTANCES - 1);
    float4x4 model = Entities[iid].Model;
    float4   color = Entities[iid].Color;

    float4 worldPos = mul(model, float4(input.position, 1.0));
    output.position      = mul(ViewProj, worldPos);
    output.lightSpacePos = mul(LightSpaceMatrix, worldPos);
    output.worldPos      = worldPos.xyz;
    output.normal        = normalize(mul((float3x3)model, input.normal));
    output.uv            = input.uv;
    output.color         = color;

    return output;
}

// ═════════════════════════════════════════════════════════════════════════════
// SHADOW PASS (instanced)
// ═════════════════════════════════════════════════════════════════════════════
struct VS_SHADOW_OUTPUT {
    float4 position : SV_POSITION;
};

VS_SHADOW_OUTPUT vs_shadow(VS_MESH_INPUT input, uint instanceID : SV_InstanceID) {
    VS_SHADOW_OUTPUT output;
    uint iid = min(instanceID, (uint)MAX_MESH_INSTANCES - 1);
    float4 worldPos = mul(Entities[iid].Model, float4(input.position, 1.0));
    output.position = mul(LightSpaceMatrix, worldPos);
    return output;
}

float4 fs_shadow(VS_SHADOW_OUTPUT input) : SV_Target0 {
    return float4(input.position.z, 0, 0, 1);
}

// ═════════════════════════════════════════════════════════════════════════════
// MESH FRAGMENT — Astra BRDF (Phase 1)
// ═════════════════════════════════════════════════════════════════════════════
float4 fs_mesh(VS_MESH_OUTPUT input, bool isFrontFace : SV_IsFrontFace) : SV_Target0 {
    // ── Surface Properties (global constant — historic orange clay) ──────────
    // Bit layout mirrors StrataFeature, incl. 2048=AlbedoMap.
    uint feat = (uint)Custom1.x;
    float3 baseColor = BaseColor.rgb;
    if ((feat & 2048u) != 0u)
        baseColor *= AlbedoTex.SampleLevel(SurfSampler, input.uv, 0).rgb;
    float metallic   = Metallic;
    float roughness  = max(0.04, Roughness);
    float ao         = AO;

    // ── Terrain (ASTRA_TERRAIN 4096u): plain white, lit normally ──
    if ((Flags & 4096u) != 0u) {
        baseColor = float3(1.0, 1.0, 1.0);
        metallic = 0.0;
        roughness = max(roughness, 0.65);
    }

    // ── Debug views (flags bits 14-15): 2 = unlit albedo (1/3 below) ────
    uint dbg = (Flags >> 14u) & 7u;
    if (dbg == 2u) {
        return float4(LinearToSRGB(baseColor), 1.0);
    }

    // ── Skin (Tier-2 flesh, v1): dielectric clamp here; wrap + deep-red
    // scatter join directLight below (HLSL has no SSS terms — approximated).
    bool skinOn = (feat & 8192u) != 0u;
    if (skinOn) {
        metallic = 0.0;
        roughness = max(roughness, 0.35);
    }

    // ── Strata Tier-1 lobes (mask in Custom1.x; uniform control flow) ────────
    // Bit layout mirrors StrataFeature (see above). Unset bits cost nothing.
    float diffWrap = Custom0.y;
    float detailTile = max(1.0, Custom0.z);
    float toksvigK = Custom0.w;

    // Screen-door coverage first (early-out before any lighting work).
    if ((feat & 1024u) != 0u) {
        float coverage = BaseColor.a * Alpha;
        float2 bpx = floor(input.position.xy);
        float b2 = frac(bpx.x * 0.5 + bpx.y * bpx.y * 0.75);
        float b4 = frac(bpx.x * 0.25 + bpx.y * bpx.y * 0.1875) * 0.25 + b2;
        float b8 = frac(bpx.x * 0.125 + bpx.y * bpx.y * 0.046875) * 0.25 + b4;
        if (coverage < frac(b8))
            discard;
    }

    // Baked AO + Toksvig variance from the RMA texture (R=rough G=metal B=AO A=variance).
    if ((feat & (1u | 16u)) != 0u) {
        float4 rma = RMATex.SampleLevel(SurfSampler, input.uv, 0);
        if ((feat & 1u) != 0u)
            ao = rma.b;
        if ((feat & 16u) != 0u)
            roughness = clamp(roughness + (1.0 - rma.a) * toksvigK, 0.04, 1.0);
    }

    // ── Debug view: baked AO factor only ─────────────────────────────
    if (dbg == 4u) {
        return float4(ao, ao, ao, 1.0);
    }

    // ── Geometry ───────────────────────────────────────────────────────────
    float3 V = normalize(CameraPos.xyz - input.worldPos);
    float3 N = normalize(input.normal);
    if (!isFrontFace) N = -N;

    // Bump-offset parallax + RNM detail blend (detail UV only; base UV untouched).
    if ((feat & (4u | 32u)) != 0u) {
        float2 dUV = input.uv * detailTile;
        if ((feat & 32u) != 0u) {
            float h = DetailAlbedo.SampleLevel(SurfSampler, dUV, 0).a;
            float2 poff = (V.xy / max(V.z, 0.3)) * (h - 0.5) * 0.06;
            dUV += poff;
        }
        float3 dn = DetailNormal.SampleLevel(SurfSampler, dUV, 0).rgb * 2.0 - 1.0;
        float dFade = saturate(1.0 - length(input.worldPos - CameraPos.xyz) / 40.0);
        N = normalize(float3(N.xy + dn.xy * dFade, N.z));
    }

    // ── Debug view: raw world-space normals ──────────────────────────────
    if (dbg == 1u) {
        return float4(N * 0.5 + 0.5, 1.0);
    }

    float3 L = normalize(SunDirection.xyz);
    float3 H = normalize(L + V);

    float NdotL = saturate(dot(N, L));
    float NdotV = max(dot(N, V), 0.001);

    // ── Debug view: sun NdotL only ───────────────────────────────────
    if (dbg == 7u) {
        return float4(NdotL, NdotL, NdotL, 1.0);
    }

    // ── Direct Lighting (Sun) ──────────────────────────────────────────────
    float3 diffuse = AstraDiffuse(baseColor, NdotL);
    float spec = AstraSpecular(N, H, Shininess, SpecularStrength);

    // Fresnel (bias+scale, no pow-5)
    float fresnel = DIELECTRIC_F0 + (1.0 - DIELECTRIC_F0) * (1.0 - NdotV);
    float3 reflectionWeight = AstraReflection(metallic, baseColor);
    float3 specular = spec * reflectionWeight * fresnel;

    float3 directLight;
    {
        // Wrapped diffuse softens the terminator when the Wrap bit is set.
        float ndlW = diffWrap > 0.0 ? saturate((NdotL + diffWrap) / (1.0 + diffWrap)) : NdotL;
        directLight = (diffuse * ndlW + specular * NdotL);

        // Clearcoat (Tier-2 lacquer: fixed-F0 Blinn lobe, reuses base normal).
        if ((feat & 64u) != 0u) {
            float coatF = 0.04 + 0.96 * pow(1.0 - NdotV, 5.0);
            float coat = pow(saturate(dot(N, H)), 600.0);
            directLight += coat * coatF * saturate(Custom1.yzw) * NdotL * 1.5;
        }

        // Iridescence (Tier-2 thin film: fixed-thickness spectral ramp, v1).
        if ((feat & 512u) != 0u) {
            float cosV = NdotV;
            float phase = cosV * 2.0;
            float3 irid = 0.5 + 0.5 * cos(6.28318 * (phase * float3(1.0, 0.85, 0.7) + float3(0.0, 0.33, 0.67)));
            float iriAmt = (1.0 - cosV) * (1.0 - cosV);
            directLight += iriAmt * irid * NdotL * 0.6;
            directLight += (irid - 1.0) * specular * NdotL * iriAmt * 0.5;
        }

        // Sheen (Tier-2 cloth: Charlie NDF retro-reflection, v1, fixed fabric roughness).
        if ((feat & 128u) != 0u) {
            float sheenRough = 0.5;
            float shInvAlpha = 1.0 / max(sheenRough, 0.05);
            float shNdotH = saturate(dot(N, H));
            float shCos2h = shNdotH * shNdotH;
            float shSin2h = max(1.0 - shCos2h, 0.0078125);
            float Dsheen = (2.0 + shInvAlpha * shInvAlpha) / 6.28318
                * pow(shSin2h, shInvAlpha * shInvAlpha * 0.5);
            float Vsheen = 0.25 / max(NdotL + NdotV - NdotL * NdotV, 0.001);
            directLight += Dsheen * baseColor * Vsheen * NdotL;
        }

        // Anisotropy (Tier-2 brushed metal: derivative-frame highlight stretch, v1).
        if ((feat & 256u) != 0u) {
            float3 aq0 = ddx(input.worldPos);
            float3 aq1 = ddy(input.worldPos);
            float2 ast0 = ddx(input.uv);
            float2 ast1 = ddy(input.uv);
            float3 anaT = aq0 * ast1.y - aq1 * ast0.y;
            float anaLen = length(anaT);
            if (anaLen > 0.00001) {
                anaT /= anaLen;
                float TH = dot(anaT, H);
                float ano = pow(sqrt(max(1.0 - TH * TH, 0.0)), 60.0);
                directLight += ano * specular * NdotL * 0.8;
            }
        }

        // Hair (Tier-2 Kajiya-Kay: strand highlight, v1, strands along UV +V).
        if ((feat & 16384u) != 0u) {
            float3 hq0 = ddx(input.worldPos);
            float3 hq1 = ddy(input.worldPos);
            float2 hst0 = ddx(input.uv);
            float2 hst1 = ddy(input.uv);
            float3 strandT = hq1 * hst0.x - hq0 * hst1.x;
            float strandLen = length(strandT);
            if (strandLen > 0.00001) {
                strandT /= strandLen;
                float TH = dot(strandT, H);
                float sinTH = sqrt(max(1.0 - TH * TH, 0.0));
                float primary = pow(sinTH, 60.0);
                float3 tilted = normalize(strandT + N * 0.1);
                float TH2 = dot(tilted, H);
                float secondary = pow(sqrt(max(1.0 - TH2 * TH2, 0.0)), 30.0);
                directLight += (primary * float3(0.9, 0.9, 0.95) + secondary * float3(0.55, 0.3, 0.2)) * specular * NdotL;
            }
        }

        // Skin scatter: wrapped diffuse + deep-red bleed (v1 approximation).
        if (skinOn) {
            float skinWrap = saturate((NdotL + 0.3) / 1.3);
            directLight += baseColor * float3(1.0, 0.35, 0.25) * skinWrap * 0.35;
        }
    }

    // ── Debug view: sun-driven direct only ─────────────────────────────
    if (dbg == 5u) {
        float3 dd = directLight * Exposure;
        return float4(LinearToSRGB(PBRNeutral(dd)), 1.0);
    }

    // ── Additional Lights ──────────────────────────────────────────────────
    int maxL = min(LightCount, min(MaxLights, 64));
    for (int li = 0; li < maxL; li++) {
        LightData light = Lights[li];
        if (light.LightType == 0) continue;

        float3 toLight = light.Position - input.worldPos;
        float dist = length(toLight);
        if (dist >= light.Range) continue;
        float3 Ll = toLight / dist;

        float lNdotL = saturate(dot(N, Ll));
        if (lNdotL <= 0.0) continue;

        float rangeFade = saturate(1.0 - dist / light.Range);
        float atten = rangeFade * rangeFade / (1.0 + dist * dist * light.Attenuation);

        if (light.LightType == 2) {
            float spotDot = dot(-Ll, normalize(light.Direction));
            float spotAng = acos(saturate(spotDot));
            if (spotAng > light.OuterAngle) continue;
            if (spotAng > light.InnerAngle)
                atten *= 1.0 - pow((spotAng - light.InnerAngle) / (light.OuterAngle - light.InnerAngle), 2);
        }

        float3 lH = normalize(V + Ll);
        float lSpec = AstraSpecular(N, lH, Shininess, SpecularStrength);
        float3 lFresnel = reflectionWeight * (DIELECTRIC_F0 + (1.0 - DIELECTRIC_F0) * (1.0 - NdotV));
        float3 lRad = light.Color * light.Intensity * atten;
        directLight += (AstraDiffuse(baseColor, lNdotL) + lSpec * lFresnel) * lRad * lNdotL;
    }

    // ── Ambient: sky-captured SH probe (replaces hardcoded hemisphere) ──────
    // BentNormal bit (2u): world-space bent direction replaces N for the
    // irradiance lookup only — specular keeps the geometric normal.
    float3 shadeN = N;
    if ((feat & 2u) != 0u) {
        float3 bent = BentTex.SampleLevel(SurfSampler, input.uv, 0).rgb * 2.0 - 1.0;
        if (dot(bent, bent) > 1e-6) shadeN = normalize(bent);
    }
    float3 ambient = SHEval(shadeN) * baseColor * (1.0 - metallic);
    ambient *= ao;

    // ── Debug view: ambient/IBL only ───────────────────────────────────
    if (dbg == 6u) {
        float3 ee = ambient * Exposure;
        return float4(LinearToSRGB(PBRNeutral(ee)), 1.0);
    }

    // ── Shadow Mapping (PCF 3x3) ──────────────────────────────────────────
    float shadow = 1.0;
    float3 projCoords = input.lightSpacePos.xyz / input.lightSpacePos.w;
    float2 shadowUV = projCoords.xy * 0.5 + 0.5;
    shadowUV.y = 1.0 - shadowUV.y;

    if (all(shadowUV >= 0.0) && all(shadowUV <= 1.0) && projCoords.z >= 0.0 && projCoords.z <= 1.0) {
        float bias = max(0.005 * (1.0 - NdotL), 0.001);
        float currentDepth = projCoords.z - bias;
        float2 texelSize = 1.0 / 2048.0;
        float shadowSum = 0.0;
        for (int sy = -1; sy <= 1; sy++) {
            for (int sx = -1; sx <= 1; sx++) {
                float pcfDepth = ShadowMap.SampleLevel(ShadowSampler, shadowUV + float2(sx, sy) * texelSize, 0).r;
                shadowSum += (currentDepth < pcfDepth) ? 1.0 : 0.0;
            }
        }
        shadow = shadowSum / 9.0;
        shadow = lerp(0.35, 1.0, shadow);
    }

    // ── Final Composition ──────────────────────────────────────────────────
    // ── Debug view: shadow factor only ───────────────────────────────────
    if (dbg == 3u) {
        return float4(shadow, shadow, shadow, 1.0);
    }
    float3 finalColor = ambient + directLight * shadow;
    finalColor *= Exposure;
    finalColor = PBRNeutral(finalColor);
    finalColor = LinearToSRGB(finalColor);

    // ── Display-referred grade LUT + interlaced gradient noise dither ─────────
    // Dither is absolutely last: ±0.5 LSB in display space kills banding.
    finalColor = LUTSample(finalColor, GradeLUT, SurfSampler);
    float ign = frac(52.9829189 * frac(dot(floor(input.position.xy), float2(0.06711056, 0.00583715))));
    finalColor += (ign - 0.5) / 255.0;

    // Draw alpha: opaque pipeline ignores it (blend off), transparent blends it.
    return float4(finalColor, Alpha);
}

// ═════════════════════════════════════════════════════════════════════════════
// WIREFRAME
// ═════════════════════════════════════════════════════════════════════════════
float4 fs_wireframe(VS_MESH_OUTPUT input) : SV_Target0 {
    return float4(0.9, 0.9, 1.0, 0.15);
}

// ═════════════════════════════════════════════════════════════════════════════
// GIZMO (Translation/Rotation/Scale handles)
// ═════════════════════════════════════════════════════════════════════════════
VS_MESH_OUTPUT vs_gizmo(VS_MESH_INPUT input, uint instanceID : SV_InstanceID) {
    VS_MESH_OUTPUT output;

    uint iid = min(instanceID, (uint)MAX_MESH_INSTANCES - 1);
    float4x4 model = Entities[iid].Model;
    float4   color = Entities[iid].Color;

    float4 worldPos = mul(model, float4(input.position, 1.0));
    output.position      = mul(ViewProj, worldPos);
    output.lightSpacePos = float4(0, 0, 0, 1);
    output.normal        = normalize(mul((float3x3)model, input.normal));
    output.worldPos      = worldPos.xyz;
    output.uv            = input.uv;
    output.color         = color;

    return output;
}

float4 fs_gizmo(VS_MESH_OUTPUT input) : SV_Target0 {
    float3 lightDir = normalize(float3(0.5, 0.7, 0.3));
    float NdotL = max(dot(normalize(input.normal), lightDir), 0.0);
    float shading = 0.5 + 0.5 * NdotL;

    return float4(input.color.rgb * shading, input.color.a);
}
