using System;
using System.Collections.Generic;
using System.Numerics;

namespace BlueSky.Rendering.Lighting;

/// <summary>
/// Horizon Lighting System - Feature-packed lighting optimized for old hardware.
/// Supports clustered lighting, IBL, volumetrics, and contact shadows with quality LOD.
/// </summary>
public class HorizonLighting
{
    // Core lighting data
    private readonly List<HorizonLight> _lights = new();
    private readonly ClusteredLightGrid _clusterGrid;
    private readonly IBLLighting _ibl;
    private readonly VolumetricLighting _volumetrics;
    private readonly ContactShadows _contactShadows;
    
    // Quality settings for old hardware optimization
    private LightingQuality _quality = LightingQuality.High;
    private int _maxLightsPerCluster = 8;
    private int _maxClusterLightsTotal = 256;
    
    // Performance metrics
    public int ActiveLightCount => _lights.Count;
    public int CulledLightCount { get; private set; }
    
    public HorizonLighting()
    {
        _clusterGrid = new ClusteredLightGrid();
        _ibl = new IBLLighting();
        _volumetrics = new VolumetricLighting();
        _contactShadows = new ContactShadows();
    }
    
    /// <summary>
    /// Set lighting quality level (affects performance on old hardware).
    /// </summary>
    public void SetQuality(LightingQuality quality)
    {
        _quality = quality;
        (_maxLightsPerCluster, _maxClusterLightsTotal) = quality switch
        {
            LightingQuality.Low => (4, 128),
            LightingQuality.Medium => (6, 192),
            LightingQuality.High => (8, 256),
            LightingQuality.Ultra => (16, 512),
            _ => (8, 256)
        };
    }
    
    /// <summary>
    /// Add a light to the Horizon system.
    /// </summary>
    public void AddLight(HorizonLight light)
    {
        if (light == null || light.Id == Guid.Empty) return;
        
        // Auto-assign priority based on intensity
        if (light.Priority == LightPriority.Auto)
        {
            light.Priority = light.Intensity > 10.0f ? LightPriority.High : LightPriority.Medium;
        }
        
        _lights.Add(light);
    }
    
    /// <summary>
    /// Remove a light from the system.
    /// </summary>
    public void RemoveLight(HorizonLight light)
    {
        _lights.Remove(light);
    }
    
    /// <summary>
    /// Update light clusters and prepare for rendering.
    /// Call once per frame.
    /// </summary>
    public void PrepareFrame(Vector3 cameraPos, Matrix4x4 viewProj, 
                              int screenWidth, int screenHeight,
                              float nearPlane, float farPlane)
    {
        // Sort lights by priority and distance
        SortLightsByPriority(cameraPos);
        
        // Build clustered light grid
        _clusterGrid.BuildClusters(_lights, cameraPos, viewProj, 
                                   screenWidth, screenHeight,
                                   nearPlane, farPlane,
                                   _maxLightsPerCluster, _maxClusterLightsTotal);
        
        // Update IBL if enabled
        if (_quality >= LightingQuality.Medium)
        {
            _ibl.PrepareFrame(cameraPos);
        }
        
        // Update volumetrics if enabled
        if (_quality >= LightingQuality.High)
        {
            _volumetrics.PrepareFrame(cameraPos, _lights);
        }
    }
    
    /// <summary>
    /// Get all lights affecting a specific world position.
    /// </summary>
    public HorizonLight[] GetLightsForPosition(Vector3 worldPos, Vector2 screenUV)
    {
        return _clusterGrid.QueryLights(worldPos, screenUV);
    }
    
