// Optimized PBR Shader for Old Hardware
// Uses simplified lighting model with fallback options
// Designed for integrated GPUs and older graphics cards

#include <metal_stdlib>
using namespace metal;

// Vertex input
struct VertexIn {
    float3 position [[attribute(0)]];
    float3 normal [[attribute(1)]];
    float2 uv [[attribute(2)]];
};

// Vertex output
struct VertexOut {
    float4 position [[position]];
    float3 worldPos;
    float3 normal;
    float2 uv;
};

// Material properties
struct MaterialData {
    float3 albedo;
    float metallic;
    float roughness;
    float3 emission;
    float emissionIntensity;
    float ao;
    int useAlbedoTex;
    int useNormalTex;
    int useRMA;
    int simplifiedLighting;
};

// Lighting data
struct LightData {
    float3 position;
    float3 color;
    float intensity;
};

// View uniforms
struct ViewUniforms {
    float4x4 viewProj;
    float4x4 view;
    float4x4 invView;
    float3 cameraPos;
};

// Textures - must use texture indices
constant sampler texSampler(filter::linear, address::repeat, compare_func::never);

// Vertex shader
vertex VertexOut vertex_main(VertexIn in [[stage_in]],
                            constant ViewUniforms& view [[buffer(0)]],
                            constant MaterialData& material [[buffer(1)]]) {
    VertexOut out;
    
    float4 worldPos = float4(in.position, 1.0);
    out.position = view.viewProj * worldPos;
    out.worldPos = worldPos.xyz;
    out.normal = in.normal;
    out.uv = in.uv;
    
    return out;
}

// Simplified Blinn-Phong for old hardware
float3 calculateBlinnPhong(float3 normal, float3 lightDir, float3 viewDir,
                          float3 lightColor, float3 albedo, float roughness) {
    float NdotL = max(dot(normal, lightDir), 0.0);
    float NdotV = max(dot(normal, viewDir), 0.0);
    
    if (NdotL <= 0.0) return float3(0.0);
    
    float3 halfDir = normalize(lightDir + viewDir);
    float NdotH = max(dot(normal, halfDir), 0.0);
    
    // Specular
    float specPower = max(2.0, 128.0 * (1.0 - roughness));
    float spec = pow(NdotH, specPower) * (specPower + 2.0) / 8.0;
    
    // Diffuse
    float3 diffuse = albedo / 3.14159;
    
    return diffuse * NdotL + spec * 0.5;
}

// Optimized PBR lighting
float3 calculatePBR(float3 normal, float3 lightDir, float3 viewDir,
                    float3 lightColor, float3 albedo, float metallic, float roughness) {
    float NdotL = max(dot(normal, lightDir), 0.0);
    float NdotV = max(dot(normal, viewDir), 0.0);
    
    if (NdotL <= 0.0) return float3(0.0);
    
    // Fresnel (Schlick approximation)
    float3 F0 = mix(float3(0.04), albedo, metallic);
    float3 F = F0 + (1.0 - F0) * pow(1.0 - NdotV, 5.0);
    
    // Distribution (GGX simplified)
    float3 halfDir = normalize(lightDir + viewDir);
    float NdotH = max(dot(normal, halfDir), 0.0);
    float alpha = roughness * roughness;
    float alpha2 = alpha * alpha;
    float denom = NdotH * NdotH * (alpha2 - 1.0) + 1.0;
    float D = alpha2 / (3.14159 * denom * denom);
    
    // Geometry (Smith simplified)
    float k = alpha / 2.0;
    float G1 = NdotL / (NdotL * (1.0 - k) + k);
    float G2 = NdotV / (NdotV * (1.0 - k) + k);
    float G = G1 * G2;
    
    // Specular
    float3 spec = (D * F * G) / (4.0 * NdotV * NdotL + 0.001);
    
    // Diffuse
    float3 kD = (1.0 - F) * (1.0 - metallic);
    float3 diffuse = kD * albedo / 3.14159;
    
    return (diffuse + spec) * NdotL;
}

// Fragment shader
fragment float4 fragment_main(VertexOut in [[stage_in]],
                              constant ViewUniforms& view [[buffer(0)]],
                              constant MaterialData& material [[buffer(1)]],
                              constant LightData& light [[buffer(2)]],
                              texture2d<float> albedoTex [[texture(0)]],
                              texture2d<float> normalTex [[texture(1)]],
                              texture2d<float> rmaTex [[texture(2)]],
                              sampler texSampler [[sampler(0)]]) {
    
    // Sample textures
    float3 albedo = material.albedo;
    if (material.useAlbedoTex) {
        albedo = albedoTex.sample(texSampler, in.uv).rgb;
    }
    
    float3 normal = normalize(in.normal);
    if (material.useNormalTex) {
        float3 tangentNormal = normalTex.sample(texSampler, in.uv).rgb * 2.0 - 1.0;
        // Simplified normal mapping (skip TBN calculation for performance)
        normal = normalize(normal + tangentNormal * 0.5);
    }
    
    float roughness = material.roughness;
    float metallic = material.metallic;
    float ao = material.ao;
    
    if (material.useRMA) {
        float3 rma = rmaTex.sample(texSampler, in.uv).rgb;
        roughness = rma.r;
        metallic = rma.g;
        ao = rma.b;
    }
    
    // View direction
    float3 viewDir = normalize(view.cameraPos - in.worldPos);
    float3 lightDir = normalize(light.position - in.worldPos);
    
    // Calculate lighting
    float3 lighting;
    if (material.simplifiedLighting) {
        lighting = calculateBlinnPhong(normal, lightDir, viewDir, light.color, albedo, roughness);
    } else {
        lighting = calculatePBR(normal, lightDir, viewDir, light.color, albedo, metallic, roughness);
    }
    
    // Apply light intensity and AO
    lighting *= light.intensity * ao;
    
    // Add emission
    lighting += material.emission * material.emissionIntensity;
    
    return float4(lighting, 1.0);
}
