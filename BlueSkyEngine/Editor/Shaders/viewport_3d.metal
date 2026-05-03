#include <metal_stdlib>
using namespace metal;

// ─────────────────────────────────────────────────────────────────────────────
// Shared uniform structure — pushed once per frame, used by both sky and grid.
// ─────────────────────────────────────────────────────────────────────────────
struct ViewUniforms {
    float4x4 view;
    float4x4 proj;
    float4x4 viewProj;
    float4x4 invViewProj;
    float4x4 lightSpaceMatrix;
    float4   cameraPos;    // w unused — matches C# Vector4
    float    time;
    float3   sunDirection;
    float4   windParams;
};

// ═════════════════════════════════════════════════════════════════════════════
// SKY — fullscreen triangle with procedural gradient atmosphere
// ═════════════════════════════════════════════════════════════════════════════

struct SkyVaryings {
    float4 position [[position]];
    float2 uv;
    float3 rayDir; // World-space ray direction
};

vertex SkyVaryings vs_sky(uint vertexID [[vertex_id]],
                          constant ViewUniforms& u [[buffer(10)]])
{
    // Full-screen triangle trick
    float2 uv = float2((vertexID << 1) & 2, vertexID & 2);
    SkyVaryings out;
    out.position = float4(uv * 2.0 - 1.0, 0.9999, 1.0);
    out.uv = uv;

    // We pass NDC to fragment shader for better precision reconstruction
    return out;
}

// Atmospheric scattering constants
constant float3 RAYLEIGH_BETA = float3(5.8e-6, 1.35e-5, 3.31e-5); // Scattering coefficients for Rayleigh
constant float3 MIE_BETA = float3(2.0e-5, 2.0e-5, 2.0e-5); // Scattering coefficients for Mie
constant float MIE_G = 0.758; // Mie phase function parameter
constant float ATMOSPHERE_SCALE_HEIGHT = 8000.0; // Scale height in meters
constant float EARTH_RADIUS = 6371000.0; // Earth radius in meters
constant float ATMOSPHERE_RADIUS = EARTH_RADIUS + ATMOSPHERE_SCALE_HEIGHT * 4.0;

// Compute atmospheric density based on height
float computeAtmosphericDensity(float height) {
    return exp(-max(0.0, height) / ATMOSPHERE_SCALE_HEIGHT);
}

// Compute optical depth for realistic light attenuation
float computeOpticalDepth(float3 rayOrigin, float3 rayDir, float maxDist) {
    float stepSize = maxDist / 16.0;
    float opticalDepth = 0.0;
    
    for (int i = 0; i < 16; i++) {
        float t = (float(i) + 0.5) * stepSize;
        float3 pos = rayOrigin + rayDir * t;
        float height = length(pos) - EARTH_RADIUS;
        opticalDepth += computeAtmosphericDensity(height) * stepSize;
    }
    
    return opticalDepth;
}

// Rayleigh phase function
float rayleighPhase(float cosTheta) {
    return 0.75 * (1.0 + cosTheta * cosTheta);
}

// Mie phase function (Henyey-Greenstein)
float miePhase(float cosTheta, float g) {
    float g2 = g * g;
    return (1.0 - g2) / (4.0 * 3.14159 * pow(1.0 + g2 - 2.0 * g * cosTheta, 1.5));
}

// Simple hash function for noise
float hash(float n) {
    return fract(sin(n) * 43758.5453123);
}

// 2D noise function
float noise(float2 x) {
    float2 p = floor(x);
    float2 f = fract(x);
    f = f * f * (3.0 - 2.0 * f);
    float n = p.x + p.y * 57.0;
    return mix(mix(hash(n), hash(n + 1.0), f.x),
               mix(hash(n + 57.0), hash(n + 58.0), f.x), f.y);
}

// Fractal Brownian Motion for cloud shapes
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

// Compute realistic daytime sky with proper sun disc rendering
float3 computeSky(float3 rayDir, float3 sunDir, float time) {
    float height = saturate(rayDir.y);
    
    // Natural Sky Colors (Rayleigh-inspired)
    float3 zenithColor  = float3(0.15, 0.45, 0.85);
    float3 horizonColor = float3(0.55, 0.82, 0.98);
    
    // Smooth vertical gradient
    float3 skyColor = mix(horizonColor, zenithColor, pow(height, 0.6));
    
    // ═══════════════════════════════════════════════════════════════════════════
    // NATURAL SUN DISC & GLOW
    // ═══════════════════════════════════════════════════════════════════════════
    
    float cosTheta = dot(rayDir, normalize(sunDir));
    float theta = acos(saturate(cosTheta));
    
    // Sharp sun disc
    float sunDisc = 1.0 - smoothstep(0.0045, 0.005, theta);
    
    // Natural atmospheric glow
    float sunGlow = exp(-theta * 10.0) * 0.4;
    float sunHaze = exp(-theta * 2.5) * 0.15;
    
    float3 sunColor = float3(1.0, 1.0, 0.95);
    float3 glowColor = float3(1.0, 0.95, 0.85);
    
    float3 finalSun = (sunColor * sunDisc * 1.5) + (glowColor * (sunGlow + sunHaze));
    skyColor += finalSun;
    
    // Subtle horizon haze
    float haze = exp(-height * 3.5) * 0.2;
    skyColor = mix(skyColor, float3(0.8, 0.9, 1.0), haze);
    
    return saturate(skyColor);
}

