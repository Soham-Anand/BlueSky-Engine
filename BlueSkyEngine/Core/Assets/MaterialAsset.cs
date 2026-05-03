using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using BlueSky.Rendering.Materials;
using System.Numerics;

namespace BlueSky.Core.Assets;

/// <summary>
/// Material asset (.blueskyasset) format for PBR materials.
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
    
    // Texture references (asset IDs — for future asset database integration)
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
    
    // Texture file paths — used by the renderer to load textures from disk.
    // These take priority over GUID references for direct file-based workflows.
    [JsonPropertyName("albedoTexturePath")]
    public string AlbedoTexturePath { get; set; } = "";
    
    [JsonPropertyName("normalTexturePath")]
    public string NormalTexturePath { get; set; } = "";
    
    [JsonPropertyName("rmaTexturePath")]
    public string RMATexturePath { get; set; } = ""; // Packed Roughness/Metallic/AO
    
    [JsonPropertyName("metallicTexturePath")]
    public string MetallicTexturePath { get; set; } = "";
    
    [JsonPropertyName("roughnessTexturePath")]
    public string RoughnessTexturePath { get; set; } = "";
    
    [JsonPropertyName("opacityTexturePath")]
    public string OpacityTexturePath { get; set; } = "";
    
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
    /// Save material to .blueskyasset file in BlueAsset format.
    /// </summary>
    public bool Save(string path)
    {
        try
        {
            if (!path.EndsWith(".blueskyasset"))
                path += ".blueskyasset";
            
            // Create a BlueAsset wrapper for the material
            var blueAsset = new BlueAsset
            {
                AssetId = MaterialId,
                AssetName = MaterialName,
                Type = AssetType.Material,
                SourceFile = path,
                ImportDate = DateTime.UtcNow,
                Metadata = new Dictionary<string, string>
                {
                    ["materialType"] = MaterialType.ToString(),
                    ["shader"] = Shader ?? "pbr_optimized"
                }
            };
            
            // Serialize material properties as JSON in the PayloadData field
            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            
            blueAsset.PayloadData = JsonSerializer.SerializeToUtf8Bytes(this, jsonOptions);
            
            return blueAsset.Save(path);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MaterialAsset] Failed to save: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Load material from .blueskyasset file.
    /// </summary>
    public static MaterialAsset? Load(string path)
    {
        try
        {
            Console.WriteLine($"[MaterialAsset] Loading material from: {path}");
            
            if (!File.Exists(path))
            {
                Console.WriteLine($"[MaterialAsset] File not found: {path}");
                return null;
            }
            
            // Try loading as BlueAsset first
            Console.WriteLine($"[MaterialAsset] Attempting to load as BlueAsset...");
            var blueAsset = BlueAsset.Load(path);
            
            if (blueAsset != null)
            {
                Console.WriteLine($"[MaterialAsset] BlueAsset loaded - Type: {blueAsset.Type}, HasPayload: {blueAsset.HasPayload}, PayloadSize: {blueAsset.PayloadData?.Length ?? 0}");
                
                if (blueAsset.Type == AssetType.Material && blueAsset.HasPayload)
                {
                    Console.WriteLine($"[MaterialAsset] Deserializing material from payload...");
                    
                    // Debug: print first few bytes of payload to see if it's valid JSON
                    if (blueAsset.PayloadData != null && blueAsset.PayloadData.Length > 0)
                    {
                        var preview = System.Text.Encoding.UTF8.GetString(blueAsset.PayloadData, 0, System.Math.Min(200, blueAsset.PayloadData.Length));
                        Console.WriteLine($"[MaterialAsset] Payload preview: {preview}");
                    }
                    
                    var material = JsonSerializer.Deserialize<MaterialAsset>(blueAsset.PayloadData);
                    Console.WriteLine($"[MaterialAsset] Deserialization {(material != null ? "SUCCESS" : "FAILED")}");
                    return material;
                }
                else
                {
                    Console.WriteLine($"[MaterialAsset] Not a material asset or no payload - Type: {blueAsset.Type}, HasPayload: {blueAsset.HasPayload}");
                }
            }
            else
            {
                Console.WriteLine($"[MaterialAsset] BlueAsset.Load returned null");
            }
            
            // Fallback: try loading as plain JSON (old format)
            Console.WriteLine($"[MaterialAsset] Trying fallback as plain JSON...");
            var json = File.ReadAllText(path);
            Console.WriteLine($"[MaterialAsset] JSON length: {json.Length}");
            
            if (json.Length > 0)
            {
                var preview = json.Substring(0, System.Math.Min(200, json.Length));
                Console.WriteLine($"[MaterialAsset] JSON preview: {preview}");
            }
            
            var fallbackMaterial = JsonSerializer.Deserialize<MaterialAsset>(json);
            Console.WriteLine($"[MaterialAsset] Fallback deserialization {(fallbackMaterial != null ? "SUCCESS" : "FAILED")}");
            return fallbackMaterial;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MaterialAsset] Failed to load: {ex.Message}");
            Console.WriteLine($"[MaterialAsset] Stack trace: {ex.StackTrace}");
            return null;
        }
    }
    
    /// <summary>
    /// Convert to Material System V2 (MaterialAssetV2).
    /// </summary>
    public BlueSky.Rendering.Materials.MaterialAssetV2 ToMaterialV2()
    {
        var material = new BlueSky.Rendering.Materials.MaterialAssetV2
        {
            Name = MaterialName,
            Albedo = new System.Numerics.Vector3(Albedo.X, Albedo.Y, Albedo.Z),
            Metallic = Metallic,
            Roughness = Roughness,
            Emissive = new System.Numerics.Vector3(Emission.X, Emission.Y, Emission.Z),
            EmissiveIntensity = EmissionIntensity,
            NormalStrength = NormalStrength,
            AO = AO,
            Tiling = new System.Numerics.Vector2(Tiling.X, Tiling.Y),
            Offset = new System.Numerics.Vector2(Offset.X, Offset.Y),
            Opacity = Opacity,
            EnableParallax = EnableParallax,
            UsePackedRMA = UseRoughnessMetallicAO
        };
        
        // Add texture slots
        if (!string.IsNullOrEmpty(AlbedoTexturePath))
        {
            material.Textures["albedoMap"] = new BlueSky.Rendering.Materials.TextureSlot
            {
                Path = AlbedoTexturePath,
                SamplerPreset = "anisotropic_repeat",
                IsSRGB = true
            };
        }
        
        if (!string.IsNullOrEmpty(NormalTexturePath))
        {
            material.Textures["normalMap"] = new BlueSky.Rendering.Materials.TextureSlot
            {
                Path = NormalTexturePath,
                SamplerPreset = "anisotropic_repeat",
                IsSRGB = false
            };
        }
        
        if (!string.IsNullOrEmpty(RMATexturePath))
        {
            material.Textures["rmaMap"] = new BlueSky.Rendering.Materials.TextureSlot
            {
                Path = RMATexturePath,
                SamplerPreset = "anisotropic_repeat",
                IsSRGB = false,
                Channels = "RMA"
            };
        }
        
        // Set render state
        material.RenderState.BlendMode = BlendMode switch
        {
            BlueSky.Rendering.Materials.BlendMode.Opaque => BlueSky.Rendering.Materials.BlendMode.Opaque,
            BlueSky.Rendering.Materials.BlendMode.AlphaTest => BlueSky.Rendering.Materials.BlendMode.AlphaTest,
            BlueSky.Rendering.Materials.BlendMode.AlphaBlend => BlueSky.Rendering.Materials.BlendMode.AlphaBlend,
            BlueSky.Rendering.Materials.BlendMode.Additive => BlueSky.Rendering.Materials.BlendMode.Additive,
            BlueSky.Rendering.Materials.BlendMode.Multiply => BlueSky.Rendering.Materials.BlendMode.Multiply,
            _ => BlueSky.Rendering.Materials.BlendMode.Opaque
        };
        material.RenderState.DoubleSided = DoubleSided;
        
        return material;
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