    /// <summary>
    /// Calculate full Horizon lighting for a surface point.
    /// </summary>
    public Vector3 CalculateLighting(LightingInput input)
    {
        Vector3 result = Vector3.Zero;
        
        // Get clustered lights for this position
        var lights = GetLightsForPosition(input.WorldPos, input.ScreenUV);
        CulledLightCount = _lights.Count - lights.Length;
        
        // Direct lighting from clustered lights
        foreach (var light in lights)
        {
            if (!light.IsEnabled) continue;
            
            result += CalculateLightContribution(light, input);
        }
        
        // Image Based Lighting (ambient from environment)
        if (_quality >= LightingQuality.Medium && _ibl.IsEnabled)
        {
            result += _ibl.CalculateAmbient(input);
        }
        
        // Apply contact shadows for fine detail
        if (_quality >= LightingQuality.High && _contactShadows.IsEnabled)
        {
            result *= _contactShadows.CalculateShadow(input);
        }
        
        // Volumetric fog/light scattering
        if (_quality >= LightingQuality.High && _volumetrics.IsEnabled)
        {
            result += _volumetrics.CalculateScattering(input);
        }
        
        return result;
    }
    
    private Vector3 CalculateLightContribution(HorizonLight light, LightingInput input)
    {
        return light.Type switch
        {
            LightType.Directional => CalculateDirectionalLight(light, input),
            LightType.Point => CalculatePointLight(light, input),
            LightType.Spot => CalculateSpotLight(light, input),
            LightType.Area => CalculateAreaLight(light, input),
            _ => Vector3.Zero
        };
    }
    
    private Vector3 CalculateDirectionalLight(HorizonLight light, LightingInput input)
    {
        Vector3 L = -Vector3.Normalize(light.Direction);
        float NdotL = Math.Max(Vector3.Dot(input.Normal, L), 0.0f);
        
        if (NdotL <= 0.0f) return Vector3.Zero;
        
        Vector3 radiance = light.Color * light.Intensity;
        
        return HorizonBRDF.Calculate(input, L, radiance, _quality);
    }
    
    private Vector3 CalculatePointLight(HorizonLight light, LightingInput input)
    {
        Vector3 toLight = light.Position - input.WorldPos;
        float distance = toLight.Length();
        
        if (distance >= light.Range) return Vector3.Zero;
        
        Vector3 L = toLight / distance;
        float NdotL = Math.Max(Vector3.Dot(input.Normal, L), 0.0f);
        
        if (NdotL <= 0.0f) return Vector3.Zero;
        
        // Attenuation with inverse square falloff
        float attenuation = 1.0f / (1.0f + distance * distance * light.Attenuation);
        
        // Smooth falloff at range boundary
        float rangeFactor = Math.Max(0.0f, 1.0f - (distance / light.Range));
        rangeFactor *= rangeFactor; // Smoothstep
        
        Vector3 radiance = light.Color * light.Intensity * attenuation * rangeFactor;
        
        return HorizonBRDF.Calculate(input, L, radiance, _quality);
    }
    
    private Vector3 CalculateSpotLight(HorizonLight light, LightingInput input)
    {
        Vector3 toLight = light.Position - input.WorldPos;
        float distance = toLight.Length();
        
        if (distance >= light.Range) return Vector3.Zero;
        
        Vector3 L = toLight / distance;
        float NdotL = Math.Max(Vector3.Dot(input.Normal, L), 0.0f);
        
        if (NdotL <= 0.0f) return Vector3.Zero;
        
        // Spot cone attenuation
        float spotDot = Vector3.Dot(-L, Vector3.Normalize(light.Direction));
        float spotAngle = MathF.Acos(spotDot);
        
        if (spotAngle > light.OuterAngle) return Vector3.Zero;
        
        float spotAttenuation = 1.0f;
        if (spotAngle > light.InnerAngle)
        {
            float t = (spotAngle - light.InnerAngle) / (light.OuterAngle - light.InnerAngle);
            spotAttenuation = 1.0f - (t * t); // Smooth falloff
        }
        
        float attenuation = 1.0f / (1.0f + distance * distance * light.Attenuation);
        Vector3 radiance = light.Color * light.Intensity * attenuation * spotAttenuation;
        
        return HorizonBRDF.Calculate(input, L, radiance, _quality);
    }
    