fragment float4 fs_sky(SkyVaryings in [[stage_in]],
                       constant ViewUniforms& u [[buffer(10)]])
{
    // Reconstruct rayDir here to avoid any interpolation artifacts
    float2 ndc = in.uv * 2.0 - 1.0;
    float4 worldPosH = u.invViewProj * float4(ndc, 1.0, 1.0);
    float3 worldPos = worldPosH.xyz / worldPosH.w;
    float3 rayDir = normalize(worldPos - u.cameraPos.xyz);

    float3 skyColor = computeSky(rayDir, u.sunDirection, u.time);
    return float4(skyColor, 1.0);
}

// ═════════════════════════════════════════════════════════════════════════════
// GRID — infinite XZ plane with multi-resolution lines and axis colouring
// ═════════════════════════════════════════════════════════════════════════════

struct GridVaryings {
    float4 position [[position]];
    float3 worldPos;
    float3 nearPoint;
    float3 farPoint;
};

// We draw a full-screen quad and reconstruct the XZ intersection in the
// fragment shader.  This gives a true "infinite grid" look without needing
// a large mesh.  Technique from "The Best Darn Grid Shader".

vertex GridVaryings vs_grid(uint vertexID [[vertex_id]],
                            constant ViewUniforms& u [[buffer(10)]])
{
    // 6-vertex fullscreen quad (2 tris)
    float2 positions[6] = {
        float2(-1, -1), float2( 1, -1), float2( 1,  1),
        float2(-1, -1), float2( 1,  1), float2(-1,  1)
    };
    float2 p = positions[vertexID];

    // Unproject near and far planes to world space
    float4 nearH = u.invViewProj * float4(p, 0.0, 1.0);
    float4 farH  = u.invViewProj * float4(p, 1.0, 1.0);
    float3 nearPt = nearH.xyz / nearH.w;
    float3 farPt  = farH.xyz  / farH.w;

    GridVaryings out;
    out.position  = float4(p, 0.0, 1.0);
    out.worldPos  = float3(0);
    out.nearPoint = nearPt;
    out.farPoint  = farPt;
    return out;
}

// Compute linear depth for depth buffer output
float computeDepth(float3 pos, float4x4 viewProj) {
    float4 clip = viewProj * float4(pos, 1.0);
    return clip.z / clip.w;
}

// Grid line pattern with anti-aliased edges and proper alpha
float4 gridPattern(float3 worldPos, float scale, float lineWidth, float dist) {
    float2 coord = worldPos.xz * scale;
    float2 derivative = fwidth(coord);
    
    // Adaptive line width — stays readable at distance without getting too thick
    float adaptiveWidth = lineWidth * (1.0 + dist * 0.005);
    
    float2 grid = abs(fract(coord - 0.5) - 0.5) / derivative;
    float line = min(grid.x, grid.y);
    
    // Smooth anti-aliased falloff - softer for a more subtle look
    float alpha = 1.0 - smoothstep(0.0, adaptiveWidth, line);
    
    // Grid color — dark gray/subtle silver
    float3 color = float3(0.25, 0.26, 0.28);
    
    return float4(color, alpha);
}

struct GridFragOut {
    float4 color [[color(0)]];
    float  depth [[depth(any)]];
};

fragment GridFragOut fs_grid(GridVaryings in [[stage_in]],
                            constant ViewUniforms& u [[buffer(10)]],
                            depth2d<float> shadowMap [[texture(1)]])
{
    // Ray-plane intersection: Y = 0
    float3 ray = in.farPoint - in.nearPoint;
    float t = -in.nearPoint.y / ray.y;

    GridFragOut out;

    if (t < 0.0) {
        // Ray misses the ground plane
        discard_fragment();
    }

    float3 hitPos = in.nearPoint + t * ray;

    // Distance-based fade (world units from camera)
    float dist = length(hitPos - u.cameraPos.xyz);
    
    // Smooth, long fade for a natural horizon blend
    float fadeFar = smoothstep(120.0, 30.0, dist);
    
    // Early discard for performance
    if (fadeFar < 0.001) {
        discard_fragment();
    }

    // ═══════════════════════════════════════════════════════════════════
    // TWO-TIER CLEAN GRID (1m and 10m)
    // ═══════════════════════════════════════════════════════════════════
    
    // Fine grid: 1-unit squares (crisp, very thin, highly transparent)
    float4 fineGrid = gridPattern(hitPos, 1.0, 0.08, dist);
    fineGrid.a *= 0.4; // Make fine grid very subtle
    
    // Coarse grid: 10-unit squares (slightly thicker)
    float4 coarseGrid = gridPattern(hitPos, 0.1, 0.20, dist);

    // Distance-based blending
    float fineFade = smoothstep(50.0, 10.0, dist);
    
    // Combine grids - coarse is always visible, fine fades out
    float4 gridColor = coarseGrid;
    gridColor.rgb = mix(coarseGrid.rgb, fineGrid.rgb, fineFade * fineGrid.a);
    gridColor.a = max(coarseGrid.a, fineGrid.a * fineFade);

    // ═══════════════════════════════════════════════════════════════════
    // AXIS LINES — X (red) and Z (blue) 
    // ═══════════════════════════════════════════════════════════════════
    float2 axisDerivative = fwidth(hitPos.xz);
    
    // X axis (Z ≈ 0): Red, crisp
    float xAxisDist = abs(hitPos.z) / (axisDerivative.y * 1.5);
    float xAxisLine = 1.0 - smoothstep(0.0, 1.0, xAxisDist);
    
    // Z axis (X ≈ 0): Blue, crisp
    float zAxisDist = abs(hitPos.x) / (axisDerivative.x * 1.5);
    float zAxisLine = 1.0 - smoothstep(0.0, 1.0, zAxisDist);

    // Blend axis lines
    if (xAxisLine > 0.01) {
        float3 xAxisColor = float3(0.85, 0.20, 0.20); // Darker Red
        gridColor.rgb = mix(gridColor.rgb, xAxisColor, xAxisLine);
        gridColor.a = max(gridColor.a, xAxisLine);
    }
    if (zAxisLine > 0.01) {
        float3 zAxisColor = float3(0.20, 0.35, 0.85); // Darker Blue
        gridColor.rgb = mix(gridColor.rgb, zAxisColor, zAxisLine);
        gridColor.a = max(gridColor.a, zAxisLine);
    }

    // Apply distance fade
    gridColor.a *= fadeFar;

    // Near-camera fade to avoid z-fighting / Moiré at feet
    float fadeNear = smoothstep(0.2, 1.0, dist);
    gridColor.a *= fadeNear;
    
    // Discard fully transparent fragments for better performance
    if (gridColor.a < 0.003) {
        discard_fragment();
    }

    // Compute proper depth so grid sits at Y=0 in the depth buffer
    float depth = computeDepth(hitPos, u.viewProj);
    // Clamp depth to valid range
    depth = clamp(depth, 0.0, 1.0);

    // ── 0. Compute Shadow ──────────────────────────────────────────────
    float4 lightSpacePos = u.lightSpaceMatrix * float4(hitPos, 1.0);
    float3 projCoords = lightSpacePos.xyz / lightSpacePos.w;
    float2 shadowUV = projCoords.xy * 0.5 + 0.5;
    shadowUV.y = 1.0 - shadowUV.y;
    
    constexpr sampler shadowSampler(coord::normalized, filter::linear, address::clamp_to_edge, compare_func::less);
    float shadow = 1.0;
    
    if (shadowUV.x >= 0.0 && shadowUV.x <= 1.0 && shadowUV.y >= 0.0 && shadowUV.y <= 1.0 && projCoords.z >= 0.0 && projCoords.z <= 1.0) {
        float bias = 0.001;
        float currentDepth = projCoords.z - bias;
        float shadowSum = 0.0;
        
        for (int x = -1; x <= 1; ++x) {
            for (int y = -1; y <= 1; ++y) {
                float2 offset = float2(x, y) / float2(shadowMap.get_width(), shadowMap.get_height());
                shadowSum += shadowMap.sample_compare(shadowSampler, shadowUV + offset, currentDepth);
            }
        }
        shadow = shadowSum / 9.0;
    }
    
    gridColor.rgb *= mix(0.4, 1.0, shadow);

    out.color = gridColor;
    out.depth = depth;
    return out;
}

