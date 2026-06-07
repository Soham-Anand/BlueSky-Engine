// BlueSkyEngine - Sampler State Management
// Cross-platform sampler descriptors with automatic deduplication

using System;
using System.Collections.Generic;

namespace BlueSky.Rendering.Textures;

/// <summary>
/// Sampler state descriptor - defines how textures are sampled.
/// Immutable and hashable for efficient caching.
/// </summary>
public readonly struct SamplerState : IEquatable<SamplerState>
{
    // Filtering
    public readonly FilterMode MinFilter;
    public readonly FilterMode MagFilter;
    public readonly MipMapMode MipFilter;
    
    // Addressing
    public readonly AddressMode AddressU;
    public readonly AddressMode AddressV;
    public readonly AddressMode AddressW;
    
    // Anisotropy
    public readonly uint MaxAnisotropy; // 1, 2, 4, 8, 16
    
    // LOD control
    public readonly float MipLodBias;
    public readonly float MinLod;
    public readonly float MaxLod;
    
    // Comparison (for shadow maps)
    public readonly CompareFunction CompareFunction;
    public readonly bool CompareEnabled;
    
    // Border color (for AddressMode.ClampToBorder)
    public readonly BorderColor BorderColor;
    
    public SamplerState(
        FilterMode minFilter = FilterMode.Linear,
        FilterMode magFilter = FilterMode.Linear,
        MipMapMode mipFilter = MipMapMode.Linear,
        AddressMode addressU = AddressMode.Repeat,
        AddressMode addressV = AddressMode.Repeat,
        AddressMode addressW = AddressMode.Repeat,
        uint maxAnisotropy = 1,
        float mipLodBias = 0.0f,
        float minLod = 0.0f,
        float maxLod = float.MaxValue,
        CompareFunction compareFunction = CompareFunction.Never,
        bool compareEnabled = false,
        BorderColor borderColor = BorderColor.TransparentBlack)
    {
        MinFilter = minFilter;
        MagFilter = magFilter;
        MipFilter = mipFilter;
        AddressU = addressU;
        AddressV = addressV;
        AddressW = addressW;
        MaxAnisotropy = Math.Clamp(maxAnisotropy, 1u, 16u);
        MipLodBias = mipLodBias;
        MinLod = minLod;
        MaxLod = maxLod;
        CompareFunction = compareFunction;
        CompareEnabled = compareEnabled;
        BorderColor = borderColor;
    }
    
    // ═══════════════════════════════════════════════════════════════
    //  COMMON PRESETS
    // ═══════════════════════════════════════════════════════════════
    
    /// <summary>Point filtering, repeat addressing (pixel art).</summary>
    public static SamplerState PointRepeat => new(
        FilterMode.Nearest, FilterMode.Nearest, MipMapMode.Nearest,
        AddressMode.Repeat, AddressMode.Repeat, AddressMode.Repeat);
    
    /// <summary>Bilinear filtering, repeat addressing (default).</summary>
    public static SamplerState BilinearRepeat => new(
        FilterMode.Linear, FilterMode.Linear, MipMapMode.Nearest,
        AddressMode.Repeat, AddressMode.Repeat, AddressMode.Repeat);
    
    /// <summary>Trilinear filtering, repeat addressing (smooth).</summary>
    public static SamplerState TrilinearRepeat => new(
        FilterMode.Linear, FilterMode.Linear, MipMapMode.Linear,
        AddressMode.Repeat, AddressMode.Repeat, AddressMode.Repeat);
    
    /// <summary>Anisotropic 4x filtering, repeat addressing (quality).</summary>
    public static SamplerState Anisotropic4xRepeat => new(
        FilterMode.Linear, FilterMode.Linear, MipMapMode.Linear,
        AddressMode.Repeat, AddressMode.Repeat, AddressMode.Repeat,
        maxAnisotropy: 4);
    
    /// <summary>Anisotropic 8x filtering, repeat addressing (high quality).</summary>
    public static SamplerState Anisotropic8xRepeat => new(
        FilterMode.Linear, FilterMode.Linear, MipMapMode.Linear,
        AddressMode.Repeat, AddressMode.Repeat, AddressMode.Repeat,
        maxAnisotropy: 8);
    
    /// <summary>Anisotropic 16x filtering, repeat addressing (ultra quality).</summary>
    public static SamplerState Anisotropic16xRepeat => new(
        FilterMode.Linear, FilterMode.Linear, MipMapMode.Linear,
        AddressMode.Repeat, AddressMode.Repeat, AddressMode.Repeat,
        maxAnisotropy: 16);
    
    /// <summary>Trilinear filtering, clamp addressing (UI, skybox).</summary>
    public static SamplerState TrilinearClamp => new(
        FilterMode.Linear, FilterMode.Linear, MipMapMode.Linear,
        AddressMode.ClampToEdge, AddressMode.ClampToEdge, AddressMode.ClampToEdge);
    
    /// <summary>Shadow map comparison sampler (PCF).</summary>
    public static SamplerState ShadowPCF => new(
        FilterMode.Linear, FilterMode.Linear, MipMapMode.Nearest,
        AddressMode.ClampToEdge, AddressMode.ClampToEdge, AddressMode.ClampToEdge,
        compareFunction: CompareFunction.LessEqual,
        compareEnabled: true);
    
    // ═══════════════════════════════════════════════════════════════
    //  EQUALITY & HASHING
    // ═══════════════════════════════════════════════════════════════
    
    public bool Equals(SamplerState other)
    {
        return MinFilter == other.MinFilter &&
               MagFilter == other.MagFilter &&
               MipFilter == other.MipFilter &&
               AddressU == other.AddressU &&
               AddressV == other.AddressV &&
               AddressW == other.AddressW &&
               MaxAnisotropy == other.MaxAnisotropy &&
               MipLodBias.Equals(other.MipLodBias) &&
               MinLod.Equals(other.MinLod) &&
               MaxLod.Equals(other.MaxLod) &&
               CompareFunction == other.CompareFunction &&
               CompareEnabled == other.CompareEnabled &&
               BorderColor == other.BorderColor;
    }
    
    public override bool Equals(object? obj) => obj is SamplerState other && Equals(other);
    
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(MinFilter);
        hash.Add(MagFilter);
        hash.Add(MipFilter);
        hash.Add(AddressU);
        hash.Add(AddressV);
        hash.Add(AddressW);
        hash.Add(MaxAnisotropy);
        hash.Add(MipLodBias);
        hash.Add(MinLod);
        hash.Add(MaxLod);
        hash.Add(CompareFunction);
        hash.Add(CompareEnabled);
        hash.Add(BorderColor);
        return hash.ToHashCode();
    }
    
    public static bool operator ==(SamplerState left, SamplerState right) => left.Equals(right);
    public static bool operator !=(SamplerState left, SamplerState right) => !left.Equals(right);
}

