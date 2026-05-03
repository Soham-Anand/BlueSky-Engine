// Horizon Lighting System Shader
// Feature-packed lighting optimized for old hardware
// Supports: Clustered lighting, IBL, volumetrics, contact shadows

#include <metal_stdlib>
using namespace metal;

// Constants
constant float PI = 3.14159265359;
constant float MIN_ROUGHNESS = 0.01;

// ============================================================================
// Structures
// ============================================================================

struct VertexIn {
    float3 position [[attribute(0)]];
    float3 normal   [[attribute(1)]];
    float2 uv       [[attribute(2)]];
};

struct VertexOut {
    float4 position      [[position]];
    float3 worldPos;
    float3 normal;
    float2 uv;
    float2 screenUV;
    float  depth;
};

struct ViewUniforms {
    float4x4 viewProj;
    float4x4 view;
    float4x4 invView;
    packed_float3 cameraPos;
    float time;
    float2 screenSize;
    float nearPlane;
    float farPlane;
};

struct MaterialData {
    packed_float3 albedo;
    float  metallic;
    float  roughness;
    float  ao;
    float  emission;
    
    int    useAlbedoTex;
    int    useNormalTex;
    int    useRMATex;
};

// Light types
#define LIGHT_TYPE_DIRECTIONAL 0
#define LIGHT_TYPE_POINT       1
#define LIGHT_TYPE_SPOT        2
#define LIGHT_TYPE_AREA        3

struct LightData {
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
};

struct LightingSettings {
    int    quality;
    int    maxLights;
    int    enableIBL;
    int    enableVolumetrics;
    int    enableContactShadows;
    float  exposure;
    packed_float3 ambientColor;
};

// ============================================================================
// Vertex Shader
// ============================================================================

struct EntityUniforms {
    float4x4 model;
    float4 color;
};

vertex VertexOut horizon_vertex(VertexIn in [[stage_in]],
                                 constant ViewUniforms& view [[buffer(10)]],
                                 constant EntityUniforms& ent [[buffer(30)]])  // Entity data
{
    VertexOut out;
    
    float4 worldPos = ent.model * float4(in.position, 1.0);
    out.worldPos = worldPos.xyz;
    out.position = view.viewProj * worldPos;
    out.normal = normalize((ent.model * float4(in.normal, 0.0)).xyz);
    out.uv = in.uv;
    
    // Calculate screen UV and depth for clustering
    out.screenUV = (out.position.xy / out.position.w) * 0.5 + 0.5;
    out.depth = out.position.w;
    
    return out;
}

// ============================================================================
// BRDF Functions
// ============================================================================

float3 fresnelSchlick(float cosTheta, float3 F0) {
    return F0 + (1.0 - F0) * pow(1.0 - cosTheta, 5.0);
}

float3 fresnelSchlickRoughness(float cosTheta, float3 F0, float roughness) {
    return F0 + (max(1.0 - roughness, F0) - F0) * pow(1.0 - cosTheta, 5.0);
}

float distributionGGX(float NdotH, float roughness) {
    float alpha = roughness * roughness;
    float alpha2 = alpha * alpha;
    float NdotH2 = NdotH * NdotH;
    float denom = NdotH2 * (alpha2 - 1.0) + 1.0;
    return alpha2 / (PI * denom * denom);
}

// Simplified GGX for old hardware
float distributionBlinnPhong(float NdotH, float roughness) {
    float specPower = max(2.0, 128.0 * (1.0 - roughness));
    return pow(NdotH, specPower) * (specPower + 2.0) / (2.0 * PI);
}

float geometrySmithGGX(float NdotV, float NdotL, float roughness) {
    float k = (roughness * roughness) / 2.0;
    float ggx1 = NdotV / (NdotV * (1.0 - k) + k);
    float ggx2 = NdotL / (NdotL * (1.0 - k) + k);
    return ggx1 * ggx2;
}

// Fast geometry for low quality
float geometryImplicit(float NdotV, float NdotL) {
    return NdotV * NdotL;
}

// ============================================================================
// Lighting Functions
// ============================================================================