    private Vector3 CalculateAreaLight(HorizonLight light, LightingInput input)
    {
        // Simplified area light using representative point method
        Vector3 L = light.Position - input.WorldPos;
        float distance = L.Length();
        
        if (distance >= light.Range) return Vector3.Zero;
        
        L /= distance;
        
        float NdotL = Math.Max(Vector3.Dot(input.Normal, L), 0.0f);
        if (NdotL <= 0.0f) return Vector3.Zero;
        
        // Area light attenuation with soft falloff
        float areaFactor = light.Area / (distance * distance + light.Area);
        float attenuation = areaFactor / (1.0f + distance * light.Attenuation);
        
        Vector3 radiance = light.Color * light.Intensity * attenuation;
        
        return HorizonBRDF.Calculate(input, L, radiance, _quality);
    }
    
    private void SortLightsByPriority(Vector3 cameraPos)
    {
        _lights.Sort((a, b) =>
        {
            // First by priority
            int priorityCompare = b.Priority.CompareTo(a.Priority);
            if (priorityCompare != 0) return priorityCompare;
            
            // Then by distance for same priority
            float distA = Vector3.DistanceSquared(a.Position, cameraPos);
            float distB = Vector3.DistanceSquared(b.Position, cameraPos);
            return distA.CompareTo(distB);
        });
    }
}

/// <summary>
/// Horizon BRDF - Optimized physically based shading model.
/// </summary>
public static class HorizonBRDF
{
    public static Vector3 Calculate(LightingInput input, Vector3 L, Vector3 radiance, LightingQuality quality)
    {
        Vector3 V = input.ViewDir;
        Vector3 N = input.Normal;
        Vector3 H = Vector3.Normalize(V + L);
        
        float NdotV = Math.Max(Vector3.Dot(N, V), 0.0f);
        float NdotL = Math.Max(Vector3.Dot(N, L), 0.0f);
        float NdotH = Math.Max(Vector3.Dot(N, H), 0.0f);
        float HdotV = Math.Max(Vector3.Dot(H, V), 0.0f);
        
        if (NdotL <= 0.0f) return Vector3.Zero;
        
        // Material properties
        float metallic = input.Metallic;
        float roughness = input.Roughness;
        Vector3 albedo = input.Albedo;
        float ao = input.AO;
        
        // Fresnel (Schlick approximation)
        Vector3 F0 = Vector3.Lerp(new Vector3(0.04f), albedo, metallic);
        Vector3 F = FresnelSchlick(HdotV, F0);
        
        // Distribution and Geometry
        float D, G;
        
        if (quality <= LightingQuality.Medium)
        {
            // Simplified for old hardware
            D = DistributionBlinnPhong(NdotH, roughness);
            G = 1.0f;
        }
        else
        {
            // Full GGX for modern hardware
            D = DistributionGGX(NdotH, roughness);
            G = GeometrySmith(NdotV, NdotL, roughness);
        }
        
        // Specular
        Vector3 specularNumerator = new Vector3(D * G) * F;
        float specularDenominator = 4.0f * NdotV * NdotL + 0.001f;
        Vector3 specular = specularNumerator / specularDenominator;
        
        // Diffuse (Lambert)
        Vector3 kS = F;
        Vector3 kD = (Vector3.One - kS) * (1.0f - metallic);
        Vector3 diffuse = kD * albedo / MathF.PI;
        
        // Combine
        Vector3 result = (diffuse + specular) * radiance * NdotL;
        
        // Apply AO
        result *= ao;
        
        return result;
    }
    
    private static Vector3 FresnelSchlick(float cosTheta, Vector3 F0)
    {
        float invCos = 1.0f - cosTheta;
        float invCos5 = invCos * invCos * invCos * invCos * invCos;
        return F0 + (Vector3.One - F0) * invCos5;
    }
    
    private static float DistributionGGX(float NdotH, float roughness)
    {
        float alpha = roughness * roughness;
        float alpha2 = alpha * alpha;
        float NdotH2 = NdotH * NdotH;
        float denom = NdotH2 * (alpha2 - 1.0f) + 1.0f;
        return alpha2 / (MathF.PI * denom * denom);
    }
    
    private static float DistributionBlinnPhong(float NdotH, float roughness)
    {
        float specPower = Math.Max(2.0f, 128.0f * (1.0f - roughness));
        return MathF.Pow(NdotH, specPower) * (specPower + 2.0f) / (2.0f * MathF.PI);
    }
    
