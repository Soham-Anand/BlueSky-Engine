// BlueSkyEngine - Material Asset V2
// Production-grade material system with texture streaming and shader permutations

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using BlueSky.Rendering.Textures;
using BlueSky.Core.Diagnostics;

namespace BlueSky.Rendering.Materials;

/// <summary>
/// Material Asset V2 - Complete rewrite with modern features.
/// Supports PBR workflow, texture streaming, shader permutations, and instancing.
/// </summary>
public class MaterialAssetV2
{
    // ═══════════════════════════════════════════════════════════════
    //  METADATA
    // ═══════════════════════════════════════════════════════════════
    
    [JsonPropertyName("version")]
    public int Version { get; set; } = 2;
    
    [JsonPropertyName("guid")]
    public Guid Guid { get; set; } = Guid.NewGuid();
    
    [JsonPropertyName("name")]
    public string Name { get; set; } = "NewMaterial";
    
    [JsonPropertyName("shader")]
    public string ShaderName { get; set; } = "PBR_Standard";
    
    // ═══════════════════════════════════════════════════════════════
    //  PBR PROPERTIES
    // ═══════════════════════════════════════════════════════════════
    
    [JsonPropertyName("albedo")]
    public Vector3 Albedo { get; set; } = Vector3.One;
    
    [JsonPropertyName("metallic")]
    public float Metallic { get; set; } = 0.0f;
    
    [JsonPropertyName("roughness")]
    public float Roughness { get; set; } = 0.5f;
    
    [JsonPropertyName("normalStrength")]
    public float NormalStrength { get; set; } = 1.0f;
    
    [JsonPropertyName("ao")]
    public float AO { get; set; } = 1.0f;
    
    [JsonPropertyName("emissive")]
    public Vector3 Emissive { get; set; } = Vector3.Zero;
    
    [JsonPropertyName("emissiveIntensity")]
    public float EmissiveIntensity { get; set; } = 1.0f;
    
    [JsonPropertyName("opacity")]
    public float Opacity { get; set; } = 1.0f;
    
    // ═══════════════════════════════════════════════════════════════
    //  TEXTURE MAPS
    // ═══════════════════════════════════════════════════════════════
    
    [JsonPropertyName("textures")]
    public Dictionary<string, TextureSlot> Textures { get; set; } = new();
    
    // ═══════════════════════════════════════════════════════════════
    //  RENDER STATE
    // ═══════════════════════════════════════════════════════════════
    
    [JsonPropertyName("renderState")]
    public RenderState RenderState { get; set; } = new();
    
    // ═══════════════════════════════════════════════════════════════
    //  ADVANCED FEATURES
    // ═══════════════════════════════════════════════════════════════
    
    [JsonPropertyName("tiling")]
    public Vector2 Tiling { get; set; } = Vector2.One;
    
    [JsonPropertyName("offset")]
    public Vector2 Offset { get; set; } = Vector2.Zero;
    
    [JsonPropertyName("usePackedRMA")]
    public bool UsePackedRMA { get; set; } = true; // Roughness-Metallic-AO in one texture
    
    [JsonPropertyName("enableParallax")]
    public bool EnableParallax { get; set; } = false;
    
    [JsonPropertyName("parallaxScale")]
    public float ParallaxScale { get; set; } = 0.05f;
    
    // ═══════════════════════════════════════════════════════════════
    //  SHADER PERMUTATION
    // ═══════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Get shader permutation key based on active features.
    /// </summary>
    [JsonIgnore]
    public MaterialFeatures Features
    {
        get
        {
            MaterialFeatures features = MaterialFeatures.None;
            
            if (Textures.ContainsKey("albedoMap")) features |= MaterialFeatures.AlbedoMap;
            if (Textures.ContainsKey("normalMap")) features |= MaterialFeatures.NormalMap;
            if (Textures.ContainsKey("roughnessMap")) features |= MaterialFeatures.RoughnessMap;
            if (Textures.ContainsKey("metallicMap")) features |= MaterialFeatures.MetallicMap;
            if (Textures.ContainsKey("aoMap")) features |= MaterialFeatures.AOMap;
            if (Textures.ContainsKey("emissiveMap")) features |= MaterialFeatures.EmissiveMap;
            if (Textures.ContainsKey("heightMap")) features |= MaterialFeatures.HeightMap;
            if (UsePackedRMA && Textures.ContainsKey("rmaMap")) features |= MaterialFeatures.PackedRMA;
            if (EnableParallax) features |= MaterialFeatures.Parallax;
            if (RenderState.BlendMode == BlendMode.AlphaBlend) features |= MaterialFeatures.AlphaBlend;
            if (RenderState.BlendMode == BlendMode.AlphaTest) features |= MaterialFeatures.AlphaTest;
            if (RenderState.DoubleSided) features |= MaterialFeatures.DoubleSided;
            
            return features;
        }
    }
    
