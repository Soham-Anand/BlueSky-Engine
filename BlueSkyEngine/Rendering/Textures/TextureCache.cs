// BlueSkyEngine - Texture Cache
// LRU cache with VRAM tracking and automatic eviction

using System;
using System.Collections.Generic;
using System.Linq;
using NotBSRenderer;
using RHITextureFormat = NotBSRenderer.TextureFormat;

namespace BlueSky.Rendering.Textures;

/// <summary>
/// LRU texture cache with VRAM tracking.
/// Thread-safe.
/// </summary>
internal class TextureCache
{
    private readonly Dictionary<string, CachedTexture> _cache = new();
    private readonly object _lock = new();
    
    // Statistics
    private long _totalHits;
    private long _totalMisses;
    
    public int Count
    {
        get { lock (_lock) return _cache.Count; }
    }
    
    public long TotalMemoryUsage
    {
        get { lock (_lock) return _cache.Values.Sum(t => t.MemoryUsage); }
    }
    
    public float HitRate
    {
        get
        {
            long total = _totalHits + _totalMisses;
            return total > 0 ? (float)_totalHits / total : 0f;
        }
    }
    
    /// <summary>
    /// Try to get texture from cache.
    /// </summary>
    public bool TryGet(string path, out CachedTexture texture)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(path, out texture!))
            {
                texture.LastAccessTime = DateTime.UtcNow;
                texture.AccessCount++;
                _totalHits++;
                return true;
            }
            
            _totalMisses++;
            return false;
        }
    }
    
    /// <summary>
    /// Add texture to cache.
    /// </summary>
    public void Add(string path, IRHITexture texture, TexturePriority priority)
    {
        lock (_lock)
        {
            if (_cache.ContainsKey(path))
            {
                // Already cached, just update priority
                _cache[path].Priority = priority;
                return;
            }
            
            var cached = new CachedTexture
            {
                Path = path,
                Texture = texture,
                Priority = priority,
                MemoryUsage = EstimateTextureMemory(texture),
                LastAccessTime = DateTime.UtcNow,
                AccessCount = 1,
                RefCount = 1
            };
            
            _cache[path] = cached;
        }
    }
    
    /// <summary>
    /// Release texture reference (decrement ref count).
    /// </summary>
    public void Release(string path)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(path, out var cached))
            {
                cached.RefCount--;
                if (cached.RefCount <= 0)
                {
                    cached.Texture.Dispose();
                    _cache.Remove(path);
                }
            }
        }
    }
    
    /// <summary>
    /// Evict low-priority textures to free memory.
    /// </summary>
    public void EvictLowPriority(long targetBytes)
    {
        lock (_lock)
        {
            // Sort by priority (low first), then by last access time (oldest first)
            var candidates = _cache.Values
                .Where(t => t.RefCount <= 1) // Don't evict actively used textures
                .OrderBy(t => t.Priority)
                .ThenBy(t => t.LastAccessTime)
                .ToList();
            
            long freedBytes = 0;
            foreach (var texture in candidates)
            {
                if (freedBytes >= targetBytes) break;
                
                freedBytes += texture.MemoryUsage;
                texture.Texture.Dispose();
                _cache.Remove(texture.Path);
            }
        }
    }
    
    /// <summary>
    /// Clear all cached textures.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            foreach (var texture in _cache.Values)
            {
                texture.Texture.Dispose();
            }
            _cache.Clear();
        }
    }
    
    private long EstimateTextureMemory(IRHITexture texture)
    {
        uint width = texture.Width;
        uint height = texture.Height;
        
        // Accurate bytes per pixel based on format
        uint bytesPerPixel = texture.Format switch
        {
            RHITextureFormat.R8Unorm => 1,
            RHITextureFormat.RGBA8Unorm => 4,
            RHITextureFormat.RGBA8Srgb => 4,
            RHITextureFormat.BGRA8Unorm => 4,
            RHITextureFormat.BGRA8Srgb => 4,
            RHITextureFormat.R32Float => 4,
            RHITextureFormat.RG32Float => 8,
            RHITextureFormat.RGB32Float => 12,
            RHITextureFormat.RGBA16Float => 8,
            RHITextureFormat.RGBA32Float => 16,
            RHITextureFormat.Depth32Float => 4,
            RHITextureFormat.Depth24Stencil8 => 4,
            RHITextureFormat.BC1 => 0, // Block compressed formats need special handling
            RHITextureFormat.BC3 => 0,
            RHITextureFormat.BC7 => 0,
            _ => 4
        };
        
        long totalBytes = 0;
        
        if (bytesPerPixel > 0)
        {
            // Uncompressed formats
            // Assuming 1 mip level for now if IRHITexture doesn't expose it
            totalBytes = width * height * bytesPerPixel;
        }
        else
        {
            // Compressed formats
            // BC1 uses 8 bytes per 4x4 block
            // BC3 and BC7 use 16 bytes per 4x4 block
            uint blockCountX = (width + 3) / 4;
            uint blockCountY = (height + 3) / 4;
            uint bytesPerBlock = texture.Format == RHITextureFormat.BC1 ? 8u : 16u;
            
            totalBytes = blockCountX * blockCountY * bytesPerBlock;
        }
        
        // Add roughly 33% if mips are assumed to be generated
        return (long)(totalBytes * 1.33);
    }
}

/// <summary>
/// Cached texture entry.
/// </summary>
internal class CachedTexture
{
    public string Path = "";
    public IRHITexture Texture = null!;
    public TexturePriority Priority;
    public long MemoryUsage;
    public DateTime LastAccessTime;
    public int AccessCount;
    public int RefCount;
}
