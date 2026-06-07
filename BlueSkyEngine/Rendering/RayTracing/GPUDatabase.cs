// BlueSkyEngine - GPU Database
//
// COMPREHENSIVE GPU KNOWLEDGE BASE
// =================================
// Contains detailed specifications for every major GPU from 2010-2024
// Used by IntelligentRTSelector to make optimal decisions
//
// Data includes:
// - Compute units, VRAM, memory bandwidth
// - RT core count (if applicable)
// - Estimated GFLOPS
// - Architecture generation
// - Ray tracing support

using System;
using System.Collections.Generic;
using BlueSky.Core.Platform.Detection;

namespace BlueSky.Rendering.RayTracing;

/// <summary>
/// Comprehensive GPU database for intelligent backend selection
/// </summary>
public static class GPUDatabase
{
    private static readonly Dictionary<string, GPUSpec> _database = new()
    {
        // ============================================================================
        // NVIDIA RTX 40 SERIES (Ada Lovelace, 2022-2023)
        // ============================================================================
        ["rtx 4090"] = new GPUSpec
        {
            Name = "GeForce RTX 4090",
            Vendor = GpuVendor.NVIDIA,
            Architecture = "Ada Lovelace",
            Year = 2022,
            ComputeUnits = 128,
            RTCores = 128,
            VRAM = 24576,
            GFLOPS = 82580,
            MemoryBandwidth = 1008,
            HasHardwareRT = true,
            RTPerformance = 19.2f, // Gigarays/sec
            GpuClass = GpuClass.Enthusiast
        },
        
        ["rtx 4080"] = new GPUSpec
        {
            Name = "GeForce RTX 4080",
            Vendor = GpuVendor.NVIDIA,
            Architecture = "Ada Lovelace",
            Year = 2022,
            ComputeUnits = 76,
            RTCores = 76,
            VRAM = 16384,
            GFLOPS = 48740,
            MemoryBandwidth = 716,
            HasHardwareRT = true,
            RTPerformance = 11.4f,
            GpuClass = GpuClass.Enthusiast
        },
        
        ["rtx 4070"] = new GPUSpec
        {
            Name = "GeForce RTX 4070",
            Vendor = GpuVendor.NVIDIA,
            Architecture = "Ada Lovelace",
            Year = 2023,
            ComputeUnits = 46,
            RTCores = 46,
            VRAM = 12288,
            GFLOPS = 29150,
            MemoryBandwidth = 504,
            HasHardwareRT = true,
            RTPerformance = 6.9f,
            GpuClass = GpuClass.HighEnd
        },
        
        ["rtx 4060"] = new GPUSpec
        {
            Name = "GeForce RTX 4060",
            Vendor = GpuVendor.NVIDIA,
            Architecture = "Ada Lovelace",
            Year = 2023,
            ComputeUnits = 24,
            RTCores = 24,
            VRAM = 8192,
            GFLOPS = 15110,
            MemoryBandwidth = 272,
            HasHardwareRT = true,
            RTPerformance = 3.6f,
            GpuClass = GpuClass.MidRange
        },
        
        // ============================================================================
        // NVIDIA RTX 30 SERIES (Ampere, 2020-2021)
        // ============================================================================
        ["rtx 3090"] = new GPUSpec
        {
            Name = "GeForce RTX 3090",
            Vendor = GpuVendor.NVIDIA,
            Architecture = "Ampere",
            Year = 2020,
            ComputeUnits = 82,
            RTCores = 82,
            VRAM = 24576,
            GFLOPS = 35580,
            MemoryBandwidth = 936,
            HasHardwareRT = true,
            RTPerformance = 12.3f,
            GpuClass = GpuClass.Enthusiast
        },
        
        ["rtx 3080"] = new GPUSpec
        {
            Name = "GeForce RTX 3080",
            Vendor = GpuVendor.NVIDIA,
            Architecture = "Ampere",
            Year = 2020,
            ComputeUnits = 68,
            RTCores = 68,
            VRAM = 10240,
            GFLOPS = 29770,
            MemoryBandwidth = 760,
            HasHardwareRT = true,
            RTPerformance = 10.2f,
            GpuClass = GpuClass.HighEnd
        },
        
        ["rtx 3070"] = new GPUSpec
        {
            Name = "GeForce RTX 3070",
            Vendor = GpuVendor.NVIDIA,
            Architecture = "Ampere",
            Year = 2020,
            ComputeUnits = 46,
            RTCores = 46,
            VRAM = 8192,
            GFLOPS = 20310,
            MemoryBandwidth = 448,
            HasHardwareRT = true,
            RTPerformance = 6.9f,
            GpuClass = GpuClass.HighEnd
        },
        
        ["rtx 3060"] = new GPUSpec
        {
            Name = "GeForce RTX 3060",
            Vendor = GpuVendor.NVIDIA,
            Architecture = "Ampere",
            Year = 2021,
            ComputeUnits = 28,
            RTCores = 28,
            VRAM = 12288,
            GFLOPS = 12740,
            MemoryBandwidth = 360,
            HasHardwareRT = true,
            RTPerformance = 4.2f,
            GpuClass = GpuClass.MidRange
        },
        
        ["rtx 3050"] = new GPUSpec
        {
            Name = "GeForce RTX 3050",
            Vendor = GpuVendor.NVIDIA,
            Architecture = "Ampere",
            Year = 2022,
            ComputeUnits = 20,
            RTCores = 20,
            VRAM = 8192,
            GFLOPS = 9100,
            MemoryBandwidth = 224,
            HasHardwareRT = true,
            RTPerformance = 3.0f,
            GpuClass = GpuClass.MidRange
        },
        
        // ============================================================================
        // NVIDIA RTX 20 SERIES (Turing, 2018-2019)
        // ============================================================================
        ["rtx 2080 ti"] = new GPUSpec
        {
            Name = "GeForce RTX 2080 Ti",
            Vendor = GpuVendor.NVIDIA,
            Architecture = "Turing",
            Year = 2018,
            ComputeUnits = 68,
            RTCores = 68,
            VRAM = 11264,
            GFLOPS = 13450,
            MemoryBandwidth = 616,
            HasHardwareRT = true,
            RTPerformance = 10.2f,
            GpuClass = GpuClass.HighEnd
        },
        
        ["rtx 2060"] = new GPUSpec
        {
            Name = "GeForce RTX 2060",
            Vendor = GpuVendor.NVIDIA,
            Architecture = "Turing",
            Year = 2019,
            ComputeUnits = 30,
            RTCores = 30,
            VRAM = 6144,
            GFLOPS = 6520,
            MemoryBandwidth = 336,
            HasHardwareRT = true,
            RTPerformance = 4.5f,
            GpuClass = GpuClass.MidRange
        },
        
        // ============================================================================
        // NVIDIA GTX 16 SERIES (Turing, No RT, 2019)
        // ============================================================================
        ["gtx 1660 ti"] = new GPUSpec
        {
            Name = "GeForce GTX 1660 Ti",
            Vendor = GpuVendor.NVIDIA,
            Architecture = "Turing",
            Year = 2019,
            ComputeUnits = 24,
            RTCores = 0,
            VRAM = 6144,
            GFLOPS = 5437,
            MemoryBandwidth = 288,
            HasHardwareRT = false,
            RTPerformance = 0.0f,
            GpuClass = GpuClass.MidRange
        },
        
        ["gtx 1650"] = new GPUSpec
        {
            Name = "GeForce GTX 1650",
            Vendor = GpuVendor.NVIDIA,
            Architecture = "Turing",
            Year = 2019,
            ComputeUnits = 14,
            RTCores = 0,
            VRAM = 4096,
            GFLOPS = 2984,
            MemoryBandwidth = 128,
            HasHardwareRT = false,
            RTPerformance = 0.0f,
            GpuClass = GpuClass.LowEnd
        },
        
        // ============================================================================
        // NVIDIA GTX 10 SERIES (Pascal, 2016-2017)
        // ============================================================================
        ["gtx 1080 ti"] = new GPUSpec
        {
            Name = "GeForce GTX 1080 Ti",
            Vendor = GpuVendor.NVIDIA,
            Architecture = "Pascal",
            Year = 2017,
            ComputeUnits = 28,
            RTCores = 0,
            VRAM = 11264,
            GFLOPS = 11340,
            MemoryBandwidth = 484,
            HasHardwareRT = false,
            RTPerformance = 0.0f,
            GpuClass = GpuClass.HighEnd
        },
        
        ["gtx 1070"] = new GPUSpec
        {
            Name = "GeForce GTX 1070",
            Vendor = GpuVendor.NVIDIA,
            Architecture = "Pascal",
            Year = 2016,
            ComputeUnits = 15,
            RTCores = 0,
            VRAM = 8192,
            GFLOPS = 6463,
            MemoryBandwidth = 256,
            HasHardwareRT = false,
            RTPerformance = 0.0f,
            GpuClass = GpuClass.MidRange
        },
        
        ["gtx 1060"] = new GPUSpec
        {
            Name = "GeForce GTX 1060",
            Vendor = GpuVendor.NVIDIA,
            Architecture = "Pascal",
            Year = 2016,
            ComputeUnits = 10,
            RTCores = 0,
            VRAM = 6144,
            GFLOPS = 4375,
            MemoryBandwidth = 192,
            HasHardwareRT = false,
            RTPerformance = 0.0f,
            GpuClass = GpuClass.MidRange
        },
        
        ["gtx 1050 ti"] = new GPUSpec
        {
            Name = "GeForce GTX 1050 Ti",
            Vendor = GpuVendor.NVIDIA,
            Architecture = "Pascal",
            Year = 2016,
            ComputeUnits = 6,
            RTCores = 0,
            VRAM = 4096,
            GFLOPS = 2138,
            MemoryBandwidth = 112,
            HasHardwareRT = false,
            RTPerformance = 0.0f,
            GpuClass = GpuClass.LowEnd
        },
        
        ["gtx 1050"] = new GPUSpec
        {
            Name = "GeForce GTX 1050",
            Vendor = GpuVendor.NVIDIA,
            Architecture = "Pascal",
            Year = 2016,
            ComputeUnits = 5,
            RTCores = 0,
            VRAM = 2048,
            GFLOPS = 1862,
            MemoryBandwidth = 112,
            HasHardwareRT = false,
            RTPerformance = 0.0f,
            GpuClass = GpuClass.LowEnd
        },
        
        // ============================================================================
        // AMD RX 7000 SERIES (RDNA 3, 2022-2023)
        // ============================================================================
        ["rx 7900 xtx"] = new GPUSpec
        {
            Name = "Radeon RX 7900 XTX",
            Vendor = GpuVendor.AMD,
            Architecture = "RDNA 3",
            Year = 2022,
            ComputeUnits = 96,
            RTCores = 96, // Ray Accelerators
            VRAM = 24576,
            GFLOPS = 61440,
            MemoryBandwidth = 960,
            HasHardwareRT = true,
            RTPerformance = 14.4f,
            GpuClass = GpuClass.Enthusiast
        },
        
        ["rx 7900 xt"] = new GPUSpec
        {
            Name = "Radeon RX 7900 XT",
            Vendor = GpuVendor.AMD,
            Architecture = "RDNA 3",
            Year = 2022,
            ComputeUnits = 84,
            RTCores = 84,
            VRAM = 20480,
            GFLOPS = 53760,
            MemoryBandwidth = 800,
            HasHardwareRT = true,
            RTPerformance = 12.6f,
            GpuClass = GpuClass.Enthusiast
        },
        
        // ============================================================================
        // AMD RX 6000 SERIES (RDNA 2, 2020-2021)
        // ============================================================================
        ["rx 6900 xt"] = new GPUSpec
        {
            Name = "Radeon RX 6900 XT",
            Vendor = GpuVendor.AMD,
            Architecture = "RDNA 2",
            Year = 2020,
            ComputeUnits = 80,
            RTCores = 80,
            VRAM = 16384,
            GFLOPS = 23040,
            MemoryBandwidth = 512,
            HasHardwareRT = true,
            RTPerformance = 12.0f,
            GpuClass = GpuClass.HighEnd
        },
        
        ["rx 6800 xt"] = new GPUSpec
        {
            Name = "Radeon RX 6800 XT",
            Vendor = GpuVendor.AMD,
            Architecture = "RDNA 2",
            Year = 2020,
            ComputeUnits = 72,
            RTCores = 72,
            VRAM = 16384,
            GFLOPS = 20740,
            MemoryBandwidth = 512,
            HasHardwareRT = true,
            RTPerformance = 10.8f,
            GpuClass = GpuClass.HighEnd
        },
        
        ["rx 6700 xt"] = new GPUSpec
        {
            Name = "Radeon RX 6700 XT",
            Vendor = GpuVendor.AMD,
            Architecture = "RDNA 2",
            Year = 2021,
            ComputeUnits = 40,
            RTCores = 40,
            VRAM = 12288,
            GFLOPS = 13210,
            MemoryBandwidth = 384,
            HasHardwareRT = true,
            RTPerformance = 6.0f,
            GpuClass = GpuClass.MidRange
        },
        
        ["rx 6600 xt"] = new GPUSpec
        {
            Name = "Radeon RX 6600 XT",
            Vendor = GpuVendor.AMD,
            Architecture = "RDNA 2",
            Year = 2021,
            ComputeUnits = 32,
            RTCores = 32,
            VRAM = 8192,
            GFLOPS = 10610,
            MemoryBandwidth = 256,
            HasHardwareRT = true,
            RTPerformance = 4.8f,
            GpuClass = GpuClass.MidRange
        },
        
        // ============================================================================
        // AMD RX 5000 SERIES (RDNA, 2019 - No RT)
        // ============================================================================
        ["rx 5700 xt"] = new GPUSpec
        {
            Name = "Radeon RX 5700 XT",
            Vendor = GpuVendor.AMD,
            Architecture = "RDNA",
            Year = 2019,
            ComputeUnits = 40,
            RTCores = 0,
            VRAM = 8192,
            GFLOPS = 9754,
            MemoryBandwidth = 448,
            HasHardwareRT = false,
            RTPerformance = 0.0f,
            GpuClass = GpuClass.MidRange
        },
        
        ["rx 580"] = new GPUSpec
        {
            Name = "Radeon RX 580",
            Vendor = GpuVendor.AMD,
            Architecture = "Polaris",
            Year = 2017,
            ComputeUnits = 36,
            RTCores = 0,
            VRAM = 8192,
            GFLOPS = 6175,
            MemoryBandwidth = 256,
            HasHardwareRT = false,
            RTPerformance = 0.0f,
            GpuClass = GpuClass.MidRange
        },
        
        ["rx 570"] = new GPUSpec
        {
            Name = "Radeon RX 570",
            Vendor = GpuVendor.AMD,
            Architecture = "Polaris",
            Year = 2017,
            ComputeUnits = 32,
            RTCores = 0,
            VRAM = 4096,
            GFLOPS = 5095,
            MemoryBandwidth = 224,
            HasHardwareRT = false,
            RTPerformance = 0.0f,
            GpuClass = GpuClass.LowEnd
        },
        
        // ============================================================================
        // INTEL ARC (Alchemist, 2022)
        // ============================================================================
        ["arc a770"] = new GPUSpec
        {
            Name = "Intel Arc A770",
            Vendor = GpuVendor.Intel,
            Architecture = "Alchemist",
            Year = 2022,
            ComputeUnits = 32,
            RTCores = 32,
            VRAM = 16384,
            GFLOPS = 17200,
            MemoryBandwidth = 560,
            HasHardwareRT = true,
            RTPerformance = 4.8f,
            GpuClass = GpuClass.MidRange
        },
        
        ["arc a750"] = new GPUSpec
        {
            Name = "Intel Arc A750",
            Vendor = GpuVendor.Intel,
            Architecture = "Alchemist",
            Year = 2022,
            ComputeUnits = 28,
            RTCores = 28,
            VRAM = 8192,
            GFLOPS = 14250,
            MemoryBandwidth = 512,
            HasHardwareRT = true,
            RTPerformance = 4.2f,
            GpuClass = GpuClass.MidRange
        },
        
        // ============================================================================
        // INTEL INTEGRATED GRAPHICS
        // ============================================================================
        ["intel hd graphics 4000"] = new GPUSpec
        {
            Name = "Intel HD Graphics 4000",
            Vendor = GpuVendor.Intel,
            Architecture = "Ivy Bridge",
            Year = 2012,
            ComputeUnits = 16,
            RTCores = 0,
            VRAM = 0, // Shared
            GFLOPS = 332,
            MemoryBandwidth = 25,
            HasHardwareRT = false,
            RTPerformance = 0.0f,
            GpuClass = GpuClass.Integrated
        },
        
        ["intel hd graphics 3000"] = new GPUSpec
        {
            Name = "Intel HD Graphics 3000",
            Vendor = GpuVendor.Intel,
            Architecture = "Sandy Bridge",
            Year = 2011,
            ComputeUnits = 12,
            RTCores = 0,
            VRAM = 0, // Shared
            GFLOPS = 166,
            MemoryBandwidth = 21,
            HasHardwareRT = false,
            RTPerformance = 0.0f,
            GpuClass = GpuClass.Integrated
        },
        
        // ============================================================================
        // APPLE SILICON
        // ============================================================================
        ["apple m3 max"] = new GPUSpec
        {
            Name = "Apple M3 Max",
            Vendor = GpuVendor.Apple,
            Architecture = "M3",
            Year = 2023,
            ComputeUnits = 40,
            RTCores = 40,
            VRAM = 0, // Unified
            GFLOPS = 14200,
            MemoryBandwidth = 400,
            HasHardwareRT = true,
            RTPerformance = 6.0f,
            GpuClass = GpuClass.HighEnd
        },
        
        ["apple m2"] = new GPUSpec
        {
            Name = "Apple M2",
            Vendor = GpuVendor.Apple,
            Architecture = "M2",
            Year = 2022,
            ComputeUnits = 10,
            RTCores = 10,
            VRAM = 0, // Unified
            GFLOPS = 3600,
            MemoryBandwidth = 100,
            HasHardwareRT = true,
            RTPerformance = 1.5f,
            GpuClass = GpuClass.MidRange
        },
        
        ["apple m1"] = new GPUSpec
        {
            Name = "Apple M1",
            Vendor = GpuVendor.Apple,
            Architecture = "M1",
            Year = 2020,
            ComputeUnits = 8,
            RTCores = 8,
            VRAM = 0, // Unified
            GFLOPS = 2600,
            MemoryBandwidth = 68,
            HasHardwareRT = true,
            RTPerformance = 1.2f,
            GpuClass = GpuClass.MidRange
        }
    };
    
