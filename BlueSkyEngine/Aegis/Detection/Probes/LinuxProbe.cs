using System;
using System.IO;
using System.Text.RegularExpressions;
using BlueSky.BSR.RHI;

namespace BlueSky.Aegis.Detection.Probes
{
    internal class LinuxProbe : IPlatformGpuProbe
    {
        private static readonly string[] PciIdsPaths =
        {
            "/usr/share/hwdata/pci.ids",
            "/usr/share/misc/pci.ids",
            "/usr/share/pci.ids",
            "/var/lib/pciutils/pci.ids"
        };

        public GpuCapabilities? Probe()
        {
            var cpu = ProcessorCapabilities.Probe();
            int sysMemMB = cpu.TotalSystemMemoryMB > 0
                ? cpu.TotalSystemMemoryMB
                : (int)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024));

            var caps = new GpuCapabilities
            {
                OS = OSPlatform.Linux,
                SystemMemoryMB = sysMemMB,
                SupportsOpenGL33 = true,
                SupportsCompute = true,
                SupportsIndirectDrawing = true,
                SupportsBCTextureCompression = true,
                SupportsFloat32Filtering = true,
                DetectionMethod = "sysfs"
            };

            bool gotAnyData = false;

            // Step 1: Fast native sysfs scan across all /sys/class/drm/card* devices (zero process spawns)
            // Reads vendor ID, device ID, dedicated VRAM, GTT/shared memory, driver version, and resolves pci.ids
            gotAnyData |= ProbeDrmSysfs(ref caps);

            // Step 2: lspci fallback if sysfs + pci.ids didn't yield a model name
            if (string.IsNullOrEmpty(caps.Name) || caps.Name == "Unknown GPU")
            {
                gotAnyData |= ProbeLspci(ref caps);
            }

            // Step 3: NVIDIA-specific (nvidia-smi for exact VRAM and driver version when sysfs mem_info is absent)
            if (IsNvidia(caps.Vendor, caps.Name) && (caps.VramMB <= 0 || string.IsNullOrEmpty(caps.DriverVersion)))
            {
                gotAnyData |= ProbeNvidiaSmi(ref caps);
            }

            // Step 4: glxinfo fallback for GL version and renderer name
            if (string.IsNullOrEmpty(caps.Name) || caps.Name == "Unknown GPU")
            {
                gotAnyData |= ProbeGlxinfo(ref caps);
            }

            // Step 5: Vulkan detection (fast native loader check first, then vulkaninfo)
            ProbeVulkan(ref caps);

            if (!gotAnyData)
                return null;

            caps.IsIntegrated = GpuClassifier.IsLikelyIntegrated(caps.Vendor, caps.Name);

            // If an integrated APU/iGPU on Linux reported 0 dedicated VRAM (or small carveout), use SystemMemoryMB fallback
            if (caps.VramMB <= 0 && caps.IsIntegrated && caps.SystemMemoryMB > 0)
            {
                caps.VramMB = System.Math.Min(caps.SystemMemoryMB / 2, 8192);
                caps.HasUnifiedMemory = true;
            }

            caps.Tier = GpuClassifier.ClassifyTier(caps.Vendor, caps.Name, caps.VramMB);

            // Modern Mesa / NVIDIA driver OpenGL 4.5 + Hardware RayTracing / Mesh Shaders capability detection
            caps.SupportsOpenGL45 = caps.Vendor is "NVIDIA" or "AMD" or "Intel";

            string upperName = (caps.Name ?? "").ToUpperInvariant();
            if (caps.SupportsVulkan &&
                (upperName.Contains("RTX") || upperName.Contains("RX 6") || upperName.Contains("RX 7") ||
                 upperName.Contains("RX 8") || upperName.Contains("RX 9") || upperName.Contains("ARC") ||
                 upperName.Contains("RADEON 780M") || upperName.Contains("RADEON 890M")))
            {
                caps.SupportsRayTracing = true;
                caps.SupportsMeshShaders = true;
                caps.SupportsShaderBarycentrics = true;
            }

            caps.SupportsDX11 = false;
            caps.SupportsDX12 = false;
            caps.SupportsMetal = false;

