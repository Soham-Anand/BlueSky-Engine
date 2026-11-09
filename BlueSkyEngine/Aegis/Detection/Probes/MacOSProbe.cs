using System;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using BlueSky.BSR.RHI;

namespace BlueSky.Aegis.Detection.Probes
{
    internal class MacOSProbe : IPlatformGpuProbe
    {
        /// <summary>
        /// Cached Metal capabilities from the most recent probe so IntelligentQueryLayer
        /// does not need to probe MTLDevice a second time.
        /// </summary>
        internal static MetalCapabilities? LastMetalCapabilities { get; private set; }

        public GpuCapabilities? Probe()
        {
            long sysMemBytes = 0;
            ProcessorCapabilities.TrySysctlInt64("hw.memsize", out sysMemBytes);
            int sysMemMB = sysMemBytes > 0
                ? (int)(sysMemBytes / (1024 * 1024))
                : (int)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024));

            // 1. Primary fast path: Native Objective-C MTLDevice + IOKit + sysctl (<1ms, zero shell spawns)
            var metalCaps = MacMetalProbe.Probe();
            LastMetalCapabilities = metalCaps;

            if (metalCaps.HasValue)
            {
                var m = metalCaps.Value;
                int vramMB = m.RecommendedMaxWorkingSetMB;
                if (vramMB <= 0 && m.HasUnifiedMemory && sysMemMB > 0)
                    vramMB = (int)(sysMemMB * 0.75f);

                var caps = new GpuCapabilities
                {
                    OS = OSPlatform.MacOS,
                    Name = m.DeviceName,
                    Vendor = DeriveVendor(m.DeviceName),
                    Architecture = m.Architecture ?? string.Empty,
                    DriverVersion = m.DriverVersion ?? string.Empty,
                    VramMB = vramMB,
                    SystemMemoryMB = sysMemMB,
                    RecommendedMaxWorkingSetMB = m.RecommendedMaxWorkingSetMB > 0 ? m.RecommendedMaxWorkingSetMB : vramMB,
                    GpuCoreCount = m.GpuCoreCount,
                    HasUnifiedMemory = m.HasUnifiedMemory,
                    IsIntegrated = GpuClassifier.IsLikelyIntegrated(DeriveVendor(m.DeviceName), m.DeviceName),

                    SupportsMetal = m.SupportsMetal,
                    SupportsMetal3 = m.SupportsMetal3,
                    SupportsMetal4 = m.SupportsMetal4,
                    SupportsOpenGL33 = true,
                    SupportsOpenGL45 = false, // macOS caps OpenGL at 4.1
                    SupportsVulkan = RHIDiscovery.IsVulkanSupported(), // True if MoltenVK is present
                    SupportsDX11 = false,
                    SupportsDX12 = false,

                    SupportsCompute = m.SupportsCompute,
                    SupportsIndirectDrawing = m.SupportsIndirectDrawing,
                    SupportsRayTracing = m.SupportsRaytracing,
                    SupportsMeshShaders = m.SupportsMeshShaders,
                    SupportsBCTextureCompression = m.SupportsBCTextureCompression,
                    SupportsFloat32Filtering = m.Supports32BitFloatFiltering,
                    SupportsShaderBarycentrics = m.SupportsShaderBarycentricCoordinates,
                    ArgumentBuffersTier = m.ArgumentBuffersTier,

                    DetectionMethod = m.RegistryId != 0
                        ? (m.GpuCoreCount > 0 ? "native:metal+iokit" : "native:metal")
                        : "native:iokit+sysctl"
                };

                // Only invoke system_profiler if VRAM is still unknown on an older Intel/AMD Mac
                if (caps.VramMB <= 0)
                {
                    var spOutput = ShellCommandRunner.Run("system_profiler", "SPDisplaysDataType", timeoutMs: 2000);
                    if (!string.IsNullOrWhiteSpace(spOutput))
                    {
                        ParseSystemProfilerOutput(spOutput, ref caps);
                        caps.DetectionMethod += "+system_profiler";
                    }
                }

                caps.Tier = GpuClassifier.ClassifyTier(caps.Vendor, caps.Name, caps.VramMB);
                return caps;
            }

            // 2. Fallback path: system_profiler SPDisplaysDataType
            var output = ShellCommandRunner.Run("system_profiler", "SPDisplaysDataType", timeoutMs: 2500);
            if (string.IsNullOrWhiteSpace(output))
                return null;

            var fallbackCaps = new GpuCapabilities
            {
                OS = OSPlatform.MacOS,
                SystemMemoryMB = sysMemMB,
                SupportsOpenGL33 = true,
                SupportsOpenGL45 = false,
                SupportsVulkan = RHIDiscovery.IsVulkanSupported(),
                SupportsDX11 = false,
                SupportsDX12 = false,
                DetectionMethod = "shell:system_profiler"
            };

            if (!ParseSystemProfilerOutput(output, ref fallbackCaps))
                return null;

