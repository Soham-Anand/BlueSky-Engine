// Forward+ PBR Shader
// Physically Based Rendering with clustered lighting
// Scales from DX9 (reduced quality) to modern APIs (full PBR)

cbuffer SceneData : register(b0)
{
    float4x4 ViewMatrix;
    float4x4 ProjMatrix;
    float4x4 ViewProjMatrix;
    float3 CameraPosition;
    float Time;
    uint ClusterCountX;
    uint ClusterCountY;
    uint ClusterCountZ;
    float NearPlane;
    float FarPlane;
    uint ScreenWidth;
    uint ScreenHeight;
    uint _padding;
};

cbuffer ObjectData : register(b1)
{
    float4x4 ModelMatrix;
    float4x4 NormalMatrix;
};

struct GPULight
{
    float4 PositionAndType;
    float4 DirectionAndRange;
    float4 ColorAndIntensity;
    float4 SpotAngles;
};

struct LightGrid
{
    uint Offset;
    uint Count;
};

// Bindless resources (modern APIs) or traditional binding (DX9/11)
#ifdef BINDLESS_RESOURCES
    StructuredBuffer<LightGrid> LightGrids : register(t0, space1);
    StructuredBuffer<uint> LightIndexList : register(t1, space1);
    StructuredBuffer<GPULight> Lights : register(t2, space1);
#else
    StructuredBuffer<LightGrid> LightGrids : register(t3);
    StructuredBuffer<uint> LightIndexList : register(t4);
    StructuredBuffer<GPULight> Lights : register(t5);
#endif

// Material textures
Texture2D AlbedoMap : register(t0);
Texture2D NormalMap : register(t1);
Texture2D MetallicRoughnessMap : register(t2);
Texture2D AOMap : register(t3);

SamplerState LinearSampler : register(s0);

struct VSInput
{
    float3 Position : POSITION;
    float3 Normal : NORMAL;
    float2 TexCoord : TEXCOORD0;
    float3 Tangent : TANGENT;
};

struct PSInput
{
    float4 Position : SV_POSITION;
    float3 WorldPos : POSITION0;
    float3 Normal : NORMAL;
    float2 TexCoord : TEXCOORD0;
    float3 Tangent : TANGENT;
    float3 Bitangent : BITANGENT;
    float4 ViewPos : POSITION1;
};

// ============================================================================
// Vertex Shader
// ============================================================================

PSInput VSMain(VSInput input)
{
    PSInput output;
    
    float4 worldPos = mul(float4(input.Position, 1.0), ModelMatrix);
    output.WorldPos = worldPos.xyz;
    output.Position = mul(worldPos, ViewProjMatrix);
    output.ViewPos = mul(worldPos, ViewMatrix);
    
    output.Normal = normalize(mul(float4(input.Normal, 0.0), NormalMatrix).xyz);
    output.Tangent = normalize(mul(float4(input.Tangent, 0.0), ModelMatrix).xyz);
    output.Bitangent = cross(output.Normal, output.Tangent);
    
    output.TexCoord = input.TexCoord;
    
    return output;
}

// ============================================================================
// PBR Functions
// ============================================================================

static const float PI = 3.14159265359;

float3 FresnelSchlick(float cosTheta, float3 F0)
{
    return F0 + (1.0 - F0) * pow(1.0 - cosTheta, 5.0);
}

float DistributionGGX(float NdotH, float roughness)
{
    float a = roughness * roughness;
    float a2 = a * a;
    float NdotH2 = NdotH * NdotH;
    
    float denom = NdotH2 * (a2 - 1.0) + 1.0;
    denom = PI * denom * denom;
    
    return a2 / denom;
}

float GeometrySchlickGGX(float NdotV, float roughness)
{
    float r = roughness + 1.0;
    float k = (r * r) / 8.0;
    
    return NdotV / (NdotV * (1.0 - k) + k);
}

float GeometrySmith(float NdotV, float NdotL, float roughness)
{
    float ggx1 = GeometrySchlickGGX(NdotV, roughness);
    float ggx2 = GeometrySchlickGGX(NdotL, roughness);
    
    return ggx1 * ggx2;
}

float3 CalculatePBR(float3 N, float3 V, float3 L, float3 albedo, float metallic, float roughness,
                    float3 lightColor, float lightIntensity)
{
    float3 H = normalize(V + L);
    
    float NdotV = max(dot(N, V), 0.0);
    float NdotL = max(dot(N, L), 0.0);
    float NdotH = max(dot(N, H), 0.0);
    float HdotV = max(dot(H, V), 0.0);
    
    if (NdotL <= 0.0)
        return float3(0, 0, 0);
    
    // Fresnel
    float3 F0 = lerp(float3(0.04, 0.04, 0.04), albedo, metallic);
    float3 F = FresnelSchlick(HdotV, F0);
    
    // Distribution and Geometry
    float D = DistributionGGX(NdotH, roughness);
    float G = GeometrySmith(NdotV, NdotL, roughness);
    
    // Specular
    float3 specular = (D * F * G) / (4.0 * NdotV * NdotL + 0.001);
    
    // Diffuse
    float3 kD = (1.0 - F) * (1.0 - metallic);
    float3 diffuse = kD * albedo / PI;
    
    // Combine
    float3 radiance = lightColor * lightIntensity;
    return (diffuse + specular) * radiance * NdotL;
}

// ============================================================================
// Light Calculation
// ============================================================================

float3 CalculateDirectionalLight(GPULight light, float3 N, float3 V, float3 albedo, 
                                 float metallic, float roughness)
{
    float3 L = -normalize(light.DirectionAndRange.xyz);
    float3 lightColor = light.ColorAndIntensity.xyz;
    float intensity = light.ColorAndIntensity.w;
    
    return CalculatePBR(N, V, L, albedo, metallic, roughness, lightColor, intensity);
}