            return caps;
        }

        internal static bool ProbeDrmSysfs(ref GpuCapabilities caps, string drmRoot = "/sys/class/drm")
        {
            try
            {
                if (!Directory.Exists(drmRoot))
                    return false;

                bool found = false;
                long bestVramBytes = -1;
                long bestGttBytes = -1;
                string bestVendor = "Unknown";
                string bestVendorHex = "";
                string bestDeviceHex = "";
                string bestDriverVersion = "";
                string bestDeviceName = "";

                foreach (var cardDir in Directory.GetDirectories(drmRoot, "card*"))
                {
                    var name = Path.GetFileName(cardDir);
                    // Skip connector subdirectories like card0-DP-1 or card1-HDMI-A-1
                    if (name.Contains('-')) continue;

                    string deviceDir = Path.Combine(cardDir, "device");
                    if (!Directory.Exists(deviceDir)) continue;

                    string vendorHex = ReadFileTrimmed(Path.Combine(deviceDir, "vendor")) ?? "";
                    string deviceHex = ReadFileTrimmed(Path.Combine(deviceDir, "device")) ?? "";
                    string vendor = VendorFromSysfsHex(vendorHex);
                    if (vendor == "Unknown") continue;

                    // Dedicated VRAM (amdgpu /xe / i915 / nouveau expose mem_info_vram_total)
                    long vramBytes = 0;
                    string? vramStr = ReadFileTrimmed(Path.Combine(deviceDir, "mem_info_vram_total"));
                    if (!string.IsNullOrEmpty(vramStr))
                        long.TryParse(vramStr, out vramBytes);

                    // GTT / Dynamic shared aperture (crucial for AMD APUs like Steam Deck / Ryzen 780M / 890M)
                    long gttBytes = 0;
                    string? gttStr = ReadFileTrimmed(Path.Combine(deviceDir, "mem_info_gtt_total"));
                    if (!string.IsNullOrEmpty(gttStr))
                        long.TryParse(gttStr, out gttBytes);

                    // NVIDIA procfs fallback if /proc/driver/nvidia/gpus/*/information exists
                    string candName = "";
                    if (vendor == "NVIDIA")
                    {
                        TryReadNvidiaProcInfo(out candName, out string nvDriverVer);
                        if (!string.IsNullOrEmpty(nvDriverVer) && string.IsNullOrEmpty(bestDriverVersion))
                            bestDriverVersion = nvDriverVer;
                    }

                    // Kernel module / driver version
                    string driverVer = ReadFileTrimmed(Path.Combine(deviceDir, "driver", "module", "version"))
                                    ?? ReadFileTrimmed("/sys/module/amdgpu/version")
                                    ?? ReadFileTrimmed("/sys/module/nvidia/version")
                                    ?? "";

                    bool isDiscrete = (vendor == "NVIDIA" || vendor == "AMD") &&
                                      (vramBytes == 0 || vramBytes >= 2L * 1024 * 1024 * 1024);
                    bool bestIsDiscrete = (bestVendor == "NVIDIA" || bestVendor == "AMD") &&
                                          (bestVramBytes == 0 || bestVramBytes >= 2L * 1024 * 1024 * 1024);

                    long effectiveMem = vramBytes > 0 ? vramBytes : gttBytes;
                    long bestEffectiveMem = bestVramBytes > 0 ? bestVramBytes : bestGttBytes;

                    if (!found || (!bestIsDiscrete && isDiscrete) || (bestIsDiscrete == isDiscrete && effectiveMem > bestEffectiveMem))
                    {
                        bestVendor = vendor;
                        bestVendorHex = vendorHex;
                        bestDeviceHex = deviceHex;
                        bestVramBytes = vramBytes;
                        bestGttBytes = gttBytes;
                        if (!string.IsNullOrEmpty(driverVer))
                            bestDriverVersion = driverVer;
                        if (!string.IsNullOrEmpty(candName))
                            bestDeviceName = candName;
                        found = true;
                    }
                }

                if (found)
                {
                    caps.Vendor = bestVendor;
                    if (!string.IsNullOrEmpty(bestDriverVersion))
                        caps.DriverVersion = bestDriverVersion;

                    // If dedicated VRAM is tiny (e.g. 512MB UMA carveout on an AMD APU with 8GB+ GTT), use GTT as unified working set
                    if (bestVramBytes > 0 && bestVramBytes <= 1024L * 1024 * 1024 && bestGttBytes >= 2L * 1024 * 1024 * 1024)
                    {
                        caps.VramMB = (int)(bestGttBytes / (1024 * 1024));
                        caps.RecommendedMaxWorkingSetMB = caps.VramMB;
                        caps.HasUnifiedMemory = true;
                    }
                    else if (bestVramBytes > 0)
                    {
                        caps.VramMB = (int)(bestVramBytes / (1024 * 1024));
                        caps.RecommendedMaxWorkingSetMB = caps.VramMB;
                    }
                    else if (bestGttBytes > 0)
                    {
                        caps.VramMB = (int)(bestGttBytes / (1024 * 1024));
                        caps.RecommendedMaxWorkingSetMB = caps.VramMB;
                        caps.HasUnifiedMemory = true;
                    }

                    if (!string.IsNullOrEmpty(bestDeviceName))
                    {
                        caps.Name = bestDeviceName;
                        caps.DetectionMethod = "sysfs+proc";
                    }
                    else if (TryLookupPciIdsDatabase(bestVendorHex, bestDeviceHex, out string? pciModel) && !string.IsNullOrEmpty(pciModel))
                    {
                        caps.Name = pciModel;
                        caps.DetectionMethod = "sysfs+pci.ids";
                    }

                    return true;
                }
            }
            catch { }
            return false;
        }

        private static bool TryReadNvidiaProcInfo(out string modelName, out string driverVersion)
        {
            modelName = "";
            driverVersion = "";
            try
            {
                if (File.Exists("/proc/driver/nvidia/version"))
                {
                    string verText = File.ReadAllText("/proc/driver/nvidia/version");
                    var verMatch = Regex.Match(verText, @"Kernel Module\s+([0-9]+\.[0-9]+(?:\.[0-9]+)?)", RegexOptions.IgnoreCase);
                    if (verMatch.Success)
                        driverVersion = verMatch.Groups[1].Value.Trim();
                }

                const string nvGpusDir = "/proc/driver/nvidia/gpus";
                if (Directory.Exists(nvGpusDir))
                {
                    foreach (var gpuDir in Directory.GetDirectories(nvGpusDir))
                    {
                        string infoFile = Path.Combine(gpuDir, "information");
                        if (!File.Exists(infoFile)) continue;
                        string infoText = File.ReadAllText(infoFile);
                        var modelMatch = Regex.Match(infoText, @"Model:\s*(.+)", RegexOptions.IgnoreCase);
                        if (modelMatch.Success)
                        {
                            modelName = modelMatch.Groups[1].Value.Trim();
                            return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Resolves a PCI vendor + device hex pair (e.g. "0x10de", "0x2684") directly from the Linux
        /// system pci.ids database without spawning `lspci`.
        /// </summary>
        internal static bool TryLookupPciIdsDatabase(string vendorHex, string deviceHex, out string? modelName, string? customPciIdsPath = null)
        {
            modelName = null;
            string vId = NormalizeHex4(vendorHex);
            string dId = NormalizeHex4(deviceHex);
            if (vId.Length != 4 || dId.Length != 4)
                return false;

            string? path = customPciIdsPath;
            if (string.IsNullOrEmpty(path))
            {
                foreach (var candidate in PciIdsPaths)
                {
                    if (File.Exists(candidate))
                    {
                        path = candidate;
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return false;

            try
            {
                bool inVendor = false;
                foreach (var line in File.ReadLines(path))
                {
                    if (string.IsNullOrEmpty(line) || line[0] == '#')
                        continue;

                    // Vendor line starts at column 0 with 4 hex chars
                    if (line[0] != '\t')
                    {
                        if (inVendor)
                            break; // Passed target vendor section without finding device

                        if (line.Length >= 4 && line.AsSpan(0, 4).Equals(vId.AsSpan(), StringComparison.OrdinalIgnoreCase))
                            inVendor = true;
                    }
                    else if (inVendor && line.Length > 5 && line[0] == '\t' && line[1] != '\t')
                    {
                        // Device line starts with a single tab followed by 4 hex chars
                        if (line.AsSpan(1, 4).Equals(dId.AsSpan(), StringComparison.OrdinalIgnoreCase))
                        {
                            string raw = line[5..].Trim();
                            var bracketMatch = Regex.Match(raw, @"\[(.+?)\]");
                            modelName = bracketMatch.Success ? bracketMatch.Groups[1].Value.Trim() : raw;
                            return !string.IsNullOrEmpty(modelName);
                        }
                    }
                }
            }
            catch { }

            return false;
        }

        private static string NormalizeHex4(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return "";
            string s = hex.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                s = s[2..];
            return s.ToLowerInvariant();
        }

        private static string? ReadFileTrimmed(string path)
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

        private static string VendorFromSysfsHex(string hex)
        {
            var upper = (hex ?? "").ToUpperInvariant();
            if (upper.Contains("10DE")) return "NVIDIA";
            if (upper.Contains("1002") || upper.Contains("1022")) return "AMD";
            if (upper.Contains("8086")) return "Intel";
            if (upper.Contains("106B")) return "Apple";
            return "Unknown";
        }

        private static bool ProbeLspci(ref GpuCapabilities caps)
        {
            var output = ShellCommandRunner.Run("bash", "-c \"lspci | grep -iE 'vga|3d controller|display controller'\"");
            if (string.IsNullOrWhiteSpace(output))
                return false;

            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            string bestLine = lines[0];
            foreach (var line in lines)
            {
                string v = DeriveVendor(line);
                if (v == "NVIDIA" || v == "AMD")
                {
                    bestLine = line;
                    break;
                }
            }

            caps.Vendor = DeriveVendor(bestLine);
            var match = Regex.Match(bestLine, @":\s*(.+?)(?:\s*\(rev|$)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var fullName = match.Groups[1].Value.Trim();
                var bracketMatch = Regex.Match(fullName, @"\[(.+?)\]");
                caps.Name = bracketMatch.Success ? bracketMatch.Groups[1].Value.Trim() : fullName;
            }
            else if (string.IsNullOrEmpty(caps.Name))
            {
                caps.Name = "Unknown GPU";
            }

            caps.DetectionMethod = caps.VramMB > 0 ? "sysfs+lspci" : "shell:lspci";
            return true;
        }

        private static bool ProbeNvidiaSmi(ref GpuCapabilities caps)
        {
            var output = ShellCommandRunner.Run("nvidia-smi",
                "--query-gpu=name,memory.total,driver_version --format=csv,noheader,nounits");
            if (string.IsNullOrWhiteSpace(output))
                return false;

            caps.DetectionMethod = "shell:nvidia-smi";

            var firstLine = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0];
            var parts = firstLine.Trim().Split(',');
            if (parts.Length >= 1)
                caps.Name = parts[0].Trim();
            if (parts.Length >= 2 && int.TryParse(parts[1].Trim(), out int vram))
                caps.VramMB = vram;
            if (parts.Length >= 3)
                caps.DriverVersion = parts[2].Trim();

            caps.Vendor = "NVIDIA";
            caps.SupportsOpenGL45 = true;
            return true;
        }

        private static bool ProbeGlxinfo(ref GpuCapabilities caps)
        {
            var output = ShellCommandRunner.Run("bash", "-c \"glxinfo 2>/dev/null | head -30\"");
            if (string.IsNullOrWhiteSpace(output))
                return false;

            var rendererMatch = Regex.Match(output, @"OpenGL renderer string:\s*(.+)", RegexOptions.IgnoreCase);
            if (rendererMatch.Success)
            {
                caps.Name = rendererMatch.Groups[1].Value.Trim();
                caps.Vendor = DeriveVendor(caps.Name);
                caps.DetectionMethod = "shell:glxinfo";
            }

            var versionMatch = Regex.Match(output, @"OpenGL version string:\s*(\d+)\.(\d+)", RegexOptions.IgnoreCase);
            if (versionMatch.Success)
            {
                int major = int.Parse(versionMatch.Groups[1].Value);
                int minor = int.Parse(versionMatch.Groups[2].Value);
                caps.SupportsOpenGL33 = major > 3 || (major == 3 && minor >= 3);
                caps.SupportsOpenGL45 = major > 4 || (major == 4 && minor >= 5);
            }

            return rendererMatch.Success;
        }

        private static void ProbeVulkan(ref GpuCapabilities caps)
        {
            if (RHIDiscovery.IsVulkanSupported())
            {
                caps.SupportsVulkan = true;
                return;
            }

            var output = ShellCommandRunner.Run("bash", "-c \"vulkaninfo --summary 2>/dev/null | head -20\"");
            caps.SupportsVulkan = !string.IsNullOrWhiteSpace(output) &&
                                  output.Contains("deviceName", StringComparison.OrdinalIgnoreCase);
        }

        private static string DeriveVendor(string text)
        {
            var upper = (text ?? "").ToUpperInvariant();
            if (upper.Contains("NVIDIA")) return "NVIDIA";
            if (upper.Contains("AMD") || upper.Contains("RADEON") || upper.Contains("ADVANCED MICRO")) return "AMD";
            if (upper.Contains("INTEL")) return "Intel";
            if (upper.Contains("APPLE")) return "Apple";
            return "Unknown";
        }

        private static bool IsNvidia(string? vendor, string? name)
        {
            return (vendor ?? "").Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                   (name ?? "").Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                   (name ?? "").Contains("GEFORCE", StringComparison.OrdinalIgnoreCase) ||
                   (name ?? "").Contains("RTX", StringComparison.OrdinalIgnoreCase);
        }
    }
}
