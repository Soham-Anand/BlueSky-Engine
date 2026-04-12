using System;
using System.Collections.Generic;
using System.Numerics;

namespace BlueSky.Rendering.Materials;

/// <summary>
/// Physically Based Rendering (PBR) material system optimized for old hardware.
/// Uses simplified PBR model with fallback options for lower-end GPUs.
/// </summary>
public class PBRMaterial
{
    // Base properties
    public Guid MaterialId { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "DefaultMaterial";
    
    // Albedo (base color)
    public Vector3 Albedo { get; set; } = new Vector3(1.0f, 1.0f, 1.0f);
    public Guid AlbedoTexture { get; set; } = Guid.Empty;
    
    // Metallic workflow
    private float _metallic = 0.0f;
    public float Metallic 
    { 
        get => _metallic; 
        set => _metallic = Math.Clamp(value, 0.0f, 1.0f); 
    }
    public Guid MetallicTexture { get; set; } = Guid.Empty;
    
    // Roughness
    private float _roughness = 0.5f;
    public float Roughness 
    { 
        get => _roughness; 
        set => _roughness = Math.Clamp(value, 0.01f, 1.0f); // Avoid 0 to prevent division errors
    }
    public Guid RoughnessTexture { get; set; } = Guid.Empty;
    
    // Specular workflow (alternative to metallic)
    public Vector3 Specular { get; set; } = new Vector3(0.5f, 0.5f, 0.5f);
    public float SpecularIntensity { get; set; } = 1.0f;
    
    // Normal map
    public float NormalStrength { get; set; } = 1.0f;
    public Guid NormalTexture { get; set; } = Guid.Empty;
    
    // Emission
    public Vector3 Emission { get; set; } = Vector3.Zero;
    public float EmissionIntensity { get; set; } = 1.0f;
    public Guid EmissionTexture { get; set; } = Guid.Empty;
    
    // Ambient Occlusion
    public float AO { get; set; } = 1.0f;
    public Guid AOTexture { get; set; } = Guid.Empty;
    
    // Optimization flags for old hardware
    public bool UseSimplifiedLighting { get; set; } = false; // Fallback to Blinn-Phong
    public bool EnableParallax { get; set; } = false; // Disable on old GPUs
    public bool EnableDetailMaps { get; set; } = false; // Extra detail texture
    public bool UseRoughnessMetallicAO { get; set; } = true; // Packed RMA texture
    
    // Tiling and offset
    public Vector2 Tiling { get; set; } = Vector2.One;
    public Vector2 Offset { get; set; } = Vector2.Zero;
    
    // Transparency
    public float Opacity { get; set; } = 1.0f;
    public BlendMode BlendMode { get; set; } = BlendMode.Opaque;
    public bool DoubleSided { get; set; } = false;
    
    // Performance settings
    public int MaxLOD { get; set; } = 0; // Force material LOD
    public bool ForceLowQuality { get; set; } = false;
    
    /// <summary>
    /// Get the shader permutation key based on material features.
    /// </summary>
    public string GetShaderKey()
    {
        var key = "PBR";
        
        if (AlbedoTexture != Guid.Empty) key += "_ALBEDO_TEX";
        if (MetallicTexture != Guid.Empty || UseRoughnessMetallicAO) key += "_RMA_TEX";
        if (NormalTexture != Guid.Empty) key += "_NORMAL_TEX";
        if (EmissionTexture != Guid.Empty) key += "_EMISSION_TEX";
        if (AOTexture != Guid.Empty && !UseRoughnessMetallicAO) key += "_AO_TEX";
        if (EnableParallax) key += "_PARALLAX";
        if (EnableDetailMaps) key += "_DETAIL";
        if (BlendMode != BlendMode.Opaque) key += "_TRANSPARENT";
        if (DoubleSided) key += "_DOUBLESIDED";
        if (UseSimplifiedLighting) key += "_SIMPLE";
        
        return key;
    }
    
    /// <summary>
    /// Create a simplified version for low-end hardware.
    /// </summary>
    public PBRMaterial GetSimplifiedVersion()
    {
        return new PBRMaterial
        {
            MaterialId = MaterialId,
            Name = $"{Name}_Low",
            Albedo = Albedo,
            AlbedoTexture = AlbedoTexture,
            Metallic = Metallic,
            Roughness = Roughness,
            NormalTexture = Guid.Empty, // Disable normal maps
            Emission = Emission,
            UseSimplifiedLighting = true,
            EnableParallax = false,
            EnableDetailMaps = false,
            BlendMode = BlendMode,
            DoubleSided = DoubleSided,
            ForceLowQuality = true
        };
    }
    
    /// <summary>
    /// Quality presets for different GPU tiers.
    /// </summary>
    public static class QualityPresets
    {
        public static PBRMaterial LowQuality => new()
        {
            UseSimplifiedLighting = true,
            EnableParallax = false,
            EnableDetailMaps = false,
            UseRoughnessMetallicAO = false,
            NormalTexture = Guid.Empty,
            ForceLowQuality = true
        };
        
        public static PBRMaterial MediumQuality => new()
        {
            UseSimplifiedLighting = false,
            EnableParallax = false,
            EnableDetailMaps = false,
            UseRoughnessMetallicAO = true,
            NormalTexture = Guid.Empty
        };
        
        public static PBRMaterial HighQuality => new()
        {
            UseSimplifiedLighting = false,
            EnableParallax = true,
            EnableDetailMaps = true,
            UseRoughnessMetallicAO = true
        };
    }
}

public enum BlendMode
{
    Opaque,
    Masked, // Alpha test
    Transparent, // Alpha blend
    Additive
}
