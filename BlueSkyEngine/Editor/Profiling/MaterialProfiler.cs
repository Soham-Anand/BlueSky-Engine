// BlueSkyEngine - Material Profiler
// GPU timing and VRAM usage analysis for materials

using System;
using System.Collections.Generic;
using System.Linq;
using BlueSky.Rendering.Materials;
using BlueSky.Rendering.Textures;

namespace BlueSky.Editor.Profiling;

/// <summary>
/// Material profiler - tracks GPU timing and VRAM usage per material.
/// </summary>
public class MaterialProfiler
{
    private readonly Dictionary<Guid, MaterialStats> _stats = new();
    private readonly object _lock = new();
    
    /// <summary>
    /// Record draw call for material.
    /// </summary>
    public void RecordDrawCall(Guid materialId, float gpuTimeMs, int triangleCount)
    {
        lock (_lock)
        {
            if (!_stats.ContainsKey(materialId))
            {
                _stats[materialId] = new MaterialStats { MaterialId = materialId };
            }
            
            var stats = _stats[materialId];
            stats.DrawCallCount++;
            stats.TotalGPUTimeMs += gpuTimeMs;
            stats.TotalTriangles += triangleCount;
        }
    }
    
    /// <summary>
    /// Record texture memory usage for material.
    /// </summary>
    public void RecordTextureMemory(Guid materialId, long bytes)
    {
        lock (_lock)
        {
            if (!_stats.ContainsKey(materialId))
            {
                _stats[materialId] = new MaterialStats { MaterialId = materialId };
            }
            
            _stats[materialId].TextureMemoryBytes = bytes;
        }
    }
    
    /// <summary>
    /// Get statistics for material.
    /// </summary>
    public MaterialStats? GetStats(Guid materialId)
    {
        lock (_lock)
        {
            return _stats.TryGetValue(materialId, out var stats) ? stats : null;
        }
    }
    
    /// <summary>
    /// Get all material statistics.
    /// </summary>
    public MaterialStats[] GetAllStats()
    {
        lock (_lock)
        {
            return _stats.Values.ToArray();
        }
    }
    
    /// <summary>
    /// Get top N most expensive materials by GPU time.
    /// </summary>
    public MaterialStats[] GetTopByGPUTime(int count = 10)
    {
        lock (_lock)
        {
            return _stats.Values
                .OrderByDescending(s => s.TotalGPUTimeMs)
                .Take(count)
                .ToArray();
        }
    }
    
    /// <summary>
    /// Get top N materials by VRAM usage.
    /// </summary>
    public MaterialStats[] GetTopByVRAM(int count = 10)
    {
        lock (_lock)
        {
            return _stats.Values
                .OrderByDescending(s => s.TextureMemoryBytes)
                .Take(count)
                .ToArray();
        }
    }
    
    /// <summary>
    /// Reset all statistics.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _stats.Clear();
        }
    }
    
    /// <summary>
    /// Get profiler summary.
    /// </summary>
    public ProfilerSummary GetSummary()
    {
        lock (_lock)
        {
            return new ProfilerSummary
            {
                TotalMaterials = _stats.Count,
                TotalDrawCalls = _stats.Values.Sum(s => s.DrawCallCount),
                TotalGPUTimeMs = _stats.Values.Sum(s => s.TotalGPUTimeMs),
                TotalVRAMBytes = _stats.Values.Sum(s => s.TextureMemoryBytes),
                TotalTriangles = _stats.Values.Sum(s => s.TotalTriangles)
            };
        }
    }
}

/// <summary>
/// Material statistics.
/// </summary>
public class MaterialStats
{
    public Guid MaterialId;
    public int DrawCallCount;
    public float TotalGPUTimeMs;
    public long TextureMemoryBytes;
    public long TotalTriangles;
    
    public float AverageGPUTimeMs => DrawCallCount > 0 ? TotalGPUTimeMs / DrawCallCount : 0f;
    public string TextureMemoryMB => $"{TextureMemoryBytes / (1024.0 * 1024.0):F2} MB";
}

/// <summary>
/// Profiler summary.
/// </summary>
public struct ProfilerSummary
{
    public int TotalMaterials;
    public long TotalDrawCalls;
    public float TotalGPUTimeMs;
    public long TotalVRAMBytes;
    public long TotalTriangles;
    
    public string TotalVRAMMB => $"{TotalVRAMBytes / (1024.0 * 1024.0):F2} MB";
    public float AverageGPUTimeMs => TotalDrawCalls > 0 ? TotalGPUTimeMs / TotalDrawCalls : 0f;
}