/// <summary>
/// Texture filtering mode.
/// </summary>
public enum FilterMode
{
    Nearest,  // Point sampling (no filtering)
    Linear    // Bilinear/trilinear filtering
}

/// <summary>
/// Mipmap filtering mode.
/// </summary>
public enum MipMapMode
{
    Nearest,  // No mip blending
    Linear    // Trilinear filtering
}

/// <summary>
/// Texture address mode (wrapping).
/// </summary>
public enum AddressMode
{
    Repeat,         // Wrap/tile texture
    MirrorRepeat,   // Mirror on each repeat
    ClampToEdge,    // Clamp to edge pixels
    ClampToBorder,  // Clamp to border color
    MirrorClampToEdge // Mirror once, then clamp
}

/// <summary>
/// Comparison function for shadow sampling.
/// </summary>
public enum CompareFunction
{
    Never,
    Less,
    Equal,
    LessEqual,
    Greater,
    NotEqual,
    GreaterEqual,
    Always
}

/// <summary>
/// Border color for ClampToBorder addressing.
/// </summary>
public enum BorderColor
{
    TransparentBlack, // (0, 0, 0, 0)
    OpaqueBlack,      // (0, 0, 0, 1)
    OpaqueWhite       // (1, 1, 1, 1)
}

/// <summary>
/// Sampler cache - deduplicates sampler objects across the engine.
/// Thread-safe singleton.
/// </summary>
public sealed class SamplerCache
{
    private static readonly Lazy<SamplerCache> _instance = new(() => new SamplerCache());
    public static SamplerCache Instance => _instance.Value;
    
    private readonly Dictionary<SamplerState, CachedSampler> _cache = new();
    private readonly object _lock = new();
    private uint _nextId = 1;
    
    private SamplerCache() { }
    
    /// <summary>
    /// Get or create a sampler for the given state.
    /// Returns a cached sampler ID that can be used across frames.
    /// </summary>
    public uint GetOrCreate(SamplerState state, NotBSRenderer.IRHIDevice device)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(state, out var cached))
            {
                cached.RefCount++;
                return cached.Id;
            }
            
            // Create new sampler (platform-specific)
            var sampler = CreatePlatformSampler(state, device);
            
            var entry = new CachedSampler
            {
                Id = _nextId++,
                State = state,
                PlatformSampler = sampler,
                RefCount = 1
            };
            
            _cache[state] = entry;
            return entry.Id;
        }
    }
    
    /// <summary>
    /// Release a sampler reference. Destroys sampler when ref count reaches 0.
    /// </summary>
    public void Release(uint samplerId)
    {
        lock (_lock)
        {
            var entry = _cache.Values.FirstOrDefault(s => s.Id == samplerId);
            if (entry != null)
            {
                entry.RefCount--;
                if (entry.RefCount <= 0)
                {
                    if (entry.PlatformSampler is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                    _cache.Remove(entry.State);
                }
            }
        }
    }
    
    /// <summary>
    /// Get platform sampler object by ID.
    /// </summary>
    public object? GetPlatformSampler(uint samplerId)
    {
        lock (_lock)
        {
            var entry = _cache.Values.FirstOrDefault(s => s.Id == samplerId);
            return entry?.PlatformSampler;
        }
    }
    
    /// <summary>
    /// Clear all cached samplers (shutdown).
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            foreach (var entry in _cache.Values)
            {
                if (entry.PlatformSampler is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            _cache.Clear();
        }
    }
    
    private object? CreatePlatformSampler(SamplerState state, NotBSRenderer.IRHIDevice device)
    {
        // TODO: Create platform-specific sampler
        // For now, return null (will be implemented when RHI supports samplers)
        return null;
    }
    
    private class CachedSampler
    {
        public uint Id;
        public SamplerState State;
        public object? PlatformSampler;
        public int RefCount;
    }
}