    private static float GeometrySmith(float NdotV, float NdotL, float roughness)
    {
        float k = (roughness * roughness) / 2.0f;
        float ggx1 = NdotV / (NdotV * (1.0f - k) + k);
        float ggx2 = NdotL / (NdotL * (1.0f - k) + k);
        return ggx1 * ggx2;
    }
}

/// <summary>
/// Clustered light grid for efficient light culling.
/// </summary>
public class ClusteredLightGrid
{
    private readonly Dictionary<(int, int, int), List<HorizonLight>> _clusters = new();
    private int _gridSizeX = 16;
    private int _gridSizeY = 9; // 16:9 aspect ratio
    private int _gridSizeZ = 24; // Depth slices
    
    public void BuildClusters(List<HorizonLight> lights, Vector3 cameraPos,
                               Matrix4x4 viewProj, int screenWidth, int screenHeight,
                               float nearPlane, float farPlane,
                               int maxLightsPerCluster, int maxTotalLights)
    {
        _clusters.Clear();
        
        int totalLightsAdded = 0;
        
        foreach (var light in lights)
        {
            if (!light.IsEnabled) continue;
            if (totalLightsAdded >= maxTotalLights) break;
            
            // Get cluster range for this light
            var clusters = GetLightClusterRange(light, cameraPos, viewProj, 
                                                screenWidth, screenHeight,
                                                nearPlane, farPlane);
            
            foreach (var cluster in clusters)
            {
                if (!_clusters.ContainsKey(cluster))
                    _clusters[cluster] = new List<HorizonLight>();
                
                if (_clusters[cluster].Count < maxLightsPerCluster)
                {
                    _clusters[cluster].Add(light);
                    totalLightsAdded++;
                }
            }
        }
    }
    
    public HorizonLight[] QueryLights(Vector3 worldPos, Vector2 screenUV)
    {
        int x = (int)(screenUV.X * _gridSizeX);
        int y = (int)(screenUV.Y * _gridSizeY);
        int z = 0; // Depth cluster would need view space Z
        
        x = Math.Clamp(x, 0, _gridSizeX - 1);
        y = Math.Clamp(y, 0, _gridSizeY - 1);
        
        var key = (x, y, z);
        if (_clusters.TryGetValue(key, out var lights))
            return lights.ToArray();
        
        return Array.Empty<HorizonLight>();
    }
    
    private List<(int, int, int)> GetLightClusterRange(HorizonLight light, Vector3 cameraPos,
                                                       Matrix4x4 viewProj, int screenWidth, int screenHeight,
                                                       float nearPlane, float farPlane)
    {
        // Simplified: just use light position for cluster assignment
        // Full implementation would calculate screen-space bounds
        var clusters = new List<(int, int, int)>();
        
        Vector4 lightPos = Vector4.Transform(new Vector4(light.Position, 1.0f), viewProj);
        if (lightPos.W <= 0) return clusters;
        
        Vector3 ndc = new Vector3(lightPos.X / lightPos.W, lightPos.Y / lightPos.W, lightPos.Z / lightPos.W);
        Vector2 screenPos = new Vector2(ndc.X * 0.5f + 0.5f, ndc.Y * 0.5f + 0.5f);
        
        int x = (int)(screenPos.X * _gridSizeX);
        int y = (int)(screenPos.Y * _gridSizeY);
        int z = 0;
        
        // Add light to cluster and neighbors (simple 3x3 kernel)
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                int cx = x + dx;
                int cy = y + dy;
                if (cx >= 0 && cx < _gridSizeX && cy >= 0 && cy < _gridSizeY)
                {
                    clusters.Add((cx, cy, z));
                }
            }
        }
        
        return clusters;
    }
}

/// <summary>
/// Image Based Lighting for ambient environment lighting.
/// </summary>
public class IBLLighting
{
    public bool IsEnabled { get; set; } = true;
    public Guid EnvironmentMap { get; set; } = Guid.Empty;
    public Guid IrradianceMap { get; set; } = Guid.Empty;
    public Guid PrefilteredMap { get; set; } = Guid.Empty;
    public Guid BRDFLUT { get; set; } = Guid.Empty;
    