            return fallbackCaps;
        }

        /// <summary>
        /// Parses `system_profiler SPDisplaysDataType` output across macOS Catalina through Sequoia/Tahoe.
        /// Handles Apple Silicon ("Metal: Supported", "Total Number of Cores: N", Unified Memory)
        /// as well as Intel/AMD Macs ("VRAM (Total): X MB/GB", "Metal Family: ...").
        /// </summary>
        internal static bool ParseSystemProfilerOutput(string output, ref GpuCapabilities caps)
        {
            if (string.IsNullOrWhiteSpace(output))
                return false;

            // Parse all Chipset Models and pick the best one (prefer Apple Silicon or discrete AMD over Intel iGPU)
            var modelMatches = Regex.Matches(output, @"Chipset Model:\s*(.+)", RegexOptions.IgnoreCase);
            if (modelMatches.Count > 0 && (string.IsNullOrEmpty(caps.Name) || caps.Name == "Unknown GPU"))
            {
                string bestModel = modelMatches[0].Groups[1].Value.Trim();
                foreach (Match m in modelMatches)
                {
                    string candidate = m.Groups[1].Value.Trim();
                    string candVendor = DeriveVendor(candidate);
                    if (candVendor == "Apple" || candVendor == "AMD")
                    {
                        bestModel = candidate;
                        break;
                    }
                }
                caps.Name = bestModel;
            }
            else if (string.IsNullOrEmpty(caps.Name))
            {
                caps.Name = "Unknown GPU";
            }

            if (string.IsNullOrEmpty(caps.Vendor) || caps.Vendor == "Unknown")
            {
                caps.Vendor = DeriveVendor(caps.Name);
                if (caps.Vendor == "Unknown")
                {
                    var vendorField = ParseField(output, @"Vendor:\s*([^\r\n(]+)");
                    if (!string.IsNullOrEmpty(vendorField))
                        caps.Vendor = DeriveVendor(vendorField);
                }
            }

            // Parse GPU Core Count ("Total Number of Cores: 8")
            if (caps.GpuCoreCount <= 0)
            {
                var coresMatch = Regex.Match(output, @"Total Number of Cores:\s*(\d+)", RegexOptions.IgnoreCase);
                if (coresMatch.Success && int.TryParse(coresMatch.Groups[1].Value, out int cores))
                    caps.GpuCoreCount = cores;
            }

            // Parse explicit VRAM ("VRAM (Total): 8 GB" or "VRAM (Dynamic, Max): 1536 MB")
            if (caps.VramMB <= 0)
            {
                var vramMatches = Regex.Matches(output, @"VRAM\s*(?:\([^)]*\))?:\s*(\d+)\s*(MB|GB)", RegexOptions.IgnoreCase);
                int bestVramMB = 0;
                foreach (Match vm in vramMatches)
                {
                    if (int.TryParse(vm.Groups[1].Value, out int vram))
                    {
                        if (vm.Groups[2].Value.Equals("GB", StringComparison.OrdinalIgnoreCase))
                            vram *= 1024;
                        if (vram > bestVramMB)
                            bestVramMB = vram;
                    }
                }
                if (bestVramMB > 0)
                    caps.VramMB = bestVramMB;
            }

            // Apple Silicon Unified Memory fallback when system_profiler omits VRAM line
            if (caps.Vendor == "Apple" || caps.Name.Contains("Apple M", StringComparison.OrdinalIgnoreCase))
            {
                caps.HasUnifiedMemory = true;
                if (caps.VramMB <= 0)
                {
                    int sysMem = caps.SystemMemoryMB > 0 ? caps.SystemMemoryMB : 8192;
                    caps.VramMB = (int)(sysMem * 0.75f);
                }
                if (caps.RecommendedMaxWorkingSetMB <= 0)
                    caps.RecommendedMaxWorkingSetMB = caps.VramMB;
            }

            // Metal support — matches:
            //   "Metal: Supported" (macOS Sequoia / Apple M4)
            //   "Metal Support: Metal 3" (macOS Ventura / Sonoma)
            //   "Metal Family: Supported, Metal GPUFamily macOS 2" (macOS Catalina / Big Sur / Monterey)
            var metalLine = ParseField(output, @"Metal(?:\s+(?:Family|Support))?:\s*(.+)");
            if (!string.IsNullOrEmpty(metalLine))
            {
                bool isUnsupported = metalLine.Contains("Not Supported", StringComparison.OrdinalIgnoreCase) ||
                                     metalLine.Equals("None", StringComparison.OrdinalIgnoreCase);
                if (!isUnsupported)
                {
                    caps.SupportsMetal = true;
                    caps.SupportsCompute = true;
                    caps.SupportsIndirectDrawing = true;
                    if (metalLine.Contains("Metal 3", StringComparison.OrdinalIgnoreCase) || caps.Vendor == "Apple")
                    {
                        caps.SupportsMetal3 = true;
                        caps.SupportsMeshShaders = true;
                    }
                    if (metalLine.Contains("Metal 4", StringComparison.OrdinalIgnoreCase))
                        caps.SupportsMetal4 = true;
                    if (caps.Name.Contains("M3", StringComparison.OrdinalIgnoreCase) ||
                        caps.Name.Contains("M4", StringComparison.OrdinalIgnoreCase) ||
                        caps.Name.Contains("M5", StringComparison.OrdinalIgnoreCase))
                    {
                        caps.SupportsRayTracing = true;
                    }
                }
            }

            caps.IsIntegrated = GpuClassifier.IsLikelyIntegrated(caps.Vendor, caps.Name);
            caps.Tier = GpuClassifier.ClassifyTier(caps.Vendor, caps.Name, caps.VramMB);
            return true;
        }

        internal static string DeriveVendor(string gpuName)
        {
            var upper = (gpuName ?? "").ToUpperInvariant();
            if (upper.Contains("APPLE")) return "Apple";
            if (upper.Contains("AMD") || upper.Contains("RADEON")) return "AMD";
            if (upper.Contains("NVIDIA") || upper.Contains("GEFORCE") || upper.Contains("QUADRO")) return "NVIDIA";
            if (upper.Contains("INTEL")) return "Intel";
            return "Unknown";
        }

        private static string? ParseField(string text, string pattern)
        {
            try
            {
                var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
                return match.Success ? match.Groups[1].Value.Trim() : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