// ═════════════════════════════════════════════════════════════════════════════
// MESH — renders ECS entities with transform and color
// ═════════════════════════════════════════════════════════════════════════════

struct EntityUniforms {
    float4x4 model;
    float4   color;
};

struct MeshVertexIn {
    float3 position [[attribute(0)]];
    float3 normal   [[attribute(1)]];
    float2 uv       [[attribute(2)]];
};

// MeshVaryings now carries UV so fs_mesh can sample textures
struct MeshVaryings {
    float4 position [[position]];
    float4 lightSpacePos;
    float3 worldPos;
    float3 normal;
    float4 color;
    float2 uv;
};

vertex MeshVaryings vs_mesh(MeshVertexIn in [[stage_in]],
                           uint instance_id [[instance_id]],
                           constant EntityUniforms* entities [[buffer(30)]],
                           constant ViewUniforms& view [[buffer(10)]])
{
    MeshVaryings out;
    constant EntityUniforms& entity = entities[instance_id];
    
    float3 localPos = in.position;
    
    float3 worldPosBase = (entity.model * float4(in.position, 1.0)).xyz;
    float windSpeed    = view.windParams.x;
    float windStrength = view.windParams.y;
    float windFreq     = view.windParams.z;
    
    float wave = sin(worldPosBase.x * windFreq + view.time * windSpeed) * 
                 cos(worldPosBase.z * windFreq * 0.8 + view.time * windSpeed * 1.1);
    
    float foliageMask = saturate(in.position.y * 0.5);
    localPos.xz += wave * windStrength * foliageMask;

    float4 worldPos   = entity.model * float4(localPos, 1.0);
    out.worldPos      = worldPos.xyz;
    out.position      = view.viewProj * worldPos;
    out.normal        = normalize((entity.model * float4(in.normal, 0.0)).xyz);
    out.lightSpacePos = view.lightSpaceMatrix * worldPos;
    out.color         = entity.color;
    out.uv            = in.uv;
    
    return out;
}

// ═════════════════════════════════════════════════════════════════════════════
// SHADOW PASS
// ═════════════════════════════════════════════════════════════════════════════

struct ShadowVaryings {
    float4 position [[position]];
};

vertex ShadowVaryings vs_shadow(MeshVertexIn in [[stage_in]],
                                uint instance_id [[instance_id]],
                                constant EntityUniforms* entities [[buffer(30)]],
                                constant ViewUniforms& view [[buffer(10)]])
{
    ShadowVaryings out;
    constant EntityUniforms& entity = entities[instance_id];
    float4 worldPos = entity.model * float4(in.position, 1.0);
    out.position = view.lightSpaceMatrix * worldPos;
    return out;
}

fragment float4 fs_shadow(ShadowVaryings in [[stage_in]])
{
    return float4(in.position.z);
}

// PBR constants
constant float PI = 3.14159265359;
constant float INV_PI = 0.31830988618;

// ── Light data for multi-light support (matches C# LightData) ──────────────
struct LightData_PBR {
    packed_float3 position;
    float  range;
    packed_float3 direction;
    float  intensity;
    packed_float3 color;
    int    type;
    float  innerAngle;
    float  outerAngle;
    float  attenuation;
    int    castShadows;
    int    volumetric;
    float  _pad1;
    float  _pad2;
};

