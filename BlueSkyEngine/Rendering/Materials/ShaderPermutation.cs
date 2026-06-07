// BlueSkyEngine - Shader Permutation System
// Auto-generates shader variants based on material features

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BlueSky.Core.Diagnostics;

namespace BlueSky.Rendering.Materials;

/// <summary>
/// Shader permutation manager - generates and caches shader variants.
/// Each material feature combination gets a unique shader variant.
/// </summary>
public sealed class ShaderPermutation
{
    private static readonly Lazy<ShaderPermutation> _instance = new(() => new ShaderPermutation());
    public static ShaderPermutation Instance => _instance.Value;
    
    private readonly Dictionary<PermutationKey, ShaderVariant> _variants = new();
    private readonly object _lock = new();
    
    private ShaderPermutation() { }
    
    /// <summary>
    /// Get or create shader variant for material features.
    /// </summary>
    public ShaderVariant GetVariant(string shaderName, MaterialFeatures features)
    {
        var key = new PermutationKey(shaderName, features);
        
        lock (_lock)
        {
            if (_variants.TryGetValue(key, out var variant))
            {
                variant.AccessCount++;
                return variant;
            }
            
            // Create new variant
            variant = CreateVariant(shaderName, features);
            _variants[key] = variant;
            
            ErrorHandler.LogInfo($"Created shader variant: {shaderName} [{features}]", "ShaderPermutation");
            return variant;
        }
    }
    
    /// <summary>
    /// Get all variants for a shader.
    /// </summary>
    public ShaderVariant[] GetAllVariants(string shaderName)
    {
        lock (_lock)
        {
            return _variants.Values
                .Where(v => v.ShaderName == shaderName)
                .ToArray();
        }
    }
    
    /// <summary>
    /// Clear all cached variants (hot reload).
    /// </summary>
    public void ClearCache()
    {
        lock (_lock)
        {
            foreach (var variant in _variants.Values)
            {
                variant.Dispose();
            }
            _variants.Clear();
            ErrorHandler.LogInfo("Shader variant cache cleared", "ShaderPermutation");
        }
    }
    
    /// <summary>
    /// Get statistics.
    /// </summary>
    public PermutationStats GetStats()
    {
        lock (_lock)
        {
            return new PermutationStats
            {
                TotalVariants = _variants.Count,
                TotalAccessCount = _variants.Values.Sum(v => v.AccessCount),
                MostUsedVariant = _variants.Values.OrderByDescending(v => v.AccessCount).FirstOrDefault()
            };
        }
    }
    
    private ShaderVariant CreateVariant(string shaderName, MaterialFeatures features)
    {
        // Generate shader defines from features
        var defines = GenerateDefines(features);
        
        // Generate unique variant ID
        string variantId = $"{shaderName}_{features:X}";
        
        return new ShaderVariant
        {
            ShaderName = shaderName,
            Features = features,
            Defines = defines,
            VariantId = variantId,
            AccessCount = 1
        };
    }
    
    private Dictionary<string, string> GenerateDefines(MaterialFeatures features)
    {
        var defines = new Dictionary<string, string>();
        
        if (features.HasFlag(MaterialFeatures.AlbedoMap)) defines["ALBEDO_MAP"] = "1";
        if (features.HasFlag(MaterialFeatures.NormalMap)) defines["NORMAL_MAP"] = "1";
        if (features.HasFlag(MaterialFeatures.RoughnessMap)) defines["ROUGHNESS_MAP"] = "1";
        if (features.HasFlag(MaterialFeatures.MetallicMap)) defines["METALLIC_MAP"] = "1";
        if (features.HasFlag(MaterialFeatures.AOMap)) defines["AO_MAP"] = "1";
        if (features.HasFlag(MaterialFeatures.EmissiveMap)) defines["EMISSIVE_MAP"] = "1";
        if (features.HasFlag(MaterialFeatures.HeightMap)) defines["HEIGHT_MAP"] = "1";
        if (features.HasFlag(MaterialFeatures.PackedRMA)) defines["PACKED_RMA"] = "1";
        if (features.HasFlag(MaterialFeatures.VertexColors)) defines["VERTEX_COLORS"] = "1";
        if (features.HasFlag(MaterialFeatures.AlphaTest)) defines["ALPHA_TEST"] = "1";
        if (features.HasFlag(MaterialFeatures.AlphaBlend)) defines["ALPHA_BLEND"] = "1";
        if (features.HasFlag(MaterialFeatures.DoubleSided)) defines["DOUBLE_SIDED"] = "1";
        if (features.HasFlag(MaterialFeatures.Parallax)) defines["PARALLAX"] = "1";
        if (features.HasFlag(MaterialFeatures.Emissive)) defines["EMISSIVE"] = "1";
        
        return defines;
    }
}

/// <summary>
/// Shader variant - compiled shader with specific feature set.
/// </summary>
public class ShaderVariant : IDisposable
{
    public string ShaderName = "";
    public MaterialFeatures Features;
    public Dictionary<string, string> Defines = new();
    public string VariantId = "";
    public int AccessCount;
    
    // Platform-specific compiled shader (will be set by shader compiler)
    public object? CompiledShader;
    
    /// <summary>
    /// Get shader source with defines injected.
    /// </summary>
    public string GetShaderSource(string baseSource)
    {
        var sb = new StringBuilder();
        
        // Inject defines at top
        foreach (var (key, value) in Defines)
        {
            sb.AppendLine($"#define {key} {value}");
        }
        
        sb.AppendLine();
        sb.Append(baseSource);
        
        return sb.ToString();
    }
    
    public void Dispose()
    {
        // Dispose platform-specific shader
        if (CompiledShader is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}

/// <summary>
/// Permutation key - uniquely identifies a shader variant.
/// </summary>
internal readonly struct PermutationKey : IEquatable<PermutationKey>
{
    private readonly string _shaderName;
    private readonly MaterialFeatures _features;
    
    public PermutationKey(string shaderName, MaterialFeatures features)
    {
        _shaderName = shaderName;
        _features = features;
    }
    
    public bool Equals(PermutationKey other)
    {
        return _shaderName == other._shaderName && _features == other._features;
    }
    
    public override bool Equals(object? obj) => obj is PermutationKey other && Equals(other);
    
    public override int GetHashCode() => HashCode.Combine(_shaderName, _features);
}

/// <summary>
/// Permutation statistics.
/// </summary>
public struct PermutationStats
{
    public int TotalVariants;
    public long TotalAccessCount;
    public ShaderVariant? MostUsedVariant;
    
    public float AverageAccessCount => TotalVariants > 0 ? (float)TotalAccessCount / TotalVariants : 0f;
}