    // ═══════════════════════════════════════════════════════════════
    //  SERIALIZATION
    // ═══════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Save material to JSON file.
    /// </summary>
    public void Save(string path)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            
            string json = JsonSerializer.Serialize(this, options);
            File.WriteAllText(path, json);
            
            ErrorHandler.LogInfo($"Material saved: {path}", "MaterialAssetV2");
        }
        catch (Exception ex)
        {
            ErrorHandler.LogError($"Failed to save material: {path}", ex, "MaterialAssetV2");
        }
    }
    
    /// <summary>
    /// Load material from JSON file.
    /// </summary>
    public static MaterialAssetV2? Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                ErrorHandler.LogError($"Material file not found: {path}", null, "MaterialAssetV2");
                return null;
            }
            
            string json = File.ReadAllText(path);
            var material = JsonSerializer.Deserialize<MaterialAssetV2>(json);
            
            if (material != null)
            {
                ErrorHandler.LogInfo($"Material loaded: {path}", "MaterialAssetV2");
            }
            
            return material;
        }
        catch (Exception ex)
        {
            ErrorHandler.LogError($"Failed to load material: {path}. Using fallback missing material.", ex, "MaterialAssetV2");
            return CreateMissing();
        }
    }
    
    // ═══════════════════════════════════════════════════════════════
    //  MATERIAL INSTANCING
    // ═══════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Create a material instance with per-instance parameters.
    /// </summary>
    public MaterialInstanceV2 CreateInstance()
    {
        return new MaterialInstanceV2(this);
    }
    
    // ═══════════════════════════════════════════════════════════════
    //  FACTORY METHODS
    // ═══════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Create default PBR material.
    /// </summary>
    public static MaterialAssetV2 CreateDefault(string name = "DefaultMaterial")
    {
        return new MaterialAssetV2
        {
            Name = name,
            Albedo = new Vector3(0.8f, 0.8f, 0.8f),
            Metallic = 0.0f,
            Roughness = 0.5f,
            RenderState = new RenderState
            {
                BlendMode = BlendMode.Opaque,
                CullMode = CullMode.Back,
                DepthTest = true,
                DepthWrite = true
            }
        };
    }
    
    /// <summary>
    /// Create missing material fallback (Magenta/Checkerboard fallback look).
    /// </summary>
    public static MaterialAssetV2 CreateMissing()
    {
        return new MaterialAssetV2
        {
            Name = "MissingMaterial",
            Albedo = new Vector3(1.0f, 0.0f, 1.0f), // Magenta
            Metallic = 0.0f,
            Roughness = 1.0f,
            RenderState = new RenderState
            {
                BlendMode = BlendMode.Opaque,
                CullMode = CullMode.Back,
                DepthTest = true,
                DepthWrite = true
            }
        };
    }
    
    /// <summary>
    /// Create PBR material with textures.
    /// </summary>
    public static MaterialAssetV2 CreatePBR(string name, 
                                            string? albedoPath = null,
                                            string? normalPath = null,
                                            string? rmaPath = null)
    {
        var material = CreateDefault(name);
        
        if (!string.IsNullOrEmpty(albedoPath))
        {
            material.Textures["albedoMap"] = new TextureSlot
            {
                Path = albedoPath,
                SamplerPreset = "anisotropic_repeat",
                IsSRGB = true
            };
        }
        
        if (!string.IsNullOrEmpty(normalPath))
        {
            material.Textures["normalMap"] = new TextureSlot
            {
                Path = normalPath,
                SamplerPreset = "anisotropic_repeat",
                IsSRGB = false
            };
        }
        
        if (!string.IsNullOrEmpty(rmaPath))
        {
            material.Textures["rmaMap"] = new TextureSlot
            {
                Path = rmaPath,
                SamplerPreset = "anisotropic_repeat",
                IsSRGB = false,
                Channels = "RMA" // R=Roughness, G=Metallic, B=AO
            };
            material.UsePackedRMA = true;
        }
        
        return material;
    }
}

