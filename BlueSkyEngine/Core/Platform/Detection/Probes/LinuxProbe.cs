using System;
using System.IO;
using System.Text.RegularExpressions;

namespace BlueSky.Core.Platform.Detection.Probes
{
    internal class LinuxProbe : IPlatformGpuProbe
    {
        public GpuCapabilities? Probe()
        {
            var caps = new GpuCapabilities
            {
                OS = OSPlatform.Linux,
                SupportsOpenGL33 = true, // Assume baseline until proven otherwise
                DetectionMethod = "shell:lspci"
            };

            bool gotAnyData = false;

            // Step 1: lspci for basic GPU identification
            gotAnyData |= ProbeLspci(ref caps);

            // Step 2: NVIDIA-specific (nvidia-smi)
            if (IsNvidia(caps.Vendor, caps.Name))
            {
                gotAnyData |= ProbeNvidiaSmi(ref caps);
            }

            // Step 3: AMD-specific (sysfs VRAM)
            if (IsAmd(caps.Vendor, caps.Name))
            {
                gotAnyData |= ProbeAmdSysfs(ref caps);
            }

            // Step 4: glxinfo fallback for GL version and renderer
            if (string.IsNullOrEmpty(caps.Name) || caps.Name == "Unknown GPU")
            {
                gotAnyData |= ProbeGlxinfo(ref caps);
            }

            // Step 5: Vulkan detection
            ProbeVulkan(ref caps);

            if (!gotAnyData)
                return null;

            // Classify
            caps.IsIntegrated = GpuClassifier.IsLikelyIntegrated(caps.Vendor, caps.Name);
            caps.Tier = GpuClassifier.ClassifyTier(caps.Vendor, caps.Name, caps.VramMB);

            // Linux doesn't support DX or Metal
            caps.SupportsDX11 = false;
            caps.SupportsDX12 = false;
            caps.SupportsMetal = false;

            return caps;
        }

        private static bool ProbeLspci(ref GpuCapabilities caps)
        {
            var output = ShellCommandRunner.Run("bash", "-c \"lspci | grep -i vga\"");
            if (string.IsNullOrWhiteSpace(output))
                return false;

            // Typical output: "01:00.0 VGA compatible controller: NVIDIA Corporation GP107 [GeForce GTX 1050 Ti] (rev a1)"
            caps.Vendor = DeriveVendor(output);
            
            // Extract the part after the colon for the GPU name
            var match = Regex.Match(output, @":\s*(.+?)(?:\s*\(rev|$)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var fullName = match.Groups[1].Value.Trim();
                // Try to get the bracketed name (more readable)
                var bracketMatch = Regex.Match(fullName, @"\[(.+?)\]");
                caps.Name = bracketMatch.Success ? bracketMatch.Groups[1].Value.Trim() : fullName;
            }
            else
            {
                caps.Name = "Unknown GPU";
            }

            return true;
        }

        private static bool ProbeNvidiaSmi(ref GpuCapabilities caps)
        {
            var output = ShellCommandRunner.Run("nvidia-smi",
                "--query-gpu=name,memory.total,driver_version --format=csv,noheader,nounits");
            if (string.IsNullOrWhiteSpace(output))
                return false;

            caps.DetectionMethod = "shell:nvidia-smi";

            // Output: "NVIDIA GeForce GTX 1050 Ti, 4096, 535.129.03"
            var parts = output.Trim().Split(',');
            if (parts.Length >= 1)
                caps.Name = parts[0].Trim();
            if (parts.Length >= 2 && int.TryParse(parts[1].Trim(), out int vram))
                caps.VramMB = vram;
            if (parts.Length >= 3)
                caps.DriverVersion = parts[2].Trim();

            caps.Vendor = "NVIDIA";
            return true;
        }

        private static bool ProbeAmdSysfs(ref GpuCapabilities caps)
        {
            const string vramPath = "/sys/class/drm/card0/device/mem_info_vram_total";
            try
            {
                if (!File.Exists(vramPath))
                    return false;

                var content = File.ReadAllText(vramPath).Trim();
                if (long.TryParse(content, out long vramBytes))
                {
                    caps.VramMB = (int)(vramBytes / (1024 * 1024));
                    caps.DetectionMethod = "shell:lspci+sysfs";
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static bool ProbeGlxinfo(ref GpuCapabilities caps)
        {
            var output = ShellCommandRunner.Run("bash", "-c \"glxinfo 2>/dev/null | head -30\"");
            if (string.IsNullOrWhiteSpace(output))
                return false;

            // Parse "OpenGL renderer string: ..."
            var rendererMatch = Regex.Match(output, @"OpenGL renderer string:\s*(.+)", RegexOptions.IgnoreCase);
            if (rendererMatch.Success)
            {
                caps.Name = rendererMatch.Groups[1].Value.Trim();
                caps.Vendor = DeriveVendor(caps.Name);
                caps.DetectionMethod = "shell:glxinfo";
            }

            // Parse "OpenGL version string: X.Y ..."
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
            var output = ShellCommandRunner.Run("bash", "-c \"vulkaninfo --summary 2>/dev/null | head -20\"");
            caps.SupportsVulkan = !string.IsNullOrWhiteSpace(output) &&
                                  output.Contains("deviceName", StringComparison.OrdinalIgnoreCase);
        }

        private static string DeriveVendor(string text)
        {
            var upper = text.ToUpperInvariant();
            if (upper.Contains("NVIDIA")) return "NVIDIA";
            if (upper.Contains("AMD") || upper.Contains("RADEON") || upper.Contains("ADVANCED MICRO")) return "AMD";
            if (upper.Contains("INTEL")) return "Intel";
            return "Unknown";
        }

        private static bool IsNvidia(string? vendor, string? name)
        {
            return (vendor ?? "").Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                   (name ?? "").Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                   (name ?? "").Contains("GEFORCE", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAmd(string? vendor, string? name)
        {
            return (vendor ?? "").Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
                   (name ?? "").Contains("RADEON", StringComparison.OrdinalIgnoreCase);
        }
    }
}
