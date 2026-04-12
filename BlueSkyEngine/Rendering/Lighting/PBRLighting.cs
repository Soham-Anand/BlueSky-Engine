using System;
using System.Collections.Generic;
using System.Numerics;

namespace BlueSky.Rendering.Lighting;

/// <summary>
/// Optimized PBR lighting system for old hardware.
/// Uses clustered forward rendering with light culling.
/// </summary>
public class PBRLighting
{
    private readonly List<Light> _lights = new();
    private readonly LightClusterGrid _clusterGrid;
    private readonly int _maxLightsPerCluster = 16;
    
    public PBRLighting()
    {
        _clusterGrid = new LightClusterGrid();
    }
    
    /// <summary>
    /// Add a light to the scene.
    /// </summary>
    public void AddLight(Light light)
    {
        if (light == null) return;
        if (light.Id == Guid.Empty) light.Id = Guid.NewGuid();
        
        // Validate light properties
        light.Intensity = Math.Max(0.0f, light.Intensity);
        light.Range = Math.Max(0.1f, light.Range);
        light.Color = Vector3.Clamp(light.Color, Vector3.Zero, Vector3.One);
        
        if (_lights.Contains(light))
        {
            Console.WriteLine($"[PBRLighting] Light {light.Id} already added");
            return;
        }
        
        _lights.Add(light);
    }
    
    /// <summary>
    /// Remove a light from the scene.
    /// </summary>
    public void RemoveLight(Light light)
    {
        if (light == null) return;
        
        if (!_lights.Contains(light))
        {
            Console.WriteLine($"[PBRLighting] Light {light.Id} not found");
            return;
        }
        
        _lights.Remove(light);
    }
    
    /// <summary>
    /// Update light clustering (call each frame).
    /// </summary>
    public void UpdateClusters(Vector3 cameraPos, Matrix4x4 viewProj, int screenWidth, int screenHeight)
    {
        _clusterGrid.Clear();
        _clusterGrid.BuildClusters(_lights, cameraPos, viewProj, screenWidth, screenHeight, _maxLightsPerCluster);
    }
    
    /// <summary>
    /// Get lights affecting a fragment position.
    /// </summary>
    public Light[] GetLightsForFragment(Vector3 position, Vector3 normal, Vector2 screenUV)
    {
        return _clusterGrid.QueryLights(position, screenUV);
    }
    
    /// <summary>
    /// Calculate lighting contribution for a fragment.
    /// </summary>
    public Vector3 CalculateLighting(Vector3 position, Vector3 normal, Vector3 viewDir,
                                     Vector3 albedo, float metallic, float roughness,
                                     float ao, Light[] lights)
    {
        Vector3 result = Vector3.Zero;
        
        foreach (var light in lights)
        {
            result += CalculateLightContribution(light, position, normal, viewDir, 
                                              albedo, metallic, roughness);
        }
        
        // Apply ambient occlusion
        result *= ao;
        
        return result;
    }
    
    /// <summary>
    /// Calculate contribution from a single light.
    /// </summary>
    private Vector3 CalculateLightContribution(Light light, Vector3 position, Vector3 normal, 
                                              Vector3 viewDir, Vector3 albedo, float metallic, float roughness)
    {
        switch (light.Type)
        {
            case LightType.Directional:
                return CalculateDirectionalLight(light, position, normal, viewDir, albedo, metallic, roughness);
            case LightType.Point:
                return CalculatePointLight(light, position, normal, viewDir, albedo, metallic, roughness);
            case LightType.Spot:
                return CalculateSpotLight(light, position, normal, viewDir, albedo, metallic, roughness);
            default:
                return Vector3.Zero;
        }
    }
    
