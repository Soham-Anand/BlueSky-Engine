using System.Runtime;
using BlueSky.Core.Diagnostics;

namespace BlueSky.Core.Memory;

/// <summary>
/// Advanced memory management system for optimal performance.
/// Tracks allocations, prevents leaks, and optimizes GC.
/// </summary>
public static class MemoryManager
{
    private static long _totalAllocated = 0;
    private static long _totalFreed = 0;
    private static readonly Dictionary<string, MemoryStats> _stats = new();
    private static readonly object _lock = new();

    public static long TotalAllocated => _totalAllocated;
    public static long TotalFreed => _totalFreed;
    public static long CurrentUsage => _totalAllocated - _totalFreed;

    /// <summary>
    /// Track an allocation.
    /// </summary>
    public static void TrackAllocation(string category, long bytes)
    {
        lock (_lock)
        {
            _totalAllocated += bytes;
            
            if (!_stats.ContainsKey(category))
            {
                _stats[category] = new MemoryStats { Category = category };
            }
            
            _stats[category].Allocated += bytes;
            _stats[category].CurrentUsage += bytes;
            _stats[category].AllocationCount++;
        }
    }

    /// <summary>
    /// Track a deallocation.
    /// </summary>
    public static void TrackDeallocation(string category, long bytes)
    {
        lock (_lock)
        {
            _totalFreed += bytes;
            
            if (_stats.ContainsKey(category))
            {
                _stats[category].Freed += bytes;
                _stats[category].CurrentUsage -= bytes;
                _stats[category].DeallocationCount++;
            }
        }
    }

    /// <summary>
    /// Force garbage collection (use sparingly!).
    /// </summary>
    public static void ForceGC(bool aggressive = false)
    {
        var before = GC.GetTotalMemory(false);
        
        if (aggressive)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
        }
        else
        {
            GC.Collect();
        }
        
        var after = GC.GetTotalMemory(true);
        var freed = before - after;
        
        ErrorHandler.LogInfo($"GC freed {FormatBytes(freed)} (before: {FormatBytes(before)}, after: {FormatBytes(after)})", "MemoryManager");
    }

    /// <summary>
    /// Configure GC for low-latency (gaming) mode.
    /// </summary>
    public static void SetLowLatencyMode(bool enabled)
    {
        if (enabled)
        {
            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
            ErrorHandler.LogInfo("GC set to SustainedLowLatency mode", "MemoryManager");
        }
        else
        {
            GCSettings.LatencyMode = GCLatencyMode.Interactive;
            ErrorHandler.LogInfo("GC set to Interactive mode", "MemoryManager");
        }
    }

    /// <summary>
    /// Get memory statistics.
    /// </summary>
    public static MemoryReport GetReport()
    {
        lock (_lock)
        {
            return new MemoryReport
            {
                TotalAllocated = _totalAllocated,
                TotalFreed = _totalFreed,
                CurrentUsage = CurrentUsage,
                ManagedMemory = GC.GetTotalMemory(false),
                Gen0Collections = GC.CollectionCount(0),
                Gen1Collections = GC.CollectionCount(1),
                Gen2Collections = GC.CollectionCount(2),
                CategoryStats = _stats.Values.ToList()
            };
        }
    }

    /// <summary>
    /// Print memory report to console.
    /// </summary>
    public static void PrintReport()
    {
        var report = GetReport();
        
        Console.WriteLine("\n=== Memory Report ===");
        Console.WriteLine($"Total Allocated: {FormatBytes(report.TotalAllocated)}");
        Console.WriteLine($"Total Freed: {FormatBytes(report.TotalFreed)}");
        Console.WriteLine($"Current Usage: {FormatBytes(report.CurrentUsage)}");
        Console.WriteLine($"Managed Memory: {FormatBytes(report.ManagedMemory)}");
        Console.WriteLine($"GC Collections: Gen0={report.Gen0Collections}, Gen1={report.Gen1Collections}, Gen2={report.Gen2Collections}");
        
        if (report.CategoryStats.Count > 0)
        {
            Console.WriteLine("\nMemory by Category:");
            foreach (var stat in report.CategoryStats.OrderByDescending(s => s.CurrentUsage))
            {
                Console.WriteLine($"  {stat.Category}: {FormatBytes(stat.CurrentUsage)} ({stat.AllocationCount} allocs, {stat.DeallocationCount} frees)");
            }
        }
    }

    /// <summary>
    /// Check for memory leaks (allocations without deallocations).
    /// </summary>
    public static List<string> DetectLeaks()
    {
        lock (_lock)
        {
            var leaks = new List<string>();
            
            foreach (var stat in _stats.Values)
            {
                if (stat.AllocationCount > stat.DeallocationCount + 10) // Allow some tolerance
                {
                    var leaked = stat.AllocationCount - stat.DeallocationCount;
                    leaks.Add($"{stat.Category}: {leaked} leaked allocations ({FormatBytes(stat.CurrentUsage)})");
                }
            }
            
            return leaks;
        }
    }

    /// <summary>
    /// Reset all statistics.
    /// </summary>
    public static void Reset()
    {
        lock (_lock)
        {
            _totalAllocated = 0;
            _totalFreed = 0;
            _stats.Clear();
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        
        return $"{len:0.##} {sizes[order]}";
    }
}

public class MemoryStats
{
    public string Category { get; set; } = "";
    public long Allocated { get; set; }
    public long Freed { get; set; }
    public long CurrentUsage { get; set; }
    public int AllocationCount { get; set; }
    public int DeallocationCount { get; set; }
}

public class MemoryReport
{
    public long TotalAllocated { get; set; }
    public long TotalFreed { get; set; }
    public long CurrentUsage { get; set; }
    public long ManagedMemory { get; set; }
    public int Gen0Collections { get; set; }
    public int Gen1Collections { get; set; }
    public int Gen2Collections { get; set; }
    public List<MemoryStats> CategoryStats { get; set; } = new();
}
