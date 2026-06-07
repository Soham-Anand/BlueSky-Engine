// BlueSkyEngine - Material LOD System
// Simplifies materials at distance for better performance

using System;
using System.Numerics;

namespace BlueSky.Rendering.Materials;

/// <summary>
/// Material LOD system - simplifies materials based on distance.
/// Reduces texture quality, shader complexity, and feature count at distance.
/// </summary>
public class MaterialLOD
{
    private readonly MaterialAssetV2 _baseMaterial;
    private readonly MaterialAssetV2[] _lodLevels;
    
    public int LODCount => _lodLevels.Length;
    
    public MaterialLOD(MaterialAssetV2 baseMaterial, int lodCount = 4)
    {
        _baseMaterial = baseMaterial;
        _lodLevels = new MaterialAssetV2[lodCount];
        
        // Generate LOD levels
        for (int i = 0; i < lodCount; i++)
        {
            _lodLevels[i] = GenerateLOD(baseMaterial, i);
        }
    }
    
    /// <summary>
    /// Select material LOD based on distance.
    /// </summary>
    public MaterialAssetV2 SelectLOD(float distance, Vector3 cameraPosition, Vector3 objectPosition)
    {
        float dist = Vector3.Distance(cameraPosition, objectPosition);
        
        // LOD thresholds
        if (dist < 50f) return _lodLevels[0];      // LOD 0: 0-50m (full quality)
        if (dist < 100f) return _lodLevels[1];     // LOD 1: 50-100m (high quality)
        if (dist < 200f) return _lodLevels[2];     // LOD 2: 100-200m (medium quality)
        return _lodLevels[3];                       // LOD 3: 200m+ (low quality)
    }
    
    /// <summary>
    /// Get LOD level by index.
    /// </summary>
    public MaterialAssetV2 GetLOD(int level)
    {
        return _lodLevels[Math.Clamp(level, 0, _lodLevels.Length - 1)];
    }
    
    private MaterialAssetV2 GenerateLOD(MaterialAssetV2 baseMaterial, int lodLevel)
    {
        var lod = new MaterialAssetV2
        {
            Name = $"{baseMaterial.Name}_LOD{lodLevel}",
            ShaderName = baseMaterial.ShaderName,
            Albedo = baseMaterial.Albedo,
            Metallic = baseMaterial.Metallic,
            Roughness = baseMaterial.Roughness,
            RenderState = baseMaterial.RenderState
        };
        
        switch (lodLevel)
        {
            case 0: // LOD 0: Full quality (0-50m)
                // Keep all textures and features
                lod.Textures = new Dictionary<string, TextureSlot>(baseMaterial.Textures);
                lod.NormalStrength = baseMaterial.NormalStrength;
                lod.EnableParallax = baseMaterial.EnableParallax;
                break;
            
            case 1: // LOD 1: High quality (50-100m)
                // Keep albedo, normal, RMA
                // Reduce sampler quality
                if (baseMaterial.Textures.ContainsKey("albedoMap"))
                {
                    var albedo = baseMaterial.Textures["albedoMap"];
                    lod.Textures["albedoMap"] = new TextureSlot
                    {
                        Path = albedo.Path,
                        SamplerPreset = "trilinear_repeat", // Reduce from anisotropic
                        IsSRGB = albedo.IsSRGB
                    };
                }
                
                if (baseMaterial.Textures.ContainsKey("normalMap"))
                {
                    var normal = baseMaterial.Textures["normalMap"];
                    lod.Textures["normalMap"] = new TextureSlot
                    {
                        Path = normal.Path,
                        SamplerPreset = "trilinear_repeat",
                        IsSRGB = false
                    };
                }
                
                if (baseMaterial.Textures.ContainsKey("rmaMap"))
                {
                    lod.Textures["rmaMap"] = baseMaterial.Textures["rmaMap"];
                }
                
                lod.NormalStrength = baseMaterial.NormalStrength * 0.8f;
                lod.EnableParallax = false; // Disable parallax
                break;
            
            case 2: // LOD 2: Medium quality (100-200m)
                // Keep albedo and RMA only
                // Drop normal map
                if (baseMaterial.Textures.ContainsKey("albedoMap"))
                {
                    var albedo = baseMaterial.Textures["albedoMap"];
                    lod.Textures["albedoMap"] = new TextureSlot
                    {
                        Path = albedo.Path,
                        SamplerPreset = "bilinear_repeat", // Further reduce quality
                        IsSRGB = albedo.IsSRGB
                    };
                }
                
                if (baseMaterial.Textures.ContainsKey("rmaMap"))
                {
                    lod.Textures["rmaMap"] = baseMaterial.Textures["rmaMap"];
                }
                
                lod.NormalStrength = 0f; // No normal map
                break;
            
            case 3: // LOD 3: Low quality (200m+)
                // Albedo only, point filtering
                if (baseMaterial.Textures.ContainsKey("albedoMap"))
                {
                    var albedo = baseMaterial.Textures["albedoMap"];
                    lod.Textures["albedoMap"] = new TextureSlot
                    {
                        Path = albedo.Path,
                        SamplerPreset = "point_repeat", // Lowest quality
                        IsSRGB = albedo.IsSRGB
                    };
                }
                
                // Use constant values for PBR
                lod.Roughness = 0.8f;
                lod.Metallic = 0.0f;
                break;
        }
        
        return lod;
    }
}

/// <summary>
/// Material LOD selector - manages LOD selection for all materials.
/// </summary>
public class MaterialLODSelector
{
    private readonly Dictionary<Guid, MaterialLOD> _lodSystems = new();
    
    /// <summary>
    /// Register material for LOD management.
    /// </summary>
    public void RegisterMaterial(MaterialAssetV2 material, int lodCount = 4)
    {
        if (!_lodSystems.ContainsKey(material.Guid))
        {
            _lodSystems[material.Guid] = new MaterialLOD(material, lodCount);
        }
    }
    
    /// <summary>
    /// Select appropriate LOD for material based on distance.
    /// </summary>
    public MaterialAssetV2? SelectLOD(Guid materialId, Vector3 cameraPosition, Vector3 objectPosition)
    {
        if (_lodSystems.TryGetValue(materialId, out var lodSystem))
        {
            float distance = Vector3.Distance(cameraPosition, objectPosition);
            return lodSystem.SelectLOD(distance, cameraPosition, objectPosition);
        }
        
        return null;
    }
    
    /// <summary>
    /// Clear all LOD systems.
    /// </summary>
    public void Clear()
    {
        _lodSystems.Clear();
    }
}
