using System;
using System.IO;
using System.Runtime.InteropServices;
using BlueSky.Aegis.Detection;
using BlueSky.Aegis.Detection.Probes;
using DetectionOSPlatform = BlueSky.Aegis.Detection.OSPlatform;

namespace BlueSky.Tests;

/// <summary>
/// Comprehensive test suite for the BlueSky Hardware &amp; Platform Detection subsystem
/// (PlatformDetector, IntelligentQueryLayer, MacMetalProbe, MacOSProbe, WindowsProbe, LinuxProbe, GpuClassifier, ProcessorCapabilities).
/// </summary>
public static class PlatformDetectionTests
{
    public static bool RunAllTests()
    {
        Console.WriteLine("\n[TEST SUITE] Running Platform & Hardware Detection Tests...");
        bool passed = true;

        passed &= TestProcessorCapabilities();
        passed &= TestIntelligentQueryLayerAndLiveGpu();
        passed &= TestMacSystemProfilerParser();
        passed &= TestWindowsCsvParserAndOverflow();
        passed &= TestLinuxSysfsAndPciIdsParser();
        passed &= TestGpuClassifierMatrix();

        return passed;
    }

    private static bool TestProcessorCapabilities()
    {
        try
        {
            var cpu = PlatformDetector.DetectCpu();
            bool validArch = !string.IsNullOrWhiteSpace(cpu.Architecture);
            bool validCores = cpu.LogicalCoreCount >= 1 &&
                              cpu.PhysicalCoreCount >= 1 &&
                              cpu.PerformanceCoreCount >= 1 &&
                              cpu.EfficiencyCoreCount >= 0 &&
                              cpu.PhysicalCoreCount >= cpu.PerformanceCoreCount &&
                              cpu.RecommendedWorkerThreadCount >= 1;
            bool validCacheLine = cpu.CacheLineSize == 64 || cpu.CacheLineSize == 128;
            bool validRam = cpu.TotalSystemMemoryMB >= 512 && cpu.UsableSystemMemoryMB > 0;
            bool validSimd = cpu.OptimalSimdWidthFloats >= 1;

            if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
            {
                validSimd &= cpu.SupportsArmSimd && cpu.SupportsArmSimdArm64 && cpu.OptimalSimdWidthFloats >= 4;
                if (OperatingSystem.IsMacOS())
                {
                    validCacheLine &= cpu.CacheLineSize == 128;
                    validArch &= !string.IsNullOrWhiteSpace(cpu.BrandString) && cpu.BrandString != "Unknown CPU";
                }
            }

            bool ok = validArch && validCores && validCacheLine && validRam && validSimd;
            Console.WriteLine($"  {(ok ? "✓" : "✗")} CPU Topology & SIMD ({cpu.BrandString}, {cpu.PhysicalCoreCount}P/{cpu.LogicalCoreCount}L [Perf={cpu.PerformanceCoreCount}, Eff={cpu.EfficiencyCoreCount}], CacheLine={cpu.CacheLineSize}B, RAM={cpu.TotalSystemMemoryMB}MB, SIMD={cpu.OptimalSimdWidthFloats}x float): {(ok ? "PASSED" : "FAILED")}");
            return ok;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ CPU Topology & SIMD Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestIntelligentQueryLayerAndLiveGpu()
    {
        try
        {
            var iql = PlatformDetector.Detect(forceRefresh: true);
            var gpu = iql.Gpu;

            bool ok = !string.IsNullOrWhiteSpace(gpu.Name) &&
                      gpu.Name != "Unknown GPU" &&
                      !string.IsNullOrWhiteSpace(gpu.Vendor) &&
                      gpu.Vendor != "Unknown" &&
                      gpu.VramMB > 0 &&
                      iql.SupportsCompute &&
                      iql.SupportsIndirectDrawing;

            if (OperatingSystem.IsMacOS())
            {
                ok &= gpu.SupportsMetal && iql.SupportsMetal && iql.MetalCaps.HasValue;
                if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
                {
                    var m = iql.MetalCaps!.Value;
                    ok &= PlatformDetector.IsAppleSilicon &&
                          gpu.HasUnifiedMemory &&
                          m.HasUnifiedMemory &&
                          m.RecommendedMaxWorkingSetMB > 0 &&
                          m.GpuCoreCount > 0 &&
                          iql.MetalFamily >= MetalGpuFamily.Apple7 &&
                          iql.SupportsMetal3 &&
                          iql.SupportsMeshShaders &&
                          gpu.Tier == GpuTier.High;

                    // M3 / M4 must report Apple9+ and hardware ray tracing
                    if (gpu.Name.Contains("M3", StringComparison.OrdinalIgnoreCase) ||
                        gpu.Name.Contains("M4", StringComparison.OrdinalIgnoreCase))
                    {
                        ok &= iql.MetalFamily >= MetalGpuFamily.Apple9 &&
                              iql.SupportsRayTracing &&
                              m.SupportsDynamicCaching;
                    }
                }
            }

            Console.WriteLine($"  {(ok ? "✓" : "✗")} Live GPU & IQL Probe ({gpu.Name}, {gpu.VramMB}MB, Cores={gpu.GpuCoreCount}, Family={iql.MetalFamily}, RT={iql.SupportsRayTracing}, Mesh={iql.SupportsMeshShaders}, Method={gpu.DetectionMethod}): {(ok ? "PASSED" : "FAILED")}");
            return ok;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ Live GPU & IQL Probe Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestMacSystemProfilerParser()
    {
        try
        {
            // 1. Apple M4 on macOS Sequoia/Tahoe ("Metal: Supported", no VRAM line, 8 GPU cores)
            const string m4Fixture = @"
Graphics/Displays:

    Apple M4:

      Chipset Model: Apple M4
      Type: GPU
      Bus: Built-In
      Total Number of Cores: 8
      Vendor: Apple (0x106b)
      Metal: Supported
";
            var m4Caps = new GpuCapabilities { OS = DetectionOSPlatform.MacOS, SystemMemoryMB = 16384 };
            bool p1 = MacOSProbe.ParseSystemProfilerOutput(m4Fixture, ref m4Caps) &&
                      m4Caps.Name == "Apple M4" &&
                      m4Caps.Vendor == "Apple" &&
                      m4Caps.GpuCoreCount == 8 &&
                      m4Caps.SupportsMetal &&
                      m4Caps.SupportsMetal3 &&
                      m4Caps.HasUnifiedMemory &&
                      m4Caps.VramMB == 12288 &&
                      m4Caps.Tier == GpuTier.High &&
                      !m4Caps.IsIntegrated;

            // 2. Dual-GPU Intel MacBook Pro (Intel UHD 630 + AMD Radeon Pro 5500M 8 GB)
            const string intelAmdFixture = @"
Graphics/Displays:

    Intel UHD Graphics 630:

      Chipset Model: Intel UHD Graphics 630
      Type: GPU
      Bus: Built-In
      VRAM (Dynamic, Max): 1536 MB
      Vendor: Intel
      Metal Family: Supported, Metal GPUFamily macOS 2

    AMD Radeon Pro 5500M:

      Chipset Model: AMD Radeon Pro 5500M
      Type: GPU
      Bus: PCIe
      VRAM (Total): 8 GB
      Vendor: AMD (0x1002)
      Metal Support: Metal 3
";
            var amdCaps = new GpuCapabilities { OS = DetectionOSPlatform.MacOS, SystemMemoryMB = 32768 };
            bool p2 = MacOSProbe.ParseSystemProfilerOutput(intelAmdFixture, ref amdCaps) &&
                      amdCaps.Name == "AMD Radeon Pro 5500M" &&
                      amdCaps.Vendor == "AMD" &&
                      amdCaps.VramMB == 8192 &&
                      amdCaps.SupportsMetal &&
                      amdCaps.Tier == GpuTier.High &&
                      !amdCaps.IsIntegrated;

            bool ok = p1 && p2;
            Console.WriteLine($"  {(ok ? "✓" : "✗")} macOS system_profiler Multi-Version Parser (Apple M4 + Dual-GPU Intel/AMD): {(ok ? "PASSED" : "FAILED")}");
            return ok;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ macOS system_profiler Parser Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestWindowsCsvParserAndOverflow()
    {
        try
        {
            // Hybrid laptop with Microsoft Basic Render Driver + Intel UHD + NVIDIA RTX 4080 where WMI AdapterRAM wrapped to 4293918720 (4095 MB)
            const string wmiCsv = @"
Node,AdapterRAM,DriverVersion,Name
DESKTOP-PC,0,10.0.22621.1,Microsoft Basic Render Driver
DESKTOP-PC,1073741824,31.0.101.5186,Intel(R) UHD Graphics
DESKTOP-PC,4293918720,32.0.15.6094,NVIDIA GeForce RTX 4080
";
            var winCaps = new GpuCapabilities { OS = DetectionOSPlatform.Windows };
            bool parsed = WindowsProbe.ParseCsvOutput(wmiCsv, ref winCaps);
            winCaps.IsIntegrated = GpuClassifier.IsLikelyIntegrated(winCaps.Vendor, winCaps.Name);
            winCaps.Tier = GpuClassifier.ClassifyTier(winCaps.Vendor, winCaps.Name, winCaps.VramMB);

            bool ok = parsed &&
                      winCaps.Name == "NVIDIA GeForce RTX 4080" &&
                      winCaps.Vendor == "NVIDIA" &&
                      !winCaps.IsIntegrated &&
                      winCaps.Tier == GpuTier.High;

            Console.WriteLine($"  {(ok ? "✓" : "✗")} Windows Hybrid GPU Selection & 4GB WMI Wrap Resilience: {(ok ? "PASSED" : "FAILED")}");
            return ok;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ Windows CSV Parser Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestLinuxSysfsAndPciIdsParser()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "bluesky_drm_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            string pciIdsFile = Path.Combine(tempDir, "pci.ids");
            File.WriteAllText(pciIdsFile,
                "# Sample PCI IDs\n" +
                "1002  Advanced Micro Devices, Inc. [AMD/ATI]\n" +
                "\t15bf  Phoenix1 [Radeon 780M]\n" +
                "\t744c  Navi 31 [Radeon RX 7900 XTX]\n" +
                "10de  NVIDIA Corporation\n" +
                "\t2684  AD102 [GeForce RTX 4090]\n");

            bool lookup1 = LinuxProbe.TryLookupPciIdsDatabase("0x10de", "0x2684", out string? nvModel, pciIdsFile) &&
                           nvModel == "GeForce RTX 4090";
            bool lookup2 = LinuxProbe.TryLookupPciIdsDatabase("0x1002", "0x15bf", out string? amdApuModel, pciIdsFile) &&
                           amdApuModel == "Radeon 780M";

            // Simulate an AMD APU sysfs tree with 512MB dedicated carveout + 8GB GTT shared aperture
            string card0Dev = Path.Combine(tempDir, "card0", "device");
            Directory.CreateDirectory(card0Dev);
            File.WriteAllText(Path.Combine(card0Dev, "vendor"), "0x1002\n");
            File.WriteAllText(Path.Combine(card0Dev, "device"), "0x15bf\n");
            File.WriteAllText(Path.Combine(card0Dev, "mem_info_vram_total"), (512L * 1024 * 1024).ToString());
            File.WriteAllText(Path.Combine(card0Dev, "mem_info_gtt_total"), (8192L * 1024 * 1024).ToString());

            var linuxCaps = new GpuCapabilities { OS = DetectionOSPlatform.Linux };
            bool sysfsOk = LinuxProbe.ProbeDrmSysfs(ref linuxCaps, tempDir) &&
                           linuxCaps.Vendor == "AMD" &&
                           linuxCaps.VramMB == 8192 &&
                           linuxCaps.HasUnifiedMemory;

            bool cacheParseOk = ProcessorCapabilities.ParseCacheSizeBytes("128K") == 131072 &&
                                ProcessorCapabilities.ParseCacheSizeBytes("16M") == 16777216;

            bool ok = lookup1 && lookup2 && sysfsOk && cacheParseOk;
            Console.WriteLine($"  {(ok ? "✓" : "✗")} Linux DRM sysfs, GTT Unified Memory & pci.ids Resolver: {(ok ? "PASSED" : "FAILED")}");
            return ok;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ Linux sysfs & pci.ids Test Exception: {ex.Message}");
            return false;
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    private static bool TestGpuClassifierMatrix()
    {
        bool ok = true;
        ok &= GpuClassifier.ClassifyTier("Apple", "Apple M4", 0) == GpuTier.High;
        ok &= !GpuClassifier.IsLikelyIntegrated("Apple", "Apple M4");
        ok &= GpuClassifier.ClassifyTier("NVIDIA", "NVIDIA GeForce RTX 3060", 4095) == GpuTier.High;
        ok &= GpuClassifier.ClassifyTier("AMD", "AMD Radeon RX 7800 XT", 16384) == GpuTier.High;
        ok &= GpuClassifier.ClassifyTier("Intel", "Intel(R) Arc(TM) A770 Graphics", 16384) == GpuTier.High;
        ok &= GpuClassifier.ClassifyTier("Intel", "Intel(R) Arc(TM) B580 Graphics", 12288) == GpuTier.High;
        ok &= GpuClassifier.ClassifyTier("AMD", "AMD Radeon 780M Graphics", 2048) == GpuTier.Mid;
        ok &= GpuClassifier.IsLikelyIntegrated("AMD", "AMD Radeon 780M Graphics");
        ok &= GpuClassifier.ClassifyTier("Intel", "Intel(R) UHD Graphics 630", 1536) == GpuTier.Low;
        ok &= GpuClassifier.IsLikelyIntegrated("Intel", "Intel(R) UHD Graphics 630");

        Console.WriteLine($"  {(ok ? "✓" : "✗")} GPU Tier & Integrated Classifier Matrix: {(ok ? "PASSED" : "FAILED")}");
        return ok;
    }
}