    public void PrepareFrame(Vector3 cameraPos)
    {
        // Update environment map parameters if needed
    }
    
    public Vector3 CalculateAmbient(LightingInput input)
    {
        // Simplified IBL - would sample cubemaps in full implementation
        Vector3 ambient = input.Albedo * 0.03f; // Base ambient
        
        // Add reflection contribution based on roughness/metallic
        float reflectivity = input.Metallic * (1.0f - input.Roughness);
        ambient += input.Albedo * reflectivity * 0.1f;
        
        return ambient * input.AO;
    }
}

/// <summary>
/// Volumetric lighting for fog and light scattering.
/// </summary>
public class VolumetricLighting
{
    public bool IsEnabled { get; set; } = false; // Disabled by default for performance
    public float Density { get; set; } = 0.01f;
    public Vector3 ScatteringColor { get; set; } = new Vector3(0.8f, 0.9f, 1.0f);
    
    public void PrepareFrame(Vector3 cameraPos, List<HorizonLight> lights)
    {
        // Update volumetric fog parameters
    }
    
    public Vector3 CalculateScattering(LightingInput input)
    {
        // Simplified volumetric effect
        float fogFactor = MathF.Exp(-Density * input.Depth);
        return ScatteringColor * (1.0f - fogFactor) * 0.1f;
    }
}

/// <summary>
/// Contact shadows for fine detail shadows near surfaces.
/// </summary>
public class ContactShadows
{
    public bool IsEnabled { get; set; } = true;
    public float MaxDistance { get; set; } = 1.0f;
    public float Thickness { get; set; } = 0.1f;
    
    public float CalculateShadow(LightingInput input)
    {
        // Simplified contact shadow calculation
        // Would use depth buffer comparison in full implementation
        return 1.0f;
    }
}

/// <summary>
/// Light data structure for Horizon Lighting.
/// </summary>
public class HorizonLight
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public LightType Type { get; set; } = LightType.Point;
    public LightPriority Priority { get; set; } = LightPriority.Auto;
    
    public Vector3 Position { get; set; }
    public Vector3 Direction { get; set; } = -Vector3.UnitZ;
    public Vector3 Color { get; set; } = Vector3.One;
    public float Intensity { get; set; } = 1.0f;
    
    // Attenuation
    public float Range { get; set; } = 10.0f;
    public float Attenuation { get; set; } = 0.1f;
    
    // Spot light angles (radians)
    public float InnerAngle { get; set; } = 0.35f; // ~20 degrees
    public float OuterAngle { get; set; } = 0.52f; // ~30 degrees
    
    // Area light
    public float Area { get; set; } = 1.0f;
    
    // Shadow casting
    public bool CastShadows { get; set; } = false;
    public ShadowType ShadowType { get; set; } = ShadowType.PCF;
    public float ShadowBias { get; set; } = 0.005f;
    public int ShadowResolution { get; set; } = 1024;
    
    // Volumetrics
    public bool VolumetricEnabled { get; set; } = false;
    public float VolumetricIntensity { get; set; } = 1.0f;
    
    // IES profile for realistic distribution
    public Guid IESProfile { get; set; } = Guid.Empty;
    
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// Lighting calculation input structure.
/// </summary>
public struct LightingInput
{
    public Vector3 WorldPos;
    public Vector3 Normal;
    public Vector3 ViewDir;
    public Vector2 ScreenUV;
    public float Depth;
    
    public Vector3 Albedo;
    public float Metallic;
    public float Roughness;
    public float AO;
}

public enum LightingQuality
{
    Low,      // Blinn-Phong, no IBL
    Medium,   // Simple PBR, basic IBL
    High,     // Full PBR, IBL, contact shadows
    Ultra     // Everything + volumetrics
}

public enum LightPriority
{
    Auto,
    Low,      // Can be culled first
    Medium,
    High      // Always rendered
}

public enum ShadowType
{
    None,
    Hard,
    PCF,      // Percentage closer filtering
    PCSS,     // Percentage closer soft shadows
    VSM       // Variance shadow maps
}
