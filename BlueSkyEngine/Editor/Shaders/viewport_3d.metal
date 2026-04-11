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
    float3   cameraPos;
    float    time;
    float3   sunDirection; // Normalized sun direction
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
    // Full-screen triangle trick: 3 vertices cover [-1,1] NDC without a buffer.
    float2 uv = float2((vertexID << 1) & 2, vertexID & 2);
    SkyVaryings out;
    out.position = float4(uv * 2.0 - 1.0, 0.9999, 1.0); // behind everything
    out.uv = uv;

    // Reconstruct world-space ray direction
    float4 clipPos = float4(uv * 2.0 - 1.0, 1.0, 1.0);
    float4 worldPosH = u.invViewProj * clipPos;
    float3 worldPos = worldPosH.xyz / worldPosH.w;
    out.rayDir = normalize(worldPos - u.cameraPos);

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

// Compute realistic daytime sky with clouds matching the reference photo
float3 computeSky(float3 rayDir, float3 sunDir, float time) {
    float height = rayDir.y;
    float sunElevation = sunDir.y;
    
    // Richer, more saturated sky colors (fixed washed out look)
    // Deep blue at top
    float3 deepBlue = float3(0.1, 0.35, 0.75);     
    // Medium blue
    float3 midBlue = float3(0.25, 0.55, 0.9);      
    // Light blue at horizon
    float3 lightBlue = float3(0.5, 0.75, 0.98);    
    // Soft horizon blend
    float3 horizonColor = float3(0.7, 0.85, 1.0); 
    
    // Height factor for gradient (0 at bottom, 1 at top)
    float t = saturate(height * 0.5 + 0.5);
    
    // Create smooth gradient
    float3 skyColor;
    if (t > 0.5) {
        // Upper sky: blend from midBlue to deepBlue
        float upperBlend = smoothstep(0.5, 1.0, t);
        skyColor = mix(midBlue, deepBlue, upperBlend);
    } else {
        // Lower sky: blend from horizon to midBlue
        float lowerBlend = smoothstep(0.0, 0.5, t);
        skyColor = mix(horizonColor, lightBlue, lowerBlend);
        skyColor = mix(skyColor, midBlue, lowerBlend * lowerBlend);
    }
    
    // Procedural clouds with lower density
    float2 cloudUV = rayDir.xz * 1.5 + float2(time * 0.01, time * 0.005);
    float cloudNoise = fbm(cloudUV);
    float cloudDetail = fbm(cloudUV * 2.5) * 0.4;
    float cloudShape = cloudNoise + cloudDetail * 0.3;
    
    // Cloud coverage (lower = fewer clouds)
    float cloudDensity = smoothstep(0.5, 0.7, cloudShape);
    float cloudEdge = smoothstep(0.4, 0.6, cloudShape) - cloudDensity;
    
    // Cloud lighting
    float sunDot = dot(rayDir, sunDir);
    float cloudBrightness = smoothstep(-0.3, 0.8, sunDot);
    
    // Softer cloud colors
    float3 cloudLit = float3(1.0, 1.0, 0.98);
    float3 cloudShadow = float3(0.75, 0.8, 0.88);
    float3 cloudColor = mix(cloudShadow, cloudLit, cloudBrightness);
    
    // Blend clouds
    float3 finalColor = mix(skyColor, cloudColor, cloudDensity * 0.7);
    finalColor = mix(finalColor, skyColor, cloudEdge * 0.2);
    
    // Sun (positioned high like in the photo)
    float cosTheta = dot(rayDir, sunDir);
    float sunAngle = acos(saturate(cosTheta));
    
    // Subtle sun glow
    float sunGlow = exp(-sunAngle * sunAngle * 30.0) * 0.3;
    float sunDisc = smoothstep(0.02, 0.01, sunAngle);
    finalColor += float3(1.0, 0.98, 0.95) * (sunGlow + sunDisc);
    
    // Very subtle horizon haze
    float horizonHaze = exp(-height * 3.0) * 0.15;
    finalColor = mix(finalColor, float3(0.75, 0.88, 1.0), horizonHaze);
    
    // Simple exposure (no tone mapping that washes out colors)
    finalColor = saturate(finalColor * 0.95);
    
    // Gamma correction
    finalColor = pow(finalColor, float3(1.0 / 2.2));
    
    return finalColor;
}

