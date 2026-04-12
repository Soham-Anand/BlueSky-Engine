using System;
using System.Collections.Generic;
using BlueSky.Core.Assets;

namespace BlueSky.Rendering.Materials;

/// <summary>
/// Material instance system for creating variations of base materials.
/// Allows overriding material parameters without duplicating texture data.
/// Optimized for old hardware with parameter caching.
/// </summary>
public class MaterialInstance
{
    public Guid InstanceId { get; set; } = Guid.NewGuid();
    public Guid ParentMaterialId { get; set; }
    public string InstanceName { get; set; } = "MaterialInstance";
    
    // Parameter overrides
    private readonly Dictionary<string, object> _parameterOverrides = new();
    
    // Cached runtime material (updated when parent changes)
    private PBRMaterial _cachedMaterial;
    private bool _isDirty = true;
    
    public MaterialInstance(Guid parentMaterialId)
    {
        ParentMaterialId = parentMaterialId;
    }
    
    /// <summary>
    /// Set a parameter override.
    /// </summary>
    public void SetParameter(string name, object value)
    {
        _parameterOverrides[name] = value;
        _isDirty = true;
    }
    
    /// <summary>
    /// Get a parameter value (with fallback to parent).
    /// </summary>
    public T GetParameter<T>(string name, T defaultValue = default)
    {
        if (_parameterOverrides.TryGetValue(name, out var value))
        {
            try
            {
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }
        
        return defaultValue;
    }
    
    /// <summary>
    /// Clear all parameter overrides.
    /// </summary>
    public void ClearOverrides()
    {
        _parameterOverrides.Clear();
        _isDirty = true;
    }
    
    /// <summary>
    /// Apply overrides to a base material.
    /// </summary>
    public PBRMaterial ApplyOverrides(PBRMaterial baseMaterial)
    {
        if (!_isDirty && _cachedMaterial != null)
            return _cachedMaterial;
        
        // Create copy of base material
        var instance = new PBRMaterial
        {
            MaterialId = InstanceId,
            Name = InstanceName,
            Albedo = baseMaterial.Albedo,
            Metallic = baseMaterial.Metallic,
            Roughness = baseMaterial.Roughness,
            Emission = baseMaterial.Emission,
            EmissionIntensity = baseMaterial.EmissionIntensity,
            NormalStrength = baseMaterial.NormalStrength,
            AO = baseMaterial.AO,
            AlbedoTexture = baseMaterial.AlbedoTexture,
            NormalTexture = baseMaterial.NormalTexture,
            MetallicTexture = baseMaterial.MetallicTexture,
            RoughnessTexture = baseMaterial.RoughnessTexture,
            EmissionTexture = baseMaterial.EmissionTexture,
            AOTexture = baseMaterial.AOTexture,
            Tiling = baseMaterial.Tiling,
            Offset = baseMaterial.Offset,
            Opacity = baseMaterial.Opacity,
            BlendMode = baseMaterial.BlendMode,
            DoubleSided = baseMaterial.DoubleSided,
            UseSimplifiedLighting = baseMaterial.UseSimplifiedLighting,
            EnableParallax = baseMaterial.EnableParallax,
            EnableDetailMaps = baseMaterial.EnableDetailMaps,
            UseRoughnessMetallicAO = baseMaterial.UseRoughnessMetallicAO,
            MaxLOD = baseMaterial.MaxLOD,
            ForceLowQuality = baseMaterial.ForceLowQuality
        };
        
        // Apply overrides
        foreach (var kvp in _parameterOverrides)
        {
            switch (kvp.Key.ToLower())
            {
                case "albedo":
                    if (kvp.Value is System.Numerics.Vector3 albedo)
                        instance.Albedo = albedo;
                    break;
                case "metallic":
                    if (kvp.Value is float metallic)
                        instance.Metallic = metallic;
                    break;
                case "roughness":
                    if (kvp.Value is float roughness)
                        instance.Roughness = roughness;
                    break;
                case "emission":
                    if (kvp.Value is System.Numerics.Vector3 emission)
                        instance.Emission = emission;
                    break;
                case "emissionintensity":
                    if (kvp.Value is float emissionIntensity)
                        instance.EmissionIntensity = emissionIntensity;
                    break;
                case "normalstrength":
                    if (kvp.Value is float normalStrength)
                        instance.NormalStrength = normalStrength;
                    break;
                case "ao":
                    if (kvp.Value is float ao)
                        instance.AO = ao;
                    break;
                case "opacity":
                    if (kvp.Value is float opacity)
                        instance.Opacity = opacity;
                    break;
                case "tiling":
                    if (kvp.Value is System.Numerics.Vector2 tiling)
                        instance.Tiling = tiling;
                    break;
                case "offset":
                    if (kvp.Value is System.Numerics.Vector2 offset)
                        instance.Offset = offset;
                    break;
                case "simplifiedlighting":
                    if (kvp.Value is bool simplifiedLighting)
                        instance.UseSimplifiedLighting = simplifiedLighting;
                    break;
                case "enableparallax":
                    if (kvp.Value is bool enableParallax)
                        instance.EnableParallax = enableParallax;
                    break;
                case "enabledetailmaps":
                    if (kvp.Value is bool enableDetailMaps)
                        instance.EnableDetailMaps = enableDetailMaps;
                    break;
                case "forcelowquality":
                    if (kvp.Value is bool forceLowQuality)
                        instance.ForceLowQuality = forceLowQuality;
                    break;
            }
        }
        
        _cachedMaterial = instance;
        _isDirty = false;
        
        return instance;
    }
    
    /// <summary>
    /// Save instance to file.
    /// </summary>
    public bool Save(string path)
    {
        try
        {
            if (!path.EndsWith(".blueskymaterialinstance"))
                path += ".blueskymaterialinstance";
            
            var instanceAsset = new MaterialInstanceAsset
            {
                InstanceId = InstanceId,
                InstanceName = InstanceName,
                ParentMaterialId = ParentMaterialId,
                ParameterOverrides = _parameterOverrides
            };
            
            return instanceAsset.Save(path);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MaterialInstance] Failed to save: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Load instance from file.
    /// </summary>
    public static MaterialInstance? Load(string path)
    {
        try
        {
            var instanceAsset = MaterialInstanceAsset.Load(path);
            if (instanceAsset == null)
                return null;
            
            var instance = new MaterialInstance(instanceAsset.ParentMaterialId)
            {
                InstanceId = instanceAsset.InstanceId,
                InstanceName = instanceAsset.InstanceName
            };
            
            foreach (var kvp in instanceAsset.ParameterOverrides)
            {
                instance._parameterOverrides[kvp.Key] = kvp.Value;
            }
            
            return instance;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MaterialInstance] Failed to load: {ex.Message}");
            return null;
        }
    }
}

/// <summary>
/// Material instance asset format.
/// </summary>
public class MaterialInstanceAsset
{
    public Guid InstanceId { get; set; }
    public string InstanceName { get; set; }
    public Guid ParentMaterialId { get; set; }
    public Dictionary<string, object> ParameterOverrides { get; set; } = new();
    
    public bool Save(string path)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(this, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
            System.IO.File.WriteAllText(path, json);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MaterialInstanceAsset] Failed to save: {ex.Message}");
            return false;
        }
    }
    
    public static MaterialInstanceAsset? Load(string path)
    {
        try
        {
            var json = System.IO.File.ReadAllText(path);
            return System.Text.Json.JsonSerializer.Deserialize<MaterialInstanceAsset>(json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MaterialInstanceAsset] Failed to load: {ex.Message}");
            return null;
        }
    }
}
