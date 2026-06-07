// BlueSkyEngine - Shader Cache
// Disk-based cache for compiled shader bytecode

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using BlueSky.Core.Diagnostics;

namespace BlueSky.Rendering.Materials;

/// <summary>
/// Shader cache - stores compiled shader bytecode on disk.
/// Avoids recompilation on subsequent runs.
/// </summary>
public sealed class ShaderCache
{
    private static readonly Lazy<ShaderCache> _instance = new(() => new ShaderCache());
    public static ShaderCache Instance => _instance.Value;
    
    private readonly string _cacheDir;
    private readonly Dictionary<string, CachedShader> _memoryCache = new();
    private readonly object _lock = new();
    
    private ShaderCache()
    {
        _cacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ShaderCache");
        Directory.CreateDirectory(_cacheDir);
        ErrorHandler.LogInfo($"Shader cache directory: {_cacheDir}", "ShaderCache");
    }
    
    /// <summary>
    /// Try to get cached shader bytecode.
    /// </summary>
    public bool TryGet(string shaderSource, string platform, out byte[]? bytecode)
    {
        bytecode = null;
        string hash = ComputeHash(shaderSource, platform);
        
        lock (_lock)
        {
            // Check memory cache first
            if (_memoryCache.TryGetValue(hash, out var cached))
            {
                bytecode = cached.Bytecode;
                cached.HitCount++;
                return true;
            }
            
            // Check disk cache
            string cachePath = GetCachePath(hash);
            if (File.Exists(cachePath))
            {
                try
                {
                    bytecode = File.ReadAllBytes(cachePath);
                    
                    // Add to memory cache
                    _memoryCache[hash] = new CachedShader
                    {
                        Hash = hash,
                        Bytecode = bytecode,
                        HitCount = 1
                    };
                    
                    ErrorHandler.LogInfo($"Shader cache hit: {hash}", "ShaderCache");
                    return true;
                }
                catch (Exception ex)
                {
                    ErrorHandler.LogError($"Failed to read shader cache: {cachePath}", ex, "ShaderCache");
                }
            }
        }
        
        return false;
    }
    
    /// <summary>
    /// Store compiled shader bytecode in cache.
    /// </summary>
    public void Store(string shaderSource, string platform, byte[] bytecode)
    {
        string hash = ComputeHash(shaderSource, platform);
        
        lock (_lock)
        {
            // Store in memory cache
            _memoryCache[hash] = new CachedShader
            {
                Hash = hash,
                Bytecode = bytecode,
                HitCount = 0
            };
            
            // Store on disk
            string cachePath = GetCachePath(hash);
            try
            {
                File.WriteAllBytes(cachePath, bytecode);
                ErrorHandler.LogInfo($"Shader cached: {hash}", "ShaderCache");
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError($"Failed to write shader cache: {cachePath}", ex, "ShaderCache");
            }
        }
    }
    
    /// <summary>
    /// Clear all cached shaders.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _memoryCache.Clear();
            
            try
            {
                if (Directory.Exists(_cacheDir))
                {
                    Directory.Delete(_cacheDir, recursive: true);
                    Directory.CreateDirectory(_cacheDir);
                }
                ErrorHandler.LogInfo("Shader cache cleared", "ShaderCache");
            }
            catch (Exception ex)
            {
                ErrorHandler.LogError("Failed to clear shader cache", ex, "ShaderCache");
            }
        }
    }
    
    /// <summary>
    /// Get cache statistics.
    /// </summary>
    public CacheStats GetStats()
    {
        lock (_lock)
        {
            long diskSize = 0;
            int diskCount = 0;
            
            if (Directory.Exists(_cacheDir))
            {
                var files = Directory.GetFiles(_cacheDir, "*.bin");
                diskCount = files.Length;
                foreach (var file in files)
                {
                    diskSize += new FileInfo(file).Length;
                }
            }
            
            return new CacheStats
            {
                MemoryCacheCount = _memoryCache.Count,
                DiskCacheCount = diskCount,
                DiskCacheSizeBytes = diskSize,
                TotalHits = _memoryCache.Values.Sum(c => c.HitCount)
            };
        }
    }
    
    private string ComputeHash(string shaderSource, string platform)
    {
        string combined = $"{platform}:{shaderSource}";
        byte[] bytes = Encoding.UTF8.GetBytes(combined);
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
    
    private string GetCachePath(string hash)
    {
        return Path.Combine(_cacheDir, $"{hash}.bin");
    }
    
    private class CachedShader
    {
        public string Hash = "";
        public byte[] Bytecode = Array.Empty<byte>();
        public int HitCount;
    }
}

/// <summary>
/// Cache statistics.
/// </summary>
public struct CacheStats
{
    public int MemoryCacheCount;
    public int DiskCacheCount;
    public long DiskCacheSizeBytes;
    public long TotalHits;
    
    public string DiskCacheSizeMB => $"{DiskCacheSizeBytes / (1024.0 * 1024.0):F2} MB";
}
