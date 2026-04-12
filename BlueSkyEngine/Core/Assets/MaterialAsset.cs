using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using BlueSky.Rendering.Materials;
using System.Numerics;

namespace BlueSky.Core.Assets;

/// <summary>
/// Material asset (.blueskymaterial) format for PBR materials.
/// Stores material properties and texture references.
/// </summary>
public class MaterialAsset
{
    [JsonPropertyName("materialId")]
    public Guid MaterialId { get; set; } = Guid.NewGuid();
    
    [JsonPropertyName("materialName")]
    public string MaterialName { get; set; } = "DefaultMaterial";
    
    [JsonPropertyName("materialType")]
    public MaterialType MaterialType { get; set; } = MaterialType.PBR;
    
    [JsonPropertyName("shader")]
    public string Shader { get; set; } = "pbr_optimized";
    
    // PBR Properties
    [JsonPropertyName("albedo")]
    public Vector3Data Albedo { get; set; } = new Vector3Data(1.0f, 1.0f, 1.0f);
    
    [JsonPropertyName("metallic")]
    public float Metallic { get; set; } = 0.0f;
    
    [JsonPropertyName("roughness")]
    public float Roughness { get; set; } = 0.5f;
    
    [JsonPropertyName("emission")]
    public Vector3Data Emission { get; set; } = new Vector3Data(0.0f, 0.0f, 0.0f);
    
    [JsonPropertyName("emissionIntensity")]
    public float EmissionIntensity { get; set; } = 1.0f;
    
    [JsonPropertyName("normalStrength")]
    public float NormalStrength { get; set; } = 1.0f;
    
    [JsonPropertyName("ao")]
    public float AO { get; set; } = 1.0f;
    
    // Texture references (asset IDs)
    [JsonPropertyName("albedoTexture")]
    public Guid AlbedoTexture { get; set; } = Guid.Empty;
    
    [JsonPropertyName("normalTexture")]
    public Guid NormalTexture { get; set; } = Guid.Empty;
    
    [JsonPropertyName("roughnessTexture")]
    public Guid RoughnessTexture { get; set; } = Guid.Empty;
    
    [JsonPropertyName("metallicTexture")]
    public Guid MetallicTexture { get; set; } = Guid.Empty;
    
    [JsonPropertyName("emissionTexture")]
    public Guid EmissionTexture { get; set; } = Guid.Empty;
    
    [JsonPropertyName("aoTexture")]
    public Guid AOTexture { get; set; } = Guid.Empty;
    
    [JsonPropertyName("rmaTexture")]
    public Guid RMATexture { get; set; } = Guid.Empty; // Packed RMA
    
    // Tiling and offset
    [JsonPropertyName("tiling")]
    public Vector2Data Tiling { get; set; } = new Vector2Data(1.0f, 1.0f);
    
    [JsonPropertyName("offset")]
    public Vector2Data Offset { get; set; } = new Vector2Data(0.0f, 0.0f);
    
    // Transparency
    [JsonPropertyName("opacity")]
    public float Opacity { get; set; } = 1.0f;
    
    [JsonPropertyName("blendMode")]
    public BlendMode BlendMode { get; set; } = BlendMode.Opaque;
    
    [JsonPropertyName("doubleSided")]
    public bool DoubleSided { get; set; } = false;
    
    // Optimization flags
    [JsonPropertyName("useSimplifiedLighting")]
    public bool UseSimplifiedLighting { get; set; } = false;
    
    [JsonPropertyName("enableParallax")]
    public bool EnableParallax { get; set; } = false;
    
    [JsonPropertyName("enableDetailMaps")]
    public bool EnableDetailMaps { get; set; } = false;
    
    [JsonPropertyName("useRoughnessMetallicAO")]
    public bool UseRoughnessMetallicAO { get; set; } = true;
    
    // Editor integration
    [JsonPropertyName("isPreviewMaterial")]
    public bool IsPreviewMaterial { get; set; } = false;
    
    [JsonPropertyName("editorCategory")]
    public string EditorCategory { get; set; } = "Default";
    
    [JsonPropertyName("thumbnail")]
    public string Thumbnail { get; set; } = "";
    
    // Quality settings
    [JsonPropertyName("maxLOD")]
    public int MaxLOD { get; set; } = 0;
    
    [JsonPropertyName("forceLowQuality")]
    public bool ForceLowQuality { get; set; } = false;
    
    // Metadata
    [JsonPropertyName("tags")]
    public string[] Tags { get; set; } = Array.Empty<string>();
    
    [JsonPropertyName("parentMaterial")]
    public Guid ParentMaterial { get; set; } = Guid.Empty; // For material instances
    
    /// <summary>
    /// Save material to .blueskymaterial file.
    /// </summary>
    public bool Save(string path)
    {
        try
        {
            if (!path.EndsWith(".blueskymaterial"))
                path += ".blueskymaterial";
            
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            
            var json = JsonSerializer.Serialize(this, options);
            File.WriteAllText(path, json);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MaterialAsset] Failed to save: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Load material from .blueskymaterial file.
    /// </summary>
    public static MaterialAsset? Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<MaterialAsset>(json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MaterialAsset] Failed to load: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Convert to runtime PBR material.
    /// </summary>
    public PBRMaterial ToRuntimeMaterial()
    {
        return new PBRMaterial
        {
            MaterialId = MaterialId,
            Name = MaterialName,
            Albedo = new System.Numerics.Vector3(Albedo.X, Albedo.Y, Albedo.Z),
            Metallic = Metallic,
            Roughness = Roughness,
            Emission = new System.Numerics.Vector3(Emission.X, Emission.Y, Emission.Z),
            EmissionIntensity = EmissionIntensity,
            NormalStrength = NormalStrength,
            AO = AO,
            AlbedoTexture = AlbedoTexture,
            NormalTexture = NormalTexture,
            MetallicTexture = MetallicTexture,
            RoughnessTexture = RoughnessTexture,
            EmissionTexture = EmissionTexture,
            AOTexture = AOTexture,
            Tiling = new System.Numerics.Vector2(Tiling.X, Tiling.Y),
            Offset = new System.Numerics.Vector2(Offset.X, Offset.Y),
            Opacity = Opacity,
            BlendMode = BlendMode,
            DoubleSided = DoubleSided,
            UseSimplifiedLighting = UseSimplifiedLighting,
            EnableParallax = EnableParallax,
            EnableDetailMaps = EnableDetailMaps,
            UseRoughnessMetallicAO = UseRoughnessMetallicAO,
            MaxLOD = MaxLOD,
            ForceLowQuality = ForceLowQuality
        };
    }
}

// Helper data structures for JSON serialization
public class Vector3Data
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    
    public Vector3Data() { }
    
    public Vector3Data(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }
}

public class Vector2Data
{
    public float X { get; set; }
    public float Y { get; set; }
    
    public Vector2Data() { }
    
    public Vector2Data(float x, float y)
    {
        X = x;
        Y = y;
    }
}

public enum MaterialType
{
    PBR,
    Unlit,
    Subsurface,
    Toon,
    Custom
}
