using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace BlueSky.Aegis.Detection;

/// <summary>
/// Comprehensive processor, SIMD instruction set, core topology, cache, and system memory detector.
/// Supports macOS (native sysctlbyname), Windows (native GetLogicalProcessorInformationEx + GlobalMemoryStatusEx + Registry),
/// and Linux (sysfs topology/cache/capacity + /proc/cpuinfo + /proc/meminfo).
/// </summary>
public readonly struct ProcessorCapabilities
{
    public ProcessorCapabilities(
        string architecture,
        bool supportsAvx,
        bool supportsSse,
        bool supportsArmSimd,
        string brandString = "Unknown CPU",
        int logicalCoreCount = 1,
        int physicalCoreCount = 1,
        int performanceCoreCount = 1,
        int efficiencyCoreCount = 0,
        int cacheLineSize = 64,
        long l1DataCacheBytes = 0,
        long l2CacheBytes = 0,
        long l3CacheBytes = 0,
        long totalSystemMemoryBytes = 0,
        long usableSystemMemoryBytes = 0,
        bool supportsSse2 = false,
        bool supportsSse3 = false,
        bool supportsSsse3 = false,
        bool supportsSse41 = false,
        bool supportsSse42 = false,
        bool supportsAvx2 = false,
        bool supportsFma = false,
        bool supportsAvx512F = false,
        bool supportsBmi1 = false,
        bool supportsBmi2 = false,
        bool supportsPopcnt = false,
        bool supportsLzcnt = false,
        bool supportsX86Aes = false,
        bool supportsArmSimdArm64 = false,
        bool supportsArmDp = false,
        bool supportsArmRdm = false,
        bool supportsArmCrc32 = false,
        bool supportsArmAes = false,
        bool supportsArmSha1 = false,
        bool supportsArmSha256 = false)
    {
        Architecture = architecture;
        SupportsAvx = supportsAvx;
        SupportsSse = supportsSse;
        SupportsArmSimd = supportsArmSimd;

        BrandString = brandString;
        LogicalCoreCount = System.Math.Max(1, logicalCoreCount);
        PhysicalCoreCount = System.Math.Max(1, physicalCoreCount);
        PerformanceCoreCount = System.Math.Max(1, performanceCoreCount);
        EfficiencyCoreCount = System.Math.Max(0, efficiencyCoreCount);
        CacheLineSize = cacheLineSize > 0 ? cacheLineSize : 64;
        L1DataCacheBytes = l1DataCacheBytes;
        L2CacheBytes = l2CacheBytes;
        L3CacheBytes = l3CacheBytes;
        TotalSystemMemoryBytes = totalSystemMemoryBytes;
        UsableSystemMemoryBytes = usableSystemMemoryBytes > 0 ? usableSystemMemoryBytes : totalSystemMemoryBytes;

        SupportsSse2 = supportsSse2;
        SupportsSse3 = supportsSse3;
        SupportsSsse3 = supportsSsse3;
        SupportsSse41 = supportsSse41;
        SupportsSse42 = supportsSse42;
        SupportsAvx2 = supportsAvx2;
        SupportsFma = supportsFma;
        SupportsAvx512F = supportsAvx512F;
        SupportsBmi1 = supportsBmi1;
        SupportsBmi2 = supportsBmi2;
        SupportsPopcnt = supportsPopcnt;
        SupportsLzcnt = supportsLzcnt;
        SupportsX86Aes = supportsX86Aes;

        SupportsArmSimdArm64 = supportsArmSimdArm64;
        SupportsArmDp = supportsArmDp;
        SupportsArmRdm = supportsArmRdm;
        SupportsArmCrc32 = supportsArmCrc32;
        SupportsArmAes = supportsArmAes;
        SupportsArmSha1 = supportsArmSha1;
        SupportsArmSha256 = supportsArmSha256;
    }

    // ── Core Properties (backward-compatible) ──
    public string Architecture { get; }
    public bool SupportsAvx { get; }
    public bool SupportsSse { get; }
    public bool SupportsArmSimd { get; }

    // ── Topology, Cache & Memory ──
    public string BrandString { get; }
    public int LogicalCoreCount { get; }
    public int PhysicalCoreCount { get; }
    public int PerformanceCoreCount { get; }
    public int EfficiencyCoreCount { get; }
    public bool IsHybridArchitecture => EfficiencyCoreCount > 0;
    public int CacheLineSize { get; }
    public long L1DataCacheBytes { get; }
    public long L2CacheBytes { get; }
    public long L3CacheBytes { get; }
    public long TotalSystemMemoryBytes { get; }
    public long UsableSystemMemoryBytes { get; }
    public int TotalSystemMemoryMB => (int)(TotalSystemMemoryBytes / (1024 * 1024));
    public int UsableSystemMemoryMB => (int)(UsableSystemMemoryBytes / (1024 * 1024));

    // ── x86/x64 Intrinsics ──
    public bool SupportsSse2 { get; }
    public bool SupportsSse3 { get; }
    public bool SupportsSsse3 { get; }
    public bool SupportsSse41 { get; }
    public bool SupportsSse42 { get; }
    public bool SupportsAvx2 { get; }
    public bool SupportsFma { get; }
    public bool SupportsAvx512F { get; }
    public bool SupportsBmi1 { get; }
    public bool SupportsBmi2 { get; }
    public bool SupportsPopcnt { get; }
    public bool SupportsLzcnt { get; }
    public bool SupportsX86Aes { get; }

    // ── ARM64 Intrinsics ──
    public bool SupportsArmSimdArm64 { get; }
    public bool SupportsArmDp { get; }
    public bool SupportsArmRdm { get; }
    public bool SupportsArmCrc32 { get; }
    public bool SupportsArmAes { get; }
    public bool SupportsArmSha1 { get; }
    public bool SupportsArmSha256 { get; }

    // ── Cross-Platform Vector Width Capabilities ──
    public bool Vector128HardwareAccelerated => Vector128.IsHardwareAccelerated;
    public bool Vector256HardwareAccelerated => Vector256.IsHardwareAccelerated;
    public bool Vector512HardwareAccelerated => Vector512.IsHardwareAccelerated;

    /// <summary>
    /// Optimal float SIMD lane count on this CPU (16 for AVX-512, 8 for AVX/AVX2, 4 for ARM64 NEON / SSE, 1 scalar).
    /// </summary>
    public int OptimalSimdWidthFloats =>
        SupportsAvx512F ? 16 :
        SupportsAvx ? 8 :
        (SupportsArmSimd || SupportsSse) ? 4 : 1;

    /// <summary>
    /// Recommended worker thread count for high-throughput engine jobs (prefers P-cores + half of E-cores on hybrid architectures).
    /// </summary>
    public int RecommendedWorkerThreadCount =>
        IsHybridArchitecture
            ? System.Math.Max(1, PerformanceCoreCount + (EfficiencyCoreCount / 2))
            : System.Math.Max(1, LogicalCoreCount - 1);

    public static ProcessorCapabilities Probe()
    {
        int logicalCores = Environment.ProcessorCount;
        int physicalCores = logicalCores;
        int pCores = logicalCores;
        int eCores = 0;
        int cacheLineSize = RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? 128 : 64;
        long l1dBytes = 0;
        long l2Bytes = 0;
        long l3Bytes = 0;
        long totalRamBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        long usableRamBytes = totalRamBytes;
        string brand = "Unknown CPU";

        if (OperatingSystem.IsMacOS())
        {
            ProbeMacOSSysctl(
                ref brand,
                ref logicalCores,
                ref physicalCores,
                ref pCores,
                ref eCores,
                ref cacheLineSize,
                ref l1dBytes,
                ref l2Bytes,
                ref l3Bytes,
                ref totalRamBytes,
                ref usableRamBytes);
        }
        else if (OperatingSystem.IsLinux())
        {
            ProbeLinuxProcAndSysfs(
                ref brand,
                ref logicalCores,
                ref physicalCores,
                ref pCores,
                ref eCores,
                ref cacheLineSize,
                ref l1dBytes,
                ref l2Bytes,
                ref l3Bytes,
                ref totalRamBytes,
                ref usableRamBytes);
        }
        else if (OperatingSystem.IsWindows())
        {
            ProbeWindowsNative(
                ref brand,
                ref logicalCores,
                ref physicalCores,
                ref pCores,
                ref eCores,
                ref cacheLineSize,
                ref l1dBytes,
                ref l2Bytes,
                ref l3Bytes,
                ref totalRamBytes,
                ref usableRamBytes);
        }

        return new ProcessorCapabilities(
            architecture: RuntimeInformation.ProcessArchitecture.ToString(),
            supportsAvx: Avx.IsSupported,
            supportsSse: Sse.IsSupported,
            supportsArmSimd: AdvSimd.IsSupported,
            brandString: brand,
            logicalCoreCount: logicalCores,
            physicalCoreCount: physicalCores,
            performanceCoreCount: pCores,
            efficiencyCoreCount: eCores,
            cacheLineSize: cacheLineSize,
            l1DataCacheBytes: l1dBytes,
            l2CacheBytes: l2Bytes,
            l3CacheBytes: l3Bytes,
            totalSystemMemoryBytes: totalRamBytes,
            usableSystemMemoryBytes: usableRamBytes,
            supportsSse2: Sse2.IsSupported,
            supportsSse3: Sse3.IsSupported,
            supportsSsse3: Ssse3.IsSupported,
            supportsSse41: Sse41.IsSupported,
            supportsSse42: Sse42.IsSupported,
            supportsAvx2: Avx2.IsSupported,
            supportsFma: Fma.IsSupported,
            supportsAvx512F: Avx512F.IsSupported,
            supportsBmi1: Bmi1.IsSupported,
            supportsBmi2: Bmi2.IsSupported,
            supportsPopcnt: Popcnt.IsSupported,
            supportsLzcnt: Lzcnt.IsSupported,
            supportsX86Aes: System.Runtime.Intrinsics.X86.Aes.IsSupported,
            supportsArmSimdArm64: AdvSimd.Arm64.IsSupported,
            supportsArmDp: Dp.IsSupported,
            supportsArmRdm: Rdm.IsSupported,
            supportsArmCrc32: Crc32.IsSupported,
            supportsArmAes: System.Runtime.Intrinsics.Arm.Aes.IsSupported,
            supportsArmSha1: Sha1.IsSupported,
            supportsArmSha256: Sha256.IsSupported);
    }

    public void LogRayTracingSummary()
    {
        Console.WriteLine($"  CPU Brand: {BrandString} ({Architecture})");
        Console.WriteLine($"  CPU Cores: {PhysicalCoreCount} physical / {LogicalCoreCount} logical (P={PerformanceCoreCount}, E={EfficiencyCoreCount}), CacheLine={CacheLineSize}B");
        Console.WriteLine($"  System Memory: {TotalSystemMemoryMB} MB (Usable: {UsableSystemMemoryMB} MB)");
        Console.WriteLine($"  CPU SIMD: AVX={(SupportsAvx ? "yes" : "no")}, AVX2={(SupportsAvx2 ? "yes" : "no")}, SSE2={(SupportsSse2 ? "yes" : "no")}, ARM NEON={(SupportsArmSimd ? "yes" : "no")} (Optimal={OptimalSimdWidthFloats}-wide float)");
        Console.WriteLine($"  AVX Ray Tracing: {(SupportsAvx ? "available" : "unavailable on this CPU/runtime (use ARM NEON / GPU RT)")}");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  macOS Native sysctlbyname P/Invoke (<0.05ms, zero process spawn)
    // ═══════════════════════════════════════════════════════════════════════════
    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "sysctlbyname")]
    private static extern int sysctlbyname(string name, IntPtr oldp, ref nuint oldlenp, IntPtr newp, nuint newlen);

    internal static bool TrySysctlInt64(string name, out long value)
    {
        value = 0;
        if (!OperatingSystem.IsMacOS()) return false;
        IntPtr buf = Marshal.AllocHGlobal(8);
        try
        {
            nuint len = 8;
            Marshal.WriteInt64(buf, 0);
            if (sysctlbyname(name, buf, ref len, IntPtr.Zero, 0) == 0 && len > 0)
            {
                value = len == 4 ? Marshal.ReadInt32(buf) : Marshal.ReadInt64(buf);
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    internal static bool TrySysctlString(string name, out string? value)
    {
        value = null;
        if (!OperatingSystem.IsMacOS()) return false;
        IntPtr buf = Marshal.AllocHGlobal(256);
        try
        {
            nuint len = 256;
            if (sysctlbyname(name, buf, ref len, IntPtr.Zero, 0) == 0 && len > 0)
            {
                value = Marshal.PtrToStringUTF8(buf)?.Trim('\0').Trim();
                return !string.IsNullOrEmpty(value);
            }
            return false;
        }
        catch
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    private static void ProbeMacOSSysctl(
        ref string brand,
        ref int logicalCores,
        ref int physicalCores,
        ref int pCores,
        ref int eCores,
        ref int cacheLineSize,
        ref long l1dBytes,
        ref long l2Bytes,
        ref long l3Bytes,
        ref long totalRamBytes,
        ref long usableRamBytes)
    {
        if (TrySysctlString("machdep.cpu.brand_string", out var sysBrand) && !string.IsNullOrEmpty(sysBrand))
            brand = sysBrand;
        else if (TrySysctlString("hw.model", out var hwModel) && !string.IsNullOrEmpty(hwModel))
            brand = hwModel;

        if ((TrySysctlInt64("hw.logicalcpu_max", out long lCpu) || TrySysctlInt64("hw.logicalcpu", out lCpu)) && lCpu > 0)
            logicalCores = (int)lCpu;

        if ((TrySysctlInt64("hw.physicalcpu_max", out long pCpu) || TrySysctlInt64("hw.physicalcpu", out pCpu)) && pCpu > 0)
            physicalCores = (int)pCpu;

        // On Apple Silicon (M1..M4+), perflevel0 is Performance and perflevel1 is Efficiency
        if ((TrySysctlInt64("hw.perflevel0.physicalcpu_max", out long perf0) || TrySysctlInt64("hw.perflevel0.physicalcpu", out perf0)) && perf0 > 0)
            pCores = (int)perf0;
        else
            pCores = physicalCores;

        if ((TrySysctlInt64("hw.perflevel1.physicalcpu_max", out long perf1) || TrySysctlInt64("hw.perflevel1.physicalcpu", out perf1)) && perf1 >= 0)
            eCores = (int)perf1;

        if (TrySysctlInt64("hw.cachelinesize", out long cls) && cls > 0)
            cacheLineSize = (int)cls;

        if (TrySysctlInt64("hw.perflevel0.l1dcachesize", out long l1d) && l1d > 0)
            l1dBytes = l1d;
        else if (TrySysctlInt64("hw.l1dcachesize", out long l1dFallback) && l1dFallback > 0)
            l1dBytes = l1dFallback;

        if (TrySysctlInt64("hw.perflevel0.l2cachesize", out long l2) && l2 > 0)
            l2Bytes = l2;
        else if (TrySysctlInt64("hw.l2cachesize", out long l2Fallback) && l2Fallback > 0)
            l2Bytes = l2Fallback;

        if (TrySysctlInt64("hw.l3cachesize", out long l3) && l3 > 0)
            l3Bytes = l3;

        if (TrySysctlInt64("hw.memsize", out long memSize) && memSize > 0)
            totalRamBytes = memSize;

        if (TrySysctlInt64("hw.memsize_usable", out long memUsable) && memUsable > 0)
            usableRamBytes = memUsable;
        else
            usableRamBytes = totalRamBytes;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Linux Native /proc + /sys/devices/system/cpu Topology & Cache Probe
    // ═══════════════════════════════════════════════════════════════════════════
    private static void ProbeLinuxProcAndSysfs(
        ref string brand,
        ref int logicalCores,
        ref int physicalCores,
        ref int pCores,
        ref int eCores,
        ref int cacheLineSize,
        ref long l1dBytes,
        ref long l2Bytes,
        ref long l3Bytes,
        ref long totalRamBytes,
        ref long usableRamBytes)
    {
        try
        {
            // 1. Memory info from /proc/meminfo
            if (File.Exists("/proc/meminfo"))
            {
                foreach (var line in File.ReadLines("/proc/meminfo"))
                {
                    if (line.StartsWith("MemTotal:", StringComparison.OrdinalIgnoreCase))
                    {
                        long kb = ParseMemInfoKb(line);
                        if (kb > 0) totalRamBytes = kb * 1024L;
                    }
                    else if (line.StartsWith("MemAvailable:", StringComparison.OrdinalIgnoreCase))
                    {
                        long kb = ParseMemInfoKb(line);
                        if (kb > 0) usableRamBytes = kb * 1024L;
                    }
                }
            }

            // 2. CPU brand & fallback core count from /proc/cpuinfo
            int cpuInfoCores = 0;
            if (File.Exists("/proc/cpuinfo"))
            {
                foreach (var line in File.ReadLines("/proc/cpuinfo"))
                {
                    if (brand == "Unknown CPU" &&
                        (line.StartsWith("model name", StringComparison.OrdinalIgnoreCase) ||
                         line.StartsWith("Hardware", StringComparison.OrdinalIgnoreCase) ||
                         line.StartsWith("Processor", StringComparison.OrdinalIgnoreCase)))
                    {
                        int idx = line.IndexOf(':');
                        if (idx >= 0)
                        {
                            string candidate = line[(idx + 1)..].Trim();
                            if (!string.IsNullOrEmpty(candidate))
                                brand = candidate;
                        }
                    }
                    else if (cpuInfoCores == 0 && line.StartsWith("cpu cores", StringComparison.OrdinalIgnoreCase))
                    {
                        int idx = line.IndexOf(':');
                        if (idx >= 0 && int.TryParse(line[(idx + 1)..].Trim(), out int cores) && cores > 0)
                            cpuInfoCores = cores;
                    }
                }
            }

            // 3. Exact physical core & hybrid P/E core topology from /sys/devices/system/cpu
            const string cpuSysRoot = "/sys/devices/system/cpu";
            if (Directory.Exists(cpuSysRoot))
            {
                var uniqueCoreKeys = new System.Collections.Generic.HashSet<string>();
                var coreMaxFreq = new System.Collections.Generic.Dictionary<string, long>();
                int logicalCount = 0;

                foreach (var dir in Directory.GetDirectories(cpuSysRoot, "cpu*"))
                {
                    string dirName = Path.GetFileName(dir);
                    if (dirName.Length <= 3 || !int.TryParse(dirName.AsSpan(3), out _))
                        continue;

                    logicalCount++;
                    string pkgId = ReadSysfsTrimmed(Path.Combine(dir, "topology", "physical_package_id")) ?? "0";
                    string coreId = ReadSysfsTrimmed(Path.Combine(dir, "topology", "core_id")) ?? dirName;
                    string key = $"{pkgId}:{coreId}";
                    uniqueCoreKeys.Add(key);

                    // Check cpu_capacity (ARM big.LITTLE / DynamIQ) or cpuinfo_max_freq (Intel Alder/Raptor/Arrow Lake P/E cores)
                    long metric = 0;
                    string? capStr = ReadSysfsTrimmed(Path.Combine(dir, "cpu_capacity"));
                    if (string.IsNullOrEmpty(capStr) || !long.TryParse(capStr, out metric))
                    {
                        string? freqStr = ReadSysfsTrimmed(Path.Combine(dir, "cpufreq", "cpuinfo_max_freq"));
                        if (!string.IsNullOrEmpty(freqStr))
                            long.TryParse(freqStr, out metric);
                    }

                    if (metric > 0 && (!coreMaxFreq.TryGetValue(key, out long prev) || metric > prev))
                        coreMaxFreq[key] = metric;
                }

                if (logicalCount > 0)
                    logicalCores = logicalCount;

                if (uniqueCoreKeys.Count > 0)
                {
                    physicalCores = uniqueCoreKeys.Count;

                    // Detect hybrid P/E split if cores have distinct max capacity/frequency tiers
                    if (coreMaxFreq.Count == physicalCores && physicalCores > 1)
                    {
                        long maxMetric = 0;
                        long minMetric = long.MaxValue;
                        foreach (var kv in coreMaxFreq)
                        {
                            if (kv.Value > maxMetric) maxMetric = kv.Value;
                            if (kv.Value < minMetric) minMetric = kv.Value;
                        }

                        // If top tier is >15% higher than bottom tier, classify into P and E cores
                        if (maxMetric > 0 && minMetric < (long)(maxMetric * 0.88))
                        {
                            long threshold = (maxMetric + minMetric) / 2;
                            int p = 0, e = 0;
                            foreach (var kv in coreMaxFreq)
                            {
                                if (kv.Value >= threshold) p++;
                                else e++;
                            }
                            if (p > 0)
                            {
                                pCores = p;
                                eCores = e;
                            }
                        }
                        else
                        {
                            pCores = physicalCores;
                            eCores = 0;
                        }
                    }
                    else
                    {
                        pCores = physicalCores;
                    }
                }
                else if (cpuInfoCores > 0)
                {
                    physicalCores = cpuInfoCores;
                    pCores = cpuInfoCores;
                }

                // 4. Cache sizes & coherency line size from /sys/devices/system/cpu/cpu0/cache/index*
                string cacheRoot = Path.Combine(cpuSysRoot, "cpu0", "cache");
                if (Directory.Exists(cacheRoot))
                {
                    foreach (var idxDir in Directory.GetDirectories(cacheRoot, "index*"))
                    {
                        string? clsStr = ReadSysfsTrimmed(Path.Combine(idxDir, "coherency_line_size"));
                        if (!string.IsNullOrEmpty(clsStr) && int.TryParse(clsStr, out int cls) && cls > 0)
                            cacheLineSize = cls;

                        string? levelStr = ReadSysfsTrimmed(Path.Combine(idxDir, "level"));
                        string? typeStr = ReadSysfsTrimmed(Path.Combine(idxDir, "type"));
                        string? sizeStr = ReadSysfsTrimmed(Path.Combine(idxDir, "size"));
                        long sizeBytes = ParseCacheSizeBytes(sizeStr);
                        if (sizeBytes <= 0) continue;

                        if (levelStr == "1" && (typeStr == null || typeStr.Equals("Data", StringComparison.OrdinalIgnoreCase)))
                            l1dBytes = sizeBytes;
                        else if (levelStr == "2")
                            l2Bytes = sizeBytes;
                        else if (levelStr == "3")
                            l3Bytes = sizeBytes;
                    }
                }
            }
            else if (cpuInfoCores > 0)
            {
                physicalCores = cpuInfoCores;
                pCores = cpuInfoCores;
            }
        }
        catch { }
    }

    private static string? ReadSysfsTrimmed(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    private static long ParseMemInfoKb(string line)
    {
        int colon = line.IndexOf(':');
        if (colon < 0) return 0;
        var span = line.AsSpan(colon + 1).Trim();
        int space = span.IndexOf(' ');
        if (space > 0) span = span[..space];
        return long.TryParse(span, out long kb) ? kb : 0;
    }

    internal static long ParseCacheSizeBytes(string? sizeStr)
    {
        if (string.IsNullOrWhiteSpace(sizeStr)) return 0;
        string s = sizeStr.Trim().ToUpperInvariant();
        long multiplier = 1;
        if (s.EndsWith("K") || s.EndsWith("KB"))
        {
            multiplier = 1024L;
            s = s.TrimEnd('B', 'K').Trim();
        }
        else if (s.EndsWith("M") || s.EndsWith("MB"))
        {
            multiplier = 1024L * 1024L;
            s = s.TrimEnd('B', 'M').Trim();
        }
        return long.TryParse(s, out long val) ? val * multiplier : 0;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Windows Native Kernel32 P/Invoke (GetLogicalProcessorInformationEx + GlobalMemoryStatusEx)
    // ═══════════════════════════════════════════════════════════════════════════
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLogicalProcessorInformationEx(
        int relationshipType,
        IntPtr buffer,
        ref uint returnedLength);

    private const int RelationProcessorCore = 0;
    private const int RelationCache = 2;

    private static void ProbeWindowsNative(
        ref string brand,
        ref int logicalCores,
        ref int physicalCores,
        ref int pCores,
        ref int eCores,
        ref int cacheLineSize,
        ref long l1dBytes,
        ref long l2Bytes,
        ref long l3Bytes,
        ref long totalRamBytes,
        ref long usableRamBytes)
    {
        // 1. Read human-readable ProcessorNameString from Windows Registry (fallback to PROCESSOR_IDENTIFIER)
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                var regBrand = key?.GetValue("ProcessorNameString") as string;
                if (!string.IsNullOrWhiteSpace(regBrand))
                    brand = regBrand.Trim();
            }
        }
        catch { }

        if (brand == "Unknown CPU")
        {
            var envBrand = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
            if (!string.IsNullOrWhiteSpace(envBrand))
                brand = envBrand.Trim();
        }

        // 2. Query exact physical & available RAM via GlobalMemoryStatusEx
        try
        {
            var memStatus = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref memStatus) && memStatus.ullTotalPhys > 0)
            {
                totalRamBytes = (long)memStatus.ullTotalPhys;
                usableRamBytes = memStatus.ullAvailPhys > 0 ? (long)memStatus.ullAvailPhys : totalRamBytes;
            }
        }
        catch { }

        // 3. Query physical cores, EfficiencyClass (Intel Hybrid P/E cores), and Cache hierarchy via GetLogicalProcessorInformationEx
        try
        {
            ProbeWindowsProcessorCores(ref physicalCores, ref pCores, ref eCores);
            ProbeWindowsCaches(ref cacheLineSize, ref l1dBytes, ref l2Bytes, ref l3Bytes);
        }
        catch { }
    }

    private static unsafe void ProbeWindowsProcessorCores(ref int physicalCores, ref int pCores, ref int eCores)
    {
        uint len = 0;
        GetLogicalProcessorInformationEx(RelationProcessorCore, IntPtr.Zero, ref len);
        if (len == 0) return;

        IntPtr buf = Marshal.AllocHGlobal((int)len);
        try
        {
            if (!GetLogicalProcessorInformationEx(RelationProcessorCore, buf, ref len))
                return;

            int coreCount = 0;
            int maxEfficiencyClass = 0;
            int minEfficiencyClass = int.MaxValue;
            var efficiencyClasses = new System.Collections.Generic.List<byte>();

            byte* ptr = (byte*)buf;
            byte* end = ptr + len;
            while (ptr < end)
            {
                int rel = *(int*)ptr;
                uint size = *(uint*)(ptr + 4);
                if (size == 0) break;

                if (rel == RelationProcessorCore)
                {
                    // PROCESSOR_RELATIONSHIP starts at offset 8:
                    // byte Flags (offset 8), byte EfficiencyClass (offset 9)
                    byte effClass = *(ptr + 9);
                    efficiencyClasses.Add(effClass);
                    if (effClass > maxEfficiencyClass) maxEfficiencyClass = effClass;
                    if (effClass < minEfficiencyClass) minEfficiencyClass = effClass;
                    coreCount++;
                }

                ptr += size;
            }

            if (coreCount > 0)
            {
                physicalCores = coreCount;
                if (maxEfficiencyClass > minEfficiencyClass)
                {
                    // On Windows, higher EfficiencyClass (>0) indicates Performance cores, 0 indicates Efficiency cores
                    int p = 0, e = 0;
                    foreach (byte ec in efficiencyClasses)
                    {
                        if (ec == maxEfficiencyClass) p++;
                        else e++;
                    }
                    pCores = p > 0 ? p : physicalCores;
                    eCores = e;
                }
                else
                {
                    pCores = physicalCores;
                    eCores = 0;
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    private static unsafe void ProbeWindowsCaches(ref int cacheLineSize, ref long l1dBytes, ref long l2Bytes, ref long l3Bytes)
    {
        uint len = 0;
        GetLogicalProcessorInformationEx(RelationCache, IntPtr.Zero, ref len);
        if (len == 0) return;

        IntPtr buf = Marshal.AllocHGlobal((int)len);
        try
        {
            if (!GetLogicalProcessorInformationEx(RelationCache, buf, ref len))
                return;

            byte* ptr = (byte*)buf;
            byte* end = ptr + len;
            while (ptr < end)
            {
                int rel = *(int*)ptr;
                uint size = *(uint*)(ptr + 4);
                if (size == 0) break;

                if (rel == RelationCache)
                {
                    // CACHE_RELATIONSHIP at offset 8:
                    // byte Level (8), byte Associativity (9), ushort LineSize (10), uint CacheSize (12), int Type (16)
                    byte level = *(ptr + 8);
                    ushort lineSize = *(ushort*)(ptr + 10);
                    uint cacheSize = *(uint*)(ptr + 12);
                    int cacheType = *(int*)(ptr + 16); // 0=Unified, 1=Instruction, 2=Data

                    if (lineSize > 0)
                        cacheLineSize = lineSize;

                    if (level == 1 && (cacheType == 2 || cacheType == 0) && cacheSize > l1dBytes)
                        l1dBytes = cacheSize;
                    else if (level == 2 && cacheSize > l2Bytes)
                        l2Bytes = cacheSize;
                    else if (level == 3 && cacheSize > l3Bytes)
                        l3Bytes = cacheSize;
                }

                ptr += size;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }
}