float3 calculateDirectionalLight(LightData light,
                                  float3 normal,
                                  float3 viewDir,
                                  float3 albedo,
                                  float metallic,
                                  float roughness,
                                  int quality)
{
    float3 L = -normalize(light.direction);
    float NdotL = max(dot(normal, L), 0.0);
    
    if (NdotL <= 0.0) return float3(0.0);
    
    float3 V = viewDir;
    float3 H = normalize(V + L);
    
    float NdotV = max(dot(normal, V), 0.0);
    float NdotH = max(dot(normal, H), 0.0);
    float HdotV = max(dot(H, V), 0.0);
    
    // Fresnel
    float3 F0 = mix(float3(0.04), albedo, metallic);
    float3 F = fresnelSchlick(HdotV, F0);
    
    // Distribution & Geometry based on quality
    float D, G;
    
    if (quality <= 1) {
        // Low quality: Blinn-Phong
        D = distributionBlinnPhong(NdotH, roughness);
        G = geometryImplicit(NdotV, NdotL);
    } else {
        // High quality: GGX
        D = distributionGGX(NdotH, roughness);
        G = geometrySmithGGX(NdotV, NdotL, roughness);
    }
    
    // Specular
    float3 specular = (D * G * F) / (4.0 * NdotV * NdotL + 0.001);
    
    // Diffuse
    float3 kS = F;
    float3 kD = (1.0 - kS) * (1.0 - metallic);
    float3 diffuse = kD * albedo / PI;
    
    // Combine
    float3 radiance = light.color * light.intensity;
    return (diffuse + specular) * radiance * NdotL;
}

float3 calculatePointLight(LightData light,
                            float3 worldPos,
                            float3 normal,
                            float3 viewDir,
                            float3 albedo,
                            float metallic,
                            float roughness,
                            int quality)
{
    float3 toLight = light.position - worldPos;
    float distance = length(toLight);
    
    if (distance >= light.range) return float3(0.0);
    
    float3 L = toLight / distance;
    float NdotL = max(dot(normal, L), 0.0);
    
    if (NdotL <= 0.0) return float3(0.0);
    
    float3 V = viewDir;
    float3 H = normalize(V + L);
    
    float NdotV = max(dot(normal, V), 0.0);
    float NdotH = max(dot(normal, H), 0.0);
    float HdotV = max(dot(H, V), 0.0);
    
    // Attenuation
    float attenuation = 1.0 / (1.0 + distance * distance * light.attenuation);
    float rangeFade = 1.0 - (distance / light.range);
    rangeFade *= rangeFade;
    
    // Fresnel
    float3 F0 = mix(float3(0.04), albedo, metallic);
    float3 F = fresnelSchlick(HdotV, F0);
    
    // Distribution & Geometry
    float D, G;
    
    if (quality <= 1) {
        D = distributionBlinnPhong(NdotH, roughness);
        G = geometryImplicit(NdotV, NdotL);
    } else {
        D = distributionGGX(NdotH, roughness);
        G = geometrySmithGGX(NdotV, NdotL, roughness);
    }
    
    float3 specular = (D * G * F) / (4.0 * NdotV * NdotL + 0.001);
    
    float3 kS = F;
    float3 kD = (1.0 - kS) * (1.0 - metallic);
    float3 diffuse = kD * albedo / PI;
    
    float3 radiance = light.color * light.intensity * attenuation * rangeFade;
    return (diffuse + specular) * radiance * NdotL;
}

float3 calculateSpotLight(LightData light,
                           float3 worldPos,
                           float3 normal,
                           float3 viewDir,
                           float3 albedo,
                           float metallic,
                           float roughness,
                           int quality)
{
    float3 toLight = light.position - worldPos;
    float distance = length(toLight);
    
    if (distance >= light.range) return float3(0.0);
    
    float3 L = toLight / distance;
    float NdotL = max(dot(normal, L), 0.0);
    
    if (NdotL <= 0.0) return float3(0.0);
    
    // Spot cone attenuation
    float spotDot = dot(-L, normalize(light.direction));
    float spotAngle = acos(spotDot);
    
    if (spotAngle > light.outerAngle) return float3(0.0);
    
    float spotAttenuation = 1.0;
    if (spotAngle > light.innerAngle) {
        float t = (spotAngle - light.innerAngle) / (light.outerAngle - light.innerAngle);
        spotAttenuation = 1.0 - (t * t);
    }
    
    float3 V = viewDir;
    float3 H = normalize(V + L);
    
    float NdotV = max(dot(normal, V), 0.0);
    float NdotH = max(dot(normal, H), 0.0);
    float HdotV = max(dot(H, V), 0.0);
    
    float attenuation = 1.0 / (1.0 + distance * distance * light.attenuation);
    
    float3 F0 = mix(float3(0.04), albedo, metallic);
    float3 F = fresnelSchlick(HdotV, F0);
    
    float D, G;
    
    if (quality <= 1) {
        D = distributionBlinnPhong(NdotH, roughness);
        G = geometryImplicit(NdotV, NdotL);
    } else {
        D = distributionGGX(NdotH, roughness);
        G = geometrySmithGGX(NdotV, NdotL, roughness);
    }
    
    float3 specular = (D * G * F) / (4.0 * NdotV * NdotL + 0.001);
    
    float3 kS = F;
    float3 kD = (1.0 - kS) * (1.0 - metallic);
    float3 diffuse = kD * albedo / PI;
    
    float3 radiance = light.color * light.intensity * attenuation * spotAttenuation;
    return (diffuse + specular) * radiance * NdotL;
}