struct LightingSettings_PBR {
    int    quality;
    int    maxLights;
    int    enableIBL;
    int    enableVolumetrics;
    int    enableContactShadows;
    float  exposure;
    packed_float3 ambientColor;
};

// ── Procedural sky environment sampling with roughness-based filtering ──────
// Roughness controls sun highlight sharpness and sky detail preservation.
// Smooth surfaces get crisp sun reflections; rough surfaces see blurred sky.
float3 sampleSkyEnv(float3 dir, float3 sunDir, float roughness) {
    float height = saturate(dir.y);
    float3 zenith  = float3(0.15, 0.45, 0.85);
    float3 horizon = float3(0.55, 0.82, 0.98);
    float3 sky = mix(horizon, zenith, pow(height, 0.6));
    
    // Sun contribution — blur sun disc based on roughness
    float cosTheta = dot(dir, normalize(sunDir));
    float sunPower = exp2(10.0 * (1.0 - roughness) + 1.0); // 2048 sharp → 2 blurry
    float sunGlow = pow(saturate(cosTheta), sunPower) * (2.0 / (1.0 + roughness * roughness));
    float sunHaze = pow(saturate(cosTheta), max(8.0 * (1.0 - roughness), 1.0)) * 0.3;
    sky += float3(1.0, 0.95, 0.85) * (sunGlow + sunHaze);
    
    // Ground color for downward reflections
    float3 ground = float3(0.15, 0.12, 0.10);
    sky = mix(ground, sky, smoothstep(-0.05, 0.1, dir.y));
    
    // For rough surfaces, converge towards hemisphere irradiance average
    // (simulates pre-filtered environment map without cubemap generation)
    float3 avgIrradiance = float3(0.35, 0.40, 0.52);
    sky = mix(sky, avgIrradiance, roughness * roughness * 0.7);
    
    return max(sky, 0.0);
}

// ── Fresnel with roughness for IBL ──────────────────────────────────────────
float3 fresnelSchlickRoughness_PBR(float cosTheta, float3 F0, float roughness) {
    return F0 + (max(float3(1.0 - roughness), F0) - F0) * pow(saturate(1.0 - cosTheta), 5.0);
}

// ── Environment BRDF Approximation (Lazarov 2013) ───────────────────────────
// Analytical fit replacing the split-sum LUT — avoids an extra texture lookup.
float2 envBRDFApprox(float NdotV, float roughness) {
    float4 r = roughness * float4(-1.0, -0.0275, -0.572, 0.022) + float4(1.0, 0.0425, 1.04, -0.04);
    float a004 = min(r.x * r.x, exp2(-9.28 * NdotV)) * r.x + r.y;
    return float2(-1.04, 1.04) * a004 + r.zw;
}

// ── ACES Fitted Tonemapping (Stephen Hill RRT+ODT) ──────────────────────────
// Full color-space transform through ACEScg for accurate hue preservation.
float3 ACESFitted(float3 color) {
    const float3x3 ACESInputMat = float3x3(
        float3(0.59719, 0.07600, 0.02840),
        float3(0.35458, 0.90834, 0.13383),
        float3(0.04823, 0.01566, 0.83777)
    );
    const float3x3 ACESOutputMat = float3x3(
        float3( 1.60475, -0.10208, -0.00327),
        float3(-0.53108,  1.10813, -0.07276),
        float3(-0.07367, -0.00605,  1.07602)
    );
    color = ACESInputMat * color;
    float3 a = color * (color + 0.0245786) - 0.000090537;
    float3 b = color * (0.983729 * color + 0.4329510) + 0.238081;
    color = a / b;
    color = ACESOutputMat * color;
    return saturate(color);
}

// ── Horizon-based Specular Occlusion ────────────────────────────────────────
// Reflection vectors that dip below the surface horizon are physically
// impossible — smoothly darken as R approaches the geometric horizon.
float computeHorizonAO(float3 N, float3 R) {
    float horizon = saturate(1.0 + dot(R, N));
    return horizon * horizon;
}

// ── Specular Occlusion from material AO (Marmoset Toolbag) ──────────────────
// Derives specular occlusion from the diffuse AO term. Rougher surfaces get
// more occlusion; smooth mirrors are mostly unaffected by cavity darkening.
float specularOcclusionFromAO(float NdotV, float ao, float roughness) {
    return saturate(pow(NdotV + ao, exp2(-16.0 * roughness - 1.0)) - 1.0 + ao);
}

// ── Multi-scatter Energy Compensation (Fdez-Aguera 2019) ────────────────────
// Single-scatter GGX BRDF loses energy at high roughness. This compensates
// for the missing multi-bounce light paths.
float3 multiscatterCompensation(float3 F0, float2 AB) {
    float3 FssEss = F0 * AB.x + AB.y;
    float  Ems    = 1.0 - (AB.x + AB.y);
    float3 Favg   = F0 + (1.0 - F0) / 21.0;
    return FssEss + Ems * FssEss * Favg / (1.0 - Favg * Ems);
}

// Optimized PBR BRDF
float3 fresnelSchlick(float cosTheta, float3 F0) {
    return F0 + (1.0 - F0) * pow(1.0 - cosTheta, 5.0);
}

float distributionGGX(float NdotH, float roughness) {
    float alpha = roughness * roughness;
    float alpha2 = alpha * alpha;
    float denom = NdotH * NdotH * (alpha2 - 1.0) + 1.0;
    return alpha2 / (PI * denom * denom);
}

