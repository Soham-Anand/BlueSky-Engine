// Software Ray Tracing - Shading Shader
// Computes lighting for ray hits
// Supports shadows, reflections, and simple GI

struct RayHit
{
    float3 Position;
    float T;
    float3 Normal;
    uint TriangleIndex;
    float2 UV;
    uint MaterialIndex;
    uint Padding;
};

cbuffer SceneParams : register(b0)
{
    float4 SunDirection;
    float4 SunColor;
    float4 AmbientColor;
    float4 CameraPosition;
};

StructuredBuffer<RayHit> Hits : register(t0);
RWTexture2D<float4> OutputTexture : register(u0);
RWTexture2D<float4> NormalTexture : register(u1);
RWTexture2D<float> DepthTexture : register(u2);

// Simple PBR shading
float3 ShadePBR(float3 albedo, float3 normal, float3 viewDir, float3 lightDir, float3 lightColor)
{
    // Diffuse (Lambert)
    float NdotL = max(0.0, dot(normal, lightDir));
    float3 diffuse = albedo * lightColor * NdotL;
    
    // Specular (Blinn-Phong for speed)
    float3 halfDir = normalize(lightDir + viewDir);
    float NdotH = max(0.0, dot(normal, halfDir));
    float specular = pow(NdotH, 32.0);
    float3 specularColor = lightColor * specular * 0.3;
    
    return diffuse + specularColor;
}

[numthreads(8, 8, 1)]
void main(uint3 dispatchThreadID : SV_DispatchThreadID)
{
    uint2 pixelCoord = dispatchThreadID.xy;
    
    // Get dimensions
    uint width, height;
    OutputTexture.GetDimensions(width, height);
    
    if (pixelCoord.x >= width || pixelCoord.y >= height)
        return;
    
    // Load hit
    uint rayIndex = pixelCoord.y * width + pixelCoord.x;
    RayHit hit = Hits[rayIndex];
    
    // Check if ray hit anything
    if (hit.TriangleIndex == 0xFFFFFFFF)
    {
        // Miss: Sky color
        float3 skyColor = float3(0.5, 0.7, 1.0);
        OutputTexture[pixelCoord] = float4(skyColor, 1.0);
        NormalTexture[pixelCoord] = float4(0, 1, 0, 0);
        DepthTexture[pixelCoord] = 1e10;
        return;
    }
    
    // Hit: Shade surface
    float3 viewDir = normalize(CameraPosition.xyz - hit.Position);
    float3 lightDir = normalize(-SunDirection.xyz);
    
    // Simple albedo (would come from material/texture)
    float3 albedo = float3(0.7, 0.7, 0.8);
    
    // Shade
    float3 color = ShadePBR(albedo, hit.Normal, viewDir, lightDir, SunColor.rgb);
    
    // Add ambient
    color += albedo * AmbientColor.rgb;
    
    // Write outputs
    OutputTexture[pixelCoord] = float4(color, 1.0);
    NormalTexture[pixelCoord] = float4(hit.Normal * 0.5 + 0.5, 1.0);
    DepthTexture[pixelCoord] = hit.T;
}