fragment float4 fs_sky(SkyVaryings in [[stage_in]],
                       constant ViewUniforms& u [[buffer(10)]])
{
    // Compute realistic sky with clouds
    float3 skyColor = computeSky(in.rayDir, u.sunDirection, u.time);

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

// Grid line pattern with anti-aliased edges
float4 gridPattern(float3 worldPos, float scale, float lineWidth) {
    float2 coord = worldPos.xz * scale;
    float2 derivative = fwidth(coord);
    float2 grid = abs(fract(coord - 0.5) - 0.5) / derivative;
    float line = min(grid.x, grid.y);
    float alpha = 1.0 - min(line, 1.0);
    return float4(float3(0.35, 0.35, 0.40), alpha);
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
    float dist = length(hitPos - u.cameraPos);
    float fadeFar = 1.0 - smoothstep(15.0, 80.0, dist);

    if (fadeFar < 0.001) {
        discard_fragment();
    }

    // Multi-resolution grid: 1-unit fine + 10-unit coarse
    float4 fineGrid   = gridPattern(hitPos, 1.0, 1.0);
    float4 coarseGrid = gridPattern(hitPos, 0.1, 1.5);

    // Combine grids — coarse on top of fine
    float4 gridColor = fineGrid;
    gridColor.a = max(fineGrid.a * 0.4, coarseGrid.a * 0.7);
    gridColor.rgb = mix(fineGrid.rgb, coarseGrid.rgb * 1.2, coarseGrid.a);

    // Axis lines — X axis = red, Z axis = blue
    float axisWidth = 0.06;
    float2 axisDerivative = fwidth(hitPos.xz);
    
    // X axis (Z ≈ 0): draw red line
    float xAxisDist = abs(hitPos.z) / axisDerivative.y;
    float xAxisLine = 1.0 - min(xAxisDist, 1.0);
    
    // Z axis (X ≈ 0): draw blue line
    float zAxisDist = abs(hitPos.x) / axisDerivative.x;
    float zAxisLine = 1.0 - min(zAxisDist, 1.0);

    if (xAxisLine > 0.01) {
        gridColor.rgb = mix(gridColor.rgb, float3(0.85, 0.20, 0.18), xAxisLine);
        gridColor.a = max(gridColor.a, xAxisLine * 0.9);
    }
    if (zAxisLine > 0.01) {
        gridColor.rgb = mix(gridColor.rgb, float3(0.20, 0.35, 0.85), zAxisLine);
        gridColor.a = max(gridColor.a, zAxisLine * 0.9);
    }

    // Apply distance fade
    gridColor.a *= fadeFar;

    // Near-camera fade to avoid z-fighting / Moiré at feet
    float fadeNear = smoothstep(0.3, 1.5, dist);
    gridColor.a *= fadeNear;

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
    float2 uv       [[attribute(2)]]; // Matches 32-byte vertex layout (even if unused)
};

struct MeshVaryings {
    float4 position [[position]];
    float4 lightSpacePos;
    float3 worldPos;
    float3 normal;
    float4 color;
};

vertex MeshVaryings vs_mesh(MeshVertexIn in [[stage_in]],
                           constant EntityUniforms& entity [[buffer(30)]],
                           constant ViewUniforms& view [[buffer(10)]])
{
    MeshVaryings out;
    
    // Transform to world space
    float4 worldPos = entity.model * float4(in.position, 1.0);
    out.worldPos = worldPos.xyz;
    
    // Transform to clip space
    out.position = view.viewProj * worldPos;
    
    // Transform normal to world space (simplified - assumes uniform scale)
    out.normal = normalize((entity.model * float4(in.normal, 0.0)).xyz);
    
    // Transform to light space
    out.lightSpacePos = view.lightSpaceMatrix * worldPos;
    
    // Pass color through
    out.color = entity.color;
    
    return out;
}

// ═════════════════════════════════════════════════════════════════════════════
// SHADOW PASS
// ═════════════════════════════════════════════════════════════════════════════

struct ShadowVaryings {
    float4 position [[position]];
};

vertex ShadowVaryings vs_shadow(MeshVertexIn in [[stage_in]],
                                constant EntityUniforms& entity [[buffer(30)]],
                                constant ViewUniforms& view [[buffer(10)]])
{
    ShadowVaryings out;
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

fragment float4 fs_mesh(MeshVaryings in [[stage_in]],
                        constant ViewUniforms& view [[buffer(10)]],
                        depth2d<float> shadowMap [[texture(1)]])
{
    // PBR Material properties (using color as albedo for now)
    float3 albedo = in.color.rgb;
    float metallic = 0.0;  // Ceramic teapot
    float roughness = 0.3; // Slightly rough ceramic
    float ao = 1.0;
    
    // View and light vectors
    float3 N = normalize(in.normal);
    float3 V = normalize(view.cameraPos - in.worldPos);
    float3 L = normalize(-view.sunDirection);
    float3 H = normalize(V + L);
    
    float NdotV = max(dot(N, V), 0.0);
    float NdotL = max(dot(N, L), 0.0);
    float NdotH = max(dot(N, H), 0.0);
    float HdotV = max(dot(H, V), 0.0);
    
    // PBR BRDF
    float3 F0 = float3(0.04);
    F0 = mix(F0, albedo, metallic);
    
    float3 F = fresnelSchlick(HdotV, F0);
    float D = distributionGGX(NdotH, roughness);
    float G = geometrySmith(NdotV, NdotL, roughness);
    
    // Specular
    float3 numerator = D * G * F;
    float denominator = 4.0 * NdotV * NdotL + 0.001;
    float3 specular = numerator / denominator;
    
    // Diffuse
    float3 kS = F;
    float3 kD = float3(1.0) - kS;
    kD *= 1.0 - metallic;
    float3 diffuse = kD * albedo / PI;
    
    // Sun color (warm white)
    float3 sunColor = float3(1.0, 0.95, 0.8);
    float3 sunIntensity = float3(3.0); // Bright sun
    
    // Ambient (sky color)
    float3 ambientColor = float3(0.2, 0.3, 0.5) * 0.5;
    float3 ambient = ambientColor * albedo * ao;
    
    // Direct lighting
    float3 radiance = sunIntensity * sunColor;
    float3 Lo = (diffuse + specular) * radiance * NdotL;
    
    // Shadow mapping
    float3 projCoords = in.lightSpacePos.xyz / in.lightSpacePos.w;
    float2 shadowUV = projCoords.xy * 0.5 + 0.5;
    shadowUV.y = 1.0 - shadowUV.y;
    
    constexpr sampler shadowSampler(coord::normalized, filter::linear, address::clamp_to_edge, compare_func::less);
    float shadow = 1.0;
    
    if (shadowUV.x >= 0.0 && shadowUV.x <= 1.0 && shadowUV.y >= 0.0 && shadowUV.y <= 1.0 && projCoords.z >= 0.0 && projCoords.z <= 1.0) {
        float bias = max(0.005 * (1.0 - dot(N, L)), 0.001);
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
    
    // Combine with shadow
    float3 finalColor = ambient + Lo * mix(0.3, 1.0, shadow);
    
    // Tone mapping (ACES approximation)
    finalColor = finalColor / (finalColor + float3(1.0));
    
    // Gamma correction
    finalColor = pow(finalColor, float3(1.0 / 2.2));
    
    return float4(finalColor, in.color.a);
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