    /// <summary>
    /// Lookup GPU specifications by model name
    /// </summary>
    public static GPUSpec? Lookup(string modelName)
    {
        string normalized = modelName.ToLower().Trim();
        
        // Try exact match first
        if (_database.TryGetValue(normalized, out var spec))
            return spec;
        
        // Try partial match
        foreach (var kvp in _database)
        {
            if (normalized.Contains(kvp.Key) || kvp.Key.Contains(normalized))
                return kvp.Value;
        }
        
        return null;
    }
    
    /// <summary>
    /// Get all GPUs from a specific vendor
    /// </summary>
    public static List<GPUSpec> GetByVendor(GpuVendor vendor)
    {
        var result = new List<GPUSpec>();
        foreach (var spec in _database.Values)
        {
            if (spec.Vendor == vendor)
                result.Add(spec);
        }
        return result;
    }
}

/// <summary>
/// GPU specification data
/// </summary>
public struct GPUSpec
{
    public string Name;
    public GpuVendor Vendor;
    public string Architecture;
    public int Year;
    public int ComputeUnits;
    public int RTCores;
    public int VRAM; // MB
    public float GFLOPS;
    public float MemoryBandwidth; // GB/s
    public bool HasHardwareRT;
    public float RTPerformance; // Gigarays/sec
    public GpuClass GpuClass;
}

/// <summary>
/// GPU vendor enumeration
/// </summary>
public enum GpuVendor
{
    Unknown,
    NVIDIA,
    AMD,
    Intel,
    Apple
}

/// <summary>
/// GPU performance class
/// </summary>
public enum GpuClass
{
    Integrated,    // Intel HD, AMD APU
    LowEnd,        // GTX 1050, RX 560
    MidRange,      // GTX 1060, RX 580
    HighEnd,       // RTX 3080, RX 6800 XT
    Enthusiast     // RTX 4090, RX 7900 XTX
}