    private Vector3 CalculateDirectionalLight(Light light, Vector3 position, Vector3 normal,
                                               Vector3 viewDir, Vector3 albedo, float metallic, float roughness)
    {
        Vector3 lightDir = -Vector3.Normalize(light.Direction);
        float NdotL = Math.Max(Vector3.Dot(normal, lightDir), 0.0f);
        
        if (NdotL <= 0.0f) return Vector3.Zero;
        
        return CalculateBRDF(normal, lightDir, viewDir, albedo, metallic, roughness, light.Color, light.Intensity * NdotL);
    }
    
    private Vector3 CalculatePointLight(Light light, Vector3 position, Vector3 normal,
                                         Vector3 viewDir, Vector3 albedo, float metallic, float roughness)
    {
        Vector3 lightDir = light.Position - position;
        float distance = lightDir.Length();
        lightDir = Vector3.Normalize(lightDir);
        
        float NdotL = Math.Max(Vector3.Dot(normal, lightDir), 0.0f);
        if (NdotL <= 0.0f) return Vector3.Zero;
        
        // Attenuation
        float attenuation = CalculateAttenuation(distance, light.Range, light.Attenuation);
        
        return CalculateBRDF(normal, lightDir, viewDir, albedo, metallic, roughness, 
                           light.Color, light.Intensity * attenuation * NdotL);
    }
    
    private Vector3 CalculateSpotLight(Light light, Vector3 position, Vector3 normal,
                                       Vector3 viewDir, Vector3 albedo, float metallic, float roughness)
    {
        Vector3 lightDir = light.Position - position;
        float distance = lightDir.Length();
        lightDir = Vector3.Normalize(lightDir);
        
        float NdotL = Math.Max(Vector3.Dot(normal, lightDir), 0.0f);
        if (NdotL <= 0.0f) return Vector3.Zero;
        
        // Spot cone
        float spotDot = Vector3.Dot(-lightDir, Vector3.Normalize(light.Direction));
        float spotFactor = MathF.Pow(Math.Clamp((spotDot - light.OuterCone) / (light.InnerCone - light.OuterCone), 0.0f, 1.0f), light.SpotExponent);
        
        // Attenuation
        float attenuation = CalculateAttenuation(distance, light.Range, light.Attenuation);
        
        return CalculateBRDF(normal, lightDir, viewDir, albedo, metallic, roughness, 
                           light.Color, light.Intensity * attenuation * spotFactor * NdotL);
    }
    
    /// <summary>
    /// Optimized BRDF calculation with simplified Fresnel and GGX.
    /// </summary>
    private Vector3 CalculateBRDF(Vector3 normal, Vector3 lightDir, Vector3 viewDir,
                                  Vector3 albedo, float metallic, float roughness,
                                  Vector3 lightColor, float intensity)
    {
        float NdotL = Math.Max(Vector3.Dot(normal, lightDir), 0.0f);
        float NdotV = Math.Max(Vector3.Dot(normal, viewDir), 0.0f);
        
        if (NdotL <= 0.0f) return Vector3.Zero;
        
        // Fresnel (Schlick approximation)
        Vector3 F0 = Vector3.Lerp(new Vector3(0.04f), albedo, metallic);
        Vector3 F = F0 + (Vector3.One - F0) * (float)Math.Pow(1.0f - NdotV, 5.0f);
        
        // Distribution (GGX simplified)
        Vector3 halfDir = Vector3.Normalize(lightDir + viewDir);
        float NdotH = Math.Max(Vector3.Dot(normal, halfDir), 0.0f);
        float alpha = roughness * roughness;
        float alpha2 = alpha * alpha;
        float denom = NdotH * NdotH * (alpha2 - 1.0f) + 1.0f;
        float D = alpha2 / (MathF.PI * denom * denom);
        
        // Geometry (Smith simplified)
        float k = alpha / 2.0f;
        float G1 = NdotL / (NdotL * (1.0f - k) + k);
        float G2 = NdotV / (NdotV * (1.0f - k) + k);
        float G = G1 * G2;
        
        // Specular
        Vector3 spec = (D * F * G) / (4.0f * NdotV * NdotL + 0.001f);
        
        // Diffuse
        Vector3 kD = (Vector3.One - F) * (1.0f - metallic);
        Vector3 diffuse = kD * albedo / MathF.PI;
        
        return (diffuse + spec) * lightColor * intensity;
    }
    