float3 CalculatePointLight(GPULight light, float3 worldPos, float3 N, float3 V, 
                          float3 albedo, float metallic, float roughness)
{
    float3 lightPos = light.PositionAndType.xyz;
    float range = light.DirectionAndRange.w;
    float3 lightColor = light.ColorAndIntensity.xyz;
    float intensity = light.ColorAndIntensity.w;
    float attenuation = light.SpotAngles.z;
    
    float3 toLight = lightPos - worldPos;
    float distance = length(toLight);
    
    if (distance >= range)
        return float3(0, 0, 0);
    
    float3 L = toLight / distance;
    
    // Attenuation
    float att = 1.0 / (1.0 + distance * distance * attenuation);
    float rangeFactor = max(0.0, 1.0 - (distance / range));
    rangeFactor *= rangeFactor;
    
    float finalIntensity = intensity * att * rangeFactor;
    
    return CalculatePBR(N, V, L, albedo, metallic, roughness, lightColor, finalIntensity);
}

float3 CalculateSpotLight(GPULight light, float3 worldPos, float3 N, float3 V,
                         float3 albedo, float metallic, float roughness)
{
    float3 lightPos = light.PositionAndType.xyz;
    float3 lightDir = normalize(light.DirectionAndRange.xyz);
    float range = light.DirectionAndRange.w;
    float3 lightColor = light.ColorAndIntensity.xyz;
    float intensity = light.ColorAndIntensity.w;
    float cosInner = light.SpotAngles.x;
    float cosOuter = light.SpotAngles.y;
    float attenuation = light.SpotAngles.z;
    
    float3 toLight = lightPos - worldPos;
    float distance = length(toLight);
    
    if (distance >= range)
        return float3(0, 0, 0);
    
    float3 L = toLight / distance;
    
    // Spot cone
    float spotDot = dot(-L, lightDir);
    if (spotDot < cosOuter)
        return float3(0, 0, 0);
    
    float spotAttenuation = 1.0;
    if (spotDot < cosInner)
    {
        float t = (spotDot - cosOuter) / (cosInner - cosOuter);
        spotAttenuation = t * t;
    }
    
    // Distance attenuation
    float att = 1.0 / (1.0 + distance * distance * attenuation);
    float finalIntensity = intensity * att * spotAttenuation;
    
    return CalculatePBR(N, V, L, albedo, metallic, roughness, lightColor, finalIntensity);
}

// ============================================================================
// Cluster Lookup
// ============================================================================

uint GetClusterIndex(float2 screenPos, float viewZ)
{
    uint clusterX = uint(screenPos.x * ClusterCountX);
    uint clusterY = uint(screenPos.y * ClusterCountY);
    
    // Exponential depth slicing
    float zNear = NearPlane;
    float zFar = FarPlane;
    float sliceScale = float(ClusterCountZ) / log2(zFar / zNear);
    uint clusterZ = uint(max(log2(-viewZ / zNear) * sliceScale, 0.0));
    
    clusterX = min(clusterX, ClusterCountX - 1);
    clusterY = min(clusterY, ClusterCountY - 1);
    clusterZ = min(clusterZ, ClusterCountZ - 1);
    
    return clusterX + clusterY * ClusterCountX + clusterZ * ClusterCountX * ClusterCountY;
}

// ============================================================================
// Pixel Shader
// ============================================================================

float4 PSMain(PSInput input) : SV_TARGET
{
    // Sample material properties
    float3 albedo = AlbedoMap.Sample(LinearSampler, input.TexCoord).rgb;
    float3 normalMap = NormalMap.Sample(LinearSampler, input.TexCoord).rgb * 2.0 - 1.0;
    float2 metallicRoughness = MetallicRoughnessMap.Sample(LinearSampler, input.TexCoord).bg;
    float ao = AOMap.Sample(LinearSampler, input.TexCoord).r;
    
    float metallic = metallicRoughness.x;
    float roughness = metallicRoughness.y;
    
    // Transform normal to world space
    float3x3 TBN = float3x3(input.Tangent, input.Bitangent, input.Normal);
    float3 N = normalize(mul(normalMap, TBN));
    
    float3 V = normalize(CameraPosition - input.WorldPos);
    
    // Get cluster index
    float2 screenPos = input.Position.xy / float2(ScreenWidth, ScreenHeight);
    uint clusterIndex = GetClusterIndex(screenPos, input.ViewPos.z);
    
    // Get lights for this cluster
    LightGrid grid = LightGrids[clusterIndex];
    
    // Accumulate lighting
    float3 lighting = float3(0, 0, 0);
    
    for (uint i = 0; i < grid.Count; i++)
    {
        uint lightIndex = LightIndexList[grid.Offset + i + 1];
        GPULight light = Lights[lightIndex];
        
        uint lightType = uint(light.PositionAndType.w);
        
        if (lightType == 0) // Directional
        {
            lighting += CalculateDirectionalLight(light, N, V, albedo, metallic, roughness);
        }
        else if (lightType == 1) // Point
        {
            lighting += CalculatePointLight(light, input.WorldPos, N, V, albedo, metallic, roughness);
        }
        else if (lightType == 2) // Spot
        {
            lighting += CalculateSpotLight(light, input.WorldPos, N, V, albedo, metallic, roughness);
        }
    }
    
    // Ambient (simplified IBL)
    float3 ambient = albedo * 0.03 * ao;
    
    float3 color = ambient + lighting * ao;
    
    // Simple tonemapping
    color = color / (color + 1.0);
    
    // Gamma correction
    color = pow(color, 1.0 / 2.2);
    
    return float4(color, 1.0);
}
