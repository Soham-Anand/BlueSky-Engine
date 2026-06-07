// BlueSkyEngine - Material Instance V2
// Per-instance material parameters with efficient GPU updates

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;

namespace BlueSky.Rendering.Materials;

/// <summary>
/// Material instance - overrides base material parameters per-object.
/// Efficient for rendering many objects with slight variations.
/// </summary>
public class MaterialInstanceV2
{
    private readonly MaterialAssetV2 _baseMaterial;
    private readonly Dictionary<string, object> _overrides = new();
    
    public Guid InstanceId { get; } = Guid.NewGuid();
    public MaterialAssetV2 BaseMaterial => _baseMaterial;
    
    // Cached parameter block for GPU upload
    private MaterialParameterBlock _parameterBlock;
    private bool _parametersDirty = true;
    
    public MaterialInstanceV2(MaterialAssetV2 baseMaterial)
    {
        _baseMaterial = baseMaterial ?? throw new ArgumentNullException(nameof(baseMaterial));
    }
    
    // ═══════════════════════════════════════════════════════════════
    //  PARAMETER OVERRIDES
    // ═══════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Set color parameter override.
    /// </summary>
    public void SetColor(string name, Vector3 value)
    {
        _overrides[name] = value;
        _parametersDirty = true;
    }
    
    /// <summary>
    /// Set float parameter override.
    /// </summary>
    public void SetFloat(string name, float value)
    {
        _overrides[name] = value;
        _parametersDirty = true;
    }
    
    /// <summary>
    /// Set vector parameter override.
    /// </summary>
    public void SetVector(string name, Vector4 value)
    {
        _overrides[name] = value;
        _parametersDirty = true;
    }
    
    /// <summary>
    /// Set texture override (small textures only, like detail maps).
    /// </summary>
    public void SetTexture(string name, string path)
    {
        _overrides[name] = path;
        _parametersDirty = true;
    }
    
    /// <summary>
    /// Get parameter value (with fallback to base material).
    /// </summary>
    public T GetParameter<T>(string name, T defaultValue)
    {
        if (_overrides.TryGetValue(name, out var value) && value is T typedValue)
        {
            return typedValue;
        }
        return defaultValue;
    }
    
    /// <summary>
    /// Clear all overrides.
    /// </summary>
    public void ClearOverrides()
    {
        _overrides.Clear();
        _parametersDirty = true;
    }
    
    // ═══════════════════════════════════════════════════════════════
    //  GPU PARAMETER BLOCK
    // ═══════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Get GPU parameter block for rendering.
    /// </summary>
    public MaterialParameterBlock GetParameterBlock()
    {
        if (_parametersDirty)
        {
            _parameterBlock = BuildParameterBlock();
            _parametersDirty = false;
        }
        return _parameterBlock;
    }
    
    private MaterialParameterBlock BuildParameterBlock()
    {
        // Merge base material + overrides
        var block = new MaterialParameterBlock
        {
            Albedo = GetParameter("albedo", _baseMaterial.Albedo),
            Metallic = GetParameter("metallic", _baseMaterial.Metallic),
            Roughness = GetParameter("roughness", _baseMaterial.Roughness),
            NormalStrength = GetParameter("normalStrength", _baseMaterial.NormalStrength),
            AO = GetParameter("ao", _baseMaterial.AO),
            Emissive = GetParameter("emissive", _baseMaterial.Emissive),
            EmissiveIntensity = GetParameter("emissiveIntensity", _baseMaterial.EmissiveIntensity),
            Opacity = GetParameter("opacity", _baseMaterial.Opacity),
            Tiling = GetParameter("tiling", _baseMaterial.Tiling),
            Offset = GetParameter("offset", _baseMaterial.Offset),
            ParallaxScale = GetParameter("parallaxScale", _baseMaterial.ParallaxScale)
        };
        
        return block;
    }
    
    // ═══════════════════════════════════════════════════════════════
    //  CONVENIENCE PROPERTIES
    // ═══════════════════════════════════════════════════════════════
    
    public Vector3 Albedo
    {
        get => GetParameter("albedo", _baseMaterial.Albedo);
        set => SetColor("albedo", value);
    }
    
    public float Metallic
    {
        get => GetParameter("metallic", _baseMaterial.Metallic);
        set => SetFloat("metallic", value);
    }
    
    public float Roughness
    {
        get => GetParameter("roughness", _baseMaterial.Roughness);
        set => SetFloat("roughness", value);
    }
    
    public Vector3 Emissive
    {
        get => GetParameter("emissive", _baseMaterial.Emissive);
        set => SetColor("emissive", value);
    }
    
    public float Opacity
    {
        get => GetParameter("opacity", _baseMaterial.Opacity);
        set => SetFloat("opacity", value);
    }
}

/// <summary>
/// Material parameter block - GPU-friendly constant buffer layout.
/// Must be 16-byte aligned for GPU upload.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 16)]
public struct MaterialParameterBlock
{
    // PBR parameters (48 bytes)
    public Vector3 Albedo;           // 12 bytes
    public float Metallic;           // 4 bytes
    public float Roughness;          // 4 bytes
    public float NormalStrength;     // 4 bytes
    public float AO;                 // 4 bytes
    public float Opacity;            // 4 bytes
    
    // Emissive (16 bytes)
    public Vector3 Emissive;         // 12 bytes
    public float EmissiveIntensity;  // 4 bytes
    
    // UV transform (16 bytes)
    public Vector2 Tiling;           // 8 bytes
    public Vector2 Offset;           // 8 bytes
    
    // Advanced (16 bytes)
    public float ParallaxScale;      // 4 bytes
    public float _padding1;          // 4 bytes
    public float _padding2;          // 4 bytes
    public float _padding3;          // 4 bytes
    
    // Total: 96 bytes (6 × 16-byte blocks)
    
    /// <summary>
    /// Get byte array for GPU upload.
    /// </summary>
    public byte[] ToBytes()
    {
        int size = Marshal.SizeOf<MaterialParameterBlock>();
        byte[] bytes = new byte[size];
        IntPtr ptr = Marshal.AllocHGlobal(size);
        
        try
        {
            Marshal.StructureToPtr(this, ptr, false);
            Marshal.Copy(ptr, bytes, 0, size);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
        
        return bytes;
    }
}