// ============================================================================
// IBL Functions
// ============================================================================

float3 calculateIBL(float3 normal,
                     float3 viewDir,
                     float3 albedo,
                     float metallic,
                     float roughness,
                     float ao,
                     texturecube<float> irradianceMap [[texture(10)]],
                     texturecube<float> prefilteredMap [[texture(11)]],
                     texture2d<float> brdfLUT [[texture(12)]],
                     sampler envSampler [[sampler(10)]])
{
    float3 N = normal;
    float3 V = viewDir;
    float NdotV = max(dot(N, V), 0.0);
    float3 R = reflect(-V, N);
    
    float3 F0 = mix(float3(0.04), albedo, metallic);
    float3 F = fresnelSchlickRoughness(NdotV, F0, roughness);
    
    float3 kS = F;
    float3 kD = 1.0 - kS;
    kD *= 1.0 - metallic;
    
    // Diffuse irradiance
    float3 irradiance = irradianceMap.sample(envSampler, N).rgb;
    float3 diffuse = irradiance * albedo;
    
    // Specular reflection - use level() for LOD sampling
    float lod = roughness * 4.0;
    float3 prefilteredColor = prefilteredMap.sample(envSampler, R, level(lod)).rgb;
    
    float2 brdf = brdfLUT.sample(envSampler, float2(NdotV, roughness)).rg;
    float3 specular = prefilteredColor * (F * brdf.x + brdf.y);
    
    float3 ambient = (kD * diffuse + specular) * ao;
    return ambient;
}

// Simplified IBL for low quality with improved hemisphere lighting
float3 calculateSimpleIBL(float3 normal,
                           float3 viewDir,
                           float3 albedo,
                           float metallic,
                           float roughness,
                           float ao)
{
    // Enhanced hemisphere lighting
    float skyFactor = max(0.0, normal.y) * 0.5 + 0.5; // Remap [0,1] to [0.5,1]
    float3 skyColor = float3(0.6, 0.7, 0.9); // Cool blue sky
    float3 skyContribution = skyColor * skyFactor * 0.8;
    
    float groundFactor = max(0.0, -normal.y) * 0.5 + 0.5;
    float3 groundColor = float3(0.3, 0.25, 0.2); // Warm ground bounce
    float3 groundContribution = groundColor * groundFactor * 0.4;
    
    float3 ambient = (skyContribution + groundContribution) * albedo;
    
    // Add subtle reflection for metallic/smooth surfaces
    float reflectivity = metallic * (1.0 - roughness);
    float3 reflectionDir = reflect(-viewDir, normal);
    float reflectionSky = max(0.0, reflectionDir.y);
    ambient += skyColor * reflectionSky * reflectivity * 0.3;
    
    return ambient * ao;
}

// ============================================================================
// Volumetric Lighting
// ============================================================================

float3 calculateVolumetrics(float3 worldPos,
                             float3 cameraPos,
                             constant LightData* lights,
                             int lightCount,
                             float density)
{
    // Simplified volumetric fog
    float3 viewDir = normalize(worldPos - cameraPos);
    float distance = length(worldPos - cameraPos);
    
    float3 volumetricColor = float3(0.0);
    float stepSize = distance / 4.0; // 4 steps for performance
    
    for (int i = 0; i < lightCount; i++) {
        LightData light = lights[i];
        if (light.volumetric == 0) continue;
        
        float3 accum = float3(0.0);
        float t = 0.0;
        
        for (int step = 0; step < 4; step++) {
            float3 samplePos = cameraPos + viewDir * t;
            t += stepSize;
            
            if (light.type == LIGHT_TYPE_DIRECTIONAL) {
                accum += light.color * density;
            } else {
                float dist = length(samplePos - light.position);
                float atten = 1.0 / (1.0 + dist * dist * light.attenuation);
                accum += light.color * atten * density;
            }
        }
        
        volumetricColor += accum * light.intensity * light.volumetric;
    }
    
    return volumetricColor / 4.0;
}