    /// <summary>
    /// Calculate light attenuation.
    /// </summary>
    private float CalculateAttenuation(float distance, float range, AttenuationMode mode)
    {
        if (distance >= range) return 0.0f;
        
        float normalizedDist = distance / range;
        
        return mode switch
        {
            AttenuationMode.Linear => 1.0f - normalizedDist,
            AttenuationMode.Quadratic => 1.0f / (1.0f + distance * distance * 0.1f),
            AttenuationMode.InverseSquare => 1.0f / (distance * distance + 0.01f),
            _ => 1.0f - normalizedDist
        };
    }
}

/// <summary>
/// Light cluster grid for efficient light culling.
/// </summary>
public class LightClusterGrid
{
    private readonly Dictionary<(int, int, int), List<Light>> _clusters = new();
    private const int GridSizeX = 16;
    private const int GridSizeY = 16;
    private const int GridSizeZ = 16;
    
    public void Clear()
    {
        _clusters.Clear();
    }
    
    public void BuildClusters(List<Light> lights, Vector3 cameraPos, Matrix4x4 viewProj,
                              int screenWidth, int screenHeight, int maxLightsPerCluster)
    {
        foreach (var light in lights)
        {
            // Determine which cluster the light belongs to
            var cluster = GetClusterIndex(light.Position, cameraPos);
            
            if (!_clusters.ContainsKey(cluster))
                _clusters[cluster] = new List<Light>();
            
            _clusters[cluster].Add(light);
            
            // Limit lights per cluster
            if (_clusters[cluster].Count > maxLightsPerCluster)
            {
                _clusters[cluster].Sort((a, b) => b.Intensity.CompareTo(a.Intensity));
                _clusters[cluster].RemoveRange(maxLightsPerCluster, _clusters[cluster].Count - maxLightsPerCluster);
            }
        }
    }
    
    public Light[] QueryLights(Vector3 position, Vector2 screenUV)
    {
        var cluster = GetClusterIndex(position, screenUV);
        if (_clusters.TryGetValue(cluster, out var lights))
            return lights.ToArray();
        
        return Array.Empty<Light>();
    }
    
    private (int, int, int) GetClusterIndex(Vector3 position, Vector3 cameraPos)
    {
        Vector3 relPos = position - cameraPos;
        int x = (int)(relPos.X / GridSizeX);
        int y = (int)(relPos.Y / GridSizeY);
        int z = (int)(relPos.Z / GridSizeZ);
        return (x, y, z);
    }
    
    private (int, int, int) GetClusterIndex(Vector3 position, Vector2 screenUV)
    {
        int x = (int)(screenUV.X * GridSizeX);
        int y = (int)(screenUV.Y * GridSizeY);
        int z = (int)(position.Z / GridSizeZ);
        return (x, y, z);
    }
}

/// <summary>
/// Light data structure.
/// </summary>
public class Light
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public LightType Type { get; set; } = LightType.Point;
    public Vector3 Position { get; set; }
    public Vector3 Direction { get; set; } = Vector3.UnitY;
    public Vector3 Color { get; set; } = Vector3.One;
    public float Intensity { get; set; } = 1.0f;
    public float Range { get; set; } = 10.0f;
    public float InnerCone { get; set; } = 0.5f;
    public float OuterCone { get; set; } = 0.9f;
    public float SpotExponent { get; set; } = 2.0f;
    public AttenuationMode Attenuation { get; set; } = AttenuationMode.Quadratic;
    public bool CastShadows { get; set; } = false;
    public bool IsEnabled { get; set; } = true;
}

public enum LightType
{
    Directional,
    Point,
    Spot,
    Area
}

public enum AttenuationMode
{
    Linear,
    Quadratic,
    InverseSquare,
    None
}