/// <summary>
/// Texture slot - defines a texture and its sampling parameters.
/// </summary>
public class TextureSlot
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";
    
    [JsonPropertyName("samplerPreset")]
    public string SamplerPreset { get; set; } = "trilinear_repeat";
    
    [JsonPropertyName("sRGB")]
    public bool IsSRGB { get; set; } = false;
    
    [JsonPropertyName("channels")]
    public string Channels { get; set; } = "RGBA";
    
    [JsonPropertyName("tiling")]
    public Vector2? Tiling { get; set; }
    
    [JsonPropertyName("offset")]
    public Vector2? Offset { get; set; }
    
    /// <summary>
    /// Get sampler state from preset name.
    /// </summary>
    [JsonIgnore]
    public SamplerState SamplerState => SamplerPreset.ToLower() switch
    {
        "point_repeat" => Textures.SamplerState.PointRepeat,
        "bilinear_repeat" => Textures.SamplerState.BilinearRepeat,
        "trilinear_repeat" => Textures.SamplerState.TrilinearRepeat,
        "anisotropic_repeat" => Textures.SamplerState.Anisotropic8xRepeat,
        "trilinear_clamp" => Textures.SamplerState.TrilinearClamp,
        _ => Textures.SamplerState.TrilinearRepeat
    };
}

/// <summary>
/// Render state - defines how material is rendered.
/// </summary>
public class RenderState
{
    [JsonPropertyName("blendMode")]
    public BlendMode BlendMode { get; set; } = BlendMode.Opaque;
    
    [JsonPropertyName("cullMode")]
    public CullMode CullMode { get; set; } = CullMode.Back;
    
    [JsonPropertyName("depthTest")]
    public bool DepthTest { get; set; } = true;
    
    [JsonPropertyName("depthWrite")]
    public bool DepthWrite { get; set; } = true;
    
    [JsonPropertyName("doubleSided")]
    public bool DoubleSided { get; set; } = false;
    
    [JsonPropertyName("alphaTestThreshold")]
    public float AlphaTestThreshold { get; set; } = 0.5f;
}

/// <summary>
/// Blend mode.
/// </summary>
public enum BlendMode
{
    Opaque,      // No blending
    AlphaTest,   // Cutout/masked
    AlphaBlend,  // Transparent
    Additive,    // Additive blending
    Multiply     // Multiplicative blending
}

/// <summary>
/// Cull mode.
/// </summary>
public enum CullMode
{
    None,   // No culling (double-sided)
    Front,  // Cull front faces
    Back    // Cull back faces (default)
}

/// <summary>
/// Material feature flags for shader permutations.
/// </summary>
[Flags]
public enum MaterialFeatures : ulong
{
    None = 0,
    AlbedoMap = 1 << 0,
    NormalMap = 1 << 1,
    RoughnessMap = 1 << 2,
    MetallicMap = 1 << 3,
    AOMap = 1 << 4,
    EmissiveMap = 1 << 5,
    HeightMap = 1 << 6,
    DetailMap = 1 << 7,
    PackedRMA = 1 << 8,        // Roughness-Metallic-AO packed
    VertexColors = 1 << 9,
    AlphaTest = 1 << 10,
    AlphaBlend = 1 << 11,
    DoubleSided = 1 << 12,
    Parallax = 1 << 13,
    Emissive = 1 << 14,
    Subsurface = 1 << 15,
    ClearCoat = 1 << 16,
    Anisotropy = 1 << 17,
    Sheen = 1 << 18,
    Transmission = 1 << 19,
}