// ============================================================================
// Fragment Shader
fragment float4 horizon_fragment(VertexOut in [[stage_in]],
                                  constant ViewUniforms& view [[buffer(10)]],
                                  constant MaterialData& material [[buffer(11)]], 
                                  constant LightData* lights [[buffer(12)]],      
                                  constant int& lightCount [[buffer(13)]],        
                                  constant LightingSettings& settings [[buffer(14)]],
                                  texture2d<float> albedoMap [[texture(2)]],
                                  texture2d<float> normalMap [[texture(3)]],
                                  texture2d<float> rmaMap [[texture(4)]])
{
    constexpr sampler texSampler(address::repeat, filter::linear, mip_filter::linear);

    // Sample material properties
    float3 albedo = material.albedo;
    if (material.useAlbedoTex != 0) {
        float4 texColor = albedoMap.sample(texSampler, in.uv);
        albedo *= texColor.rgb;
    }
    
    float3 normal = normalize(in.normal);
    if (material.useNormalTex != 0) {
        // Basic tangent space mapping approximation (assuming flat UVs for now, proper TBN requires tangent vector)
        float3 texNormal = normalMap.sample(texSampler, in.uv).rgb * 2.0 - 1.0;
        // Simplified blend
        normal = normalize(normal + texNormal * 0.5); 
    }
    
    float metallic = material.metallic;
    float roughness = material.roughness;
    float ao = material.ao;
    
    if (material.useRMATex != 0) {
        float3 rma = rmaMap.sample(texSampler, in.uv).rgb;
        roughness = rma.g;  // G = Roughness
        metallic = rma.b;   // B = Metallic
        // Note: some GLTF packings might use R=AO, G=Roughness, B=Metallic, or other variants. 
        // Standard engine packing: R=AO, G=Roughness, B=Metallic
        ao *= rma.r;
    }
    
    roughness = max(MIN_ROUGHNESS, roughness);
    
    float3 viewDir = normalize(view.cameraPos - in.worldPos);
    
    // Direct lighting
    float3 directLighting = float3(0.0);
    int processedLights = 0;
    int maxLights = min(lightCount, settings.maxLights);
    
    for (int i = 0; i < maxLights; i++) {
        LightData light = lights[i];
        if (!light.castShadows && processedLights > settings.maxLights / 2) {
            // Skip non-shadowing lights if over budget
            continue;
        }
        
        float3 lightContribution;
        
        switch (light.type) {
            case LIGHT_TYPE_DIRECTIONAL:
                lightContribution = calculateDirectionalLight(
                    light, normal, viewDir, albedo, metallic, roughness, settings.quality);
                break;
            case LIGHT_TYPE_POINT:
                lightContribution = calculatePointLight(
                    light, in.worldPos, normal, viewDir, albedo, metallic, roughness, settings.quality);
                break;
            case LIGHT_TYPE_SPOT:
                lightContribution = calculateSpotLight(
                    light, in.worldPos, normal, viewDir, albedo, metallic, roughness, settings.quality);
                break;
            default:
                lightContribution = float3(0.0);
        }
        
        directLighting += lightContribution;
        processedLights++;
    }
    
    // Image Based Lighting
    float3 ambient = float3(0.0);
    if (settings.enableIBL != 0) {
        ambient = calculateSimpleIBL(normal, viewDir, albedo, metallic, roughness, ao);
    } else {
        // Fallback ambient with hemisphere lighting
        float skyFactor = max(0.0, normal.y) * 0.5 + 0.5;
        float3 skyLight = float3(0.6, 0.7, 0.9) * skyFactor * 0.5;
        float groundFactor = max(0.0, -normal.y) * 0.5 + 0.5;
        float3 groundLight = float3(0.3, 0.25, 0.2) * groundFactor * 0.3;
        ambient = (skyLight + groundLight) * albedo * ao;
    }
    
    // Volumetric lighting
    float3 volumetric = float3(0.0);
    if (settings.enableVolumetrics != 0 && settings.quality >= 2) {
        volumetric = calculateVolumetrics(in.worldPos, view.cameraPos, lights, lightCount, 0.01);
    }
    
    // Combine
    float3 finalColor = directLighting + ambient + volumetric;
    
    // Emission
    finalColor += albedo * material.emission;
    
    // Exposure adjustment
    finalColor *= settings.exposure;
    
    // Tone mapping (Improved ACES Filmic)
    float3 x = max(float3(0.0), finalColor - 0.004);
    finalColor = (x * (6.2 * x + 0.5)) / (x * (6.2 * x + 1.7) + 0.06);
    
    // Subtle color grading for warmth
    finalColor *= float3(1.02, 1.0, 0.98);
    
    // Gamma correction
    finalColor = pow(finalColor, float3(1.0 / 2.2));
    
    return float4(finalColor, 1.0);
}

// ============================================================================
// Shadow Mapping Pass
// ============================================================================

struct ShadowVertexOut {
    float4 position [[position]];
    float2 depth;
};

vertex ShadowVertexOut horizon_shadow_vertex(VertexIn in [[stage_in]],
                                              constant float4x4& lightSpaceMatrix [[buffer(10)]],
                                              constant EntityUniforms& ent [[buffer(30)]])
{
    ShadowVertexOut out;
    float4 worldPos = ent.model * float4(in.position, 1.0);
    out.position = lightSpaceMatrix * worldPos;
    out.depth = out.position.zw;
    return out;
}

fragment float4 horizon_shadow_fragment(ShadowVertexOut in [[stage_in]])
{
    float depth = in.depth.x / in.depth.y;
    return float4(depth, depth * depth, 0.0, 1.0); // VSM: depth and depth squared
}