float geometrySmith(float NdotV, float NdotL, float roughness) {
    float k = (roughness * roughness) / 2.0;
    float ggx1 = NdotV / (NdotV * (1.0 - k) + k);
    float ggx2 = NdotL / (NdotL * (1.0 - k) + k);
    return ggx1 * ggx2;
}

// Material data structure (matches C# MaterialData — float3 padded to float4 in Metal)
struct MaterialData {
    float4 albedo;      // xyz=albedo, w=metallic
    float  roughness;
    float  ao;
    float  emission;
    float  subsurface;
    int    useAlbedoTex;
    int    useNormalTex;
    int    useRMATex;
    int    blendMode;   // 0=Opaque, 1=AlphaTest, 2=AlphaBlend
    int    useOpacityTex; // Separate opacity/alpha map (map_d)
    int    _pad0;
    int    _pad1;
    int    _pad2;
};

// UV passthrough — MeshVaryings now carries UV for texture sampling in fs_mesh

fragment float4 fs_mesh(MeshVaryings in [[stage_in]],
                        bool isFrontFace [[front_facing]],
                        constant ViewUniforms& view [[buffer(10)]],
                        constant MaterialData& material [[buffer(11)]],
                        constant LightData_PBR* lights [[buffer(13)]],
                        constant int& lightCount [[buffer(14)]],
                        constant LightingSettings_PBR& lightSettings [[buffer(15)]],
                        depth2d<float> shadowMap [[texture(1)]],
                        texture2d<float> albedoTex [[texture(2)]],
                        texture2d<float> normalTex [[texture(3)]],
                        texture2d<float> rmaTex    [[texture(4)]],
                        texture2d<float> opacityTex [[texture(5)]])
{
    // ═══════════════════════════════════════════════════════════════════════════
    // PRODUCTION-QUALITY PBR SHADER WITH TWO-SIDED LIGHTING
    // ═══════════════════════════════════════════════════════════════════════════
    
    constexpr sampler texSampler(coord::normalized, filter::linear,
                                 mip_filter::linear, address::repeat);
    
    // ── Material Properties ────────────────────────────────────────────────────
    // albedo.xyz = color, albedo.w = metallic (packed to avoid float3 padding gap)
    float3 albedo   = material.albedo.xyz;
    float  metallic = material.albedo.w;
    if (material.useAlbedoTex != 0)
        albedo = albedoTex.sample(texSampler, in.uv).rgb;
    
    float roughness  = max(0.04, material.roughness);
    float ao         = material.ao;
    float subsurface = material.subsurface;
    
    // Sample RMA (Roughness/Metallic/AO packed) if assigned
    if (material.useRMATex != 0)
    {
        float3 rma = rmaTex.sample(texSampler, in.uv).rgb;
        roughness = max(0.04, rma.r);
        metallic  = rma.g;
        ao        = rma.b;
    }
    
    // ── Geometry Setup ─────────────────────────────────────────────────────────
    float3 V = normalize(view.cameraPos.xyz - in.worldPos);
    
    float3 N = normalize(in.normal);
    if (!isFrontFace) N = -N;
    
    // Perturb normal from normal map if assigned
    if (material.useNormalTex != 0)
    {
        float3 tangentNormal = normalTex.sample(texSampler, in.uv).rgb * 2.0 - 1.0;
        // Build TBN from geometry normal (simplified — no tangent attribute yet)
        float3 up = abs(N.y) < 0.999 ? float3(0,1,0) : float3(1,0,0);
        float3 T  = normalize(cross(up, N));
        float3 B  = cross(N, T);
        N = normalize(T * tangentNormal.x + B * tangentNormal.y + N * tangentNormal.z);
    }
    
    // Light direction (sun)
    float3 L = normalize(-view.sunDirection);
    
    // Half vector for specular
    float3 H = normalize(L + V);
    
    // ── Dot Products ───────────────────────────────────────────────────────────
    float NdotL = max(dot(N, L), 0.0);
    float NdotV = max(dot(N, V), 0.0);
    float NdotH = max(dot(N, H), 0.0);
    float VdotH = max(dot(V, H), 0.0);
    
    // ═══════════════════════════════════════════════════════════════════════════
    // OPTIMIZED PBR BRDF — half precision, manual pow5, Hammon visibility
    // ═══════════════════════════════════════════════════════════════════════════
    
    // ── Fresnel (Schlick — manual pow5: 3 muls vs log-mul-exp) ──────────────────
    half3 hF0 = half3(mix(float3(0.04), albedo, metallic));
    half hVdotH = half(VdotH);
    half ft = 1.0h - hVdotH; half ft2 = ft * ft; half ft5 = ft2 * ft2 * ft;
    half3 F = hF0 + (1.0h - hF0) * ft5;
    
    // ── GGX NDF ────────────────────────────────────────────────────────────────
    half hAlpha = half(roughness * roughness);
    half hAlpha2 = hAlpha * hAlpha;
    half hNdotH = half(NdotH);
    half hDenom = hNdotH * hNdotH * (hAlpha2 - 1.0h) + 1.0h;
    half D = hAlpha2 * half(INV_PI) / (hDenom * hDenom);
    
    // ── Visibility (Hammon 2017 — replaces Smith G + 4NLV denominator) ──────────
    half hNdotL = half(NdotL);
    half hNdotV = half(NdotV);
    half vis = 0.5h / max(mix(2.0h * hNdotL * hNdotV, hNdotL + hNdotV, hAlpha), half(0.001));
    
    // ── Combined Specular + Diffuse BRDF ───────────────────────────────────────
    half3 specular = D * F * vis;
    half3 kD = (1.0h - F) * (1.0h - half(metallic));
    half3 hAlbedo = half3(albedo);
    half3 diffuse = kD * hAlbedo * half(INV_PI);
    
    // ── Subsurface Scattering (half precision) ─────────────────────────────────
    half hSub = half(subsurface);
    half wrap = 0.5h * hSub;
    half wrappedNdotL = saturate((hNdotL + wrap) / ((1.0h + wrap) * (1.0h + wrap)));
    half3 sss = hAlbedo * wrappedNdotL * hSub * half3(1.0, 0.4, 0.3);
    
    // ── Translucency (manual pow4 instead of pow()) ────────────────────────────
    float3 H_trans = normalize(L + N * 0.5);
    half hTD = half(saturate(dot(V, -H_trans)));
    half hTD2 = hTD * hTD; half hTD4 = hTD2 * hTD2;
    half3 translucency = hAlbedo * hTD4 * hSub * half3(1.0, 0.5, 0.3) * 0.5h;
    
    // ═══════════════════════════════════════════════════════════════════════════
    // LIGHTING ACCUMULATION
    // ═══════════════════════════════════════════════════════════════════════════
    
    // ── Direct Lighting (Sun) ──────────────────────────────────────────────────
    half3 directLight = (diffuse + specular) * hNdotL * 3.5h;
    
    // ── Additional Lights (half-precision, Hammon visibility) ────────────────────
    int maxL = min(lightCount, lightSettings.maxLights);
    for (int li = 0; li < maxL; li++) {
        LightData_PBR light = lights[li];
        if (light.type == 0) continue;
        
        float3 toLight = float3(light.position) - in.worldPos;
        float dist = length(toLight);
        if (dist >= light.range) continue;
        float3 Ll = toLight / dist;
        
        half lNdotL = half(max(dot(N, Ll), 0.0));
        if (lNdotL <= 0.0h) continue;
        
        half rangeFade = half(saturate(1.0 - dist / light.range));
        half atten = rangeFade * rangeFade / (1.0h + half(dist * dist * light.attenuation));
        
        if (light.type == 2) {
            float spotDot = dot(-Ll, normalize(float3(light.direction)));
            float spotAng = acos(saturate(spotDot));
            if (spotAng > light.outerAngle) continue;
            if (spotAng > light.innerAngle) {
                half t = half((spotAng - light.innerAngle) / (light.outerAngle - light.innerAngle));
                atten *= 1.0h - t * t;
            }
        }
        
        float3 lH = normalize(V + Ll);
        half lNdotH = half(max(dot(N, lH), 0.0));
        half lVdotH = half(max(dot(V, lH), 0.0));
        
        // Fast Fresnel (manual pow5)
        half lft = 1.0h - lVdotH; half lft2 = lft * lft; half lft5 = lft2 * lft2 * lft;
        half3 lF = hF0 + (1.0h - hF0) * lft5;
        // GGX + Hammon visibility
        half lDenom = lNdotH * lNdotH * (hAlpha2 - 1.0h) + 1.0h;
        half lD = hAlpha2 * half(INV_PI) / (lDenom * lDenom);
        half lVis = 0.5h / max(mix(2.0h * lNdotL * hNdotV, lNdotL + hNdotV, hAlpha), half(0.001));
        
        half3 lSpec = lD * lF * lVis;
        half3 lkD = (1.0h - lF) * (1.0h - half(metallic));
        half3 lRad = half3(float3(light.color)) * half(light.intensity) * atten;
        directLight += (lkD * hAlbedo * half(INV_PI) + lSpec) * lRad * lNdotL;
    }
    
    // ── Environment Reflections (half precision IBL) ────────────────────────────
    float3 R = reflect(-V, N);
    half3 hF_env = half3(fresnelSchlickRoughness_PBR(NdotV, float3(hF0), roughness));
    
    // Diffuse irradiance from hemisphere
    half hSkyFac = half(N.y) * 0.5h + 0.5h;
    half3 irradiance = mix(half3(0.18, 0.15, 0.12), half3(0.5, 0.6, 0.8), hSkyFac);
    half3 kD_env = (1.0h - hF_env) * (1.0h - half(metallic));
    half3 ambientDiffuse = kD_env * irradiance * hAlbedo;
    
    // Specular reflection from sky
    half3 prefilteredColor = half3(sampleSkyEnv(R, view.sunDirection, roughness));
    half2 AB = half2(envBRDFApprox(NdotV, roughness));
    half3 specEnergy = half3(multiscatterCompensation(float3(hF0), float2(AB)));
    half3 ambientSpecular = prefilteredColor * specEnergy;
    
    // Horizon + specular occlusion
    half horizonAO = half(computeHorizonAO(N, R));
    half specAO = half(specularOcclusionFromAO(NdotV, ao, roughness));
    ambientSpecular *= horizonAO * specAO;
    
    half hAO = half(ao);
    half3 ambient = (ambientDiffuse + ambientSpecular) * hAO;
    
    // ── Rim Lighting (reuses irradiance — saves second sampleSkyEnv call) ──────
    half hRim = 1.0h - hNdotV; hRim = hRim * hRim * hRim * hRim; // manual pow4
    half3 rimLight = irradiance * hRim * 0.15h * (1.0h - half(metallic));
    
    // ── Shadow Mapping with Enhanced PCF ───────────────────────────────────────
    float shadow = 1.0;
    float3 projCoords = in.lightSpacePos.xyz / in.lightSpacePos.w;
    float2 shadowUV = projCoords.xy * 0.5 + 0.5;
    shadowUV.y = 1.0 - shadowUV.y; // Flip Y for Metal
    
    if (shadowUV.x >= 0.0 && shadowUV.x <= 1.0 && 
        shadowUV.y >= 0.0 && shadowUV.y <= 1.0 && 
        projCoords.z >= 0.0 && projCoords.z <= 1.0) {
        
        constexpr sampler shadowSampler(coord::normalized, filter::linear, 
                                       address::clamp_to_edge, compare_func::less);
        
        // Adaptive bias based on surface angle (prevents shadow acne)
        float bias = max(0.008 * (1.0 - NdotL), 0.002);
        float currentDepth = projCoords.z - bias;
        
        // Rotated 4-tap PCF (Jimenez 2014 interleaved gradient noise)
        // 4x fewer texture reads than 16-tap Poisson, rotation breaks banding
        float2 texelSize = 1.0 / float2(shadowMap.get_width(), shadowMap.get_height());
        float3 magic = float3(0.06711056, 0.00583715, 52.9829189);
        float angle = fract(magic.z * fract(dot(floor(in.position.xy), magic.xy))) * 6.2832;
        float sa = sin(angle), ca = cos(angle);
        float2x2 rot = float2x2(ca, sa, -sa, ca);
        
        const float2 taps[4] = { float2(-0.5,-0.5), float2(0.5,-0.5), float2(-0.5,0.5), float2(0.5,0.5) };
        float shadowSum = 0.0;
        for (int i = 0; i < 4; ++i) {
            shadowSum += shadowMap.sample_compare(shadowSampler, shadowUV + rot * taps[i] * texelSize * 2.0, currentDepth);
        }
        shadow = shadowSum * 0.25;
        
        // Soften shadow transition (never fully black for more natural look)
        shadow = mix(0.35, 1.0, shadow);
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // FINAL COMPOSITION
    // ═══════════════════════════════════════════════════════════════════════════
    
    // Combine all lighting components (half → float at composition boundary)
    float3 finalColor = float3(ambient) + float3(directLight + sss + translucency) * shadow + float3(rimLight);
    
    // Add emission if present
    finalColor += albedo * material.emission;
    
    // Exposure from lighting settings
    finalColor *= lightSettings.exposure;
    
    // ── Tone Mapping (ACES Fitted — Stephen Hill RRT+ODT) ──────────────────────
    finalColor = ACESFitted(finalColor);
    
    // Subtle color grading for natural warmth
    finalColor *= float3(1.02, 1.0, 0.98);
    
    // ── Gamma Correction (sRGB) ────────────────────────────────────────────────
    finalColor = pow(finalColor, float3(1.0 / 2.2));
    
    // ── Output ─────────────────────────────────────────────────────────────────
    float outAlpha = in.color.a;
    
    // Only use texture alpha for transparency modes (AlphaTest or AlphaBlend)
    if (material.blendMode != 0) {
        // Priority 1: Dedicated opacity texture (map_d as separate file)
        if (material.useOpacityTex != 0) {
            outAlpha *= opacityTex.sample(texSampler, in.uv).r; // map_d is grayscale — use red channel
        }
        // Priority 2: Albedo texture alpha channel (map_d == map_Kd, or PNG with embedded alpha)
        else if (material.useAlbedoTex != 0) {
            outAlpha *= albedoTex.sample(texSampler, in.uv).a;
        }
    }

    // Alpha Test (Masking) — hard cutoff, no blending
    if (material.blendMode == 1) { // AlphaTest
        if (outAlpha < 0.5) discard_fragment();
        outAlpha = 1.0; // Opaque after test
    }
    else if (material.blendMode == 0) { // Opaque
        outAlpha = 1.0;
    }
    
    return float4(finalColor, outAlpha);
}

// ═════════════════════════════════════════════════════════════════════════════
// WIREFRAME PASS — super thin outline for 3D depth perception
// ═════════════════════════════════════════════════════════════════════════════

fragment float4 fs_wireframe(MeshVaryings in [[stage_in]])
{
    // Super subtle dark gray wireframe — razor sharp but not cartoonish
    // Alpha ~0.3 gives it that "technical drawing" look
    float3 wireColor = float3(0.15, 0.15, 0.18); // Dark blue-gray
    float alpha = 0.25; // Very subtle
    return float4(wireColor, alpha);
}

// ═════════════════════════════════════════════════════════════════════════════
// EDITOR GIZMO — transform handles (translate / rotate / scale)
// ═════════════════════════════════════════════════════════════════════════════

struct GizmoUniforms {
    float4x4 viewProj;
    float4x4 model;     // Gizmo world transform (at entity position)
    float4   color;     // Axis color (R/G/B per axis)
    float    gizmoType; // 0 = translate, 1 = rotate, 2 = scale
    float    axisId;    // 0 = X, 1 = Y, 2 = Z, 3 = center
    float    isHovered; // 1.0 when mouse is over this gizmo element
    float    _pad;
};

struct GizmoVertexIn {
    float3 position [[attribute(0)]];
    float3 normal   [[attribute(1)]];
    float2 uv       [[attribute(2)]];
};

struct GizmoVaryings {
    float4 position [[position]];
    float3 worldPos;
    float3 normal;
    float4 color;
    float  hovered;
};

vertex GizmoVaryings vs_gizmo(GizmoVertexIn in [[stage_in]],
                               constant GizmoUniforms& u [[buffer(10)]])
{
    GizmoVaryings out;
    float4 worldPos = u.model * float4(in.position, 1.0);
    out.worldPos = worldPos.xyz;
    out.position = u.viewProj * worldPos;
    out.normal = normalize((u.model * float4(in.normal, 0.0)).xyz);
    out.color = u.color;
    out.hovered = u.isHovered;
    return out;
}

fragment float4 fs_gizmo(GizmoVaryings in [[stage_in]])
{
    // Gizmo shading: solid axis color with subtle lighting for 3D feel
    float3 lightDir = normalize(float3(0.3, 0.8, 0.5));
    float3 N = normalize(in.normal);
    
    // Simple hemisphere lighting so gizmos are always visible
    float NdotL = dot(N, lightDir) * 0.4 + 0.6; // Wrapped diffuse
    
    float3 baseColor = in.color.rgb;
    
    // Brighten when hovered for visual feedback
    float hover = in.hovered;
    baseColor = mix(baseColor, min(baseColor * 1.6 + 0.1, float3(1.0)), hover * 0.6);
    
    float3 finalColor = baseColor * NdotL;
    
    // Slight rim highlight for visibility against dark backgrounds
    float rim = pow(1.0 - max(abs(N.z), 0.0), 2.0) * 0.3;
    finalColor += float3(rim);
    
    // Gizmos are semi-transparent so you can see through them slightly
    float alpha = mix(0.85, 1.0, hover);
    
    return float4(finalColor, alpha);
}

// ═══════════════════════════════════════════════════════════════════════════════
// SCREEN-SPACE REFLECTIONS (SSR) — Quarter-res ray marching
// Optimized for Intel HD-class integrated GPUs.
// ═══════════════════════════════════════════════════════════════════════════════

struct SSRUniforms {
    float4x4 projection;
    float4x4 invProjection;
    float4x4 view;
    float4x4 invView;
    float4x4 viewProj;
    float4x4 invViewProj;
    float2   resolution;
    float2   invResolution;
    float    maxDistance;
    float    thickness;
    float    stride;
    int      maxSteps;
};

struct SSRVaryings {
    float4 position [[position]];
    float2 uv;
};

vertex SSRVaryings vs_ssr(uint vid [[vertex_id]]) {
    SSRVaryings out;
    out.uv = float2((vid << 1) & 2, vid & 2);
    out.position = float4(out.uv * 2.0 - 1.0, 0.0, 1.0);
    out.uv.y = 1.0 - out.uv.y;
    return out;
}

fragment float4 fs_ssr(SSRVaryings in [[stage_in]],
                       constant SSRUniforms& u [[buffer(0)]],
                       texture2d<float> sceneColor [[texture(0)]],
                       depth2d<float> sceneDepth  [[texture(1)]]) {
    
    constexpr sampler samp(coord::normalized, filter::linear, address::clamp_to_edge);
    
    float2 uv = in.uv;
    float4 baseColor = sceneColor.sample(samp, uv);
    float depth = sceneDepth.sample(samp, uv).r;
    
    if (depth >= 0.999) return baseColor;
    
    // Reconstruct view-space position from depth
    float2 ndc = float2(uv.x * 2.0 - 1.0, (1.0 - uv.y) * 2.0 - 1.0);
    float4 viewPosH = u.invProjection * float4(ndc, depth, 1.0);
    float3 viewPos = viewPosH.xyz / viewPosH.w;
    
    // Reconstruct normal from depth derivatives
    float3 viewN = normalize(cross(dfdy(viewPos), dfdx(viewPos)));
    
    // Reflection vector
    float3 viewDir = normalize(viewPos);
    float3 viewR = reflect(viewDir, viewN);
    
    if (viewR.z > -0.01) return baseColor;
    
    // Project ray endpoints to screen space
    float3 rayEnd = viewPos + viewR * u.maxDistance;
    float4 startClip = u.projection * float4(viewPos, 1.0);
    float4 endClip   = u.projection * float4(rayEnd, 1.0);
    
    float2 startUV = (startClip.xy / startClip.w) * 0.5 + 0.5;
    float2 endUV   = (endClip.xy / endClip.w) * 0.5 + 0.5;
    startUV.y = 1.0 - startUV.y;
    endUV.y   = 1.0 - endUV.y;
    
    float startZ = startClip.z / startClip.w;
    float endZ   = endClip.z / endClip.w;
    
    // Per-pixel jitter (Jimenez IGN)
    float noise = fract(52.9829189 * fract(dot(floor(in.position.xy), float2(0.06711056, 0.00583715))));
    
    // Linear ray march
    int steps = u.maxSteps;
    float2 deltaUV = (endUV - startUV) / float(steps);
    float  deltaZ  = (endZ - startZ) / float(steps);
    
    float2 marchUV = startUV + deltaUV * (1.0 + noise);
    float  marchZ  = startZ  + deltaZ  * (1.0 + noise);
    
    half3 reflColor = half3(0);
    half  reflStrength = 0.0h;
    
    for (int i = 0; i < steps; i++) {
        if (marchUV.x < 0.0 || marchUV.x > 1.0 || marchUV.y < 0.0 || marchUV.y > 1.0) break;
        
        float sceneZ = sceneDepth.sample(samp, marchUV);
        float diff = marchZ - sceneZ;
        
        if (diff > 0.0 && diff < u.thickness) {
            reflColor = half3(sceneColor.sample(samp, marchUV).rgb);
            
            half2 edgeFade = half2(smoothstep(0.0, 0.05, marchUV) * (1.0 - smoothstep(0.95, 1.0, marchUV)));
            half fade = edgeFade.x * edgeFade.y;
            fade *= half(1.0 - float(i) / float(steps));
            
            half NdotV = half(saturate(-dot(viewDir, viewN)));
            half fresnel = 1.0h - NdotV;
            fresnel = fresnel * fresnel;
            fade *= max(fresnel, 0.15h);
            
            reflStrength = fade;
            break;
        }
        
        marchUV += deltaUV;
        marchZ  += deltaZ;
    }
    
    float3 result = mix(baseColor.rgb, float3(reflColor), float(reflStrength));
    return float4(result, baseColor.a);
}
