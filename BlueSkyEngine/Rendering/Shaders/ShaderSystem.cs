using System;
using System.Collections.Generic;
using System.IO;

namespace BlueSky.Rendering.Shaders;

/// <summary>
/// Centralized shader management system with permutation support and hot reload.
/// Optimized for old hardware with shader variant caching.
/// </summary>
public class ShaderSystem
{
    private readonly Dictionary<string, Shader> _shaders = new();
    private readonly Dictionary<string, ShaderVariant> _variants = new();
    private readonly string _shaderDirectory;
    
    public ShaderSystem(string shaderDirectory)
    {
        _shaderDirectory = shaderDirectory;
    }
    
    /// <summary>
    /// Load or get a shader by name.
    /// </summary>
    public Shader GetShader(string name)
    {
        if (_shaders.TryGetValue(name, out var shader))
            return shader;
        
        shader = LoadShader(name);
        _shaders[name] = shader;
        return shader;
    }
    
    /// <summary>
    /// Get a shader variant with specific permutation flags.
    /// </summary>
    public ShaderVariant GetVariant(string baseShader, ShaderPermutation permutation)
    {
        string variantKey = $"{baseShader}_{permutation.GetKey()}";
        
        if (_variants.TryGetValue(variantKey, out var variant))
            return variant;
        
        // Create variant
        var baseShaderObj = GetShader(baseShader);
        variant = new ShaderVariant(baseShaderObj, permutation);
        _variants[variantKey] = variant;
        
        return variant;
    }
    
    /// <summary>
    /// Reload all shaders (hot reload).
    /// </summary>
    public void ReloadAll()
    {
        foreach (var kvp in _shaders)
        {
            ReloadShader(kvp.Key);
        }
        
        // Clear variants cache
        _variants.Clear();
    }
    
    /// <summary>
    /// Reload a specific shader.
    /// </summary>
    public void ReloadShader(string name)
    {
        if (_shaders.TryGetValue(name, out var shader))
        {
            var reloaded = LoadShader(name);
            if (reloaded != null)
            {
                _shaders[name] = reloaded;
            }
        }
    }
    
    private Shader LoadShader(string name)
    {
        // In a real implementation, this would load from disk
        // For now, return a placeholder
        return new Shader(name);
    }
    
    /// <summary>
    /// Check if shader file has been modified (for hot reload).
    /// </summary>
    public bool IsShaderModified(string name, DateTime lastModified)
    {
        var path = Path.Combine(_shaderDirectory, $"{name}.metal");
        if (!File.Exists(path))
            return false;
        
        return File.GetLastWriteTime(path) > lastModified;
    }
}

/// <summary>
/// Base shader class.
/// </summary>
public class Shader
{
    public string Name { get; set; }
    public DateTime LastModified { get; set; }
    public ShaderStage Stage { get; set; }
    
    public Shader(string name)
    {
        Name = name;
        LastModified = DateTime.Now;
    }
}

/// <summary>
/// Shader variant with permutation flags.
/// </summary>
public class ShaderVariant
{
    public Shader BaseShader { get; set; }
    public ShaderPermutation Permutation { get; set; }
    public string CompiledKey { get; set; }
    
    public ShaderVariant(Shader baseShader, ShaderPermutation permutation)
    {
        BaseShader = baseShader;
        Permutation = permutation;
        CompiledKey = $"{baseShader.Name}_{permutation.GetKey()}";
    }
}

/// <summary>
/// Shader permutation flags for creating shader variants.
/// </summary>
[Flags]
public enum ShaderPermutation : uint
{
    None = 0,
    WithAlbedoTexture = 1 << 0,
    WithNormalTexture = 1 << 1,
    WithRoughnessTexture = 1 << 2,
    WithMetallicTexture = 1 << 3,
    WithEmissionTexture = 1 << 4,
    WithAOTexture = 1 << 5,
    WithParallax = 1 << 6,
    WithDetailMap = 1 << 7,
    WithSkinning = 1 << 8,
    WithMorphTargets = 1 << 9,
    Transparent = 1 << 10,
    DoubleSided = 1 << 11,
    SimplifiedLighting = 1 << 12,
    UseRMA = 1 << 13, // Roughness/Metallic/AO packed texture
    LowQuality = 1 << 14,
    
    // Common presets
    Standard = WithAlbedoTexture | WithNormalTexture | UseRMA,
    LowEnd = WithAlbedoTexture | SimplifiedLighting | LowQuality,
    HighEnd = WithAlbedoTexture | WithNormalTexture | WithParallax | WithDetailMap | UseRMA
}

/// <summary>
/// Extension methods for ShaderPermutation.
/// </summary>
public static class ShaderPermutationExtensions
{
    public static string GetKey(this ShaderPermutation permutation)
    {
        return ((uint)permutation).ToString("X");
    }
}

public enum ShaderStage
{
    Vertex,
    Fragment,
    Geometry,
    Compute,
    Hull,
    Domain
}
